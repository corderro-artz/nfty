using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

public class RecipeDetailViewModelTests
{
    [Fact]
    public void Layer_table_follows_layer_order()
    {
        var book = ExplorerViewModelTests.TwoRecipeBook();
        var cat = book.Recipes.First(r => r.Manifest.Id == "cat");
        var vm = new RecipeDetailViewModel(cat, book, new FakeNotYetWired(), _ => { });
        Assert.Equal(new[] { "bg", "aura" }, vm.Layers.Select(l => l.Layer));
        Assert.All(vm.Layers, l => Assert.Equal(1, l.VariantCount));
        Assert.Empty(vm.Rules);   // TwoRecipeBook has no rules
    }

    [Fact]
    public void Reroll_changes_the_roll_seed_and_open_ingredient_invokes_callback()
    {
        var book = ExplorerViewModelTests.TwoRecipeBook();
        var cat = book.Recipes.First(r => r.Manifest.Id == "cat");
        string? opened = null;
        var vm = new RecipeDetailViewModel(cat, book, new FakeNotYetWired(), id => opened = id);
        var before = vm.RollSeed; vm.RerollCommand.Execute(null); Assert.NotEqual(before, vm.RollSeed);
        vm.OpenIngredientCommand.Execute("aura"); Assert.Equal("aura", opened);
    }
}
