using System.Text.Json;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Output;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

public class SetWriterTests
{
    private static GeneratedSet MakeSet() => new(
        "VaporPets", "desc", "VP", "seed-1",
        new[]
        {
            new GeneratedAsset
            {
                SetNumber = 1, Dna = "abc", RecipeId = "cat", RecipeName = "Cat",
                Image = new Image<Rgba32>(2, 2, new Rgba32(1, 2, 3, 255)),
                Traits = new[] { new TraitSelection("bg", "Background", "sunset", "Sunset") },
                ColorRolls = Array.Empty<ColorRoll>(),
            },
        });

    [Fact]
    public void Writes_images_metadata_and_set_manifest()
    {
        var dir = Path.Combine(Directory.CreateTempSubdirectory().FullName, "out");
        SetWriter.Write(MakeSet(), dir, pack: false);

        Assert.True(File.Exists(Path.Combine(dir, "images", "0001.png")));
        Assert.True(File.Exists(Path.Combine(dir, "set.json")));

        var json = File.ReadAllText(Path.Combine(dir, "metadata", "0001.json"));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("VaporPets #1", doc.RootElement.GetProperty("name").GetString());
        var attrs = doc.RootElement.GetProperty("attributes");
        Assert.Equal("Type", attrs[0].GetProperty("trait_type").GetString());
        Assert.Equal("Cat", attrs[0].GetProperty("value").GetString());
        Assert.Equal("Background", attrs[1].GetProperty("trait_type").GetString());
        Assert.Equal("cat", doc.RootElement.GetProperty("recipe").GetString());
        Assert.Equal("abc", doc.RootElement.GetProperty("dna").GetString());
    }

    [Fact]
    public void ReadExisting_recovers_dnas_and_next_number()
    {
        var dir = Path.Combine(Directory.CreateTempSubdirectory().FullName, "out");
        SetWriter.Write(MakeSet(), dir, pack: false);

        var existing = SetWriter.ReadExisting(dir);
        Assert.Contains("abc", existing.Dnas);
        Assert.Equal(2, existing.NextNumber);
    }

    [Fact]
    public void Pack_produces_a_set_archive()
    {
        var dir = Path.Combine(Directory.CreateTempSubdirectory().FullName, "out");
        SetWriter.Write(MakeSet(), dir, pack: true);
        Assert.True(File.Exists(dir + ".set"));
    }

    [Fact]
    public void Extend_preserves_existing_and_appends_new()
    {
        LoadedIngredient Ing(string id, params string[] vids) => new()
        {
            Manifest = new Nfty.Core.Model.IngredientManifest(id, id,
                Nfty.Core.Model.LayerKind.Static, null,
                vids.Select(v => new Nfty.Core.Model.Variant(v, v, 1)).ToList()),
            VariantImages = vids.ToDictionary(v => v,
                _ => new Image<Rgba32>(2, 2, new Rgba32(9, 9, 9, 255))),
        };
        var book = new Nfty.Core.Formats.LoadedCookBook
        {
            Manifest = new Nfty.Core.Model.CookBookManifest("cb", "VP",
                new Nfty.Core.Model.Dimensions(2, 2),
                new Nfty.Core.Model.Collection("VP", "", "VP"),
                new Dictionary<string, double> { ["cat"] = 1 }),
            Recipes = new[]
            {
                new Nfty.Core.Formats.LoadedRecipe
                {
                    Manifest = new Nfty.Core.Model.RecipeManifest("cat", "Cat",
                        new[] { "bg", "body" }, Array.Empty<Nfty.Core.Model.IncompatibilityRule>()),
                    Ingredients = new[] { Ing("bg", "a", "b"), Ing("body", "x", "y") }, // 4 combos
                },
            },
        };

        var dir = Path.Combine(Directory.CreateTempSubdirectory().FullName, "out");
        SetWriter.Write(Generator.Generate(book, new GenerateOptions(2, "s")), dir, pack: false);

        var existing = SetWriter.ReadExisting(dir);
        var more = Generator.Generate(book, new GenerateOptions(2, "s2"),
            existingDnas: existing.Dnas, startNumber: existing.NextNumber);
        SetWriter.Write(more, dir, pack: false);

        var all = Directory.GetFiles(Path.Combine(dir, "images"), "*.png")
            .Select(Path.GetFileName).OrderBy(x => x);
        Assert.Equal(new[] { "0001.png", "0002.png", "0003.png", "0004.png" }, all);
    }

    [Fact]
    public void Extend_recomputes_collection_rarity_and_count()
    {
        LoadedIngredient Ing(string id, string name, params string[] vids) => new()
        {
            Manifest = new Nfty.Core.Model.IngredientManifest(id, name,
                Nfty.Core.Model.LayerKind.Static, null,
                vids.Select(v => new Nfty.Core.Model.Variant(v, v, 1)).ToList()),
            VariantImages = vids.ToDictionary(v => v,
                _ => new Image<Rgba32>(2, 2, new Rgba32(9, 9, 9, 255))),
        };
        var book = new Nfty.Core.Formats.LoadedCookBook
        {
            Manifest = new Nfty.Core.Model.CookBookManifest("cb", "VP",
                new Nfty.Core.Model.Dimensions(2, 2),
                new Nfty.Core.Model.Collection("VP", "", "VP"),
                new Dictionary<string, double> { ["cat"] = 1 }),
            Recipes = new[]
            {
                new Nfty.Core.Formats.LoadedRecipe
                {
                    Manifest = new Nfty.Core.Model.RecipeManifest("cat", "Cat",
                        new[] { "bg" }, Array.Empty<Nfty.Core.Model.IncompatibilityRule>()),
                    Ingredients = new[] { Ing("bg", "BG", "a", "b", "c", "d") }, // 4 combos
                },
            },
        };

        var dir = Path.Combine(Directory.CreateTempSubdirectory().FullName, "out");
        SetWriter.Write(Generator.Generate(book, new GenerateOptions(2, "s1")), dir, pack: false);

        var e = SetWriter.ReadExisting(dir);
        SetWriter.Write(Generator.Generate(book, new GenerateOptions(2, "s2"), e.Dnas, e.NextNumber), dir, pack: false);

        var setJson = File.ReadAllText(Path.Combine(dir, "set.json"));
        using var setDoc = JsonDocument.Parse(setJson);
        Assert.Equal(4, setDoc.RootElement.GetProperty("count").GetInt32());

        int distributionSum = setDoc.RootElement.GetProperty("distribution")
            .EnumerateArray().Sum(d => d.GetProperty("count").GetInt32());
        Assert.Equal(4, distributionSum);

        // Item from the FIRST batch must reflect full-collection rarity, not just its batch.
        var itemJson = File.ReadAllText(Path.Combine(dir, "metadata", "0001.json"));
        using var itemDoc = JsonDocument.Parse(itemJson);
        foreach (var rarity in itemDoc.RootElement.GetProperty("rarity").EnumerateArray())
        {
            if (rarity.GetProperty("trait_type").GetString() == "BG")
                Assert.Equal(25.0, rarity.GetProperty("rarityPct").GetDouble());
        }
    }
}
