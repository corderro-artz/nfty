using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Nfty.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>The Cook dialog's <em>Include 3D models</em> box: what it writes, what it refuses, and
/// that it never becomes a button that looks usable and does nothing.</summary>
public class CookDialogModelsTests
{
    private sealed class FixedFolder : IFilePickerService
    {
        private readonly string _dir;
        public FixedFolder(string dir) => _dir = dir;
        public Task<string?> OpenFileAsync(string t, params string[] e) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string t, string e) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string t) => Task.FromResult<string?>(_dir);
    }

    private sealed class NoReveal : IFolderRevealer { public void Reveal(string path) { } }

    /// <summary>A one-recipe book whose single layer carries <paramref name="variants"/> variants of
    /// <paramref name="alpha"/> alpha: 255 is a book a model can be made of, anything else is 2D only.</summary>
    internal static LoadedCookBook Book(int variants = 3, byte alpha = 255)
    {
        var ids = Enumerable.Range(0, variants).Select(i => $"v{i}").ToArray();
        var images = new Dictionary<string, Image<Rgba32>>();
        foreach (var id in ids)
        {
            var img = new Image<Rgba32>(4, 4);
            for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++) img[x, y] = new Rgba32(200, 40, 40, alpha);
            images[id] = img;
        }
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("body", "Body", LayerKind.Custom, null,
                ids.Select(i => new Variant(i, i.ToUpperInvariant(), 1)).ToArray()),
            VariantImages = images,
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Blocks", new Dimensions(4, 4),
                new Collection("Blocks", "", "BL"), new Dictionary<string, double> { ["cat"] = 1 }),
            Recipes = new[]
            {
                new LoadedRecipe
                {
                    Manifest = new RecipeManifest("cat", "Cat", new[] { "body" }, Array.Empty<IncompatibilityRule>()),
                    Ingredients = new[] { ing },
                },
            },
        };
    }

    private static CookDialogViewModel Cook(LoadedCookBook book, string dir, FakeDialogs? dialogs = null) =>
        new(book, new FixedFolder(dir), new NoReveal(), dialogs ?? new FakeDialogs());

    private static string[] ModelFiles(string dir) =>
        Directory.Exists(Path.Combine(dir, "models"))
            ? Directory.GetFiles(Path.Combine(dir, "models")).Select(p => Path.GetFileName(p)!).OrderBy(n => n, StringComparer.Ordinal).ToArray()
            : Array.Empty<string>();

    [AvaloniaFact]
    public async Task Ticking_include_models_cooks_a_model_per_asset()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            using var book = Book();
            var vm = Cook(book, dir);
            vm.Count = 3; vm.Seed = "m"; vm.Unlimited = true; vm.IncludeModels = true;
            await vm.CookCommand.ExecuteAsync(null);

            Assert.True(vm.IsDone);
            Assert.Equal(new[] { "0001.glb", "0002.glb", "0003.glb" }, ModelFiles(dir));
            Assert.Contains("3 models", vm.ResultText);
            Assert.DoesNotContain("skipped", vm.ResultText);
        }
        finally { Directory.Delete(dir, true); }
    }

    [AvaloniaFact]
    public async Task The_options_section_starts_collapsed_and_its_choices_reach_the_files()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            using var book = Book();
            var vm = Cook(book, dir);
            Assert.False(vm.ShowModelOptions);
            Assert.False(vm.ShowModelOptionsSection);
            Assert.Equal(ModelFormat.Glb, vm.ModelFormat);
            Assert.Equal(Pivot.BottomCenter, vm.ModelPivot);
            Assert.Equal(1, vm.VoxelSize);

            vm.IncludeModels = true; vm.ShowModelOptions = true;
            Assert.True(vm.ShowModelOptionsSection);
            vm.ModelFormat = ModelFormat.GltfEmbedded; vm.ModelPivot = Pivot.Center; vm.VoxelSize = 0.5;
            vm.Count = 1; vm.Seed = "m"; vm.Unlimited = true;
            await vm.CookCommand.ExecuteAsync(null);

            Assert.Equal(new[] { "0001.gltf" }, ModelFiles(dir));
            var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "set.json")));
            var models = json.RootElement.GetProperty("models");
            Assert.Equal("gltf-embedded", models.GetProperty("format").GetString());
            Assert.Equal("center", models.GetProperty("pivot").GetString());
            Assert.Equal(0.5, models.GetProperty("voxelSize").GetDouble());
        }
        finally { Directory.Delete(dir, true); }
    }

    private static (Window window, Nfty.App.Views.CookDialogView view) Show(CookDialogViewModel vm)
    {
        var view = new Nfty.App.Views.CookDialogView { DataContext = vm };
        var window = new Window { Content = view, Width = 900, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view);
    }

    [AvaloniaFact]
    public async Task A_two_d_only_book_disables_the_box_and_says_why()
    {
        using var book = Book(variants: 3, alpha: 128);
        var vm = Cook(book, "unused");
        await vm.ScanTask;
        Assert.True(vm.BookIsTwoDOnly);
        Assert.Equal("This book is 2D only — 3 variants have partly transparent pixels. "
            + "Open a layer in Rise and use Make voxel-ready.", vm.ModelsNote);

        var (window, view) = Show(vm);
        try
        {
            var box = view.GetVisualDescendants().OfType<CheckBox>().Single(c => (string?)c.Content == "Include 3D models");
            Assert.False(box.IsEnabled);
            // The reason is NOT inside the dimmed control: it must stay legible.
            var note = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == vm.ModelsNote);
            Assert.True(note.IsEffectivelyEnabled);
            Assert.True(note.IsEffectivelyVisible);
        }
        finally { window.Close(); }

        using var oneBad = Book(variants: 1, alpha: 128);
        var one = Cook(oneBad, "unused");
        await one.ScanTask;
        Assert.Contains("1 variant has partly", one.ModelsNote);
        using var clean = Book();
        var cleanVm = Cook(clean, "unused");
        await cleanVm.ScanTask;
        Assert.False(cleanVm.BookIsTwoDOnly);
    }

    [AvaloniaFact]
    public async Task Executing_cook_on_a_two_d_only_book_with_the_box_forced_writes_no_models()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            using var book = Book(variants: 2, alpha: 128);
            var vm = Cook(book, dir);
            vm.Count = 2; vm.Seed = "m"; vm.Unlimited = true;
            vm.IncludeModels = true;       // behind the gate: a disabled box is a label
            await vm.CookCommand.ExecuteAsync(null);

            Assert.True(vm.IsDone);
            Assert.Empty(ModelFiles(dir));
            Assert.False(Directory.Exists(Path.Combine(dir, "models")));
            Assert.DoesNotContain("models", vm.ResultText);
        }
        finally { Directory.Delete(dir, true); }
    }

    [AvaloniaFact]
    public async Task Extending_a_modeled_set_keeps_making_models()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            using var book = Book();
            var first = Cook(book, dir);
            first.Count = 1; first.Seed = "a"; first.Unlimited = true; first.IncludeModels = true;
            first.ModelFormat = ModelFormat.GltfEmbedded;
            await first.CookCommand.ExecuteAsync(null);
            var before = File.ReadAllBytes(Path.Combine(dir, "models", "0001.gltf"));

            var second = Cook(book, dir);
            second.Count = 2; second.Seed = "b"; second.Unlimited = true;     // box NOT ticked
            Assert.False(second.IncludeModels);
            await second.CookCommand.ExecuteAsync(null);

            Assert.True(second.IsExtending);
            Assert.Equal(new[] { "0001.gltf", "0002.gltf", "0003.gltf" }, ModelFiles(dir));
            Assert.Equal(before, File.ReadAllBytes(Path.Combine(dir, "models", "0001.gltf")));
            Assert.Contains(" · 2 models", second.ResultText);
        }
        finally { Directory.Delete(dir, true); }
    }

    [AvaloniaFact]
    public async Task Ticking_the_box_over_an_unmodeled_set_surfaces_the_refusal()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            using var book = Book();
            var plain = Cook(book, dir);
            plain.Count = 1; plain.Seed = "a"; plain.Unlimited = true;
            await plain.CookCommand.ExecuteAsync(null);

            var dialogs = new FakeDialogs();
            var more = Cook(book, dir, dialogs);
            more.Count = 1; more.Seed = "b"; more.Unlimited = true; more.IncludeModels = true;
            await more.CookCommand.ExecuteAsync(null);

            Assert.IsType<ErrorDialogViewModel>(dialogs.Active);
            Assert.False(more.IsDone);
            Assert.Empty(ModelFiles(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [AvaloniaFact]
    public async Task Ticking_the_box_moves_nothing_above_the_reveal()
    {
        using var book = Book();
        var vm = Cook(book, "unused");
        await vm.ScanTask;
        var (window, view) = Show(vm);
        try
        {
            double Y(string text) => view.GetVisualDescendants().OfType<Control>()
                .First(c => c is CheckBox { Content: string s } && s == text || c is TextBlock t && t.Text == text)
                .TranslatePoint(new Avalonia.Point(0, 0), view)!.Value.Y;
            var texts = new[] { "Pack into a single .set", "Allow repeats", "Include 3D models", vm.ModelsNote };
            var before = texts.Select(Y).ToArray();
            vm.IncludeModels = true; vm.ShowModelOptions = true;
            Dispatcher.UIThread.RunJobs();
            var after = texts.Select(Y).ToArray();
            // The card is centered, so everything may shift together; the SPACING between the rows
            // above the reveal must not change.
            for (int i = 1; i < before.Length; i++)
                Assert.Equal(before[i] - before[0], after[i] - after[0], 0.5);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task The_options_button_shows_whether_it_is_open_without_changing_width()
    {
        using var book = Book();
        var vm = Cook(book, "unused");
        await vm.ScanTask;
        vm.IncludeModels = true;
        var (window, view) = Show(vm);
        try
        {
            var button = view.GetVisualDescendants().OfType<Button>().Single(b => b.Command == vm.ToggleModelOptionsCommand);
            var chevrons = button.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().ToArray();
            Assert.Equal(2, chevrons.Length);
            Assert.DoesNotContain("on", button.Classes);
            Assert.True(chevrons[0].IsVisible); Assert.False(chevrons[1].IsVisible);   // down while closed
            double closedWidth = button.Bounds.Width;

            vm.ToggleModelOptionsCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("on", button.Classes);
            Assert.False(chevrons[0].IsVisible); Assert.True(chevrons[1].IsVisible);   // up while open
            Assert.Equal(closedWidth, button.Bounds.Width, 0.5);
        }
        finally { window.Close(); }
    }
}
