using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Nfty.Core.Stats;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>The last three CLI-only capabilities, reached from the app: stats, inspect and preview.
///
/// None of them is a re-implementation — all three render through Core (<see cref="CollectionReport"/>,
/// <see cref="IdentityReport"/>, <see cref="Nfty.Core.Imaging.VariantPreview"/>), which is what these
/// tests actually pin. A GUI that produced a *similar* report or a *similar* render would be worse
/// than not having one, because an author comparing the two would have no way to tell which was
/// right.</summary>
public class ReportAndPreviewTests
{
    private sealed class SavePicker : IFilePickerService
    {
        private readonly string? _save;
        public int SaveCalls { get; private set; }
        public SavePicker(string? save) => _save = save;
        public Task<string?> OpenFileAsync(string t, params string[] e) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string t, string e) { SaveCalls++; return Task.FromResult(_save); }
        public Task<string?> PickFolderAsync(string t) => Task.FromResult<string?>(null);
    }

    // ---- stats + inspect -------------------------------------------------------------------------

    [AvaloniaFact]
    public void The_report_dialog_shows_the_same_text_the_cli_prints()
    {
        using var book = ExplorerViewModelTests.TwoRecipeBook();
        var vm = new ReportDialogViewModel(book, new FakeDialogs(), new NoopClipboardService());

        // Byte-identical, not merely similar. This is the whole reason the rendering lives in Core.
        Assert.Equal(CollectionReport.Render(book), vm.Text);

        vm.ShowIdentityCommand.Execute(null);
        Assert.Equal(IdentityReport.Render(book), vm.Text);
    }

    [AvaloniaFact]
    public void Switching_report_changes_the_text_and_the_title()
    {
        using var book = ExplorerViewModelTests.TwoRecipeBook();
        var vm = new ReportDialogViewModel(book, new FakeDialogs(), new NoopClipboardService());

        var stats = vm.Text;
        var statsTitle = vm.Title;
        vm.ShowIdentityCommand.Execute(null);

        Assert.True(vm.ShowingIdentity);
        Assert.NotEqual(stats, vm.Text);
        Assert.NotEqual(statsTitle, vm.Title);

        vm.ShowStatsCommand.Execute(null);
        Assert.Equal(stats, vm.Text);
    }

    /// <summary>The identity report exists to surface ids, which the GUI shows nowhere else — that is
    /// the entire reason inspect is worth wiring at all.</summary>
    [AvaloniaFact]
    public void The_identity_report_reaches_ids_the_rest_of_the_gui_never_shows()
    {
        using var book = ExplorerViewModelTests.TwoRecipeBook();
        var vm = new ReportDialogViewModel(book, new FakeDialogs(), new NoopClipboardService());
        vm.ShowIdentityCommand.Execute(null);

        foreach (var recipe in book.Recipes)
            Assert.Contains($"[{recipe.Manifest.Id}]", vm.Text);
    }

    [AvaloniaFact]
    public async Task Copy_puts_the_report_on_the_clipboard_and_says_so()
    {
        using var book = ExplorerViewModelTests.TwoRecipeBook();
        var clipboard = new NoopClipboardService();
        var vm = new ReportDialogViewModel(book, new FakeDialogs(), clipboard);

        Assert.Equal("Copy", vm.CopyLabel);
        await vm.CopyCommand.ExecuteAsync(null);

        Assert.Equal(vm.Text, clipboard.Last);
        // A clipboard write is otherwise completely silent - the user cannot tell it happened.
        Assert.Equal("Copied", vm.CopyLabel);
    }

    [AvaloniaFact]
    public async Task Switching_report_after_copying_resets_the_confirmation()
    {
        using var book = ExplorerViewModelTests.TwoRecipeBook();
        var vm = new ReportDialogViewModel(book, new FakeDialogs(), new NoopClipboardService());
        await vm.CopyCommand.ExecuteAsync(null);
        Assert.Equal("Copied", vm.CopyLabel);

        vm.ShowIdentityCommand.Execute(null);

        // "Copied" belonged to the other report; leaving it would claim this one is on the clipboard.
        Assert.Equal("Copy", vm.CopyLabel);
    }

    [AvaloniaFact]
    public void The_cookbook_pane_offers_reports_only_when_it_can_show_them()
    {
        using var book = ExplorerViewModelTests.TwoRecipeBook();

        var without = new CookBookDetailViewModel(book, new FakeNotYetWired(), () => { });
        Assert.False(without.ShowReportsCommand.CanExecute(null));

        var opened = false;
        var with = new CookBookDetailViewModel(book, new FakeNotYetWired(), () => { }, () => opened = true);
        Assert.True(with.ShowReportsCommand.CanExecute(null));
        with.ShowReportsCommand.Execute(null);
        Assert.True(opened);
    }

    // ---- preview ----------------------------------------------------------------------------------

    private static (LoadedCookBook book, LoadedRecipe recipe, LoadedIngredient ing) Custom()
    {
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "BG", LayerKind.Custom, null,
                new[] { new Variant("a", "A", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>>
            {
                ["a"] = new Image<Rgba32>(4, 4, new Rgba32(9, 180, 40, 255)),
            },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "bg" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "B", new Dimensions(4, 4),
                new Collection("B", "", "B"), new Dictionary<string, double> { ["cat"] = 1 }),
            Recipes = new[] { recipe },
        };
        return (book, recipe, ing);
    }

    [AvaloniaFact]
    public async Task Export_preview_writes_the_png_generation_would_produce()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var target = Path.Combine(dir, "aura.png");
        var (book, recipe, ing) = Custom();
        var status = new StatusService();
        try
        {
            using var vm = new IngredientDetailViewModel(ing, recipe, book, new ImageBridge(),
                new FakeNotYetWired(), () => { }, () => false, null, status,
                new SavePicker(target), new FakeDialogs());

            Assert.True(vm.ExportPreviewCommand.CanExecute(null));
            await vm.ExportPreviewCommand.ExecuteAsync(null);

            Assert.True(File.Exists(target));

            // A Custom layer is composited as-is and never colorized, so the exported pixels must be
            // the art untouched - the same rule VariantPreview enforces for the CLI.
            using var written = Image.Load<Rgba32>(target);
            Assert.Equal(new Rgba32(9, 180, 40, 255), written[0, 0]);
            Assert.NotNull(status.Last);
        }
        finally { book.Dispose(); Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Cancelling_the_picker_writes_nothing()
    {
        var (book, recipe, ing) = Custom();
        var picker = new SavePicker(null);
        try
        {
            using var vm = new IngredientDetailViewModel(ing, recipe, book, new ImageBridge(),
                new FakeNotYetWired(), () => { }, () => false, null, new StatusService(),
                picker, new FakeDialogs());

            await vm.ExportPreviewCommand.ExecuteAsync(null);   // must not throw
            Assert.Equal(1, picker.SaveCalls);
        }
        finally { book.Dispose(); }
    }

    [AvaloniaFact]
    public void Without_a_picker_the_export_is_unavailable_rather_than_throwing()
    {
        var (book, recipe, ing) = Custom();
        try
        {
            using var vm = new IngredientDetailViewModel(ing, recipe, book, new ImageBridge(),
                new FakeNotYetWired(), () => { }, () => false);

            Assert.False(vm.ExportPreviewCommand.CanExecute(null));
        }
        finally { book.Dispose(); }
    }
}
