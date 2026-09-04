using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Nfty.Core.Formats;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>The CookBook-scoped palette: an ADDITIVE OPTIONAL manifest field, so a collection's
/// colors travel inside the archive without a schema bump.
///
/// The bump is deliberately absent. System.Text.Json ignores unknown properties, so a build that
/// predates this field still reads these archives; bumping Schema.Current would instead make every
/// shipped build reject them. These pin both halves — the field round-trips, and nothing older
/// breaks — the same way <see cref="SchemaCompatibilityTests"/> pins TargetSupply.</summary>
public class CookBookPaletteTests
{
    private static string TempPath(string name) =>
        Path.Combine(Directory.CreateTempSubdirectory().FullName, name);

    private static LoadedCookBook Book(IReadOnlyList<string>? palette)
    {
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "BG", LayerKind.Custom, null,
                new[] { new Variant("a", "A", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>>
            {
                ["a"] = new Image<Rgba32>(2, 2, new Rgba32(1, 2, 3, 255)),
            },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "bg" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "VaporPets", new Dimensions(2, 2),
                new Collection("VaporPets", "d", "VP"),
                new Dictionary<string, double> { ["cat"] = 1 }, Palette: palette),
            Recipes = new[] { recipe },
        };
    }

    [Fact]
    public void A_palette_round_trips_through_the_archive()
    {
        var path = TempPath("palette.cbk");
        var swatches = Palette.ToSpecs(new[] { new RgbColor(214, 36, 159), new RgbColor(61, 127, 143) });

        using (var book = Book(swatches))
            CookBookArchive.Write(path, book.Manifest, book.Recipes);

        using var read = CookBookArchive.Read(path);

        Assert.Equal(swatches, read.Manifest.Palette);
        Assert.Equal(new[] { new RgbColor(214, 36, 159), new RgbColor(61, 127, 143) },
            Palette.FromSpecs(read.Manifest.Palette));
    }

    [Fact]
    public async Task A_palette_survives_the_async_round_trip_too()
    {
        var path = TempPath("palette-async.cbk");
        var swatches = new[] { "hex:d6249f" };

        using (var book = Book(swatches))
            await CookBookArchive.WriteAsync(path, book.Manifest, book.Recipes);

        using var read = await CookBookArchive.ReadAsync(path);

        Assert.Equal(swatches, read.Manifest.Palette);
    }

    [Fact]
    public void The_field_is_written_camel_cased_like_every_other_manifest_property()
    {
        // Manifest JSON goes through Json.Options; serialising with defaults would silently break
        // the round trip by writing "Palette".
        var path = TempPath("case.cbk");
        using (var book = Book(new[] { "hex:d6249f" }))
            CookBookArchive.Write(path, book.Manifest, book.Recipes);

        using var zip = ZipFile.OpenRead(path);
        using var s = zip.GetEntry("manifest.json")!.Open();
        using var doc = JsonDocument.Parse(s);

        Assert.True(doc.RootElement.TryGetProperty("palette", out var prop));
        Assert.Equal("hex:d6249f", prop[0].GetString());
    }

    [Fact]
    public void An_unset_palette_reads_back_as_never_saved_rather_than_empty()
    {
        var path = TempPath("nopalette.cbk");
        using (var book = Book(null))
            CookBookArchive.Write(path, book.Manifest, book.Recipes);

        using var zip = ZipFile.OpenRead(path);
        using var s = zip.GetEntry("manifest.json")!.Open();
        using var doc = JsonDocument.Parse(s);

        // Written as an explicit null, NOT omitted — and that is deliberate rather than incidental.
        // Omitting nulls globally was tried and reverted: `Colorization?` is positional with no
        // default, so RespectRequiredConstructorParameters treats it as required, and leaving it out
        // made every Custom ingredient unreadable. The truncation guard that rule provides is worth
        // more than tidier JSON.
        //
        // This assertion used to be an `if (present) assert null`, which passed whether the field was
        // there or not — so it proved nothing its name claimed.
        Assert.True(doc.RootElement.TryGetProperty("palette", out var prop));
        Assert.Equal(JsonValueKind.Null, prop.ValueKind);

        // And the distinction that matters to a caller: null means "never saved one", not "saved an
        // empty one" — Palette.FromSpecs turns both into an empty list, so only the manifest keeps it.
        using var read = CookBookArchive.Read(path);
        Assert.Null(read.Manifest.Palette);
    }

