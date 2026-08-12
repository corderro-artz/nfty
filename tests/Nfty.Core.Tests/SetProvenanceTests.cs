using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>
/// Whether the CookBook about to extend a Set is the one that cooked it.
///
/// <para>The rule that is easy to get backwards, and is therefore what most of this file is:
/// <b>a null on either side means "cannot tell", which is not "they differ".</b> Both sides are
/// legitimately absent — an unsaved book has no file to hash, a Set can be cooked from one, and an
/// older Set may carry no stamp at all — so warning on a null would fire on the ordinary case and
/// teach the author to ignore the one message that matters.</para>
/// </summary>
public class SetProvenanceTests
{
    private const string ShaA = "a7675bf3aedb558afba2905e75c44a64b1936a71fb77b6bb4fbaa50deebe3e77";
    private const string ShaB = "9e5862c59af759cd6ba2cd26a42b7f0e41567ce8aa96e23e45c13893ed31d2db";

    [Theory]
    [InlineData(null, ShaB)]   // the Set recorded nothing
    [InlineData(ShaA, null)]   // the book never came from a file
    [InlineData(null, null)]   // neither
    public void A_null_on_either_side_cannot_tell_and_so_does_not_warn(string? recorded, string? book) =>
        Assert.Null(SetProvenance.Warning(recorded, book));

    [Fact]
    public void The_same_hash_does_not_warn() => Assert.Null(SetProvenance.Warning(ShaA, ShaA));

    /// <summary><c>set.json</c> is plain JSON a person can edit, and an uppercase copy of a hash is
    /// the same hash — a warning over letter case would be pure noise.</summary>
    [Fact]
    public void The_same_hash_in_a_different_case_does_not_warn() =>
        Assert.Null(SetProvenance.Warning(ShaA, ShaA.ToUpperInvariant()));

    [Fact]
    public void Two_different_hashes_warn_and_name_both()
    {
        string? warning = SetProvenance.Warning(ShaA, ShaB);

        Assert.NotNull(warning);
        Assert.Contains(ShaA, warning, StringComparison.Ordinal);
        Assert.Contains(ShaB, warning, StringComparison.Ordinal);

        // It must say what actually goes wrong, not merely that something differs: extending with a
        // changed book adds assets from a second generation to a collection minted from the first.
        Assert.Contains("seed", warning, StringComparison.Ordinal);
    }

    // ---- the same rule, over real files, since that is where the two hashes actually come from ----

    /// <summary>
    /// Both ends wired up: <c>SetWriter</c> stamps the source archive's hash into <c>set.json</c> at
    /// cook time, <c>CookBookArchive.Read</c> hashes the archive on the way in, and
    /// <c>ReadExisting</c> hands the stamp back for the comparison. Cooking and re-reading the SAME
    /// book must be silent.
    /// </summary>
    [Fact]
    public void A_set_cooked_from_a_book_does_not_warn_about_that_same_book()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string cbk = Path.Combine(tmp.FullName, "book.cbk");
            WriteBook(cbk, ["body", "hat"]);
            string setDir = Path.Combine(tmp.FullName, "set");

            using (var book = CookBookArchive.Read(cbk))
            using (var set = Generator.Generate(book, new GenerateOptions(2, "s")))
                SetWriter.Write(set, setDir, pack: false);

            using var reopened = CookBookArchive.Read(cbk);
            var existing = SetWriter.ReadExisting(setDir);

