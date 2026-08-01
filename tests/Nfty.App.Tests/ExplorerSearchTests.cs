using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using Nfty.App.Models;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>Explorer search / filter (D2): <see cref="ExplorerViewModel.SearchQuery"/> filters the tree
/// kept in <c>_fullRoot</c> down to matching recipes/ingredients/variants, with selection-safety and
/// filter-survives-a-graph-swap as the two named risks (spec §6).</summary>
public class ExplorerSearchTests
{
    private static ExplorerViewModel Make(out FakeNotYetWired n, LoadedCookBook? book = null)
    {
        n = new FakeNotYetWired();
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var session = new CookBookSession();
        return new ExplorerViewModel(book ?? ExplorerViewModelTests.TwoRecipeBook(), nav, dialogs, n, new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav), ExplorerViewModelTests.CookFactory(dialogs), session,
            new FilePickerService(), ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), new StatusService());
    }

    /// <summary>A book whose one ingredient has a variant name/id that never appears anywhere else in
    /// the tree (unlike <see cref="ExplorerViewModelTests.TwoRecipeBook"/>, whose fixture variants are
    /// all "a"/"A" and so can't prove a match came from the variant path rather than name/id).</summary>
    private static LoadedCookBook BookWithDistinctiveVariant()
    {
        LoadedIngredient PlainIng(string id) => new()
        {
            Manifest = new IngredientManifest(id, id, LayerKind.Custom, null, new[] { new Variant("a", "A", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["a"] = new Image<Rgba32>(8, 8) },
        };
        var auraIng = new LoadedIngredient
        {
            Manifest = new IngredientManifest("aura", "aura", LayerKind.Custom, null,
                new[] { new Variant("shiny", "Shiny Chrome", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["shiny"] = new Image<Rgba32>(8, 8) },
        };
        var cat = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "cat", new[] { "bg", "aura" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { PlainIng("bg"), auraIng },
        };
        var dog = new LoadedRecipe
        {
            Manifest = new RecipeManifest("dog", "dog", new[] { "body" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { PlainIng("body") },
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "VaporPets", new Dimensions(8, 8),
                new Collection("VaporPets", "", "VP"),
                new Dictionary<string, double> { ["cat"] = 1, ["dog"] = 1 }),
            Recipes = new[] { cat, dog },
        };
    }

    [AvaloniaFact]
    public void Matching_an_ingredient_keeps_its_recipe_and_drops_siblings()
    {
        using var vm = Make(out _);
        vm.SearchQuery = "aura";   // unique to cat's ingredient
        Assert.Equal(new[] { "cat" }, vm.Root.Children.Select(c => c.Id));
        Assert.Equal(new[] { "aura" }, vm.Root.Children[0].Children.Select(c => c.Id));
    }

    [AvaloniaFact]
    public void Matching_a_recipe_keeps_all_its_ingredients()
    {
        using var vm = Make(out _);
        vm.SearchQuery = "cat";
        Assert.Equal(new[] { "cat" }, vm.Root.Children.Select(c => c.Id));
        Assert.Equal(new[] { "bg", "aura" }, vm.Root.Children[0].Children.Select(c => c.Id));
    }

    [AvaloniaFact]
    public void Matching_a_variant_keeps_its_ingredient()
    {
        using var vm = Make(out _, BookWithDistinctiveVariant());
        vm.SearchQuery = "Shiny Chrome";   // matches only the "aura" ingredient's variant, not any id/name
        Assert.Equal(new[] { "cat" }, vm.Root.Children.Select(c => c.Id));
        Assert.Equal(new[] { "aura" }, vm.Root.Children[0].Children.Select(c => c.Id));
    }

    [AvaloniaFact]
    public void A_blank_query_restores_the_full_tree()
    {
        using var vm = Make(out _);
        var fullRoot = vm.Root;   // captured before any filtering

        vm.SearchQuery = "aura";
        Assert.Equal(new[] { "cat" }, vm.Root.Children.Select(c => c.Id));

        vm.SearchQuery = "";
        Assert.Equal(new[] { "cat", "dog" }, vm.Root.Children.Select(c => c.Id));
        Assert.Equal(new[] { "bg", "aura" }, vm.Root.Children[0].Children.Select(c => c.Id));
        Assert.Same(fullRoot, vm.Root);   // blank query returns _fullRoot unchanged (same instance)
    }

    [AvaloniaFact]
    public void A_query_matching_nothing_yields_an_empty_root_and_zero_matches()
    {
        using var vm = Make(out _);
        vm.SearchQuery = "zzz-does-not-exist";
        Assert.Empty(vm.Root.Children);
        Assert.Equal("0 matches", vm.SearchSummary);
    }

    [AvaloniaFact]
    public void Matching_is_case_insensitive()
    {
        using var lower = Make(out _);
        using var upper = Make(out _);
        lower.SearchQuery = "cat";
        upper.SearchQuery = "CAT";
        Assert.Equal(lower.Root.Children.Select(c => c.Id), upper.Root.Children.Select(c => c.Id));
        Assert.Equal(lower.Root.Children[0].Children.Select(c => c.Id), upper.Root.Children[0].Children.Select(c => c.Id));
    }

    [AvaloniaFact]
    public void Selection_falls_back_to_the_root_when_filtered_away()
    {
        using var vm = Make(out _);
        var auraNode = vm.Root.Children[0].Children[1];   // cat/aura
        vm.SelectNodeCommand.Execute(auraNode);
        Assert.Equal("aura", vm.SelectedNode!.Id);

        vm.SearchQuery = "dog";   // excludes aura entirely
        Assert.Equal(vm.Root.Id, vm.SelectedNode!.Id);
        Assert.IsType<CookBookDetailViewModel>(vm.CurrentDetail);
    }

    [AvaloniaFact]
    public void The_filter_survives_a_graph_swap()
    {
        using var vm = Make(out _);
        vm.SearchQuery = "cat";
        Assert.Equal(new[] { "cat" }, vm.Root.Children.Select(c => c.Id));

        vm.OnEditorSaved(ExplorerViewModelTests.TwoRecipeBook());   // e.g. save/add/delete rebuilds the tree

        Assert.Equal("cat", vm.SearchQuery);
        Assert.Equal(new[] { "cat" }, vm.Root.Children.Select(c => c.Id));
    }

    /// <summary>Filtering rebuilds kept nodes via `record with`, so the "same" node arrives as a new
    /// instance each keystroke. Without a guard that re-selection tore down and rebuilt CurrentDetail
    /// per character — a full generation pass + canvas-sized bitmap for a recipe, and it reset the
    /// user's Reroll seed. Assert the detail survives typing.</summary>
    [AvaloniaFact]
    public void Typing_does_not_rebuild_the_detail_pane()
    {
        using var vm = Make(out _);
        vm.SelectNodeCommand.Execute(vm.Root);            // cookbook root selected
        var detail = vm.CurrentDetail;
        Assert.NotNull(detail);

        foreach (var q in new[] { "c", "ca", "cat" })
            vm.SearchQuery = q;

        Assert.Same(detail, vm.CurrentDetail);            // same instance — never rebuilt
    }

    /// <summary>Typing must not invent a selection where there was none (it would also flip AddLabel
    /// and populate the detail pane as a side effect of searching).</summary>
    [AvaloniaFact]
    public void Typing_with_nothing_selected_does_not_select_the_root()
    {
        using var vm = Make(out _);
        Assert.Null(vm.SelectedNode);
        vm.SearchQuery = "cat";
        Assert.Null(vm.SelectedNode);
        Assert.Null(vm.CurrentDetail);
    }
}
