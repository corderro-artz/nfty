using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

public class LandingViewModelTests
{
    private static LandingViewModel Make(out FakeNotYetWired notify, out FakeDialogs dialogs)
    {
        notify = new FakeNotYetWired();
        dialogs = new FakeDialogs();
        return new LandingViewModel(new FakeNav(), dialogs, notify,
            new FilePickerService(), new RecentsService());
    }

    [Fact]
    public void New_cookbook_opens_the_wizard_dialog()
    {
        var vm = Make(out _, out var dialogs);
        vm.NewCookBookCommand.Execute(null);
        Assert.IsType<NewCookBookViewModel>(dialogs.Active);
    }

    [Fact]
    public void Open_cookbook_reports_not_yet_wired()
    {
        var vm = Make(out var notify, out _);
        vm.OpenCookBookCommand.Execute(null);
        Assert.Equal("Open CookBook", notify.Last);
    }

    [Fact]
    public void Recents_are_exposed_for_the_list()
    {
        var vm = Make(out _, out _);
        Assert.NotEmpty(vm.Recents);
    }

    [Fact]
    public void New_kitchen_is_disabled_reserved()
    {
        var vm = Make(out _, out _);
        Assert.False(vm.NewKitchenCommand.CanExecute(null));
    }
}
