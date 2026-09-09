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
}
