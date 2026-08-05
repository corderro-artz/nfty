using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>
/// <see cref="UniqueSpace.Count"/> promises exactly <c>Total</c> distinct DNA are generable, so its
/// bucket arithmetic has to be the same arithmetic <see cref="Dna.Compute"/> uses — not merely
/// equivalent on paper.
///
/// It was not. <see cref="ColorRoller.Roll"/> divides saturation by 100 and <c>Dna</c> multiplies it
/// back, and that round-trip is not the identity in IEEE 754: <c>(29/100.0)*100.0</c> is
/// <c>28.999999999999996</c>. <c>Dna</c> therefore filed sat 29 under bucket 28 while the counter,
/// working from the raw percentage, filed it under 29 — so the counter over-promised and
/// <c>Generate</c> threw the self-contradicting "allows exactly N, but N were requested".
/// </summary>
public class ColorBucketAgreementTests
{
    private static LoadedIngredient Dynamic(string id, int satQ, params int[] degenerateSats) => new()
    {
        Manifest = new IngredientManifest(id, id, LayerKind.Dynamic,
            new Colorization(ColorModel.Hsv, 1, satQ,
                degenerateSats.Select(s => new ColorEntry(1, new ColorRange(0, 0, s, s), null)).ToList()),
            new[] { new Variant("v", "v", 1) }),
        VariantImages = new Dictionary<string, Image<Rgba32>>
        {
            ["v"] = new Image<Rgba32>(2, 2, new Rgba32(128, 128, 128, 255)),
        },
    };

    private static LoadedCookBook Book(LoadedIngredient ing)
    {
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("r", "r", new[] { ing.Manifest.Id },
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

    /// <summary>Rolls the colorization directly and counts the DNA the engine would actually emit.</summary>
    private static int ReachableDna(LoadedCookBook book, int rolls = 8000)
    {
        var col = book.Recipes[0].Ingredients[0].Manifest.Colorization!;
        var rng = new SplitMix64Rng(SeedHash.ToUlong("agreement"));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < rolls; i++)
        {
            var c = ColorRoller.Roll(col, rng);
            seen.Add(Dna.Compute("r", new[]
            {
                new LayerSelection("l", "v", c.H, c.S, col.HueQuantize, col.SatQuantize),
            }));
        }
        return seen.Count;
    }

    // The ten (sat, quantize) pairs whose /100 round-trip lands below the integer. Every one of
    // them used to make the counter disagree with Dna by exactly one bucket.
    [Theory]
    [InlineData(29, 1)]
    [InlineData(29, 29)]
    [InlineData(57, 1)]
    [InlineData(57, 3)]
    [InlineData(57, 19)]
    [InlineData(57, 57)]
    [InlineData(58, 1)]
    [InlineData(58, 2)]
    [InlineData(58, 29)]
    [InlineData(58, 58)]
    public void A_lossy_percentage_lands_in_the_same_bucket_for_the_counter_and_for_dna(int sat, int q)
    {
        // Two entries a point apart, NOT one. With a single entry the counter and Dna both report
        // one bucket even when they disagree about *which* bucket, so a count-only assertion is
        // blind to the whole defect — verified by mutation probe. Pairing them means a disagreement
        // shows up as 2 buckets against 1 reachable DNA.
        using var book = Book(Dynamic("l", q, sat - 1, sat));

        Assert.Empty(Validator.Validate(book));
        var count = UniqueSpace.Count(book);

        Assert.True(count.IsExact);
        Assert.Equal(ReachableDna(book), (int)count.Total);

        // The count is a promise, so hold it to delivery rather than to arithmetic alone.
        using var set = Generator.Generate(book, new GenerateOptions((int)count.Total, "agreement"));
        Assert.Equal((int)count.Total, set.Assets.Count);
    }

    /// <summary>The failure as it actually reached a user: a legal book, a truthful-looking count,
    /// and a generate that cannot deliver it.</summary>
    [Fact]
    public void Two_degenerate_sats_one_apart_do_not_over_promise()
    {
        using var book = Book(Dynamic("l", 1, 28, 29));

        Assert.Empty(Validator.Validate(book));
        var count = UniqueSpace.Count(book);
        Assert.Equal(ReachableDna(book), (int)count.Total);

        // Whatever the count says, Generate must be able to honour it.
        using var set = Generator.Generate(book, new GenerateOptions((int)count.Total, "agreement"));
        Assert.Equal((int)count.Total, set.Assets.Count);
    }

    /// <summary>Every sat 0..100 at a spread of quantizations. A single disagreement anywhere is an
    /// over- or under-promise, so this sweeps rather than sampling.</summary>
    [Fact]
    public void The_counter_agrees_with_dna_across_the_whole_saturation_axis()
    {
        var disagreements = new List<string>();
        foreach (int q in new[] { 1, 2, 3, 5, 7, 10, 19, 25, 29, 33, 50, 57, 58, 100 })
            for (int sat = 0; sat <= 100; sat++)
            {
                // What Dna.Compute will hash, spelled out rather than called, so this test fails if
                // the shared helper is changed to something Dna's historical arithmetic never did.
                long dna = (long)Math.Floor(sat / 100.0 * 100.0 / q);

                // What the counter walks: the roller's own sampler at the degenerate endpoint, fed
                // through the shared bucketer — the exact composition UniqueSpace.BucketSpan uses.
                var range = new ColorRange(0, 0, sat, sat);
                long counter = ColorBuckets.Sat(ColorRoller.SampleSat(range, 0.0), q);

                if (dna != counter) disagreements.Add($"sat={sat} q={q}: dna={dna} counter={counter}");
            }

        Assert.True(disagreements.Count == 0, string.Join("\n", disagreements));
    }

    /// <summary>Hue never had the defect — it has no round-trip — but it shares the helper now, so
    /// pin it too rather than leave the guard one-sided.</summary>
    [Fact]
    public void The_counter_agrees_with_dna_across_the_whole_hue_axis()
    {
        foreach (int q in new[] { 1, 2, 5, 10, 30, 60, 90, 180, 360 })
            for (int hue = 0; hue <= 360; hue++)
            {
                var range = new ColorRange(hue, hue, 0, 0);
                Assert.Equal(
                    (long)Math.Floor(hue / (double)q),
                    ColorBuckets.Hue(ColorRoller.SampleHue(range, 0.0), q));
            }
    }
}
