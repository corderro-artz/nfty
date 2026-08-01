using System.Collections.Generic;
using System.Linq;
using Nfty.App.Services;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public class LooseWorkspaceTests
{
    [Fact]
    public void WrapIngredient_builds_a_one_recipe_book_sized_to_the_variants()
    {
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("aura", "Aura", LayerKind.Dynamic, null,
                new[] { new Variant("glow", "Glow", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["glow"] = new(6, 9) },
        };
        using var book = LooseWorkspace.WrapIngredient(ing);
        Assert.Equal(6, book.Manifest.Canvas.Width);
        Assert.Equal(9, book.Manifest.Canvas.Height);
        var recipe = Assert.Single(book.Recipes);
        Assert.Equal(new[] { "aura" }, recipe.Manifest.LayerOrder);
        Assert.Same(ing, recipe.Ingredients.Single());     // wraps the same ingredient
    }
}
