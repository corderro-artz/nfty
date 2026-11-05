using System.Numerics;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

public class DnaIdentifierTests
{
    [Fact]
    public void Historical_dna_encoding_including_the_old_collision_stays_unchanged()
    {
        const string collision = "b6e1efdc250cb83085ce98ec996e261bfbf393f35a4318abda539f89010a19e6";
        Assert.Equal(collision, Dna.Compute("cat", [new("a", "b=c", null, null, 0, 0)]));
        Assert.Equal(collision, Dna.Compute("cat", [new("a=b", "c", null, null, 0, 0)]));
        Assert.Equal("2382ef177b6ab4726c0b474ffec7bbb40044570605798e2864690f031958a040",
            Dna.Compute("cat", [new("bg", "sunset", null, null, 0, 0),
                new("aura", "glow", 181, 0.71, 5, 5)]));
    }

    [Fact]
    public void Ambiguous_ingredient_is_refused_at_every_validation_boundary()
    {
        using var book = CollisionBook();
        var recipe = book.Recipes[0];
        var ingredient = recipe.Ingredients[1];
        AssertDnaSeparatorProblem(Validator.ValidateIngredient(ingredient));
        AssertDnaSeparatorProblem(Validator.ValidateRecipe(recipe));
        AssertDnaSeparatorProblem(Validator.Validate(book));
    }

    [Fact]
    public void Generation_refuses_the_optional_layer_collision_before_rolling()
    {
        using var book = CollisionBook();
        // Four optional selections formerly passed validation but only produced three hashes.
        Assert.Equal(new BigInteger(4), UniqueSpace.Count(book).Total);
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Generator.Generate(book, new GenerateOptions(4, "dna-identifiers")));
        Assert.Contains("'='", ex.Message, StringComparison.Ordinal);
        Assert.Contains("DNA", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Historical_ambiguous_archives_remain_readable_for_inspection_and_repair()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            string path = Path.Combine(dir.FullName, "historical.cbk");
            using var original = CollisionBook();
            CookBookArchive.Write(path, original.Manifest, original.Recipes);
            using var loaded = CookBookArchive.Read(path);
            Assert.Equal("a=b", loaded.Recipes[0].Ingredients[1].Manifest.Id);
            AssertDnaSeparatorProblem(Validator.Validate(loaded));
        }
        finally { dir.Delete(recursive: true); }
    }

    [Fact]
    public void Equals_and_color_like_suffixes_in_variant_ids_keep_the_whole_space_reachable()
    {
        using var book = Book(Ingredient("a", "b=c", "b=c@0,0"), Ingredient("ab", "c"));
        Assert.Empty(Validator.Validate(book));
        Assert.Equal(new BigInteger(6), UniqueSpace.Count(book).Total);
        using var set = Generator.Generate(book, new GenerateOptions(6, "dna-identifiers"));
        Assert.Equal(6, set.Assets.Select(a => a.Dna).Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("ingredient")]
    [InlineData("recipe")]
    [InlineData("variant")]
    public void Ill_formed_utf16_ids_are_refused_before_utf8_replacement_can_merge_them(string kind)
    {
        string first = new((char)0xD800, 1);
        string second = new((char)0xD801, 1);
        Assert.Equal(Dna.Compute("cat", [new("a", first, null, null, 0, 0)]),
            Dna.Compute("cat", [new("a", second, null, null, 0, 0)]));
        foreach (string malformed in new[] { first, new string((char)0xDC00, 1), "x" + first + "y" })
        {
            using var book = Book(Ingredient(kind == "ingredient" ? malformed : "a",
                kind == "variant" ? malformed : "v"), recipeId: kind == "recipe" ? malformed : "cat");
            Assert.Contains(Validator.Validate(book), p => p.Contains("UTF-16", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Paired_surrogates_remain_valid_identifiers()
    {
        using var book = Book(Ingredient("layer\U0001F34E", "variant\U0001F34F"), recipeId: "recipe\U0001F373");
        Assert.Empty(Validator.Validate(book));
        using var set = Generator.Generate(book, new GenerateOptions(2, "unicode-identifiers"));
        Assert.Equal(2, set.Assets.Count);
    }

    private static void AssertDnaSeparatorProblem(IReadOnlyList<string> problems) =>
        Assert.Contains(problems, p => p.Contains("'='", StringComparison.Ordinal)
            && p.Contains("DNA", StringComparison.Ordinal));

    private static LoadedCookBook CollisionBook() => Book(Ingredient("a", "b=c"), Ingredient("a=b", "c"));

    private static LoadedIngredient Ingredient(string id, params string[] variants) => new()
    {
        Manifest = new IngredientManifest(id, id, LayerKind.Custom, null,
            variants.Select(v => new Variant(v, v, 1)).ToList()),
        VariantImages = variants.ToDictionary(v => v, _ => new Image<Rgba32>(1, 1, new Rgba32(10, 20, 30, 255))),
    };

    private static LoadedCookBook Book(LoadedIngredient first, LoadedIngredient? second = null,
        string recipeId = "cat")
    {
        LoadedIngredient[] ingredients = second is null ? [first] : [first, second];
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("book", "Book", new Dimensions(1, 1),
                new Collection("Book", "", "B"), new Dictionary<string, double> { [recipeId] = 1 }),
            Recipes = [new LoadedRecipe
            {
                Manifest = new RecipeManifest(recipeId, "Recipe", ingredients.Select(i => i.Manifest.Id).ToList(), [],
                    AbsentPercent: ingredients.ToDictionary(i => i.Manifest.Id, _ => 50d)),
                Ingredients = ingredients,
            }],
        };
    }
}
