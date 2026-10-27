using Nfty.Core.Demo;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Ticket;

namespace Nfty.Core.Tests;

/// <summary>
/// Generator now rolls through Ticketer. These pin that the move changed nothing — beside
/// TicketFreezeTests, which pins it against a baseline recorded before the move happened.
/// </summary>
public class TicketRollEquivalenceTests
{
    /// <summary>A spread of seeds, including empty and non-ASCII, driven through both paths.</summary>
    public static TheoryData<string> Seeds() => new("alpha", "beta", "9000", "", "ünïcødé");

    [Theory]
    [MemberData(nameof(Seeds))]
    public void The_generator_and_the_ticketer_roll_the_same_dna_in_the_same_order(string seed)
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string cbk = DemoCookBook.WriteTo(dir);   // takes a DIRECTORY, returns the written path
            using var book = CookBookArchive.Read(cbk);

            using var set = Generator.Generate(book, new GenerateOptions(Count: 50, Seed: seed));

            // The same loop Generator runs, driven directly: one RNG from the same seed, reroll on a
            // rules rejection, dedup on DNA.
            var menu = Ticketer.Prepare(MenuProjection.From(book));
            var rng = new SplitMix64Rng(SeedHash.ToUlong(seed));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var dnas = new List<string>();
            while (dnas.Count < 50)
            {
                var ticket = Ticketer.TryRoll(menu, rng);
                if (ticket is null || !seen.Add(ticket.Dna)) continue;
                dnas.Add(ticket.Dna);
            }

            Assert.Equal(set.Assets.Select(a => a.Dna), dnas);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// Duplicates are the case that matters: asking for the whole of a six-asset space makes most
    /// late rolls collide, so any draw spent differently on a collision moves every later asset. The
    /// demo is far too large for this to ever happen there.
    /// </summary>
    [Fact]
    public void A_run_that_asks_for_the_whole_space_spends_one_draw_per_attempt()
    {
        var variants = Enumerable.Range(0, 6).Select(i => new Variant($"v{i}", $"V{i}", 1 + i)).ToList();
        var images = variants.ToDictionary(v => v.Id,
            v => new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(1, 1),
            StringComparer.Ordinal);
        var layer = new LoadedIngredient
        {
            Manifest = new IngredientManifest("hat", "Hat", LayerKind.Custom, null, variants),
            VariantImages = images,
        };
        using var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("b", "B", new Dimensions(1, 1), new Collection("B", "d", "B"),
                new Dictionary<string, double> { ["r"] = 1 }),
            Recipes =
            [
                new LoadedRecipe
                {
                    Manifest = new RecipeManifest("r", "R", ["hat"], Array.Empty<IncompatibilityRule>()),
                    Ingredients = [layer],
                },
            ],
        };
        var opts = new GenerateOptions(Count: 6, Seed: "tight");

        var menu = Ticketer.Prepare(MenuProjection.From(book));
        var rng = new SplitMix64Rng(SeedHash.ToUlong("tight"));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var expected = new List<string>();
        while (expected.Count < 6)
        {
            var ticket = Ticketer.TryRoll(menu, rng);
            if (ticket is null || !seen.Add(ticket.Dna)) continue;
            expected.Add(ticket.Dna);
        }

        using (var set = Generator.Generate(book, opts))
            Assert.Equal(expected, set.Assets.Select(a => a.Dna));

        var streamed = new List<string>();
        foreach (var asset in Generator.GenerateStreaming(book, opts))
        {
            streamed.Add(asset.Dna);
            asset.Dispose();
        }
        Assert.Equal(expected, streamed);
    }

    [Fact]
    public void A_projected_book_keeps_every_recipe_layer_and_variant()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string cbk = DemoCookBook.WriteTo(dir);   // takes a DIRECTORY, returns the written path
            using var book = CookBookArchive.Read(cbk);
            var menu = MenuProjection.From(book);

            Assert.Equal(book.Recipes.Count, menu.Recipes.Count);
            foreach (var recipe in book.Recipes)
            {
                var projected = menu.Recipes.Single(r => r.Id == recipe.Manifest.Id);
                Assert.Equal(recipe.Manifest.LayerOrder, projected.LayerOrder);
                Assert.Equal(recipe.Ingredients.Count, projected.Layers.Count);
            }
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
