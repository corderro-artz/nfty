using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Rise;
using Nfty.App.Services;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public class VariantRiseSourceTests
{
    private static LoadedIngredient Body(LoadedCookBook b) =>
        b.Recipes[0].Ingredients.Single(i => i.Manifest.Id == "body");

    [AvaloniaFact]
    public async Task Save_refuses_depth_painted_on_a_picture_that_has_since_changed()
    {
        var (s, path) = RiseFixtures.OnDisk(RiseFixtures.ChestBook(withPine: true));
        using var src = RiseFixtures.Variant(s);
        foreach (var item in src.Items.Where(i => i.Id is "oak" or "pine"))
        {
            var subject = src.Open(item);
            subject.History.Do(new DepthStep(new[] { (2, 1) }, DepthSide.Front, +1), subject.Depth);
        }
        // The pixels of oak and pine change on disk through the real persistence path, after Rise opened them.
        var live = Body(s.Current!);
        var images = live.VariantImages.ToDictionary(kv => kv.Key, kv => kv.Value.Clone());
        foreach (var id in new[] { "oak", "pine" })
        {
            images[id].Dispose();
            images[id] = RiseFixtures.Picture(RiseFixtures.Gold);
        }
        var changed = new LoadedIngredient { Manifest = live.Manifest, VariantImages = images, Depths = live.Depths };
        await CookBookPersistence.PersistAsync(s, CookBookEdits.UpsertIngredient(s.Current!, "chest", changed));
        var before = File.ReadAllBytes(path);

        var ex = await Assert.ThrowsAsync<System.InvalidOperationException>(() => src.SaveAsync());

        Assert.Contains("Oak, Pine", ex.Message);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.True(src.IsDirty);                 // nothing was marked saved
        s.Dispose();
    }

    [AvaloniaFact]
    public void Items_are_the_variants_with_stored_pixels_weights_and_chips()
    {
        var (s, _) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        using var src = RiseFixtures.Variant(s);
        Assert.Equal(new[] { "oak", "ghost" }, src.Items.Select(i => i.Id));
        Assert.Equal("weight 3 · 75%", src.Items[0].Caption);
        Assert.Equal("paper", src.Items[0].DepthChip);
        Assert.Equal("2D only", src.Items[1].DepthChip);
        Assert.True(src.Items[1].IsChipWarning);
        Assert.Equal("Body-Oak", src.Items[0].ExportBaseName);
        Assert.Equal("BODY · VARIANTS", src.Kicker);
        Assert.Single(src.PartialAlphaRows);
        Assert.Equal("1 px", src.PartialAlphaRows[0].CountText);
        Assert.Equal(new[] { "custom", "layer 1 of 2 in Chest" }, src.Facts);
        Assert.True(src.IsTwoDOnly);
        s.Dispose();
    }

    [AvaloniaFact]
    public async Task Saving_writes_the_painted_depth_into_the_cookbook_on_disk()
    {
        var (s, path) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        using var src = RiseFixtures.Variant(s);
        LoadedCookBook? saved = null;
        src.Saved += b => saved = b;
        var oak = src.Open(src.Items[0]);
        oak.History.Do(new DepthStep(new[] { (2, 1) }, DepthSide.Front, +1), oak.Depth);
        Assert.True(src.Items[0].IsDirty);

        await src.SaveAsync();

        Assert.NotNull(saved);
        Assert.Same(saved, s.Current);
        Assert.False(src.Items[0].IsDirty);
        using var reread = CookBookArchive.Read(path);
        var map = Body(reread).Depths["oak"];
        Assert.Equal(1, map.Front(2, 1));
        Assert.Equal(0, map.Front(3, 1));
        Assert.Equal(0, map.Back(2, 1));
        s.Dispose();
    }

    [AvaloniaFact]
    public async Task Saving_one_variant_leaves_every_other_variant_and_layer_byte_for_byte()
    {
        // Ghost carries a stored depth over its solid pixels; oak is the one painted. (The archive writer
        // itself zeroes depth under transparency, so "before" is what the first write left on disk.)
        var ghostDepth = DepthMap.FromBytes(8, 8, Enumerable.Repeat((byte)2, 128).ToArray());
        var (s, path) = RiseFixtures.OnDisk(RiseFixtures.ChestBook(ghostDepth: ghostDepth));
        using var before = CookBookArchive.Read(path);
        using var src = RiseFixtures.Variant(s);
        var oak = src.Open(src.Items[0]);
        oak.History.Do(new DepthStep(new[] { (2, 1) }, DepthSide.Back, +1), oak.Depth);

        await src.SaveAsync();

        using var after = CookBookArchive.Read(path);
        var ghostBefore = Body(before).Depths["ghost"].ToBytes();
        Assert.Contains((byte)2, ghostBefore);   // fixture really carries depth, so equality below means something
        Assert.Equal(ghostBefore, Body(after).Depths["ghost"].ToBytes());
        foreach (var id in new[] { "oak", "ghost" })
            Assert.Equal(PixelBytes(Body(before).VariantImages[id]), PixelBytes(Body(after).VariantImages[id]));
        var lockBefore = before.Recipes[0].Ingredients.Single(i => i.Manifest.Id == "lock");
        var lockAfter = after.Recipes[0].Ingredients.Single(i => i.Manifest.Id == "lock");
        Assert.Equal(PixelBytes(lockBefore.VariantImages["gold"]), PixelBytes(lockAfter.VariantImages["gold"]));
        Assert.Empty(lockAfter.Depths);
        Assert.Equal(before.Manifest with { RecipeWeights = after.Manifest.RecipeWeights }, after.Manifest);
        Assert.Equal(before.Manifest.RecipeWeights.OrderBy(kv => kv.Key), after.Manifest.RecipeWeights.OrderBy(kv => kv.Key));
        Assert.Equal(Body(before).Manifest.Variants, Body(after).Manifest.Variants);
        Assert.Equal(1, Body(after).Depths["oak"].Back(2, 1));
        s.Dispose();
    }

    [AvaloniaFact]
    public async Task Saving_on_a_locked_book_writes_nothing_even_when_called_directly()
    {
        var (s, path) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        var before = File.ReadAllBytes(path);
        using var src = RiseFixtures.Variant(s, editing: false);
        var oak = src.Open(src.Items[0]);
        oak.History.Do(new DepthFillAll(2, 2), oak.Depth);
        Assert.True(src.IsLocked);

        await src.SaveAsync();

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.True(src.Items[0].IsDirty);
        s.Dispose();
    }

    [AvaloniaFact]
    public async Task The_lock_is_read_live_so_closing_it_after_opening_still_blocks_a_save()
    {
        var (s, path) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        var before = File.ReadAllBytes(path);
        bool editing = true;
        var recipe = s.Current!.Recipes[0];
        using var src = new VariantRiseSource(recipe, recipe.Ingredients[0], "oak", new ImageBridge(), s, () => editing);
        var oak = src.Open(src.Items[0]);
        oak.History.Do(new DepthFillAll(1, 1), oak.Depth);
        editing = false;

        await src.SaveAsync();

        Assert.Equal(before, File.ReadAllBytes(path));
        s.Dispose();
    }

    [AvaloniaFact]
    public async Task Painting_back_to_paper_removes_the_stored_map()
    {
        var oakDepth = DepthMap.FromBytes(8, 8, Enumerable.Repeat((byte)1, 128).ToArray());
        var (s, path) = RiseFixtures.OnDisk(RiseFixtures.ChestBook(oakDepth));
        using var src = RiseFixtures.Variant(s);
        var oak = src.Open(src.Items[0]);
        oak.History.Do(new DepthFillAll(0, 0), oak.Depth);
        await src.SaveAsync();
        using var reread = CookBookArchive.Read(path);
        Assert.False(Body(reread).Depths.ContainsKey("oak"));
        s.Dispose();
    }

    [AvaloniaFact]
    public async Task A_loose_ingredient_saves_to_its_own_igt_and_a_second_save_keeps_the_first()
    {
        var dir = Directory.CreateTempSubdirectory("nfty-rise-").FullName;
        var path = Path.Combine(dir, "Body.igt");
        using (var book = RiseFixtures.ChestBook())
            IngredientArchive.Write(path, Body(book).Manifest, Body(book).VariantImages, DepthMaps.None);
        var ing = IngredientArchive.Read(path);
        var wrap = LooseWorkspace.WrapIngredient(ing);
        using var session = new CookBookSession();
        using var src = new VariantRiseSource(wrap.Recipes[0], ing, "oak", new ImageBridge(), session,
            isEditing: null, looseSavePath: path);
        Assert.Equal("loose ingredient", src.Facts[1]);
        Assert.False(src.IsLocked);

        var oak = src.Open(src.Items[0]);
        oak.History.Do(new DepthStep(new[] { (2, 1) }, DepthSide.Front, +1), oak.Depth);
        await src.SaveAsync();
        var ghost = src.Open(src.Items[1]);
        ghost.History.Do(new DepthStep(new[] { (2, 1) }, DepthSide.Back, +1), ghost.Depth);
        await src.SaveAsync();

        using var reread = IngredientArchive.Read(path);
        Assert.Equal(1, reread.Depths["oak"].Front(2, 1));
        Assert.Equal(1, reread.Depths["ghost"].Back(2, 1));
        Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        Assert.Null(session.Current);
    }

    [AvaloniaFact]
    public void A_variant_that_is_not_on_the_layer_is_refused()
    {
        var (s, _) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        Assert.Throws<System.ArgumentException>(() => RiseFixtures.Variant(s, "nope"));
        s.Dispose();
    }

    private static byte[] PixelBytes(Image<Rgba32> img)
    {
        var bytes = new byte[img.Width * img.Height * 4];
        img.CopyPixelDataTo(bytes);
        return bytes;
    }
}
