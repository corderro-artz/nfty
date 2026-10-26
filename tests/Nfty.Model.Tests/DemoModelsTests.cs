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
                        using var withoutRise = StackDepth.ForSelection(recipe, [("feet", feet), (bodyId, body.Id)]);
                        using var withRise = StackDepth.ForSelection(recipe,
                            [("feet", feet), (bodyId, body.Id), (overlayId, variant.Id)]);
                        var without = withoutRise.Depth;
                        var with = withRise.Depth;
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
                using var rise = StackDepth.ForSelection(recipe, [(bodyId, v.Id)]);
                var depth = rise.Depth;
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
            // Body-only: paper 1 + deepest body back 10 (an overhang row) + front 3 = 14. Past that, an overlay rose.
            if (extent > 14f + 1e-4f) anyDeeperThanBody = true;
        }
        Assert.True(anyDeeperThanBody, "no cooked asset had an overlay proud of its body");
    }

    [Fact]
    public void Overlays_never_change_the_body_back()
    {
        using var book = Read();
        int checkedPixels = 0;
        foreach (var recipe in book.Recipes)
        {
            string bodyId = BodyId(recipe);
            foreach (var body in Layer(recipe, bodyId).Manifest.Variants)
                foreach (var overlayId in new[] { "bands", "lock", "trim" })
                {
                    var overlay = Layer(recipe, overlayId);
                    foreach (var variant in overlay.Manifest.Variants)
                    {
                        using var without = StackDepth.ForSelection(recipe, [(bodyId, body.Id)]);
                        using var with = StackDepth.ForSelection(recipe, [(bodyId, body.Id), (overlayId, variant.Id)]);
                        var art = overlay.VariantImages[variant.Id];
                        for (int y = 0; y < art.Height; y++)
                            for (int x = 0; x < art.Width; x++)
                            {
                                if (!Opacity.IsSolid(art[x, y])) continue;
                                checkedPixels++;
                                Assert.True(with.Depth.Back(x, y) == without.Depth.Back(x, y),
                                    $"{recipe.Manifest.Id}: {overlayId}/{variant.Id} changed the {body.Id} back at ({x},{y})");
                            }
                    }
                }
        }
        Assert.True(checkedPixels > 1000, "the cross product walked no pixels");
    }

    [Fact]
    public void The_chest_lid_curves_on_its_back()
    {
        using var book = Read();
        var recipe = book.Recipes.First(r => BodyId(r) == "chestbody");
        var ing = Layer(recipe, "chestbody");
        foreach (var v in ing.Manifest.Variants)
        {
            using var rise = StackDepth.ForSelection(recipe, [("chestbody", v.Id)]);
            var art = ing.VariantImages[v.Id];
            var seen = new HashSet<int>();
            int previous = -1;
            for (int y = 7; y <= 11; y++)
            {
                var solid = Enumerable.Range(0, art.Width).Where(x => Opacity.IsSolid(art[x, y])).ToList();
                int mid = (solid[0] + solid[^1]) / 2;
                int center = rise.Depth.Back(mid, y);
                Assert.True(center >= previous, $"{v.Id}: back falls going down at row {y}");
                previous = center;
                seen.Add(center);
                Assert.True(rise.Depth.Back(solid[0], y) < center, $"{v.Id}: row {y} left end is not rounded");
                Assert.True(rise.Depth.Back(solid[^1], y) < center, $"{v.Id}: row {y} right end is not rounded");
            }
            Assert.True(seen.Count >= 3, $"{v.Id}: the lid back has {seen.Count} distinct depths");
        }
    }

    [Fact]
    public void The_lower_body_back_mirrors_its_front()
    {
        using var book = Read();
        foreach (var recipe in book.Recipes)
        {
            string bodyId = BodyId(recipe);
            var ing = Layer(recipe, bodyId);
            foreach (var v in ing.Manifest.Variants)
            {
                using var rise = StackDepth.ForSelection(recipe, [(bodyId, v.Id)]);
                var art = ing.VariantImages[v.Id];
                var d = rise.Depth;
                int overhangChecks = 0;
                for (int y = 12; y < art.Height; y++)
                {
                    var solid = Enumerable.Range(0, art.Width).Where(x => Opacity.IsSolid(art[x, y])).ToList();
                    if (solid.Count < 3) continue;
                    Assert.True(d.Back(solid[0], y) < d.Back(solid[0] + 1, y), $"{bodyId}/{v.Id}: row {y} left column is not bevelled");
                    Assert.True(d.Back(solid[^1], y) < d.Back(solid[^1] - 1, y), $"{bodyId}/{v.Id}: row {y} right column is not bevelled");
                }
                // Overhang rows (the lid course and the plinth) are deeper than the face at the same x.
                foreach (int overhang in new[] { 12, 14, 28 })
                    foreach (int face in new[] { 17, 20, 24 })
                    {
                        Assert.True(Opacity.IsSolid(art[16, overhang]) && Opacity.IsSolid(art[16, face]));
                        Assert.True(d.Back(16, overhang) > d.Back(16, face), $"{bodyId}/{v.Id}: row {overhang} is not deeper than row {face}");
                        overhangChecks++;
                    }
                Assert.Equal(9, overhangChecks);
            }
        }
    }

    [Fact]
    public void A_cooked_demo_asset_is_whole_from_behind()
    {
        using var book = Read();
        using var set = Generator.Generate(book, new GenerateOptions(Count: 40, Seed: "behind"));
        var recipes = book.Recipes.ToDictionary(r => r.Manifest.Id);
        int overlayPixels = 0;
        foreach (var asset in set.Assets)
        {
            var recipe = recipes[asset.RecipeId];
            string bodyId = BodyId(recipe);
            string bodyVariant = asset.Traits.Single(t => t.IngredientId == bodyId).VariantId;
            var bodyArt = Layer(recipe, bodyId).VariantImages[bodyVariant];
            using var rise = StackDepth.ForAsset(recipe, asset)!;
            double volume = 0;
            for (int y = 0; y < asset.Image.Height; y++)
                for (int x = 0; x < asset.Image.Width; x++)
                    if (Opacity.IsSolid(asset.Image[x, y]))
                        volume += 1 + rise.Depth.Front(x, y) + rise.Depth.Back(x, y);
            foreach (var t in asset.Traits.Where(t => t.IngredientId is "bands" or "lock" or "trim"))
            {
                var art = Layer(recipe, t.IngredientId).VariantImages[t.VariantId];
                for (int y = 0; y < art.Height; y++)
                    for (int x = 0; x < art.Width; x++)
                        if (Opacity.IsSolid(art[x, y]) && Opacity.IsSolid(bodyArt[x, y]))
                        {
                            overlayPixels++;
                            Assert.True(rise.Depth.Back(x, y) >= 1, $"asset {asset.SetNumber}: {t.IngredientId} pixel ({x},{y}) is hollow behind");
                        }
            }
            var mesh = Riser.TryBuild(asset.Image, rise.Depth, new RiseOptions(), "x", rise.Faces);
            Assert.NotNull(mesh);
            Assert.Equal(volume, MeshChecks.Volume(mesh!));
        }
        Assert.True(overlayPixels > 100, "no overlay pixel was checked");
    }
}
