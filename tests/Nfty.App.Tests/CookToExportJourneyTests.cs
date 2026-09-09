using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Output;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// A finished cook hands you the Set it wrote, which is the only route from a CookBook to the export
/// dialog.
/// </summary>
/// <remarks>
/// <para>Export — pick the art, either metadata set, the source <c>.cbk</c>, and whether to seal —
/// lives on the Set browser, because it is an operation on a cooked Set and there is nothing to plan
/// before one exists. But cooking left you on the Explorer looking at a path, so the only way to
/// reach it from the book you had just cooked was to go back to Landing and open that folder by
/// hand. Every part worked; the feature was unreachable from the flow that produces its input, and
/// it read as though the export options had never shipped.</para>
///
/// <para>Two halves, because one test cannot see both: the dialog CLOSES with the folder, and the
/// Explorer TURNS that folder into a browser that can export. A test of either alone passes with the
/// other end disconnected.</para>
/// </remarks>
public class CookToExportJourneyTests
{
    private sealed class FolderPicker(string? folder) : IFilePickerService
    {
        public Task<string?> OpenFileAsync(string t, params string[] e) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string t, string e) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string t) => Task.FromResult(folder);
    }

    /// <summary>Records what a dialog closed WITH, and answers the next Show with a stated result.</summary>
    internal sealed class PathDialogs(object? answer = null) : IDialogService
    {
        public object? Closed;
        public bool DidClose;
        public ViewModelBase? Active { get; private set; }
        public event Action? Changed;
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog)
        {
            Active = dialog;
            Changed?.Invoke();
            return Task.FromResult(answer is TResult t ? t : default);
        }
        public void Close(object? result)
        {
            Closed = result; DidClose = true; Active = null; Changed?.Invoke();
        }
    }

    private sealed class NoRevealer : IFolderRevealer { public void Reveal(string p) { } }

    [AvaloniaFact]
    public async Task Open_Set_closes_the_cook_dialog_with_the_folder_it_wrote()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var dialogs = new PathDialogs();
            using var book = ExplorerViewModelTests.TwoRecipeBook();
            var vm = new CookDialogViewModel(book, new FolderPicker(dir), new NoRevealer(), dialogs);
            vm.Count = 2; vm.Seed = "seed1";

            // Before the run there is nothing to open, and the button is not offered.
            Assert.False(vm.OpenSetCommand.CanExecute(null));

            await vm.CookCommand.ExecuteAsync(null);
            Assert.True(vm.IsDone);
            Assert.True(vm.OpenSetCommand.CanExecute(null));

            vm.OpenSetCommand.Execute(null);
            Assert.True(dialogs.DidClose);
            Assert.Equal(dir, dialogs.Closed);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Cooking_from_the_card_lands_on_a_browser_that_can_export()
    {
        // The far end, and the assertion that matters: not merely that a page was pushed, but that
        // the page is one whose Export button is available. CanExport is what gates it, and it is
        // false for a Set with no source directory — which is exactly what a browser handed the
        // wrong thing would have.
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            using (var source = ExplorerViewModelTests.TwoRecipeBook())
            using (var cooked = Nfty.Core.Generation.Generator.Generate(
                source, new Nfty.Core.Generation.GenerateOptions(2, "seed1")))
            {
                SetWriter.Write(cooked, dir, pack: false);
            }

            var nav = new FakeNav();
            var dialogs = new PathDialogs(dir);
            var session = new CookBookSession();
            using var book = ExplorerViewModelTests.TwoRecipeBook();
            var explorer = new ExplorerViewModel(book, nav, dialogs, new ImageBridge(),
                ExplorerViewModelTests.EditorFactory(nav),
                ExplorerViewModelTests.CookFactory(dialogs), session, new FilePickerService(),
                ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), new StatusService(),
                setBrowserFactory: set => new SetBrowserViewModel(set));

            var card = Assert.IsType<CookBookDetailViewModel>(explorer.CurrentDetail);
            card.CookCommand.Execute(null);
            // The command is fire-and-forget, as the one it replaced was; the fake answers
            // synchronously, so one turn of the loop is enough to let the continuation land.
            await Task.Yield();

            var browser = Assert.IsType<SetBrowserViewModel>(nav.Current);
            Assert.True(browser.CanExport,
                "the browser opened on the cooked Set cannot export it");
            Assert.Equal(2, browser.Items.Count);
            browser.Dispose();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }
}
