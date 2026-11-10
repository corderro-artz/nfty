using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>The Community page as drawn: reserved slots, the measured page size, previews and the rail.</summary>
public class CommunityLayoutTests
{
    /// <summary>The page area at the smallest window.</summary>
    internal static Size PageArea => new(
        (ShellViewModel.MinWindowWidth - 24) / ShellViewModel.BaseScale,
        (ShellViewModel.MinWindowHeight - ShellViewModel.ChromeReserve) / ShellViewModel.BaseScale);

    private static void Settle(CommunityViewModel vm)
    {
        Dispatcher.UIThread.RunJobs();
        foreach (var t in vm.Tiles) UiThreadWait.For(t.PendingDecode);
        UiThreadWait.For(vm.PendingRail);
        Dispatcher.UIThread.RunJobs();
    }

    private static (Window Window, Views.CommunityView View) Show(CommunityViewModel vm, Size? size = null,
        ThemeVariant? variant = null)
    {
        var area = size ?? PageArea;
        var view = new Views.CommunityView { DataContext = vm };
        var window = new Window { RequestedThemeVariant = variant, Content = view, Width = area.Width, Height = area.Height };
        window.Show();
        Settle(vm);
        return (window, view);
    }

    private static string TempSheetDir() => Directory.CreateTempSubdirectory("nfty-community-sheet-").FullName;

    private static byte[] Pixels(Bitmap bmp)
    {
        int w = bmp.PixelSize.Width, h = bmp.PixelSize.Height;
        var buffer = new byte[w * h * 4];
        unsafe
        {
            fixed (byte* p = buffer)
                bmp.CopyPixels(new PixelRect(0, 0, w, h), (nint)p, buffer.Length, w * 4);
        }
        return buffer;
    }

    private static Rect RectIn(Control c, Visual ancestor) => new(c.TranslatePoint(default, ancestor)!.Value, c.Bounds.Size);

    private static CommunityViewModel Page(CommunityListing listing, string sheetDir, ICommunitySeen? seen = null,
        IGitHubAccount? account = null)
    {
        var cache = new FakeCommunityCache { Next = listing, PreviewPath = CommunityFixtures.Sheet(sheetDir) };
        var vm = new CommunityViewModel(cache, seen ?? new CommunitySeen(Directory.CreateTempSubdirectory("nfty-seen-").FullName),
            account ?? new FakeGitHubAccount(), new FakeNav(), new FakeDialogs(), new StatusService(), _ => Task.CompletedTask);
        UiThreadWait.For(vm.PendingRefresh);
        return vm;
    }


    /// <summary>The page in its riskiest state, for the sweeps: offline (warning ink in the status line and under
    /// Download), a New badge showing, an Updated one, both chips, a long description selected.</summary>
    internal static CommunityViewModel RiskyPage(int entries = 12, bool online = false)
    {
        var dir = TempSheetDir();
        var seen = new CommunitySeen(Directory.CreateTempSubdirectory("nfty-seen-").FullName, () => CommunityFixtures.T0);
        var list = Enumerable.Range(0, entries).Select(i => CommunityFixtures.Entry($"e{i:00}", $"Collection {i:00}",
            login: i % 2 == 0 ? "alice" : "bob", assets: 40 + i * 33, tags: i % 3 == 0 ? ["pets"] : ["robots", "pixel"],
            models: i % 2 == 0, book: i % 3 == 0, created: CommunityFixtures.T0.AddDays(i % 4 == 0 ? 2 : -3),
            updated: CommunityFixtures.T0.AddDays(i), description: string.Join(' ', Enumerable.Repeat("A long sentence that wraps.", 30))))
            .ToList();
        seen.See(list[1]);
        list[1] = list[1] with { Updated = CommunityFixtures.T0.AddDays(40) };
        var notice = Nfty.Core.Publish.Community.ShareRules.Offline(new DateTime(2026, 11, 9, 13, 0, 0));
        var vm = Page(CommunityFixtures.Listing(list, online ? null : notice), dir, seen);
        vm.SelectCommand.Execute(vm.Tiles[2]);
        return vm;
    }

