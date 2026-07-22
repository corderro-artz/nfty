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
    public void Create_reports_not_yet_wired()
    {
        var vm = Make(out var d, out var n);
        d.ShowAsync<object>(vm);
        vm.CreateCommand.Execute(null);
        Assert.Equal("Create Recipe", n.Last);
        Assert.Null(d.Active);
    }
}
