using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Xunit;

namespace Nfty.App.Tests;

public class ExplorerAddRecipeTests
{
    private sealed class AddRecipeDialogs : IDialogService
    {
        private readonly string _name; private readonly double _weight;
        public string? ErrorTitle { get; private set; }
        public AddRecipeDialogs(string name, double weight = 50) { _name = name; _weight = weight; }
        public ViewModelBase? Active => null;
        public event Action? Changed { add { } remove { } }
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog)
        {
            if (dialog is NewRecipeViewModel w) { w.Name = _name; w.Weight = _weight; return Task.FromResult((TResult?)(object?)w); }
            if (dialog is ErrorDialogViewModel e) { ErrorTitle = e.Title; return Task.FromResult(default(TResult)); }
            return Task.FromResult(default(TResult));
        }
        public void Close(object? result) { }
    }

    private static (ExplorerViewModel vm, CookBookSession session, string path) Explorer(IDialogService dialogs)
    {
        (var path, var session, _, _) = IngredientEditorSaveTests.OnDisk();
        var nav = new FakeNav();
        var vm = new ExplorerViewModel(session.Current!, nav, dialogs, new FakeNotYetWired(), new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
            ExplorerViewModelTests.CookFactory(dialogs), session);
        return (vm, session, path);
    }

    [AvaloniaFact]
    public async Task Add_recipe_on_the_root_persists_with_weight_and_selects_it()
    {
        var dialogs = new AddRecipeDialogs("Bird", 25);
        var (vm, session, path) = Explorer(dialogs);
        try
        {
            vm.ToggleLockCommand.Execute(null);
            vm.SelectNodeCommand.Execute(vm.Root);          // cookbook root
            await vm.AddCommand.ExecuteAsync(null);

            using var reread = CookBookArchive.Read(path);
            Assert.Contains(reread.Recipes, r => r.Manifest.Id == "bird");
            Assert.Equal(25, reread.Manifest.RecipeWeights["bird"]);
            Assert.Equal("bird", vm.SelectedNode!.Id);       // new recipe selected
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Add_recipe_duplicate_or_blank_reports_and_writes_nothing()
    {
        foreach (var name in new[] { "cat", "   " })   // existing id / blank
        {
            var dialogs = new AddRecipeDialogs(name);
            var (vm, session, path) = Explorer(dialogs);
            try
            {
                var before = CookBookArchive.Read(path).Recipes.Count;
                vm.ToggleLockCommand.Execute(null);
                vm.SelectNodeCommand.Execute(vm.Root);
                await vm.AddCommand.ExecuteAsync(null);
                Assert.NotNull(dialogs.ErrorTitle);
                using var reread = CookBookArchive.Read(path);
                Assert.Equal(before, reread.Recipes.Count);
                vm.Dispose();
            }
            finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
        }
    }
}
