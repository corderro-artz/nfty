using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;
using Xunit;
using Image = Avalonia.Controls.Image;

namespace Nfty.App.Tests;

/// <summary>
/// THE CANVAS ZOOMS AND PANS, AND EVERYTHING THAT READS THE CANVAS FOLLOWS.
/// </summary>
/// <remarks>
/// <para>A pixel editor whose canvas is pinned to one size is one you cannot do detail work in: an
/// 8px sprite in a 320px tile is fine and a 512px drawing in the same tile is under two thirds of a
/// device pixel per art pixel, which the backdrop already refuses to draw a lattice at.</para>
///
/// <para><b>The load-bearing test is the one that paints.</b> Zoom touches four things that each map
/// between canvas pixels and the screen — the drawn art, the pointer, the marquee and the backdrop
/// lattice — and any one of them left behind is a canvas where you click one pixel and paint
/// another. They all go through one function, so the proof is to zoom, click, and ask which pixel
/// changed.</para>
/// </remarks>
public class CanvasZoomTests
{
    private static (Window Window, IngredientEditorViewModel Vm, Views.IngredientEditorView View) Render()
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

    private static Image Art(Visual view) =>
        view.GetVisualDescendants().OfType<Image>().First(i => i.Name == "CanvasImage");

    private static Panel Backdrop(Visual view) =>
        view.GetVisualDescendants().OfType<Panel>().First(p => p.Name == "CanvasBackdrop");

    /// <summary>
    /// A pointer position over one canvas pixel's center, in window coordinates — at the FITTED zoom,
    /// which is the only state a test may compute one in.
    /// </summary>
    /// <remarks>
    /// The guard is not decoration: <c>TranslatePoint</c> walks THROUGH the art's render transform,
    /// so calling this after a zoom returns points in a space nothing on screen is in. It cost a
    /// green-looking failure once already.
    /// </remarks>
    private static Func<double, double, Point> FittedMapper(Window window, Image art)
    {
        Assert.False(art.RenderTransform is TransformGroup { Children: [ScaleTransform { ScaleX: not 1 }, _] },
            "FittedMapper was called after a zoom, so its points are in a space nothing is drawn in");
        var origin = art.TranslatePoint(default, window)!.Value;
        var bmp = (Bitmap)art.Source!;
        double scale = Math.Min(art.Bounds.Width / bmp.PixelSize.Width,
                                art.Bounds.Height / bmp.PixelSize.Height);
        double offX = (art.Bounds.Width - bmp.PixelSize.Width * scale) / 2;
        double offY = (art.Bounds.Height - bmp.PixelSize.Height * scale) / 2;
        return (px, py) => new Point(origin.X + offX + (px + 0.5) * scale,
                                     origin.Y + offY + (py + 0.5) * scale);
    }

    /// <summary>Every canvas pixel whose value differs from the given snapshot.</summary>
    private static (int X, int Y)[] Changed(IngredientEditorViewModel vm, byte[,] before)
    {
        int n = before.GetLength(0);
        return (from x in Enumerable.Range(0, n)
                from y in Enumerable.Range(0, n)
                where vm.ValueAt(x, y) != before[x, y]
                select (x, y)).ToArray();
    }

    private static byte[,] Snapshot(IngredientEditorViewModel vm, int n)
    {
        var map = new byte[n, n];
        for (int x = 0; x < n; x++)
            for (int y = 0; y < n; y++)
                map[x, y] = vm.ValueAt(x, y);
        return map;
    }

    private static ScaleTransform DrawnScale(Visual view) =>
        (ScaleTransform)((TransformGroup)Art(view).RenderTransform!).Children[0];

    private static TranslateTransform DrawnOffset(Visual view) =>
        (TranslateTransform)((TransformGroup)Art(view).RenderTransform!).Children[1];

    // ---- the state it opens in ---------------------------------------------------------------