    private static T Named<T>(Visual root, string cls) where T : Control =>
        root.GetVisualDescendants().OfType<T>().First(c => c.Classes.Contains(cls));

    [AvaloniaFact]
    public void The_grid_shows_every_preview_and_a_missing_one_says_so()
    {
        var dir = TempSheetDir();
        var listing = CommunityFixtures.Listing([CommunityFixtures.Entry("ok", "Ok"), CommunityFixtures.Entry("gone", "Gone")]);
        var cache = new FakeCommunityCache { Next = listing, PreviewPath = CommunityFixtures.Sheet(dir) };
        cache.MissingPreviews.Add("https://example.test/gone.png");
        using var vm = new CommunityViewModel(cache, new CommunitySeen(dir), new FakeGitHubAccount(), new FakeNav(),
            new FakeDialogs(), new StatusService(), _ => Task.CompletedTask);
        UiThreadWait.For(vm.PendingRefresh);
        var (window, view) = Show(vm);
        try
        {
            var ok = vm.Tiles.Single(t => t.Name == "Ok");
            var gone = vm.Tiles.Single(t => t.Name == "Gone");
            Assert.NotNull(ok.Preview);
            Assert.False(ok.IsLoading);
            Assert.False(ok.ImageMissing);
            Assert.Equal(16, ok.Preview!.PixelSize.Width);        // the first cell, not the whole sheet
            Assert.True(gone.ImageMissing);
            Assert.False(gone.IsLoading);
            Assert.Single(view.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("tmissing") && b.IsVisible
                && b.Bounds.Width > 0 && b.GetVisualAncestors().OfType<Border>().Any(a => a.Classes.Contains("cpreview")));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Badges_reserve_space_when_absent()
    {
        // Two entries identical but for the 3D and Book flags: every pixel of their tiles agrees except
        // the two chips themselves, and the chips' own boxes are the same on both.
        var dir = TempSheetDir();
        var listing = CommunityFixtures.Listing([
            CommunityFixtures.Entry("with", "Same", models: true, book: true, updated: CommunityFixtures.T0.AddDays(1)),
            CommunityFixtures.Entry("without", "Same")]);
        using var vm = Page(listing, dir);
        var (window, view) = Show(vm);
        try
        {
            var buttons = view.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("ctile")).ToList();
            Assert.Equal(2, buttons.Count);
            var chips = buttons.Select(b => b.GetVisualDescendants().OfType<Border>().Where(c => c.Classes.Contains("cchip")).ToList()).ToList();
            Assert.All(chips, c => Assert.Equal(2, c.Count));
            Assert.Equal([1.0, 1.0], chips[0].Select(c => c.Opacity).ToArray());
            Assert.Equal([0.0, 0.0], chips[1].Select(c => c.Opacity).ToArray());
            for (int i = 0; i < 2; i++)
            {
                var a = RectIn(chips[0][i], buttons[0]);
                var b = RectIn(chips[1][i], buttons[1]);
                Assert.Equal(a, b);
                Assert.True(a.Width > 0 && a.Height > 0);
            }
            Assert.Equal(buttons[0].Bounds.Size, buttons[1].Bounds.Size);

            using var frame = window.CaptureRenderedFrame()!;
            var px = Pixels(frame);
            int stride = frame.PixelSize.Width * 4;
            var r0 = RectIn(buttons[0], window);
            var r1 = RectIn(buttons[1], window);
            // The bottom-left of the preview: the chips, and the rounded clip's corner, which anti-aliases one
            // pixel differently once a chip is inked beside it.
            var preview = RectIn(buttons[0].GetVisualDescendants().OfType<Border>().First(c => c.Classes.Contains("cpreview")), buttons[0]);
            var masks = new List<Rect> { new(preview.X, preview.Bottom - 32, 110, 32) };
            int diff = 0;
            Point first = default;
            for (int y = 0; y < (int)r0.Height; y++)
                for (int x = 0; x < (int)r0.Width; x++)
                {
                    if (masks.Any(m => m.Contains(new Point(x, y)))) continue;
                    int i0 = ((int)r0.Y + y) * stride + ((int)r0.X + x) * 4;
                    int i1 = ((int)r1.Y + y) * stride + ((int)r1.X + x) * 4;
                    for (int k = 0; k < 4; k++) if (px[i0 + k] != px[i1 + k]) { diff++; if (diff == 1) first = new Point(x, y); break; }
                }
            Assert.True(diff == 0, $"{diff} pixels of the two tiles differ outside the chips, first at {first} of {r0.Size}; chips {string.Join(' ', masks)}");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Badge_slot_is_reserved_when_unmarked()
    {
        var dir = TempSheetDir();
        var seen = new CommunitySeen(Directory.CreateTempSubdirectory("nfty-seen-").FullName, () => CommunityFixtures.T0);
        var listing = CommunityFixtures.Listing([
            CommunityFixtures.Entry("n", "Fresh", created: CommunityFixtures.T0.AddDays(2))]);
        using var vm = Page(listing, dir, seen);
        var (window, view) = Show(vm);
        try
        {
            var badge = Named<Border>(view, "cbadge");
            var statusRow = Named<TextBlock>(view, "cstatus").GetVisualParent<Grid>()!;
            Assert.Equal(1.0, badge.Opacity);
            var inked = badge.Bounds;
            var badgeRect = RectIn(badge, window).Inflate(1);
            var statusRect = RectIn(statusRow, window);
            using var before = window.CaptureRenderedFrame()!;
            var a = Pixels(before);

            vm.MarkAllSeenCommand.Execute(null);
            Settle(vm);

            Assert.Equal(0.0, badge.Opacity);
            Assert.Equal(inked, badge.Bounds);                       // same box, only its ink left
            using var after = window.CaptureRenderedFrame()!;
            var b = Pixels(after);
            int stride = after.PixelSize.Width * 4, diffOutside = 0, diffInside = 0;
            for (int y = 0; y < after.PixelSize.Height; y++)
                for (int x = 0; x < after.PixelSize.Width; x++)
                {
                    int i = y * stride + x * 4;
                    bool same = a[i] == b[i] && a[i + 1] == b[i + 1] && a[i + 2] == b[i + 2] && a[i + 3] == b[i + 3];
                    if (same) continue;
                    var p = new Point(x, y);
                    if (badgeRect.Contains(p)) diffInside++;
                    else if (!statusRect.Contains(p)) diffOutside++;
                }
            Assert.True(diffInside > 0, "the badge's ink did not change");
            Assert.True(diffOutside == 0, $"{diffOutside} pixels moved outside the badge and the status line");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1046, 526)]
    [InlineData(1180, 720)]
    [InlineData(1600, 900)]
    [InlineData(900, 480)]
    public void Page_size_is_measured_and_stable_on_resize(int width, int height)
    {
        var dir = TempSheetDir();
        var entries = Enumerable.Range(0, 60).Select(i => CommunityFixtures.Entry($"e{i:00}", $"Set {i:00}",
            updated: CommunityFixtures.T0.AddDays(-i)));
        using var vm = Page(CommunityFixtures.Listing(entries), dir);
        var (window, view) = Show(vm, new Size(width, height));
        try
        {
            var host = view.FindControl<ScrollViewer>("TilesHost")!;
            double pw = (double)view.FindResource("CommunityTilePitchWidth")!;
            double ph = (double)view.FindResource("CommunityTilePitchHeight")!;
            double tw = (double)view.FindResource("CommunityTileWidth")!;
            double th = (double)view.FindResource("CommunityTileHeight")!;
            int expected = Math.Max(1, (int)(host.Bounds.Width / pw)) * Math.Max(1, (int)(host.Bounds.Height / ph));
            Assert.Equal(expected, vm.PageSize);
            Assert.Equal(expected, vm.Tiles.Count);

            // Declared size = drawn size, and every tile of the page lies inside the host: a page
            // that held one more would have been clipped.
            foreach (var button in view.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("ctile")))
            {
                Assert.Equal(tw, button.Bounds.Width);
                Assert.Equal(th, button.Bounds.Height);
                var r = RectIn(button, host);
                Assert.True(r.Right + (pw - tw) <= host.Bounds.Width + 0.5 && r.Bottom + (ph - th) <= host.Bounds.Height + 0.5,
                    $"tile {r} overruns the host {host.Bounds.Size}");
            }

            // Stable: laying out again and again, with the grid's own content changing underneath,
            // never moves the answer (the infinite-layout-loop rule: the handler reads only the host).
            for (int i = 0; i < 6; i++)
            {
                view.InvalidateMeasure();
                vm.NextPageCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(expected, vm.PageSize);
            }

            // And a resize follows the host.
            window.Width = width + 180;
            window.Height = height + 90;
            Dispatcher.UIThread.RunJobs();
            int again = Math.Max(1, (int)(host.Bounds.Width / pw)) * Math.Max(1, (int)(host.Bounds.Height / ph));
            Assert.Equal(again, vm.PageSize);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void The_rail_says_how_to_pick_until_something_is_picked()
    {
        var dir = TempSheetDir();
        using var vm = Page(CommunityFixtures.Listing([CommunityFixtures.Entry("a", "Dragons", description: "Scaly friends.")]), dir);
        var (window, view) = Show(vm);
        try
        {
            var hint = view.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Pick a collection to see it here.");
            Assert.True(hint.IsVisible);
            var download = view.GetVisualDescendants().OfType<Button>().First(b => ReferenceEquals(b.Command, vm.DownloadCommand));
            Assert.Contains(download.GetVisualAncestors().OfType<Control>(), a => !a.IsHitTestVisible);    // reserved, not clickable

            vm.SelectCommand.Execute(vm.Tiles[0]);
            Settle(vm);

            Assert.False(hint.IsVisible);
            Assert.DoesNotContain(download.GetVisualAncestors().OfType<Control>(), a => !a.IsHitTestVisible);
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Scaly friends.");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_pointer_click_on_a_tile_selects_it()
    {
        // The tile template reaches the page's command through $parent[UserControl]; no earlier test
        // pressed a real tile, so nothing proved that binding resolves.
        var dir = TempSheetDir();
        using var vm = Page(CommunityFixtures.Listing([
            CommunityFixtures.Entry("a", "Dragons"),
            CommunityFixtures.Entry("b", "Robots", updated: CommunityFixtures.T0.AddDays(1))]), dir);
        var (window, view) = Show(vm);
        try
        {
            Assert.Null(vm.Selected);
            var tile = view.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("ctile")).ToList()[1];
            var center = tile.TranslatePoint(new Point(tile.Bounds.Width / 2, tile.Bounds.Height / 2), window)!.Value;

            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            Settle(vm);

            Assert.Same(tile.DataContext, vm.Selected);
            Assert.True(vm.HasSelection);
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == vm.Selected!.Name);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Starting_a_download_moves_nothing_in_the_rail()
    {
        var dir = TempSheetDir();
        var body = new StallingBody();
        var http = new ScriptedHttp((_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new StreamContent(body) }));
        var cache = new FakeCommunityCache
        {
            Next = CommunityFixtures.Listing([CommunityFixtures.Entry("a", "Dragons")]),
            PreviewPath = CommunityFixtures.Sheet(dir),
            SetsDir = Path.Combine(dir, "sets"),
        };
        using var vm = new CommunityViewModel(cache, new CommunitySeen(Directory.CreateTempSubdirectory("nfty-seen-").FullName),
            new FakeGitHubAccount(), new FakeNav(), new FakeDialogs(), new StatusService(), _ => Task.CompletedTask,
            http: http.Client());
        UiThreadWait.For(vm.PendingRefresh);
        var (window, view) = Show(vm);
        try
        {
            vm.SelectCommand.Execute(vm.Tiles[0]);
            Settle(vm);
            var stop = view.GetVisualDescendants().OfType<Button>().First(b => ReferenceEquals(b.Command, vm.StopDownloadCommand));
            var download = view.GetVisualDescendants().OfType<Button>().First(b => ReferenceEquals(b.Command, vm.DownloadCommand));
            var row = stop.GetVisualAncestors().OfType<Grid>().First();
            var before = (RectIn(download, window), RectIn(stop, window));
            Assert.Contains(row.Classes, c => c == "cgone");                  // reserved, not inked

            _ = vm.DownloadCommand.ExecuteAsync(null);
            UiThreadWait.For(body.Started.Task);
            Dispatcher.UIThread.RunJobs();

            Assert.DoesNotContain(row.Classes, c => c == "cgone");            // inked
            Assert.Equal(before, (RectIn(download, window), RectIn(stop, window)));

            vm.StopDownloadCommand.Execute(null);
            UiThreadWait.For(vm.PendingDownload);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(row.Classes, c => c == "cgone");
            Assert.Equal(before, (RectIn(download, window), RectIn(stop, window)));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void The_wheel_steps_the_rail_through_the_preview_cells()
    {
        var dir = TempSheetDir();
        using var vm = Page(CommunityFixtures.Listing([CommunityFixtures.Entry("a")]), dir);
        var (window, view) = Show(vm);
        try
        {
            vm.SelectCommand.Execute(vm.Tiles[0]);
            Settle(vm);
            Assert.Equal(4, vm.CellCount);
            Assert.Equal("cell 1 of 4", vm.CellText);
            Assert.NotNull(vm.RailPreview);
            Assert.Equal(16, vm.RailPreview!.PixelSize.Width);

            var shot = view.FindControl<Border>("RailShot")!;
            var center = shot.TranslatePoint(new Point(shot.Bounds.Width / 2, shot.Bounds.Height / 2), window)!.Value;
            var first = Pixels(vm.RailPreview);
            window.MouseWheel(center, new Vector(0, -1));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, vm.Cell);
            Assert.NotEqual(first, Pixels(vm.RailPreview!));
            window.MouseWheel(center, new Vector(0, 1));
            Assert.Equal(0, vm.Cell);
            window.MouseWheel(center, new Vector(0, 1));
            Assert.Equal(3, vm.Cell);                              // wraps
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void The_rail_is_one_shape_for_every_collection()
    {
        var dir = TempSheetDir();
        var listing = CommunityFixtures.Listing([
            CommunityFixtures.Entry("short", "Short", description: "Brief."),
            CommunityFixtures.Entry("long", "Long", description: string.Join(' ', Enumerable.Repeat("A long sentence that wraps.", 60)),
                updated: CommunityFixtures.T0.AddDays(1))]);
        using var vm = Page(listing, dir);
        var (window, view) = Show(vm);
        try
        {
            vm.SelectCommand.Execute(vm.Tiles.Single(t => t.Name == "Short"));
            Settle(vm);
            var download = view.GetVisualDescendants().OfType<Button>().First(b => ReferenceEquals(b.Command, vm.DownloadCommand));
            var at = RectIn(download, window);

            vm.SelectCommand.Execute(vm.Tiles.Single(t => t.Name == "Long"));
            Settle(vm);

            Assert.Equal(at, RectIn(download, window));            // the pinned buttons never move
            var rail = download.GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("rules-panel"));
            Assert.True(RectIn(download, rail).Bottom <= rail.Bounds.Height, "Download is cut off by the rail");
        }
        finally { window.Close(); }
    }
}
