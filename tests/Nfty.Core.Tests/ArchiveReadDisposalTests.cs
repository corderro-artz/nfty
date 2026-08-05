using System.IO.Compression;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Diagnostics;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>
/// Reading an archive eagerly decodes every variant PNG (see Loaded.cs / CLAUDE.md "Callers own
/// image disposal"). If a later entry (a sibling variant, ingredient, or recipe) fails to load,
/// everything decoded before it has no handle any caller can reach — the reader itself must
/// dispose it before the exception propagates, or it leaks silently on every retry.
///
/// Asserted via ImageSharp's own leak counter,
/// <see cref="MemoryDiagnostics.TotalUndisposedAllocationCount"/>: a process-wide count of
/// outstanding image allocations, incremented by every <c>Image&lt;T&gt;</c> construct/clone/load
/// and decremented by every <c>Dispose</c>. It is exact and not GC/finalizer-timing dependent,
/// which is what lets a before/after snapshot around one failing read prove disposal actually
/// happened rather than merely being plausible. Because it is a single process-wide counter,
/// this assembly disables xUnit's cross-class test parallelization
/// (see <c>AssemblyInfo.cs</c>) so no unrelated test's image churn can land inside the
/// snapshot window and produce a false positive/negative.
/// </summary>
public class ArchiveReadDisposalTests
{
    private static string TempPath(string name) =>
        Path.Combine(Directory.CreateTempSubdirectory().FullName, name);

    /// <summary>Walks up from the test binary to the directory holding nfty.sln.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "nfty.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    // ---- IngredientArchive: variant "a" decodes fine, variant "b"'s PNG bytes are garbage ----

    private static void WriteIngredientWithCorruptSecondVariant(string path)
    {
        var manifest = new IngredientManifest("bg", "Background", LayerKind.Custom, null,
            new[] { new Variant("a", "A", 1), new Variant("b", "B", 1) });
        using var imgA = new Image<Rgba32>(2, 2, new Rgba32(1, 2, 3, 255));
        using var imgB = new Image<Rgba32>(2, 2, new Rgba32(4, 5, 6, 255));
        IngredientArchive.Write(path, manifest,
            new Dictionary<string, Image<Rgba32>> { ["a"] = imgA, ["b"] = imgB });

        using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
        zip.GetEntry("variants/b.png")!.Delete();
        using var s = zip.CreateEntry("variants/b.png").Open();
        var junk = "not a png"u8.ToArray();
        s.Write(junk);
    }

    [Fact]
    public void Ingredient_sync_read_disposes_an_earlier_decoded_variant_when_a_later_one_fails_to_decode()
    {
        var path = TempPath("bg.igt");
        WriteIngredientWithCorruptSecondVariant(path);

        int before = MemoryDiagnostics.TotalUndisposedAllocationCount;
        Assert.ThrowsAny<Exception>(() => IngredientArchive.Read(path));
        Assert.Equal(before, MemoryDiagnostics.TotalUndisposedAllocationCount);
    }

    [Fact]
    public async Task Ingredient_async_read_disposes_an_earlier_decoded_variant_when_a_later_one_fails_to_decode()
    {
        var path = TempPath("bg.igt");
        WriteIngredientWithCorruptSecondVariant(path);

        int before = MemoryDiagnostics.TotalUndisposedAllocationCount;
        await Assert.ThrowsAnyAsync<Exception>(() => IngredientArchive.ReadAsync(path));
        Assert.Equal(before, MemoryDiagnostics.TotalUndisposedAllocationCount);
    }

    // ---- RecipeArchive: ingredient "ing1" loads fully, "ing2" has a duplicate variant id -------
    // (rejected by IngredientArchive.EnsureUniqueVariantIds before it decodes any image of its
    // own — so this exercises the sibling boundary, not the inner-variant boundary above.)

    private static LoadedIngredient GoodIngredient(string id) => new()
    {
        Manifest = new IngredientManifest(id, id, LayerKind.Custom, null, new[] { new Variant("v", "V", 1) }),
        VariantImages = new Dictionary<string, Image<Rgba32>> { ["v"] = new Image<Rgba32>(2, 2, new Rgba32(1, 2, 3, 255)) },
    };

