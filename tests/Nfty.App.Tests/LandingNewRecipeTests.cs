using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// Landing's "+ Recipe" button.
/// </summary>
/// <remarks>
/// It used to be <c>_dialogs.ShowAsync&lt;object&gt;(new NewRecipeViewModel(_dialogs))</c>: the
/// wizard opened, took a name, a weight and a destination, and its result was discarded — a dead
/// button beside a live one that looks identical. Nothing referenced the command, so nothing failed;
/// it was found by driving the app. These mirror <see cref="LandingNewIngredientTests"/>, the
/// sibling flow that was wired all along.
/// </remarks>
public class LandingNewRecipeTests
{
    private sealed class SavePicker : IFilePickerService
    {
        private readonly string? _save;
        public SavePicker(string? save) => _save = save;
        public Task<string?> OpenFileAsync(string title, params string[] extensions) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string title, string defaultExtension) => Task.FromResult(_save);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private sealed class WizardDialogs : IDialogService
    {
        private readonly string _name;
        private readonly RecipeDestination _dest;
        public string? ErrorTitle { get; private set; }
        public WizardDialogs(string name, RecipeDestination dest = RecipeDestination.LooseKitchen)
        { _name = name; _dest = dest; }
        public ViewModelBase? Active => null;
        public event Action? Changed { add { } remove { } }
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog)
        {
            if (dialog is NewRecipeViewModel w)
            { w.Name = _name; w.Destination = _dest; return Task.FromResult((TResult?)(object?)w); }
            if (dialog is ErrorDialogViewModel e) { ErrorTitle = e.Title; return Task.FromResult(default(TResult)); }
            return Task.FromResult(default(TResult));
        }
        public void Close(object? result) { }
    }

    private static (LandingViewModel vm, FakeNav nav) Landing(IDialogService dialogs, IFilePickerService picker)
    {
        var nav = new FakeNav(); var session = new CookBookSession();
        var vm = new LandingViewModel(nav, dialogs, picker,
            new RecentsService(Directory.CreateTempSubdirectory().FullName), session,
            book => new ExplorerViewModel(book, nav, dialogs, new ImageBridge(),
                ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
                ExplorerViewModelTests.CookFactory(dialogs), session,
                new FilePickerService(), ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), new StatusService()),
            set => new SetBrowserViewModel(set),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs));
        return (vm, nav);
    }

    [AvaloniaFact]
    public async Task New_recipe_writes_an_rcp_and_opens_it()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "cat.rcp");
        var (vm, nav) = Landing(new WizardDialogs("Cat"), new SavePicker(path));
        try
        {
            await vm.NewRecipeCommand.ExecuteAsync(null);

            Assert.True(File.Exists(path));
            using var reread = RecipeArchive.Read(path);
            Assert.Equal("cat", reread.Manifest.Id);
            Assert.Equal("Cat", reread.Manifest.Name);
            Assert.Empty(reread.Manifest.LayerOrder);          // a fresh recipe is empty on purpose
            Assert.IsType<ExplorerViewModel>(nav.Current);     // opened, wrapped as a read-only book
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>Landing has no CookBook open, so the wizard's other destination has nothing to add to.</summary>
    [AvaloniaFact]
    public async Task Into_cookbook_from_landing_errors_and_writes_nothing()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "cat.rcp");
        var dialogs = new WizardDialogs("Cat", RecipeDestination.IntoCookBook);
        var (vm, nav) = Landing(dialogs, new SavePicker(path));
        try
        {
            await vm.NewRecipeCommand.ExecuteAsync(null);

            Assert.NotNull(dialogs.ErrorTitle);
            Assert.False(File.Exists(path));
            Assert.Null(nav.Current);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Canceling_the_save_picker_writes_nothing()
    {
        var dialogs = new WizardDialogs("Cat");
        var (vm, nav) = Landing(dialogs, new SavePicker(null));

        await vm.NewRecipeCommand.ExecuteAsync(null);

        Assert.Null(nav.Current);
        Assert.Null(dialogs.ErrorTitle);   // a clean cancel is not an error
    }

    [AvaloniaFact]
    public async Task Write_failure_shows_an_error_and_opens_nothing()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var badPath = Path.Combine(dir, "nope", "cat.rcp");   // parent missing → Write throws
        var dialogs = new WizardDialogs("Cat");
        var (vm, nav) = Landing(dialogs, new SavePicker(badPath));
        try
        {
            await vm.NewRecipeCommand.ExecuteAsync(null);

            Assert.NotNull(dialogs.ErrorTitle);
            Assert.False(File.Exists(badPath));
            Assert.Null(nav.Current);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
