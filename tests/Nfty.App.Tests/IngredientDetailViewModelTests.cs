using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public class IngredientDetailViewModelTests
{
    private static (LoadedCookBook book, LoadedRecipe recipe, LoadedIngredient ing) Fixture()
    {
        LoadedIngredient Ing(string id, params (string vid, string name, double w)[] vs) => new()
        {
            Manifest = new IngredientManifest(id, id, LayerKind.Custom, null,
                vs.Select(v => new Variant(v.vid, v.name, v.w)).ToArray()),
            VariantImages = vs.ToDictionary(v => v.vid, _ => new Image<Rgba32>(4, 4)),
        };
        var aura = Ing("aura", ("glow", "Glow", 3), ("spark", "Spark", 1));   // 75% / 25% within
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "aura" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { aura },
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(4, 4),
                new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 }),
            Recipes = new[] { recipe },
        };
        return (book, recipe, aura);
    }

    /// <summary>The hero's rule-count pill (mockup .hflag). It appears ONLY when the layer is named
    /// in a rule, which is the question it exists to answer — so no capture fixture reaches it: the
    /// explorer's book has no rules and this file's default fixture has none either. Same blind spot
    /// as the Custom-only ingredients and the editor's disabled toolstrip, so it is pinned here.
    ///
    /// It replaced a permanently-visible "Jump to rules" button whose command body was empty.</summary>
    [AvaloniaFact]
    public void Rule_pill_counts_rules_on_either_side_and_hides_when_there_are_none()
    {
        var (book, recipe, ing) = Fixture();
        using (var none = new IngredientDetailViewModel(ing, recipe, book, new ImageBridge(),
            () => { }, () => false))
        {
            Assert.Equal(0, none.RuleCount);
            Assert.False(none.HasRules);        // nothing to jump to, so the pill must not show
        }

        // One rule naming this layer as the CONDITION, one naming it as a TARGET. Both count: the
        // question is "is this layer entangled", not "which side of the rule is it on".
        var rules = new[]
        {
            new IncompatibilityRule(RuleType.Exclude, new RuleTarget("aura", "glow"),
                new[] { new RuleTarget("other", "x") }),
            new IncompatibilityRule(RuleType.Require, new RuleTarget("other", "y"),
                new[] { new RuleTarget("aura", "spark") }),
            new IncompatibilityRule(RuleType.Exclude, new RuleTarget("other", "y"),
                new[] { new RuleTarget("unrelated", "z") }),   // mentions neither side - must not count
        };
        var withRules = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "aura" }, rules),
            Ingredients = recipe.Ingredients,
        };
        var jumped = false;
        using var vm = new IngredientDetailViewModel(ing, withRules, book, new ImageBridge(),
            () => { }, () => false, () => jumped = true);

        Assert.Equal(2, vm.RuleCount);
        Assert.True(vm.HasRules);
        Assert.Equal("2 rules", vm.RuleFlagText);

        // The command actually does something now.
        vm.JumpToRulesCommand.Execute(null);
        Assert.True(jumped);
    }

    /// <summary>
    /// "Delete variant" deletes the selected variant.
    /// </summary>
    /// <remarks>
    /// It has been wrong twice. It first reported itself as unbuilt ("Not wired yet: Delete
    /// variant") while sitting there enabled; then it said "delete variants in the editor" and
    /// NAVIGATED there, so a button labelled Delete variant deleted nothing and moved the reader to
    /// another screen. Both are the same mistake, and the second is the one a bug report called
    /// broken.
    /// </remarks>
    [AvaloniaFact]
    public async Task Delete_variant_deletes_the_selected_variant()
    {
        var (book, recipe, ing) = Fixture();
        var opened = false;
        string? deleted = null;
        using var vm = new IngredientDetailViewModel(ing, recipe, book, new ImageBridge(),
            () => opened = true, () => true, null, new StatusService(), null, new YesDialogs(),
            id => { deleted = id; return Task.CompletedTask; });

        Assert.True(vm.DeleteVariantCommand.CanExecute(null));
        await vm.DeleteVariantCommand.ExecuteAsync(null);

        Assert.Equal(vm.Selected!.Id, deleted);
        Assert.False(opened, "it navigated to the editor instead of deleting");
    }

    /// <summary>The confirm is a gate: saying no deletes nothing.</summary>
    [AvaloniaFact]
    public async Task Declining_the_confirmation_deletes_nothing()
    {
        var (book, recipe, ing) = Fixture();
        string? deleted = null;
        using var vm = new IngredientDetailViewModel(ing, recipe, book, new ImageBridge(),
            () => { }, () => true, null, new StatusService(), null, new FakeDialogs(),
            id => { deleted = id; return Task.CompletedTask; });

        await vm.DeleteVariantCommand.ExecuteAsync(null);
        Assert.Null(deleted);
    }

    /// <summary>
    /// The table is a selection, and the hero follows it.
    /// </summary>
    /// <remarks>
    /// It was inert: the rows could not be clicked, the hero was pinned to the first variant for
    /// the life of the pane, and <c>SelectVariantCommand</c> was reachable from no markup at all.
    /// The wiring sweep matches commands by NAME across every view, and the ingredient EDITOR has a
    /// command of the same name that IS bound — so a dead command on this screen looked wired.
    /// </remarks>
    [AvaloniaFact]
    public void Selecting_a_row_moves_the_hero_and_what_delete_would_remove()
    {
        var (book, recipe, ing) = Fixture();
        using var vm = new IngredientDetailViewModel(ing, recipe, book, new ImageBridge(),
            () => { }, () => true, null, new StatusService(), null, new YesDialogs(),
            _ => Task.CompletedTask);

        var first = vm.Variants[0];
        var other = vm.Variants.First(v => v.Id != first.Id);
        Assert.True(vm.Selected!.IsSelected);
        var heroBefore = vm.Hero;

        vm.SelectVariantCommand.Execute(other.Id);

        Assert.Same(other, vm.Selected);
        Assert.True(other.IsSelected);
        Assert.False(first.IsSelected);                 // exactly two rows change
        Assert.NotSame(heroBefore, vm.Hero);
        Assert.Same(other.Thumbnail, vm.SelectedThumb);
    }

    [AvaloniaFact]
    public void Variant_rows_carry_within_recipe_rarity()
    {
        var (book, recipe, ing) = Fixture();
        using var vm = new IngredientDetailViewModel(ing, recipe, book, new ImageBridge(),
            () => { }, () => false);
        Assert.Equal(2, vm.Variants.Count);
        var glow = vm.Variants.Single(v => v.Name == "Glow");
        Assert.Equal(75.0, glow.WithinPercent, 1);   // 3/(3+1)
    }

    [AvaloniaFact]
    public void Sorting_reorders_variants_by_the_chosen_column()
    {
        LoadedIngredient Ing(string id, params (string vid, string name, double w)[] vs) => new()
        {
            Manifest = new IngredientManifest(id, id, LayerKind.Custom, null,
                vs.Select(v => new Variant(v.vid, v.name, v.w)).ToArray()),
            VariantImages = vs.ToDictionary(v => v.vid, _ => new Image<Rgba32>(4, 4)),
        };
        var aura = Ing("aura", ("a", "Apple", 1), ("z", "Zephyr", 5));   // name order ≠ weight order
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "aura" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { aura },
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(4, 4),
                new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 }),
            Recipes = new[] { recipe },
        };
        using var vm = new IngredientDetailViewModel(aura, recipe, book, new ImageBridge(),
            () => { }, () => false);

        Assert.Equal(new[] { "Apple", "Zephyr" }, vm.Variants.Select(v => v.Name));   // default "Variant": by name

        // ONE RULE, NO EXCEPTIONS: the first click on a column is ascending, whatever the column
        // holds. This used to jump straight to weight-DESCENDING with no way back, which meant a
        // numeric column behaved differently from a text one and neither could be reversed.
        vm.Sort.ByCommand.Execute("Weight");
        Assert.Equal(new[] { "Apple", "Zephyr" }, vm.Variants.Select(v => v.Name));   // lightest first
        vm.Sort.ByCommand.Execute("Weight");
        Assert.Equal(new[] { "Zephyr", "Apple" }, vm.Variants.Select(v => v.Name));   // reversed
    }

    [AvaloniaFact]
    public void Delete_variant_enabled_only_when_editing()
    {
        var (book, recipe, ing) = Fixture();
        bool editing = false;
        using var vm = new IngredientDetailViewModel(ing, recipe, book, new ImageBridge(),
            () => { }, () => editing, null, new StatusService(), null, new YesDialogs(),
            _ => Task.CompletedTask);
        Assert.False(vm.DeleteVariantCommand.CanExecute(null));
        editing = true; vm.RaiseCanExecuteChanged();
        Assert.True(vm.DeleteVariantCommand.CanExecute(null));
    }

    /// <summary>
    /// A layer needs a variant, so the last one cannot be deleted — and the tooltip says which of
    /// the reasons it is, since a control dim for an unstated reason is one the user has to guess at.
    /// </summary>
    [AvaloniaFact]
    public void The_last_variant_cannot_be_deleted()
    {
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("aura", "Aura", LayerKind.Custom, null,
                new[] { new Variant("glow", "Glow", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["glow"] = new(4, 4) },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "aura" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        using var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(4, 4),
                new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 }),
            Recipes = new[] { recipe },
        };
        using var vm = new IngredientDetailViewModel(ing, recipe, book, new ImageBridge(),
            () => { }, () => true, null, new StatusService(), null, new YesDialogs(),
            _ => Task.CompletedTask);

        Assert.False(vm.DeleteVariantCommand.CanExecute(null));
        Assert.Contains("at least one variant", vm.DeleteVariantTip);
    }

    /// <summary>Answers the confirm with a yes. <see cref="FakeDialogs"/> returns default, which is
    /// a no.</summary>
    private sealed class YesDialogs : IDialogService
    {
        public ViewModelBase? Active => null;
        public event System.Action? Changed { add { } remove { } }
        public System.Threading.Tasks.Task<TResult?> ShowAsync<TResult>(ViewModelBase d) =>
            System.Threading.Tasks.Task.FromResult((TResult?)(object?)true);
        public void Close(object? result) { }
    }

    [AvaloniaFact]
    public void Hero_thumbnails_and_colorways_are_built()
    {
        var (book, recipe, ing) = Fixture();
        using var vm = new IngredientDetailViewModel(ing, recipe, book, new ImageBridge(),
            () => { }, () => false);
        Assert.NotNull(vm.Hero);
        Assert.All(vm.Variants, v => Assert.NotNull(v.Thumbnail));
        Assert.NotEmpty(vm.Colorways);
    }

    [AvaloniaFact]
    public void Zero_variant_ingredient_does_not_crash_the_detail_pane()
    {
        var aura = new LoadedIngredient
        {
            Manifest = new IngredientManifest("aura", "Aura", LayerKind.Custom, null,
                Array.Empty<Variant>()),
            VariantImages = new Dictionary<string, Image<Rgba32>>(),
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "aura" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { aura },
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(4, 4),
                new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 }),
            Recipes = new[] { recipe },
        };

        using var vm = new IngredientDetailViewModel(aura, recipe, book, new ImageBridge(),
            () => { }, () => false);

        Assert.Empty(vm.Variants);
        Assert.Null(vm.Hero);
        Assert.Empty(vm.Colorways);
    }

    [AvaloniaFact]
    public void Selecting_a_variant_swaps_the_hero()
    {
        var (book, recipe, ing) = Fixture();
        using var vm = new IngredientDetailViewModel(ing, recipe, book, new ImageBridge(),
            () => { }, () => false);
        var first = vm.Hero;
        vm.SelectVariantCommand.Execute(ing.Manifest.Variants[^1].Id);
        Assert.NotNull(vm.Hero);   // rebuilt; old disposed internally
        Assert.NotSame(first, vm.Hero);   // each render builds a fresh Bitmap instance
    }

    [AvaloniaFact]
    public void Colorway_axes_reflect_the_kind()
    {
        var (book, recipe, ing) = Fixture();
        using var vm = new IngredientDetailViewModel(ing, recipe, book, new ImageBridge(),
            () => { }, () => false);

        // A Custom layer has NO axes. It used to report one synthetic
        // ColorwayAxis("COLOR", "no colorize · composited as-is"), which borrowed the axis-row
        // shape to say "there are no axes" and put a full sentence in a column sized for "190–320°".
        // The mockup gives Custom its own branch instead - a swatch of the art plus a note - so the
        // rail must have nothing to lay out here.
        Assert.True(vm.IsCustom);
        Assert.Empty(vm.ColorwayAxes);
        Assert.False(vm.HasHueBand);            // and no hue band either: it rolls no hue
        Assert.NotNull(vm.SelectedThumb);       // the swatch the Custom branch draws instead
    }
}
