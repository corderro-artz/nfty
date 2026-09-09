using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The identity card's <c>colorize</c> chip names a color model, says <c>mixed</c> when the book's
/// layers disagree, and says <c>none</c> when nothing in the book is colorized.
/// </summary>
/// <remarks>
/// A CookBook has no color model of its own — it lives on each colorized Ingredient — so this chip
/// is derived, and the derivation has three outcomes rather than two. The third used to print the
/// same em-dash the card uses for a DNA space that cannot be counted, so one glyph stood for both
/// "there is nothing here" and "we cannot tell". The first is a fact and the second is a failure,
/// and on a chip labelled <c>colorize</c> the dash simply read as an error.
/// </remarks>
public class ColorizeChipTests
{
    private static LoadedCookBook BookOf(params LayerKind[] kinds)
    {
        var ings = kinds.Select((k, i) => new LoadedIngredient
        {
            Manifest = new IngredientManifest($"l{i}", $"l{i}", k,
                k == LayerKind.Custom
                    ? null
                    : new Colorization(ColorModel.Hsv, 30, 40,
                        new[] { new ColorEntry(1, new ColorRange(0, 360, 50, 100), null) }),
                [new Variant($"l{i}a", "A", 1)]),
            VariantImages = new Dictionary<string, Image<Rgba32>> { [$"l{i}a"] = new Image<Rgba32>(8, 8) },
        }).ToArray();

        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(8, 8),
                new Collection("Book", "", "BK"),
                new Dictionary<string, double> { ["r"] = 100 }),
            Recipes = [new LoadedRecipe
            {
                Manifest = new RecipeManifest("r", "R", ings.Select(i => i.Manifest.Id).ToArray(), []),
                Ingredients = ings,
            }],
        };
    }

    [AvaloniaFact]
    public void A_book_that_colorizes_nothing_says_none_rather_than_an_em_dash()
    {
        // Every layer Custom: full-color images composited as-is. Ordinary, not broken - a pixel-art
        // collection that never recolors is exactly this - so the chip states the fact.
        using var book = BookOf(LayerKind.Custom, LayerKind.Custom);
        var vm = new CookBookDetailViewModel(book, () => { });

        Assert.Equal("none", vm.ColorizeText);
        Assert.DoesNotContain("—", vm.ColorizeText);
    }

    [AvaloniaFact]
    public void A_colorized_book_names_its_model()
    {
        using var book = BookOf(LayerKind.Dynamic, LayerKind.Custom);
        var vm = new CookBookDetailViewModel(book, () => { });
        Assert.Equal("hsv", vm.ColorizeText);
    }

    [AvaloniaFact]
    public void The_em_dash_still_means_a_figure_that_could_not_be_computed()
    {
        // The other half of the distinction, and the reason "none" had to be a different word: the
        // card keeps the dash for a DNA space it genuinely cannot report. Two states, two glyphs.
        using var book = BookOf(LayerKind.Custom);
        var vm = new CookBookDetailViewModel(book, () => { });

        Assert.NotEqual("—", vm.ColorizeText);
        Assert.False(string.IsNullOrEmpty(vm.UniqueDnaText));
    }
}
