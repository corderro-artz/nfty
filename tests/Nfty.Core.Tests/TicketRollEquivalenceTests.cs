using Nfty.Core.Demo;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
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
