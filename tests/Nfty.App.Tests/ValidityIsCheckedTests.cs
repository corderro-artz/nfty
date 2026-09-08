using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>The shell and the CookBook detail pane report the REAL validation result.
///
/// Both used to assert it instead. The status bar printed a hardcoded green "Valid" for every book
/// it was handed, and the identity card had no status chip at all — so a book with a variant that
/// does not match the canvas, or a rule naming a layer that is not in the recipe, still announced
/// itself as fine. Reading an archive deliberately does NOT validate it, so the UI has to ask.
///
/// The assertion that matters here is the NEGATIVE one: anything can report "Valid" for a valid
/// book, including a hardcoded string. Only the invalid case can tell the difference.</summary>
public class ValidityIsCheckedTests
{
    /// <summary>A book whose recipe names a layer it does not contain — a real Validator problem.</summary>
    private static LoadedCookBook BrokenBook()
    {
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "Background", LayerKind.Custom, null,
                new[] { new Variant("a", "A", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["a"] = new Image<Rgba32>(8, 8) },
        };
        var recipe = new LoadedRecipe
        {
            // "ghost" is in the layer order but not in Ingredients.
            Manifest = new RecipeManifest("cat", "Cat", new[] { "bg", "ghost" },
                Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(8, 8),
                new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 }),
            Recipes = new[] { recipe },
        };
    }

    [AvaloniaFact]
    public void A_broken_book_is_reported_as_broken_not_as_valid()
    {
        using var book = BrokenBook();

        // Precondition: if Validator stopped flagging this, the test below would pass vacuously.
        Assert.NotEmpty(Validator.Validate(book));

        var vm = new CookBookDetailViewModel(book, () => { });

        Assert.False(vm.IsValid);
        Assert.NotEqual("Valid", vm.StatusText);
        Assert.Contains("problem", vm.StatusText);
        Assert.NotNull(vm.StatusTip);                     // the problems are discoverable, not just counted
        Assert.Contains("ghost", vm.StatusTip!);
    }

    [AvaloniaFact]
    public void A_sound_book_still_reads_as_valid()
    {
        using var book = ExplorerViewModelTests.TwoRecipeBook();
        Assert.Empty(Validator.Validate(book));

        var vm = new CookBookDetailViewModel(book, () => { });
        Assert.True(vm.IsValid);
        Assert.Equal("Valid", vm.StatusText);
        Assert.Null(vm.StatusTip);                        // nothing to explain
    }

    [AvaloniaFact]
    public void The_shell_status_pill_reports_the_same_thing()
    {
        using var book = BrokenBook();
        var nav = new FakeNav(); var dialogs = new FakeDialogs(); var session = new CookBookSession();
        using var vm = new ExplorerViewModel(book, nav, dialogs, new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
            ExplorerViewModelTests.CookFactory(dialogs), session, new FilePickerService(),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), new StatusService());

        Assert.False(vm.IsValid);
        Assert.NotEqual("Valid", vm.ValidityText);
        Assert.Contains("ghost", vm.ValidityTip!);
    }

    // ---- target supply surfacing ---------------------------------------------------------------

    /// <summary>
    /// Unset is a real state — the book has not committed to a number — so the supply rail is not
    /// there at all rather than showing a bar at zero.
    /// </summary>
    /// <remarks>
    /// This used to assert a cookbar SENTENCE, which is gone: the target and the space were being
    /// stated in three places (a chip on the identity card, that sentence, and the figure), and the
    /// rail now states them once, beside the figure the target is a fraction of.
    /// </remarks>
    [AvaloniaFact]
    public void An_unset_target_supply_shows_no_supply_rail()
    {
        using var book = ExplorerViewModelTests.TwoRecipeBook();
        var vm = new CookBookDetailViewModel(book, () => { });

        Assert.False(vm.HasTargetSupply);
        Assert.False(vm.HasSupplyRail);
    }

    [AvaloniaFact]
    public void A_set_target_supply_fills_the_rail_against_the_space()
    {
        using var src = ExplorerViewModelTests.TwoRecipeBook();
        using var book = new LoadedCookBook
        {
            Manifest = src.Manifest with { TargetSupply = 5000 },
            Recipes = src.Recipes,
        };
        var vm = new CookBookDetailViewModel(book, () => { });

        Assert.True(vm.HasTargetSupply);
        Assert.True(vm.HasSupplyRail);
        Assert.Equal("5,000", vm.TargetSupplyText);          // grouped, as the card formats it

        // This fixture's space is 2, so 5,000 asks for far more than the book can make. That is the
        // case the rail exists for, and the one it has to read BEFORE Cook is pressed: the bar is
        // clamped to its track while the figure beside it is not, and the state is named rather
        // than inferred from a bar that happens to be full.
        Assert.True(vm.SupplyExceedsSpace);
        Assert.Equal(100, vm.SupplyPercent);
    }

    [AvaloniaFact]
    public void A_target_inside_the_space_reads_as_a_fraction_of_it()
    {
        using var src = ExplorerViewModelTests.TwoRecipeBook();
        using var book = new LoadedCookBook
        {
            Manifest = src.Manifest with { TargetSupply = 1 },
            Recipes = src.Recipes,
        };
        var vm = new CookBookDetailViewModel(book, () => { });

        Assert.True(vm.HasSupplyRail);
        Assert.False(vm.SupplyExceedsSpace);
        Assert.Equal(50, vm.SupplyPercent);                  // 1 of this fixture's 2
        Assert.Equal("50%", vm.SupplyPercentText);
    }
}
