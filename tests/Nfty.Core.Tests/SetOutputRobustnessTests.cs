using System.Text.Json;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>
/// Two ways a Set could be written or re-read wrongly, both of which reached the user as either
/// corrupt output or a framework exception with nothing useful in it.
/// </summary>
public class SetOutputRobustnessTests
{
    private static LoadedIngredient Ing(string id, string name, params string[] variantIds) => new()
    {
        Manifest = new IngredientManifest(id, name, LayerKind.Custom, null,
            variantIds.Select(v => new Variant(v, v, 1)).ToList()),
        VariantImages = variantIds.ToDictionary(
            v => v, _ => new Image<Rgba32>(2, 2, new Rgba32(1, 2, 3, 255))),
    };

    private static LoadedCookBook Book(LoadedIngredient ing, string recipeName = "Cat")
    {
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("r", recipeName, new[] { ing.Manifest.Id },
                Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(2, 2),
                new Collection("B", "d", "B"), new Dictionary<string, double> { ["r"] = 1.0 }),
            Recipes = new[] { recipe },
        };
    }

    /// <summary>
    /// An Ingredient named "Type" merged with the pseudo trait-type the Recipe is published under,
    /// because both live in one namespace in the rarity table. The shipped nfty/NNNN.json carried a
    /// duplicated row and a rarityPct of 200 — a percentage above 100, in the file a marketplace
    /// reads. It is refused up front, since after writing the two are indistinguishable: on extend
    /// both read back as the bare string "Type".
    /// </summary>
    [Fact]
    public void An_ingredient_named_Type_is_refused_rather_than_shipped_at_200_percent()
    {
        using var book = Book(Ing("l", SetWriter.TypeTrait, "Cat"));

        var problems = Validator.Validate(book);

        Assert.Contains(problems, p => p.Contains("reserved"));
        // And Generate refuses, because it validates itself.
        Assert.ThrowsAny<Exception>(() => Generator.Generate(book, new GenerateOptions(1, "s")));
    }

    /// <summary>An ordinary ingredient name is of course untouched.</summary>
    [Fact]
    public void An_ingredient_named_anything_else_is_fine()
    {
        using var book = Book(Ing("l", "Background", "Day"));

        Assert.Empty(Validator.Validate(book));
    }

    private static string WriteSetWithItem(string itemJson)
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        Directory.CreateDirectory(Path.Combine(dir, "nfty"));
        File.WriteAllText(Path.Combine(dir, "nfty", "0001.json"), itemJson);
        return dir;
    }

    /// <summary>
    /// extend re-reads every nfty/NNNN.json to learn which DNA already exist. A missing field used
    /// to surface as KeyNotFoundException — "The given key was not present in the dictionary" — and
    /// ErrorReport prints exactly that, so the user extending their collection learned nothing about
    /// which file was bad or why. SiblingOf, forty lines away in the same file, already did this
    /// properly with CorruptSetException.
    /// </summary>
    [Fact]
    public void Extend_names_the_file_when_an_item_has_no_dna()
    {
        var dir = WriteSetWithItem("""{"setNumber":1}""");

        var ex = Assert.Throws<CorruptSetException>(() => SetWriter.ReadExisting(dir));

        Assert.Contains("0001.json", ex.Message);
        Assert.Contains("dna", ex.Message);
    }

    [Fact]
    public void Extend_names_the_file_when_an_item_has_no_set_number()
    {
        var dir = WriteSetWithItem("""{"dna":"abc"}""");

        var ex = Assert.Throws<CorruptSetException>(() => SetWriter.ReadExisting(dir));

        Assert.Contains("0001.json", ex.Message);
        Assert.Contains("setNumber", ex.Message);
    }

    [Fact]
    public void Extend_names_the_file_when_an_item_is_not_json()
    {
        var dir = WriteSetWithItem("{ truncated");

        var ex = Assert.Throws<CorruptSetException>(() => SetWriter.ReadExisting(dir));

        Assert.Contains("0001.json", ex.Message);
    }

    [Fact]
    public async Task The_async_reader_reports_a_corrupt_item_identically()
    {
        var dir = WriteSetWithItem("""{"setNumber":1}""");

        var ex = await Assert.ThrowsAsync<CorruptSetException>(() => SetWriter.ReadExistingAsync(dir));

        Assert.Contains("dna", ex.Message);
    }

    /// <summary>A well-formed item still reads, so the guards above did not simply reject everything.</summary>
    [Fact]
    public void A_well_formed_item_still_reads()
    {
        var dir = WriteSetWithItem(JsonSerializer.Serialize(new { dna = "abc", setNumber = 7 }, Json.Options));

        var existing = SetWriter.ReadExisting(dir);

        Assert.Equal("abc", Assert.Single(existing.Dnas));
        Assert.Equal(8, existing.NextNumber);
    }
}
