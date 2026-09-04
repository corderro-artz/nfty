using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;
using PixelRect = Nfty.Core.Editing.PixelRect;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The canvas overlay: the marquee that shows what is selected, in the place the pixels actually
/// are.
/// </summary>
/// <remarks>
/// The ViewModel knowing a <see cref="IngredientEditorViewModel.Selection"/> is not the feature —
/// the user seeing it is. That mapping (canvas pixels to control coordinates, through
/// <c>Stretch="Uniform"</c>) is the part that can be wrong while every ViewModel test stays green,
/// so it is asserted off a laid-out frame.
/// </remarks>
public class EditorOverlayTests
{
    private static (Window window, IngredientEditorViewModel vm, Views.IngredientEditorView view) Render()
    {
        var (book, recipe, ing) = VisualCapture.DynamicIngredient();
        var vm = new IngredientEditorViewModel(ing, recipe, book, new ImageBridge(), new FakeNav(),
            new CookBookSession(), new FakeDialogs(), new FilePickerService());
        var view = new Views.IngredientEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, view);
    }

    private static Canvas Overlay(Visual view) =>
        view.GetVisualDescendants().OfType<Canvas>().First(c => c.Name == "CanvasOverlay");

    private static Image Art(Visual view) =>
        view.GetVisualDescendants().OfType<Image>().First(i => i.Name == "CanvasImage");

    [AvaloniaFact]
    public void No_selection_draws_nothing_over_the_art()
    {
        var (window, vm, view) = Render();
        try
        {
            Assert.Null(vm.Selection);
            Assert.Empty(Overlay(view).Children);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>
    /// The marquee lands on the pixels it names. The canvas is drawn letterboxed inside the tile, so
    /// a marquee drawn in raw pixel units — the obvious wrong version — would sit in the corner at a
    /// fraction of the size, and every ViewModel assertion would still pass.
    /// </summary>
    [AvaloniaFact]
    public void A_selection_draws_a_marquee_over_exactly_those_pixels()
    {
        var (window, vm, view) = Render();
        try
        {
            var art = Art(view);
            var bmp = (Avalonia.Media.Imaging.Bitmap)art.Source!;
            double scale = System.Math.Min(art.Bounds.Width / bmp.PixelSize.Width,
                                           art.Bounds.Height / bmp.PixelSize.Height);
            Assert.True(scale > 1, "the fixture canvas should be drawn larger than its pixel count");

            vm.Selection = new PixelRect(2, 3, 4, 5);
            Dispatcher.UIThread.RunJobs();

            var marquee = Assert.IsType<Rectangle>(Assert.Single(Overlay(view).Children));
            Assert.Equal(4 * scale, marquee.Width, 1);
            Assert.Equal(5 * scale, marquee.Height, 1);
            Assert.NotEmpty(marquee.StrokeDashArray!);      // a marquee, not a solid box

            double offX = (art.Bounds.Width - bmp.PixelSize.Width * scale) / 2;
            double offY = (art.Bounds.Height - bmp.PixelSize.Height * scale) / 2;
            Assert.Equal(offX + 2 * scale, Canvas.GetLeft(marquee), 1);
            Assert.Equal(offY + 3 * scale, Canvas.GetTop(marquee), 1);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>The overlay must never eat the pointer — it sits directly over the surface the user
    /// is drawing on.</summary>
    [AvaloniaFact]
    public void The_overlay_is_not_hit_testable()
    {
        var (window, vm, view) = Render();
        try
        {
            vm.Selection = new PixelRect(0, 0, 2, 2);
            Dispatcher.UIThread.RunJobs();
            Assert.False(Overlay(view).IsHitTestVisible);
            Assert.All(Overlay(view).Children.OfType<Control>(), c => Assert.False(c.IsHitTestVisible));
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>
    /// The gesture end to end: real pointer events on the real control. Everything above asserts the
    /// overlay given a selection; this asserts that dragging is what produces one, and that the band
    /// is live during the drag rather than only after it — which is the whole complaint about the
    /// shape tools ("you drag blind and find out on release").
    /// </summary>
    [AvaloniaFact]
    public void Dragging_shows_a_live_band_and_leaves_a_marquee_behind()
    {
        var (window, vm, view) = Render();
        try
        {
            vm.ActiveTool = EditorTool.Select;
            Dispatcher.UIThread.RunJobs();

            var art = Art(view);
            var origin = art.TranslatePoint(default, window)!.Value;
            var bmp = (Avalonia.Media.Imaging.Bitmap)art.Source!;
            double scale = System.Math.Min(art.Bounds.Width / bmp.PixelSize.Width,
                                           art.Bounds.Height / bmp.PixelSize.Height);
            double offX = (art.Bounds.Width - bmp.PixelSize.Width * scale) / 2;
            double offY = (art.Bounds.Height - bmp.PixelSize.Height * scale) / 2;
            Point At(int px, int py) =>
                new(origin.X + offX + (px + 0.5) * scale, origin.Y + offY + (py + 0.5) * scale);

            window.MouseDown(At(1, 1), Avalonia.Input.MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            var pressed = Assert.IsType<Rectangle>(Assert.Single(Overlay(view).Children));
            Assert.Equal(scale, pressed.Width, 1);              // one pixel, where the press landed

            window.MouseMove(At(4, 5));
            Dispatcher.UIThread.RunJobs();
            // The band FOLLOWS. Redrawing only on press would leave the 1px box above and still
            // satisfy "something is on the overlay", which is the assertion worth not writing.
            var band = Assert.IsType<Rectangle>(Assert.Single(Overlay(view).Children));
            Assert.Equal(4 * scale, band.Width, 1);
            Assert.Equal(5 * scale, band.Height, 1);
            Assert.Null(vm.Selection);                          // and nothing committed yet

            window.MouseUp(At(4, 5), Avalonia.Input.MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(new PixelRect(1, 1, 4, 5), vm.Selection);
            Assert.Single(Overlay(view).Children);              // the band gone, the marquee standing
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>
    /// A drag that starts INSIDE the marquee drags the marquee, and shows that while it is
    /// happening.
    /// </summary>
    /// <remarks>
    /// Found by dragging the real app: mid-move the overlay drew a second, growing mark box, because
    /// mark-vs-move was only decided on release and the band had no idea which gesture it was in.
    /// The user saw the opposite of what release was about to do. Two things pin it — that there is
    /// exactly ONE outline (not the standing marquee plus a new box), and that it sits at the
    /// dragged-to position rather than the original one.
    /// </remarks>
    [AvaloniaFact]
    public void A_move_drags_the_marquee_rather_than_drawing_a_new_one()
    {
        var (window, vm, view) = Render();
        try
        {
            var art = Art(view);
            var origin = art.TranslatePoint(default, window)!.Value;
            var bmp = (Avalonia.Media.Imaging.Bitmap)art.Source!;
            double scale = System.Math.Min(art.Bounds.Width / bmp.PixelSize.Width,
                                           art.Bounds.Height / bmp.PixelSize.Height);
            double offX = (art.Bounds.Width - bmp.PixelSize.Width * scale) / 2;
            double offY = (art.Bounds.Height - bmp.PixelSize.Height * scale) / 2;
            Point At(int px, int py) =>
                new(origin.X + offX + (px + 0.5) * scale, origin.Y + offY + (py + 0.5) * scale);

            vm.ActiveTool = EditorTool.Select;
            vm.Selection = new PixelRect(1, 1, 3, 3);
            Dispatcher.UIThread.RunJobs();

            window.MouseDown(At(2, 2), Avalonia.Input.MouseButton.Left);   // inside it
            window.MouseMove(At(6, 5));                                    // +4, +3
            Dispatcher.UIThread.RunJobs();

            var ghost = Assert.IsType<Rectangle>(Assert.Single(Overlay(view).Children));
            Assert.Equal(3 * scale, ghost.Width, 1);                       // same size, not a new box
            Assert.Equal(3 * scale, ghost.Height, 1);
            Assert.Equal(offX + (1 + 4) * scale, Canvas.GetLeft(ghost), 1);
            Assert.Equal(offY + (1 + 3) * scale, Canvas.GetTop(ghost), 1);

            window.MouseUp(At(6, 5), Avalonia.Input.MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new PixelRect(5, 4, 3, 3), vm.Selection);         // where the ghost was
        }
        finally { window.Close(); vm.Dispose(); }
    }

    [AvaloniaFact]
    public void Clearing_the_selection_clears_the_overlay()
    {
        var (window, vm, view) = Render();
        try
        {
            vm.Selection = new PixelRect(1, 1, 3, 3);
            Dispatcher.UIThread.RunJobs();
            Assert.Single(Overlay(view).Children);

            vm.ClearSelectionCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(Overlay(view).Children);
        }
        finally { window.Close(); vm.Dispose(); }
    }
}
