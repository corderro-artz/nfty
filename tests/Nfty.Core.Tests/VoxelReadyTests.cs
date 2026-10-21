using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

public class VoxelReadyTests
{
    private static Image<Rgba32> Row(params byte[] alphas)
    {
        var img = new Image<Rgba32>(alphas.Length, 1);
        for (int x = 0; x < alphas.Length; x++) img[x, 0] = new Rgba32(10, 20, 30, alphas[x]);
        return img;
    }

    private static LoadedIngredient Ing(string id, string name, params (string id, Image<Rgba32> img)[] variants) => new()
    {
        Manifest = new IngredientManifest(id, name, LayerKind.Custom, null,
            variants.Select(v => new Variant(v.id, v.id + "-name", 1)).ToList()),
        VariantImages = variants.ToDictionary(v => v.id, v => v.img),
    };

    private static LoadedRecipe Recipe(string id, params LoadedIngredient[] ings) => new()
    {
        Manifest = new RecipeManifest(id, id, ings.Select(i => i.Manifest.Id).ToList(),
            Array.Empty<IncompatibilityRule>()),
        Ingredients = ings,
    };

    private static LoadedCookBook Book(params LoadedRecipe[] recipes) => new()
    {
        Manifest = new CookBookManifest("cb", "Book", new Dimensions(2, 1), new Collection("Book", "d", "B"),
            recipes.ToDictionary(r => r.Manifest.Id, _ => 1.0)),
        Recipes = recipes,
    };

    // robot: eyes{e1 127,255}; cat: body{b-dirty 127,128 ; a-clean 0,255}.
    private static LoadedCookBook DirtyBook() => Book(
        Recipe("robot", Ing("eyes", "Eyes", ("e1", Row(127, 255)))),
        Recipe("cat", Ing("body", "Body", ("b-dirty", Row(127, 128)), ("a-clean", Row(0, 255)))));

    [Theory]
    [InlineData(1, 0)] [InlineData(127, 0)] [InlineData(128, 255)] [InlineData(254, 255)]
    [InlineData(0, 0)] [InlineData(255, 255)]
    public void Snaps_at_fifty_percent(byte before, byte after)
    {
        var map = new ColorMap(1, 1);
        map.Set(0, 0, new Rgba32(10, 20, 30, before));
        new EditHistory<Rgba32>().Do(new AlphaSnap<Rgba32>(), map);
        Assert.Equal(new Rgba32(10, 20, 30, after), map.Get(0, 0));
    }

    [Fact]
    public void Snapping_is_undoable_through_the_history()
    {
        var map = new ColorMap(2, 1);
        map.Set(0, 0, new Rgba32(1, 2, 3, 127)); map.Set(1, 0, new Rgba32(4, 5, 6, 128));
        var h = new EditHistory<Rgba32>();
        h.Do(new AlphaSnap<Rgba32>(), map);
        h.Undo(map);
        Assert.Equal(127, map.Get(0, 0).A); Assert.Equal(128, map.Get(1, 0).A);
    }

    [Fact]
    public void A_snap_on_clean_art_is_a_no_op_and_records_no_undo_step()
    {
        var map = new ColorMap(2, 1);
        map.Set(0, 0, new Rgba32(1, 2, 3, 0)); map.Set(1, 0, new Rgba32(4, 5, 6, 255));
        var h = new EditHistory<Rgba32>();
        h.Do(new AlphaSnap<Rgba32>(), map);
        Assert.False(h.CanUndo);
    }

    [Fact]
    public void Plan_lists_every_partial_variant_in_the_book_with_its_counts()
    {
        using var book = DirtyBook();
        var plan = VoxelReady.Plan(book);
        Assert.Equal(2, plan.Count);
        Assert.Equal(new VoxelReadyChange("cat", "body", "Body", "b-dirty", "b-dirty-name", 1, 1), plan[0]);
        Assert.Equal(new VoxelReadyChange("robot", "eyes", "Eyes", "e1", "e1-name", 0, 1), plan[1]);
    }

    [Fact]
    public void Apply_snaps_every_listed_variant_and_leaves_the_book_voxel_ready()
    {
        using var book = DirtyBook();
        using var result = VoxelReady.Apply(book);
        Assert.False(Opacity.HasPartialAlpha(result.Book));
        Assert.Equal(VoxelReady.Plan(book), result.Changes);
        var snapped = result.Book.Recipes.Single(r => r.Manifest.Id == "cat").Ingredients[0].VariantImages["b-dirty"];
        Assert.Equal(0, snapped[0, 0].A); Assert.Equal(255, snapped[1, 0].A);
    }

    [Fact]
    public void Apply_leaves_depth_maps_and_clean_variants_untouched()
    {
        var clean = Row(0, 255);
        var depth = DepthMap.FromBytes(2, 1, new byte[] { 1, 2, 3, 4 });
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("body", "Body", LayerKind.Custom, null,
                new[] { new Variant("d", "d", 1), new Variant("c", "c", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["d"] = Row(127, 255), ["c"] = clean },
            Depths = new Dictionary<string, DepthMap> { ["d"] = depth },
        };
        using var book = Book(Recipe("cat", ing));
        using var result = VoxelReady.Apply(book);
        var after = result.Book.Recipes[0].Ingredients[0];
        Assert.Same(clean, after.VariantImages["c"]);
        Assert.Same(depth, after.Depths["d"]);
    }

    [Fact]
    public void Undo_restores_the_exact_pixels()
    {
        using var book = DirtyBook();
        using var result = VoxelReady.Apply(book);
        using var restored = VoxelReady.Undo(result.Book, result);
        foreach (var r in book.Recipes)
            foreach (var i in r.Ingredients)
                foreach (var (vid, img) in i.VariantImages)
                {
                    var back = restored.Recipes.Single(x => x.Manifest.Id == r.Manifest.Id)
                        .Ingredients.Single(x => x.Manifest.Id == i.Manifest.Id).VariantImages[vid];
                    for (int x = 0; x < img.Width; x++) Assert.Equal(img[x, 0], back[x, 0]);
                }
    }

    [Fact]
    public void Undo_refuses_naming_a_variant_whose_pixels_changed_since()
    {
        using var book = DirtyBook();
        using var result = VoxelReady.Apply(book);
        var cat = result.Book.Recipes.Single(r => r.Manifest.Id == "cat").Ingredients[0];
        var edited = new LoadedIngredient
        {
            Manifest = cat.Manifest,
            VariantImages = new Dictionary<string, Image<Rgba32>>(cat.VariantImages) { ["b-dirty"] = Row(255, 255) },
        };
        var current = CookBookEdits.UpsertIngredient(result.Book, "cat", edited);
        var ex = Assert.Throws<InvalidOperationException>(() => VoxelReady.Undo(current, result));
        Assert.Contains("b-dirty-name", ex.Message);
        Assert.DoesNotContain("e1-name", ex.Message);
    }

    [Fact]
    public void Disposing_the_result_frees_the_originals()
    {
        using var book = DirtyBook();
        var result = VoxelReady.Apply(book);
        var originals = result.Originals.Values.ToList();
        Assert.NotEmpty(originals);
        result.Dispose();
        foreach (var o in originals) Assert.Throws<ObjectDisposedException>(() => o[0, 0]);
    }
}
