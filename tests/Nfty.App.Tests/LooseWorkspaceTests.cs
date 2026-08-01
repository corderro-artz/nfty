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

    private static LoadedIngredient Ing(string id, int w, int h) => new()
    {
        Manifest = new IngredientManifest(id, id, LayerKind.Dynamic, null,
            new[] { new Variant(id + "-v", "V", 1) }),
        VariantImages = new Dictionary<string, Image<Rgba32>> { [id + "-v"] = new(w, h) },
    };

    [Fact]
    public void WrapRecipe_builds_a_one_recipe_book_sized_to_the_first_variant()
    {
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "bg" }, System.Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { Ing("bg", 5, 7) },
        };
        using var book = LooseWorkspace.WrapRecipe(recipe);
        Assert.Equal(5, book.Manifest.Canvas.Width);
        Assert.Equal(7, book.Manifest.Canvas.Height);
        Assert.Same(recipe, Assert.Single(book.Recipes));
        Assert.Equal(100, book.Manifest.RecipeWeights["cat"]);   // keyed by the recipe's real id
    }

    [Fact]
    public void WrapRecipe_falls_back_to_a_default_canvas_when_the_recipe_has_no_images()
    {
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("empty", "Empty", System.Array.Empty<string>(),
                System.Array.Empty<IncompatibilityRule>()),
            Ingredients = System.Array.Empty<LoadedIngredient>(),
        };
        using var book = LooseWorkspace.WrapRecipe(recipe);
        Assert.Equal(512, book.Manifest.Canvas.Width);
        Assert.Equal(512, book.Manifest.Canvas.Height);
    }
}
