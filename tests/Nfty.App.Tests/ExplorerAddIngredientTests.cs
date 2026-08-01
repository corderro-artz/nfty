using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Xunit;

namespace Nfty.App.Tests;

public class ExplorerAddIngredientTests
{
    // A dialog stub that acts as the user: fills the New-Ingredient wizard the Explorer shows and
    // "clicks Create" (returns it), and records any error dialog shown.
    private sealed class AddDialogs : IDialogService
    {
        private readonly string _name; private readonly LayerKind _kind;
        public string? ErrorTitle { get; private set; }
        public AddDialogs(string name, LayerKind kind) { _name = name; _kind = kind; }
        public ViewModelBase? Active => null;
        public event Action? Changed { add { } remove { } }
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog)
        {
            if (dialog is NewIngredientViewModel w) { w.Name = _name; w.Kind = _kind; return Task.FromResult((TResult?)(object?)w); }
            if (dialog is ErrorDialogViewModel e) { ErrorTitle = e.Title; return Task.FromResult(default(TResult)); }
            return Task.FromResult(default(TResult));
        }
        public void Close(object? result) { }
    }

    private static (ExplorerViewModel vm, CookBookSession session, string path, FakeNav nav) Explorer(IDialogService dialogs)
    {
        (var path, var session, _, _) = IngredientEditorSaveTests.OnDisk();
        var nav = new FakeNav();
        var vm = new ExplorerViewModel(session.Current!, nav, dialogs, new FakeNotYetWired(), new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
            ExplorerViewModelTests.CookFactory(dialogs), session);
        return (vm, session, path, nav);
    }

    [AvaloniaFact]
    public async Task Add_ingredient_persists_selects_and_opens_the_editor()
    {
        var dialogs = new AddDialogs("Hat", LayerKind.Dynamic);
        var (vm, session, path, nav) = Explorer(dialogs);
        try
        {
            vm.ToggleLockCommand.Execute(null);
            vm.SelectNodeCommand.Execute(vm.Root.Children[0]);   // recipe "cat"
            await vm.AddCommand.ExecuteAsync(null);

            using var reread = CookBookArchive.Read(path);
            Assert.Contains(reread.Recipes[0].Ingredients, i => i.Manifest.Id == "hat");
            Assert.Equal("hat", vm.SelectedNode!.Id);              // new ingredient selected
            Assert.IsType<IngredientEditorViewModel>(nav.Current); // editor opened on it
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Add_duplicate_id_reports_an_error_and_writes_nothing()
    {
        var dialogs = new AddDialogs("aura", LayerKind.Dynamic);   // "aura" already exists in "cat"
        var (vm, session, path, nav) = Explorer(dialogs);
        try
        {
            vm.ToggleLockCommand.Execute(null);
            vm.SelectNodeCommand.Execute(vm.Root.Children[0]);
            await vm.AddCommand.ExecuteAsync(null);
            Assert.NotNull(dialogs.ErrorTitle);                    // error surfaced
            using var reread = CookBookArchive.Read(path);
            Assert.Single(reread.Recipes[0].Ingredients, i => i.Manifest.Id == "aura");  // still one
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Add_on_a_recipe_without_editing_is_a_no_op_stub()
    {
        var dialogs = new AddDialogs("Hat", LayerKind.Dynamic);
        var (vm, session, path, nav) = Explorer(dialogs);
        try
        {
            vm.SelectNodeCommand.Execute(vm.Root.Children[0]);     // recipe, but lock is OFF
            await vm.AddCommand.ExecuteAsync(null);
            using var reread = CookBookArchive.Read(path);
            Assert.DoesNotContain(reread.Recipes[0].Ingredients, i => i.Manifest.Id == "hat");  // nothing added
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }
}