    private static LoadedIngredient DuplicateIdIngredient(string id) => new()
    {
        Manifest = new IngredientManifest(id, id, LayerKind.Custom, null,
            new[] { new Variant("x", "X", 1), new Variant("x", "X again", 1) }),
        VariantImages = new Dictionary<string, Image<Rgba32>> { ["x"] = new Image<Rgba32>(2, 2, new Rgba32(9, 9, 9, 255)) },
    };

    private static void WriteRecipeWithMalformedSecondIngredient(string path)
    {
        var good = GoodIngredient("ing1");
        var bad = DuplicateIdIngredient("ing2");
        RecipeArchive.Write(path,
            new RecipeManifest("cat", "Cat", new[] { "ing1", "ing2" }, Array.Empty<IncompatibilityRule>()),
            new[] { good, bad });
        good.Dispose();
        bad.Dispose();
    }

    [Fact]
    public void Recipe_sync_read_disposes_an_earlier_fully_loaded_ingredient_when_a_later_one_is_malformed()
    {
        var path = TempPath("cat.rcp");
        WriteRecipeWithMalformedSecondIngredient(path);

        int before = MemoryDiagnostics.TotalUndisposedAllocationCount;
        var ex = Assert.Throws<InvalidDataException>(() => RecipeArchive.Read(path));
        Assert.Contains("duplicate variant id", ex.Message);
        Assert.Equal(before, MemoryDiagnostics.TotalUndisposedAllocationCount);
    }

