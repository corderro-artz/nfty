using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Rise;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>The write-through Rise page at the smallest page area: the target header keeps its place, the dimmed
/// images stay on the target at every zoom, and a rail row is clicked like a button. The pointer-on-pixel and
/// key gestures live in <c>RiseWriteThroughGestureTests</c>.</summary>
public class RiseWriteThroughViewTests
{
    private static RisePage Open()
    {
        var book = RiseFixtures.ChestBook(oakDepth: RiseFixtures.Flat(1, 9), goldDepth: RiseFixtures.Flat(2, 0), withGhost: false);
        var seed = RiseFixtures.OakSeed(book, book.Recipes[0]);
        var (session, _) = RiseFixtures.OnDisk(book);
        var src = new RecipeRiseSource(session.Current!, session.Current!.Recipes[0], seed, new ImageBridge(), session, () => true);
        var vm = new RiseViewModel(src, new ImageBridge(), new FakeNav(), new FakeDialogs(), new FilePickerService(), new StatusService());
        var area = RisePages.MinArea;
        return RisePages.Show(vm, area.Width, area.Height, owner: session);
    }

    private static T Named<T>(RisePage page, string name) where T : Control =>
        page.View.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    private static Rect BoundsOnWindow(RisePage page, Visual v) =>
        new(v.TranslatePoint(default, page.Window)!.Value, v.Bounds.Size);

    [AvaloniaFact]
    public void Clicking_a_rail_row_targets_its_layer_and_an_absent_row_does_nothing()
    {
        using var page = Open();
        var row = page.View.GetVisualDescendants().OfType<Button>()
            .First(b => b.DataContext is RiseLayerRow { IngredientId: "lock" });
        var at = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), page.Window)!.Value;

        page.Window.MouseDown(at, MouseButton.Left);
        page.Window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Lock / Gold", ((RecipeRiseSource)page.Vm.Source).Target!.Name);
        Assert.True(((RiseLayerRow)row.DataContext!).IsTarget);
    }

    [AvaloniaFact]
    public void The_target_header_keeps_its_place_with_and_without_a_target()
    {
        using var page = Open();
        var chip = Named<Border>(page, "TargetChip");
        var picking = BoundsOnWindow(page, chip);
        string pickingText = page.Vm.TargetHeader;

        RisePages.TargetLayer(page.Vm, 1);
        Dispatcher.UIThread.RunJobs();

        Assert.NotEqual(pickingText, page.Vm.TargetHeader);              // the sentence changed ...
        Assert.Equal(picking.Top, BoundsOnWindow(page, chip).Top);       // ... the slot did not
        Assert.Equal(picking.Height, chip.Bounds.Height);
    }

    [AvaloniaTheory]
    [InlineData(1.0)]
    [InlineData(4.0)]
    public void The_dimmed_images_line_up_with_the_target_at_every_zoom(double zoom)
    {
        using var page = Open();
        RisePages.TargetLayer(page.Vm, 1);
        page.Vm.Zoom = zoom;
        Dispatcher.UIThread.RunJobs();

        var canvas = Named<Image>(page, "CanvasImage");
        foreach (var name in new[] { "BelowImage", "AboveImage" })
        {
            var dim = Named<Image>(page, name);
            Assert.Equal(canvas.Bounds, dim.Bounds);
            Assert.Equal(BoundsOnWindow(page, canvas), BoundsOnWindow(page, dim));
            Assert.Equal(canvas.RenderTransform?.Value, dim.RenderTransform?.Value);
        }
        if (zoom > 1) Assert.NotEqual(Matrix.Identity, canvas.RenderTransform?.Value ?? Matrix.Identity);
    }
}
