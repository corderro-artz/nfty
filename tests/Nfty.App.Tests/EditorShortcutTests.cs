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
/// The canvas keyboard and the second mouse button: a letter per tool, the brackets for brush size,
/// Ctrl+A and Delete on the marquee, and right-drag to erase.
/// </summary>
/// <remarks>
/// Every one of these is driven from the WINDOW rather than by calling the command, because the
/// whole risk in a shortcut is the routing: a key that never reaches the handler, a handler that
/// swallows a key a text box wanted, a chord printed on a tooltip and bound nowhere. The commands
/// themselves are trivial.
/// </remarks>
public class EditorShortcutTests
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
        view.Focus();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, view);
    }

    private static Image Art(Visual view) =>
        view.GetVisualDescendants().OfType<Image>().First(i => i.Name == "CanvasImage");

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

    [AvaloniaTheory]
    [InlineData(PhysicalKey.B, EditorTool.Brush)]
    [InlineData(PhysicalKey.E, EditorTool.Eraser)]
    [InlineData(PhysicalKey.G, EditorTool.Fill)]
    [InlineData(PhysicalKey.R, EditorTool.Rectangle)]
    [InlineData(PhysicalKey.C, EditorTool.Circle)]
    [InlineData(PhysicalKey.T, EditorTool.Triangle)]
    [InlineData(PhysicalKey.L, EditorTool.Line)]
    [InlineData(PhysicalKey.M, EditorTool.Select)]
    public void A_letter_arms_its_tool(PhysicalKey key, EditorTool expected)
    {
        var (window, vm, _) = Render();
        try
        {
            vm.ActiveTool = expected == EditorTool.Brush ? EditorTool.Line : EditorTool.Brush;
            Dispatcher.UIThread.RunJobs();

            window.KeyPressQwerty(key, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(expected, vm.ActiveTool);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>
    /// THE GUARD THAT MAKES BARE LETTERS ADMISSIBLE. This screen has a name box and a weight box; a
    /// user typing "Brush" into the first would otherwise arm five tools on the way through.
    /// </summary>
    /// <remarks>
    /// Every visible box on the screen, the weight field's inner box included — a NumericUpDown puts
    /// focus on the TextBox in its template, so it is the same case rather than a separate one.
    /// Probed by deleting the guard: two keystrokes then arm the Line tool from inside the name box.
    /// </remarks>
    [AvaloniaFact]
    public void A_letter_typed_into_a_field_is_text_and_not_a_shortcut()
    {
        var (window, vm, view) = Render();
        try
        {
            vm.ActiveTool = EditorTool.Brush;
            Dispatcher.UIThread.RunJobs();

            var boxes = view.GetVisualDescendants().OfType<TextBox>().Where(b => b.IsEffectivelyVisible).ToArray();
            Assert.NotEmpty(boxes);

            foreach (var box in boxes)
            {
                box.Focus();
                Dispatcher.UIThread.RunJobs();
                window.KeyPressQwerty(PhysicalKey.L, RawInputModifiers.None);
                window.KeyPressQwerty(PhysicalKey.E, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(EditorTool.Brush, vm.ActiveTool);
            }
        }
        finally { window.Close(); vm.Dispose(); }
    }

    [AvaloniaFact]
    public void The_bracket_keys_step_the_brush_and_stop_at_its_limits()
    {
        var (window, vm, _) = Render();
        try
        {
            vm.BrushSize = 3;
            Dispatcher.UIThread.RunJobs();

            window.KeyPressQwerty(PhysicalKey.BracketRight, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(4, vm.BrushSize);

            window.KeyPressQwerty(PhysicalKey.BracketLeft, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.BracketLeft, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, vm.BrushSize);

            // The floor. A zero-pixel brush paints nothing and a negative one is not a size.
            for (int i = 0; i < 5; i++) window.KeyPressQwerty(PhysicalKey.BracketLeft, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, vm.BrushSize);

            // And the ceiling, which is the canvas: a stamp wider than the art paints the same
            // pixels as one exactly that wide.
            for (int i = 0; i < 40; i++) window.KeyPressQwerty(PhysicalKey.BracketRight, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(vm.BrushSizeMax, vm.BrushSize);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>The ceiling belongs to the PROPERTY, so the field cannot walk past it either.</summary>
    [AvaloniaFact]
    public void The_brush_size_field_cannot_be_typed_past_the_canvas()
    {
        var (window, vm, _) = Render();
        try
        {
            vm.BrushSize = 9999;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(vm.BrushSizeMax, vm.BrushSize);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    [AvaloniaFact]
    public void Ctrl_A_marks_the_whole_canvas_with_the_select_tool_armed()
    {
        var (window, vm, _) = Render();
        try
        {
            vm.ActiveTool = EditorTool.Brush;
            Dispatcher.UIThread.RunJobs();

            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Dispatcher.UIThread.RunJobs();

            // Select-all off the brush would mark a region and drop it in the same breath, because
            // leaving the Select tool clears the marquee.
            Assert.Equal(EditorTool.Select, vm.ActiveTool);
            var sel = Assert.NotNull(vm.Selection);
            Assert.Equal(0, sel.X);
            Assert.Equal(0, sel.Y);
            Assert.Equal(8, sel.Width);
            Assert.Equal(8, sel.Height);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    [AvaloniaTheory]
    [InlineData(PhysicalKey.Delete)]
    [InlineData(PhysicalKey.Backspace)]
    public void Delete_clears_the_marked_region_and_nothing_else(PhysicalKey key)
    {
        var (window, vm, view) = Render();
        try
        {
            // Paint the whole canvas, then mark a corner of it and clear that.
            vm.ActiveTool = EditorTool.Fill;
            vm.BrushValue = 255;
            vm.ApplyToolStroke(new[] { (0, 0) });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(255, vm.ValueAt(1, 1));
            Assert.Equal(255, vm.ValueAt(6, 6));

            vm.ActiveTool = EditorTool.Select;
            Dispatcher.UIThread.RunJobs();
            var at = Mapper(window, Art(view));
            window.MouseDown(at(0, 0), MouseButton.Left);
            window.MouseUp(at(2, 2), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(vm.Selection);

            window.KeyPressQwerty(key, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(0, vm.ValueAt(1, 1));      // inside the marquee
            Assert.Equal(255, vm.ValueAt(6, 6));    // outside it
            Assert.True(vm.UndoCommand.CanExecute(null));
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>Delete with nothing marked does nothing at all — and must not clear the canvas.</summary>
    [AvaloniaFact]
    public void Delete_with_no_marquee_is_not_a_way_to_erase_everything()
    {
        var (window, vm, _) = Render();
        try
        {
            vm.ActiveTool = EditorTool.Fill;
            vm.BrushValue = 255;
            vm.ApplyToolStroke(new[] { (0, 0) });
            Dispatcher.UIThread.RunJobs();
            Assert.Null(vm.Selection);

            window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(255, vm.ValueAt(4, 4));
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>
    /// The right button erases with whatever tool is armed, and does not disturb what is armed.
    /// </summary>
    /// <remarks>
    /// Flipping <c>ActiveTool</c> for the duration would have been the easy implementation and is
    /// wrong twice: the toolstrip's highlight moves under the hand, and leaving Select drops the
    /// marquee — so a right-click to tidy one pixel would silently discard a selection.
    /// </remarks>
    [AvaloniaFact]
    public void The_right_button_erases_without_changing_the_armed_tool()
    {
        var (window, vm, view) = Render();
        try
        {
            vm.ActiveTool = EditorTool.Fill;
            vm.BrushValue = 255;
            vm.ApplyToolStroke(new[] { (0, 0) });
            vm.ActiveTool = EditorTool.Line;
            vm.BrushSize = 1;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(255, vm.AlphaAt(1, 1));

            var at = Mapper(window, Art(view));
            window.MouseDown(at(1, 1), MouseButton.Right);
            window.MouseMove(at(5, 1));
            window.MouseUp(at(5, 1), MouseButton.Right);
            Dispatcher.UIThread.RunJobs();

            // ALPHA, not value: erasing clears what you can see and leaves the value byte alone.
            Assert.Equal(0, vm.AlphaAt(1, 1));      // erased along the drag
            Assert.Equal(0, vm.AlphaAt(5, 1));
            Assert.Equal(255, vm.AlphaAt(5, 5));    // and nowhere else
            Assert.Equal(EditorTool.Line, vm.ActiveTool);
        }
        finally { window.Close(); vm.Dispose(); }
    }
}
