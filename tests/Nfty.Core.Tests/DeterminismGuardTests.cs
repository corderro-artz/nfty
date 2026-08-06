using System.Globalization;
using System.IO.Compression;
using System.Text;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>
/// CLAUDE.md names two ordinal sorts as load-bearing for determinism: <see cref="WeightedRoller"/>'s
/// draw order and <see cref="Dna"/>'s layer order. Only one of them was actually guarded — deleting
/// <c>StringComparer.Ordinal</c> from <c>Dna.Compute</c> left the whole suite green while changing
/// the identity of every asset between locales. The function that decides whether two assets are the
/// same asset was protected by nothing.
/// </summary>
public class DeterminismGuardTests
{
    private static string DnaUnder(string culture, params string[] ingredientIds)
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
            return Dna.Compute("r", ingredientIds
                .Select(id => new LayerSelection(id, "v", null, null, 1, 1))
                .ToList());
        }
        finally { Thread.CurrentThread.CurrentCulture = previous; }
    }

    /// <summary>
    /// The ids are chosen to actually diverge. A default <c>OrderBy</c> uses the current culture, and
    /// under sv-SE 'ä' sorts AFTER 'z' while ordinally it comes first — so a culture-sensitive sort
    /// reorders these two and the hash changes. Ascii-only ids would sort identically either way and
    /// the test would pass with the bug present.
    /// </summary>
    [Fact]
    public void Dna_does_not_change_with_the_machine_locale()
    {
        var american = DnaUnder("en-US", "äura", "zenith");
        var swedish = DnaUnder("sv-SE", "äura", "zenith");
        var turkish = DnaUnder("tr-TR", "äura", "zenith");   // the dotted/dotless-I culture

        Assert.Equal(american, swedish);
        Assert.Equal(american, turkish);
    }

    /// <summary>Order of the input list must not matter either: the sort exists so that the same
    /// selection hashes the same however the layers were enumerated.</summary>
    [Fact]
    public void Dna_is_independent_of_the_order_the_layers_arrive_in()
    {
        Assert.Equal(
            DnaUnder("en-US", "äura", "zenith"),
            DnaUnder("en-US", "zenith", "äura"));
    }

    /// <summary>The guard that already worked, kept beside its twin so the pair is visible.</summary>
    [Fact]
    public void Weighted_draw_order_does_not_change_with_the_machine_locale()
    {
        var weights = new Dictionary<string, double> { ["äura"] = 1, ["zenith"] = 1 };

        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
            var a = WeightedRoller.Roll(weights, new SplitMix64Rng(7));
            Thread.CurrentThread.CurrentCulture = new CultureInfo("sv-SE");
            var b = WeightedRoller.Roll(weights, new SplitMix64Rng(7));
            Assert.Equal(a, b);
        }
        finally { Thread.CurrentThread.CurrentCulture = previous; }
    }
}

/// <summary>
/// The defensive arms of <see cref="ArchiveIo"/> — the one place every manifest is read. Coverage
/// showed 100% of its lines run and only 60% of its branches: the happy paths were exercised and the
/// <c>?? throw</c>s were not, so the messages a user actually meets on a damaged file had never been
/// checked.
/// </summary>
public class ArchiveFailureMessageTests
{
    private static string Zip(string extension, Action<ZipArchive> build)
    {
        var dir = Directory.CreateTempSubdirectory();
        string path = Path.Combine(dir.FullName, "broken" + extension);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        build(zip);
        return path;
    }

    private static void Write(ZipArchive zip, string name, string text)
    {
        using var s = zip.CreateEntry(name).Open();
        s.Write(Encoding.UTF8.GetBytes(text));
    }

    [Fact]
    public void A_missing_manifest_names_the_file_it_wanted()
    {
        string path = Zip(".cbk", zip => zip.CreateEntry("nothing.txt"));

        var ex = Assert.Throws<InvalidDataException>(() => CookBookArchive.Read(path));

        Assert.Contains("manifest.json", ex.Message);
    }

    [Fact]
    public void A_manifest_of_literal_null_is_rejected()
    {
        string path = Zip(".cbk", zip => Write(zip, "manifest.json", "null"));

        var ex = Assert.Throws<InvalidDataException>(() => CookBookArchive.Read(path));

        Assert.Contains("null", ex.Message);
    }

    [Fact]
    public void A_missing_variant_png_names_the_entry()
    {
        string path = Zip(".igt", zip => Write(zip, "manifest.json",
            """{"id":"i","name":"I","kind":"custom","colorization":null,"variants":[{"id":"v","name":"V","weight":1}],"schemaVersion":1}"""));

        var ex = Assert.Throws<InvalidDataException>(() => IngredientArchive.Read(path));

        Assert.Contains("variants/v.png", ex.Message);
    }

    /// <summary>
    /// A stray file under <c>recipes/</c> used to surface as "Central Directory corrupt" — a message
    /// that names nothing and blames the OUTER archive, whose directory is intact. Dropping a README
    /// in with an unzip tool is exactly the workflow CLAUDE.md advertises as supported.
    /// </summary>
    [Fact]
    public void A_stray_entry_under_recipes_names_itself_instead_of_blaming_the_cookbook()
    {
        string path = Zip(".cbk", zip =>
        {
            Write(zip, "manifest.json",
                """{"id":"cb","name":"B","canvas":{"width":2,"height":2},"collection":{"name":"B","description":"","symbol":"B"},"recipeWeights":{},"schemaVersion":1}""");
            Write(zip, "recipes/README.txt", "notes to self");
        });

        var ex = Assert.Throws<InvalidDataException>(() => CookBookArchive.Read(path));

        // Names the offending entry, and says what belongs there. The framework's own
        // "Central Directory corrupt" is kept as the tail rather than dropped — it is the accurate
        // cause once you know WHICH file it refers to, and the whole defect was that it did not say.
        Assert.StartsWith("Entry 'recipes/README.txt' is not a readable nfty archive", ex.Message);
        Assert.Contains("only nfty archives belong", ex.Message);
    }
}
