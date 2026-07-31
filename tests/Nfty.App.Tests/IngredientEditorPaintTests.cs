using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public class IngredientEditorPaintTests
{
    // A small dynamic ingredient (value-map layer) with one variant on an 8x8 canvas.
    private static (LoadedIngredient ing, LoadedRecipe recipe, LoadedCookBook book) Fixture()
    {
        var coloriz = new Colorization(ColorModel.Hsv, 12, 4,
            new[] { new ColorEntry(1, new ColorRange(0, 360, 40, 100), null) });
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("aura", "Aura", LayerKind.Dynamic, coloriz,
                new[] { new Variant("glow", "Glow", 1), new Variant("spark", "Spark", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["glow"] = new(8, 8), ["spark"] = new(8, 8) },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "aura" }, System.Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(8, 8),
                new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 }),
            Recipes = new[] { recipe },
        };
        return (ing, recipe, book);
    }

    private static IngredientEditorViewModel Editor()
    {
        var (ing, recipe, book) = Fixture();
        return new IngredientEditorViewModel(ing, recipe, book, new ImageBridge(), new FakeNav(), new FakeNotYetWired());
    }

    [AvaloniaFact]
    public void Canvas_and_preview_build_over_a_draft()
    {
        using var vm = Editor();
        Assert.NotNull(vm.Canvas);
        Assert.NotNull(vm.Preview);
        Assert.Equal(0, vm.ValueAt(2, 2));   // seeded from a blank 8x8 image → value 0
    }
}