    [Fact]
    public async Task Recipe_async_read_disposes_an_earlier_fully_loaded_ingredient_when_a_later_one_is_malformed()
    {
        var path = TempPath("cat.rcp");
        WriteRecipeWithMalformedSecondIngredient(path);

        int before = MemoryDiagnostics.TotalUndisposedAllocationCount;
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => RecipeArchive.ReadAsync(path));
        Assert.Contains("duplicate variant id", ex.Message);
        Assert.Equal(before, MemoryDiagnostics.TotalUndisposedAllocationCount);
    }

    // ---- CookBookArchive: recipe "r1" loads fully, "r2" contains the same malformed ingredient --

    private static void WriteCookBookWithMalformedSecondRecipe(string path)
    {
        var goodRecipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("r1", "R1", new[] { "ing" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { GoodIngredient("ing") },
        };
        var badRecipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("r2", "R2", new[] { "ing" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { DuplicateIdIngredient("ing") },
        };
        var manifest = new CookBookManifest("cb", "VaporPets", new Dimensions(2, 2),
            new Collection("VaporPets", "d", "VP"),
            new Dictionary<string, double> { ["r1"] = 1, ["r2"] = 1 });

        CookBookArchive.Write(path, manifest, new[] { goodRecipe, badRecipe });
        goodRecipe.Dispose();
        badRecipe.Dispose();
    }

    [Fact]
    public void CookBook_sync_read_disposes_an_earlier_fully_loaded_recipe_when_a_later_one_is_malformed()
    {
        var path = TempPath("cb.cbk");
        WriteCookBookWithMalformedSecondRecipe(path);

        int before = MemoryDiagnostics.TotalUndisposedAllocationCount;
        var ex = Assert.Throws<InvalidDataException>(() => CookBookArchive.Read(path));
        Assert.Contains("duplicate variant id", ex.Message);
        Assert.Equal(before, MemoryDiagnostics.TotalUndisposedAllocationCount);
    }

    [Fact]
    public async Task CookBook_async_read_disposes_an_earlier_fully_loaded_recipe_when_a_later_one_is_malformed()
    {
        var path = TempPath("cb.cbk");
        WriteCookBookWithMalformedSecondRecipe(path);

        int before = MemoryDiagnostics.TotalUndisposedAllocationCount;
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => CookBookArchive.ReadAsync(path));
        Assert.Contains("duplicate variant id", ex.Message);
        Assert.Equal(before, MemoryDiagnostics.TotalUndisposedAllocationCount);
    }

    // ---- the source hash: every recipe loads, and the step AFTER them fails --------------------

    private static void WriteIntactCookBook(string path)
    {
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("r1", "R1", new[] { "ing" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { GoodIngredient("ing") },
        };
        var manifest = new CookBookManifest("cb", "VaporPets", new Dimensions(2, 2),
            new Collection("VaporPets", "d", "VP"),
            new Dictionary<string, double> { ["r1"] = 1 });

        CookBookArchive.Write(path, manifest, new[] { recipe });
        recipe.Dispose();

        // Pad the file with incompressible bytes so hashing it takes appreciably longer than
        // decoding its one 2x2 variant. Without this, a cancellation aimed at the hash almost always
        // arrives after the whole read has finished and the test proves nothing.
        using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
        using var s = zip.CreateEntry("padding.bin", CompressionLevel.NoCompression).Open();
        var noise = new byte[4 * 1024 * 1024];
        new Random(1234).NextBytes(noise);
        s.Write(noise);
    }

    /// <summary>
    /// The failure the other cases in this file could not reach: nothing in the archive is wrong, so
    /// every recipe decodes, and then the SourceSha256 step throws. That step used to sit in the
    /// object initializer after the try/catch, which meant the whole decoded tree — every variant
    /// image in the book — was stranded with no owner.
    ///
    /// <para>Cancellation is the realistic trigger and the reason this matters: ReadAsync takes a
    /// CancellationToken as a first-class input, a GUI passes one that fires when the user navigates
    /// away, and HashFileAsync reads the entire file. Forced here with an already-cancelled token so
    /// the test does not depend on winning a race.</para>
    /// </summary>
    [Fact]
    public async Task Cancelling_a_cookbook_read_never_strands_a_decoded_image()
    {
        var path = TempPath("cb.cbk");
        WriteIntactCookBook(path);

        // Sweep the cancellation across the whole read rather than aiming at one instant. Aiming is
        // a race — the read either finished first (proving nothing) or was cancelled before the
        // recipes existed (also proving nothing). Sweeping guarantees some iterations land in the
        // entry loop, some in the hash, and some after completion, and the invariant is the same
        // for all of them: whatever was decoded is disposed. `cancelled` then confirms the window
        // was actually hit, so a run where every attempt completed early fails loudly instead of
        // passing vacuously.
        int cancelled = 0, completed = 0;
        foreach (var delay in new[] { 0, 1, 2, 4, 8, 16, 24, 32, 48, 64 })
        {
            int before = MemoryDiagnostics.TotalUndisposedAllocationCount;
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(delay);
            try
            {
                using var book = await CookBookArchive.ReadAsync(path, cts.Token);
                completed++;
            }
            catch (OperationCanceledException) { cancelled++; }

            Assert.Equal(before, MemoryDiagnostics.TotalUndisposedAllocationCount);
        }

        Assert.True(cancelled > 0,
            $"no attempt was cancelled ({completed} completed), so the disposal-on-cancel path never ran");
    }

    /// <summary>
    /// The sync seam, stated structurally because it cannot be forced from outside: <c>Read</c> and
    /// <c>ReadAsync</c> reach the source hash the same way, and the hash is the one step that runs
    /// after every image is decoded and before any caller owns them. Both now compute it inside the
    /// try that disposes the recipes; this pins the shape so a refactor that moves either one back
    /// out into the object initializer is visible here.
    /// </summary>
    [Fact]
    public void Both_cookbook_readers_hash_the_source_inside_the_disposing_try()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "Nfty.Core", "Formats", "CookBookArchive.cs"));

        foreach (var reader in new[] { "public static LoadedCookBook Read(", "public static async Task<LoadedCookBook> ReadAsync(" })
        {
            int start = source.IndexOf(reader, StringComparison.Ordinal);
            Assert.True(start >= 0, $"could not find {reader}");
            int hash = source.IndexOf("SourceSha256", start, StringComparison.Ordinal);
            int catchAt = source.IndexOf("catch", start, StringComparison.Ordinal);

            Assert.True(hash < catchAt,
                $"{reader} computes SourceSha256 after its catch block, so a failing hash strands "
                + "every decoded image in the book");
        }
    }
}
