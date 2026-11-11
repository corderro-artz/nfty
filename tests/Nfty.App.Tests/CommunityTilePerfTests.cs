using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Publish;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>What the Community grid costs: tiles kept across a resize, a preview kept at tile size, the seen file
/// written once.</summary>
public class CommunityTilePerfTests
{
    private static CommunityViewModel Page(int entries, ICommunitySeen? seen = null, string? sheet = null)
    {
        string dir = Directory.CreateTempSubdirectory("nfty-community-perf-").FullName;
        var list = Enumerable.Range(0, entries).Select(i => CommunityFixtures.Entry($"e{i:00}", $"Set {i:00}",
            updated: CommunityFixtures.T0.AddDays(-i)));
        var cache = new FakeCommunityCache { Next = CommunityFixtures.Listing(list), PreviewPath = sheet ?? CommunityFixtures.Sheet(dir) };
        var vm = new CommunityViewModel(cache, seen ?? new CommunitySeen(Directory.CreateTempSubdirectory("nfty-seen-").FullName),
            new FakeGitHubAccount(), new FakeNav(), new FakeDialogs(), new StatusService(), _ => Task.CompletedTask);
        UiThreadWait.For(vm.PendingRefresh);
        return vm;
    }

    [AvaloniaFact]
    public void A_resize_that_grows_the_page_reuses_the_tiles_already_shown()
    {
        using var vm = Page(80);
        var view = new Views.CommunityView { DataContext = vm };
        var window = new Window { Content = view, Width = 900, Height = 560 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            static List<Button> Cells(Visual v) =>
                v.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("ctile")).ToList();

            var before = Cells(view);
            int size = vm.PageSize;
            Assert.Equal(size, before.Count);

            double pitch = (double)view.FindResource("CommunityTilePitchWidth")!;
            window.Width += pitch * 2;
            Dispatcher.UIThread.RunJobs();

            Assert.True(vm.PageSize > size, "the page did not grow");
            var after = Cells(view);
            Assert.Equal(vm.PageSize, after.Count);
            Assert.All(before, b => Assert.Contains(b, after));   // the same Button instances, not look-alikes
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Changing_the_page_size_edits_only_the_tail_of_the_tiles()
    {
        using var vm = Page(40);
        vm.PageSize = 8;
        var first = vm.Tiles.ToList();
        var log = new List<NotifyCollectionChangedAction>();
        ((INotifyCollectionChanged)vm.Tiles).CollectionChanged += (_, e) => log.Add(e.Action);

        vm.PageSize = 12;
        Assert.Equal(12, vm.Tiles.Count);
        Assert.Equal(4, log.Count);
        Assert.All(log, a => Assert.Equal(NotifyCollectionChangedAction.Add, a));
        Assert.True(first.SequenceEqual(vm.Tiles.Take(8)));

        log.Clear();
        vm.PageSize = 5;
        Assert.All(log, a => Assert.Equal(NotifyCollectionChangedAction.Remove, a));
        Assert.True(first.Take(5).SequenceEqual(vm.Tiles));
    }

    [AvaloniaFact]
    public void A_page_turn_still_shows_the_next_tiles()
    {
        using var vm = Page(40);
        vm.PageSize = 8;
        var first = vm.Tiles.ToList();
        vm.NextPageCommand.Execute(null);
        Assert.Equal(8, vm.Tiles.Count);
        Assert.DoesNotContain(vm.Tiles, t => first.Contains(t));
    }

    private static string BigSheet(int side)
    {
        string path = Path.Combine(Directory.CreateTempSubdirectory("nfty-community-big-").FullName, "preview.png");
        using (var img = new Image<Rgba32>(side * 2, side))
        {
            for (int y = 0; y < side; y++)
                for (int x = 0; x < side * 2; x++) img[x, y] = new Rgba32((byte)(x % 251), (byte)(y % 241), 9, 255);
            img.SaveAsPng(path);
        }
        SheetGrid.WriteInto(path, new SheetGrid(2, 1, side, side, 2));
        return path;
    }

    private static Avalonia.Media.Imaging.Bitmap Decoded(CommunityViewModel vm)
    {
        var tile = vm.Tiles[0];
        _ = tile.Preview;
        UiThreadWait.For(tile.PendingDecode);
        Dispatcher.UIThread.RunJobs();
        return tile.Preview!;
    }

    [AvaloniaFact]
    public void A_large_cell_is_kept_at_tile_size_by_a_whole_number_factor()
    {
        using var vm = Page(1, sheet: BigSheet(1000));
        var bmp = Decoded(vm);
        Assert.True(bmp.PixelSize.Width <= CommunityTile.PreviewMaxSide && bmp.PixelSize.Height <= CommunityTile.PreviewMaxSide,
            $"kept {bmp.PixelSize}");
        Assert.Equal(200, bmp.PixelSize.Width);   // ceil(1000 / 240) = 5, so 1000 / 5
    }

    [Theory]
    [InlineData(16, 1)]
    [InlineData(240, 1)]
    [InlineData(241, 2)]
    [InlineData(1000, 5)]
    public void The_reduction_is_the_smallest_whole_factor_that_fits_and_never_an_enlargement(int side, int factor)
    {
        Assert.Equal(factor, CommunityTile.ReductionFor(side, side));
        Assert.True((side + factor - 1) / factor <= CommunityTile.PreviewMaxSide);
    }

    [AvaloniaFact]
    public void A_small_cell_keeps_its_exact_pixels()
    {
        using var vm = Page(1);
        var bmp = Decoded(vm);
        Assert.Equal(16, bmp.PixelSize.Width);   // first cell of the 2 x 2 grid, not enlarged
        Assert.Equal(16, bmp.PixelSize.Height);
    }

    [Fact]
    public async Task Many_selections_make_one_write_after_the_quiet_time()
    {
        string root = Directory.CreateTempSubdirectory("nfty-seen-debounce-").FullName;
        var gate = new TaskCompletionSource();
        var seen = new CommunitySeen(root, () => CommunityFixtures.T0, _ => gate.Task);
        int baseline = seen.SaveCount;   // the first-visit write

        for (int i = 0; i < 20; i++) seen.See(CommunityFixtures.Entry($"e{i}"));
        Assert.Equal(baseline, seen.SaveCount);   // nothing written on the selecting thread

        gate.SetResult();
        await seen.PendingSave;
        Assert.Equal(baseline + 1, seen.SaveCount);
        Assert.Contains("e19", File.ReadAllText(Path.Combine(root, "seen.json")));
    }

    [Fact]
    public void Flush_writes_a_mark_that_is_still_waiting()
    {
        string root = Directory.CreateTempSubdirectory("nfty-seen-flush-").FullName;
        var seen = new CommunitySeen(root, () => CommunityFixtures.T0, _ => new TaskCompletionSource().Task);
        seen.See(CommunityFixtures.Entry("late"));
        Assert.DoesNotContain("late", File.ReadAllText(Path.Combine(root, "seen.json")));
        seen.Flush();
        Assert.Contains("late", File.ReadAllText(Path.Combine(root, "seen.json")));
    }

    [AvaloniaFact]
    public void Disposing_the_page_flushes_a_mark_still_waiting()
    {
        string root = Directory.CreateTempSubdirectory("nfty-seen-dispose-").FullName;
        var seen = new CommunitySeen(root, () => CommunityFixtures.T0, _ => new TaskCompletionSource().Task);
        var vm = Page(3, seen);
        var tile = vm.Tiles[0];
        vm.SelectCommand.Execute(tile);
        vm.Dispose();
        Assert.Contains(tile.Entry.Id, File.ReadAllText(Path.Combine(root, "seen.json")));
    }
}
