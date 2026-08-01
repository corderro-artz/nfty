using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Xunit;

namespace Nfty.App.Tests;

public class LandingNewCookBookTests
{
    private sealed class SavePicker : IFilePickerService
    {
        private readonly string? _save;
        public SavePicker(string? save) => _save = save;
        public Task<string?> OpenFileAsync(string title, params string[] extensions) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string title, string defaultExtension) => Task.FromResult(_save);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    // Fills the New-CookBook wizard and "clicks Create"; records any error dialog.
    private sealed class WizardDialogs : IDialogService
    {
        private readonly string _name;
        public string? ErrorTitle { get; private set; }
        public WizardDialogs(string name) => _name = name;
        public ViewModelBase? Active => null;
        public event Action? Changed { add { } remove { } }
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog)
        {
            if (dialog is NewCookBookViewModel w)
            { w.Name = _name; w.Symbol = "VP"; w.Width = 64; w.Height = 64; w.Description = "d";
              return Task.FromResult((TResult?)(object?)w); }
            if (dialog is ErrorDialogViewModel e) { ErrorTitle = e.Title; return Task.FromResult(default(TResult)); }
            return Task.FromResult(default(TResult));
        }
        public void Close(object? result) { }
    }

    private static (LandingViewModel vm, FakeNav nav, CookBookSession session) Landing(
        IDialogService dialogs, IFilePickerService picker)
    {
        var nav = new FakeNav(); var notify = new FakeNotYetWired(); var session = new CookBookSession();
        var vm = new LandingViewModel(nav, dialogs, notify, picker, new RecentsService(), session,
            book => new ExplorerViewModel(book, nav, dialogs, notify, new ImageBridge(),
                ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
                ExplorerViewModelTests.CookFactory(dialogs), session,
                picker, ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs)),
            set => new SetBrowserViewModel(set),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs));
        return (vm, nav, session);
    }

    [AvaloniaFact]
    public async Task New_cookbook_writes_a_cbk_and_opens_the_explorer()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "vapor.cbk");
        var (vm, nav, session) = Landing(new WizardDialogs("Vapor Pets"), new SavePicker(path));
        try
        {
            await vm.NewCookBookCommand.ExecuteAsync(null);
            Assert.True(File.Exists(path));
            using (var reread = CookBookArchive.Read(path))
            {
                Assert.Equal("vapor-pets", reread.Manifest.Id);
                Assert.Equal(64, reread.Manifest.Canvas.Width);
                Assert.Empty(reread.Recipes);                     // empty starting book
            }
            Assert.IsType<ExplorerViewModel>(nav.Current);        // opened in the Explorer
            Assert.NotNull(session.Current);
            Assert.Equal(path, session.SourcePath);               // source set → Add/Save/Cook enabled
        }
        finally { session.Dispose(); Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Cancelling_the_save_picker_writes_nothing()
    {
        var (vm, nav, session) = Landing(new WizardDialogs("Vapor Pets"), new SavePicker(null));
        try
        {
            await vm.NewCookBookCommand.ExecuteAsync(null);
            Assert.Null(nav.Current);
            Assert.Null(session.Current);
        }
        finally { session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task A_blank_name_errors_and_writes_nothing()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "vapor.cbk");
        var dialogs = new WizardDialogs("   ");
        var (vm, nav, session) = Landing(dialogs, new SavePicker(path));
        try
        {
            await vm.NewCookBookCommand.ExecuteAsync(null);
            Assert.NotNull(dialogs.ErrorTitle);
            Assert.False(File.Exists(path));
            Assert.Null(nav.Current);
        }
        finally { session.Dispose(); Directory.Delete(dir, recursive: true); }
    }
}
