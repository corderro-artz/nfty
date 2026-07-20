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
}
