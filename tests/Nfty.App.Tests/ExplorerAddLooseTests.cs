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

public class ExplorerAddLooseTests
{
    private sealed class SavePicker : IFilePickerService
    {
        private readonly string? _save;
        public SavePicker(string? save) => _save = save;
        public Task<string?> OpenFileAsync(string title, params string[] extensions) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string title, string defaultExtension) => Task.FromResult(_save);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    // Fills the New-Ingredient wizard the Explorer shows as LOOSE with a name/canvas + "clicks Create".
    private sealed class LooseWizardDialogs : IDialogService
    {
        private readonly string _name; private readonly string _canvas;
        public string? ErrorTitle { get; private set; }
        public LooseWizardDialogs(string name, string canvas) { _name = name; _canvas = canvas; }
        public ViewModelBase? Active => null;
        public event Action? Changed { add { } remove { } }
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog)
        {
            if (dialog is NewIngredientViewModel w)
            { w.Name = _name; w.Kind = LayerKind.Dynamic; w.CanvasSize = _canvas;
              w.Destination = RecipeDestination.LooseKitchen; return Task.FromResult((TResult?)(object?)w); }
            if (dialog is ErrorDialogViewModel e) { ErrorTitle = e.Title; return Task.FromResult(default(TResult)); }
            return Task.FromResult(default(TResult));
        }
        public void Close(object? result) { }
    }

    private static (ExplorerViewModel vm, CookBookSession session, string cbkDir, FakeNav nav) Explorer(
        IDialogService dialogs, IFilePickerService picker)
    {
        (var cbkPath, var session, _, _) = IngredientEditorSaveTests.OnDisk();
        var nav = new FakeNav();
        var vm = new ExplorerViewModel(session.Current!, nav, dialogs, new FakeNotYetWired(), new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
            ExplorerViewModelTests.CookFactory(dialogs), session,
            picker, ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs));
        return (vm, session, Path.GetDirectoryName(cbkPath)!, nav);
    }

    [AvaloniaFact]
    public async Task Add_loose_writes_an_igt_opens_editor_and_leaves_the_cookbook_untouched()
    {
        var igtDir = Directory.CreateTempSubdirectory().FullName;
        var igtPath = Path.Combine(igtDir, "hat.igt");
        var (vm, session, cbkDir, nav) = Explorer(new LooseWizardDialogs("Hat", "8x8"), new SavePicker(igtPath));
        try
        {
            var recipe = (LoadedRecipe)vm.Root.Children[0].Domain!;
            var before = recipe.Ingredients.Count;
            vm.ToggleLockCommand.Execute(null);
            vm.SelectNodeCommand.Execute(vm.Root.Children[0]);   // recipe "cat"
            await vm.AddCommand.ExecuteAsync(null);

            Assert.True(File.Exists(igtPath));                   // loose .igt written
            using var reread = IngredientArchive.Read(igtPath);
            Assert.Equal("hat", reread.Manifest.Id);
            Assert.Equal(8, reread.VariantImages["variant-1"].Width);
            Assert.IsType<IngredientEditorViewModel>(nav.Current);   // opened a (loose) editor
            Assert.Equal(before, ((LoadedRecipe)vm.Root.Children[0].Domain!).Ingredients.Count);   // cookbook NOT mutated
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(cbkDir, recursive: true); Directory.Delete(igtDir, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Add_loose_cancelled_picker_writes_nothing_and_leaves_the_cookbook_untouched()
    {
        var (vm, session, cbkDir, nav) = Explorer(new LooseWizardDialogs("Hat", "8x8"), new SavePicker(null));
        try
        {
            var before = ((LoadedRecipe)vm.Root.Children[0].Domain!).Ingredients.Count;
            vm.ToggleLockCommand.Execute(null);
            vm.SelectNodeCommand.Execute(vm.Root.Children[0]);
            await vm.AddCommand.ExecuteAsync(null);
            Assert.Null(nav.Current);
            Assert.Equal(before, ((LoadedRecipe)vm.Root.Children[0].Domain!).Ingredients.Count);
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(cbkDir, recursive: true); }
    }
}