            Assert.Equal(reopened.SourceSha256, existing.CookbookSha256);
            Assert.Null(SetProvenance.Warning(existing.CookbookSha256, reopened.SourceSha256));
        }
        finally { tmp.Delete(recursive: true); }
    }

    /// <summary>
    /// The case the warning exists for: the same recipe with its layers reordered is a different
    /// archive, and — because reordering moves which draw reaches which layer — a different
    /// collection. Nothing but the hash records that.
    /// </summary>
    [Fact]
    public void A_reordered_book_warns_against_a_set_cooked_from_the_original()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string original = Path.Combine(tmp.FullName, "book.cbk");
            string reordered = Path.Combine(tmp.FullName, "reordered.cbk");
            WriteBook(original, ["body", "hat"]);
            WriteBook(reordered, ["hat", "body"]);
            string setDir = Path.Combine(tmp.FullName, "set");

            using (var book = CookBookArchive.Read(original))
            using (var set = Generator.Generate(book, new GenerateOptions(2, "s")))
                SetWriter.Write(set, setDir, pack: false);

            using var other = CookBookArchive.Read(reordered);
            var existing = SetWriter.ReadExisting(setDir);

            Assert.NotNull(SetProvenance.Warning(existing.CookbookSha256, other.SourceSha256));
        }
        finally { tmp.Delete(recursive: true); }
    }

    /// <summary>
    /// The stamp is read best-effort, because it only decorates the run. Extend's real inputs are the
    /// per-item files; a <c>set.json</c> that is missing or unparseable must cost the warning and
    /// nothing else, or a cosmetic check would start failing operations.
    /// </summary>
    [Fact]
    public void An_unreadable_set_json_costs_the_warning_and_not_the_extend()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string cbk = Path.Combine(tmp.FullName, "book.cbk");
            WriteBook(cbk, ["body", "hat"]);
            string setDir = Path.Combine(tmp.FullName, "set");

            using (var book = CookBookArchive.Read(cbk))
            using (var set = Generator.Generate(book, new GenerateOptions(2, "s")))
                SetWriter.Write(set, setDir, pack: false);

            File.WriteAllText(Path.Combine(setDir, "set.json"), "{ this is not json");

            var existing = SetWriter.ReadExisting(setDir);

            Assert.Null(existing.CookbookSha256);        // cannot tell
            Assert.Equal(2, existing.Dnas.Count);        // but the extend inputs still read
            Assert.Equal(3, existing.NextNumber);
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void A_folder_that_is_not_a_set_yet_records_nothing_rather_than_failing()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            var existing = SetWriter.ReadExisting(tmp.FullName);

            Assert.Null(existing.CookbookSha256);
            Assert.Empty(existing.Dnas);
            Assert.Equal(1, existing.NextNumber);
        }
        finally { tmp.Delete(recursive: true); }
    }

    /// <summary>
    /// <b>The warning must survive being heeded — or ignored.</b> Extending stamps a fresh
    /// <c>set.json</c>, and it used to stamp the hash of the book in hand, so the very first mismatched
    /// extend erased the record it had just warned about. The collection was then genuinely mixed while
    /// claiming a single origin: the second book, which had minted the <i>fewest</i> of its assets.
    ///
    /// <para>Both halves of the damage are asserted, because the second is the one that misleads
    /// rather than merely forgets: a further extend with the wrong book fell silent, and one with the
    /// <i>right</i> book — the one that actually cooked the collection — started warning. The check
    /// did not just stop working, it inverted.</para>
    /// </summary>
    [Fact]
    public void Extending_with_the_wrong_book_does_not_overwrite_the_set_s_record_of_its_origin()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string original = Path.Combine(tmp.FullName, "book.cbk");
            string reordered = Path.Combine(tmp.FullName, "reordered.cbk");
            WriteBook(original, ["body", "hat"]);
            WriteBook(reordered, ["hat", "body"]);
            string setDir = Path.Combine(tmp.FullName, "set");

            string originalSha;
            using (var book = CookBookArchive.Read(original))
            {
                originalSha = book.SourceSha256!;
                using var set = Generator.Generate(book, new GenerateOptions(2, "s"));
                SetWriter.Write(set, setDir, pack: false);
            }

            // Extend with the WRONG book — the operation the warning exists to flag, done anyway.
            using (var wrong = CookBookArchive.Read(reordered))
            {
                var before = SetWriter.ReadExisting(setDir);
                using var more = Generator.Generate(wrong, new GenerateOptions(1, "s2"),
                    before.Dnas, before.NextNumber);
                SetWriter.Write(more, setDir, pack: false);
            }

            var after = SetWriter.ReadExisting(setDir);
            Assert.Equal(originalSha, after.CookbookSha256);

            // The check still works afterwards, and still points the same way.
            using var wrongAgain = CookBookArchive.Read(reordered);
            using var right = CookBookArchive.Read(original);
            Assert.NotNull(SetProvenance.Warning(after.CookbookSha256, wrongAgain.SourceSha256));
            Assert.Null(SetProvenance.Warning(after.CookbookSha256, right.SourceSha256));
        }
        finally { tmp.Delete(recursive: true); }
    }

    /// <summary>
    /// The other side of "first one wins": a Set whose origin was never recorded — cooked from an
    /// in-memory book that had no file to hash — <i>gains</i> a stamp on its first extend from a real
    /// archive. Freezing must not mean staying unknowable forever; null is an absence to fill, not a
    /// value to preserve.
    /// </summary>
    [Fact]
    public void A_set_that_recorded_no_origin_takes_one_from_the_first_book_that_extends_it()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string cbk = Path.Combine(tmp.FullName, "book.cbk");
            WriteBook(cbk, ["body", "hat"]);
            string setDir = Path.Combine(tmp.FullName, "set");

            // Read, then strip the source hash: a book that never came from a file cooks a Set with
            // nothing to record, which is exactly the SourceSha256-is-null case.
            using (var onDisk = CookBookArchive.Read(cbk))
            {
                var inMemory = new LoadedCookBook
                {
                    Manifest = onDisk.Manifest,
                    Recipes = onDisk.Recipes,
                    SourceSha256 = null,
                };
                using var set = Generator.Generate(inMemory, new GenerateOptions(2, "s"));
                SetWriter.Write(set, setDir, pack: false);
            }

            Assert.Null(SetWriter.ReadExisting(setDir).CookbookSha256);

            using var book = CookBookArchive.Read(cbk);
            var before = SetWriter.ReadExisting(setDir);
            using (var more = Generator.Generate(book, new GenerateOptions(1, "s2"),
                       before.Dnas, before.NextNumber))
                SetWriter.Write(more, setDir, pack: false);

            Assert.Equal(book.SourceSha256, SetWriter.ReadExisting(setDir).CookbookSha256);
        }
        finally { tmp.Delete(recursive: true); }
    }

    /// <summary>A 1x1 two-layer book, written to <paramref name="path"/> with the given layer order —
    /// the whole point being that two orders of the same layers are two different archives.</summary>
    private static void WriteBook(string path, IReadOnlyList<string> layerOrder)
    {
        var ingredients = layerOrder.Select(id => new LoadedIngredient
        {
            Manifest = new IngredientManifest(id, id, LayerKind.Custom, null,
                [new Variant("a", "A", 1), new Variant("b", "B", 1)]),
            VariantImages = new Dictionary<string, Image<Rgba32>>(StringComparer.Ordinal)
            {
                ["a"] = new(1, 1, new Rgba32(10, 20, 30, 255)),
                ["b"] = new(1, 1, new Rgba32(40, 50, 60, 255)),
            },
        }).ToList();

        try
        {
            var recipe = new LoadedRecipe
            {
                Manifest = new RecipeManifest("cat", "Cat", layerOrder, []),
                Ingredients = ingredients,
            };
            var manifest = new CookBookManifest("cb", "Book", new Dimensions(1, 1),
                new Collection("Book", "provenance fixture", "BK"),
                new Dictionary<string, double> { ["cat"] = 1 });

            CookBookArchive.Write(path, manifest, [recipe]);
        }
        finally { foreach (var ing in ingredients) ing.Dispose(); }
    }
}
