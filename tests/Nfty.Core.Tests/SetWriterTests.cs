using System.Globalization;
using System.Text.Json;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
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
                Traits = new[]
                {
                    new TraitSelection("bg", "Background", "sunset", "Sunset"),
                    new TraitSelection("aura", "Aura", "glow", "Glow"),
                },
                ColorRolls = new[]
                {
                    new ColorRoll("bg", LayerKind.Custom, null, null, null),
                    new ColorRoll("aura", LayerKind.Dynamic, ColorModel.Hsv, 187, 0.72),
                },
            },
        });

    /// <summary>A real .cbk on disk, so its hash is over actual archive bytes.</summary>
    private static string WriteCookBook(string dir)
    {
        var path = Path.Combine(dir, "VaporPets.cbk");
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "Background", LayerKind.Custom, null,
                new[] { new Variant("sunset", "Sunset", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>>
            {
                ["sunset"] = new Image<Rgba32>(2, 2, new Rgba32(255, 128, 0, 255)),
            },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "bg" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        CookBookArchive.Write(path,
            new CookBookManifest("cb", "VaporPets", new Dimensions(2, 2),
                new Collection("VaporPets", "d", "VP"),
                new Dictionary<string, double> { ["cat"] = 1 }),
            new[] { recipe });
        return path;
    }

    [Fact]
    public void Set_json_records_the_source_cookbook_hash()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var cbkPath = WriteCookBook(dir);
        var outDir = Path.Combine(dir, "out");

        var book = CookBookArchive.Read(cbkPath);
        var set = Generator.Generate(book, new GenerateOptions(1, "seed-1"));
        SetWriter.Write(set, outDir, pack: false);
        foreach (var a in set.Assets) a.Image.Dispose();

        string expected = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(cbkPath))).ToLowerInvariant();
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "set.json")));

        Assert.Equal(expected, doc.RootElement.GetProperty("cookbookSha256").GetString());
    }

    [Fact]
    public void Set_json_hash_is_null_for_a_cookbook_that_never_touched_disk()
    {
        // In-memory books (tests, and a GUI holding an unsaved cookbook) have no source file
        // to hash. The field must be null rather than a hash of something invented.
        var dir = Path.Combine(Directory.CreateTempSubdirectory().FullName, "out");
        SetWriter.Write(MakeSet(), dir, pack: false);

        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "set.json")));

        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("cookbookSha256").ValueKind);
    }

    /// <summary>
    /// A set whose trait types and recipe ids sort differently under a Swedish collation than an
    /// English one: sv-SE orders 'z' before 'ä', en-US the reverse.
    /// </summary>
    private static GeneratedSet CultureSensitiveSet() => new(
        "VaporPets", "desc", "VP", "seed-1",
        new[]
        {
            new GeneratedAsset
            {
                SetNumber = 1, Dna = "abc", RecipeId = "zebra", RecipeName = "Zebra",
                Image = new Image<Rgba32>(2, 2, new Rgba32(1, 2, 3, 255)),
                Traits = new[]
                {
                    new TraitSelection("z", "zebra", "zv", "zink"),
                    new TraitSelection("a", "änd", "av", "ätt"),
                },
                ColorRolls = new[]
                {
                    new ColorRoll("z", LayerKind.Custom, null, null, null),
                    new ColorRoll("a", LayerKind.Custom, null, null, null),
                },
            },
            new GeneratedAsset
            {
                SetNumber = 2, Dna = "def", RecipeId = "änd", RecipeName = "And",
                Image = new Image<Rgba32>(2, 2, new Rgba32(4, 5, 6, 255)),
                Traits = new[]
                {
                    new TraitSelection("z", "zebra", "zv", "zink"),
                    new TraitSelection("a", "änd", "av", "ätt"),
                },
                ColorRolls = new[]
                {
                    new ColorRoll("z", LayerKind.Custom, null, null, null),
                    new ColorRoll("a", LayerKind.Custom, null, null, null),
                },
            },
        });

    private static string WriteSetJsonUnderCulture(string cultureName)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);
            var dir = Path.Combine(Directory.CreateTempSubdirectory().FullName, "out");
            using var set = CultureSensitiveSet();
            SetWriter.Write(set, dir, pack: false);
            return File.ReadAllText(Path.Combine(dir, "set.json"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Set_json_is_byte_identical_across_cultures()
    {
        // Spec 5.5: same cookbook + same seed => byte-identical output. A current-culture sort
        // makes that a lie — the machine's locale leaks into the artifact.
        Assert.Equal(WriteSetJsonUnderCulture("en-US"), WriteSetJsonUnderCulture("sv-SE"));
    }

    /// <summary>A second batch landing in the same directory — the shape `extend` writes.</summary>
    private static GeneratedSet SecondBatch() => new(
        "VaporPets", "desc", "VP", "seed-2",
        new[]
        {
            new GeneratedAsset
            {
                SetNumber = 2, Dna = "def", RecipeId = "cat", RecipeName = "Cat",
                Image = new Image<Rgba32>(2, 2, new Rgba32(9, 9, 9, 255)),
                Traits = new[] { new TraitSelection("bg", "Background", "dawn", "Dawn") },
                ColorRolls = new[] { new ColorRoll("bg", LayerKind.Custom, null, null, null) },
            },
        });

    [Fact]
    public void Extend_over_a_set_missing_an_opensea_sibling_names_the_file()
    {
        // A set pairs every nfty/NNNN.json with a metadata/NNNN.json. If the sibling is gone the
        // set is corrupt; a raw FileNotFoundException makes the user guess what nfty was doing.
        var dir = Path.Combine(Directory.CreateTempSubdirectory().FullName, "out");
        SetWriter.Write(MakeSet(), dir, pack: false);
        File.Delete(Path.Combine(dir, "metadata", "0001.json"));

        using var more = SecondBatch();
        var ex = Assert.Throws<CorruptSetException>(() => SetWriter.Write(more, dir, pack: false));

        Assert.Contains("0001.json", ex.Message, StringComparison.Ordinal);
        Assert.Contains("metadata", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtendAsync_over_a_set_missing_an_opensea_sibling_names_the_file()
    {
        var dir = Path.Combine(Directory.CreateTempSubdirectory().FullName, "out");
        SetWriter.Write(MakeSet(), dir, pack: false);
        File.Delete(Path.Combine(dir, "metadata", "0001.json"));

        using var more = SecondBatch();
        var ex = await Assert.ThrowsAsync<CorruptSetException>(
            () => SetWriter.WriteAsync(more, dir, pack: false));

        Assert.Contains("0001.json", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Writes_images_and_set_manifest()
    {
        var dir = Path.Combine(Directory.CreateTempSubdirectory().FullName, "out");
        SetWriter.Write(MakeSet(), dir, pack: false);

        Assert.True(File.Exists(Path.Combine(dir, "images", "0001.png")));
        Assert.True(File.Exists(Path.Combine(dir, "set.json")));
        Assert.True(File.Exists(Path.Combine(dir, "metadata", "0001.json")));
        Assert.True(File.Exists(Path.Combine(dir, "nfty", "0001.json")));
    }

    [Fact]
    public void OpenSea_metadata_file_is_standard_only()
    {
        var dir = Path.Combine(Directory.CreateTempSubdirectory().FullName, "out");
        SetWriter.Write(MakeSet(), dir, pack: false);

        var json = File.ReadAllText(Path.Combine(dir, "metadata", "0001.json"));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("VaporPets #1", root.GetProperty("name").GetString());
        Assert.Equal("desc", root.GetProperty("description").GetString());
        Assert.Equal("images/0001.png", root.GetProperty("image").GetString());

        var attrs = root.GetProperty("attributes");
        Assert.Equal("Type", attrs[0].GetProperty("trait_type").GetString());
        Assert.Equal("Cat", attrs[0].GetProperty("value").GetString());
        Assert.Equal("Background", attrs[1].GetProperty("trait_type").GetString());

        // Standards-pure: no nfty-specific keys leak into the OpenSea file.
        foreach (var extra in new[] { "setNumber", "recipe", "dna", "seed", "rarity", "colorRolls", "layers" })
            Assert.False(root.TryGetProperty(extra, out _), $"OpenSea file must not contain '{extra}'.");
    }

    [Fact]
    public void Nfty_metadata_file_carries_extras_and_all_layer_colors()
    {
        var dir = Path.Combine(Directory.CreateTempSubdirectory().FullName, "out");
        SetWriter.Write(MakeSet(), dir, pack: false);

        var json = File.ReadAllText(Path.Combine(dir, "nfty", "0001.json"));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(1, root.GetProperty("setNumber").GetInt32());
        Assert.Equal("cat", root.GetProperty("recipe").GetString());
        Assert.Equal("abc", root.GetProperty("dna").GetString());
        Assert.Equal("seed-1", root.GetProperty("seed").GetString());
        Assert.NotEqual(0, root.GetProperty("rarity").GetArrayLength());

        var layers = root.GetProperty("layers");
        Assert.Equal(2, layers.GetArrayLength());
        var bg = layers.EnumerateArray().Single(l => l.GetProperty("layer").GetString() == "bg");
        Assert.Equal("custom", bg.GetProperty("kind").GetString());
        var aura = layers.EnumerateArray().Single(l => l.GetProperty("layer").GetString() == "aura");
        Assert.Equal("dynamic", aura.GetProperty("kind").GetString());
        Assert.Equal("hsv", aura.GetProperty("model").GetString());
        Assert.Equal(187, aura.GetProperty("h").GetDouble());
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
                Nfty.Core.Model.LayerKind.Custom, null,
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
                Nfty.Core.Model.LayerKind.Custom, null,
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
        var itemJson = File.ReadAllText(Path.Combine(dir, "nfty", "0001.json"));
        using var itemDoc = JsonDocument.Parse(itemJson);
        foreach (var rarity in itemDoc.RootElement.GetProperty("rarity").EnumerateArray())
        {
            if (rarity.GetProperty("trait_type").GetString() == "BG")
                Assert.Equal(25.0, rarity.GetProperty("rarityPct").GetDouble());
        }
    }
}
