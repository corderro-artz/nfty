using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The Cook card can limit a run to one recipe, and an extend says what the Set will hold.
/// </summary>
/// <remarks>
/// The engine and the CLI have always had <c>--recipe</c>; the card took the default. And an extend's
/// "Count" means how many MORE, which left the person to add it to what the folder holds. Both are
/// asked of the real card here: the choice must reach the written Set, and the card must stay one
/// height whichever state it is in, because it is a modal and a modal does not scroll.
/// </remarks>
public class CookRecipeAndTotalTests
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

    /// <summary>Three one-variant recipes. The book lists them dog, cat, bird; "bird" is shelved
    /// (weight 0). One variant each means one unique DNA per recipe, so cooks here allow repeats.</summary>
    internal static LoadedCookBook Book()
    {
        LoadedIngredient Ing(string id) => new()
        {
            Manifest = new IngredientManifest(id, id, LayerKind.Custom, null, new[] { new Variant("a", "A", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["a"] = new Image<Rgba32>(8, 8) },
        };
        LoadedRecipe Rec(string id, string name) => new()
        {
            Manifest = new RecipeManifest(id, name, new[] { id + "-body" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { Ing(id + "-body") },
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Pets", new Dimensions(8, 8),
                new Collection("Pets", "", "PT"),
                new Dictionary<string, double> { ["cat"] = 1, ["dog"] = 1, ["bird"] = 0 },
                RecipeOrder: new[] { "dog", "cat", "bird" }),
            Recipes = new[] { Rec("bird", "Bird"), Rec("cat", "Cat"), Rec("dog", "Dog") },
        };
    }

    private static CookDialogViewModel Vm(LoadedCookBook book, string dir) =>
        new(book, new FixedFolder(dir), new NoReveal(), new FakeDialogs());

    private static string[] Recipes(string dir) =>
        Directory.GetFiles(Path.Combine(dir, "nfty"), "*.json").OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => JsonDocument.Parse(File.ReadAllText(f)).RootElement.GetProperty("recipe").GetString()!)
            .ToArray();

    [AvaloniaFact]
    public async Task Choosing_a_recipe_cooks_only_that_recipe()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            using var book = Book();
            var vm = Vm(book, dir);
            vm.Count = 12; vm.Seed = "s"; vm.Unlimited = true;
            vm.SelectedRecipe = vm.RecipeChoices.Single(c => c.Id == "dog");
            await vm.CookCommand.ExecuteAsync(null);

            var recipes = Recipes(dir);
            Assert.Equal(12, recipes.Length);
            Assert.All(recipes, r => Assert.Equal("dog", r));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Any_recipe_cooks_by_weight()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            using var book = Book();
            var vm = Vm(book, dir);
            Assert.Null(vm.SelectedRecipe.Id);                  // the card opens on "any"
            Assert.Equal("Any recipe (by weight)", vm.RecipeChoices[0].Label);
            vm.Count = 40; vm.Seed = "s"; vm.Unlimited = true;
            await vm.CookCommand.ExecuteAsync(null);

            var recipes = Recipes(dir);
            Assert.Contains("cat", recipes);
            Assert.Contains("dog", recipes);                    // both weighted recipes appear
            Assert.DoesNotContain("bird", recipes);             // the shelved one does not, by weight
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public async Task A_shelved_recipe_can_be_chosen_and_is_labeled()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            using var book = Book();
            var vm = Vm(book, dir);
            // Any, then the recipes in the book's own RecipeOrder (not the order they are stored in).
            Assert.Equal(new[] { "Any recipe (by weight)", "Dog", "Cat", "Bird (shelved)" },
                vm.RecipeChoices.Select(c => c.Label));

            vm.Count = 3; vm.Seed = "s"; vm.Unlimited = true;
            vm.SelectedRecipe = vm.RecipeChoices.Single(c => c.Id == "bird");
            await vm.CookCommand.ExecuteAsync(null);
            Assert.All(Recipes(dir), r => Assert.Equal("bird", r));   // --recipe cooks it too
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private static async Task<string> SeedSet(string dir)
    {
        using var book = Book();
        var first = Vm(book, dir);
        first.Count = 5; first.Seed = "first"; first.Unlimited = true;
        await first.CookCommand.ExecuteAsync(null);
        return dir;
    }

    [AvaloniaFact]
    public async Task The_recipe_choice_is_disabled_with_a_reason_while_extending()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            await SeedSet(dir);
            using var book = Book();
            var vm = Vm(book, dir);
            Assert.True(vm.RecipeChoiceEnabled);                // a fresh cook may choose
            vm.Count = 16; vm.Seed = "more"; vm.Unlimited = true;
            vm.SelectedRecipe = vm.RecipeChoices.Single(c => c.Id == "dog");   // chosen before the folder is known
            await vm.CookCommand.ExecuteAsync(null);

            Assert.True(vm.IsExtending);
            Assert.False(vm.RecipeChoiceEnabled);
            Assert.Equal("An extend continues the Set's own mix.", CookDialogViewModel.RecipeLockedReason);

            // The box is a label: the extend ignored the choice and continued the Set's own mix, so
            // the NEW assets (numbers 6 on) are not all the chosen recipe.
            var all = Recipes(dir);
            Assert.Equal(21, all.Length);
            Assert.Contains("cat", all.Skip(5));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Extending_shows_the_resulting_total()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            await SeedSet(dir);
            using var book = Book();
            var vm = Vm(book, dir);
            vm.Count = 1200; vm.Seed = "more"; vm.Unlimited = true;
            await vm.CookCommand.ExecuteAsync(null);

            Assert.Equal(5, vm.ExistingCount);
            Assert.Equal("→ 1,205 total", vm.ResultingTotalText);   // invariant, thousands-separated
            vm.Count = 7;
            Assert.Equal("→ 12 total", vm.ResultingTotalText);       // Count stays "how many more"
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Choosing_a_recipe_then_extending_says_the_choice_was_set_aside()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            await SeedSet(dir);
            using var book = Book();
            var vm = Vm(book, dir);
            var phases = new List<string>();
            vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.PhaseText)) phases.Add(vm.PhaseText); };
            vm.Count = 3; vm.Seed = "more"; vm.Unlimited = true;
            vm.SelectedRecipe = vm.RecipeChoices.Single(c => c.Id == "dog");
            await vm.CookCommand.ExecuteAsync(null);

            Assert.Contains(phases, p => p.StartsWith("Extending 5 assets") && p.EndsWith(CookDialogViewModel.RecipeSetAsideText));
            Assert.Equal("Extending — the Set's own recipe mix is used; your recipe choice was set aside.",
                CookDialogViewModel.RecipeSetAsideText);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Extending_with_no_recipe_chosen_has_nothing_to_set_aside()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            await SeedSet(dir);
            using var book = Book();
            var vm = Vm(book, dir);
            var phases = new List<string>();
            vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.PhaseText)) phases.Add(vm.PhaseText); };
            vm.Count = 3; vm.Seed = "more"; vm.Unlimited = true;
            await vm.CookCommand.ExecuteAsync(null);

            Assert.Contains(phases, p => p.StartsWith("Extending 5 assets"));
            Assert.DoesNotContain(phases, p => p.Contains("set aside"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public async Task The_existing_count_is_the_items_the_set_holds_not_its_highest_number()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            await SeedSet(dir);                                          // items 1..5
            File.Delete(Path.Combine(dir, "nfty", "0003.json"));          // one gone: four items, top number 5
            using var book = Book();
            var vm = Vm(book, dir);
            vm.Count = 7; vm.Seed = "more"; vm.Unlimited = true;
            await vm.CookCommand.ExecuteAsync(null);

            Assert.Equal(4, vm.ExistingCount);
            Assert.Equal("→ 11 total", vm.ResultingTotalText);
            Assert.Contains("11 total", vm.ResultText);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public async Task The_existing_count_counts_repeated_items_each()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            await SeedSet(dir);                                          // one-variant recipes: repeats
            var dnas = Directory.GetFiles(Path.Combine(dir, "nfty"), "*.json")
                .Select(f => JsonDocument.Parse(File.ReadAllText(f)).RootElement.GetProperty("dna").GetString()).ToArray();
            Assert.True(dnas.Distinct().Count() < dnas.Length, "fixture must hold repeated DNA");

            using var book = Book();
            var vm = Vm(book, dir);
            vm.Count = 2; vm.Seed = "more"; vm.Unlimited = true;
            await vm.CookCommand.ExecuteAsync(null);

            Assert.Equal(5, vm.ExistingCount);
            Assert.Equal("→ 7 total", vm.ResultingTotalText);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private static (Window w, Control view) Show(CookDialogViewModel vm)
    {
        var view = new Views.CookDialogView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 900 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view);
    }

    [AvaloniaFact]
    public void A_fresh_cook_reserves_the_total_without_ink()
    {
        using var book = Book();
        var vm = new CookDialogViewModel(book, new FixedFolder("x"), new NoReveal(), new FakeDialogs());
        var (window, view) = Show(vm);
        try
        {
            Assert.Equal("", vm.ResultingTotalText);
            var slot = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "TotalText");
            var before = slot.Bounds;
            Assert.True(before.Width > 40 && before.Height > 8, "the total's slot is laid out with no text in it");

            // Finding out the folder holds a Set draws ink into the slot and moves nothing.
            var cardBefore = view.DesiredSize;
            vm.ExistingCount = 5; vm.IsExtending = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(before, slot.Bounds);
            Assert.Equal(cardBefore, view.DesiredSize);
            Assert.False(string.IsNullOrEmpty(slot.Text));

            // The reason line is the same: always laid out, inked only while extending.
            var reason = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "RecipeReason");
            Assert.Equal(1.0, reason.Opacity);
            vm.IsExtending = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0.0, reason.Opacity);
            Assert.True(reason.Bounds.Height > 8);
        }
        finally { window.Close(); }
    }

    /// <summary>The card's desired size at infinity, the way <c>ModalFitTests</c> measures it.</summary>
    internal static Avalonia.Size Card(CookDialogViewModel vm)
    {
        var (window, view) = Show(vm);
        try
        {
            view.Measure(Avalonia.Size.Infinity);
            return view.DesiredSize;
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void The_cook_card_is_one_height_in_every_state()
    {
        using var book = Book();
        CookDialogViewModel Fresh() => new(book, new FixedFolder("x"), new NoReveal(), new FakeDialogs());

        var heights = new Dictionary<string, double>
        {
            ["fresh"] = Card(Fresh()).Height,
            ["extending"] = Card(new CookDialogViewModel(book, new FixedFolder("x"), new NoReveal(), new FakeDialogs())
                { IsExtending = true, ExistingCount = 4200 }).Height,
            ["models ticked"] = Card(new CookDialogViewModel(book, new FixedFolder("x"), new NoReveal(), new FakeDialogs())
                { IncludeModels = true }).Height,
            ["running"] = Card(new CookDialogViewModel(book, new FixedFolder("x"), new NoReveal(), new FakeDialogs())
                { IsRunning = true, PhaseText = "Extending 4200 assets → 4,250 total…", Progress = 0.4 }).Height,
            ["done"] = Card(new CookDialogViewModel(book, new FixedFolder("x"), new NoReveal(), new FakeDialogs())
                { IsDone = true, IsExtending = true, ExistingCount = 4200,
                  ResultText = "Added +50 assets — 4250 total in the collection. · 50 models",
                  OutputPath = @"C:\Users\someone\Documents\nfty\a-rather-long-collection-folder" }).Height,
        };

        var distinct = heights.Values.Select(h => Math.Round(h, 1)).Distinct().ToList();
        Assert.True(distinct.Count == 1,
            "the card changes height between states: " + string.Join(", ", heights.Select(kv => $"{kv.Key} {kv.Value:0.#}")));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_cook_card_fits_the_smallest_window_with_the_recipe_choice(bool extending)
    {
        // The tallest honest states: the 2D-only note is the longest sentence on the card, and the
        // extending state carries the reason line and the total. Neither may push the card off the
        // window the app allows; a modal does not scroll.
        using var book = CookDialogModelsTests.Book(3, 128);
        var size = Card(new CookDialogViewModel(book, new FixedFolder("x"), new NoReveal(), new FakeDialogs())
            { IsExtending = extending, ExistingCount = 1_000_000 });

        double needW = size.Width * ShellViewModel.BaseScale + 24;
        double needH = size.Height * ShellViewModel.BaseScale + ShellViewModel.ChromeReserve;
        Assert.True(ShellViewModel.MinWindowWidth >= needW && ShellViewModel.MinWindowHeight >= needH,
            $"the cook card needs {needW:0} x {needH:0}; the minimum window is " +
            $"{ShellViewModel.MinWindowWidth:0} x {ShellViewModel.MinWindowHeight:0}");
    }
}
