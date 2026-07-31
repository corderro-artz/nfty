using System.IO;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Nfty.App;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Generation;
using Nfty.Core.Output;
using Xunit;

namespace Nfty.App.Tests;

public class SmokeTests
{
    [AvaloniaFact]
    public void ViewLocator_resolves_a_view_for_every_page_and_dialog_vm()
    {
        var locator = new ViewLocator();
        var dialogs = new FakeDialogs();
        var notify = new FakeNotYetWired();
        var nav = new FakeNav();
        var editorFactory = ExplorerViewModelTests.EditorFactory(nav);
        var cookFactory = ExplorerViewModelTests.CookFactory(dialogs);
        var smokeBook = ExplorerViewModelTests.TwoRecipeBook();
        var cat = smokeBook.Recipes.First(r => r.Manifest.Id == "cat");

        var setDir = Directory.CreateTempSubdirectory().FullName;
        using var generated = Generator.Generate(CoreTestBook.Tiny(), new GenerateOptions(2, "seed1"));
        SetWriter.Write(generated, setDir, pack: false);
        var loadedSet = SetReader.Read(setDir);   // ownership passes to SetBrowserViewModel below

        ViewModelBase[] vms =
        [
            new LandingViewModel(nav, dialogs, notify, new FilePickerService(), new RecentsService(),
                new CookBookSession(), book => new ExplorerViewModel(book, nav, dialogs, notify, new ImageBridge(), editorFactory, cookFactory, new CookBookSession()),
                set => new SetBrowserViewModel(set)),
            new ExplorerViewModel(smokeBook, nav, dialogs, notify, new ImageBridge(), editorFactory, cookFactory, new CookBookSession()),
            editorFactory(cat.Ingredients[0], cat, smokeBook),
            new HelpViewModel(dialogs),
            new NewCookBookViewModel(dialogs, notify),
            new NewRecipeViewModel(dialogs, notify),
            new NewIngredientViewModel(dialogs, notify),
            new ErrorDialogViewModel(dialogs, "Error", "Could not open the cookbook."),
            new ConfirmDialogViewModel(dialogs, "Discard?", "You have unsaved edits.", "Discard"),
            new CookDialogViewModel(smokeBook, new FilePickerService(), new NoopFolderRevealer(), dialogs),
            new SetBrowserViewModel(loadedSet),
        ];
        foreach (var vm in vms)
        {
            var control = locator.Build(vm);
            Assert.False(control is TextBlock tb && tb.Text!.StartsWith("View not found"),
                $"No view for {vm.GetType().Name}");
        }

        foreach (var vm in vms.OfType<IDisposable>())
            vm.Dispose();
        Directory.Delete(setDir, recursive: true);
    }

    [AvaloniaFact]
    public void Landing_new_cookbook_opens_then_cancel_closes()
    {
        var dialogs = new DialogService();
        var nav = new FakeNav(); var notify = new FakeNotYetWired();
        var vm = new LandingViewModel(nav, dialogs, notify,
            new FilePickerService(), new RecentsService(), new CookBookSession(),
            book => new ExplorerViewModel(book, nav, dialogs, notify, new ImageBridge(), ExplorerViewModelTests.EditorFactory(nav),
                ExplorerViewModelTests.CookFactory(dialogs), new CookBookSession()),
            set => new SetBrowserViewModel(set));
        vm.NewCookBookCommand.Execute(null);
        Assert.IsType<NewCookBookViewModel>(dialogs.Active);
        ((NewCookBookViewModel)dialogs.Active!).CancelCommand.Execute(null);
        Assert.Null(dialogs.Active);
    }
}
