using System.Globalization;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using Nfty.Core.Stats;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>
/// The <c>inspect</c> report for a cooked Set — the report the manual promised for a long time
/// before the command would take a <c>.set</c> at all.
/// </summary>
public class SetReportTests
{
    private static LoadedCookBook Book()
    {
        LoadedIngredient Ing(string id, string name, params string[] vids) => new()
        {
            Manifest = new IngredientManifest(id, name, LayerKind.Custom, null,
                vids.Select(v => new Variant(v, v.ToUpperInvariant(), 1)).ToList()),
            VariantImages = vids.ToDictionary(v => v, _ => new Image<Rgba32>(4, 4)),
        };
        var cat = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "bg" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { Ing("bg", "Background", "a", "b") },
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Vapor Cats", new Dimensions(4, 4),
                new Collection("Vapor Cats", "desc", "VC"),
                new Dictionary<string, double> { ["cat"] = 100 }),
            Recipes = new[] { cat },
        };
    }

    /// <summary>Cooks a real Set into a temp folder and reads it back, so the report is rendered
    /// from a manifest the writer produced rather than one the test hand-built. Everything below
    /// depends on set.json meaning what SetWriter says it means.</summary>
    private static LoadedSet Cook(bool unique = true, int count = 2)
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        using var book = Book();
        using var set = Generator.Generate(book,
            new GenerateOptions(count, "seed1", EnforceUniqueDna: unique));
        SetWriter.Write(set, dir, pack: false);
        return SetReader.Read(dir);
    }

    [Fact]
    public void It_reports_what_the_run_produced()
    {
        using var set = Cook();

        var text = SetReport.Render(set);

        Assert.StartsWith("Set: Vapor Cats", text);
        Assert.Contains("Assets: 2", text);
        Assert.Contains("Seed: seed1", text);
        Assert.Contains("Recipes (by id):", text);
        Assert.Contains("Traits (observed):", text);
        // The trait table is the observed collection, so its values are the ones that were actually
        // rolled — a name from the artwork, not a heading the report invented.
        Assert.Contains("Background", text);
    }

    [Fact]
    public void It_names_the_cookbook_that_produced_the_set()
    {
        // The one field CLAUDE.md insists stays threaded through, and until now nothing in the
        // product ever showed it: an author holding a Set and three CookBooks could not tell which
        // of them made it without unzipping set.json by hand.
        using var set = Cook();

        Assert.Contains($"CookBook SHA-256: {set.Manifest.CookbookSha256}", SetReport.Render(set));
    }

    [Fact]
    public void An_unrecorded_cookbook_hash_says_so_rather_than_printing_nothing()
    {
        // Null is a real state — a book generated in memory never came from a file — and a blank
        // after the label reads as a rendering fault rather than as an answer.
        var set = new LoadedSet
        {
            Manifest = new SetManifest("In Memory", 1, "s", null, "nfty/1.0",
                Array.Empty<RecipeCount>(), Array.Empty<RarityAttribute>()),
            Items = Array.Empty<SetItem>(),
        };

        Assert.Contains("CookBook SHA-256: not recorded", SetReport.Render(set));
    }

    [Theory]
    [InlineData(true, "required")]
    [InlineData(false, "not required")]
    [InlineData(null, "not recorded")]
    public void Unique_dna_has_three_states_and_null_is_not_false(bool? recorded, string expected)
    {
        // The seed alone does not reproduce a run: uniqueness discards colliding rolls and so
        // consumes RNG draws the unlimited mode never spends. A Set written before the field
        // existed cannot say which it was, and printing "not required" there would invent a fact.
        var set = new LoadedSet
        {
            Manifest = new SetManifest("S", 1, "s", null, "nfty/1.0",
                Array.Empty<RecipeCount>(), Array.Empty<RarityAttribute>(), recorded),
            Items = Array.Empty<SetItem>(),
        };

        var line = SetReport.Render(set).Split('\n').First(l => l.Contains("Unique DNA:"));
        Assert.Contains(expected, line);
        if (recorded is null) Assert.DoesNotContain("not required", line);
    }

    [Fact]
    public void An_empty_distribution_and_rarity_omit_their_headings()
    {
        // A heading with no rows under it reads as a failed scan — the rule KitchenReport.Section
        // already follows. This is what a Set with nothing recorded should look like: the identity
        // block, and nothing pretending to be a table.
        var set = new LoadedSet
        {
            Manifest = new SetManifest("S", 0, "s", null, "nfty/1.0",
                Array.Empty<RecipeCount>(), Array.Empty<RarityAttribute>()),
            Items = Array.Empty<SetItem>(),
        };

        var text = SetReport.Render(set);
        Assert.DoesNotContain("Recipes", text);
        Assert.DoesNotContain("Traits", text);
    }

    [Fact]
    public void Percentages_are_invariant_whatever_the_machine_locale_is()
    {
        // This text is copied, pasted into an issue and diffed against a colleague's run. A plain
        // interpolation renders 55.00 as "55,00" under sv-SE, which makes two identical collections
        // look different — the defect IdentityReport already carries a note about.
        using var set = Cook();
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            var text = SetReport.Render(set);
            Assert.Contains("100.00%", text);
            Assert.DoesNotContain("100,00", text);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void A_set_folder_is_recognised_but_an_ordinary_folder_is_not()
    {
        // What lets `inspect ./collection` work straight after `generate --out ./collection`.
        // It asks for set.json rather than merely for a directory, so a folder that is not a Set
        // still falls through to the caller's own unknown-path message instead of being guessed at.
        var dir = Directory.CreateTempSubdirectory().FullName;
        Assert.False(SetReader.IsSetFolder(dir));

        using var book = Book();
        using var generated = Generator.Generate(book, new GenerateOptions(1, "s"));
        SetWriter.Write(generated, dir, pack: false);

        Assert.True(SetReader.IsSetFolder(dir));
        Assert.False(SetReader.IsSetFolder(Path.Combine(dir, "images")));
        Assert.False(SetReader.IsSetFolder(Path.Combine(dir, "set.json")));   // the file, not a folder
        Assert.False(SetReader.IsSetFolder(""));
    }
}
