using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>The New Recipe wizard's "The Kitchen" destination.
///
/// It used to be accepted and then discarded: <c>AddRecipe</c> had no branch for it at all, so
/// choosing Kitchen still added the recipe to the open CookBook. A choice the UI offers, takes, and
/// silently ignores is worse than one it does not offer — and nothing failed, because no test knew
/// the option existed.</summary>
public class ExplorerAddLooseRecipeTests
{
    private sealed class SavePicker : IFilePickerService
    {
        private readonly string? _save;
        public SavePicker(string? save) => _save = save;
        public Task<string?> OpenFileAsync(string title, params string[] extensions) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string title, string defaultExtension) => Task.FromResult(_save);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    /// <summary>Fills the New-Recipe wizard and "clicks Create" with the Kitchen destination.</summary>
    private sealed class LooseRecipeDialogs : IDialogService
    {
        private readonly string _name;
        public string? ErrorTitle { get; private set; }
        public LooseRecipeDialogs(string name) => _name = name;
        public ViewModelBase? Active => null;
        public event Action? Changed { add { } remove { } }
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog)
        {
            if (dialog is NewRecipeViewModel w)
            {
                w.Name = _name;
                w.Destination = RecipeDestination.LooseKitchen;
                return Task.FromResult((TResult?)(object?)w);
            }
            if (dialog is ErrorDialogViewModel e) { ErrorTitle = e.Title; return Task.FromResult(default(TResult)); }
            return Task.FromResult(default(TResult));
        }
        public void Close(object? result) { }
    }

    [AvaloniaFact]
    public async Task Kitchen_destination_writes_an_rcp_and_leaves_the_cookbook_untouched()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var rcpPath = Path.Combine(dir, "fox.rcp");
        (var cbkPath, var session, _, _) = IngredientEditorSaveTests.OnDisk();
        var nav = new FakeNav();
        var dialogs = new LooseRecipeDialogs("Fox");
        var status = new StatusService();
        var vm = new ExplorerViewModel(session.Current!, nav, dialogs, new FakeNotYetWired(), new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
            ExplorerViewModelTests.CookFactory(dialogs), session,
            new SavePicker(rcpPath),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), status);
        try
        {
            var before = session.Current!.Recipes.Count;
            vm.ToggleLockCommand.Execute(null);          // unlock
            vm.SelectNodeCommand.Execute(vm.Root);       // cookbook selected → Add means "add recipe"
            await vm.AddCommand.ExecuteAsync(null);

            // It went to disk as a real .rcp...
            Assert.True(File.Exists(rcpPath));
            using (var read = RecipeArchive.Read(rcpPath))
                Assert.Equal("fox", read.Manifest.Id);

            // ...and NOT into the cookbook, which is the whole point of the choice. Asserted against
            // the session's live book and the .cbk on disk, since the tree's Root is a pre-add
            // snapshot and asserting only on it could not fail.
            Assert.Equal(before, session.Current!.Recipes.Count);
            using (var onDisk = CookBookArchive.Read(cbkPath))
                Assert.Equal(before, onDisk.Recipes.Count);

            Assert.Null(dialogs.ErrorTitle);
            Assert.NotNull(status.Last);                 // tells the user where it went
            vm.Dispose();
        }
        finally
        {
            session.Dispose();
            Directory.Delete(Path.GetDirectoryName(cbkPath)!, recursive: true);
            Directory.Delete(dir, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Cancelling_the_save_picker_writes_nothing()
    {
        (var cbkPath, var session, _, _) = IngredientEditorSaveTests.OnDisk();
        var nav = new FakeNav();
        var dialogs = new LooseRecipeDialogs("Fox");
        var vm = new ExplorerViewModel(session.Current!, nav, dialogs, new FakeNotYetWired(), new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
            ExplorerViewModelTests.CookFactory(dialogs), session,
            new SavePicker(null),   // user cancelled
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), new StatusService());
        try
        {
            var before = session.Current!.Recipes.Count;
            vm.ToggleLockCommand.Execute(null);
            vm.SelectNodeCommand.Execute(vm.Root);
            await vm.AddCommand.ExecuteAsync(null);

            Assert.Equal(before, session.Current!.Recipes.Count);   // and no fallback into the book
            Assert.Null(dialogs.ErrorTitle);
            vm.Dispose();
        }
        finally
        {
            session.Dispose();
            Directory.Delete(Path.GetDirectoryName(cbkPath)!, recursive: true);
        }
    }
}
