using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>The schema gate's compatibility rule, and the contract that makes it sound.
///
/// <see cref="UnsupportedSchemaVersionException.Require"/> used to demand EXACT equality with
/// <see cref="Schema.Current"/>. That made the format unevolvable: bumping Current by one would have
/// made every archive already written unreadable — including tests/fixtures, which exists to catch
/// exactly that kind of silent format break. A version gate whose only safe value is the one it
/// already has is not a gate.
///
/// It now accepts 1..Current and refuses anything newer. Reading older is only sound because of a
/// contract this project keeps deliberately: every field added after v1 is OPTIONAL and defaults to
/// "absent". These tests pin both halves — the range, and the contract.
///
/// <see cref="SchemaVersionTests"/> covers the future-version rejection per archive type; this file
/// covers the boundaries and the round-trip of a post-v1 field.</summary>
public class SchemaCompatibilityTests
{
    private static string TempDir() => Directory.CreateTempSubdirectory().FullName;
    private static string TempPath(string name) => Path.Combine(TempDir(), name);

    private static LoadedIngredient Ing() => new()
    {
        Manifest = new IngredientManifest("bg", "BG", LayerKind.Custom, null,
            new[] { new Variant("a", "A", 1) }),
        VariantImages = new Dictionary<string, Image<Rgba32>>
        {
            ["a"] = new Image<Rgba32>(2, 2, new Rgba32(1, 2, 3, 255)),
        },
    };

    /// <summary>Two variants, so the unique space is 2 and a generation comparison actually exercises
    /// rolling. One variant gives a space of exactly 1, and asking for more correctly throws
    /// UniqueSpaceExhaustedException - the engine being right, not the test.</summary>
    private static LoadedIngredient RollableIng() => new()
    {
        Manifest = new IngredientManifest("bg", "BG", LayerKind.Custom, null,
            new[] { new Variant("a", "A", 1), new Variant("b", "B", 1) }),
        VariantImages = new Dictionary<string, Image<Rgba32>>
        {
            ["a"] = new Image<Rgba32>(2, 2, new Rgba32(1, 2, 3, 255)),
            ["b"] = new Image<Rgba32>(2, 2, new Rgba32(4, 5, 6, 255)),
        },
    };

