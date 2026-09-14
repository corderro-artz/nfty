using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using Point = Avalonia.Point;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The three places the app knew something and did not say it.
/// </summary>
/// <remarks>
/// Each was found by looking at a rendered frame rather than by any assertion: a screen that reports
/// a count and never the thing it counted, and a screen that goes blank without saying which
/// blankness it is. Neither is a crash, and both look perfectly tidy.
/// </remarks>
public class EmptyAndProblemStatesTests
{
    // ---------------- the invalid CookBook says what is wrong ----------------

    /// <summary>A book whose recipe names a layer it does not hold — one Validator problem, and the
    /// state <see cref="VisualCapture"/> renders for this card.</summary>
    private static LoadedCookBook BrokenBook()
    {
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "bg", LayerKind.Custom, null,
                new[] { new Variant("a", "A", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["a"] = new Image<Rgba32>(8, 8) },
        };
        var cat = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "bg", "missing-layer" },
                Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "VaporPets", new Dimensions(8, 8),
                new Collection("VaporPets", "", "VP"),
                new Dictionary<string, double> { ["cat"] = 1 }),
            Recipes = new[] { cat },
        };
    }

    [AvaloniaFact]
    public void An_invalid_cookbook_names_its_problems_on_the_card()
    {
        var vm = new CookBookDetailViewModel(BrokenBook(), () => { });

        Assert.False(vm.IsValid);
        Assert.Equal("1 problem", vm.StatusText);
        Assert.True(vm.HasProblems, "the card said '1 problem' and listed none");
        var only = Assert.Single(vm.ShownProblems);
        Assert.Contains("missing-layer", only);
        Assert.Null(vm.MoreProblemsText);
    }

    /// <summary>A valid book draws no panel at all — the state is not "no problems listed", it is
    /// "nothing to list".</summary>
    [AvaloniaFact]
    public void A_valid_cookbook_draws_no_problem_panel()
    {
        var vm = new CookBookDetailViewModel(ExplorerViewModelTests.TwoRecipeBook(), () => { });

        Assert.True(vm.IsValid);
        Assert.False(vm.HasProblems);
        Assert.Empty(vm.ShownProblems);
    }

    /// <summary>The panel is RENDERED, and only in the invalid state. A property nothing binds is a
    /// panel nobody sees, which is the failure this whole file is about.</summary>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_problem_panel_is_drawn_exactly_when_the_book_is_invalid(bool broken)
    {
        var vm = new CookBookDetailViewModel(
            broken ? BrokenBook() : ExplorerViewModelTests.TwoRecipeBook(), () => { });
        var view = new Views.CookBookDetailView { DataContext = vm };
        var window = new Window { Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var panel = view.GetVisualDescendants().OfType<Border>()
                .FirstOrDefault(b => b.Classes.Contains("probpanel"));
            Assert.NotNull(panel);
            Assert.Equal(broken, panel!.IsVisible);
            if (broken) Assert.True(panel.Bounds.Height > 0, "the panel is visible but has no height");
        }
        finally { window.Close(); }
    }

    // ---------------- a search that matches nothing says so ----------------

    [AvaloniaFact]
    public void A_search_that_matches_nothing_says_which_emptiness_it_is()
    {
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var session = new CookBookSession();
        var vm = new ExplorerViewModel(ExplorerViewModelTests.TwoRecipeBook(), nav, dialogs,
            new ImageBridge(), ExplorerViewModelTests.EditorFactory(nav),
            ExplorerViewModelTests.CookFactory(dialogs), session, new FilePickerService(),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), new StatusService());

        Assert.False(vm.SearchFoundNothing);         // an unfiltered tree is not an empty search

        vm.SearchQuery = "zzz-nothing-matches-this";

        Assert.True(vm.SearchFoundNothing);
        Assert.Contains("zzz-nothing-matches-this", vm.SearchEmptyText);

        // The one click that undoes this emptiness, which is what separates it from the other kind.
        vm.ClearSearchCommand.Execute(null);
        Assert.False(vm.SearchFoundNothing);
        Assert.Equal("", vm.SearchQuery);
    }

    [AvaloniaFact]
    public void The_tree_is_replaced_by_the_message_rather_than_drawn_empty_behind_it()
    {
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var session = new CookBookSession();
        var vm = new ExplorerViewModel(ExplorerViewModelTests.TwoRecipeBook(), nav, dialogs,
            new ImageBridge(), ExplorerViewModelTests.EditorFactory(nav),
            ExplorerViewModelTests.CookFactory(dialogs), session, new FilePickerService(),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), new StatusService());
        var view = new Views.ExplorerView { DataContext = vm };
        var window = new Window { Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var tree = view.GetVisualDescendants().OfType<TreeView>().First();
            Assert.True(tree.IsVisible);

            vm.SearchQuery = "zzz-nothing-matches-this";
            Dispatcher.UIThread.RunJobs();

            // A filtered tree with nothing in it still draws the CookBook root, which is exactly
            // what a book holding nothing looks like.
            Assert.False(tree.IsVisible);
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(),
                t => t.Text is { } s && s.Contains("zzz-nothing-matches-this"));
        }
        finally { window.Close(); }
    }

    // ---------------- the ingredient pane's actions stay reachable ----------------

    /// <summary>
    /// A LAYER WITH MANY VARIANTS MUST NOT PUSH ITS OWN BUTTONS OFF THE PANE.
    /// </summary>
    /// <remarks>
    /// The column was one StackPanel — hero, the whole variant table, then the actions — so the
    /// table's height decided whether "Delete variant" and "Export preview…" were on screen. A
    /// fixture with two variants cannot see that, exactly as the rarity rail's snap test could not
    /// until it was given a book that overflows; this one builds twelve.
    /// </remarks>
    [AvaloniaFact]
    public void The_ingredient_panes_actions_stay_inside_the_pane_with_many_variants()
    {
        var images = new Dictionary<string, Image<Rgba32>>();
        var variants = new List<Variant>();
        for (int i = 0; i < 12; i++)
        {
            variants.Add(new Variant($"v{i}", $"Variant {i}", 1));
            images[$"v{i}"] = new Image<Rgba32>(8, 8);
        }
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("aura", "Aura", LayerKind.Custom, null, variants),
            VariantImages = images,
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "aura" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "VaporPets", new Dimensions(8, 8),
                new Collection("VaporPets", "", "VP"),
                new Dictionary<string, double> { ["cat"] = 1 }),
            Recipes = new[] { recipe },
        };

        var vm = new IngredientDetailViewModel(ing, recipe, book, new ImageBridge(),
            editIngredient: () => { }, isEditing: () => true);
        var view = new Views.IngredientDetailView { DataContext = vm };
        // The detail pane's own area at the smallest window: the page less the 286px tree column.
        var window = new Window
        {
            Content = view,
            Width = ShellViewModel.MinWindowWidth / ShellViewModel.BaseScale - 286,
            Height = (ShellViewModel.MinWindowHeight - ShellViewModel.ChromeReserve)
                     / ShellViewModel.BaseScale,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var actions = view.GetVisualDescendants().OfType<Button>()
                .First(b => b.Content as string == "Delete variant");
            var bottom = actions.TranslatePoint(new Point(0, actions.Bounds.Height), view);

            Assert.NotNull(bottom);
            Assert.True(bottom!.Value.Y <= view.Bounds.Height + 0.5,
                $"Delete variant reaches {bottom.Value.Y:0} in a pane {view.Bounds.Height:0} tall — "
                + "a button that names an action has to be reachable to perform it");
            Assert.True(actions.Bounds.Height > 0);
        }
        finally { vm.Dispose(); window.Close(); }
    }
}
