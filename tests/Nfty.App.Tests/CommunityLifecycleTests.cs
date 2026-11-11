using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

using Nfty.Core.Publish.Community;

/// <summary>Community refresh and preview lifetime regressions.</summary>
public class CommunityLifecycleTests
{
    private sealed class Page : IDisposable
    {
        public readonly string Dir = Directory.CreateTempSubdirectory("nfty-community-keys-").FullName;
        public readonly NavigationService Nav = new();
        public readonly ViewModelBase Landing = new HelpViewModel(new FakeDialogs());
        public readonly FakeCommunityCache Cache;
        public readonly CommunityViewModel Vm;
        public readonly Views.CommunityView View;
        public readonly Window Window;
        public Page()
        {
            Cache = new FakeCommunityCache
            {
                Next = CommunityFixtures.Listing(Enumerable.Range(0, 30)
                    .Select(i => CommunityFixtures.Entry($"e{i}", $"Collection {i}"))),
                PreviewPath = CommunityFixtures.Sheet(Dir),
            };
            Nav.To(Landing);
            Vm = new CommunityViewModel(Cache, new CommunitySeen(Dir), new FakeGitHubAccount(), Nav,
                new FakeDialogs(), new StatusService(), _ => Task.CompletedTask);
            UiThreadWait.For(Vm.PendingRefresh);
            Nav.To(Vm);
            View = new Views.CommunityView { DataContext = Vm };
            var area = CommunityLayoutTests.PageArea;
            Window = new Window { Content = View, Width = area.Width, Height = area.Height };
            Window.Show();
            Dispatcher.UIThread.RunJobs();
        }
        public void Press(PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            Window.KeyPressQwerty(key, modifiers);
            Window.KeyReleaseQwerty(key, modifiers);
            Dispatcher.UIThread.RunJobs();
        }
        public void Dispose()
        {
            Window.Close();
            Nav.Dispose();
            Directory.Delete(Dir, recursive: true);
        }
    }

    [AvaloniaFact]
    public void Repeating_the_refresh_chord_keeps_one_fetch_in_flight()
    {
        using var p = new Page();
        var refresh = new TaskCompletionSource<CommunityListing>();
        p.Cache.RefreshResult = refresh.Task;
        var initialCalls = p.Cache.Refreshes;
        try
        {
            p.Press(PhysicalKey.R);
            Assert.True(p.Vm.RefreshCommand.IsRunning);
            p.Press(PhysicalKey.R);
            Assert.Equal(initialCalls + 1, p.Cache.Refreshes);
        }
        finally
        {
            refresh.SetResult(p.Cache.Next!);
            UiThreadWait.For(p.Vm.PendingRefresh);
        }
    }

    [AvaloniaFact]
    public void Selecting_another_collection_disposes_the_previous_rail_bitmap()
    {
        using var p = new Page();
        p.Vm.SelectCommand.Execute(p.Vm.Tiles[0]);
        UiThreadWait.For(p.Vm.PendingRail);
        Dispatcher.UIThread.RunJobs();
        var previous = Assert.IsType<WriteableBitmap>(p.Vm.RailPreview);
        using (previous.Lock()) { }   // The first selection has a live, readable bitmap.

        p.Vm.SelectCommand.Execute(p.Vm.Tiles[1]);
        try
        {
            Assert.Throws<ObjectDisposedException>(() => { using var frame = previous.Lock(); });
        }
        finally
        {
            UiThreadWait.For(p.Vm.PendingRail);
            Dispatcher.UIThread.RunJobs();
            previous.Dispose();
        }
    }

    [Fact]
    public async Task Leaving_community_does_not_recreate_tiles_when_a_refresh_finishes()
    {
        var dir = Directory.CreateTempSubdirectory("nfty-community-leave-").FullName;
        var refresh = new TaskCompletionSource<CommunityListing>();
        var cache = new FakeCommunityCache { RefreshResult = refresh.Task };
        using var nav = new NavigationService();
        try
        {
            var landing = new HelpViewModel(new FakeDialogs());
            nav.To(landing);
            var vm = new CommunityViewModel(cache, new CommunitySeen(dir), new FakeGitHubAccount(),
                nav, new FakeDialogs(), new StatusService(), _ => Task.CompletedTask);
            nav.To(vm);
            vm.BackCommand.Execute(null);
            Assert.Same(landing, nav.Current);

            // A fetch can complete before cancellation reaches it, with its UI continuation still queued.
            refresh.SetResult(CommunityFixtures.Listing([CommunityFixtures.Entry("late")]));
            await vm.PendingRefresh;
            Assert.Empty(vm.Tiles);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

}
