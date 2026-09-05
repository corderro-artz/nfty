using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// Rule authoring in the GUI — the dialog, and the panel commands that drive it.
///
/// <para>Until this shipped a rule could not be created or edited anywhere in the product. The
/// dialog's job is not convenience: an unknown layer or variant id is a thing <c>Validator</c> can
/// only report AFTER the fact, so a form that offers nothing but what the recipe holds makes that
/// whole class of rule unwritable rather than merely detectable.</para>
/// </summary>
public class RuleAuthoringTests
{
    private static (LoadedCookBook book, LoadedRecipe recipe) Fixture(params IncompatibilityRule[] rules)
    {
        LoadedIngredient Ing(string id, params string[] vs) => new()
        {
            Manifest = new IngredientManifest(id, id.ToUpperInvariant(), LayerKind.Custom, null,
                vs.Select(v => new Variant(v, v, 1)).ToArray()),
            VariantImages = vs.ToDictionary(v => v, _ => new Image<Rgba32>(4, 4)),
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "bg", "aura", "hat" }, rules),
            Ingredients = new[] { Ing("bg", "day", "night"), Ing("aura", "none", "glow"), Ing("hat", "crown") },
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(4, 4),
                new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 }),
            Recipes = new[] { recipe },
        };
        return (book, recipe);
    }

    private static RuleDialogViewModel Dialog(LoadedRecipe recipe, int editing = -1) =>
        new(new FakeDialogs(), recipe.Manifest, recipe.Ingredients, editing);

    private static IncompatibilityRule Exclude(string wi, string wv, string ti, string tv) =>
        new(RuleType.Exclude, new RuleTarget(wi, wv), new[] { new RuleTarget(ti, tv) });

    // ---------------------------------------------------------------- the dialog

    [Fact]
    public void The_pickers_offer_only_what_the_recipe_holds()
    {
        var (book, recipe) = Fixture();
        try
        {
            var vm = Dialog(recipe);
            Assert.Equal(new[] { "bg", "aura", "hat" }, vm.Layers.Select(l => l.Id));
            Assert.Equal(new[] { "day", "night" },
                vm.Layers.First(l => l.Id == "bg").Variants.Select(v => v.Id));
        }
        finally { book.Dispose(); }
    }

    [Fact]
    public void Layers_are_offered_in_paint_order_not_load_order()
    {
        // The pane lists layers in layerOrder; a form that listed them in ingredient order would put
        // the same layers in a different sequence one click apart.
        var (book, recipe) = Fixture();
        try
        {
            var reordered = new LoadedRecipe
            {
                Manifest = recipe.Manifest with { LayerOrder = new[] { "hat", "bg", "aura" } },
                Ingredients = recipe.Ingredients,
            };
            var vm = Dialog(reordered);
            Assert.Equal(new[] { "hat", "bg", "aura" }, vm.Layers.Select(l => l.Id));
        }
        finally { book.Dispose(); }
    }

    [Fact]
    public void Changing_a_layer_drops_the_variant_that_belonged_to_the_old_one()
    {
        var (book, recipe) = Fixture();
        try
        {
            var vm = Dialog(recipe);
            vm.Trigger.Layer = vm.Layers.First(l => l.Id == "bg");
            vm.Trigger.Variant = vm.Layers.First(l => l.Id == "bg").Variants.First(v => v.Id == "night");

            vm.Trigger.Layer = vm.Layers.First(l => l.Id == "aura");

            // Keeping "night" would name bg:night on the aura layer — a pair that does not exist,
            // and exactly the shape this form is here to make unwritable.
            Assert.Equal("aura", vm.Build()!.When.IngredientId);
            Assert.Contains(vm.Trigger.Variant!.Id, new[] { "none", "glow" });
        }
        finally { book.Dispose(); }
    }

    [Fact]
    public void A_second_target_opens_on_a_pair_that_is_actually_legal()
    {
        var (book, recipe) = Fixture();
        try
        {
            var vm = Dialog(recipe);
            vm.Trigger.Layer = vm.Layers.First(l => l.Id == "bg");
            vm.AddTargetCommand.Execute(null);

            // Found on a rendered frame: "+ Target" used to seat every new row on the first layer in
            // the list, so a second target opened as a duplicate of the first and the form refused a
            // rule the user had done nothing to break.
            Assert.Equal(2, vm.Targets.Count);
            Assert.Equal(string.Empty, vm.Problem);
            var targets = vm.Build()!.Targets;
            Assert.Equal(targets.Count, targets.Distinct().Count());
        }
        finally { book.Dispose(); }
    }

    [Fact]
    public void The_form_refuses_live_and_says_why()
    {
        var (book, recipe) = Fixture();
        try
        {
            var vm = Dialog(recipe);
            vm.Trigger.Layer = vm.Layers.First(l => l.Id == "bg");
            // A layer against itself: degenerate, and the CLI refuses the same rule with the same
            // sentence. Refused while the picker is still on screen, not on submit.
            vm.Targets[0].Layer = vm.Layers.First(l => l.Id == "bg");

            Assert.True(vm.HasProblem);
            Assert.Contains("against itself", vm.Problem);
            Assert.False(vm.ConfirmCommand.CanExecute(null));
        }
        finally { book.Dispose(); }
    }

    [Fact]
    public void A_rule_the_recipe_already_carries_is_refused_by_position()
    {
        var (book, recipe) = Fixture(Exclude("bg", "day", "aura", "none"));
        try
        {
            var vm = Dialog(recipe);
            vm.Trigger.Layer = vm.Layers.First(l => l.Id == "bg");
            vm.Trigger.Variant = vm.Layers.First(l => l.Id == "bg").Variants.First(v => v.Id == "day");
            vm.Targets[0].Layer = vm.Layers.First(l => l.Id == "aura");
            vm.Targets[0].Variant = vm.Layers.First(l => l.Id == "aura").Variants.First(v => v.Id == "none");

            Assert.Contains("position 1", vm.Problem);
            Assert.False(vm.ConfirmCommand.CanExecute(null));
        }
        finally { book.Dispose(); }
    }

    [Fact]
    public void Editing_a_rule_opens_seated_on_it_and_does_not_call_it_a_duplicate_of_itself()
    {
        var (book, recipe) = Fixture(Exclude("bg", "day", "aura", "none"));
        try
        {
            var vm = Dialog(recipe, editing: 0);

            Assert.Equal("Edit rule", vm.Title);
            Assert.Equal("bg", vm.Trigger.Layer!.Id);
            Assert.Equal("day", vm.Trigger.Variant!.Id);
            Assert.Equal("aura", vm.Targets[0].Layer!.Id);

            // An edit that changes nothing must be savable: the rule being edited is not one of the
            // rules it can collide with.
            Assert.Equal(string.Empty, vm.Problem);
            Assert.True(vm.ConfirmCommand.CanExecute(null));
        }
        finally { book.Dispose(); }
    }

    [Fact]
    public void The_last_target_cannot_be_removed()
    {
        var (book, recipe) = Fixture();
        try
        {
            var vm = Dialog(recipe);
            Assert.False(vm.CanRemoveTarget);
            vm.RemoveTargetCommand.Execute(vm.Targets[0]);
            Assert.Single(vm.Targets);   // a rule with no targets can never fire; RuleEdits refuses it

            vm.AddTargetCommand.Execute(null);
            Assert.True(vm.CanRemoveTarget);
        }
        finally { book.Dispose(); }
    }

    // ------------------------------------------------------- the panel's commands

    [AvaloniaFact]
    public void The_rule_commands_are_gated_by_the_edit_lock_not_by_the_pencils_exemption()
    {
        var (book, recipe) = Fixture(Exclude("bg", "day", "aura", "none"));
        try
        {
            // No seam supplied: a pane with nothing to persist through cannot author at all.
            using var orphan = new RecipeDetailViewModel(recipe, book, new ImageBridge(), _ => { });
            Assert.False(orphan.CanEditRules);

            // Seam supplied but the book locked — the same gate reordering uses. Rules are STRUCTURE:
            // what a recipe permits, not pixels, so the pencil's deliberate exemption does not apply.
            using var locked = new RecipeDetailViewModel(recipe, book, new ImageBridge(), _ => { },
                moveLayer: (_, _) => Task.FromResult<LoadedCookBook?>(null), canReorder: false,
                editRules: (_, _) => Task.FromResult<LoadedCookBook?>(null), dialogs: new FakeDialogs());
            Assert.False(locked.CanEditRules);
            Assert.False(locked.AddRuleCommand.CanExecute(null));

            locked.CanReorder = true;
            Assert.True(locked.CanEditRules);
            Assert.True(locked.AddRuleCommand.CanExecute(null));
        }
        finally { book.Dispose(); }
    }

    [AvaloniaFact]
    public void Adding_a_rule_applies_the_edit_the_dialog_produced()
    {
        var (book, recipe) = Fixture();
        try
        {
            var rule = Exclude("bg", "day", "aura", "none");
            var dialogs = new ScriptedDialogs(rule);
            RecipeManifest? applied = null;

            using var vm = new RecipeDetailViewModel(recipe, book, new ImageBridge(), _ => { },
                canReorder: true, dialogs: dialogs,
                editRules: (edit, _) => { applied = edit(recipe.Manifest); return Task.FromResult<LoadedCookBook?>(null); });

            vm.AddRuleCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.NotNull(applied);
            Assert.Equal(rule, Assert.Single(applied!.Rules));
        }
        finally { book.Dispose(); }
    }

    [AvaloniaFact]
    public void A_dismissed_dialog_changes_nothing()
    {
        var (book, recipe) = Fixture();
        try
        {
            bool touched = false;
            using var vm = new RecipeDetailViewModel(recipe, book, new ImageBridge(), _ => { },
                canReorder: true, dialogs: new ScriptedDialogs(null),
                editRules: (_, _) => { touched = true; return Task.FromResult<LoadedCookBook?>(null); });

            vm.AddRuleCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.False(touched);
        }
        finally { book.Dispose(); }
    }

    [AvaloniaFact]
    public void Deleting_a_rule_asks_first_and_removes_the_one_that_was_clicked()
    {
        var (book, recipe) = Fixture(
            Exclude("bg", "day", "aura", "none"),
            Exclude("bg", "night", "aura", "glow"));
        try
        {
            RecipeManifest? applied = null;
            using var vm = new RecipeDetailViewModel(recipe, book, new ImageBridge(), _ => { },
                canReorder: true, dialogs: new ScriptedDialogs(true),
                editRules: (edit, _) => { applied = edit(recipe.Manifest); return Task.FromResult<LoadedCookBook?>(null); });

            vm.DeleteRuleCommand.Execute(vm.Rules[0]);
            Dispatcher.UIThread.RunJobs();

            var left = Assert.Single(applied!.Rules);
            Assert.Equal("night", left.When.VariantId);   // the SECOND rule survived
        }
        finally { book.Dispose(); }
    }

    [AvaloniaFact]
    public void A_declined_delete_removes_nothing()
    {
        var (book, recipe) = Fixture(Exclude("bg", "day", "aura", "none"));
        try
        {
            bool touched = false;
            using var vm = new RecipeDetailViewModel(recipe, book, new ImageBridge(), _ => { },
                canReorder: true, dialogs: new ScriptedDialogs(false),
                editRules: (_, _) => { touched = true; return Task.FromResult<LoadedCookBook?>(null); });

            vm.DeleteRuleCommand.Execute(vm.Rules[0]);
            Dispatcher.UIThread.RunJobs();

            Assert.False(touched);
        }
        finally { book.Dispose(); }
    }

    [AvaloniaFact]
    public void The_panel_draws_an_add_button_and_a_pair_of_row_actions()
    {
        var (book, recipe) = Fixture(Exclude("bg", "day", "aura", "none"));
        try
        {
            using var vm = new RecipeDetailViewModel(recipe, book, new ImageBridge(), _ => { },
                canReorder: true, dialogs: new FakeDialogs(),
                editRules: (_, _) => Task.FromResult<LoadedCookBook?>(null));
            var view = new Views.RecipeDetailView { DataContext = vm };
            var window = new Window { Content = view, Width = 1180, Height = 720 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            try
            {
                var actions = view.GetVisualDescendants().OfType<StackPanel>()
                    .First(p => p.Classes.Contains("ractions"));

                // Reserved, not conditional: the row's columns must be identical whether the pointer
                // is over it or not, so the actions are laid out and merely un-inked.
                Assert.True(actions.Bounds.Width > 0, "the action column is not reserved");
                Assert.Equal(2, actions.GetVisualDescendants().OfType<Button>().Count());
            }
            finally { window.Close(); }
        }
        finally { book.Dispose(); }
    }

    [Fact]
    public void The_add_form_opens_on_a_rule_the_recipe_does_not_already_have()
    {
        // A first rule is usually made of first things, so a form that opened on the first layer and
        // first variant opened refused — telling the user their untouched form was a duplicate
        // before they had touched anything. Caught on a rendered frame.
        //
        // Seating the two halves SEPARATELY does not fix it, which is the instructive part: each is
        // individually unused and the PAIR they form is the rule that already exists.
        var (book, recipe) = Fixture(Exclude("bg", "day", "aura", "none"));
        try
        {
            var vm = Dialog(recipe);
            Assert.Equal(string.Empty, vm.Problem);
            Assert.True(vm.ConfirmCommand.CanExecute(null));
        }
        finally { book.Dispose(); }
    }

}

/// <summary>A dialog layer that answers with a canned result instead of showing anything.</summary>
internal sealed class ScriptedDialogs : IDialogService
{
    private readonly object? _result;

    public ScriptedDialogs(object? result) => _result = result;

    public ViewModelBase? Active => null;
    public event Action? Changed { add { } remove { } }

    public Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog) =>
        Task.FromResult(_result is TResult r ? r : default);

    public void Close(object? result) { }
}
