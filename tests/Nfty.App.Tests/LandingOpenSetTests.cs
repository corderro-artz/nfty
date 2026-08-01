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
        var notify = new FakeNotYetWired();
        return new LandingViewModel(nav, dialogs, notify, picker,
            new RecentsService(Directory.CreateTempSubdirectory().FullName), new CookBookSession(),
            book => new ExplorerViewModel(book, nav, dialogs, notify, new ImageBridge(),
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
    public async Task Cancelled_picker_does_nothing()
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
}
