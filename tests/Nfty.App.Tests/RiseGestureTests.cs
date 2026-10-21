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
using Nfty.App.ViewModels;
using Nfty.App.Views;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The Rise canvas, driven through the REAL control: pointer events go in at the pixel the picture is
/// drawn at, and every assertion reads the depth the surface ended up with. No test calls a command
/// to stand in for a gesture, because "the command works" is the half that was never in doubt.
/// </summary>
/// <remarks>
/// <para>Points come from a test-local fitted mapper (the art's bounds and bitmap size), not from
/// <c>CanvasMapping</c>: a mapping bug the view shares with its own test would otherwise cancel out.
/// Only valid before a zoom, which none of these tests use.</para>
/// <para>The mouse map under test: left raises the FRONT, right raises the BACK, Shift lowers, Ctrl
/// constrains. Shift is not the constrain key here, so a test that held Shift expecting a straight
/// line would be asserting the editor's rule on a page that does not have it.</para>
/// </remarks>
public class RiseGestureTests
{
    private const int W = 1180, H = 720;

    private static Image Art(RisePage page) =>
        page.View.GetVisualDescendants().OfType<Image>().First(i => i.Name == "CanvasImage");

    private static System.Func<int, int, Point> Mapper(RisePage page)
    {
        var art = Art(page);
        var origin = art.TranslatePoint(default, page.Window)!.Value;
        var bmp = (Bitmap)art.Source!;
        double scale = System.Math.Min(art.Bounds.Width / bmp.PixelSize.Width,
                                       art.Bounds.Height / bmp.PixelSize.Height);
        double offX = (art.Bounds.Width - bmp.PixelSize.Width * scale) / 2;
        double offY = (art.Bounds.Height - bmp.PixelSize.Height * scale) / 2;
        return (px, py) => new Point(origin.X + offX + (px + 0.5) * scale,
                                     origin.Y + offY + (py + 0.5) * scale);
    }

    private static RisePage Flat() => RisePages.Show(RisePages.VariantVm(), W, H);

    private static RisePage Painted() => RisePages.Show(RisePages.VariantVm(oakDepth: RisePages.PaintedOak()), W, H);

