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
    public void The_same_book_writes_a_byte_identical_atlas_every_time()
    {
        // Same cookbook, same atlas is the central promise a Menu makes - untested, this is the
        // weakest point in the format. Written twice in one run, from the same in-memory book, so
        // any nondeterminism in the content-hash grouping or the placement order would show up as a
        // byte difference here.
        var (dir, book) = Demo();
        using (book)
        {
            string mnu1 = Path.Combine(dir, "one.mnu");
            string mnu2 = Path.Combine(dir, "two.mnu");
            MenuWriter.Write(book, mnu1, new MenuWriteOptions { Art = MenuArtMode.Atlas });
            MenuWriter.Write(book, mnu2, new MenuWriteOptions { Art = MenuArtMode.Atlas });

            using (var zip1 = ZipFile.OpenRead(mnu1))
            using (var zip2 = ZipFile.OpenRead(mnu2))
            {
                Assert.Equal(ReadEntryBytes(zip1, "atlas.png"), ReadEntryBytes(zip2, "atlas.png"));
            }

            using var menu1 = MenuArchive.Open(mnu1);
            using var menu2 = MenuArchive.Open(mnu2);
            // The rects are half of what makes a Menu reproducible - identical atlas bytes with a
            // shuffled cell list would still be a Menu that reads different art at the same key on
            // two "identical" writes.
            Assert.Equal(
                menu1.Book.Art.Cells.Select(c => (c.Key, c.X, c.Y, c.Width, c.Height)).ToList(),
                menu2.Book.Art.Cells.Select(c => (c.Key, c.X, c.Y, c.Width, c.Height)).ToList());
        }
        Directory.Delete(dir, recursive: true);
    }

    private static byte[] ReadEntryBytes(ZipArchive zip, string entryName)
    {
        var entry = zip.GetEntry(entryName) ?? throw new InvalidOperationException($"No {entryName}.");
        using var s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
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
