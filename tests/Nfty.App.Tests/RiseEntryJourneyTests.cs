using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Rise;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// Step 8 end to end, the <c>OptionalLayerJourneyTests</c> way: each journey starts at the ViewModel a
/// view binds, crosses the real seams (Explorer lock, Rise page, the one persistence path, the Cook
/// dialog, the model writer), lands in real files and reads those files back. A depth that reached the
/// ViewModel and never the model passes every unit test; only the last assertion here sees it.
/// </summary>
public class RiseEntryJourneyTests
{
    private sealed class FixedFolder : IFilePickerService
    {
        private readonly string _dir;
        public FixedFolder(string dir) => _dir = dir;
        public Task<string?> OpenFileAsync(string t, params string[] e) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string t, string e) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string t) => Task.FromResult<string?>(_dir);
    }

    private sealed class NoReveal : IFolderRevealer { public void Reveal(string path) { } }

    private sealed class Rig : IDisposable
    {
        public readonly FakeNav Nav = new();
        public readonly RiseVoxelReadyTests.ScriptedDialogs Dialogs = new() { Answer = true };
        public readonly CookBookSession Session;
        public readonly string Path;
        public readonly ExplorerViewModel Explorer;

        public Rig(CookBookSession session, string path)
        {
            (Session, Path) = (session, path);
            var status = new StatusService();
            Explorer = new ExplorerViewModel(Session.Current!, Nav, Dialogs, new ImageBridge(),
                ExplorerViewModelTests.EditorFactory(Nav, Session, Dialogs),
                ExplorerViewModelTests.CookFactory(Dialogs), Session, new FilePickerService(),
                ExplorerViewModelTests.LooseEditorFactory(Nav, Session, Dialogs), status,
                riseFactory: src => new RiseViewModel(src, new ImageBridge(), Nav, Dialogs,
                    new FilePickerService(), status));
            Explorer.ToggleLockCommand.Execute(null);            // unlock
        }

        public IngredientDetailViewModel OpenBody()
        {
            Explorer.SelectNodeCommand.Execute(Explorer.Root.Children[0].Children.Single(c => c.Id == "body"));
            return Assert.IsType<IngredientDetailViewModel>(Explorer.CurrentDetail);
        }

        public void Dispose()
        {
            (Nav.Current as IDisposable)?.Dispose();
            Explorer.Dispose();
            Session.Dispose();
            try { Directory.Delete(System.IO.Path.GetDirectoryName(Path)!, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>The Z extent (max - min) of a GLB's POSITION accessor, read from its JSON chunk.</summary>
    private static double ZExtent(string glb)
    {
        var bytes = File.ReadAllBytes(glb);
        int jsonLength = BitConverter.ToInt32(bytes, 12);
        using var doc = JsonDocument.Parse(new ReadOnlyMemory<byte>(bytes, 20, jsonLength));
        var root = doc.RootElement;
        int position = root.GetProperty("meshes")[0].GetProperty("primitives")[0]
            .GetProperty("attributes").GetProperty("POSITION").GetInt32();
        var accessor = root.GetProperty("accessors")[position];
        return accessor.GetProperty("max")[2].GetDouble() - accessor.GetProperty("min")[2].GetDouble();
    }

    private static CookDialogViewModel CookOver(LoadedCookBook book, string dir, int count)
    {
        var vm = new CookDialogViewModel(book, new FixedFolder(dir), new NoReveal(), new FakeDialogs())
        { Count = count, Seed = "journey", Unlimited = true };
        return vm;
    }

    [AvaloniaFact]
    public async Task Pane_rise_save_lands_in_the_cbk_and_cooks_into_the_model()
    {
        // A book a model can be made of: the ghost's one half-alpha pixel made solid.
        var fixture = RiseFixtures.ChestBook();
        fixture.Recipes[0].Ingredients.Single(i => i.Manifest.Id == "body").VariantImages["ghost"][3, 3] =
            new Rgba32(200, 40, 40, 255);
        var (session, path) = RiseFixtures.OnDisk(fixture);
        using var rig = new Rig(session, path);
        var outDir = Directory.CreateTempSubdirectory("nfty-journey-cook-").FullName;
        try
        {
            // Ingredient pane -> Rise on the oak -> front 3 on every solid pixel -> Save.
            var pane = rig.OpenBody();
            pane.SelectVariantCommand.Execute("oak");
            pane.RiseCommand.Execute(null);
            var rise = Assert.IsType<RiseViewModel>(rig.Nav.Current);
            rise.FillFront = 3; rise.FillBack = 0;
            rise.FillAllCommand.Execute(null);
            Assert.True(rise.SaveCommand.CanExecute(null));
            await rise.SaveCommand.ExecuteAsync(null);
            Assert.False(rise.HasUnsavedDepth);

            // The file on disk, read fresh: the oak carries the depth, the ghost none.
            using var reread = CookBookArchive.Read(path);
            var body = reread.Recipes[0].Ingredients.Single(i => i.Manifest.Id == "body");
            Assert.Equal(3, body.Depths["oak"].Front(2, 1));
            Assert.DoesNotContain("ghost", body.Depths.Keys);

            // Cook the book that was saved, with models. Both variants are rolled (two legal outcomes),
            // so one model is the thick oak and the other the flat ghost.
            var cook = CookOver(reread, outDir, count: 2);
            cook.IncludeModels = true;
            cook.ModelPivot = Pivot.MinCorner;                    // z from 0, so the extent is the max
            await cook.CookCommand.ExecuteAsync(null);
            Assert.True(cook.IsDone);

            var extents = Directory.GetFiles(Path.Combine(outDir, "models"), "*.glb")
                .Select(ZExtent).OrderBy(e => e).ToArray();
            // 1 voxel of body + 3 front = 4; the depthless ghost is one voxel thick.
            Assert.Equal(new[] { 1.0, 4.0 }, extents);
        }
        finally { try { Directory.Delete(outDir, true); } catch (IOException) { } }
    }

    [AvaloniaFact]
    public async Task A_two_d_only_book_is_fixed_and_then_cooks_models()
    {
        var (session, path) = RiseVoxelReadyTests.Disk();
        using var rig = new Rig(session, path);
        var outDir = Directory.CreateTempSubdirectory("nfty-journey-cook-").FullName;
        try
        {
            // The Cook dialog refuses: the box is shut, the sentence says why, and a forced tick writes none.
            using (var before = CookBookArchive.Read(path))
            {
                var refused = CookOver(before, outDir, count: 2);
                Assert.True(refused.BookIsTwoDOnly);
                Assert.Contains("Make voxel-ready", refused.ModelsNote);
                refused.IncludeModels = true;
                await refused.CookCommand.ExecuteAsync(null);
                Assert.False(Directory.Exists(Path.Combine(outDir, "models")));
            }
            Directory.Delete(outDir, true);
            Directory.CreateDirectory(outDir);

            // Rise on the ghost -> Make voxel-ready (the dialog's answer is yes) -> the file is rewritten.
            var pane = rig.OpenBody();
            pane.SelectVariantCommand.Execute("ghost");
            pane.RiseCommand.Execute(null);
            var rise = Assert.IsType<RiseViewModel>(rig.Nav.Current);
            await rise.MakeVoxelReadyCommand.ExecuteAsync(null);
            Assert.Equal(1, rig.Dialogs.Cards);

            // The same file, reopened: no longer 2D only, and the Cook dialog now makes models.
            using var after = CookBookArchive.Read(path);
            var cook = CookOver(after, outDir, count: 2);
            Assert.False(cook.BookIsTwoDOnly);
            cook.IncludeModels = true;
            await cook.CookCommand.ExecuteAsync(null);
            Assert.True(cook.IsDone);
            Assert.Equal(2, Directory.GetFiles(Path.Combine(outDir, "models"), "*.glb").Length);
        }
        finally { try { Directory.Delete(outDir, true); } catch (IOException) { } }
    }

    [AvaloniaFact]
    public async Task A_foreign_sheet_is_risen_saved_as_igt_and_its_cells_reopen_with_depth()
    {
        // A COPY of a sheet nfty never wrote: 3 cells of 16 px with a transparent margin, no grid chunk.
        var dir = Directory.CreateTempSubdirectory("nfty-journey-sheet-").FullName;
        var sheet = Path.Combine(dir, "heroes.png");
        using (var img = new Image<Rgba32>(48, 16))
        {
            for (int n = 0; n < 3; n++)
                for (int y = 4; y < 12; y++)
                    for (int x = 4; x < 12; x++)
                        img[n * 16 + x, y] = new Rgba32((byte)(40 + n * 60), 180, 90, 255);
            img.Save(sheet);
        }
        long foreignBytes = new FileInfo(sheet).Length;
        var igt = Path.Combine(dir, "heroes.igt");
        try
        {
            using (var vm = new RiseViewModel(
                PictureRiseSource.Open(sheet, new ImageBridge(), () => Task.FromResult<string?>(igt)),
                new ImageBridge(), new FakeNav(), new FakeDialogs(), new FilePickerService(), new StatusService()))
            {
                // Opening detected the grid and wrote it into the copy: the file grew by the chunk.
                var src = Assert.IsType<PictureRiseSource>(vm.Source);
                Assert.Equal(3, src.Grid!.Columns);
                Assert.True(new FileInfo(sheet).Length > foreignBytes);

                vm.SelectItemCommand.Execute(vm.Items.Single(i => i.Id == "cell-2"));
                vm.FillFront = 2; vm.FillBack = 1;
                vm.FillAllCommand.Execute(null);
                Assert.True(vm.SaveCommand.CanExecute(null));
                await vm.SaveCommand.ExecuteAsync(null);
            }

            // Reopened from the .igt alone: three cells, depth on the one that was risen, and nothing else.
            using var read = await IngredientArchive.ReadAsync(igt);
            Assert.Equal(new[] { "cell-1", "cell-2", "cell-3" }, read.Manifest.Variants.Select(v => v.Id).ToArray());
            var depth = Assert.Single(read.Depths);
            Assert.Equal("cell-2", depth.Key);
            Assert.Equal(2, depth.Value.Front(6, 6));
            Assert.Equal(1, depth.Value.Back(6, 6));

            // And the sheet itself reopens with its grid read back, not detected again or rewritten.
            long grown = new FileInfo(sheet).Length;
            using var again = PictureRiseSource.Open(sheet, new ImageBridge());
            Assert.Equal(3, again.Grid!.Columns);
            Assert.Equal(grown, new FileInfo(sheet).Length);
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}
