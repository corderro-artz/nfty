using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The canvas shows a gesture WHILE it is being made, as the pixels it will commit.
/// </summary>
/// <remarks>
/// Reported from the running app: dragging a line shows nothing until the release. The overlay did
/// draw a 1px accent hairline the whole time, which is exactly why no test caught it - something
/// was on screen. It was the wrong something: the shape tools FILL and a line commits at the
/// brush's size in the brush's ink, so the outline disagreed with the result in weight, in color
/// and (for the shapes) in whether the middle is painted at all - and the brush and eraser had no
/// band at all. So these tests never ask whether the overlay has children. They ask what the CANVAS
/// shows, and compare it against what the release produces.
/// </remarks>
public class EditorLivePreviewTests
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

    private static Image Art(Visual view) =>
        view.GetVisualDescendants().OfType<Image>().First(i => i.Name == "CanvasImage");

    /// <summary>The canvas bitmap's pixels, so a test can ask what is on SCREEN rather than what the
    /// surface holds - the two differ by exactly the preview, which is the point.</summary>
    private static byte[] Shown(IngredientEditorViewModel vm)
    {
        var bmp = (Bitmap)vm.Canvas!;
        int w = bmp.PixelSize.Width, h = bmp.PixelSize.Height;
        var buffer = new byte[w * h * 4];
        unsafe
        {
            fixed (byte* p = buffer)
                bmp.CopyPixels(new Avalonia.PixelRect(0, 0, w, h), (nint)p, buffer.Length, w * 4);
        }
        return buffer;
    }

    /// <summary>A pointer position over one canvas pixel's center, in window coordinates.</summary>
    private static System.Func<int, int, Point> Mapper(Window window, Image art)
    {
        var origin = art.TranslatePoint(default, window)!.Value;
        var bmp = (Bitmap)art.Source!;
        double scale = System.Math.Min(art.Bounds.Width / bmp.PixelSize.Width,
                                       art.Bounds.Height / bmp.PixelSize.Height);
        double offX = (art.Bounds.Width - bmp.PixelSize.Width * scale) / 2;
        double offY = (art.Bounds.Height - bmp.PixelSize.Height * scale) / 2;
        return (px, py) => new Point(origin.X + offX + (px + 0.5) * scale,
                                     origin.Y + offY + (py + 0.5) * scale);
    }

    /// <summary>
    /// The whole complaint, as one assertion: what the canvas shows mid-drag is what the release
    /// commits, byte for byte. Comparing the two renders rather than naming a pixel value is what
    /// makes it a statement about the feature - the preview is the same edit, not a lookalike.
    /// </summary>
    [AvaloniaFact]
    public void A_line_is_on_the_canvas_while_it_is_being_dragged()
    {
        var (window, vm, view) = Render();
        try
        {
            vm.ActiveTool = EditorTool.Line;
            vm.BrushValue = 255;
            Dispatcher.UIThread.RunJobs();

            var at = Mapper(window, Art(view));
            var before = Shown(vm);

            window.MouseDown(at(1, 1), MouseButton.Left);
            window.MouseMove(at(6, 6));
            Dispatcher.UIThread.RunJobs();

            var during = Shown(vm);
            Assert.NotEqual(before, during);        // the pixels are there before the release

            window.MouseUp(at(6, 6), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(during, Shown(vm));        // and they are exactly the ones that committed
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>
    /// A preview is a preview: it is applied, rendered and undone inside one call, so mid-drag the
    /// surface still holds what it held, nothing is dirty and there is nothing to undo. Without
    /// this, the cheap implementation - paint on every move and let history sort it out - would pass
    /// the test above while filling the undo stack with one stroke per pointer sample.
    /// </summary>
    [AvaloniaFact]
    public void A_preview_touches_no_pixel_and_no_history()
    {
        var (window, vm, view) = Render();
        try
        {
            vm.ActiveTool = EditorTool.Line;
            vm.BrushValue = 255;
            Dispatcher.UIThread.RunJobs();

            var at = Mapper(window, Art(view));
            byte was = vm.ValueAt(3, 3);

            window.MouseDown(at(1, 1), MouseButton.Left);
            window.MouseMove(at(6, 6));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(was, vm.ValueAt(3, 3));
            Assert.False(vm.IsDirty);
            Assert.False(vm.UndoCommand.CanExecute(null));

            window.MouseUp(at(6, 6), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(255, vm.ValueAt(3, 3));    // (3,3) is on the diagonal the line walks
            Assert.True(vm.IsDirty);
            Assert.True(vm.UndoCommand.CanExecute(null));
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>
    /// The brush and eraser had no band at all, so this is the tool the report did not name and the
    /// one that was drawn most blind.
    /// </summary>
    [AvaloniaFact]
    public void A_brush_stroke_is_on_the_canvas_while_it_is_being_dragged()
    {
        var (window, vm, view) = Render();
        try
        {
            vm.ActiveTool = EditorTool.Brush;
            vm.BrushValue = 255;
            Dispatcher.UIThread.RunJobs();

            var at = Mapper(window, Art(view));
            var before = Shown(vm);

            window.MouseDown(at(1, 6), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            var pressed = Shown(vm);
            Assert.NotEqual(before, pressed);       // the press already stamps its disc

            window.MouseMove(at(6, 1));
            Dispatcher.UIThread.RunJobs();

            // And the stroke FOLLOWS. Previewing only on press would satisfy the line above and
            // still leave the drag itself invisible, which is the actual complaint.
            Assert.NotEqual(pressed, Shown(vm));
            Assert.False(vm.IsDirty);               // still only a preview

            window.MouseUp(at(6, 1), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.IsDirty);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>
    /// A gesture that is abandoned rather than released - capture taken away by a deactivating
    /// window - must not leave its preview standing. On screen that ghost is indistinguishable from
    /// a committed stroke, which is worse than the blindness it replaced.
    /// </summary>
    [AvaloniaFact]
    public void An_abandoned_gesture_takes_its_preview_with_it()
    {
        var (window, vm, view) = Render();
        try
        {
            vm.ActiveTool = EditorTool.Rectangle;
            vm.BrushValue = 255;
            Dispatcher.UIThread.RunJobs();

            var at = Mapper(window, Art(view));
            var before = Shown(vm);

            // The real pointer, taken on the way DOWN (tunnel, so before the view's own handler
            // captures it). IPointer cannot be implemented outside Avalonia, and releasing the real
            // capture is what the app will actually experience anyway.
            IPointer? pointer = null;
            window.AddHandler(InputElement.PointerPressedEvent,
                (object? _, PointerPressedEventArgs e) => pointer = e.Pointer,
                RoutingStrategies.Tunnel, handledEventsToo: true);

            window.MouseDown(at(1, 1), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            var pressed = Shown(vm);

            window.MouseMove(at(6, 6));
            Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(before, Shown(vm));
            Assert.NotEqual(pressed, Shown(vm));    // the 6x6 rectangle, not the press point

            // Capture taken away: the release the view is waiting for is never coming.
            Assert.NotNull(pointer);
            pointer!.Capture(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(before, Shown(vm));
            Assert.False(vm.IsDirty);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>
    /// Shift while dragging snaps a line to 45 degrees, all the way through the real control: the
    /// modifier is read off the pointer event, the preview and the commit both go through the same
    /// constrained path, and the pixels that land are the snapped ones.
    /// </summary>
    /// <remarks>
    /// StrokeConstraintTests owns the arithmetic. This owns the wiring, which is the half that can
    /// be right in a pure function and never reach a pixel - the editor's chance field shipped
    /// exactly that way.
    /// </remarks>
    [AvaloniaFact]
    public void Shift_snaps_a_dragged_line_to_45_degrees()
    {
        var (window, vm, view) = Render();
        try
        {
            vm.ActiveTool = EditorTool.Line;
            vm.BrushValue = 255;
            vm.BrushSize = 1;
            Dispatcher.UIThread.RunJobs();

            var at = Mapper(window, Art(view));

            // Dragged 5 across and 1 down: unconstrained that ends at (6,2), snapped it is flat.
            window.MouseDown(at(1, 1), MouseButton.Left, RawInputModifiers.Shift);
            window.MouseMove(at(6, 2), RawInputModifiers.Shift);
            Dispatcher.UIThread.RunJobs();
            var during = Shown(vm);

            window.MouseUp(at(6, 2), MouseButton.Left, RawInputModifiers.Shift);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(255, vm.ValueAt(6, 1));    // the snapped end
            Assert.NotEqual(255, vm.ValueAt(6, 2)); // where the pointer actually was
            Assert.Equal(during, Shown(vm));        // and the preview said so before the release
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>
    /// Ctrl is accepted for constrain as well. It is not the convention - Shift is - but it is what
    /// a good many people reach for, and refusing the key teaches nothing.
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_constrains_as_well_as_shift()
    {
        var (window, vm, view) = Render();
        try
        {
            vm.ActiveTool = EditorTool.Rectangle;
            vm.BrushValue = 255;
            Dispatcher.UIThread.RunJobs();

            var at = Mapper(window, Art(view));
            window.MouseDown(at(1, 1), MouseButton.Left, RawInputModifiers.Control);
            window.MouseMove(at(6, 3), RawInputModifiers.Control);
            window.MouseUp(at(6, 3), MouseButton.Left, RawInputModifiers.Control);
            Dispatcher.UIThread.RunJobs();

            // A square off the longer side: 1..6 on both axes, not 1..3 vertically.
            Assert.Equal(255, vm.ValueAt(6, 6));
            Assert.Equal(255, vm.ValueAt(1, 6));
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>
    /// Ctrl+Z and Ctrl+Y, which the quick-reference sheet has been printing while nothing bound
    /// them. Driven from the window, because a KeyBinding that never fires is exactly the failure
    /// being fixed.
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_Z_undoes_and_Ctrl_Y_redoes()
    {
        var (window, vm, view) = Render();
        try
        {
            vm.ActiveTool = EditorTool.Line;
            vm.BrushValue = 255;
            Dispatcher.UIThread.RunJobs();

            var at = Mapper(window, Art(view));
            var before = Shown(vm);

            window.MouseDown(at(1, 1), MouseButton.Left);
            window.MouseUp(at(6, 6), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            var drawn = Shown(vm);
            Assert.NotEqual(before, drawn);

            view.Focus();
            window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(before, Shown(vm));

            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.Control);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(drawn, Shown(vm));
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>Escape mid-drag abandons the stroke rather than dropping the marquee: two meanings
    /// on one key, told apart by whether a gesture is in progress.</summary>
    [AvaloniaFact]
    public void Escape_mid_drag_abandons_the_stroke()
    {
        var (window, vm, view) = Render();
        try
        {
            vm.ActiveTool = EditorTool.Line;
            vm.BrushValue = 255;
            Dispatcher.UIThread.RunJobs();

            var at = Mapper(window, Art(view));
            var before = Shown(vm);

            view.Focus();
            window.MouseDown(at(1, 1), MouseButton.Left);
            window.MouseMove(at(6, 6));
            Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(before, Shown(vm));

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(before, Shown(vm));

            // And the release that follows commits nothing, because there is no gesture any more.
            window.MouseUp(at(6, 6), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(before, Shown(vm));
            Assert.False(vm.IsDirty);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>With no gesture in progress Escape means what it always meant: drop the marquee.
    /// Asserted because the gesture handler above sits in front of that KeyBinding and takes handled
    /// events - a handler that swallowed Escape unconditionally would leave a selection nothing can
    /// dismiss, which is the mode the KeyBinding exists to prevent.</summary>
    [AvaloniaFact]
    public void Escape_with_no_gesture_still_drops_the_marquee()
    {
        var (window, vm, view) = Render();
        try
        {
            vm.ActiveTool = EditorTool.Select;
            Dispatcher.UIThread.RunJobs();

            var at = Mapper(window, Art(view));
            view.Focus();
            window.MouseDown(at(1, 1), MouseButton.Left);
            window.MouseUp(at(5, 5), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(vm.Selection);

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Null(vm.Selection);
        }
        finally { window.Close(); vm.Dispose(); }
    }
}
