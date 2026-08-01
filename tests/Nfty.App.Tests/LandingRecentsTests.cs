using System.IO;
using System.Threading.Tasks;
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
}
