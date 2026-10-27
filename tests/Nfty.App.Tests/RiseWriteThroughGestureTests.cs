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
using Nfty.App.Rise;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The write-through Rise page driven through the REAL view: pointer events at the pixel the art is drawn
/// at, and real keys. No test here calls a command or a view-model method to stand in for a gesture
/// ("the command works" was never in doubt; a KeyBinding firing under a text field was).
/// </summary>
/// <remarks>The chest: oak everywhere (1 front, 9 back) under a 2x2 gold strap at (3,3)-(4,4) (2 front, 0 back).
/// Points come from a test-local fitted mapper, valid before any zoom.</remarks>
public class RiseWriteThroughGestureTests
{
    private const int W = 1180, H = 720;

    private static RisePage Open()
    {
        var book = RiseFixtures.ChestBook(oakDepth: RiseFixtures.Flat(1, 9), goldDepth: RiseFixtures.Flat(2, 0), withGhost: false);
        var seed = RiseFixtures.OakSeed(book, book.Recipes[0]);
        var (session, _) = RiseFixtures.OnDisk(book);
        var src = new RecipeRiseSource(session.Current!, session.Current!.Recipes[0], seed, new ImageBridge(), session, () => true);
        var vm = new RiseViewModel(src, new ImageBridge(), new FakeNav(), new FakeDialogs(), new FilePickerService(), new StatusService());
        return RisePages.Show(vm, W, H, owner: session);
    }

    private static RecipeRiseSource Src(RisePage page) => (RecipeRiseSource)page.Vm.Source;

    private static Image Art(RisePage page) =>
        page.View.GetVisualDescendants().OfType<Image>().First(i => i.Name == "CanvasImage");

