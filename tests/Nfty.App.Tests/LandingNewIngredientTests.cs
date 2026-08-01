using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Xunit;

namespace Nfty.App.Tests;

public class LandingNewIngredientTests
{
    private sealed class SavePicker : IFilePickerService
    {
        private readonly string? _save;
        public SavePicker(string? save) => _save = save;
        public Task<string?> OpenFileAsync(string title, params string[] extensions) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string title, string defaultExtension) => Task.FromResult(_save);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    // Fills the New-Ingredient wizard as Loose with a name/canvas and "clicks Create" (returns it);
    // records any error dialog.
    private sealed class WizardDialogs : IDialogService
    {
        private readonly string _name; private readonly string _canvas; private readonly RecipeDestination _dest;
        public string? ErrorTitle { get; private set; }
        public WizardDialogs(string name, string canvas, RecipeDestination dest = RecipeDestination.LooseKitchen)
        { _name = name; _canvas = canvas; _dest = dest; }
        public ViewModelBase? Active => null;
        public event Action? Changed { add { } remove { } }
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog)
        {
            if (dialog is NewIngredientViewModel w)
            { w.Name = _name; w.Kind = LayerKind.Dynamic; w.CanvasSize = _canvas; w.Destination = _dest;
              return Task.FromResult((TResult?)(object?)w); }
            if (dialog is ErrorDialogViewModel e) { ErrorTitle = e.Title; return Task.FromResult(default(TResult)); }
            return Task.FromResult(default(TResult));
        }
        public void Close(object? result) { }
    }

    private static (LandingViewModel vm, FakeNav nav) Landing(IDialogService dialogs, IFilePickerService picker)
    {
        var nav = new FakeNav(); var notify = new FakeNotYetWired(); var session = new CookBookSession();
        var vm = new LandingViewModel(nav, dialogs, notify, picker, new RecentsService(), session,
            book => new ExplorerViewModel(book, nav, dialogs, notify, new ImageBridge(),
                ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
                ExplorerViewModelTests.CookFactory(dialogs), session),
            set => new SetBrowserViewModel(set),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs));
        return (vm, nav);
    }

    [AvaloniaFact]
    public async Task New_ingredient_writes_an_igt_and_opens_the_editor()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "hat.igt");
        var (vm, nav) = Landing(new WizardDialogs("Hat", "8x8"), new SavePicker(path));
        try
        {
            await vm.NewIngredientCommand.ExecuteAsync(null);
            Assert.True(File.Exists(path));
            using var reread = IngredientArchive.Read(path);
            Assert.Equal("hat", reread.Manifest.Id);
            Assert.Single(reread.Manifest.Variants);
            Assert.Equal(8, reread.VariantImages["variant-1"].Width);
            Assert.IsType<IngredientEditorViewModel>(nav.Current);   // opened in the editor
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Into_cookbook_from_landing_errors_and_writes_nothing()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "hat.igt");
        var dialogs = new WizardDialogs("Hat", "8x8", RecipeDestination.IntoCookBook);
        var (vm, nav) = Landing(dialogs, new SavePicker(path));
        try
        {
            await vm.NewIngredientCommand.ExecuteAsync(null);
            Assert.NotNull(dialogs.ErrorTitle);
            Assert.False(File.Exists(path));
            Assert.Null(nav.Current);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Cancelling_the_save_picker_writes_nothing()
    {
        var dialogs = new WizardDialogs("Hat", "8x8");
        var (vm, nav) = Landing(dialogs, new SavePicker(null));   // picker cancelled
        await vm.NewIngredientCommand.ExecuteAsync(null);
        Assert.Null(nav.Current);
        Assert.Null(dialogs.ErrorTitle);   // clean cancel — no error dialog
    }

    [AvaloniaFact]
    public async Task Write_failure_shows_an_error_and_opens_nothing()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var badPath = Path.Combine(dir, "nope", "hat.igt");   // parent dir doesn't exist → Write throws
        var dialogs = new WizardDialogs("Hat", "8x8");
        var (vm, nav) = Landing(dialogs, new SavePicker(badPath));
        try
        {
            await vm.NewIngredientCommand.ExecuteAsync(null);
            Assert.NotNull(dialogs.ErrorTitle);        // error surfaced
            Assert.False(File.Exists(badPath));        // nothing written
            Assert.Null(nav.Current);                  // editor not opened
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public async Task A_huge_canvas_is_rejected_before_the_save_prompt()
    {
        var dialogs = new WizardDialogs("Hat", "50000x50000");   // > 100M px cap
        var (vm, nav) = Landing(dialogs, new SavePicker("unused.igt"));
        await vm.NewIngredientCommand.ExecuteAsync(null);
        Assert.Equal("Invalid canvas", dialogs.ErrorTitle);   // rejected by TryGetCanvas
        Assert.Null(nav.Current);
    }
}
