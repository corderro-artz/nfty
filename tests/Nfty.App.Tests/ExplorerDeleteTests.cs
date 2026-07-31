using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Models;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Xunit;

namespace Nfty.App.Tests;

public class ExplorerDeleteTests
{
    private static ExplorerViewModel Explorer(out CookBookSession session, out string path, IDialogService dialogs)
    {
        (path, session, _, _) = IngredientEditorSaveTests.OnDisk();
        var nav = new FakeNav();
        return new ExplorerViewModel(session.Current!, nav, dialogs, new FakeNotYetWired(), new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
            ExplorerViewModelTests.CookFactory(dialogs), session);
    }

    [AvaloniaFact]
    public async Task Delete_ingredient_persists_and_reselects_the_recipe()
    {
        var vm = Explorer(out var session, out var path, new ConfirmingDialogs(true));
        try
        {
            vm.ToggleLockCommand.Execute(null);                       // IsEditing = true
            var recipeNode = vm.Root.Children[0];
            var ingNode = recipeNode.Children[0];
            vm.SelectNodeCommand.Execute(ingNode);
            Assert.True(vm.DeleteSelectedCommand.CanExecute(null));
            await vm.DeleteSelectedCommand.ExecuteAsync(null);

            using var reread = CookBookArchive.Read(path);
            Assert.DoesNotContain(reread.Recipes[0].Ingredients, i => i.Manifest.Id == ingNode.Id);
            Assert.Equal(recipeNode.Id, vm.SelectedNode!.Id);        // parent recipe reselected
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Delete_recipe_persists_and_selects_the_root()
    {
        var vm = Explorer(out var session, out var path, new ConfirmingDialogs(true));
        try
        {
            vm.ToggleLockCommand.Execute(null);
            var recipeNode = vm.Root.Children[0];
            vm.SelectNodeCommand.Execute(recipeNode);
            await vm.DeleteSelectedCommand.ExecuteAsync(null);
            using var reread = CookBookArchive.Read(path);
            Assert.DoesNotContain(reread.Recipes, r => r.Manifest.Id == recipeNode.Id);
            Assert.Equal(vm.Root.Id, vm.SelectedNode!.Id);
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Delete_is_cancelled_when_the_confirm_is_declined()
    {
        var vm = Explorer(out var session, out var path, new ConfirmingDialogs(false));
        try
        {
            vm.ToggleLockCommand.Execute(null);
            vm.SelectNodeCommand.Execute(vm.Root.Children[0]);
            await vm.DeleteSelectedCommand.ExecuteAsync(null);
            using var reread = CookBookArchive.Read(path);
            Assert.Contains(reread.Recipes, r => r.Manifest.Id == "cat");   // declined → still there
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [AvaloniaFact]
    public void CanDelete_requires_editing_a_source_file_and_a_non_root_node()
    {
        var vm = Explorer(out var session, out var path, new FakeDialogs());
        try
        {
            vm.SelectNodeCommand.Execute(vm.Root.Children[0]);       // recipe, but not editing
            Assert.False(vm.DeleteSelectedCommand.CanExecute(null)); // lock on → disabled
            vm.ToggleLockCommand.Execute(null);
            Assert.True(vm.DeleteSelectedCommand.CanExecute(null));  // editing + recipe + source
            vm.SelectNodeCommand.Execute(vm.Root);                   // cookbook root
            Assert.False(vm.DeleteSelectedCommand.CanExecute(null)); // root not deletable
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    private sealed class ConfirmingDialogs : IDialogService
    {
        private readonly bool _v;
        public ConfirmingDialogs(bool v) => _v = v;
        public ViewModelBase? Active => null;
        public event Action? Changed { add { } remove { } }
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase d) => Task.FromResult((TResult?)(object?)_v);
        public void Close(object? result) { }
    }
}
