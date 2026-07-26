using Avalonia.Headless.XUnit;
using Nfty.App.Models;
using Nfty.App.Services;
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
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["a"] = new Image<Rgba32>(8, 8) },
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

    /// <summary>Shared stub editor factory for tests that construct an <see cref="ExplorerViewModel"/>
    /// but don't exercise the Ingredient Editor navigation itself.</summary>
    internal static Func<LoadedIngredient, LoadedRecipe, LoadedCookBook, IngredientEditorViewModel> EditorFactory(
        INavigationService nav) => (i, r, b) => new IngredientEditorViewModel(i, r, b, new ImageBridge(), nav, new FakeNotYetWired());

    private static ExplorerViewModel Make(out FakeNotYetWired n)
    {
        n = new FakeNotYetWired();
        var nav = new FakeNav();
        return new ExplorerViewModel(TwoRecipeBook(), nav, new FakeDialogs(), n, new ImageBridge(), EditorFactory(nav));
    }

    [AvaloniaFact]
    public void Tree_is_built_from_the_cookbook_recipes_and_ingredients()
    {
        var nav = new FakeNav();
        using var vm = new ExplorerViewModel(TwoRecipeBook(), nav, new FakeDialogs(), new FakeNotYetWired(), new ImageBridge(), EditorFactory(nav));
        Assert.Equal(ExplorerNodeKind.CookBook, vm.Root.Kind);
        Assert.Equal("VaporPets", vm.Root.Name);
        Assert.Equal(new[] { "cat", "dog" }, vm.Root.Children.Select(c => c.Id));
        Assert.Equal(new[] { "bg", "aura" }, vm.Root.Children[0].Children.Select(c => c.Id));
        Assert.All(vm.Root.Children, r => Assert.Equal(ExplorerNodeKind.Recipe, r.Kind));
    }

    [Fact]
    public void Opens_read_only_and_lock_toggles_editing()
    {
        using var vm = Make(out _);
        Assert.False(vm.IsEditing);
        vm.ToggleLockCommand.Execute(null);
        Assert.True(vm.IsEditing);
    }

    [Fact]
    public void Delete_is_disabled_until_editing()
    {
        using var vm = Make(out _);
        Assert.False(vm.DeleteSelectedCommand.CanExecute(null));
        vm.ToggleLockCommand.Execute(null);
        Assert.True(vm.DeleteSelectedCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void Add_label_tracks_the_selected_node_kind()
    {
        using var vm = Make(out _);
        var cat = TwoRecipeBook().Recipes.First(r => r.Manifest.Id == "cat");
        vm.SelectNodeCommand.Execute(new ExplorerNode("r", "Cat", ExplorerNodeKind.Recipe, [], cat));
        Assert.Equal("Add ingredient", vm.AddLabel);
        vm.SelectNodeCommand.Execute(new ExplorerNode("i", "Aura", ExplorerNodeKind.Ingredient, [], null));
        Assert.Equal("Add variant", vm.AddLabel);
    }

    [Fact]
    public void Search_and_import_report_not_yet_wired()
    {
        using var vm = Make(out var n);
        vm.SearchCommand.Execute(null); Assert.Equal("Search (⌘K)", n.Last);
        vm.ImportCommand.Execute(null); Assert.Equal("Import", n.Last);
    }

    [AvaloniaFact]
    public void Ingredient_nodes_carry_their_layer_kind()
    {
        var nav = new FakeNav();
        using var vm = new ExplorerViewModel(TwoRecipeBook(), nav, new FakeDialogs(), new FakeNotYetWired(), new ImageBridge(), EditorFactory(nav));
        var recipe = vm.Root.Children[0];
        var ingredient = recipe.Children[0];
        Assert.Null(vm.Root.LayerKind);            // cookbook node
        Assert.Null(recipe.LayerKind);             // recipe node
        Assert.Equal(Nfty.Core.Model.LayerKind.Custom, ingredient.LayerKind);  // TwoRecipeBook ingredients are Custom
        Assert.True(ingredient.IsCustom);
    }

    [AvaloniaFact]
    public void Crumbs_follow_the_selected_node_path()
    {
        var nav = new FakeNav();
        using var vm = new ExplorerViewModel(TwoRecipeBook(), nav, new FakeDialogs(), new FakeNotYetWired(), new ImageBridge(), EditorFactory(nav));

        // nothing selected → just the cookbook, active
        Assert.Equal(new[] { (vm.Root.Name, true, false) }, vm.Crumbs.Select(c => (c.Text, c.Active, c.Leading)));

        var recipe = vm.Root.Children[0];
        vm.SelectNodeCommand.Execute(recipe);
        Assert.Equal(new[] { (vm.Root.Name, false, false), (recipe.Name, true, true) }, vm.Crumbs.Select(c => (c.Text, c.Active, c.Leading)));

        var ingredient = recipe.Children[0];
        vm.SelectNodeCommand.Execute(ingredient);
        Assert.Equal(new[] { (vm.Root.Name, false, false), (recipe.Name, false, true), (ingredient.Name, true, true) },
            vm.Crumbs.Select(c => (c.Text, c.Active, c.Leading)));
    }
}
