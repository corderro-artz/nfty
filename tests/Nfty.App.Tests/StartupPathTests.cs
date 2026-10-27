using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Output;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// <c>nfty book.cbk</c>, or a file dropped on the executable, opens through the routing a Recent row
/// uses - and a path that cannot be opened lands on the ordinary error card.
/// </summary>
public class StartupPathTests
{
    private sealed class RecordingDialogs : IDialogService
    {
        public List<ViewModelBase> Shown { get; } = [];
        public ViewModelBase? Active { get; private set; }
        public event System.Action? Changed;
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog) { Shown.Add(dialog); Active = dialog; Changed?.Invoke(); return Task.FromResult<TResult?>(default); }
        public void Close(object? result) { Active = null; Changed?.Invoke(); }
    }

    private sealed class NoPicker : IFilePickerService
    {
        public Task<string?> OpenFileAsync(string t, params string[] e) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string t, string e) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string t) => Task.FromResult<string?>(null);
    }

    private static LandingViewModel Landing(FakeNav nav, RecordingDialogs dialogs)
    {
        var session = new CookBookSession();
        return new LandingViewModel(nav, dialogs, new NoPicker(),
            new RecentsService(Directory.CreateTempSubdirectory().FullName), session,
            book => new ExplorerViewModel(book, nav, dialogs, new ImageBridge(), ExplorerViewModelTests.EditorFactory(nav),
                ExplorerViewModelTests.CookFactory(dialogs), session,
                new FilePickerService(), ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), new StatusService()),
            set => new SetBrowserViewModel(set),
            (_, _, _) => null!);
    }

    [AvaloniaFact]
    public async Task A_path_given_at_startup_opens_like_a_recent()
    {
        var tmp = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var cbk = Path.Combine(tmp, "b.cbk");
            using (var b = ExplorerViewModelTests.TwoRecipeBook()) CookBookArchive.Write(cbk, b.Manifest, b.Recipes);
            var setDir = Path.Combine(tmp, "set");
            using (var src = ExplorerViewModelTests.TwoRecipeBook())
            using (var cooked = Generator.Generate(src, new GenerateOptions(2, "s")))
                SetWriter.Write(cooked, setDir, pack: false);

            // A book goes to the Explorer ...
            var nav = new FakeNav();
            await Landing(nav, new RecordingDialogs()).OpenFromCommandLineAsync(cbk);
            Assert.IsType<ExplorerViewModel>(nav.Current);

            // ... and a Set FOLDER, which has no extension for the router to read, to the browser.
            var nav2 = new FakeNav();
            await Landing(nav2, new RecordingDialogs()).OpenFromCommandLineAsync(setDir);
            var browser = Assert.IsType<SetBrowserViewModel>(nav2.Current);
            browser.Dispose();
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }

    [AvaloniaFact]
    public async Task A_missing_startup_path_shows_an_error_card()
    {
        var nav = new FakeNav();
        var dialogs = new RecordingDialogs();
        await Landing(nav, dialogs).OpenFromCommandLineAsync(Path.Combine(Path.GetTempPath(), "nfty-no-such-file.cbk"));

        Assert.Null(nav.Current);
        var error = Assert.IsType<ErrorDialogViewModel>(dialogs.Shown.Single());
        Assert.Contains("nfty-no-such-file.cbk", error.Message);
    }

    [AvaloniaFact]
    public async Task An_unknown_startup_path_shows_an_error_card_and_does_not_throw()
    {
        var tmp = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var odd = Path.Combine(tmp, "notes.txt");
            File.WriteAllText(odd, "hello");
            var nav = new FakeNav();
            var dialogs = new RecordingDialogs();
            await Landing(nav, dialogs).OpenFromCommandLineAsync(odd);

            Assert.Null(nav.Current);
            Assert.IsType<ErrorDialogViewModel>(dialogs.Shown.Single());
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }

    [Theory]
    [InlineData(new[] { "book.cbk" }, "book.cbk")]
    [InlineData(new[] { "--verbose", "book.cbk", "other.cbk" }, "book.cbk")]
    [InlineData(new[] { "-x" }, null)]
    [InlineData(new string[0], null)]
    public void The_first_argument_that_is_not_an_option_is_the_path(string[] args, string? expected) =>
        Assert.Equal(expected, StartupPath.FromArguments(args));
}
