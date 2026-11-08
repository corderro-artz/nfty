using Avalonia.Headless.XUnit;
using Nfty.App.Rise;
using Nfty.App.Services;
using Nfty.Core.Editing;
using Xunit;

namespace Nfty.App.Tests;

public class RiseWriteGateAuditTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rise_depth_save_refuses_another_book_writer_and_keeps_paint_unsaved(bool recipe)
    {
        var directory = Directory.CreateTempSubdirectory();
        using var session = new CookBookSession();
        var book = RiseFixtures.ChestBook(withGhost: false);
        string path = Path.Combine(directory.FullName, "book.cbk");
        CookBookPersistence.WriteNew(path, book.Manifest, book.Recipes);
        session.Open(book, path);
        using RiseSource source = recipe
            ? new RecipeRiseSource(book, book.Recipes[0], 1, new ImageBridge(), session)
            : RiseFixtures.Variant(session);
        if (source is RecipeRiseSource r) Assert.True(r.SetTarget("body"));
        var subject = source.Open(source.InitialItem);
        source.EditorFor(subject).Do(new DepthStep([(2, 1)], DepthSide.Front, 1));
        byte[] before = File.ReadAllBytes(path);
        try
        {
            using (var held = session.TryBeginWrite())
            {
                Assert.NotNull(held);
                var error = await Assert.ThrowsAsync<InvalidOperationException>(() => source.SaveAsync());
                Assert.Contains("current save", error.Message);
                Assert.Equal(before, File.ReadAllBytes(path));
                Assert.True(source.IsDirty);
            }
            bool leaseHeldOnSaved = false;
            source.Saved += _ => leaseHeldOnSaved = session.IsWriting;
            await source.SaveAsync();
            Assert.True(leaseHeldOnSaved);
            Assert.False(source.IsDirty);
            Assert.False(session.IsWriting);
        }
        finally { directory.Delete(true); }
    }

    [AvaloniaFact]
    public async Task In_book_voxel_ready_refuses_another_writer_without_changing_pixels()
    {
        using var session = new CookBookSession();
        session.Open(RiseFixtures.ChestBook());
        using var source = RiseFixtures.Variant(session);
        var art = session.Current!.Recipes[0].Ingredients[0].VariantImages["ghost"];
        var before = art[3, 3];
        using var held = session.TryBeginWrite();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => source.MakeVoxelReadyAsync());
        Assert.Contains("current save", error.Message);
        Assert.Equal(before, art[3, 3]);
        Assert.False(source.CanUndoVoxelReady);
    }
}
