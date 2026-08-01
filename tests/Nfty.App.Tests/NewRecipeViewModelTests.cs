using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

public class NewRecipeViewModelTests
{
    private static NewRecipeViewModel Make(out FakeDialogs d, out FakeNotYetWired n)
    { d = new FakeDialogs(); n = new FakeNotYetWired(); return new NewRecipeViewModel(d, n); }

    [Fact]
    public void Choosing_loose_kitchen_disables_the_weight_field()
    {
        var vm = Make(out _, out _);
        vm.Destination = RecipeDestination.LooseKitchen;
        Assert.False(vm.WeightEnabled);
        vm.Destination = RecipeDestination.IntoCookBook;
        Assert.True(vm.WeightEnabled);
    }

    [Fact]
    public void DerivedId_slugs_the_name()
    {
        var vm = Make(out _, out _); vm.Name = "Night Sky";
        Assert.Equal("night-sky", vm.DerivedId);
    }

    [Fact]
    public void Create_is_disabled_until_the_name_yields_a_non_blank_id()
    {
        var vm = Make(out _, out _);
        Assert.False(vm.CreateCommand.CanExecute(null));
        vm.Name = "  ";
        Assert.False(vm.CreateCommand.CanExecute(null));
        vm.Name = "Bird";
        Assert.True(vm.CreateCommand.CanExecute(null));
    }

    [Fact]
    public async System.Threading.Tasks.Task Create_closes_the_dialog_with_the_vm()
    {
        var real = new DialogService();
        var vm = new NewRecipeViewModel(real, new FakeNotYetWired()) { Name = "Bird" };
        var task = real.ShowAsync<NewRecipeViewModel>(vm);
        vm.CreateCommand.Execute(null);
        Assert.Same(vm, await task);
    }
}