    [AvaloniaFact]
    public void The_canvas_opens_fitted_and_the_chip_says_so()
    {
        var (window, vm, view) = Render();
        try
        {
            Assert.Equal(IngredientEditorViewModel.ZoomMin, vm.Zoom);
            Assert.False(vm.IsZoomed);
            Assert.Equal("fit", vm.ZoomText);
            Assert.Equal(1, DrawnScale(view).ScaleX);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>The ceiling belongs to the property, so the wheel, the keys and the commands cannot
    /// disagree about the limits — the rule <c>BrushSize</c> and <c>GridSize</c> already keep.</summary>
    [AvaloniaTheory]
    [InlineData(1000, IngredientEditorViewModel.ZoomMax)]
    [InlineData(0.1, IngredientEditorViewModel.ZoomMin)]
    [InlineData(double.NaN, IngredientEditorViewModel.ZoomMin)]
    public void The_zoom_is_clamped_by_the_property_itself(double asked, double expected)
    {
        var (window, vm, _) = Render();
        try
        {
            vm.Zoom = asked;
            Assert.Equal(expected, vm.Zoom);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    // ---- the gesture -------------------------------------------------------------------------

    [AvaloniaFact]
    public void A_wheel_notch_over_the_canvas_draws_the_art_bigger()
    {
        var (window, vm, view) = Render();
        try
        {
            var at = FittedMapper(window, Art(view));
            window.MouseWheel(at(4, 4), new Vector(0, 1));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(IngredientEditorViewModel.ZoomStep, vm.Zoom, 6);
            Assert.True(vm.IsZoomed);
            Assert.Equal("125%", vm.ZoomText);
            Assert.Equal(vm.Zoom, DrawnScale(view).ScaleX, 6);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>
    /// THE ONE THAT MATTERS: after zooming in on a pixel, clicking the same point paints that same
    /// pixel.
    /// </summary>
    /// <remarks>
    /// It proves two things at once and neither can be faked. The zoom is ANCHORED — the art under
    /// the pointer stayed under the pointer, which is the whole difference between a magnifier and a
    /// control you fight — and the pointer mapping went with the drawn art, which is what stops a
    /// magnified canvas painting the pixel next door. Asked of the surface the release wrote, not of
    /// any coordinate the test worked out for itself.
    /// </remarks>
    [AvaloniaFact]
    public void Painting_after_a_zoom_hits_the_pixel_that_is_under_the_pointer()
    {
        var (window, vm, view) = Render();
        try
        {
            vm.ActiveTool = EditorTool.Brush;
            vm.BrushSize = 1;
            vm.BrushValue = 255;
            Dispatcher.UIThread.RunJobs();

            var target = FittedMapper(window, Art(view))(5, 2);

            // Four notches, all anchored on the same point: 1.25^4, comfortably past the point where
            // an unanchored zoom would have walked the target pixel off under the pointer.
            for (int i = 0; i < 4; i++) window.MouseWheel(target, new Vector(0, 1));
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.Zoom > 2, "the zoom did not take");

            var before = Snapshot(vm, 8);
            window.MouseDown(target, MouseButton.Left);
            window.MouseUp(target, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(new[] { (5, 2) }, Changed(vm, before));
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>
    /// THE PROBE for the test above. At the fitted zoom the same click lands on the same pixel, so
    /// that test would pass on a canvas that ignored the zoom entirely — unless the zoom moves the
    /// art, which this asserts it does.
    /// </summary>
    [AvaloniaFact]
    public void An_anchored_zoom_actually_moves_the_art()
    {
        var (window, vm, view) = Render();
        try
        {
            // Off-center on purpose: anchoring on the middle is a no-op whatever the code does.
            var corner = FittedMapper(window, Art(view))(1, 1);
            for (int i = 0; i < 4; i++) window.MouseWheel(corner, new Vector(0, 1));
            Dispatcher.UIThread.RunJobs();

            Assert.NotEqual(0, DrawnOffset(view).X);
            Assert.NotEqual(0, DrawnOffset(view).Y);
            Assert.Equal(vm.PanX, DrawnOffset(view).X, 6);
            Assert.Equal(vm.PanY, DrawnOffset(view).Y, 6);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>A middle-button drag pans, and does not paint — two answers to one drag is the trap
    /// a gesture decided at press avoids.</summary>
    [AvaloniaFact]
    public void A_middle_drag_pans_and_paints_nothing()
    {
        var (window, vm, view) = Render();
        try
        {
            vm.ActiveTool = EditorTool.Brush;
            vm.BrushValue = 255;
            Dispatcher.UIThread.RunJobs();

            // The mapper first, then the zoom: see FittedMapper.
            var at = FittedMapper(window, Art(view));
            vm.Zoom = 4;
            Dispatcher.UIThread.RunJobs();
            var before = Snapshot(vm, 8);

            window.MouseDown(at(4, 4), MouseButton.Middle);
            window.MouseMove(at(2, 4));
            window.MouseUp(at(2, 4), MouseButton.Middle);
            Dispatcher.UIThread.RunJobs();

            Assert.True(vm.PanX < 0, "the art did not follow the drag");
            Assert.Empty(Changed(vm, before));
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>The keyboard path, which a pointer gesture in this app is required to ship with —
    /// and the only path at all on a trackpad with no middle button.</summary>
    [AvaloniaFact]
    public void The_keys_zoom_and_pan_as_well()
    {
        var (window, vm, view) = Render();
        try
        {
            view.Focus();
            Dispatcher.UIThread.RunJobs();

            window.KeyPressQwerty(PhysicalKey.Equal, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.IsZoomed, "+ did not zoom in");

            window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            // Right moves the VIEW right, so the art travels left — the direction every scroller has
            // taught, and the opposite of dragging the art itself.
            Assert.True(vm.PanX < 0, "the arrow key did not pan");

            window.KeyPressQwerty(PhysicalKey.Digit0, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.False(vm.IsZoomed);
            Assert.Equal(0, vm.PanX);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    // ---- what the pan is allowed to do -------------------------------------------------------

    /// <summary>Fitted, the art has nowhere to go, and a pan left over from a magnified view would
    /// shove it sideways inside a tile it already fits.</summary>
    [AvaloniaFact]
    public void At_fit_there_is_nothing_to_pan()
    {
        var (window, vm, _) = Render();
        try
        {
            vm.PanBy(200, 200);
            Assert.Equal(0, vm.PanX);
            Assert.Equal(0, vm.PanY);

            vm.Zoom = 4;
            vm.PanBy(30, 0);
            Assert.NotEqual(0, vm.PanX);

            vm.ZoomToFitCommand.Execute(null);
            Assert.Equal(0, vm.PanX);
            Assert.Equal(0, vm.PanY);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>
    /// The art cannot be pushed off its own tile. Half the difference between the tile and the art
    /// is the whole rule, and at four times an 8px canvas in a 320px tile that is exactly 480.
    /// </summary>
    [AvaloniaFact]
    public void The_art_cannot_be_panned_off_the_tile()
    {
        var (window, vm, view) = Render();
        try
        {
            vm.Zoom = 4;
            Dispatcher.UIThread.RunJobs();

            vm.PanBy(10_000, 10_000);
            Dispatcher.UIThread.RunJobs();

            double artSide = Art(view).Bounds.Width * 4;
            double room = (artSide - Art(view).Bounds.Width) / 2;
            Assert.Equal(room, vm.PanX, 3);
            Assert.Equal(room, vm.PanY, 3);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    // ---- everything that reads the canvas ----------------------------------------------------

    /// <summary>The lattice is drawn from the same geometry the pointer is, so a square is still one
    /// canvas pixel at any magnification — four times as many device pixels across.</summary>
    [AvaloniaFact]
    public void The_lattice_square_grows_with_the_zoom()
    {
        var (window, vm, view) = Render();
        try
        {
            double fitted = ((DrawingBrush)Backdrop(view).Background!).DestinationRect.Rect.Width;

            vm.Zoom = 4;
            Dispatcher.UIThread.RunJobs();

            double zoomed = ((DrawingBrush)Backdrop(view).Background!).DestinationRect.Rect.Width;
            Assert.Equal(fitted * 4, zoomed, 3);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>
    /// A square is still a PIXEL at any magnification: it grows with the art AND stays in phase with
    /// the art's corner. Size alone is half the rule — a lattice of the right size tiled from the
    /// wrong origin is the original defect with better arithmetic.
    /// </summary>
    [AvaloniaFact]
    public void The_lattice_stays_in_phase_with_the_art_after_a_zoom()
    {
        var (window, vm, view) = Render();
        try
        {
            vm.Zoom = 3;
            vm.PanBy(37, -19);            // off any round number, so a phase error cannot hide
            Dispatcher.UIThread.RunJobs();

            var brush = (DrawingBrush)Backdrop(view).Background!;
            double tile = brush.DestinationRect.Rect.Width;

            // The art's corner in the backdrop's coordinates, from the app's own geometry rather
            // than a second copy of it: the surface is the untransformed space the view maps in.
            var surface = view.GetVisualDescendants().OfType<Panel>().First(p => p.Name == "CanvasSurface");
            var bmp = (Bitmap)Art(view).Source!;
            double fit = Math.Min(surface.Bounds.Width / bmp.PixelSize.Width,
                                  surface.Bounds.Height / bmp.PixelSize.Height);
            double scale = fit * vm.Zoom;
            double offX = (surface.Bounds.Width - bmp.PixelSize.Width * scale) / 2 + vm.PanX;
            double offY = (surface.Bounds.Height - bmp.PixelSize.Height * scale) / 2 + vm.PanY;
            var corner = surface.TranslatePoint(new Point(offX, offY), Backdrop(view))!.Value;

            Assert.Equal(scale * vm.GridSize, tile / 2, 3);
            Assert.True(Phase(corner.X, brush.DestinationRect.Rect.X, tile) < 0.01, "out of phase horizontally");
            Assert.True(Phase(corner.Y, brush.DestinationRect.Rect.Y, tile) < 0.01, "out of phase vertically");
        }
        finally { window.Close(); vm.Dispose(); }
    }

    private static double Phase(double artEdge, double tileStart, double tile)
    {
        double d = (artEdge - tileStart) % tile;
        if (d < 0) d += tile;
        return Math.Min(d, tile - d);
    }

    /// <summary>
    /// WITH THE PREVIEW OVER THE PANE THERE IS NOTHING TO PAINT ON. The pointer talks to a panel that
    /// is still there when the art is hidden, so the guard has to be explicit — it used to be the
    /// image's own visibility, back when the image was what the pointer talked to.
    /// </summary>
    [AvaloniaFact]
    public void A_click_paints_nothing_while_the_preview_covers_the_canvas()
    {
        var (window, vm, view) = Render();
        try
        {
            vm.ActiveTool = EditorTool.Brush;
            vm.BrushValue = 255;
            Dispatcher.UIThread.RunJobs();
            var at = FittedMapper(window, Art(view));

            vm.FillPanePreviewCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(vm.ShowPaintCanvas);

            var before = Snapshot(vm, 8);
            window.MouseDown(at(3, 3), MouseButton.Left);
            window.MouseUp(at(3, 3), MouseButton.Left);
            window.MouseWheel(at(3, 3), new Vector(0, 1));
            Dispatcher.UIThread.RunJobs();

            Assert.Empty(Changed(vm, before));
            Assert.False(vm.IsZoomed);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>And so is the marquee: a selection made at one zoom is drawn around the same pixels
    /// at another. Measured off the overlay rather than trusted, because the overlay is the one
    /// reader of the geometry that is NOT transformed with the art.</summary>
    [AvaloniaFact]
    public void The_marquee_follows_the_zoom()
    {
        var (window, vm, view) = Render();
        try
        {
            vm.SelectAllCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            var overlay = view.GetVisualDescendants().OfType<Canvas>().First(c => c.Name == "CanvasOverlay");
            double fitted = overlay.Children.OfType<Rectangle>().Single().Width;

            vm.Zoom = 4;
            Dispatcher.UIThread.RunJobs();

            double zoomed = overlay.Children.OfType<Rectangle>().Single().Width;
            Assert.Equal(fitted * 4, zoomed, 3);

            // The dashes themselves must NOT scale — the overlay is drawn in control coordinates,
            // and transforming it would turn a hairline marquee into a ribbon.
            Assert.Equal(1, overlay.Children.OfType<Rectangle>().Single().StrokeThickness);
        }
        finally { window.Close(); vm.Dispose(); }
    }
}
