using Nfty.Core.Demo;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Imaging;
using Nfty.Core.Model;

namespace Nfty.Model.Tests;

/// <summary>The Chest Demo rises into solid, believable objects, on both recipes.</summary>
public class DemoModelsTests
{
    private static LoadedCookBook Read()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        return CookBookArchive.Read(DemoCookBook.WriteTo(dir));
    }

    private static string BodyId(LoadedRecipe r) =>
        r.Ingredients.Single(i => i.Manifest.Id.EndsWith("body", StringComparison.Ordinal)).Manifest.Id;

    private static LoadedIngredient Layer(LoadedRecipe r, string id) => r.Ingredients.Single(i => i.Manifest.Id == id);

    [Fact]
    public void Every_overlay_stands_proud_of_every_body_on_both_recipes()
    {
        using var book = Read();
        int checkedPixels = 0;
        foreach (var recipe in book.Recipes)
        {
            string bodyId = BodyId(recipe);
            string feet = Layer(recipe, "feet").Manifest.Variants[0].Id;
            foreach (var body in Layer(recipe, bodyId).Manifest.Variants)
                foreach (var overlayId in new[] { "bands", "lock", "trim" })
                {
                    var overlay = Layer(recipe, overlayId);
                    foreach (var variant in overlay.Manifest.Variants)
                    {
                        var without = StackDepth.ForSelection(recipe, [("feet", feet), (bodyId, body.Id)]);
                        var with = StackDepth.ForSelection(recipe,
                            [("feet", feet), (bodyId, body.Id), (overlayId, variant.Id)]);
                        var art = overlay.VariantImages[variant.Id];
                        for (int y = 0; y < art.Height; y++)
                            for (int x = 0; x < art.Width; x++)
                            {
                                if (!Opacity.IsSolid(art[x, y])) continue;
                                checkedPixels++;
                                Assert.True(with.Front(x, y) > without.Front(x, y),
                                    $"{recipe.Manifest.Id}: {overlayId}/{variant.Id} on {bodyId}/{body.Id} "
                                    + $"is not proud at ({x},{y}): {with.Front(x, y)} vs {without.Front(x, y)}");
                            }
                    }
                }
        }
        Assert.True(checkedPixels > 1000, "the cross product walked no pixels");
    }

    [Fact]
    public void A_body_alone_still_rises()
    {
        using var book = Read();
        foreach (var recipe in book.Recipes)
        {
            string bodyId = BodyId(recipe);
            var ing = Layer(recipe, bodyId);
            foreach (var v in ing.Manifest.Variants)
            {
                var depth = StackDepth.ForSelection(recipe, [(bodyId, v.Id)]);
                Assert.NotNull(Riser.TryBuild(ing.VariantImages[v.Id], depth, new RiseOptions(), "x"));
            }
        }
    }

    [Fact]
    public void A_cooked_demo_asset_is_not_flat()
    {
        using var book = Read();
        using var set = Generator.Generate(book, new GenerateOptions(Count: 40, Seed: "explore"));
        var modeler = new SetModeler(book, new RiseOptions { Format = ModelFormat.Glb });
        bool anyDeeperThanBody = false;
        foreach (var asset in set.Assets)
        {
            var files = modeler.Model(asset, "m");
            Assert.NotNull(files);   // every demo asset has a solid pixel
            var (json, _) = GltfValidator.ReadGlb(files!.Single().Bytes);
            int acc = json["meshes"]![0]!["primitives"]![0]!["attributes"]!["POSITION"]!.GetValue<int>();
            var a = json["accessors"]![acc]!;
            float extent = a["max"]![2]!.GetValue<float>() - a["min"]![2]!.GetValue<float>();
            Assert.True(extent >= 12f - 1e-4f, $"asset {asset.SetNumber}: Z extent {extent} < 12");
            // Body-only: paper 1 + back 9 + highest body front 3 = 13. Past that, an overlay rose.
            if (extent > 13f + 1e-4f) anyDeeperThanBody = true;
        }
        Assert.True(anyDeeperThanBody, "no cooked asset had an overlay proud of its body");
    }
}
