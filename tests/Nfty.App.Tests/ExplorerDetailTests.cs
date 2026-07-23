using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

public class ExplorerDetailTests
{
    // Cook_reports_not_yet_wired moved to CookBookDetailViewModelTests, which now
    // constructs CookBookDetailViewModel with a real LoadedCookBook (Task 3).

    [Fact]
    public void Reroll_changes_the_roll_seed()
    {
        var vm = new RecipeDetailViewModel(new FakeNotYetWired(), _ => { });
        var before = vm.RollSeed;
        vm.RerollCommand.Execute(null);
        Assert.NotEqual(before, vm.RollSeed);
    }

    [Fact]
    public void Sort_sets_the_active_column()
    {
        var vm = new IngredientDetailViewModel(new FakeNotYetWired(), () => { }, () => false);
        vm.SortByCommand.Execute("Weight");
        Assert.Equal("Weight", vm.SortColumn);
    }

    [Fact]
    public void Delete_variant_enabled_only_when_editing()
    {
        bool editing = false;
        var vm = new IngredientDetailViewModel(new FakeNotYetWired(), () => { }, () => editing);
        Assert.False(vm.DeleteVariantCommand.CanExecute(null));
        editing = true;
        vm.RaiseCanExecuteChanged();
        Assert.True(vm.DeleteVariantCommand.CanExecute(null));
    }
}
