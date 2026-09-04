using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

public class NewCookBookViewModelTests
{
    private static NewCookBookViewModel Make(out FakeDialogs dialogs)
    { dialogs = new FakeDialogs(); return new NewCookBookViewModel(dialogs); }

    [Fact]
    public void Derived_id_lowercases_and_hyphenates_the_name()
    {
        var vm = Make(out _);
        vm.Name = "Vapor Pets";
        Assert.Equal("vapor-pets", vm.DerivedId);
    }

    [Fact]
    public void Aspect_lock_scales_height_when_width_changes()
    {
        var vm = Make(out _);
        vm.Width = 1000; vm.Height = 1000; vm.AspectLocked = true;
        vm.Width = 500;
        Assert.Equal(500, vm.Height);
    }

    [Fact]
    public void Aspect_lock_scales_width_when_height_changes()
    {
        var vm = Make(out _);
        // AspectLocked defaults to true (locked-by-default wizard), so toggle off then back on to
        // force a genuine false->true transition — CommunityToolkit's generated OnXxxChanged hooks
        // only fire on an actual value change, so re-assigning "true" while already true is a no-op.
        vm.AspectLocked = false;
        vm.Width = 800; vm.Height = 600;
        vm.AspectLocked = true;                                    // lock captures 800:600 = 4:3
        vm.Height = 300;
        Assert.Equal(400, vm.Width);                               // 300 * (800/600) = 400
    }

    [Fact]
    public void Create_is_disabled_until_the_name_yields_a_non_blank_id()
    {
        var vm = Make(out _);
        Assert.False(vm.CreateCommand.CanExecute(null));
        vm.Name = "   ";
        Assert.False(vm.CreateCommand.CanExecute(null));
        vm.Name = "Vapor Pets";
        Assert.True(vm.CreateCommand.CanExecute(null));
    }

    [Fact]
    public async System.Threading.Tasks.Task Create_closes_the_dialog_with_the_vm()
    {
        var real = new DialogService();
        var vm = new NewCookBookViewModel(real) { Name = "Vapor Pets" };
        var task = real.ShowAsync<NewCookBookViewModel>(vm);
        vm.CreateCommand.Execute(null);
        Assert.Same(vm, await task);
    }

    [Fact]
    public void Cancel_closes_the_dialog()
    {
        var vm = Make(out var dialogs);
        dialogs.ShowAsync<object>(vm);
        vm.CancelCommand.Execute(null);
        Assert.Null(dialogs.Active);
    }
}
