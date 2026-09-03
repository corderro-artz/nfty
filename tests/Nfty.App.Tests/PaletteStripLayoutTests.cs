using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Collections.Generic;
using Nfty.App.Imaging;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;
using Nfty.Core.Imaging;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The palette strip's layout, asserted from a laid-out visual tree.
/// </summary>
/// <remarks>
/// Written because of a failure no unit test could see and no exception reported: the strip's fixed
/// cells added up to more than the canvas pane's width at the app's minimum size, so the one
/// star-sized column — the saved swatches — was arranged at ZERO width and the palette was simply
/// not there. The ViewModel's collection was correct throughout; only a laid-out frame knows.
///
/// <para>Width is therefore a budget, and this is the test that keeps it one. Anything added to the
/// strip has to shrink something else, and this fails the moment it does not.</para>
/// </remarks>
public class PaletteStripLayoutTests
{
    // The pane track's own minimum: the mockups' 1180 page, less the 262 variants rail and the
    // 300 colorize rail. Below this the panes scroll rather than compress, so this is the narrowest
    // the strip is ever asked to fit into.
    private const double MinimumWindowWidth = 1180;

    // Button.sw's own size, from Styles.axaml. Duplicated deliberately: the point of the assertion
    // below is that the cell has room for a real swatch, which a value read back off the control
    // would satisfy vacuously at any size, zero included.
    private const double SwatchSize = 18;

    private static (Window window, IngredientEditorViewModel vm, Views.IngredientEditorView view)
        Render(int savedSwatches, ThemeVariant variant)
    {
        var (book, recipe, ing) = VisualCapture.DynamicIngredient();
        var palette = new PaletteService(StateStore.InMemory());
        for (int i = 0; i < savedSwatches; i++)
            palette.Add(new RgbColor((byte)(10 + i * 7), (byte)(200 - i * 5), 40));

        var vm = new IngredientEditorViewModel(ing, recipe, book, new ImageBridge(), new FakeNav(),
            new FakeNotYetWired(), new CookBookSession(), new FakeDialogs(), new FilePickerService(),
            palette: palette);
        vm.SetPaintColorCommand.Execute(null);

        var view = new Views.IngredientEditorView { DataContext = vm };
        var window = new Window
        {
            RequestedThemeVariant = variant,
            Content = view,
            Width = MinimumWindowWidth,
            Height = 720,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, view);
    }

    private static List<Button> Swatches(Visual view) => view.GetVisualDescendants()
        .OfType<Button>()
        .Where(b => b.Classes.Contains("sw") && !b.Classes.Contains("add"))
        .ToList();

    private static Border Strip(Visual view) => view.GetVisualDescendants()
        .OfType<Border>().First(b => b.Classes.Contains("pstrip"));

    /// <summary>The saved run's viewport. Its width is the whole point: it is the strip's only
    /// star-sized cell, so it is what absorbs — or vanishes under — everything else's width.</summary>
    private static ScrollViewer SavedViewport(Visual view) => view.GetVisualDescendants()
        .OfType<ScrollViewer>()
        .First(sv => sv.GetVisualAncestors().OfType<Border>().Any(b => b.Classes.Contains("pstrip")));

    private static Button LockButton(Visual view) => view.GetVisualDescendants()
        .OfType<Button>()
        .First(b => b.Classes.Contains("ttool")
                    && b.GetVisualAncestors().OfType<Border>().Any(a => a.Classes.Contains("pstrip")));

