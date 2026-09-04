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

/// <summary>
/// The Ingredient editor's colors readout, and the CookBook panel's DNA-space chips: two places
/// where a number on screen was not the number it claimed to be.
/// </summary>
public class ColorCountReadoutTests
{
    /// <summary>
    /// The readout counts buckets, not the product of the two quantize STEPS.
    /// </summary>
    /// <remarks>
    /// It used to be <c>HueQuantize * SatQuantize</c>, which is not a count of anything: with a
    /// 30-degree hue step and a 20% saturation step it printed "600 colors" where the layer admits
    /// 36. Every ViewModel test passed, because nothing had ever asserted the arithmetic.
    /// </remarks>
    [AvaloniaFact]
    public void Colors_readout_is_the_bucket_count_the_engine_promises()
    {
        var (ing, recipe, book) = IngredientEditorViewModelTests.Real();
        using var vm = new IngredientEditorViewModel(ing, recipe, book, new ImageBridge(),
            new FakeNav(), new CookBookSession(), new FakeDialogs(), new FilePickerService());

        vm.HueQuantize = 30;
        vm.SatQuantize = 20;
        vm.HueMin = 0; vm.HueMax = 360;
        vm.SatMin = 25; vm.SatMax = 70;

        Assert.Equal("≈ 36 colors", vm.ApproxColorsText);
        Assert.DoesNotContain("600", vm.ApproxColorsText);
    }

    /// <summary>A coarser step can only remove colors, so the readout can only fall.</summary>
    [AvaloniaFact]
    public void Coarsening_the_step_lowers_the_readout()
    {
        var (ing, recipe, book) = IngredientEditorViewModelTests.Real();
        using var vm = new IngredientEditorViewModel(ing, recipe, book, new ImageBridge(),
            new FakeNav(), new CookBookSession(), new FakeDialogs(), new FilePickerService());

        vm.HueMin = 0; vm.HueMax = 360; vm.SatMin = 0; vm.SatMax = 100;
        vm.HueQuantize = 10; vm.SatQuantize = 10;
        int fine = Digits(vm.ApproxColorsText);
        vm.HueQuantize = 30; vm.SatQuantize = 20;
        int coarse = Digits(vm.ApproxColorsText);

        Assert.True(coarse < fine, $"fine={fine} coarse={coarse}");
    }

    /// <summary>Narrowing a range lowers it too — the old formula could not see the ranges at all.</summary>
    [AvaloniaFact]
    public void Narrowing_the_range_lowers_the_readout()
    {
        var (ing, recipe, book) = IngredientEditorViewModelTests.Real();
        using var vm = new IngredientEditorViewModel(ing, recipe, book, new ImageBridge(),
            new FakeNav(), new CookBookSession(), new FakeDialogs(), new FilePickerService());

        vm.HueQuantize = 30; vm.SatQuantize = 20; vm.SatMin = 25; vm.SatMax = 70;
        vm.HueMin = 0; vm.HueMax = 360;
        int whole = Digits(vm.ApproxColorsText);
        vm.HueMax = 90;
        int quarter = Digits(vm.ApproxColorsText);

        Assert.Equal(36, whole);
        Assert.Equal(9, quarter);
    }

    /// <summary>
    /// The DNA-space chips are the layer stack in PAINT order, the same order the recipe panel
    /// prints two clicks away.
    /// </summary>
    /// <remarks>
    /// They came off <c>recipe.Ingredients</c> — the archive's own ordering, which nothing
    /// constrains — so the same five numbers appeared shuffled between the CookBook panel and the
    /// Recipe panel one click apart. The fixture here deliberately stores its ingredients in the
    /// REVERSE of its layerOrder, because every shared fixture happens to store them in agreement
    /// and would pass either way.
    /// </remarks>
    [AvaloniaFact]
    public void Dna_space_chips_follow_the_paint_order()
    {
        LoadedIngredient Ing(string id, params string[] variantIds) => new()
        {
            Manifest = new IngredientManifest(id, id, LayerKind.Custom, null,
                variantIds.Select(v => new Variant(v, v, 1)).ToArray()),
            VariantImages = variantIds.ToDictionary(v => v, _ => new Image<Rgba32>(4, 4)),
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "bg", "body", "aura" },
                Array.Empty<IncompatibilityRule>()),
            // Stored back to front on purpose.
            Ingredients = new[] { Ing("aura", "none"), Ing("body", "a", "b"), Ing("bg", "day", "night") },
        };
        using var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(4, 4),
                new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 }),
            Recipes = new[] { recipe },
        };

        var vm = new CookBookDetailViewModel(book, () => { }, () => { });

        Assert.Equal(new[] { "bg", "body", "aura" }, vm.Recipes.Single().Factors.Select(f => f.Name));
    }

    private static int Digits(string text) =>
        int.Parse(new string(text.Where(char.IsDigit).ToArray()));
}
