using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.ViewModels;
using Nfty.Core.Model;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>Guards that a wizard card actually FITS at the size the app ships at.
///
/// New Ingredient is the tallest form in the app — it is the only one carrying two color-range
/// controls, and Fluent lays a horizontal Slider out at roughly twice the height the mockup's range
/// row wants. When it overflows, the body's ScrollViewer silently absorbs the excess: DESTINATION,
/// CANVAS and the derived-identifier row drop below the fold behind a scrollbar with no cue that
/// anything is missing. That is not a crash and not a failing assertion anywhere else — it renders
/// as a perfectly tidy card that is quietly hiding a third of itself, which is exactly why it
/// survived several passes.
///
/// The frames are the real check on how the card LOOKS. This pins the one thing a frame cannot
/// assert cheaply: that nothing is hidden.</summary>
public class WizardFitsTests
{
    // MainWindow's own shipping size. Measuring at any other size is meaningless here — the card is
    // centered in the page area and a smaller host would report an overflow the user never sees.
    private const int WindowWidth = 1180;
    private const int WindowHeight = 720;

    private static T ShowAtShippingSize<T>(T control) where T : Control
    {
        var window = new Window { Content = control, Width = WindowWidth, Height = WindowHeight };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return control;
    }

    private static ScrollViewer BodyScroller(Visual view) =>
        view.GetVisualDescendants().OfType<ScrollViewer>().First();

    [AvaloniaFact]
    public void New_ingredient_form_fits_without_hiding_fields()
    {
        // Dynamic is the worst case: it is the only kind that reveals BOTH color-range controls.
        var view = ShowAtShippingSize(new Views.NewIngredientView
        {
            DataContext = new NewIngredientViewModel(new FakeDialogs())
            {
                Name = "Aura",
                Kind = LayerKind.Dynamic,
            },
        });

        var scroller = BodyScroller(view);

        Assert.True(
            scroller.Extent.Height <= scroller.Viewport.Height + 0.5,
            $"New Ingredient overflows its card by {scroller.Extent.Height - scroller.Viewport.Height:0.#}px, " +
            "so the fields at the bottom (Destination, Canvas, the derived identifier) are hidden behind " +
            "a scrollbar. Reclaim height in the form rather than pinning the Slider or its Panel — both " +
            "of those clip the range control's ring handles.");
    }

    [AvaloniaFact]
    public void Dynamic_is_the_tallest_kind_so_the_other_kinds_fit_too()
    {
        foreach (var kind in new[] { LayerKind.Static, LayerKind.Custom })
        {
            var view = ShowAtShippingSize(new Views.NewIngredientView
            {
                DataContext = new NewIngredientViewModel(new FakeDialogs())
                {
                    Name = "Aura",
                    Kind = kind,
                },
            });

            var scroller = BodyScroller(view);
            Assert.True(
                scroller.Extent.Height <= scroller.Viewport.Height + 0.5,
                $"New Ingredient overflows its card for kind {kind}.");
        }
    }

    /// <summary>
    /// The import-an-image form fits too, in the state that shows the most.
    /// </summary>
    /// <remarks>
    /// Dynamic again, for the same reason, plus this form's own additions - a preview frame and the
    /// caveat about discarding a picture's colors. Its refusal line sits OUTSIDE the scroller, so
    /// what this measures is the form proper; a refusal that can be scrolled away from is a disabled
    /// button with no reason given.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(LayerKind.Dynamic)]
    [InlineData(LayerKind.Static)]
    [InlineData(LayerKind.Custom)]
    public void Import_image_form_fits_without_hiding_fields(LayerKind kind)
    {
        using var vm = VisualCapture.ImportImageForm(new FakeDialogs());
        vm.Kind = kind;
        var view = ShowAtShippingSize(new Views.ImportImageView { DataContext = vm });

        var scroller = BodyScroller(view);
        Assert.True(
            scroller.Extent.Height <= scroller.Viewport.Height + 0.5,
            $"Import image overflows its card by {scroller.Extent.Height - scroller.Viewport.Height:0.#}px "
            + $"for kind {kind}, so the fields at the bottom are hidden behind a scrollbar.");
    }
}