    [Fact]
    public void A_manifest_written_before_the_palette_existed_still_reads()
    {
        // The literal shape an older build wrote: hand-rolled, so it cannot accidentally carry the
        // field this is meant to prove is optional.
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

        using (var src = Book(null))
            CookBookArchive.Write(path, src.Manifest, src.Recipes);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            zip.GetEntry("manifest.json")!.Delete();
            using var s = zip.CreateEntry("manifest.json").Open();
            s.Write(Encoding.UTF8.GetBytes(legacyJson));
        }

        using var read = CookBookArchive.Read(path);

        Assert.Null(read.Manifest.Palette);          // absent means "never saved one", not empty
        Assert.Empty(Palette.FromSpecs(read.Manifest.Palette));
    }

    [Fact]
    public void A_build_that_has_never_heard_of_the_field_still_reads_the_manifest()
    {
        // The actual claim behind "no bump": deserialize a manifest we just WROTE with a palette
        // into a record shaped like the one an older build has, and it must succeed. If this ever
        // needed a bump, it would fail here rather than in the field.
        var path = TempPath("forward.cbk");
        using (var book = Book(new[] { "hex:d6249f" }))
            CookBookArchive.Write(path, book.Manifest, book.Recipes);

        using var zip = ZipFile.OpenRead(path);
        using var s = zip.GetEntry("manifest.json")!.Open();
        var older = JsonSerializer.Deserialize<OlderCookBookManifest>(s, Json.Options);

        Assert.NotNull(older);
        Assert.Equal("cb", older!.Id);
        Assert.Equal(1, older.SchemaVersion);
    }

    [Fact]
    public void A_book_with_a_palette_is_still_valid_and_still_cooks()
    {
        // A palette is authoring convenience — Validator must have no opinion about it, and it must
        // not reach the pipeline.
        using var withPalette = Book(new[] { "hex:d6249f", "not a spec at all" });
        Assert.Empty(Validator.Validate(withPalette));

        using var without = Book(null);
        using var a = Generation.Generator.Generate(withPalette, new Generation.GenerateOptions(1, "seed"));
        using var b = Generation.Generator.Generate(without, new Generation.GenerateOptions(1, "seed"));

        Assert.Equal(a.Assets.Select(x => x.Dna), b.Assets.Select(x => x.Dna));
    }

    [Fact]
    public void Adding_the_palette_did_not_bump_the_schema()
    {
        // Purely additive, so older builds keep reading these archives. A bump here would orphan
        // every archive already written. If a future change genuinely breaks the format, bump
        // Current and this assertion is the reminder to think about it.
        Assert.Equal(1, Schema.Current);
    }

    [Fact]
    public void The_shipped_v1_fixture_still_reads_and_carries_no_palette()
    {
        // tests/fixtures exists because an OLDER build wrote it. Never regenerated to make a test
        // pass — that launders a format change instead of catching it.
        var fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "VaporPets.cbk");
        using var book = CookBookArchive.Read(fixture);

        Assert.Equal(1, book.Manifest.SchemaVersion);
        Assert.Null(book.Manifest.Palette);
        Assert.Equal("VaporPets", book.Manifest.Name);
        Assert.Empty(Validator.Validate(book));
    }

    /// <summary>A stand-in for the CookBookManifest an older build compiled: every v1 member, and no
    /// palette.</summary>
    private sealed record OlderCookBookManifest(
        string Id,
        string Name,
        Dimensions Canvas,
        Collection Collection,
        IReadOnlyDictionary<string, double> RecipeWeights,
        int SchemaVersion = Schema.Current);
}
