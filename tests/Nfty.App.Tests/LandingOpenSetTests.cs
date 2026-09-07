using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Generation;
using Nfty.Core.Output;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>Wires Landing's "Open a cooked .set…" action: reads a cooked Set off disk and
/// navigates to a <see cref="SetBrowserViewModel"/>. Mirrors LandingOpenFlowTests' shape (its
/// StubPicker/FakeNav/FakeDialogs doubles), but for the .set path instead of .cbk.</summary>
public class LandingOpenSetTests
{
    private sealed class StubPicker : IFilePickerService
    {
        private readonly string? _path;
        public StubPicker(string? path) => _path = path;
        public Task<string?> OpenFileAsync(string title, params string[] extensions) => Task.FromResult(_path);
        public Task<string?> SaveFileAsync(string title, string defaultExtension) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private static LandingViewModel MakeLanding(FakeNav nav, FakeDialogs dialogs, IFilePickerService picker)
    {
        return new LandingViewModel(nav, dialogs, picker,
            new RecentsService(Directory.CreateTempSubdirectory().FullName), new CookBookSession(),
            book => new ExplorerViewModel(book, nav, dialogs, new ImageBridge(),
                ExplorerViewModelTests.EditorFactory(nav), ExplorerViewModelTests.CookFactory(dialogs), new CookBookSession(),
                new FilePickerService(), ExplorerViewModelTests.LooseEditorFactory(nav, new CookBookSession(), dialogs), new StatusService()),
            s => new SetBrowserViewModel(s),
                (_, _, _) => null!);
    }

    private static string CookTinySet()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        using var generated = Generator.Generate(CoreTestBook.Tiny(), new GenerateOptions(2, "seed1"));
        SetWriter.Write(generated, dir, pack: false);
        return dir;
    }

    /// <summary>A packed Set, and the path of the archive itself — which lives INSIDE the folder it
    /// packs and is named after it.</summary>
    private static string CookPackedSet()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        using var generated = Generator.Generate(CoreTestBook.Tiny(), new GenerateOptions(2, "seed1"));
        SetWriter.Write(generated, dir, pack: true);
        return Path.Combine(dir, Path.GetFileName(dir) + ".set");
    }

    [AvaloniaFact]
    public async Task Open_set_reads_and_navigates_to_the_browser()
    {
        var dir = CookTinySet();
        try
        {
            var nav = new FakeNav();
            var vm = MakeLanding(nav, new FakeDialogs(), new StubPicker(dir));
            await vm.OpenSetCommand.ExecuteAsync(null);
            Assert.IsType<SetBrowserViewModel>(nav.Current);
            ((SetBrowserViewModel)nav.Current!).Dispose();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Canceled_picker_does_nothing()
    {
        var nav = new FakeNav();
        var vm = MakeLanding(nav, new FakeDialogs(), new StubPicker(null));
        await vm.OpenSetCommand.ExecuteAsync(null);
        Assert.Null(nav.Current);
    }

    [AvaloniaFact]
    public async Task A_bad_path_shows_the_error_dialog_and_does_not_navigate()
    {
        var tmp = Directory.CreateTempSubdirectory().FullName;   // empty dir, no set.json
        try
        {
            var nav = new FakeNav();
            var dialogs = new FakeDialogs();
            var vm = MakeLanding(nav, dialogs, new StubPicker(tmp));
            await vm.OpenSetCommand.ExecuteAsync(null);
            Assert.IsType<ErrorDialogViewModel>(dialogs.Active);
            Assert.Null(nav.Current);
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }

    [AvaloniaFact]
    public void A_remembered_set_reopens_in_the_browser()
    {
        // OpenRecent used to route a .set with its own extension compare, ABOVE the dispatch, because
        // Archives.KindOf did not know the kind — the second copy of the mapping TryKindOf exists to
        // prevent. It goes through the enum now, and nothing else covered this path.
        var archive = CookPackedSet();
        var nav = new FakeNav();
        var vm = MakeLanding(nav, new FakeDialogs(), new StubPicker(null));

        vm.OpenRecentCommand.Execute(
            new Nfty.App.Models.RecentItem("Tiny", "set · 2 assets", archive, false));

        Assert.IsType<SetBrowserViewModel>(nav.Current);
        ((SetBrowserViewModel)nav.Current!).Dispose();
    }

    [AvaloniaFact]
    public async Task Import_opens_a_set_rather_than_calling_it_a_Kitchen()
    {
        // Import's picker is filtered to .cbk/.rcp/.igt, but a TYPED filename is not — the same hole
        // that once made importing a .ktn report "Not wired yet". A .set fell past every arm into the
        // Kitchen message, which named the wrong file type and the wrong action to use instead.
        var archive = CookPackedSet();
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var vm = MakeLanding(nav, dialogs, new StubPicker(archive));

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.IsType<SetBrowserViewModel>(nav.Current);
        Assert.IsNotType<ErrorDialogViewModel>(dialogs.Active);
        ((SetBrowserViewModel)nav.Current!).Dispose();
    }

    [AvaloniaFact]
    public async Task A_recent_entry_naming_a_SET_FOLDER_opens_it()
    {
        // FOUND BY DRIVING THE RUNNING APP, and reachable from the running app the moment anything
        // records a folder - which `export --folder` now writes and the CLI has always written.
        //
        // OpenRecent's own existence guard says "a Set may be a folder" and then handed the path
        // straight to Archives.KindOf, which resolves an EXTENSION. The entry could never be
        // reopened; it failed with "has no extension; expected one of .cbk, .rcp, .igt, .ktn, .set,
        // .tin", which describes the mechanism rather than the situation. Every ViewModel test
        // passed, because none of them put a directory in Recents.
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var recents = new RecentsService(Directory.CreateTempSubdirectory().FullName);
        var landing = new LandingViewModel(nav, dialogs, new StubPicker(null), recents,
            new CookBookSession(),
            book => new ExplorerViewModel(book, nav, dialogs, new ImageBridge(),
                ExplorerViewModelTests.EditorFactory(nav), ExplorerViewModelTests.CookFactory(dialogs),
                new CookBookSession(), new FilePickerService(),
                ExplorerViewModelTests.LooseEditorFactory(nav, new CookBookSession(), dialogs),
                new StatusService()),
            s => new SetBrowserViewModel(s), (_, _, _) => null!);

        var folder = CookTinySet();
        landing.OpenRecentCommand.Execute(
            new Models.RecentItem("Tiny", "set · 2 assets", folder, false));
        await Task.Yield();

        Assert.Null(dialogs.Active);                       // no "Can't open"
        Assert.IsType<SetBrowserViewModel>(nav.Current);
    }

    [AvaloniaFact]
    public void An_ordinary_folder_in_recents_still_reports_that_it_is_not_a_set()
    {
        // The other half: IsSetFolder asks for set.json rather than merely for a directory, so a
        // folder that is not a Set falls through to the same message it always did instead of being
        // guessed at - the rule the extension table follows.
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var landing = MakeLanding(nav, dialogs, new StubPicker(null));

        landing.OpenRecentCommand.Execute(new Models.RecentItem("Nope", "?",
            Directory.CreateTempSubdirectory().FullName, false));

        Assert.IsType<ErrorDialogViewModel>(dialogs.Active);
        Assert.Null(nav.Current);
    }
}
