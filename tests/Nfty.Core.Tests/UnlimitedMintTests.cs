using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>
/// Minting MORE assets than the cookbook has distinct combinations — the way a real collection is
/// usually dropped.
/// </summary>
/// <remarks>
/// <para>The two modes answer different questions and both are legitimate. With uniqueness ON,
/// every asset is provably distinct and the run refuses rather than repeat itself; that is the right
/// default for a generative set sold on distinctness. With it OFF, identity is the token id exactly
/// as ERC-721 defines it, and duplicates are the point: a 10,000-piece drop from a few hundred
/// combinations is ordinary, and what makes one asset rarer than another is the WEIGHTS, not the
/// combinatorics.</para>
///
/// <para>So the claim this file has to prove is not merely "duplicates happen". It is that the
/// rarity structure survives them: a low-weighted variant stays proportionally rare, a layer with a
/// low appearance chance is rarer still, and the incompatibility rules are still obeyed. A run that
/// produced duplicates but flattened the weights would pass a naive test and be useless.</para>
/// </remarks>
public class UnlimitedMintTests
{
    private static LoadedIngredient Ing(string id, string name, LayerKind kind,
                                        Colorization? color, params (string Id, double W)[] vs) => new()
    {
        Manifest = new IngredientManifest(id, name, kind, color,
            vs.Select(v => new Variant(v.Id, v.Id, v.W)).ToList()),
        VariantImages = vs.ToDictionary(v => v.Id,
            _ => new Image<Rgba32>(2, 2, new Rgba32(20, 20, 20, 255))),
    };

    /// <summary>A deliberately tiny space: two backgrounds and two auras, one of which is a chase.</summary>
    private static LoadedCookBook Book(double auraAbsentPercent) => new()
    {
        Manifest = new CookBookManifest("cb", "Tiny", new Dimensions(2, 2),
            new Collection("Tiny", "d", "TN"), new Dictionary<string, double> { ["cat"] = 1 }),
        Recipes = new[]
        {
            new LoadedRecipe
            {
                Manifest = new RecipeManifest("cat", "Cat", new[] { "bg", "aura" },
                    Array.Empty<IncompatibilityRule>(),
                    AbsentPercent: auraAbsentPercent > 0
                        ? new Dictionary<string, double> { ["aura"] = auraAbsentPercent }
                        : null),
                Ingredients = new[]
                {
                    Ing("bg", "Background", LayerKind.Custom, null, ("day", 70), ("night", 30)),
                    Ing("aura", "Aura", LayerKind.Custom, null, ("glow", 60), ("spark", 40)),
                },
            },
        },
    };

    [Fact]
    public void Asking_for_more_than_the_space_holds_refuses_when_every_asset_must_be_distinct()
    {
        using var book = Book(0);
        var ex = Assert.Throws<UniqueSpaceExhaustedException>(
            () => Generator.Generate(book, new GenerateOptions(500, "s")));

        // The message states the true maximum, so the author can act on it rather than guess.
        Assert.Contains("4", ex.Message, StringComparison.Ordinal);
        Assert.Contains("500", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same book mints 500 with repeats allowed, and the rarity structure survives.
    /// </summary>
    /// <remarks>
    /// Tolerances are wide on purpose. This is a seeded, deterministic run, so the numbers are
    /// stable — but pinning them exactly would make the test a change-detector for the RNG rather
    /// than a statement about weights, and the claim being made is proportional, not numeric.
    /// </remarks>
    [Fact]
    public void With_repeats_allowed_any_count_mints_and_the_weights_still_decide_rarity()
    {
        using var book = Book(auraAbsentPercent: 90);
        using var set = Generator.Generate(book,
            new GenerateOptions(500, "s", EnforceUniqueDna: false));

        Assert.Equal(500, set.Assets.Count);

        // Duplicates are expected, not tolerated: the space cannot hold 500.
        int distinct = set.Assets.Select(a => a.Dna).Distinct(StringComparer.Ordinal).Count();
        Assert.True(distinct < 500,
            $"every one of 500 assets was distinct from a space of a handful — {distinct} unique.");

        // WEIGHTS. Background is 70/30 and never absent, so its split is the clean test.
        int day = set.Assets.Count(a => a.Traits.Any(t => t.VariantId == "day"));
        double dayShare = 100.0 * day / 500;
        Assert.InRange(dayShare, 60, 80);

        // THE CHASE LAYER. Set to appear one time in ten.
        int auraPresent = set.Assets.Count(a => a.Traits.Any(t => t.IngredientId == "aura"));
        double presentShare = 100.0 * auraPresent / 500;
        Assert.InRange(presentShare, 4, 18);

        // And within the few that have it, the 60/40 between its variants still holds — so a rare
        // layer's rare variant is rarer than either alone, which is the compound scarcity a chase
        // item is for.
        int glow = set.Assets.Count(a => a.Traits.Any(t => t.VariantId == "glow"));
        Assert.True(glow > 0 && glow < auraPresent,
            $"expected both aura variants among the {auraPresent} that have one; glow={glow}.");
        Assert.True(100.0 * glow / 500 < dayShare,
            "a chase variant must be rarer than an ordinary one.");
    }

    [Fact]
    public void A_repeat_run_is_still_reproducible_from_its_seed()
    {
        using var a = Book(50);
        using var b = Book(50);
        using var one = Generator.Generate(a, new GenerateOptions(200, "same", EnforceUniqueDna: false));
        using var two = Generator.Generate(b, new GenerateOptions(200, "same", EnforceUniqueDna: false));

        Assert.Equal(one.Assets.Select(x => x.Dna), two.Assets.Select(x => x.Dna));
    }

    /// <summary>
    /// The Set records which mode made it.
    /// </summary>
    /// <remarks>
    /// The seed alone does not reproduce a run: with uniqueness on, a colliding roll is discarded
    /// and re-rolled, consuming draws the unlimited run never spends, so the two diverge from the
    /// first collision. Without this field a Set carried two thirds of its own recipe — and the
    /// determinism promise this project makes everywhere else would have had a hole in it.
    /// </remarks>
    [Fact]
    public void A_set_records_whether_its_assets_were_required_to_be_distinct()
    {
        foreach (bool unique in new[] { true, false })
        {
            string dir = Directory.CreateTempSubdirectory().FullName;
            try
            {
                using var book = Book(0);
                using var set = Generator.Generate(book,
                    new GenerateOptions(3, "s", EnforceUniqueDna: unique));
                SetWriter.Write(set, dir, pack: false);

                var written = SetReader.Read(dir);
                try { Assert.Equal(unique, written.Manifest.UniqueDna); }
                finally { written.Dispose(); }
            }
            finally { Directory.Delete(dir, recursive: true); }
        }
    }
}
