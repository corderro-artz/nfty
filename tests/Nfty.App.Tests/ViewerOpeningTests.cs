using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Point = Avalonia.Point;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Models;
using Nfty.App.Services;
using Nfty.App.Viewer;
using Nfty.App.ViewModels;
using Nfty.Core.Demo;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Output;
using Nfty.Core.Publish;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// Every place a viewer can be asked for: the gesture reaches the right subject, a plain click keeps
/// doing what it did, and a seal travels with the request. The host is the seam
/// (<see cref="ArchiveGalleryTests.Host"/> records what was asked for), because headless Avalonia has
/// no second window to look at.
/// </summary>
public class ViewerOpeningTests
{
    private static LoadedSet CookedSet(out string dir)
    {
        dir = Directory.CreateTempSubdirectory("nfty-open-").FullName;
        using var set = Generator.Generate(CoreTestBook.Tiny(), new GenerateOptions(3, "seed1"));
        SetWriter.Write(set, dir, pack: false);
        return SetReader.Read(dir);
    }

    private static async Task<T> Until<T>(Func<T?> read) where T : class
    {
        for (int i = 0; i < 400; i++)
        {
            Dispatcher.UIThread.RunJobs();
            if (read() is { } v) return v;
            await Task.Delay(10);
        }
        throw new TimeoutException("the viewer was never requested");
    }

