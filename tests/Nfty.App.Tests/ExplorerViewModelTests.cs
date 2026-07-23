using Nfty.App.Models;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public class ExplorerViewModelTests
{
    internal static LoadedCookBook TwoRecipeBook()
    {
        LoadedIngredient Ing(string id) => new()
        {
            Manifest = new IngredientManifest(id, id, LayerKind.Custom, null, new[] { new Variant("a", "A", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["a"] = new Image<Rgba32>(4, 4) },
        };
        LoadedRecipe Rec(string id, params string[] layers) => new()
        {
            Manifest = new RecipeManifest(id, id, layers, Array.Empty<IncompatibilityRule>()),
            Ingredients = layers.Select(Ing).ToArray(),
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "VaporPets", new Dimensions(8, 8),
                new Collection("VaporPets", "", "VP"),
                new Dictionary<string, double> { ["cat"] = 1, ["dog"] = 1 }),
            Recipes = new[] { Rec("cat", "bg", "aura"), Rec("dog", "body") },
        };
    }

    private static ExplorerViewModel Make(out FakeNotYetWired n)
    { n = new FakeNotYetWired(); return new ExplorerViewModel(TwoRecipeBook(), new FakeNav(), new FakeDialogs(), n); }

    [Fact]
    public void Tree_is_built_from_the_cookbook_recipes_and_ingredients()
    {
        var vm = new ExplorerViewModel(TwoRecipeBook(), new FakeNav(), new FakeDialogs(), new FakeNotYetWired());
        Assert.Equal(ExplorerNodeKind.CookBook, vm.Root.Kind);
        Assert.Equal("VaporPets", vm.Root.Name);
        Assert.Equal(new[] { "cat", "dog" }, vm.Root.Children.Select(c => c.Id));
        Assert.Equal(new[] { "bg", "aura" }, vm.Root.Children[0].Children.Select(c => c.Id));
        Assert.All(vm.Root.Children, r => Assert.Equal(ExplorerNodeKind.Recipe, r.Kind));
    }

    [Fact]
    public void Opens_read_only_and_lock_toggles_editing()
    {
        var vm = Make(out _);
        Assert.False(vm.IsEditing);
        vm.ToggleLockCommand.Execute(null);
        Assert.True(vm.IsEditing);
    }

    [Fact]
    public void Delete_is_disabled_until_editing()
    {
        var vm = Make(out _);
        Assert.False(vm.DeleteSelectedCommand.CanExecute(null));
        vm.ToggleLockCommand.Execute(null);
        Assert.True(vm.DeleteSelectedCommand.CanExecute(null));
    }

    [Fact]
    public void Add_label_tracks_the_selected_node_kind()
    {
        var vm = Make(out _);
        vm.SelectNodeCommand.Execute(new ExplorerNode("r", "Cat", ExplorerNodeKind.Recipe, [], null));
        Assert.Equal("Add ingredient", vm.AddLabel);
        vm.SelectNodeCommand.Execute(new ExplorerNode("i", "Aura", ExplorerNodeKind.Ingredient, [], null));
        Assert.Equal("Add variant", vm.AddLabel);
    }

    [Fact]
    public void Search_and_import_report_not_yet_wired()
    {
        var vm = Make(out var n);
        vm.SearchCommand.Execute(null); Assert.Equal("Search (⌘K)", n.Last);
        vm.ImportCommand.Execute(null); Assert.Equal("Import", n.Last);
    }
}
