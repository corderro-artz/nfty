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
using Nfty.Core.Model;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// NO MODAL SCROLLS AT THE SMALLEST WINDOW THE APP ALLOWS.
/// </summary>
/// <remarks>
/// <para>A page may scroll; a modal may not. When a card overflows, the body's ScrollViewer absorbs
/// it in silence — the card still looks tidy and is quietly hiding a third of itself behind a bar
/// nothing points at. That is not a crash and not a failing assertion anywhere else, which is why it
/// survived several passes.</para>
///
/// <para><b>And the old version of this file agreed with the bug, for the reason this repo has now
/// hit three times: a layout test that states its own width is testing a window the app does not
/// have.</b> It measured at a raw 1180x720 — wider AND taller than any page this app gives a modal,
/// because the shell renders at <see cref="ShellViewModel.BaseScale"/> and the titlebar and status
/// bar take <see cref="ShellViewModel.ChromeReserve"/> off the top and bottom first. The real area
/// is about 1067x526, and measured there FIVE modal states overflowed: New Ingredient by 112px,
/// Import Image by 194, New CookBook by 58. The numbers here are DERIVED from the shell so they
/// cannot go stale again.</para>
///
/// <para>The frames are the real check on how a card LOOKS. This pins the one thing a frame cannot
/// assert cheaply: that nothing is hidden.</para>
/// </remarks>
public class WizardFitsTests
{
    /// <summary>The widest a modal is ever given: the window minimum, less the frame gutter, at the
    /// scale the whole shell renders at.</summary>
    private static double PageWidth => ShellViewModel.MinWindowWidth / ShellViewModel.BaseScale;

    /// <summary>And the tallest — the titlebar and the status bar come off first.</summary>
    private static double PageHeight =>
        (ShellViewModel.MinWindowHeight - ShellViewModel.ChromeReserve) / ShellViewModel.BaseScale;

    /// <summary>How much room a card must keep spare. Fitting exactly is one style tweak from not
    /// fitting, which is how every overflow in this file's history started.</summary>
    private const double Slack = 12;

    private static Window Show(Control control)
    {
        var window = new Window { Content = control, Width = PageWidth, Height = PageHeight };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    /// <summary>Every scroller inside the view that could hide something, outermost first. A modal
    /// has more than one — a NumericUpDown's inner TextBox carries its own — so the assertion has to
    /// be about ALL of them rather than about the first one found.</summary>
    private static IEnumerable<ScrollViewer> Scrollers(Visual view) =>
        view.GetVisualDescendants().OfType<ScrollViewer>();

    private static void AssertNothingHidden(string what, Control view)
    {
        var window = Show(view);
        try
        {
            foreach (var sv in Scrollers(view))
            {
                double over = sv.Extent.Height - sv.Viewport.Height;
                Assert.True(over <= 0.5,
                    $"{what} overflows by {over:0.#}px at {PageWidth:0}x{PageHeight:0} — the fields at "
                    + "the bottom are behind a scrollbar with nothing pointing at them.");
            }

            var card = view.GetVisualDescendants().OfType<Border>()
                .FirstOrDefault(b => b.Classes.Contains("modal"));
            if (card is null) return;

            Assert.True(card.Bounds.Height <= view.Bounds.Height - Slack,
                $"{what}'s card is {card.Bounds.Height:0} of {view.Bounds.Height:0} — it has less than "
                + $"{Slack}px of room, and fitting exactly is one style tweak from not fitting.");
            Assert.True(card.Bounds.Width <= view.Bounds.Width - Slack,
                $"{what}'s card is {card.Bounds.Width:0} wide in {view.Bounds.Width:0} of page.");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(LayerKind.Dynamic)]      // the worst case: the only kind that reveals both bands
    [InlineData(LayerKind.Static)]
    [InlineData(LayerKind.Custom)]
    public void New_ingredient_hides_nothing(LayerKind kind) =>
        AssertNothingHidden($"New Ingredient ({kind})", new Views.NewIngredientView
        {
            DataContext = new NewIngredientViewModel(new FakeDialogs()) { Name = "Aura", Kind = kind },
        });

    /// <summary>The loose destination adds a CANVAS field the CookBook branch inherits instead —
    /// the state with the most fields on screen at once.</summary>
    [AvaloniaFact]
    public void New_ingredient_hides_nothing_with_the_canvas_field_open()
    {
        var vm = new NewIngredientViewModel(new FakeDialogs()) { Name = "Aura", Kind = LayerKind.Dynamic };
        vm.IsLooseKitchen = true;
        Assert.True(vm.ShowCanvas, "the fixture no longer reaches the state it was written for");
        AssertNothingHidden("New Ingredient (loose)", new Views.NewIngredientView { DataContext = vm });
    }

    [AvaloniaTheory]
    [InlineData(LayerKind.Dynamic)]
    [InlineData(LayerKind.Static)]
    [InlineData(LayerKind.Custom)]
    public void Import_image_hides_nothing(LayerKind kind)
    {
        using var vm = VisualCapture.ImportImageForm(new FakeDialogs());
        vm.Kind = kind;
        AssertNothingHidden($"Import image ({kind})", new Views.ImportImageView { DataContext = vm });
    }

    [AvaloniaFact]
    public void New_cookbook_hides_nothing() =>
        AssertNothingHidden("New CookBook", new Views.NewCookBookView
        {
            DataContext = new NewCookBookViewModel(new FakeDialogs()) { Name = "VaporPets" },
        });

    [AvaloniaFact]
    public void New_recipe_hides_nothing() =>
        AssertNothingHidden("New Recipe", new Views.NewRecipeView
        {
            DataContext = new NewRecipeViewModel(new FakeDialogs()) { Name = "Cat" },
        });

    [AvaloniaFact]
    public void The_quick_reference_sheet_hides_nothing() =>
        AssertNothingHidden("The quick-reference sheet",
            new Views.HelpView { DataContext = new HelpViewModel(new FakeDialogs()) });

    /// <summary>
    /// A wizard card HUGS its form rather than stretching to the page.
    /// </summary>
    /// <remarks>
    /// The cards were <c>VerticalAlignment="Stretch"</c>, which bought the clamp that keeps a card
    /// inside its page and paid for it with a void: New Ingredient's form is about 370px in a 506px
    /// card, so every wizard drew a hundred and fifty pixels of empty panel under its last field.
    /// Avalonia arranges a non-Stretch child at <c>min(desired, available)</c>, so Center buys the
    /// clamp too — this asserts the hug, and <see cref="AssertNothingHidden"/> above asserts the
    /// clamp.
    /// </remarks>
    [AvaloniaFact]
    public void A_wizard_card_ends_where_its_form_ends()
    {
        var view = new Views.NewRecipeView
        {
            DataContext = new NewRecipeViewModel(new FakeDialogs()) { Name = "Cat" },
        };
        var window = Show(view);
        try
        {
            var card = view.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("modal"));
            Assert.True(card.Bounds.Height < view.Bounds.Height - 40,
                $"the card fills {card.Bounds.Height:0} of {view.Bounds.Height:0} — a short form should "
                + "not draw an empty panel under itself");
        }
        finally { window.Close(); }
    }
}
