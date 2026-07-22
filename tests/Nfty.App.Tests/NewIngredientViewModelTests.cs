using Nfty.App.ViewModels;
using Nfty.Core.Model;
using Xunit;

namespace Nfty.App.Tests;

public class NewIngredientViewModelTests
{
    private static NewIngredientViewModel Make(out FakeDialogs d, out FakeNotYetWired n)
    { d = new FakeDialogs(); n = new FakeNotYetWired(); return new NewIngredientViewModel(d, n); }

    [Fact]
    public void Kind_selects_the_matching_colour_zone()
    {
        var vm = Make(out _, out _);
        vm.Kind = LayerKind.Dynamic; Assert.True(vm.ShowColourRange); Assert.False(vm.ShowFixedColour);
        vm.Kind = LayerKind.Static;  Assert.True(vm.ShowFixedColour); Assert.False(vm.ShowColourRange);
        vm.Kind = LayerKind.Custom;  Assert.False(vm.ShowColourRange); Assert.False(vm.ShowFixedColour);
    }

    [Fact]
    public void Canvas_field_shows_only_when_loose()
    {
        var vm = Make(out _, out _);
        vm.Destination = RecipeDestination.LooseKitchen; Assert.True(vm.ShowCanvas);
        vm.Destination = RecipeDestination.IntoCookBook; Assert.False(vm.ShowCanvas);
    }

    [Fact]
    public void Create_reports_not_yet_wired()
    {
        var vm = Make(out var d, out var n);
        d.ShowAsync<object>(vm); vm.CreateCommand.Execute(null);
        Assert.Equal("Create Ingredient", n.Last);
    }
}
