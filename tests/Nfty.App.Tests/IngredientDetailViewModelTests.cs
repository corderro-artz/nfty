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
            new FakeNotYetWired(), () => { }, () => false))
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
            new FakeNotYetWired(), () => { }, () => false, () => jumped = true);

        Assert.Equal(2, vm.RuleCount);
        Assert.True(vm.HasRules);
        Assert.Equal("2 rules", vm.RuleFlagText);

        // The command actually does something now.
        vm.JumpToRulesCommand.Execute(null);
        Assert.True(jumped);
    }

    /// <summary>"Delete variant" used to call INotYetWired, which the shell renders as
    /// "Not wired yet: Delete variant" — while the button sat there enabled, looking like it worked,
    /// and while the editor had a real delete with a confirm dialog and undo history. It must never
    /// touch the not-wired channel for a feature that exists.</summary>
    [AvaloniaFact]
    public void Delete_variant_opens_the_editor_and_never_claims_to_be_unbuilt()
    {
        var (book, recipe, ing) = Fixture();
        var opened = false;
        var notify = new FakeNotYetWired();
        var status = new StatusService();
        using var vm = new IngredientDetailViewModel(ing, recipe, book, new ImageBridge(),
            notify, () => opened = true, () => true, null, status);

        Assert.True(vm.DeleteVariantCommand.CanExecute(null));   // enabled while editing
        vm.DeleteVariantCommand.Execute(null);

        Assert.True(opened);                                     // the editor, which owns variants
        Assert.Null(notify.Last);                                // NOT "not wired yet"
        Assert.NotNull(status.Last);                             // it explains where deletion lives
    }

    [AvaloniaFact]
    public void Variant_rows_carry_within_recipe_rarity()
    {
        var (book, recipe, ing) = Fixture();
        using var vm = new IngredientDetailViewModel(ing, recipe, book, new ImageBridge(),
            new FakeNotYetWired(), () => { }, () => false);
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
            new FakeNotYetWired(), () => { }, () => false);

        Assert.Equal(new[] { "Apple", "Zephyr" }, vm.Variants.Select(v => v.Name));   // default "Variant": by name
        vm.SortByCommand.Execute("Weight");
        Assert.Equal(new[] { "Zephyr", "Apple" }, vm.Variants.Select(v => v.Name));   // "Weight": heaviest first
    }

    [AvaloniaFact]
    public void Delete_variant_enabled_only_when_editing()
    {
        var (book, recipe, ing) = Fixture();
        bool editing = false;
        using var vm = new IngredientDetailViewModel(ing, recipe, book, new ImageBridge(),
            new FakeNotYetWired(), () => { }, () => editing);
        Assert.False(vm.DeleteVariantCommand.CanExecute(null));
        editing = true; vm.RaiseCanExecuteChanged();
        Assert.True(vm.DeleteVariantCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void Hero_thumbnails_and_colorways_are_built()
    {
        var (book, recipe, ing) = Fixture();
        using var vm = new IngredientDetailViewModel(ing, recipe, book, new ImageBridge(),
            new FakeNotYetWired(), () => { }, () => false);
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
            new FakeNotYetWired(), () => { }, () => false);

        Assert.Empty(vm.Variants);
        Assert.Null(vm.Hero);
        Assert.Empty(vm.Colorways);
    }

    [AvaloniaFact]
    public void Selecting_a_variant_swaps_the_hero()
    {
        var (book, recipe, ing) = Fixture();
        using var vm = new IngredientDetailViewModel(ing, recipe, book, new ImageBridge(),
            new FakeNotYetWired(), () => { }, () => false);
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
            new FakeNotYetWired(), () => { }, () => false);

        // A Custom layer has NO axes. It used to report one synthetic
        // ColorwayAxis("COLOUR", "no colorize · composited as-is"), which borrowed the axis-row
        // shape to say "there are no axes" and put a full sentence in a column sized for "190–320°".
        // The mockup gives Custom its own branch instead - a swatch of the art plus a note - so the
        // rail must have nothing to lay out here.
        Assert.True(vm.IsCustom);
        Assert.Empty(vm.ColorwayAxes);
        Assert.False(vm.HasHueBand);            // and no hue band either: it rolls no hue
        Assert.NotNull(vm.SelectedThumb);       // the swatch the Custom branch draws instead
    }
}
