using System.IO.Compression;
using Nfty.Core.Demo;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Nfty.Core.Publish;
using Nfty.Ticket;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

public class MenuWriterTests
{
    private static (string dir, LoadedCookBook book) Demo()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        string cbk = DemoCookBook.WriteTo(dir);   // takes a DIRECTORY, returns the written path
        return (dir, CookBookArchive.Read(cbk));
    }

    /// <summary>
    /// A book whose walk order and ordinal order DISAGREE: the recipe's ingredient list — which is
    /// what the writer walks, and therefore the order its dictionaries are populated in — is zebra
    /// then apple, while "rec/apple/v" sorts before "rec/zebra/v". Each layer's single variant is a
    /// different solid fill, so the two never collapse into one deduped cell.
    /// </summary>
    /// <param name="appleSize">The apple variant's side. Anything but the canvas's 2 makes a book
    /// <c>Validator</c> refuses.</param>
    private static LoadedCookBook DisagreeingOrderBook(int appleSize = 2)
    {
        static LoadedIngredient Layer(string id, Rgba32 fill, int size = 2) => new()
        {
            Manifest = new IngredientManifest(id, id, LayerKind.Custom, null,
                new[] { new Variant("v", "v", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>>
            {
                ["v"] = new Image<Rgba32>(size, size, fill),
            },
        };

        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Orders", new Dimensions(2, 2),
                new Collection("Orders", "d", "ORD"), new Dictionary<string, double> { ["rec"] = 1 }),
            Recipes = new[]
            {
                new LoadedRecipe
                {
                    Manifest = new RecipeManifest("rec", "Rec", new[] { "zebra", "apple" },
                        Array.Empty<IncompatibilityRule>()),
                    Ingredients = new[]
                    {
                        Layer("zebra", new Rgba32(200, 30, 30, 255)),
                        Layer("apple", new Rgba32(30, 30, 200, 255), appleSize),
                    },
                },
            },
        };
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

    /// <summary>
    /// Which grid slot an image lands in follows its ordinally-least cell key, never the order the
    /// writer happened to walk the book in.
    /// </summary>
    /// <remarks>
    /// <para><b>This is needed ON TOP of the byte-diff above, and the byte-diff cannot replace
    /// it.</b> Writing twice in one run proves the two writes agree with EACH OTHER; it does not
    /// prove the order is stable ACROSS PROCESSES, which is what reproducibility actually means
    /// here. The writer groups cell keys in a <c>Dictionary</c>, and with no removals a
    /// <c>Dictionary</c> enumerates in INSERTION order, not hash-bucket order — but that insertion
    /// order is itself just whatever order the book's own collections happened to walk in, an
    /// implementation detail nobody chose and nothing here contracts to keep stable. Delete the
    /// ordinal <c>OrderBy</c> in <c>MenuWriter</c> and the byte-diff stays green (one process, one
    /// walk order, self-consistent) while the actual placement silently stops being a function of
    /// the cell key — same book, a layout that tracks whatever the walk order happens to be rather
    /// than the key that is supposed to decide it.</para>
    ///
    /// <para>So this pins the PLACEMENT rather than the agreement, on a fixture whose walk order and
    /// ordinal order disagree. Note the manifest's cell LIST is sorted ordinally again when it is
    /// built, so the cell order carries no evidence either way — the rects are the only thing that
    /// records which image went into which slot.</para>
    /// </remarks>
    [Fact]
    public void The_atlas_slot_an_image_lands_in_follows_the_ordinal_cell_key()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            using var book = DisagreeingOrderBook();
            string mnu = Path.Combine(dir, "order.mnu");
            MenuWriter.Write(book, mnu, new MenuWriteOptions { Art = MenuArtMode.Atlas });

            using var menu = MenuArchive.Open(mnu);
            var apple = menu.Cell(MenuBook.CellKey("rec", "apple", "v"));
            var zebra = menu.Cell(MenuBook.CellKey("rec", "zebra", "v"));

            // "rec/apple/v" sorts before "rec/zebra/v", so apple takes slot 0 - the atlas origin -
            // even though zebra is the layer the writer encoded and grouped first.
            Assert.Equal(0, apple.X);
            Assert.Equal(0, apple.Y);
            Assert.False(zebra.X == 0 && zebra.Y == 0,
                "zebra took the origin, so placement followed the walk order rather than the cell key");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
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
        var ex = Assert.Throws<AtlasTooLargeException>(() =>
            MenuWriter.CheckAtlasFits(columns: 200, rows: 200, cellWidth: 1000, cellHeight: 1000));
        Assert.Contains("loose", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("200000", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_side_refusal_keeps_the_text_the_command_line_always_printed()
    {
        // Pinned word for word: splitting the refusal into Problem + Hint must not change what the
        // CLI prints. The expected string is the one the writer threw before it had a type.
        var ex = Assert.Throws<AtlasTooLargeException>(() =>
            MenuWriter.CheckAtlasFits(columns: 200, rows: 200, cellWidth: 1000, cellHeight: 1000));
        Assert.Equal("A Menu atlas of 200000 x 200000 is past the 16384 pixel limit on a side. "
            + "Most things that would load an atlas texture cannot open one larger, so this is "
            + "refused rather than built — write with --art loose instead.", ex.Message);
        Assert.EndsWith("refused rather than built", ex.Problem);
        Assert.Equal("write with --art loose instead.", ex.Hint);
        Assert.DoesNotContain("--art", ex.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_one_pixel_strip_past_the_side_limit_is_the_same_typed_refusal()
    {
        // (The area branch cannot be reached on its own: MaxPixels is MaxSide squared, so a grid whose
        // sides both fit always fits in area too. Its text is pinned by the side case above.)
        var ex = Assert.Throws<AtlasTooLargeException>(() =>
            MenuWriter.CheckAtlasFits(columns: 1, rows: 1, cellWidth: 16385, cellHeight: 1));
        Assert.Equal("A Menu atlas of 16385 x 1 is past the 16384 pixel limit on a side. "
            + "Most things that would load an atlas texture cannot open one larger, so this is "
            + "refused rather than built — write with --art loose instead.", ex.Message);
        Assert.IsAssignableFrom<InvalidOperationException>(ex);
    }

    // ------------------------------------------------------------------ validation

    /// <summary>
    /// A book <c>generate</c> refuses must not become a Menu either. The case chosen is the one whose
    /// damage is quietest: a variant larger than the canvas was packed into a canvas-sized rect, so
    /// <c>serve</c> cropped it — and in atlas mode drew part of its NEIGHBOR's cell. Refused the way
    /// <c>Generator</c> refuses it, and before a byte reaches the destination.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_book_the_validator_refuses_is_not_written(bool async)
    {
        using var book = DisagreeingOrderBook(appleSize: 3);   // canvas is 2x2
        Assert.NotEmpty(Validator.Validate(book));

        string dir = Directory.CreateTempSubdirectory().FullName;
        string mnu = Path.Combine(dir, "game.mnu");
        try
        {
            var options = new MenuWriteOptions { Art = MenuArtMode.Atlas };
            var ex = async
                ? await Assert.ThrowsAsync<InvalidOperationException>(
                    () => MenuWriter.WriteAsync(book, mnu, options, null, TestContext.Current.CancellationToken))
                : Assert.Throws<InvalidOperationException>(() => MenuWriter.Write(book, mnu, options));

            Assert.StartsWith("Invalid cookbook:", ex.Message, StringComparison.Ordinal);
            Assert.Contains("apple", ex.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(mnu));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
