using Nfty.Core.Formats;
using Nfty.Core.Model;
using Nfty.Core.Output;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>
/// Two layers in one recipe may not share a display name.
/// </summary>
/// <remarks>
/// A layer's NAME is the <c>trait_type</c> its variant is published under — <see cref="SetWriter"/>
/// writes one attribute per layer keyed by name. Two layers sharing one name therefore write two
/// attributes with the same trait_type: a marketplace shows one trait where the author drew two, and
/// the rarity table folds both layers' variants into a single bucket, which is how a collection
/// ships percentages above 100. The reserved-name check for "Type" was the special case of exactly
/// this; these are the general one.
/// </remarks>
public class DuplicateLayerNameTests
{
    private static LoadedIngredient Ing(string id, string name) => new()
    {
        Manifest = new IngredientManifest(id, name, LayerKind.Custom, null,
            new[] { new Variant("v", "V", 1) }),
        VariantImages = new Dictionary<string, Image<Rgba32>>
        { ["v"] = new(4, 4, new Rgba32(1, 2, 3, 255)) },
    };

    private static LoadedRecipe Recipe(params LoadedIngredient[] ings) => new()
    {
        Manifest = new RecipeManifest("cat", "Cat", ings.Select(i => i.Manifest.Id).ToList(),
            Array.Empty<IncompatibilityRule>()),
        Ingredients = ings,
    };

    private static LoadedCookBook Book(LoadedRecipe recipe) => new()
    {
        Manifest = new CookBookManifest("cb", "Book", new Dimensions(4, 4),
            new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 }),
        Recipes = new[] { recipe },
    };

    [Fact]
    public void Two_layers_sharing_a_name_are_reported()
    {
        using var book = Book(Recipe(Ing("a", "Aura"), Ing("b", "Aura")));

        var problems = Validator.Validate(book);

        Assert.Contains(problems, p => p.Contains("two ingredients named 'Aura'", StringComparison.Ordinal));
    }

    /// <summary>The message has to say WHY, because "rename one" is not obviously necessary until
    /// you know the name is the published trait.</summary>
    [Fact]
    public void The_problem_explains_what_a_layer_name_actually_is()
    {
        using var book = Book(Recipe(Ing("a", "Aura"), Ing("b", "Aura")));

        var problem = Validator.Validate(book)
            .First(p => p.Contains("two ingredients named", StringComparison.Ordinal));

        Assert.Contains("trait", problem, StringComparison.Ordinal);
        Assert.Contains("rarity", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Distinct_names_are_fine()
    {
        using var book = Book(Recipe(Ing("a", "Aura"), Ing("b", "Skin")));

        Assert.DoesNotContain(Validator.Validate(book),
            p => p.Contains("two ingredients named", StringComparison.Ordinal));
    }

    /// <summary>Names only have to be unique WITHIN a recipe: each generated item comes from one
    /// recipe, so two recipes may each have a layer called "Aura" without ever colliding.</summary>
    [Fact]
    public void The_same_name_in_two_different_recipes_is_allowed()
    {
        var first = Recipe(Ing("a", "Aura"));
        var second = new LoadedRecipe
        {
            Manifest = new RecipeManifest("dog", "Dog", new[] { "b" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { Ing("b", "Aura") },
        };
        using var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(4, 4),
                new Collection("Book", "", "B"),
                new Dictionary<string, double> { ["cat"] = 50, ["dog"] = 50 }),
            Recipes = new[] { first, second },
        };

        Assert.DoesNotContain(Validator.Validate(book),
            p => p.Contains("two ingredients named", StringComparison.Ordinal));
    }

    /// <summary>Case matters, because <c>trait_type</c> is compared as written: "Aura" and "aura"
    /// are two traits, so they are not a collision.</summary>
    [Fact]
    public void Names_differing_only_in_case_are_two_traits_and_not_a_collision()
    {
        using var book = Book(Recipe(Ing("a", "Aura"), Ing("b", "aura")));

        Assert.DoesNotContain(Validator.Validate(book),
            p => p.Contains("two ingredients named", StringComparison.Ordinal));
    }

    /// <summary>Validator REPORTS; it never throws. A duplicate name must not take down the rest of
    /// the checks.</summary>
    [Fact]
    public void The_rest_of_validation_still_runs()
    {
        // Same name AND a dangling layerOrder entry: both must be reported.
        var a = Ing("a", "Aura");
        var b = Ing("b", "Aura");
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "a", "b", "ghost" },
                Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { a, b },
        };
        using var book = Book(recipe);

        var problems = Validator.Validate(book);

        Assert.Contains(problems, p => p.Contains("two ingredients named 'Aura'", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("ghost", StringComparison.Ordinal));
    }
}
