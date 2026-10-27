using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Nfty.App.Models;
using Nfty.App.Rise;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Nfty.Model;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// Step 10 end to end, the <c>OptionalLayerJourneyTests</c> way: start at the ViewModel the Rise page binds,
/// cross the real seams (Recipe pane, Explorer, edit lock, the one persistence path), land in a real .cbk,
/// REOPEN that file and cook it. A depth that reached the page and never the file passes every unit test;
/// only the last assertions here see it.
/// </summary>
public class RiseWriteThroughJourneyTests
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
        public readonly FakeDialogs Dialogs = new();
        public readonly StatusService Status = new();
        public readonly CookBookSession Session;
        public readonly string Path;
        public readonly ExplorerViewModel Explorer;

        public Rig(IRecentsService? recents = null)
        {
            // Oak and pine bodies with NO depth, a gold strap with none either: every entry starts absent.
            (Session, Path) = RiseFixtures.OnDisk(RiseFixtures.ChestBook(withPine: true, withGhost: false));
            Func<RiseViewModel, RiseViewModel> wrap = vm => vm;
            if (recents is not null)
            {
                var sp = new ServiceCollection()
                    .AddSingleton<ICookBookSession>(Session)
                    .AddSingleton<IRecentsService>(recents)
                    .BuildServiceProvider();
                wrap = vm => ServiceRegistration.RememberOnRiseSave(sp, vm);
            }
            Explorer = new ExplorerViewModel(Session.Current!, Nav, Dialogs, new ImageBridge(),
                ExplorerViewModelTests.EditorFactory(Nav, Session, Dialogs),
                ExplorerViewModelTests.CookFactory(Dialogs), Session, new FilePickerService(),
                ExplorerViewModelTests.LooseEditorFactory(Nav, Session, Dialogs), Status,
                riseFactory: src => wrap(new RiseViewModel(src, new ImageBridge(), Nav, Dialogs,
                    new FilePickerService(), Status)));
            Explorer.ToggleLockCommand.Execute(null);            // unlock
        }

        public ExplorerNode Recipe => Explorer.Root.Children[0];

        /// <summary>Recipe pane -> Rise on the roll it shows, the way the button does.</summary>
        public RiseViewModel OpenRise()
        {
            Explorer.SelectNodeCommand.Execute(Recipe);
            var pane = Assert.IsType<RecipeDetailViewModel>(Explorer.CurrentDetail);
            pane.RiseCommand.Execute(null);
            return Assert.IsType<RiseViewModel>(Nav.Current);
        }

        /// <summary>Picks the strap, then raises its front by two at the pixel (3,3).</summary>
        public static void PaintStrapTwice(RiseViewModel rise)
        {
            var at = new[] { (3, 3) };
            rise.PreviewDepthStroke(at, DepthSide.Front, lower: false);
            rise.ApplyDepthStroke(at, DepthSide.Front, lower: false);        // the picking click: paints nothing
            Assert.Equal("Lock / Gold", ((RecipeRiseSource)rise.Source).Target!.Name);
            for (int n = 0; n < 2; n++)
            {
                rise.PreviewDepthStroke(at, DepthSide.Front, lower: false);
                rise.ApplyDepthStroke(at, DepthSide.Front, lower: false);
            }
        }

        public void Dispose()
        {
            (Nav.Current as IDisposable)?.Dispose();
            Explorer.Dispose();
            Session.Dispose();
            try { Directory.Delete(System.IO.Path.GetDirectoryName(Path)!, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>Every variant's picture and depth, by "layer/variant", as text that compares for equality.</summary>
    private static Dictionary<string, string> Snapshot(string cbk)
    {
        using var book = CookBookArchive.Read(cbk);
        var all = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var ing in book.Recipes.SelectMany(r => r.Ingredients))
            foreach (var v in ing.Manifest.Variants)
            {
                var png = new StringBuilder();
                var img = ing.VariantImages[v.Id];
                for (int y = 0; y < img.Height; y++)
                    for (int x = 0; x < img.Width; x++)
                        png.Append(img[x, y].PackedValue).Append(',');
                string depth = "absent";
                if (ing.Depths.TryGetValue(v.Id, out var d))
                {
                    var sb = new StringBuilder();
                    for (int y = 0; y < d.Height; y++)
                        for (int x = 0; x < d.Width; x++)
                            sb.Append(d.Front(x, y)).Append('/').Append(d.Back(x, y)).Append(' ');
                    depth = sb.ToString();
                }
                all[$"{ing.Manifest.Id}/{v.Id}"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(png.ToString()))) + "|" + depth;
            }
        return all;
    }

    private static string FileHash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

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

    [AvaloniaFact]
    public async Task Painting_a_strap_pixel_writes_only_that_variants_depth_and_the_cooked_model_shows_it()
    {
        using var rig = new Rig();
        var before = Snapshot(rig.Path);
        var outDir = Directory.CreateTempSubdirectory("nfty-wt-cook-").FullName;
        try
        {
            var rise = rig.OpenRise();
            Rig.PaintStrapTwice(rise);
            Assert.True(rise.SaveCommand.CanExecute(null));
            await rise.SaveCommand.ExecuteAsync(null);
            Assert.False(rise.HasUnsavedDepth);

            // The file, reopened: gold carries (2,0) at the painted pixel, and nothing else moved.
            var after = Snapshot(rig.Path);
            Assert.Equal(before.Keys.OrderBy(k => k, StringComparer.Ordinal), after.Keys.OrderBy(k => k, StringComparer.Ordinal));
            var changed = after.Keys.Where(k => before[k] != after[k]).ToList();
            Assert.Equal(new[] { "lock/gold" }, changed);
            Assert.Equal("absent", after["body/oak"].Split('|')[1]);          // absent stays absent
            Assert.Equal("absent", after["body/pine"].Split('|')[1]);
            Assert.Equal(before["lock/gold"].Split('|')[0], after["lock/gold"].Split('|')[0]);   // its picture too
            using (var reread = CookBookArchive.Read(rig.Path))
            {
                var gold = reread.Recipes[0].Ingredients.Single(i => i.Manifest.Id == "lock").Depths["gold"];
                Assert.Equal(2, gold.Front(3, 3));
                Assert.Equal(0, gold.Back(3, 3));
                Assert.Equal(0, gold.Front(4, 4));                           // the stroke touched one pixel

                // Cook the book that was saved, with models: the strap stands two proud of the body.
                var cook = new CookDialogViewModel(reread, new FixedFolder(outDir), new NoReveal(), new FakeDialogs())
                { Count = 2, Seed = "journey", Unlimited = true };
                cook.IncludeModels = true;
                cook.ModelPivot = Pivot.MinCorner;
                await cook.CookCommand.ExecuteAsync(null);
                Assert.True(cook.IsDone);
            }
            var extents = Directory.GetFiles(Path.Combine(outDir, "models"), "*.glb").Select(ZExtent).ToArray();
            Assert.Equal(2, extents.Length);
            Assert.All(extents, e => Assert.Equal(3.0, e));                  // 1 voxel of body + 2 of strap
        }
        finally { try { Directory.Delete(outDir, true); } catch (IOException) { } }
    }

    [AvaloniaFact]
    public async Task A_write_through_save_refreshes_the_explorer_and_the_recents()
    {
        var dir = Directory.CreateTempSubdirectory("nfty-wt-recents-").FullName;
        try
        {
            var recents = new RecentsService(dir);
            using var rig = new Rig(recents);
            var oldBook = rig.Session.Current;
            var rise = rig.OpenRise();
            Rig.PaintStrapTwice(rise);

            await rise.SaveCommand.ExecuteAsync(null);

            Assert.NotSame(oldBook, rig.Session.Current);
            // The tree was rebuilt from the saved book: its lock node holds the graph that carries the depth.
            var node = rig.Explorer.Root.Children[0].Children.Single(c => c.Id == "lock");
            var (liveRecipe, liveLock) = ((LoadedRecipe, LoadedIngredient))node.Domain!;
            Assert.Same(rig.Session.Current!.Recipes[0], liveRecipe);
            Assert.Equal(2, liveLock.Depths["gold"].Front(3, 3));
            // And the Recent was recorded once, against the file on disk.
            var recent = Assert.Single(recents.Items);
            Assert.Equal(Path.GetFullPath(rig.Path), recent.Path);
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [AvaloniaFact]
    public async Task An_open_editor_reloads_its_draft_after_a_write_through_save()
    {
        using var rig = new Rig();
        // The editor on the strap is open underneath (the Rise page is pushed over the Explorer).
        rig.Explorer.SelectNodeCommand.Execute(rig.Recipe.Children.Single(c => c.Id == "lock"));
        var pane = Assert.IsType<IngredientDetailViewModel>(rig.Explorer.CurrentDetail);
        pane.EditIngredientCommand.Execute(null);
        var editor = Assert.IsType<IngredientEditorViewModel>(rig.Nav.Current);
        Assert.Null(editor.DepthOf("gold"));

        var rise = rig.OpenRise();
        Rig.PaintStrapTwice(rise);
        await rise.SaveCommand.ExecuteAsync(null);

        // The Rise page's Saved reached the Explorer's handler, which handed the book to the editor.
        Assert.Equal(2, editor.DepthOf("gold")!.Front(3, 3));
        Assert.False(editor.IsDirty);
    }

    [AvaloniaFact]
    public async Task The_lock_refuses_write_through_even_when_forced()
    {
        using var rig = new Rig();
        var rise = rig.OpenRise();
        Rig.PaintStrapTwice(rise);
        Assert.True(rise.HasUnsavedDepth);
        string hash = FileHash(rig.Path);

        rig.Explorer.ToggleLockCommand.Execute(null);                        // lock again

        await rise.Source.SaveAsync();                                        // the source, past the button
        rise.SaveCommand.Execute(null);                                       // the button, whatever it thinks
        await Task.Delay(50);

        Assert.Equal(hash, FileHash(rig.Path));
        Assert.Contains("read-only", rise.FooterText, StringComparison.Ordinal);
    }
}
