using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The Recipe detail's rules rail, measured off a laid-out frame.
///
/// <para>It used to stack two bordered chips per rule with the relationship carried only by a mark
/// in the gutter and that mark's tooltip — so a reader had to learn a symbol vocabulary before the
/// panel said anything, and a two-target rule read as two separate rules. It is a table now, and
/// each of these pins one thing that redesign had to get right.</para>
/// </summary>
public class RulesPanelTests
{
    private static (LoadedCookBook book, LoadedRecipe recipe) Fixture(int ruleCount)
    {
        LoadedIngredient Ing(string id, params string[] vs) => new()
        {
            Manifest = new IngredientManifest(id, id.ToUpperInvariant(), LayerKind.Custom, null,
                vs.Select(v => new Variant(v, v, 1)).ToArray()),
            VariantImages = vs.ToDictionary(v => v, _ => new Image<Rgba32>(4, 4)),
        };

        // Alternating types and a two-target rule in the middle, because a list that is all one kind
        // proves nothing about a panel whose whole job is telling the two kinds apart.
        var pool = new List<IncompatibilityRule>();
        for (int i = 0; i < 12; i++)
            pool.Add(new IncompatibilityRule(
                i % 2 == 0 ? RuleType.Exclude : RuleType.Require,
                new RuleTarget("bg", i % 2 == 0 ? "day" : "night"),
                i == 2
                    ? new[] { new RuleTarget("aura", "none"), new RuleTarget("hat", "crown") }
                    : new[] { new RuleTarget("aura", i % 4 == 0 ? "none" : "glow") }));

        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "bg", "aura", "hat" },
                pool.Take(ruleCount).ToList()),
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

    private static (Window window, RecipeDetailViewModel vm, Views.RecipeDetailView view) Render(int ruleCount)
    {
        var (book, recipe) = Fixture(ruleCount);
        var vm = new RecipeDetailViewModel(recipe, book, new ImageBridge(), _ => { });
        var view = new Views.RecipeDetailView { DataContext = vm };
        var window = new Window { Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, view);
    }

    private static Border Panel(Visual root) => root.GetVisualDescendants().OfType<Border>()
        .First(b => b.Classes.Contains("rules-panel"));

    [AvaloniaFact]
    public void Each_rule_is_one_row_that_names_its_relationship_in_a_word()
    {
        var (window, vm, view) = Render(3);
        try
        {
            var rows = view.GetVisualDescendants().OfType<Border>()
                .Where(b => b.Classes.Contains("rulerow")).ToList();
            Assert.Equal(3, rows.Count);

            // The word, not just the mark. The mark alone was the whole problem: nothing on screen
            // said which of the two kinds a badge meant.
            var words = rows.SelectMany(r => r.GetVisualDescendants().OfType<TextBlock>())
                .Where(t => t.Classes.Contains("rmark"))
                .Select(t => t.Text).ToList();
            Assert.Equal(new[] { "never", "always", "never" }, words);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    [AvaloniaFact]
    public void A_two_target_rule_is_one_row_not_two()
    {
        var (window, vm, view) = Render(3);
        try
        {
            // Rule 3 of the fixture carries two targets. The old stacked-chip layout drew them as
            // three unlabelled chips in a column with nothing saying where the rule ended.
            Assert.Equal(2, vm.Rules[2].Targets.Count);
            Assert.Equal(3, view.GetVisualDescendants().OfType<Border>()
                .Count(b => b.Classes.Contains("rulerow")));
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>
    /// THE ONE THAT MATTERS. The panel had no <c>ScrollViewer</c> at all: a long rule list did not
    /// scroll, the panel Border simply grew past the bottom of the pane and every rule below the
    /// fold was unreachable. Asserted two ways — the content really is taller than its viewport, and
    /// the panel really does stay inside the window — because either alone can pass on a panel that
    /// is broken the other way.
    /// </summary>
    [AvaloniaFact]
    public void A_long_rule_list_scrolls_inside_the_panel_rather_than_overflowing_the_pane()
    {
        var (window, vm, view) = Render(12);
        try
        {
            var scroller = Panel(view).GetVisualDescendants().OfType<ScrollViewer>().First();
            Assert.True(scroller.Extent.Height > scroller.Viewport.Height,
                $"12 rules measured {scroller.Extent.Height:0.#} in a {scroller.Viewport.Height:0.#} "
                + "viewport — the fixture is not long enough to prove anything");

            var panel = Panel(view);
            var bottom = panel.TranslatePoint(new Avalonia.Point(0, panel.Bounds.Height), view)!.Value.Y;
            Assert.True(bottom <= view.Bounds.Height + 0.5,
                $"the panel runs to {bottom:0.#} in a {view.Bounds.Height:0.#} pane");
        }
        finally { window.Close(); vm.Dispose(); }
    }

    [AvaloniaFact]
    public void A_recipe_with_no_rules_says_so_and_draws_no_column_heads()
    {
        var (window, vm, view) = Render(0);
        try
        {
            var panel = Panel(view);
            var texts = panel.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();

            Assert.Contains("No incompatibility rules", texts);
            // Column heads over an empty table label nothing. IsEffectivelyVisible, not Bounds:
            // Avalonia does not reset an arranged Bounds when an ancestor goes invisible.
            Assert.DoesNotContain("LAYER", texts);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Border>(),
                b => b.Classes.Contains("rulerow"));
        }
        finally { window.Close(); vm.Dispose(); }
    }

    [AvaloniaFact]
    public void The_header_badge_counts_the_rules_and_does_not_repeat_the_word()
    {
        var (window, vm, view) = Render(3);
        try
        {
            Assert.Equal("3", vm.RulesBadgeText);
            Assert.Equal("3 rules", vm.RuleCountText);   // the hero's sentence, a different string
            var badge = Panel(view).GetVisualDescendants().OfType<Border>()
                .First(b => b.Classes.Contains("tabcount"));
            Assert.Equal("3", badge.GetVisualDescendants().OfType<TextBlock>().First().Text);
        }
        finally { window.Close(); vm.Dispose(); }
    }
}
