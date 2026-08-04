using System.Linq;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

public class NewRecipeViewModelTests
{
    private static NewRecipeViewModel Make(out FakeDialogs d, out FakeNotYetWired n)
    { d = new FakeDialogs(); n = new FakeNotYetWired(); return new NewRecipeViewModel(d, n); }

    /// <summary>The "Resulting mix" readout. A selection weight is RELATIVE to its siblings and the
    /// book normalises the set — it is not a percentage. The control this replaces was a ProgressBar
    /// bound Value=Weight Maximum=100, which drew a weight of 100 as a full bar and therefore told
    /// the user their recipe was the whole collection regardless of what the siblings weighed.</summary>
    [Fact]
    public void Share_is_relative_to_siblings_not_an_absolute_percentage()
    {
        var vm = new NewRecipeViewModel(new FakeDialogs(), new FakeNotYetWired(),
            new[] { ("Fox", 45d), ("Owl", 25d) }) { Name = "Cat", Weight = 100 };

        var rows = vm.ShareRows;
        Assert.Equal(3, rows.Count);

        // 100 / (100 + 45 + 25) — emphatically NOT 100%, which is what the old bar showed.
        Assert.Equal(100d / 170 * 100, rows[0].Percent, 3);
        Assert.True(rows[0].IsCurrent);
        Assert.Equal("Cat", rows[0].Name);
        Assert.Equal(45d / 170 * 100, rows[1].Percent, 3);
        Assert.Equal(25d / 170 * 100, rows[2].Percent, 3);
        Assert.All(rows.Skip(1), r => Assert.False(r.IsCurrent));
        Assert.Equal(100d, rows.Sum(r => r.Percent), 3);   // the shares are a whole
    }

    [Fact]
    public void Share_tracks_the_weight_and_hides_where_there_is_nothing_to_share_with()
    {
        var vm = new NewRecipeViewModel(new FakeDialogs(), new FakeNotYetWired(),
            new[] { ("Fox", 100d) }) { Name = "Cat", Weight = 100 };
        Assert.Equal(50d, vm.ShareRows[0].Percent, 3);

        vm.Weight = 300;                       // now three times the sibling
        Assert.Equal(75d, vm.ShareRows[0].Percent, 3);

        // A loose Recipe has no collection to be a share OF, so the panel must not show.
        Assert.True(vm.ShowShare);
        vm.Destination = RecipeDestination.LooseKitchen;
        Assert.False(vm.ShowShare);

        // Nor when the book has no other recipes: a lone recipe is trivially 100% of itself.
        var alone = new NewRecipeViewModel(new FakeDialogs(), new FakeNotYetWired()) { Weight = 100 };
        Assert.False(alone.ShowShare);
    }

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
