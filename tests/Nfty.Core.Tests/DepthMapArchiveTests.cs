using System.IO.Compression;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>
/// Depth maps inside a <c>.igt</c>: written as <c>depth/{variantId}.dep</c> only when a variant
/// carries one and it is not flat, zeroed over any transparent pixel first, refused at read when
/// its length disagrees with its variant image, and carried through nesting in a <c>.rcp</c> and
/// a <c>.cbk</c>.
/// </summary>
public class DepthMapArchiveTests
{
    private static string Dir() => Directory.CreateTempSubdirectory().FullName;

    private static Image<Rgba32> AllSolid(int w, int h) => new(w, h, new Rgba32(10, 20, 30, 255));

    private static IngredientManifest OneVariantManifest(string ingId = "ing", string variantId = "v") =>
        new(ingId, "Ing", LayerKind.Custom, null, new[] { new Variant(variantId, "V", 1) });

    private static IReadOnlyDictionary<string, Image<Rgba32>> OneImage(Image<Rgba32> img, string variantId = "v") =>
        new Dictionary<string, Image<Rgba32>>(StringComparer.Ordinal) { [variantId] = img };

    private static IReadOnlyDictionary<string, DepthMap> OneDepth(DepthMap map, string variantId = "v") =>
        new Dictionary<string, DepthMap>(StringComparer.Ordinal) { [variantId] = map };

