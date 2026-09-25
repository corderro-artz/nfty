using Nfty.Core.Formats;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

public class OpacityTests
{
    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, false, true)]
    [InlineData(128, false, true)]
    [InlineData(254, false, true)]
    [InlineData(255, true, false)]
    public void Solid_and_partial_are_decided_by_alpha_alone(byte a, bool solid, bool partial)
    {
        var p = new Rgba32(10, 20, 30, a);
        Assert.Equal(solid, Opacity.IsSolid(p));
        Assert.Equal(partial, Opacity.IsPartial(p));
    }

    [Fact]
    public void One_partial_pixel_makes_the_image_partial()
    {
        using var img = new Image<Rgba32>(3, 3, new Rgba32(0, 0, 0, 255));
        Assert.False(Opacity.HasPartialAlpha(img));
        img[2, 2] = new Rgba32(0, 0, 0, 0);
        Assert.False(Opacity.HasPartialAlpha(img));   // fully erased is not partial
        img[1, 2] = new Rgba32(0, 0, 0, 200);
        Assert.True(Opacity.HasPartialAlpha(img));
    }

    private static Image<Rgba32> Solid(int w, int h, byte alpha) =>
        new(w, h, new Rgba32(10, 20, 30, alpha));

    private static LoadedIngredient Ing(string name, params (string id, string vname, byte alpha)[] variants)
    {
        var images = new Dictionary<string, Image<Rgba32>>(StringComparer.Ordinal);
        foreach (var (id, _, alpha) in variants) images[id] = Solid(4, 4, alpha);
        return new LoadedIngredient
        {
            Manifest = new IngredientManifest(name.ToLowerInvariant(), name, LayerKind.Custom, null,
                variants.Select(v => new Variant(v.id, v.vname, 1)).ToList()),
            VariantImages = images,
        };
    }

    [Fact]
    public void An_ingredient_is_partial_iff_any_variant_carries_partial_alpha()
    {
        using var clean = Ing("Solid", ("a", "Opaque", 255), ("b", "Erased", 0));
        Assert.False(Opacity.HasPartialAlpha(clean));

        using var dirty = Ing("Soft", ("a", "Opaque", 255), ("b", "Half", 128));
        Assert.True(Opacity.HasPartialAlpha(dirty));
    }

    [Fact]
    public void A_recipe_is_partial_iff_any_layer_carries_partial_alpha()
    {
        using var top = Ing("Top", ("t", "T", 255));
        using var bottom = Ing("Bottom", ("b", "B", 255));
        var clean = new LoadedRecipe
        {
            Manifest = new RecipeManifest("r", "R", new[] { "bottom", "top" },
                Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { top, bottom },
        };
        Assert.False(Opacity.HasPartialAlpha(clean));

        using var soft = Ing("Soft", ("s", "S", 128));
        var dirty = new LoadedRecipe
        {
            Manifest = new RecipeManifest("r2", "R2", new[] { "bottom", "soft" },
                Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { bottom, soft },
        };
        Assert.True(Opacity.HasPartialAlpha(dirty));
    }

    [Fact]
    public void A_book_is_partial_iff_any_variant_image_anywhere_in_it_has_a_partial_pixel()
    {
        using var top = Ing("Top", ("t", "T", 255));
        using var bottom = Ing("Bottom", ("b", "B", 255));
        var cleanRecipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("r", "R", new[] { "bottom", "top" },
                Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { top, bottom },
        };
        var cleanBook = new LoadedCookBook
        {
            Manifest = new CookBookManifest("book", "Book", new Dimensions(4, 4),
                new Collection("Book", "desc", "BOOK"),
                new Dictionary<string, double> { ["r"] = 1 }),
            Recipes = new[] { cleanRecipe },
        };
        Assert.False(Opacity.HasPartialAlpha(cleanBook));

        using var soft = Ing("Soft", ("s", "S", 128));
        var dirtyRecipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("r2", "R2", new[] { "soft" },
                Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { soft },
        };
        var dirtyBook = new LoadedCookBook
        {
            Manifest = new CookBookManifest("book", "Book", new Dimensions(4, 4),
                new Collection("Book", "desc", "BOOK"),
                new Dictionary<string, double> { ["r"] = 1, ["r2"] = 1 }),
            Recipes = new[] { cleanRecipe, dirtyRecipe },
        };
        Assert.True(Opacity.HasPartialAlpha(dirtyBook));
    }
}
