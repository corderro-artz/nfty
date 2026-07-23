using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Nfty.App;
using Nfty.App.Services;
using Nfty.App.ViewModels;
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
        ViewModelBase[] vms =
        [
            new LandingViewModel(nav, dialogs, notify, new FilePickerService(), new RecentsService(),
                new CookBookSession(), book => new ExplorerViewModel(book, nav, dialogs, notify)),
            new ExplorerViewModel(ExplorerViewModelTests.TwoRecipeBook(), nav, dialogs, notify),
            new IngredientEditorViewModel(nav, notify),
            new HelpViewModel(dialogs),
            new NewCookBookViewModel(dialogs, notify),
            new NewRecipeViewModel(dialogs, notify),
            new NewIngredientViewModel(dialogs, notify),
            new ErrorDialogViewModel(dialogs, "Error", "Could not open the cookbook."),
        ];
        foreach (var vm in vms)
        {
            var control = locator.Build(vm);
            Assert.False(control is TextBlock tb && tb.Text!.StartsWith("View not found"),
                $"No view for {vm.GetType().Name}");
        }
    }

    [AvaloniaFact]
    public void Landing_new_cookbook_opens_then_cancel_closes()
    {
        var dialogs = new DialogService();
        var nav = new FakeNav(); var notify = new FakeNotYetWired();
        var vm = new LandingViewModel(nav, dialogs, notify,
            new FilePickerService(), new RecentsService(), new CookBookSession(),
            book => new ExplorerViewModel(book, nav, dialogs, notify));
        vm.NewCookBookCommand.Execute(null);
        Assert.IsType<NewCookBookViewModel>(dialogs.Active);
        ((NewCookBookViewModel)dialogs.Active!).CancelCommand.Execute(null);
        Assert.Null(dialogs.Active);
    }
}
