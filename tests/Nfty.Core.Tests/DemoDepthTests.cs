using Nfty.Core.Demo;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using Nfty.Core.Stats;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>
/// The Chest Demo's hand-designed depth, checked against the embedded bytes.
/// </summary>
/// <remarks>
/// Like <see cref="DemoCookBookTests"/> these read the EMBEDDED archive, never <c>tools/demo</c>:
/// the thing that ships is the thing under test.
/// </remarks>
public class DemoDepthTests
{
    private static readonly string[] Volumetric = ["chestbody", "boxbody", "bands", "lock", "trim", "feet"];

    private static LoadedCookBook Read()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try { return CookBookArchive.Read(DemoCookBook.WriteTo(dir)); }
        finally { Directory.Delete(dir, true); }   // every image is decoded eagerly; nothing reads the file again
    }

    private static IEnumerable<LoadedIngredient> AllIngredients(LoadedCookBook book) =>
        book.Recipes.SelectMany(r => r.Ingredients);

    [Fact]
    public void Every_demo_variant_is_voxel_ready()
    {
        // A regression pin, not a red-first test: the art was already fully opaque before depth
        // arrived and must stay so, because partial alpha is the one thing a voxel model cannot
        // resolve (Riser refuses it by name).
        using var book = Read();
        foreach (var recipe in book.Recipes)
            Assert.All(VoxelReport.Scan(recipe), row => Assert.Equal(0, row.Partial));
        Assert.False(book.Manifest.PartialAlpha);
    }

    [Fact]
    public void The_demo_carries_depth_on_every_layer_that_has_volume()
    {
        using var book = Read();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ing in AllIngredients(book))
        {
            string id = ing.Manifest.Id;
            seen.Add(id);
            if (id == "glow")
            {
                Assert.Empty(ing.Depths);   // flat on purpose: the glow is light, not a thing
                continue;
            }
            Assert.Contains(id, Volumetric);
            foreach (var v in ing.Manifest.Variants)
            {
                Assert.True(ing.Depths.TryGetValue(v.Id, out var map), $"{id}/{v.Id} has no depth");
                Assert.False(map!.IsFlat, $"{id}/{v.Id} depth is flat");
            }
        }
        Assert.True(seen.IsSupersetOf(Volumetric), "a volumetric layer is missing from the demo");
        Assert.Contains("glow", seen);
    }

    [Fact]
    public void Depth_exists_only_where_something_is_solid()
    {
        using var book = Read();
        foreach (var ing in AllIngredients(book))
            foreach (var (vid, map) in ing.Depths)
            {
                var img = ing.VariantImages[vid];
                for (int y = 0; y < map.Height; y++)
                    for (int x = 0; x < map.Width; x++)
                        if (map.Front(x, y) != 0 || map.Back(x, y) != 0)
                            Assert.True(Opacity.IsSolid(img[x, y]),
                                $"{ing.Manifest.Id}/{vid}: depth under a clear pixel at ({x},{y})");
            }
    }

    [Fact]
    public void Depth_never_touches_the_collection()
    {
        // Depth is a second reading of the art and must not move a roll or a pixel: cook the book
        // as shipped and a twin whose every depth map is stripped, and the collections must match.
        using var book = Read();
        var options = new GenerateOptions(Count: 60, Seed: "depth-is-not-dna");
        using var withDepth = Generator.Generate(book, options);

        var twin = new LoadedCookBook
        {
            Manifest = book.Manifest,
            SourceSha256 = book.SourceSha256,
            Recipes = book.Recipes.Select(r => new LoadedRecipe
            {
                Manifest = r.Manifest,
                Ingredients = r.Ingredients.Select(i => new LoadedIngredient
                {
                    Manifest = i.Manifest,
                    VariantImages = i.VariantImages,   // shared with `book`; only `book` disposes
                    Depths = DepthMaps.None,
                }).ToList(),
            }).ToList(),
        };
        using var withoutDepth = Generator.Generate(twin, options);

        Assert.Equal(withDepth.Assets.Select(a => a.Dna), withoutDepth.Assets.Select(a => a.Dna));
        for (int i = 0; i < withDepth.Assets.Count; i++)
            Assert.Equal(Bytes(withDepth.Assets[i].Image), Bytes(withoutDepth.Assets[i].Image));
    }

    private static byte[] Bytes(Image<Rgba32> image)
    {
        var raw = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(raw);
        return raw;
    }
}
