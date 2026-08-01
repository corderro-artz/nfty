using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Threading;
using Avalonia.Headless.XUnit;
using Nfty.App.Models;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>Landing records a Recent entry after each successful open and reopens a clicked recent
/// by dispatching on its extension. Mirrors LandingNewCookBookTests' Landing-construction helper —
/// the ctor gained params in recent slices, so real source wins over any stale quote.</summary>
public class LandingRecentsTests
{
    private sealed class StubPicker : IFilePickerService
    {
        private readonly string? _path;
        public StubPicker(string? path) => _path = path;
        public Task<string?> OpenFileAsync(string title, params string[] extensions) => Task.FromResult(_path);
        public Task<string?> SaveFileAsync(string title, string defaultExtension) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private static (LandingViewModel vm, FakeNav nav, FakeDialogs dialogs, IRecentsService recents) Landing(
        IFilePickerService picker, string storageDir)
    {
        var nav = new FakeNav(); var dialogs = new FakeDialogs(); var notify = new FakeNotYetWired();
        var session = new CookBookSession();
        var recents = new RecentsService(storageDir);
        var vm = new LandingViewModel(nav, dialogs, notify, picker, recents, session,
            book => new ExplorerViewModel(book, nav, dialogs, notify, new ImageBridge(),
                ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
                ExplorerViewModelTests.CookFactory(dialogs), session,
                picker, ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs)),
            set => new SetBrowserViewModel(set),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs));
        return (vm, nav, dialogs, recents);
    }

    private static string WriteTinyCookBook(string dir)
    {
        var path = Path.Combine(dir, "vapor.cbk");
        var manifest = new CookBookManifest("vapor-pets", "VaporPets", new Dimensions(8, 8),
            new Collection("VaporPets", "d", "VP"), new Dictionary<string, double>());
        CookBookPersistence.WriteNew(path, manifest, Array.Empty<LoadedRecipe>());
        return path;
    }

    [AvaloniaFact]
    public async Task Opening_a_cookbook_records_a_recent()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var storageDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var path = WriteTinyCookBook(dir);
            var (vm, _, _, _) = Landing(new StubPicker(path), storageDir);

            await vm.ImportCommand.ExecuteAsync(null);

            var recent = Assert.Single(vm.Recents);
            Assert.Equal(Path.GetFullPath(path), recent.Path);
            Assert.False(recent.Loose);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
            Directory.Delete(storageDir, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task A_failed_open_records_nothing()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var storageDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var path = Path.Combine(dir, "not-an-archive.cbk");
            File.WriteAllText(path, "not a zip");
            var (vm, _, _, _) = Landing(new StubPicker(path), storageDir);

            await vm.ImportCommand.ExecuteAsync(null);

            Assert.Empty(vm.Recents);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
            Directory.Delete(storageDir, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Clicking_a_missing_recent_removes_it_and_errors()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var storageDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var missingPath = Path.Combine(dir, "gone.cbk");   // never written
            var (vm, nav, dialogs, recents) = Landing(new StubPicker(null), storageDir);
            recents.Add(new RecentItem("Gone", "some cookbook", missingPath, false));

            vm.OpenRecentCommand.Execute(vm.Recents[0]);

            Assert.IsType<ErrorDialogViewModel>(dialogs.Active);
            Assert.Empty(vm.Recents);
            Assert.Null(nav.Current);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
            Directory.Delete(storageDir, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Clicking_a_cookbook_recent_opens_the_explorer()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var storageDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var path = WriteTinyCookBook(dir);
            var (vm, nav, _, recents) = Landing(new StubPicker(null), storageDir);
            recents.Add(new RecentItem("VaporPets", "0 recipes · 8x8", path, false));

            vm.OpenRecentCommand.Execute(vm.Recents[0]);

            Assert.IsType<ExplorerViewModel>(nav.Current);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
            Directory.Delete(storageDir, recursive: true);
        }
    }

    /// <summary>The removal must reach the SCREEN, not just the view-model. Bindings short-circuit
    /// when a property returns the same instance, so a live-list Recents made OnPropertyChanged inert
    /// and the dead row stayed visible. Asserted through a real bound ItemsControl, because a
    /// vm.Recents assertion cannot see this class of bug.</summary>
    [AvaloniaFact]
    public async Task Removing_a_missing_recent_updates_the_bound_list()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var (vm, _, _, recents) = Landing(new StubPicker(null), dir);
            recents.Add(new RecentItem("Gone", "1 recipe", Path.Combine(dir, "gone.cbk"), false));

            var list = new ItemsControl();
            list.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(vm.Recents)) { Source = vm });
            var window = new Window { Content = list };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, list.ItemCount);                       // the dead row is on screen

            vm.OpenRecentCommand.Execute(vm.Recents[0]);           // file doesn't exist → removed
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, list.ItemCount);                       // ...and it left the screen
            await Task.CompletedTask;
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
