using System.IO;
using System.Threading.Tasks;
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
        INavigationService nav, ICookBookSession? session = null, IDialogService? dialogs = null)
    {
        var s = session ?? new CookBookSession();
        var d = dialogs ?? new FakeDialogs();
        return (i, r, b) => new IngredientEditorViewModel(i, r, b, new ImageBridge(), nav, new FakeNotYetWired(), s, d,
            new FilePickerService());
    }

    /// <summary>Shared stub loose-editor factory (for a standalone .igt): builds the editor over a
    /// synthetic wrapper book with a loose-save path.</summary>
    internal static Func<LoadedIngredient, LoadedCookBook, string, IngredientEditorViewModel> LooseEditorFactory(
        INavigationService nav, ICookBookSession session, IDialogService dialogs) =>
        (ing, book, path) => new IngredientEditorViewModel(ing, book.Recipes[0], book, new ImageBridge(),
            nav, new FakeNotYetWired(), session, dialogs, new FilePickerService(), looseSavePath: path);

    /// <summary>Shared stub cook factory for tests that construct an <see cref="ExplorerViewModel"/>
    /// but don't exercise the Cook dialog flow itself.</summary>
    internal static Func<LoadedCookBook, CookDialogViewModel> CookFactory(IDialogService dialogs) =>
        b => new CookDialogViewModel(b, new FilePickerService(), new NoopFolderRevealer(), dialogs);

    private static ExplorerViewModel Make(out FakeNotYetWired n)
    {
        n = new FakeNotYetWired();
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var session = new CookBookSession();
        return new ExplorerViewModel(TwoRecipeBook(), nav, dialogs, n, new ImageBridge(), EditorFactory(nav), CookFactory(dialogs), session,
            new FilePickerService(), LooseEditorFactory(nav, session, dialogs), new StatusService());
    }

    [AvaloniaFact]
    public void Tree_is_built_from_the_cookbook_recipes_and_ingredients()
    {
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var session = new CookBookSession();
        using var vm = new ExplorerViewModel(TwoRecipeBook(), nav, dialogs, new FakeNotYetWired(), new ImageBridge(), EditorFactory(nav), CookFactory(dialogs), session,
            new FilePickerService(), LooseEditorFactory(nav, session, dialogs), new StatusService());
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
    public void Delete_stays_disabled_without_a_source_file_even_when_editing()
    {
        using var vm = Make(out _);              // in-memory book (Make's session has no source path)
        Assert.False(vm.DeleteSelectedCommand.CanExecute(null));
        vm.ToggleLockCommand.Execute(null);      // editing on
        Assert.False(vm.DeleteSelectedCommand.CanExecute(null));   // no source .cbk to write → still disabled
        // The full enabled path (editing + source file + a recipe/ingredient selected) is covered by
        // ExplorerDeleteTests.CanDelete_requires_editing_a_source_file_and_a_non_root_node.
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
    public void Import_reports_not_yet_wired()
    {
        using var vm = Make(out var n);
        vm.ImportCommand.Execute(null); Assert.Equal("Import", n.Last);
    }

    [AvaloniaFact]
    public void Ingredient_nodes_carry_their_layer_kind()
    {
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var session = new CookBookSession();
        using var vm = new ExplorerViewModel(TwoRecipeBook(), nav, dialogs, new FakeNotYetWired(), new ImageBridge(), EditorFactory(nav), CookFactory(dialogs), session,
            new FilePickerService(), LooseEditorFactory(nav, session, dialogs), new StatusService());
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
        var dialogs = new FakeDialogs();
        var session = new CookBookSession();
        using var vm = new ExplorerViewModel(TwoRecipeBook(), nav, dialogs, new FakeNotYetWired(), new ImageBridge(), EditorFactory(nav), CookFactory(dialogs), session,
            new FilePickerService(), LooseEditorFactory(nav, session, dialogs), new StatusService());

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

    [AvaloniaFact]
    public async Task Editor_save_rebuilds_the_tree_and_reselects_the_ingredient()
    {
        // Dynamic on-disk book so Save is enabled (TwoRecipeBook is Custom → blocked).
        var (path, session, recipe, ing) = IngredientEditorSaveTests.OnDisk();
        try
        {
            var nav = new FakeNav();
            var dialogs = new FakeDialogs();
            var editorFactory = EditorFactory(nav, session, dialogs);
            using var explorer = new ExplorerViewModel(session.Current!, nav, dialogs, new FakeNotYetWired(),
                new ImageBridge(), editorFactory, CookFactory(dialogs), session,
                new FilePickerService(), LooseEditorFactory(nav, session, dialogs), new StatusService());

            // Select the ingredient, then open + save its editor the way the Explorer wires it.
            var ingNode = explorer.Root.Children[0].Children[0];
            explorer.SelectNodeCommand.Execute(ingNode);
            var editor = editorFactory(ing, recipe, session.Current!);
            editor.Saved += explorer.OnEditorSaved;
            editor.ActiveTool = EditorTool.Fill; editor.BrushValue = 123;
            editor.ApplyToolStroke(new[] { (0, 0) });
            await editor.SaveCommand.ExecuteAsync(null);

            Assert.Equal("aura", explorer.SelectedNode!.Id);          // same ingredient reselected
            Assert.Same(session.Current, explorer.BookForTest);       // rebuilt against the saved graph
            editor.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }
}