    private static Func<int, int, Point> Mapper(RisePage page)
    {
        var art = Art(page);
        var origin = art.TranslatePoint(default, page.Window)!.Value;
        var bmp = (Bitmap)art.Source!;
        double scale = Math.Min(art.Bounds.Width / bmp.PixelSize.Width, art.Bounds.Height / bmp.PixelSize.Height);
        double offX = (art.Bounds.Width - bmp.PixelSize.Width * scale) / 2;
        double offY = (art.Bounds.Height - bmp.PixelSize.Height * scale) / 2;
        return (px, py) => new Point(origin.X + offX + (px + 0.5) * scale, origin.Y + offY + (py + 0.5) * scale);
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();

    private static void Click(RisePage page, Point at, MouseButton button, RawInputModifiers mods = RawInputModifiers.None)
    {
        page.Window.MouseDown(at, button, mods);
        page.Window.MouseUp(at, button, mods);
        Pump();
    }

    private static void Key(RisePage page, PhysicalKey key)
    {
        page.Window.KeyPressQwerty(key, RawInputModifiers.None);
        Pump();
    }

    private static void PickGold(RisePage page) => Click(page, Mapper(page)(3, 3), MouseButton.Left);

    [AvaloniaFact]
    public void The_picking_click_chooses_the_front_owner_and_paints_nothing()
    {
        using var page = Open();
        Assert.True(page.Vm.IsPicking);

        PickGold(page);

        Assert.Equal("Lock / Gold", Src(page).Target!.Name);
        Assert.Equal(0, page.Vm.DirtyCount);
        Assert.Equal(new DepthPixel(2, 0), Src(page).Target!.Surface.Get(3, 3));
        Assert.False(page.Vm.UndoCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void A_right_click_picks_the_back_owner()
    {
        using var page = Open();

        Click(page, Mapper(page)(3, 3), MouseButton.Right);     // gold is in front here, the oak owns the back

        Assert.Equal("Body / Oak", Src(page).Target!.Name);
        Assert.Equal(0, page.Vm.DirtyCount);
        Assert.Equal(new DepthPixel(1, 9), Src(page).Target!.Surface.Get(3, 3));
    }

    [AvaloniaFact]
    public void A_press_move_release_paints_the_dragged_pixels_of_the_target_only()
    {
        using var page = Open();
        var at = Mapper(page);
        PickGold(page);

        page.Window.MouseDown(at(3, 3), MouseButton.Left);
        page.Window.MouseMove(at(4, 3));
        page.Window.MouseUp(at(4, 3), MouseButton.Left);
        Pump();

        var gold = Src(page).Target!.Surface;
        Assert.Equal(new DepthPixel(3, 0), gold.Get(3, 3));
        Assert.Equal(new DepthPixel(3, 0), gold.Get(4, 3));
        Assert.Equal(new DepthPixel(2, 0), gold.Get(3, 4));
        Assert.Equal(1, page.Vm.DirtyCount);
    }

    [AvaloniaFact]
    public void A_press_on_a_dimmed_pixel_starts_nothing()
    {
        using var page = Open();
        var at = Mapper(page);
        PickGold(page);
        page.Vm.BrushSize = 3;                                   // would reach the strap from the oak pixel beside it

        Click(page, at(2, 3), MouseButton.Left);

        Assert.Equal(0, page.Vm.DirtyCount);
        Assert.Equal("Lock / Gold", Src(page).Target!.Name);
    }

    [AvaloniaFact]
    public void Shift_lowers_instead_of_raising()
    {
        using var page = Open();
        PickGold(page);

        Click(page, Mapper(page)(3, 3), MouseButton.Left, RawInputModifiers.Shift);

        Assert.Equal(new DepthPixel(1, 0), Src(page).Target!.Surface.Get(3, 3));
    }

    [AvaloniaFact]
    public void Ctrl_makes_the_drag_a_straight_line_from_where_it_began_to_where_it_ended()
    {
        using var page = Open();
        var at = Mapper(page);
        PickGold(page);

        // Wanders through (4,4) on the way to (4,3). Held Ctrl, only the straight line counts.
        page.Window.MouseDown(at(3, 3), MouseButton.Left, RawInputModifiers.Control);
        page.Window.MouseMove(at(4, 4), RawInputModifiers.Control);
        page.Window.MouseMove(at(4, 3), RawInputModifiers.Control);
        page.Window.MouseUp(at(4, 3), MouseButton.Left, RawInputModifiers.Control);
        Pump();

        var gold = Src(page).Target!.Surface;
        Assert.Equal(new DepthPixel(3, 0), gold.Get(3, 3));
        Assert.Equal(new DepthPixel(3, 0), gold.Get(4, 3));
        Assert.Equal(new DepthPixel(2, 0), gold.Get(4, 4));      // the wander left no mark
    }

    [AvaloniaFact]
    public void Enter_on_a_focused_layer_row_targets_that_layer()
    {
        using var page = Open();
        var row = page.View.GetVisualDescendants().OfType<Button>()
            .First(b => b.DataContext is RiseLayerRow { IngredientId: "lock" });
        Assert.Null(Src(page).Target);

        row.Focus();
        Key(page, PhysicalKey.Enter);

        Assert.Equal("Lock / Gold", Src(page).Target!.Name);
    }

    [AvaloniaFact]
    public void PageUp_and_PageDown_as_real_keys_step_the_target()
    {
        using var page = Open();
        page.View.Focus();

        Key(page, PhysicalKey.PageUp);
        Assert.Equal("Body / Oak", Src(page).Target!.Name);
        Key(page, PhysicalKey.PageUp);
        Assert.Equal("Lock / Gold", Src(page).Target!.Name);
        Key(page, PhysicalKey.PageDown);
        Assert.Equal("Body / Oak", Src(page).Target!.Name);
    }

    [AvaloniaFact]
    public void Escape_as_a_real_key_drops_the_marquee_then_the_target()
    {
        using var page = Open();
        PickGold(page);
        page.View.Focus();
        page.Vm.SelectAllCommand.Execute(null);

        Key(page, PhysicalKey.Escape);
        Assert.False(page.Vm.HasSelection);
        Assert.NotNull(Src(page).Target);

        Key(page, PhysicalKey.Escape);
        Assert.Null(Src(page).Target);
    }

    [AvaloniaFact]
    public void PageUp_PageDown_and_Escape_typed_in_a_number_field_change_nothing()
    {
        using var page = Open();
        PickGold(page);
        var field = page.View.GetVisualDescendants().OfType<TextBox>().First(t => t.IsEffectivelyVisible);
        field.Focus();
        Pump();
        Assert.True(field.IsFocused);

        Key(page, PhysicalKey.PageUp);
        Key(page, PhysicalKey.PageDown);
        Key(page, PhysicalKey.Escape);

        Assert.Equal("Lock / Gold", Src(page).Target!.Name);
    }
}
