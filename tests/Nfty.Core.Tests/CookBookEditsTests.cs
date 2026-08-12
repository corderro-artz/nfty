using System.Collections.Generic;
using System.Linq;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.Core.Tests;

public class CookBookEditsTests
{
    private static LoadedIngredient Ing(string id) => new()
    {
        Manifest = new IngredientManifest(id, id, LayerKind.Dynamic, null,
            new[] { new Variant(id + "-v", "V", 1.0) }),
        VariantImages = new Dictionary<string, Image<Rgba32>>
        {
            [id + "-v"] = new Image<Rgba32>(1, 1)
        }
    };

    private static LoadedCookBook OneRecipeBook()
    {
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("aurora", "Aurora", new List<string> { "body" },
                System.Array.Empty<IncompatibilityRule>()),
            Ingredients = new List<LoadedIngredient> { Ing("body") }
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("vp", "VaporPets", new Dimensions(8, 8),
                new Collection("VaporPets", "", "VP"),
                new Dictionary<string, double> { ["aurora"] = 1.0 }),
            Recipes = new List<LoadedRecipe> { recipe }
        };
    }

    [Fact]
    public void Adding_a_new_ingredient_appends_it_and_updates_layer_order()
    {
        var book = CookBookEdits.UpsertIngredient(OneRecipeBook(), "aurora", Ing("ears"));
        var recipe = book.Recipes.Single();
        Assert.Equal(2, recipe.Ingredients.Count);
        Assert.Equal(new[] { "body", "ears" }, recipe.Manifest.LayerOrder);
    }

    [Fact]
    public void Replacing_an_existing_ingredient_keeps_layer_order()
    {
        var book = CookBookEdits.UpsertIngredient(OneRecipeBook(), "aurora", Ing("body"));
        var recipe = book.Recipes.Single();
        Assert.Single(recipe.Ingredients);
        Assert.Equal(new[] { "body" }, recipe.Manifest.LayerOrder);
    }

    [Fact]
    public void Unknown_recipe_id_throws()
    {
        Assert.Throws<System.Collections.Generic.KeyNotFoundException>(
            () => CookBookEdits.UpsertIngredient(OneRecipeBook(), "nope", Ing("ears")));
    }

    private static LoadedCookBook TwoRecipeBook()
    {
        var cat = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new List<string> { "bg", "aura" },
                System.Array.Empty<IncompatibilityRule>()),
            Ingredients = new List<LoadedIngredient> { Ing("bg"), Ing("aura") },
        };
        var dog = new LoadedRecipe
        {
            Manifest = new RecipeManifest("dog", "Dog", new List<string> { "bg" },
                System.Array.Empty<IncompatibilityRule>()),
            Ingredients = new List<LoadedIngredient> { Ing("bg") },
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(8, 8),
                new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 60, ["dog"] = 40 }),
            Recipes = new List<LoadedRecipe> { cat, dog },
        };
    }

    [Fact]
    public void RemoveIngredient_drops_it_from_ingredients_and_layer_order_keeping_others()
    {
        var b = CookBookEdits.RemoveIngredient(TwoRecipeBook(), "cat", "aura");
        var cat = b.Recipes.Single(r => r.Manifest.Id == "cat");
        Assert.DoesNotContain(cat.Ingredients, i => i.Manifest.Id == "aura");
        Assert.DoesNotContain("aura", cat.Manifest.LayerOrder);
        Assert.Contains(cat.Ingredients, i => i.Manifest.Id == "bg");   // sibling kept
        Assert.Equal(2, b.Recipes.Count);                               // other recipe untouched
    }

    [Fact]
    public void RemoveRecipe_drops_the_recipe_and_its_weight()
    {
        var b = CookBookEdits.RemoveRecipe(TwoRecipeBook(), "dog");
        Assert.DoesNotContain(b.Recipes, r => r.Manifest.Id == "dog");
        Assert.False(b.Manifest.RecipeWeights.ContainsKey("dog"));
        Assert.Single(b.Recipes);
    }

    [Fact]
    public void Remove_rejects_absent_ids()
    {
        Assert.Throws<KeyNotFoundException>(() => CookBookEdits.RemoveIngredient(TwoRecipeBook(), "cat", "nope"));
        Assert.Throws<KeyNotFoundException>(() => CookBookEdits.RemoveIngredient(TwoRecipeBook(), "nope", "bg"));
        Assert.Throws<KeyNotFoundException>(() => CookBookEdits.RemoveRecipe(TwoRecipeBook(), "nope"));
    }

    [Fact]
    public void MoveLayer_reorders_one_recipe_and_leaves_the_rest_of_the_book_alone()
    {
        var book = TwoRecipeBook();
        var moved = CookBookEdits.MoveLayer(book, "cat", "aura", 1);

        Assert.Equal(new[] { "aura", "bg" },
            moved.Recipes.Single(r => r.Manifest.Id == "cat").Manifest.LayerOrder);
        Assert.Equal(new[] { "bg" },
            moved.Recipes.Single(r => r.Manifest.Id == "dog").Manifest.LayerOrder);
        Assert.Equal(book.Manifest.RecipeWeights, moved.Manifest.RecipeWeights);

        // The book it was given is untouched — every edit here returns a new graph.
        Assert.Equal(new[] { "bg", "aura" },
            book.Recipes.Single(r => r.Manifest.Id == "cat").Manifest.LayerOrder);
    }

    [Fact]
    public void MoveLayer_clamps_the_depth_rather_than_rejecting_it()
    {
        var moved = CookBookEdits.MoveLayer(TwoRecipeBook(), "cat", "bg", 99);

        Assert.Equal(new[] { "aura", "bg" },
            moved.Recipes.Single(r => r.Manifest.Id == "cat").Manifest.LayerOrder);
    }

    [Fact]
    public void MoveLayer_reuses_every_image_and_carries_the_source_hash()
    {
        var src = TwoRecipeBook();
        var book = new LoadedCookBook
        {
            Manifest = src.Manifest,
            Recipes = src.Recipes,
            SourceSha256 = "abc123",
        };

        var moved = CookBookEdits.MoveLayer(book, "cat", "aura", 1);

        Assert.Equal("abc123", moved.SourceSha256);

        var before = book.Recipes.Single(r => r.Manifest.Id == "cat")
            .Ingredients.Single(i => i.Manifest.Id == "aura");
        var after = moved.Recipes.Single(r => r.Manifest.Id == "cat")
            .Ingredients.Single(i => i.Manifest.Id == "aura");
        Assert.Same(before, after);
        Assert.Same(before.VariantImages["aura-v"], after.VariantImages["aura-v"]);
    }

    [Fact]
    public void MoveLayer_rejects_absent_ids()
    {
        Assert.Throws<KeyNotFoundException>(() => CookBookEdits.MoveLayer(TwoRecipeBook(), "nope", "bg", 1));
        Assert.Throws<KeyNotFoundException>(() => CookBookEdits.MoveLayer(TwoRecipeBook(), "cat", "nope", 1));
    }

    [Fact]
    public void UpsertRecipe_adds_a_new_recipe_with_its_weight()
    {
        var newRecipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("bird", "Bird", new List<string>(),
                System.Array.Empty<IncompatibilityRule>()),
            Ingredients = new List<LoadedIngredient>(),
        };
        var b = CookBookEdits.UpsertRecipe(TwoRecipeBook(), newRecipe, 25);
        Assert.Contains(b.Recipes, r => r.Manifest.Id == "bird");
        Assert.Equal(25, b.Manifest.RecipeWeights["bird"]);
        Assert.Equal(3, b.Recipes.Count);                        // cat, dog, bird
        Assert.Contains(b.Recipes, r => r.Manifest.Id == "cat"); // existing kept
    }

    [Fact]
    public void UpsertRecipe_replaces_an_existing_recipe_and_updates_its_weight()
    {
        var replacement = new LoadedRecipe
        {
            Manifest = new RecipeManifest("dog", "Dog2", new List<string>(),
                System.Array.Empty<IncompatibilityRule>()),
            Ingredients = new List<LoadedIngredient>(),
        };
        var b = CookBookEdits.UpsertRecipe(TwoRecipeBook(), replacement, 5);
        Assert.Equal(2, b.Recipes.Count);                        // still cat, dog (replaced)
        Assert.Equal("Dog2", b.Recipes.Single(r => r.Manifest.Id == "dog").Manifest.Name);
        Assert.Equal(5, b.Manifest.RecipeWeights["dog"]);
    }
}