    [Fact]
    public void A_depth_map_round_trips_through_an_igt()
    {
        var dir = Dir();
        try
        {
            var path = Path.Combine(dir, "ing.igt");
            var manifest = OneVariantManifest();
            using var img = AllSolid(2, 2);
            var map = DepthMap.Flat(2, 2).With(1, 0, front: 3, back: 4);

            IngredientArchive.Write(path, manifest, OneImage(img), OneDepth(map));

            using var loaded = IngredientArchive.Read(path);
            Assert.True(loaded.Depths.ContainsKey("v"));
            Assert.Equal(map.ToBytes(), loaded.Depths["v"].ToBytes());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_flat_map_is_not_written_at_all()
    {
        var dir = Dir();
        try
        {
            var path = Path.Combine(dir, "ing.igt");
            var manifest = OneVariantManifest();
            using var img = AllSolid(2, 2);
            var flat = DepthMap.Flat(2, 2);

            IngredientArchive.Write(path, manifest, OneImage(img), OneDepth(flat));

            using (var zip = ZipFile.OpenRead(path))
                Assert.DoesNotContain(zip.Entries, e => e.FullName.StartsWith("depth/", StringComparison.Ordinal));

            using var loaded = IngredientArchive.Read(path);
            Assert.False(loaded.Depths.ContainsKey("v"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Depth_over_a_transparent_pixel_is_zeroed_on_write()
    {
        var dir = Dir();
        try
        {
            var path = Path.Combine(dir, "ing.igt");
            var manifest = OneVariantManifest();
            using var img = new Image<Rgba32>(2, 2, new Rgba32(10, 20, 30, 255));
            img[0, 0] = new Rgba32(10, 20, 30, 0);
            var map = DepthMap.Flat(2, 2).With(0, 0, front: 9, back: 9).With(1, 1, front: 5, back: 6);

            IngredientArchive.Write(path, manifest, OneImage(img), OneDepth(map));

            using var loaded = IngredientArchive.Read(path);
            var result = loaded.Depths["v"];
            Assert.Equal(0, result.Front(0, 0));
            Assert.Equal(0, result.Back(0, 0));
            Assert.Equal(5, result.Front(1, 1));
            Assert.Equal(6, result.Back(1, 1));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_map_that_is_only_depth_over_transparency_is_not_written()
    {
        var dir = Dir();
        try
        {
            var path = Path.Combine(dir, "ing.igt");
            var manifest = OneVariantManifest();
            using var img = new Image<Rgba32>(2, 2, new Rgba32(10, 20, 30, 255));
            img[0, 0] = new Rgba32(10, 20, 30, 0);
            var map = DepthMap.Flat(2, 2).With(0, 0, front: 9, back: 9);

            IngredientArchive.Write(path, manifest, OneImage(img), OneDepth(map));

            using (var zip = ZipFile.OpenRead(path))
                Assert.DoesNotContain(zip.Entries, e => e.FullName.StartsWith("depth/", StringComparison.Ordinal));

            using var loaded = IngredientArchive.Read(path);
            Assert.False(loaded.Depths.ContainsKey("v"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_depth_map_of_the_wrong_length_is_refused_naming_the_variant()
    {
        var dir = Dir();
        try
        {
            var path = Path.Combine(dir, "ing.igt");
            var manifest = OneVariantManifest("ing", "v");
            using (var img = AllSolid(2, 2))
                IngredientArchive.Write(path, manifest, OneImage(img), DepthMaps.None);

            using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
            {
                var entry = zip.CreateEntry("depth/v.dep");
                using var s = entry.Open();
                s.Write(new byte[] { 1, 2, 3 }, 0, 3);
            }

            var ex = Assert.Throws<InvalidDataException>(() => IngredientArchive.Read(path));
            Assert.Contains("ing", ex.Message);
            Assert.Contains("v", ex.Message);
            Assert.Contains("3", ex.Message);
            Assert.Contains("8", ex.Message);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Depth_survives_nesting_in_a_recipe_and_a_cookbook()
    {
        var dir = Dir();
        try
        {
            var path = Path.Combine(dir, "book.cbk");
            var manifest = OneVariantManifest("ing", "v");
            var img = AllSolid(2, 2);
            var map = DepthMap.Flat(2, 2).With(1, 0, front: 3, back: 4);
            var ing = new LoadedIngredient
            {
                Manifest = manifest,
                VariantImages = OneImage(img),
                Depths = OneDepth(map),
            };
            var recipe = new LoadedRecipe
            {
                Manifest = new RecipeManifest("r", "R", new[] { "ing" }, Array.Empty<IncompatibilityRule>()),
                Ingredients = new[] { ing },
            };
            using (recipe)
                CookBookArchive.Write(path,
                    new CookBookManifest("book", "Book", new Dimensions(2, 2),
                        new Collection("Book", "desc", "BOOK"),
                        new Dictionary<string, double> { ["r"] = 1 }),
                    new[] { recipe });

            using var loaded = CookBookArchive.Read(path);
            var loadedMap = loaded.Recipes[0].Ingredients[0].Depths["v"];
            Assert.Equal(map.ToBytes(), loadedMap.ToBytes());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task A_depth_map_round_trips_through_an_igt_async()
    {
        var dir = Dir();
        try
        {
            var path = Path.Combine(dir, "ing.igt");
            var manifest = OneVariantManifest();
            using var img = AllSolid(2, 2);
            var map = DepthMap.Flat(2, 2).With(1, 0, front: 3, back: 4);

            await IngredientArchive.WriteAsync(path, manifest, OneImage(img), OneDepth(map));

            using var loaded = await IngredientArchive.ReadAsync(path);
            Assert.Equal(map.ToBytes(), loaded.Depths["v"].ToBytes());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task A_depth_map_of_the_wrong_length_is_refused_naming_the_variant_async()
    {
        var dir = Dir();
        try
        {
            var path = Path.Combine(dir, "ing.igt");
            var manifest = OneVariantManifest("ing", "v");
            using (var img = AllSolid(2, 2))
                await IngredientArchive.WriteAsync(path, manifest, OneImage(img), DepthMaps.None);

            using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
            {
                var entry = zip.CreateEntry("depth/v.dep");
                using var s = entry.Open();
                s.Write(new byte[] { 1, 2, 3 }, 0, 3);
            }

            var ex = await Assert.ThrowsAsync<InvalidDataException>(() => IngredientArchive.ReadAsync(path));
            Assert.Contains("ing", ex.Message);
            Assert.Contains("v", ex.Message);
            Assert.Contains("3", ex.Message);
            Assert.Contains("8", ex.Message);
        }
        finally { Directory.Delete(dir, true); }
    }
}
