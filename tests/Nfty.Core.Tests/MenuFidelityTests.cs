using Nfty.Core.Demo;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using Nfty.Core.Publish;
using Nfty.Ticket;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Nfty.Core.Tests;

/// <summary>
/// The Menu carries enough to reproduce an asset EXACTLY. Cook one the normal way, then rebuild the
/// same asset from the .mnu alone and compare pixels.
///
/// This is the test the whole format exists to pass. It exercises the atlas rects, the rolled color,
/// the paint order and the absent layers at once, in both art modes — and it ASSERTS that this seed
/// actually rolled that variety, rather than merely commenting that it does. A reweighting or a
/// demo-book edit that silently dropped a branch (all-Custom, nothing ever absent) would otherwise
/// leave this green with a comment that had quietly stopped being true.
/// </summary>
public class MenuFidelityTests
{
    [Theory]
    [InlineData(MenuArtMode.Atlas)]
    [InlineData(MenuArtMode.Loose)]
    public void An_asset_rebuilt_from_a_menu_is_pixel_identical_to_the_cooked_one(MenuArtMode art)
    {
        const string seed = "fidelity";
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string cbk = DemoCookBook.WriteTo(dir);   // takes a DIRECTORY, returns the written path
            using var book = CookBookArchive.Read(cbk);

            using var set = Generator.Generate(book, new GenerateOptions(Count: 8, Seed: seed));

            string mnu = Path.Combine(dir, "game.mnu");
            MenuWriter.Write(book, mnu, new MenuWriteOptions { Art = art });

            using var menu = MenuArchive.Open(mnu);
            var prepared = Ticketer.Prepare(menu.Book);
            var rng = new SplitMix64Rng(SeedHash.ToUlong(seed));
            var seen = new HashSet<string>(StringComparer.Ordinal);

            // What this run actually rolled, collected as it happens rather than assumed from the
            // seed — so losing coverage (a book edit that stops rolling a Static layer, say) fails
            // this test loudly instead of leaving an aspirational comment green.
            var kindsSeen = new HashSet<LayerKind>();
            var everPresent = new HashSet<string>(StringComparer.Ordinal);
            var everAbsent = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < 8; i++)
            {
                AssetTicket? ticket;
                do { ticket = Ticketer.TryRoll(prepared, rng); }
                while (ticket is null || !seen.Add(ticket.Dna));

                Assert.Equal(set.Assets[i].Dna, ticket.Dna);

                var draws = Plating.Plate(menu, ticket);
                foreach (var d in draws) kindsSeen.Add(d.Kind);

                var recipe = menu.Book.Recipes.Single(r => r.Id == ticket.RecipeId);
                var present = new HashSet<string>(
                    ticket.Layers.Select(l => l.IngredientId), StringComparer.Ordinal);
                foreach (string layerId in recipe.LayerOrder)
                    (present.Contains(layerId) ? everPresent : everAbsent).Add(layerId);

                using var rebuilt = RenderFromMenu(menu, draws, menu.Book.Canvas);
                AssertPixelIdentical(set.Assets[i].Image, rebuilt);
            }

            // Coverage this test claims to exercise, asserted directly rather than hoped for.
            Assert.Contains(LayerKind.Dynamic, kindsSeen);
            Assert.Contains(LayerKind.Static, kindsSeen);
            Assert.Contains(LayerKind.Custom, kindsSeen);

            Assert.True(everAbsent.Count > 0,
                "No layer was left out of any of the 8 assets - the absent-layer path was not exercised.");

            // A layer that is present in one asset and absent in another proves the ABSENT-PERCENT
            // roll itself fired at least once. Without this, "everAbsent is non-empty" could be
            // satisfied entirely by a layer that structurally belongs to the OTHER recipe (e.g.
            // "boxbody" is simply not part of "chest"'s layerOrder) and never once by a real
            // absent-percent roll.
            Assert.True(everPresent.Overlaps(everAbsent),
                "No single layer was ever both present in one asset and absent in another - the "
                + "absent-percent roll was never exercised, only layers a recipe never has at all.");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>Renders a set of draws using Core's own Colorizer and Compositor over the Menu's
    /// art.</summary>
    private static Image<Rgba32> RenderFromMenu(
        MenuArchive menu, IReadOnlyList<Draw> draws, Dimensions canvas)
    {
        var layers = new List<Image<Rgba32>>();
        try
        {
            foreach (var draw in draws)
            {
                using var whole = Image.Load<Rgba32>(menu.ArtBytes(draw.ImageKey));
                var cell = draw.Source;
                using var cropped = whole.Clone(c =>
                    c.Crop(new Rectangle(cell.X, cell.Y, cell.Width, cell.Height)));

                layers.Add(draw.Kind == LayerKind.Custom
                    ? cropped.Clone()
                    : Colorizer.Apply(cropped, draw.Hue, draw.Sat, draw.Model));
            }
            return Compositor.Composite(canvas, layers);
        }
        finally { foreach (var l in layers) l.Dispose(); }
    }

    private static void AssertPixelIdentical(Image<Rgba32> expected, Image<Rgba32> actual)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        for (int y = 0; y < expected.Height; y++)
            for (int x = 0; x < expected.Width; x++)
                Assert.True(expected[x, y] == actual[x, y],
                    $"pixel ({x},{y}): expected {expected[x, y]}, got {actual[x, y]}");
    }
}