    /// <summary>Whether a control's box is entirely inside <paramref name="within"/>. A CLIPPED
    /// control still reports its own non-zero Bounds — that is why "is it laid out?" is not the
    /// question, and containment is.</summary>
    private static bool IsFullyInside(Visual child, Visual within)
    {
        var topLeft = child.TranslatePoint(new Point(0, 0), within);
        var bottomRight = child.TranslatePoint(new Point(child.Bounds.Width, child.Bounds.Height), within);
        if (topLeft is not { } tl || bottomRight is not { } br) return false;
        return tl.X >= -0.5 && tl.Y >= -0.5
            && br.X <= within.Bounds.Width + 0.5 && br.Y <= within.Bounds.Height + 0.5;
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    public void The_saved_swatches_get_real_width_at_the_minimum_window_size(int saved)
    {
        var (window, vm, view) = Render(saved, ThemeVariant.Dark);
        try
        {
            Assert.Equal(saved, vm.SavedSwatches.Count);

            // ONE swatch wide is the floor. Below it the cell is not "tight", it is absent: the
            // column collapses to zero, every saved colour scrolls out of a zero-width viewport, and
            // nothing anywhere reports a problem.
            Assert.True(SavedViewport(view).Bounds.Width >= SwatchSize,
                $"the saved-swatch cell was arranged {SavedViewport(view).Bounds.Width}px wide — "
                + "the strip's fixed cells have outgrown the pane, so the palette is invisible");
        }
        finally { vm.Dispose(); window.Close(); }
    }

    /// <summary>The last cell in the row proves the whole row fits: if the fixed cells overflow, the
    /// lock button is pushed past the strip's edge and clipped.</summary>
    [AvaloniaFact]
    public void Nothing_in_the_strip_is_pushed_off_its_own_edge()
    {
        var (window, vm, view) = Render(4, ThemeVariant.Dark);
        try
        {
            var strip = Strip(view);
            Assert.True(IsFullyInside(LockButton(view), strip),
                "the opacity lock was clipped by the strip — the row's fixed cells do not fit");

            foreach (var cell in Swatches(view).Take(Palette.Slots))
                Assert.True(IsFullyInside(cell, strip), "a ramp slot was clipped by the strip");
        }
        finally { vm.Dispose(); window.Close(); }
    }

    /// <summary>The row is one height whichever mode it offers and whichever way the lock is set —
    /// the ten slots change fill, and the alpha control is dimmed rather than removed. Measured, not
    /// asserted from the markup.</summary>
    [AvaloniaFact]
    public void The_strip_is_the_same_height_in_every_mode_and_lock_state()
    {
        var (window, vm, view) = Render(2, ThemeVariant.Dark);
        try
        {
            var strip = Strip(view);
            var heights = new List<double>();

            foreach (var mode in new[] { PaletteMode.Color, PaletteMode.Grayscale, PaletteMode.Color })
                foreach (var op in new[] { OpacityLock.Locked, OpacityLock.Unlocked })
                {
                    vm.PaintMode = mode;
                    vm.OpacityMode = op;
                    Dispatcher.UIThread.RunJobs();
                    heights.Add(strip.Bounds.Height);
                }

            Assert.All(heights, h => Assert.Equal(heights[0], h));
            Assert.True(heights[0] > 0);
        }
        finally { vm.Dispose(); window.Close(); }
    }

    /// <summary>"Reserve the space, toggle the ink": the alpha slider keeps its box while the lock is
    /// on and only stops taking input. Asserted on the SLIDER, not on what sits after it — the star
    /// column would silently absorb a collapsed cell and leave every neighbour exactly where it was.</summary>
    [AvaloniaFact]
    public void The_alpha_control_keeps_its_box_while_the_lock_is_on()
    {
        var (window, vm, view) = Render(2, ThemeVariant.Dark);
        try
        {
            var alpha = view.GetVisualDescendants().OfType<StackPanel>()
                .First(sp => sp.Classes.Contains("alphacell"));
            var slider = alpha.GetVisualDescendants().OfType<Slider>().First();

            vm.OpacityMode = OpacityLock.Unlocked;
            Dispatcher.UIThread.RunJobs();
            var live = (alpha.Bounds, slider.Bounds);

            vm.OpacityMode = OpacityLock.Locked;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(live.Item1, alpha.Bounds);
            Assert.Equal(live.Item2, slider.Bounds);
            Assert.True(slider.Bounds.Width > 0);
            Assert.False(alpha.IsHitTestVisible);   // inert, not gone
        }
        finally { vm.Dispose(); window.Close(); }
    }
}
