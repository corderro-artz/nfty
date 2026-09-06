using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Demo;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// Landing's "Open the demo CookBook": the one action on that screen that works on a machine with
/// no nfty files on it at all.
/// </summary>
/// <remarks>
/// Every test here pins the <see cref="IStateStore"/> to a temp folder. Left to its own discovery
/// the store resolves to <c>AppContext.BaseDirectory</c>, which under the harness is the test
/// project's own build output — the same rule that keeps <c>RecentsService</c> off the developer's
/// real list, applied to the folder the demo is unpacked into.
/// </remarks>
public class LandingDemoTests
{
    private sealed class NoPicker : IFilePickerService
    {
        public Task<string?> OpenFileAsync(string title, params string[] extensions) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string title, string defaultExtension) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private static LandingViewModel Make(string root, out FakeNav nav, out CookBookSession session)
    {
        var n = new FakeNav();
        var d = new FakeDialogs();
        var s = new CookBookSession();
        nav = n; session = s;
        return new LandingViewModel(n, d, new NoPicker(),
            new RecentsService(Directory.CreateTempSubdirectory().FullName), s,
            book => new ExplorerViewModel(book, n, d, new ImageBridge(), ExplorerViewModelTests.EditorFactory(n),
                ExplorerViewModelTests.CookFactory(d), new CookBookSession(),
                new FilePickerService(), ExplorerViewModelTests.LooseEditorFactory(n, new CookBookSession(), d),
                new StatusService()),
            set => new SetBrowserViewModel(set),
            (_, _, _) => null!,
            kitchen: null,
            // StateStore.At uses the folder exactly as given, so the demo lands in <root>/demo -
            // beside the store rather than inside it, which is what the app does with a real
            // <root>/.nfty.
            store: StateStore.At(Path.Combine(root, ".nfty")));
    }

    [Fact]
    public void Opening_the_demo_unpacks_it_beside_the_app_and_navigates_to_the_explorer()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var vm = Make(root, out var nav, out var session);

        vm.OpenDemoCommand.Execute(null);

        var expected = Path.Combine(root, "demo", DemoCookBook.FileName);
        Assert.True(File.Exists(expected), $"the demo was not unpacked to {expected}");
        Assert.IsType<ExplorerViewModel>(nav.Current);
        Assert.NotNull(session.Current);
        Assert.Equal(DemoCookBook.DisplayName, session.Current!.Manifest.Name);
        // The session has to know the FILE, not just the book: without a source path the Explorer
        // opens permanently read-only and the demo cannot be edited, which is its whole job.
        Assert.Equal(expected, session.SourcePath);
    }

    [Fact]
    public void The_demo_joins_the_recent_list_like_any_other_cookbook()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var vm = Make(root, out _, out _);

        vm.OpenDemoCommand.Execute(null);

        var recent = Assert.Single(vm.Recents);
        Assert.Equal(DemoCookBook.DisplayName, recent.Name);
        Assert.False(recent.Loose);
        Assert.False(vm.HasNoRecents);
    }

    [Fact]
    public void Opening_it_a_second_time_keeps_the_edits_made_to_it()
    {
        // Driven through the command rather than DemoCookBook.WriteTo, because the promise being
        // kept here is the BUTTON's: the user clicks the same thing twice and finds their work.
        var root = Directory.CreateTempSubdirectory().FullName;
        var vm = Make(root, out _, out _);
        vm.OpenDemoCommand.Execute(null);

        var path = Path.Combine(root, "demo", DemoCookBook.FileName);
        var stamp = File.GetLastWriteTimeUtc(path).AddDays(-1);
        File.SetLastWriteTimeUtc(path, stamp);

        vm.OpenDemoCommand.Execute(null);

        Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void With_no_store_it_still_opens_from_a_temp_folder()
    {
        // The in-memory store is a real state - nowhere on the machine was writable - and the demo
        // is worth having anyway. A null store takes the same branch, which is what a ViewModel
        // constructed without one gets.
        var n = new FakeNav();
        var d = new FakeDialogs();
        var s = new CookBookSession();
        var vm = new LandingViewModel(n, d, new NoPicker(),
            new RecentsService(Directory.CreateTempSubdirectory().FullName), s,
            book => new ExplorerViewModel(book, n, d, new ImageBridge(), ExplorerViewModelTests.EditorFactory(n),
                ExplorerViewModelTests.CookFactory(d), new CookBookSession(),
                new FilePickerService(), ExplorerViewModelTests.LooseEditorFactory(n, new CookBookSession(), d),
                new StatusService()),
            set => new SetBrowserViewModel(set),
            (_, _, _) => null!);

        vm.OpenDemoCommand.Execute(null);

        Assert.IsType<ExplorerViewModel>(n.Current);
        Assert.Equal(Path.Combine(Path.GetTempPath(), "nfty-demo", DemoCookBook.FileName), s.SourcePath);
    }
}
