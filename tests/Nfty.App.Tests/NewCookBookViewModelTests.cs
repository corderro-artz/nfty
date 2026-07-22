using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

public class NewCookBookViewModelTests
{
    private static NewCookBookViewModel Make(out FakeDialogs dialogs, out FakeNotYetWired notify)
    { dialogs = new FakeDialogs(); notify = new FakeNotYetWired(); return new NewCookBookViewModel(dialogs, notify); }

    [Fact]
    public void Derived_id_lowercases_and_hyphenates_the_name()
    {
        var vm = Make(out _, out _);
        vm.Name = "Vapor Pets";
        Assert.Equal("vapor-pets", vm.DerivedId);
    }

    [Fact]
    public void Aspect_lock_scales_height_when_width_changes()
    {
        var vm = Make(out _, out _);
        vm.Width = 1000; vm.Height = 1000; vm.AspectLocked = true;
        vm.Width = 500;
        Assert.Equal(500, vm.Height);
    }

    [Fact]
    public void Create_reports_not_yet_wired_and_closes()
    {
        var vm = Make(out var dialogs, out var notify);
        dialogs.ShowAsync<object>(vm);
        vm.CreateCommand.Execute(null);
        Assert.Equal("Create CookBook", notify.Last);
        Assert.Null(dialogs.Active);
    }

    [Fact]
    public void Cancel_closes_without_reporting()
    {
        var vm = Make(out var dialogs, out var notify);
        dialogs.ShowAsync<object>(vm);
        vm.CancelCommand.Execute(null);
        Assert.Null(dialogs.Active);
        Assert.Null(notify.Last);
    }
}
