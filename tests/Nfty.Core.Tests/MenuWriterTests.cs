using System.IO.Compression;
using Nfty.Core.Demo;
using Nfty.Core.Formats;
using Nfty.Core.Publish;
using Nfty.Ticket;

namespace Nfty.Core.Tests;

public class MenuWriterTests
{
    private static (string dir, LoadedCookBook book) Demo()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        string cbk = DemoCookBook.WriteTo(dir);   // takes a DIRECTORY, returns the written path
        return (dir, CookBookArchive.Read(cbk));
    }

    [Theory]
    [InlineData(MenuArtMode.Atlas)]
    [InlineData(MenuArtMode.Loose)]
    public void A_written_menu_is_a_zip_with_a_manifest(MenuArtMode art)
    {
        var (dir, book) = Demo();
        using (book)
        {
            string mnu = Path.Combine(dir, "game.mnu");
            MenuWriter.Write(book, mnu, new MenuWriteOptions { Art = art });

            using var zip = ZipFile.OpenRead(mnu);
            Assert.NotNull(zip.GetEntry("manifest.json"));
            Assert.Equal(art == MenuArtMode.Atlas, zip.GetEntry("atlas.png") is not null);
        }
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Every_variant_in_the_book_has_a_cell()
    {
        var (dir, book) = Demo();
        using (book)
        {
            string mnu = Path.Combine(dir, "game.mnu");
            MenuWriter.Write(book, mnu, new MenuWriteOptions { Art = MenuArtMode.Atlas });

            using var menu = MenuArchive.Open(mnu);
            foreach (var recipe in book.Recipes)
                foreach (var ing in recipe.Ingredients)
                    foreach (var v in ing.Manifest.Variants)
                        Assert.True(
                            menu.Book.Art.Cells.Any(c => c.Key ==
                                MenuBook.CellKey(recipe.Manifest.Id, ing.Manifest.Id, v.Id)),
                            $"no cell for {recipe.Manifest.Id}/{ing.Manifest.Id}/{v.Id}");
        }
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Identical_art_is_packed_once()
    {
        // Two recipes sharing a layer must cost one cell's pixels, not two. The keys stay distinct —
        // a reader addresses by key — but they resolve to the same rect.
        var (dir, book) = Demo();
        using (book)
        {
            string mnu = Path.Combine(dir, "game.mnu");
            MenuWriter.Write(book, mnu, new MenuWriteOptions { Art = MenuArtMode.Atlas });

            using var menu = MenuArchive.Open(mnu);
            var rects = menu.Book.Art.Cells.Select(c => (c.X, c.Y)).ToList();
            var distinct = rects.Distinct().Count();
            Assert.True(distinct <= rects.Count);
            // The demo shares art between its two chest recipes, so this must actually bite.
            Assert.True(distinct < rects.Count, "the demo shares art; dedupe did not happen");
        }
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void A_book_too_big_for_one_atlas_is_refused_with_the_numbers()
    {
        // Build a book whose atlas would exceed the pixel ceiling, and assert the refusal names the
        // size and points at --art loose rather than throwing OutOfMemoryException minutes later.
        // Use a synthetic book with a very large canvas; do not actually allocate the atlas.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            MenuWriter.CheckAtlasFits(columns: 200, rows: 200, cellWidth: 1000, cellHeight: 1000));
        Assert.Contains("loose", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("200000", ex.Message, StringComparison.Ordinal);
    }
}