    private static (int Front, int Back) At(RisePage page, int x, int y)
    {
        var p = page.Vm.Surface.Get(x, y);
        return (p.Front, p.Back);
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();

    private static void Click(RisePage page, Point at, MouseButton button, RawInputModifiers mods = RawInputModifiers.None)
    {
        page.Window.MouseDown(at, button, mods);
        page.Window.MouseUp(at, button, mods);
        Pump();
    }

    [AvaloniaFact]
    public void A_left_click_raises_the_front_by_one()
    {
        using var page = Flat();
        var at = Mapper(page);

        Click(page, at(3, 2), MouseButton.Left);

        Assert.Equal((1, 0), At(page, 3, 2));
        Assert.Equal((0, 0), At(page, 4, 2));         // only the pixel clicked
    }

    [AvaloniaFact]
    public void A_right_click_raises_the_back_by_one()
    {
        using var page = Flat();
        var at = Mapper(page);

        Click(page, at(3, 2), MouseButton.Right);

        Assert.Equal((0, 1), At(page, 3, 2));
    }

    [AvaloniaFact]
    public void Shift_left_click_lowers_the_front_and_floors_at_zero()
    {
        using var page = Painted();          // top half: front 1
        var at = Mapper(page);

        Click(page, at(2, 2), MouseButton.Left, RawInputModifiers.Shift);
        Assert.Equal(0, At(page, 2, 2).Front);

        Click(page, at(2, 2), MouseButton.Left, RawInputModifiers.Shift);
        Assert.Equal(0, At(page, 2, 2).Front);        // the floor; and the other side is untouched
        Assert.Equal(0, At(page, 2, 2).Back);
    }

    [AvaloniaFact]
    public void Shift_right_click_lowers_the_back()
    {
        using var page = Painted();          // right half: back 1
        var at = Mapper(page);

        Click(page, at(5, 2), MouseButton.Right, RawInputModifiers.Shift);

        Assert.Equal((1, 0), At(page, 5, 2));         // front kept, back lowered
    }

    [AvaloniaFact]
    public void Dragging_back_and_forth_raises_each_pixel_once()
    {
        using var page = Flat();
        var at = Mapper(page);

        page.Window.MouseDown(at(1, 2), MouseButton.Left);
        for (int pass = 0; pass < 3; pass++)
        {
            page.Window.MouseMove(at(6, 2));
            page.Window.MouseMove(at(1, 2));
        }
        page.Window.MouseMove(at(6, 2));
        Pump();
        page.Window.MouseUp(at(6, 2), MouseButton.Left);
        Pump();

        for (int x = 1; x <= 6; x++) Assert.Equal((1, 0), At(page, x, 2));
        Assert.Equal((0, 0), At(page, 3, 3));
        Assert.Equal((0, 0), At(page, 3, 1));
    }

    [AvaloniaFact]
    public void Ctrl_constrains_a_line_to_forty_five_degrees()
    {
        using var page = Flat();
        var at = Mapper(page);
        page.Vm.ActiveTool = EditorTool.Line;
        Pump();

        // (1,2) -> (6,3) is closer to horizontal than to the diagonal: held with Ctrl it is row 2 only.
        page.Window.MouseDown(at(1, 2), MouseButton.Left, RawInputModifiers.Control);
        page.Window.MouseMove(at(6, 3), RawInputModifiers.Control);
        page.Window.MouseUp(at(6, 3), MouseButton.Left, RawInputModifiers.Control);
        Pump();

        for (int x = 1; x <= 6; x++) Assert.Equal(1, At(page, x, 2).Front);
        for (int x = 1; x <= 6; x++) Assert.Equal(0, At(page, x, 3).Front);
    }

    [AvaloniaFact]
    public void Shift_does_not_constrain_a_line_because_it_lowers()
    {
        using var page = Painted();
        var at = Mapper(page);
        page.Vm.ActiveTool = EditorTool.Line;
        Pump();

        // Unconstrained, the line from (1,2) to (6,3) crosses into row 3; Shift has a different job.
        page.Window.MouseDown(at(1, 2), MouseButton.Left, RawInputModifiers.Shift);
        page.Window.MouseMove(at(6, 3), RawInputModifiers.Shift);
        page.Window.MouseUp(at(6, 3), MouseButton.Left, RawInputModifiers.Shift);
        Pump();

        Assert.Equal(0, At(page, 6, 3).Front);        // row 3 is lowered (1 -> 0): it was reached
    }

    [AvaloniaFact]
    public void A_click_on_a_transparent_pixel_changes_nothing()
    {
        using var page = Flat();
        var at = Mapper(page);

        Click(page, at(0, 0), MouseButton.Left);      // the oak's corner is clear

        Assert.Equal((0, 0), At(page, 0, 0));
        Assert.False(page.Vm.UndoCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void Losing_capture_mid_drag_leaves_the_depth_untouched()
    {
        using var page = Flat();
        var at = Mapper(page);

        IPointer? pointer = null;
        page.Window.AddHandler(InputElement.PointerPressedEvent,
            (object? _, PointerPressedEventArgs e) => pointer = e.Pointer,
            RoutingStrategies.Tunnel, handledEventsToo: true);

        page.Window.MouseDown(at(1, 2), MouseButton.Left);
        page.Window.MouseMove(at(6, 2));
        Pump();
        Assert.Equal(1, At(page, 4, 2).Front);        // the preview is standing

        Assert.NotNull(pointer);
        pointer!.Capture(null);
        Pump();

        Assert.Equal((0, 0), At(page, 4, 2));
        Assert.False(page.Vm.UndoCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void Escape_mid_drag_abandons_the_stroke()
    {
        using var page = Flat();
        var at = Mapper(page);

        page.Window.MouseDown(at(1, 2), MouseButton.Left);
        page.Window.MouseMove(at(6, 2));
        Pump();
        page.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Pump();
        page.Window.MouseUp(at(6, 2), MouseButton.Left);
        Pump();

        Assert.Equal((0, 0), At(page, 4, 2));
    }

    [AvaloniaFact]
    public void Keys_arm_the_tools_but_not_while_typing_in_a_field()
    {
        using var page = Flat();
        Assert.Equal(EditorTool.Brush, page.Vm.ActiveTool);

        page.Window.KeyPressQwerty(PhysicalKey.L, RawInputModifiers.None);
        Pump();
        Assert.Equal(EditorTool.Line, page.Vm.ActiveTool);

        page.Window.KeyPressQwerty(PhysicalKey.B, RawInputModifiers.None);
        Pump();
        Assert.Equal(EditorTool.Brush, page.Vm.ActiveTool);

        // Every visible box on the page: typing into a field is text, never a shortcut.
        var boxes = page.View.GetVisualDescendants().OfType<TextBox>().Where(b => b.IsEffectivelyVisible).ToArray();
        Assert.NotEmpty(boxes);
        foreach (var box in boxes)
        {
            box.Focus();
            Pump();
            page.Window.KeyPressQwerty(PhysicalKey.L, RawInputModifiers.None);
            Pump();
            Assert.Equal(EditorTool.Brush, page.Vm.ActiveTool);
        }
    }

    [AvaloniaFact]
    public void Ctrl_Z_undoes_the_last_stroke_and_Ctrl_Y_puts_it_back()
    {
        using var page = Flat();
        var at = Mapper(page);
        Click(page, at(3, 2), MouseButton.Left);
        Assert.Equal(1, At(page, 3, 2).Front);

        page.Window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
        Pump();
        Assert.Equal(0, At(page, 3, 2).Front);

        page.Window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.Control);
        Pump();
        Assert.Equal(1, At(page, 3, 2).Front);
    }

    [AvaloniaFact]
    public void Hovering_a_solid_pixel_fills_the_readout_and_leaving_blanks_it()
    {
        using var page = Painted();
        var at = Mapper(page);

        page.Window.MouseMove(at(5, 2));
        Pump();
        Assert.Equal("front 1 · back 1 · total 3", page.Vm.ReadoutText);

        page.Window.MouseMove(at(0, 0));              // clear paper: the line is reserved, empty
        Pump();
        Assert.Equal("", page.Vm.ReadoutText);

        page.Window.MouseMove(at(5, 2));
        Pump();
        Assert.NotEqual("", page.Vm.ReadoutText);
        page.Window.MouseMove(new Point(5, 5));       // off the canvas entirely
        Pump();
        Assert.Null(page.Vm.HoverPixel);
        Assert.Equal("", page.Vm.ReadoutText);
    }

    [AvaloniaFact]
    public void A_recipe_source_ignores_clicks()
    {
        using var page = RisePages.Show(RisePages.RecipeVm(), W, H);
        var at = Mapper(page);
        var before = At(page, 3, 2);

        Click(page, at(3, 2), MouseButton.Left);
        Click(page, at(3, 2), MouseButton.Right);

        Assert.Equal(before, At(page, 3, 2));
        Assert.False(page.Vm.UndoCommand.CanExecute(null));
        // A refused stroke raises no GestureEnded, so the 3D budget must not be left switched on.
        Assert.False(page.View.GetVisualDescendants().OfType<ModelPreviewView>().First().HostInteracting);
    }

    [AvaloniaFact]
    public void Strokes_do_nothing_while_the_3d_view_fills_the_pane()
    {
        using var page = Flat();
        var at = Mapper(page);
        var target = at(3, 2);                         // read while the canvas is what is drawn
        page.Vm.SwapPreviewCommand.Execute(null);
        Pump();
        Assert.False(page.Vm.ShowPaintCanvas);

        Click(page, target, MouseButton.Left);

        Assert.Equal((0, 0), At(page, 3, 2));
        Assert.False(page.Vm.UndoCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void The_3d_frame_repaints_exactly_when_the_stroke_ends()
    {
        using var page = Flat();
        var at = Mapper(page);
        var model = page.View.GetVisualDescendants().OfType<ModelPreviewView>().First();

        page.Window.MouseDown(at(1, 2), MouseButton.Left);
        page.Window.MouseMove(at(6, 2));
        Pump();
        Assert.True(model.HostInteracting);            // the budget is on for the length of the stroke

        int before = model.RepaintCount;
        page.Window.MouseUp(at(6, 2), MouseButton.Left);
        Pump();

        Assert.False(model.HostInteracting);
        Assert.True(model.RepaintCount > before, "the frame was left to the budget instead of brought up to date");
        Assert.False(model.RepaintPending);
    }
}