    private static Point Centre(Control c, Window w) =>
        c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), w)!.Value;

    private static (Window w, Views.SetBrowserView view, Button tile) ShowBrowser(SetBrowserViewModel vm, int tileIndex = 1)
    {
        var view = new Views.SetBrowserView { DataContext = vm };
        var w = new Window { Content = view, Width = 1180, Height = 720 };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        var tiles = view.GetVisualDescendants().OfType<Button>().Where(b => b.DataContext is SetItemRow).ToList();
        Assert.True(tiles.Count > tileIndex);
        return (w, view, tiles[tileIndex]);
    }

    // ------------------------------------------------------------------ Set browser

    [AvaloniaTheory]
    [InlineData(MouseButton.Left, RawInputModifiers.Control)]
    [InlineData(MouseButton.Middle, RawInputModifiers.None)]
    public async Task Ctrl_click_and_middle_click_on_a_set_tile_request_a_viewer_for_that_asset(
        MouseButton button, RawInputModifiers mods)
    {
        var loaded = CookedSet(out var dir);
        var host = new ArchiveGalleryTests.Host();
        var dialogs = new FakeDialogs();
        using var vm = new SetBrowserViewModel(loaded, dialogs: dialogs) { Viewer = host };
        var (w, _, tile) = ShowBrowser(vm);
        try
        {
            var row = (SetItemRow)tile.DataContext!;
            var before = vm.SelectedItem;
            Assert.NotSame(row, before);

            w.MouseDown(Centre(tile, w), button, mods);
            w.MouseUp(Centre(tile, w), button, mods);
            var subject = await Until(() => host.Opened.FirstOrDefault());

            Assert.Contains($"#{row.Number:D4}", subject.Title);
            Assert.True(subject.AllowExport);
            // A viewer request is not a selection and not an inspector.
            Assert.Same(before, vm.SelectedItem);
            Assert.Null(dialogs.Active);
            subject.Dispose();
        }
        finally { w.Close(); loaded.Dispose(); Directory.Delete(dir, true); }
    }

    [AvaloniaFact]
    public async Task A_plain_click_on_a_set_tile_still_opens_the_inspector_and_no_viewer()
    {
        var loaded = CookedSet(out var dir);
        var host = new ArchiveGalleryTests.Host();
        var dialogs = new FakeDialogs();
        using var vm = new SetBrowserViewModel(loaded, dialogs: dialogs) { Viewer = host };
        var (w, _, tile) = ShowBrowser(vm);
        try
        {
            var row = (SetItemRow)tile.DataContext!;
            w.MouseDown(Centre(tile, w), MouseButton.Left);
            w.MouseUp(Centre(tile, w), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(50);

            Assert.IsType<SetInspectViewModel>(dialogs.Active);
            Assert.Same(row, vm.SelectedItem);
            Assert.Empty(host.Opened);
        }
        finally { w.Close(); loaded.Dispose(); Directory.Delete(dir, true); }
    }

    [AvaloniaFact]
    public async Task Popping_out_of_a_sealed_set_requests_a_view_only_subject()
    {
        CookedSet(out var dir).Dispose();
        string zip = Path.Combine(Directory.CreateTempSubdirectory("nfty-open-").FullName, "payload.zip");
        ZipFile.CreateFromDirectory(dir, zip);
        string tin = Path.Combine(Path.GetDirectoryName(zip)!, "x" + Archives.SealedExtension);
        using (var plain = File.OpenRead(zip))
            Seal.Write(tin, plain, "Sealed", 3, null, SealPolicy.ViewOnly, "correct-horse-battery", 1000);
        using var sealedSet = SealedSetReader.Open(tin, "correct-horse-battery");
        var host = new ArchiveGalleryTests.Host();
        using var vm = new SetBrowserViewModel(sealedSet) { Viewer = host };

        await vm.PopOutCommand.ExecuteAsync(vm.Items[0]);

        var subject = Assert.Single(host.Opened);
        Assert.False(subject.AllowExport);
        subject.Dispose();
    }

    [AvaloniaFact]
    public async Task Pop_out_from_the_inspector_opens_its_current_asset()
    {
        var loaded = CookedSet(out var dir);
        var host = new ArchiveGalleryTests.Host();
        var dialogs = new FakeDialogs();
        using var vm = new SetBrowserViewModel(loaded, dialogs: dialogs) { Viewer = host };
        try
        {
            await vm.InspectCommand.ExecuteAsync(vm.Items[1]);
            var inspector = Assert.IsType<SetInspectViewModel>(dialogs.Active);
            Assert.True(inspector.CanPopOut);

            inspector.NextCommand.Execute(null);                    // now on the third asset
            await inspector.PopOutCommand.ExecuteAsync(null);

            var subject = Assert.Single(host.Opened);
            Assert.Contains($"#{vm.Items[2].Number:D4}", subject.Title);
            subject.Dispose();
        }
        finally { loaded.Dispose(); Directory.Delete(dir, true); }
    }

    [AvaloniaFact]
    public async Task The_inspector_of_a_browser_with_no_viewer_offers_no_pop_out()
    {
        var loaded = CookedSet(out var dir);
        var dialogs = new FakeDialogs();
        using var vm = new SetBrowserViewModel(loaded, dialogs: dialogs);
        try
        {
            await vm.InspectCommand.ExecuteAsync(vm.Items[0]);
            Assert.False(Assert.IsType<SetInspectViewModel>(dialogs.Active).CanPopOut);
        }
        finally { loaded.Dispose(); Directory.Delete(dir, true); }
    }

    // ------------------------------------------------------------------ Rise page

    [AvaloniaFact]
    public async Task Ctrl_click_on_the_rise_canvas_paints_nothing_and_opens_a_viewer()
    {
        var host = new ArchiveGalleryTests.Host();
        var vm = RisePages.VariantVm();
        vm.Viewer = host;
        using var page = RisePages.Show(vm, 1180, 720);
        var surface = page.View.GetVisualDescendants().OfType<Panel>().First(p => p.Name == "CanvasSurface");
        var before = vm.Subject.Depth.ToDepthMap()?.ToBytes();

        page.Window.MouseDown(Centre(surface, page.Window), MouseButton.Left, RawInputModifiers.Control);
        page.Window.MouseUp(Centre(surface, page.Window), MouseButton.Left, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        var subject = Assert.Single(host.Opened);
        Assert.True(subject.IsLive);
        Assert.False(vm.Editor.CanUndo);
        Assert.Equal(before, vm.Subject.Depth.ToDepthMap()?.ToBytes());
        subject.Dispose();
        await Task.CompletedTask;
    }

    [AvaloniaFact]
    public void A_plain_click_on_the_rise_canvas_still_paints_and_opens_no_viewer()
    {
        var host = new ArchiveGalleryTests.Host();
        var vm = RisePages.VariantVm();
        vm.Viewer = host;
        using var page = RisePages.Show(vm, 1180, 720);
        var surface = page.View.GetVisualDescendants().OfType<Panel>().First(p => p.Name == "CanvasSurface");

        page.Window.MouseDown(Centre(surface, page.Window), MouseButton.Left);
        page.Window.MouseUp(Centre(surface, page.Window), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(host.Opened);
        Assert.True(vm.Editor.CanUndo);
    }

    [AvaloniaFact]
    public void Ctrl_click_on_the_rise_3D_tile_opens_a_viewer_and_the_button_does_the_same()
    {
        var host = new ArchiveGalleryTests.Host();
        var vm = RisePages.VariantVm();
        vm.Viewer = host;
        using var page = RisePages.Show(vm, 1180, 720);
        var tile = page.View.GetVisualDescendants().OfType<Border>().First(b => b.Name == "ModelTile");

        page.Window.MouseDown(Centre(tile, page.Window), MouseButton.Left, RawInputModifiers.Control);
        page.Window.MouseUp(Centre(tile, page.Window), MouseButton.Left, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.Single(host.Opened);

        vm.PopOutCommand.Execute(null);
        Assert.Equal(2, host.Opened.Count);
        foreach (var s in host.Opened) s.Dispose();
    }

    // ------------------------------------------------------------------ Ingredient editor

    [AvaloniaFact]
    public void Ctrl_click_on_the_editor_tile_opens_a_live_viewer_and_a_plain_click_does_not()
    {
        var (book, recipe, ing) = VisualCapture.DynamicIngredient();
        var host = new ArchiveGalleryTests.Host();
        var vm = new IngredientEditorViewModel(ing, recipe, book, new ImageBridge(), new FakeNav(),
            new CookBookSession(), new FakeDialogs(), new FilePickerService()) { Viewer = host };
        var view = new Views.IngredientEditorView { DataContext = vm };
        var w = new Window { Content = view, Width = 1180, Height = 720 };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var tile = view.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PreviewTile");
            // The middle of the tile is art, not one of its buttons.
            var at = tile.TranslatePoint(new Point(tile.Bounds.Width / 2, tile.Bounds.Height / 2), w)!.Value;

            w.MouseDown(at, MouseButton.Left);
            w.MouseUp(at, MouseButton.Left);
            Assert.Empty(host.Opened);

            w.MouseDown(at, MouseButton.Left, RawInputModifiers.Control);
            w.MouseUp(at, MouseButton.Left, RawInputModifiers.Control);
            w.MouseDown(at, MouseButton.Middle);
            w.MouseUp(at, MouseButton.Middle);
            Assert.Equal(2, host.Opened.Count);
            Assert.All(host.Opened, s => Assert.True(s.IsLive));
            foreach (var s in host.Opened) s.Dispose();
        }
        finally { w.Close(); vm.Dispose(); }
    }

    // ------------------------------------------------------------------ Explorer + Recipe pane

    private static ExplorerViewModel Explorer(ArchiveGalleryTests.Host host)
    {
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var session = new CookBookSession();
        return new ExplorerViewModel(ExplorerViewModelTests.TwoRecipeBook(), nav, dialogs, new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav), ExplorerViewModelTests.CookFactory(dialogs), session,
            new FilePickerService(), ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs),
            new StatusService()) { Viewer = host };
    }

    [AvaloniaFact]
    public void Ctrl_click_on_a_tree_row_does_not_change_the_selection()
    {
        var host = new ArchiveGalleryTests.Host();
        using var vm = Explorer(host);
        var view = new Views.ExplorerView { DataContext = vm };
        var w = new Window { Content = view, Width = 1180, Height = 720 };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var rows = view.GetVisualDescendants().OfType<TreeViewItem>()
                .Where(t => t.DataContext is ExplorerNode { Kind: ExplorerNodeKind.Recipe }).ToList();
            Assert.NotEmpty(rows);
            var row = rows[0];
            var node = (ExplorerNode)row.DataContext!;
            var header = row.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("node"));
            var at = Centre(header, w);
            var selected = vm.SelectedNode;
            Assert.NotSame(node, selected);

            w.MouseDown(at, MouseButton.Left, RawInputModifiers.Control);
            w.MouseUp(at, MouseButton.Left, RawInputModifiers.Control);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(selected, vm.SelectedNode);
            var subject = Assert.Single(host.Opened);
            Assert.False(subject.IsLive);
            subject.Dispose();

            // The same press without Ctrl is the ordinary selection, so the test above can fail.
            w.MouseDown(at, MouseButton.Left);
            w.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(node.Id, vm.SelectedNode?.Id);
            Assert.Single(host.Opened);   // still only the one request
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void An_ingredient_node_and_a_recipe_layer_each_request_their_own_snapshot()
    {
        var host = new ArchiveGalleryTests.Host();
        using var vm = Explorer(host);
        var recipeNode = vm.Roots[0].Children[0];
        var ingredientNode = recipeNode.Children[0];

        vm.PopOutNodeCommand.Execute(ingredientNode);
        vm.SelectNodeCommand.Execute(recipeNode);
        var pane = Assert.IsType<RecipeDetailViewModel>(vm.CurrentDetail);
        var layer = pane.Layers[0];
        pane.PopOutLayerAtCommand.Execute(layer.Id);

        Assert.Equal(2, host.Opened.Count);
        Assert.Equal(host.Opened[0].Title, host.Opened[1].Title);
        Assert.Contains(ingredientNode.Name, host.Opened[0].Title);
        Assert.All(host.Opened, s => Assert.False(s.IsLive));
        foreach (var s in host.Opened) s.Dispose();
    }

    // ------------------------------------------------------------------ Landing

    private sealed class Picks(string path) : IFilePickerService
    {
        public string[] Asked = Array.Empty<string>();
        public Task<string?> OpenFileAsync(string title, params string[] extensions)
        {
            Asked = extensions;
            return Task.FromResult<string?>(path);
        }
        public Task<string?> SaveFileAsync(string title, string defaultExtension) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private static LandingViewModel Landing(IFilePickerService picker, ArchiveGalleryTests.Host host)
    {
        var n = new FakeNav();
        var d = new FakeDialogs();
        return new LandingViewModel(n, d, picker, new RecentsService(Directory.CreateTempSubdirectory().FullName),
            new CookBookSession(), book => null!, set => new SetBrowserViewModel(set), (_, _, _) => null!,
            viewer: host);
    }

    [AvaloniaFact]
    public async Task Open_in_viewer_from_landing_routes_by_extension()
    {
        var dir = Directory.CreateTempSubdirectory("nfty-open-").FullName;
        string png = Path.Combine(dir, "pic.png");
        using (var img = new Image<Rgba32>(4, 4, new Rgba32(10, 200, 30, 255))) img.SaveAsPng(png);
        string cbk = DemoCookBook.WriteTo(dir);

        var host = new ArchiveGalleryTests.Host();
        var picker = new Picks(png);
        var landing = Landing(picker, host);
        await landing.OpenInViewerCommand.ExecuteAsync(null);
        Assert.IsType<SnapshotSubject>(Assert.Single(host.Opened));

        landing = Landing(new Picks(cbk), host);
        await landing.OpenInViewerCommand.ExecuteAsync(null);
        Assert.IsType<GallerySubject>(host.Opened[1]);

        // Every extension the opener understands is offered to the picker, from the one list.
        Assert.Equal(ViewerOpener.Extensions, picker.Asked);
        foreach (var ext in new[] { ".cbk", ".rcp", ".igt", ".set", ".tin", ".mnu", ".ktn", ".png", ".glb", ".json" })
            Assert.Contains(ext, picker.Asked);
        foreach (var s in host.Opened) s.Dispose();
    }

    [AvaloniaFact]
    public void Open_in_viewer_is_dim_when_there_is_no_viewer()
    {
        var n = new FakeNav();
        var landing = new LandingViewModel(n, new FakeDialogs(), new Picks("x"),
            new RecentsService(Directory.CreateTempSubdirectory().FullName), new CookBookSession(),
            book => null!, set => new SetBrowserViewModel(set), (_, _, _) => null!);
        Assert.False(landing.OpenInViewerCommand.CanExecute(null));
    }
}