    private static LoadedCookBook RollableBook(int? targetSupply)
    {
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "bg" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { RollableIng() },
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "VaporPets", new Dimensions(2, 2),
                new Collection("VaporPets", "d", "VP"),
                new Dictionary<string, double> { ["cat"] = 1 }, TargetSupply: targetSupply),
            Recipes = new[] { recipe },
        };
    }

    private static LoadedCookBook Book(int? targetSupply = null, int schemaVersion = Schema.Current)
    {
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "bg" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { Ing() },
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "VaporPets", new Dimensions(2, 2),
                new Collection("VaporPets", "d", "VP"),
                new Dictionary<string, double> { ["cat"] = 1 },
                SchemaVersion: schemaVersion, TargetSupply: targetSupply),
            Recipes = new[] { recipe },
        };
    }

    // ---- the accepted range -------------------------------------------------------------------

    /// <summary>The range rule itself, pinned against a HYPOTHETICAL current version.
    ///
    /// This matters more than it looks. With <see cref="Schema.Current"/> at 1, the range 1..Current
    /// contains exactly one value, so every test written against the real constant passes whether the
    /// gate accepts a range or demands exact equality — I proved that by mutation-probing the widened
    /// gate back to `==` and watching all sixteen tests stay green. Passing a hypothetical current is
    /// what makes "older versions are readable" actually testable before a version 2 exists.</summary>
    [Theory]
    // version, current, expected
    [InlineData(1, 1, true)]     // the only case reachable today
    [InlineData(1, 2, true)]     // THE point: an old archive read by a newer build
    [InlineData(1, 9, true)]
    [InlineData(2, 3, true)]
    [InlineData(3, 3, true)]     // current itself
    [InlineData(2, 1, false)]    // newer than the build understands
    [InlineData(4, 3, false)]
    [InlineData(0, 1, false)]    // not a schema at all
    [InlineData(-1, 5, false)]
    public void The_supported_range_is_oldest_through_current(int version, int current, bool expected)
        => Assert.Equal(expected, Schema.IsSupported(version, current));

    [Fact]
    public void Every_version_this_build_writes_can_be_read_back()
    {
        for (int v = Schema.Oldest; v <= Schema.Current; v++)
        {
            var path = TempPath($"v{v}.cbk");
            using (var book = Book(schemaVersion: v))
                CookBookArchive.Write(path, book.Manifest, book.Recipes);

            using var read = CookBookArchive.Read(path);
            Assert.Equal(v, read.Manifest.SchemaVersion);
        }
    }

    [Fact]
    public void One_past_current_is_refused_and_says_why()
    {
        var path = TempPath("future.cbk");
        using (var book = Book(schemaVersion: Schema.Current + 1))
            CookBookArchive.Write(path, book.Manifest, book.Recipes);

        var ex = Assert.Throws<UnsupportedSchemaVersionException>(() => CookBookArchive.Read(path));

        Assert.Equal(Schema.Current + 1, ex.Found);
        Assert.Equal(Schema.Current, ex.Supported);
        // The message must name the boundary, not just fail: a user hitting this needs to know
        // whether to upgrade nfty or fix the file.
        Assert.Contains(Schema.Current.ToString(), ex.Message);
        Assert.Contains("supports up to", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_version_below_one_is_refused_as_invalid_not_as_too_new(int version)
    {
        var path = TempPath($"bad{version}.cbk");
        using (var book = Book(schemaVersion: version))
            CookBookArchive.Write(path, book.Manifest, book.Recipes);

        var ex = Assert.Throws<UnsupportedSchemaVersionException>(() => CookBookArchive.Read(path));

        Assert.Equal(version, ex.Found);
        Assert.Contains("at least 1", ex.Message);
        Assert.DoesNotContain("supports up to", ex.Message);   // a different failure, a different message
    }

    [Fact]
    public void The_range_check_applies_to_every_archive_type_not_just_cookbooks()
    {
        var igt = TempPath("future.igt");
        var ing = Ing();
        IngredientArchive.Write(igt, ing.Manifest with { SchemaVersion = Schema.Current + 1 }, ing.VariantImages);
        Assert.Throws<UnsupportedSchemaVersionException>(() => IngredientArchive.Read(igt));

        var rcp = TempPath("future.rcp");
        using (var i2 = Ing())
        {
            var manifest = new RecipeManifest("cat", "Cat", new[] { "bg" },
                Array.Empty<IncompatibilityRule>(), SchemaVersion: Schema.Current + 1);
            RecipeArchive.Write(rcp, manifest, new[] { i2 });
        }
        Assert.Throws<UnsupportedSchemaVersionException>(() => RecipeArchive.Read(rcp));
    }

    // ---- the contract that makes reading older sound --------------------------------------------

    /// <summary>A manifest whose JSON omits every field added after v1 must still read. This is the
    /// literal shape of an archive written by an older build, hand-rolled rather than produced by
    /// this one, so it cannot accidentally include a field the test is meant to prove is optional.</summary>
    [Fact]
    public void A_manifest_written_before_a_field_existed_still_reads()
    {
        var path = TempPath("legacy.cbk");
        const string legacyJson = """
        {
          "id": "cb",
          "name": "VaporPets",
          "canvas": { "width": 2, "height": 2 },
          "collection": { "name": "VaporPets", "description": "d", "symbol": "VP" },
          "recipeWeights": { "cat": 1 },
          "schemaVersion": 1
        }
        """;

        // Build a .cbk by hand with that manifest and one real nested recipe, so the read exercises
        // the whole path rather than just JSON deserialization.
        using (var src = Book())
        {
            CookBookArchive.Write(path, src.Manifest, src.Recipes);
        }
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            zip.GetEntry("manifest.json")!.Delete();
            var entry = zip.CreateEntry("manifest.json");
            using var s = entry.Open();
            s.Write(Encoding.UTF8.GetBytes(legacyJson));
        }

        using var read = CookBookArchive.Read(path);

        Assert.Equal("cb", read.Manifest.Id);
        Assert.Equal(1, read.Manifest.SchemaVersion);
        Assert.Null(read.Manifest.TargetSupply);   // absent means unset, not zero
    }

    [Fact]
    public void The_shipped_v1_fixture_still_reads_after_the_gate_change()
    {
        // The whole point of tests/fixtures is that an OLDER build wrote them and they still read.
        // If the gate change broke backward compatibility, this is where it shows.
        var fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "VaporPets.cbk");
        using var book = CookBookArchive.Read(fixture);

        Assert.Equal(1, book.Manifest.SchemaVersion);
        Assert.NotEmpty(book.Recipes);
        Assert.Null(book.Manifest.TargetSupply);
    }

    // ---- target supply -------------------------------------------------------------------------

    [Fact]
    public void Target_supply_round_trips_through_an_archive()
    {
        var path = TempPath("target.cbk");
        using (var book = Book(targetSupply: 500))
            CookBookArchive.Write(path, book.Manifest, book.Recipes);

        using var read = CookBookArchive.Read(path);
        Assert.Equal(500, read.Manifest.TargetSupply);
    }

    [Fact]
    public void Target_supply_is_absent_from_the_json_when_unset_so_older_builds_see_no_change()
    {
        var path = TempPath("none.cbk");
        using (var book = Book(targetSupply: null))
            CookBookArchive.Write(path, book.Manifest, book.Recipes);

        using var zip = ZipFile.OpenRead(path);
        using var s = zip.GetEntry("manifest.json")!.Open();
        using var doc = JsonDocument.Parse(s);

        // Null is written as JSON null or omitted; either way it must not deserialize to a number.
        if (doc.RootElement.TryGetProperty("targetSupply", out var prop))
            Assert.Equal(JsonValueKind.Null, prop.ValueKind);
    }

    [Fact]
    public void Adding_target_supply_did_not_bump_the_schema()
    {
        // Deliberate: the change is purely additive and System.Text.Json ignores unknown properties,
        // so older builds read these archives fine. Bumping would instead make every SHIPPED build -
        // whose gate demands exact equality - reject them. If a future change genuinely breaks the
        // format, bump Current and this assertion is the reminder to think about it.
        Assert.Equal(1, Schema.Current);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_target_supply_below_one_is_a_validation_problem(int target)
    {
        using var book = Book(targetSupply: target);
        var problems = Validator.Validate(book);

        Assert.Contains(problems, p => p.Contains("target supply", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(10_000)]
    public void An_unset_or_positive_target_supply_is_not_a_problem(int? target)
    {
        using var book = Book(targetSupply: target);
        var problems = Validator.Validate(book);

        Assert.DoesNotContain(problems, p => p.Contains("target supply", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Target_supply_does_not_influence_generation()
    {
        // It is declarative. Two books identical but for the target must cook byte-identical output,
        // or the field has quietly become part of the pipeline.
        using var withTarget = RollableBook(targetSupply: 3);
        using var without = RollableBook(targetSupply: null);

        using var a = Generation.Generator.Generate(withTarget, new Generation.GenerateOptions(2, "seed"));
        using var b = Generation.Generator.Generate(without, new Generation.GenerateOptions(2, "seed"));

        Assert.Equal(a.Assets.Count, b.Assets.Count);
        Assert.Equal(a.Assets.Select(x => x.Dna), b.Assets.Select(x => x.Dna));
    }
}
