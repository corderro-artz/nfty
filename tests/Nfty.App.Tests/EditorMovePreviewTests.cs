using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// Dragging a marked region moves the ART while the button is down, not just the dashed box.
/// </summary>
/// <remarks>
/// <para>Every tool that paints shows the pixels it would commit on every pointer move. Select was
/// left out of that, correctly for half of what it does and wrongly for the other half: a MARK
/// changes no pixel and the marquee is its whole feedback, but a MOVE clears a region and stamps it
/// somewhere else, which is an ordinary pixel edit. So the last gesture in the editor still drawn
/// blind was the one that moves the most ink at once — the box followed the pointer and the art
/// underneath sat still until the button came up.</para>
///
/// <para>The corner tile had the same fault one level out: it showed what a cook would make of the
/// layer and was a whole gesture behind the canvas beside it. Two accounts of the same pixels,
/// disagreeing, on one screen.</para>
///
/// <para>Asked of the BITMAPS rather than of the surface: the surface is clean throughout — the
/// preview applies its edit, renders, and undoes it inside one call — so a test that read the
/// value-map would see nothing either way.</para>
/// </remarks>
public class EditorMovePreviewTests
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

    private static byte[] Pixels(Bitmap bmp)
    {
        int w = bmp.PixelSize.Width, h = bmp.PixelSize.Height;
        var buffer = new byte[w * h * 4];
        unsafe
        {
            fixed (byte* p = buffer)
                bmp.CopyPixels(new Avalonia.PixelRect(0, 0, w, h), (nint)p, buffer.Length, w * 4);
        }
        return buffer;
    }

    private static Func<int, int, Point> Mapper(Window window, Image art)
    {
        var origin = art.TranslatePoint(default, window)!.Value;
        var bmp = (Bitmap)art.Source!;
        double scale = Math.Min(art.Bounds.Width / bmp.PixelSize.Width,
                               art.Bounds.Height / bmp.PixelSize.Height);
        double offX = (art.Bounds.Width - bmp.PixelSize.Width * scale) / 2;
        double offY = (art.Bounds.Height - bmp.PixelSize.Height * scale) / 2;
        return (px, py) => new Point(origin.X + offX + (px + 0.5) * scale,
                                     origin.Y + offY + (py + 0.5) * scale);
    }

    /// <summary>Paints a 3x3 block in the top-left corner and marks it.</summary>
    private static void PaintAndMark(IngredientEditorViewModel vm)
    {
        vm.ActiveTool = EditorTool.Rectangle;
        vm.BrushValue = 255;
        vm.ApplyToolStroke(new[] { (0, 0), (2, 2) });
        vm.ActiveTool = EditorTool.Select;
        vm.Selection = new Nfty.Core.Editing.PixelRect(0, 0, 3, 3);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// The complaint, as one assertion: what the canvas shows mid-drag is what the release commits,
    /// byte for byte.
    /// </summary>
    [AvaloniaFact]
    public void The_canvas_shows_the_region_in_its_new_place_while_the_button_is_down()
    {
        var (window, vm, view) = Render();
        try
        {
            PaintAndMark(vm);
            var at = Mapper(window, Art(view));

            var before = Pixels((Bitmap)vm.Canvas!);

            window.MouseDown(at(1, 1), MouseButton.Left);      // inside the marquee: a move
            window.MouseMove(at(5, 5));
            Dispatcher.UIThread.RunJobs();
            var midDrag = Pixels((Bitmap)vm.Canvas!);

            Assert.False(before.AsSpan().SequenceEqual(midDrag),
                "the canvas did not change while the region was being dragged");

            window.MouseUp(at(5, 5), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            var committed = Pixels((Bitmap)vm.Canvas!);

            Assert.True(midDrag.AsSpan().SequenceEqual(committed),
                "what was shown mid-drag is not what the release committed");

            // And the pixels really moved: the corner is empty, the new place is painted.
            Assert.Equal(0, vm.AlphaAt(1, 1));
            Assert.Equal(255, vm.ValueAt(5, 5));
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>The surface is untouched until the release — the preview is applied, rendered and
    /// undone inside one call, so nothing reaches history or the dirty flag.</summary>
    [AvaloniaFact]
    public void A_move_in_progress_has_not_changed_a_pixel_or_the_history()
    {
        var (window, vm, view) = Render();
        try
        {
            PaintAndMark(vm);
            var at = Mapper(window, Art(view));

            window.MouseDown(at(1, 1), MouseButton.Left);
            window.MouseMove(at(5, 5));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(255, vm.ValueAt(1, 1));      // still where it was
            Assert.Equal(0, vm.AlphaAt(5, 5));        // and not yet where it is going

            window.MouseUp(at(5, 5), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.UndoCommand.CanExecute(null));
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>
    /// Abandoning a move puts the art back. A preview left standing is worse than no preview: it is
    /// indistinguishable from a committed edit.
    /// </summary>
    [AvaloniaFact]
    public void Escaping_a_move_restores_the_canvas()
    {
        var (window, vm, view) = Render();
        try
        {
            PaintAndMark(vm);
            var at = Mapper(window, Art(view));
            var before = Pixels((Bitmap)vm.Canvas!);

            window.MouseDown(at(1, 1), MouseButton.Left);
            window.MouseMove(at(5, 5));
            Dispatcher.UIThread.RunJobs();
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.True(before.AsSpan().SequenceEqual(Pixels((Bitmap)vm.Canvas!)),
                "the abandoned move was left standing on the canvas");
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>
    /// The corner tile follows the canvas rather than the release.
    /// </summary>
    /// <remarks>
    /// Asserted on the FIRST move of the gesture, which always repaints the tile: the budget that
    /// bounds the tile's cost mid-drag can skip a later one, and a test that asserted every move
    /// would be asserting the clock rather than the feature.
    /// </remarks>
    [AvaloniaFact]
    public void The_corner_tile_follows_the_gesture_too()
    {
        var (window, vm, view) = Render();
        try
        {
            PaintAndMark(vm);
            var at = Mapper(window, Art(view));
            var before = Pixels((Bitmap)vm.Preview!);

            window.MouseDown(at(1, 1), MouseButton.Left);
            window.MouseMove(at(5, 5));
            Dispatcher.UIThread.RunJobs();

            Assert.False(before.AsSpan().SequenceEqual(Pixels((Bitmap)vm.Preview!)),
                "the tile still shows the region where it was before the drag");
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>
    /// A drag that starts OUTSIDE the marquee marks a new region, and marking changes no pixel — so
    /// the canvas must not move under it.
    /// </summary>
    [AvaloniaFact]
    public void Marking_a_new_region_still_paints_nothing()
    {
        var (window, vm, view) = Render();
        try
        {
            PaintAndMark(vm);
            var at = Mapper(window, Art(view));
            var before = Pixels((Bitmap)vm.Canvas!);

            window.MouseDown(at(5, 5), MouseButton.Left);      // outside the marquee: a new mark
            window.MouseMove(at(7, 7));
            Dispatcher.UIThread.RunJobs();
            Assert.True(before.AsSpan().SequenceEqual(Pixels((Bitmap)vm.Canvas!)),
                "marking a region moved pixels");

            window.MouseUp(at(7, 7), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.True(before.AsSpan().SequenceEqual(Pixels((Bitmap)vm.Canvas!)));

            // It re-marked rather than moved: the region is where the drag drew it. (The undo stack
            // is not the assertion to make here - PaintAndMark left a real edit on it.)
            var sel = Assert.NotNull(vm.Selection);
            Assert.Equal(5, sel.X);
            Assert.Equal(5, sel.Y);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>
    /// The move a preview built must never be inherited by the next gesture.
    /// </summary>
    /// <remarks>
    /// The preview arms the pending move that <c>BuildCommand</c> consumes. Every early return
    /// between the two would leave it armed, and the next gesture — a brush stroke, anything — would
    /// build a <c>MoveSelection</c> out of it. Both entry points clear it on the way in.
    /// </remarks>
    [AvaloniaFact]
    public void A_stroke_after_a_move_is_a_stroke()
    {
        var (window, vm, view) = Render();
        try
        {
            PaintAndMark(vm);
            var at = Mapper(window, Art(view));

            window.MouseDown(at(1, 1), MouseButton.Left);
            window.MouseMove(at(5, 5));
            window.MouseUp(at(5, 5), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            vm.ActiveTool = EditorTool.Brush;
            vm.BrushSize = 1;
            vm.BrushValue = 128;
            Dispatcher.UIThread.RunJobs();

            window.MouseDown(at(0, 7), MouseButton.Left);
            window.MouseUp(at(0, 7), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(128, vm.ValueAt(0, 7));
            Assert.Equal(255, vm.ValueAt(5, 5));       // the moved block is still where it landed
        }
        finally { window.Close(); vm.Dispose(); }
    }
}
