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
/// This is the test the whole format exists to pass. It exercises the atlas rects, the rolled colour,
/// the paint order and the absent layers at once, in both art modes.
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

            for (int i = 0; i < 8; i++)
            {
                AssetTicket? ticket;
                do { ticket = Ticketer.TryRoll(prepared, rng); }
                while (ticket is null || !seen.Add(ticket.Dna));

                Assert.Equal(set.Assets[i].Dna, ticket.Dna);

                using var rebuilt = RenderFromMenu(menu, ticket, menu.Book.Canvas);
                AssertPixelIdentical(set.Assets[i].Image, rebuilt);
            }
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>Renders a ticket using Core's own Colorizer and Compositor over the Menu's art.</summary>
    private static Image<Rgba32> RenderFromMenu(MenuArchive menu, AssetTicket ticket, Dimensions canvas)
    {
        var layers = new List<Image<Rgba32>>();
        try
        {
            foreach (var draw in Plating.Plate(menu, ticket))
            {
                using var whole = Image.Load<Rgba32>(menu.ArtBytes(draw.ImageKey));
                var cell = draw.Source;
                using var cropped = whole.Clone(c =>
                    c.Crop(new Rectangle(cell.X, cell.Y, cell.Width, cell.Height)));

                layers.Add(draw.Kind == Nfty.Core.Model.LayerKind.Custom
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
