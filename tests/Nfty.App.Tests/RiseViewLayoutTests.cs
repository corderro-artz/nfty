using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Controls;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.App.Views;
using Nfty.Core.Editing;
using SixLabors.ImageSharp;
using Point = Avalonia.Point;
using Size = Avalonia.Size;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The Rise page's layout, asserted from a laid-out frame at the area the smallest window really
/// gives a page. Never at the mockups' 1180: that is 130px wider than any page the app hosts, and a
/// layout test that states its own width tests a window the app does not have.
/// </summary>
public class RiseViewLayoutTests
{
    private static Size Min => RisePages.MinArea;

    private static IEnumerable<Border> Descendants(Visual v, string cls) =>
        v.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains(cls));

    private static Border PageFooter(RiseView view) =>
        Descendants(view, "modal-foot").OrderBy(b => b.TranslatePoint(default, view)!.Value.Y).Last();

    private static Border RailFoot(RiseView view) =>
        Descendants(view, "modal-foot").OrderBy(b => b.TranslatePoint(default, view)!.Value.X).First();

    private static Button ExportButton(RiseView view) =>
        view.GetVisualDescendants().OfType<Button>()
            .First(b => b.Classes.Contains("accent") && b.GetVisualDescendants().OfType<TextBlock>()
                .Any(t => t.Text is { } s && s.StartsWith("Export", StringComparison.Ordinal)));

    private static Button SaveButton(RiseView view) =>
        view.GetVisualDescendants().OfType<Button>()
            .First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Save depth"));

    private static Point At(Visual v, Visual relativeTo) => v.TranslatePoint(default, relativeTo)!.Value;

    private static NumericUpDown Voxel(RiseView view) =>
        view.GetVisualDescendants().OfType<NumericUpDown>().First(n => n.Classes.Contains("qnt"));

    // ------------------------------------------------------------------ 1. the three panes

    [AvaloniaFact]
    public void The_three_panes_measure_262_star_300_and_the_footer_is_one_band()
    {
        using var page = RisePages.Show(RisePages.VariantVm(), Min.Width, Min.Height);
        var panes = Descendants(page.View, "pane").Where(b => !b.Classes.Contains("pane-hrow"))
            .OrderBy(b => b.TranslatePoint(default, page.View)!.Value.X).ToList();

        Assert.Equal(3, panes.Count);
        Assert.Equal(262, panes[0].Bounds.Width, 0.5);
        Assert.Equal(300, panes[2].Bounds.Width, 0.5);
        Assert.Equal(Min.Width - 262 - 300, panes[1].Bounds.Width, 0.5);
        Assert.True(PageFooter(page.View).Bounds.Height <= 48,
            $"the footer is {PageFooter(page.View).Bounds.Height:0.#} tall");
    }

    // ------------------------------------------------------------------ 2 + 3. the swap keeps a way back, in a sized slot

    [AvaloniaFact]
    public void Swapping_moves_the_one_3d_view_between_its_homes_and_the_tile_stays_a_way_back()
    {
        using var page = RisePages.Show(RisePages.VariantVm(), Min.Width, Min.Height);
        var view = page.View;
        var model = view.FindControl<ModelPreviewView>("ModelView")!;
        var tileHost = view.FindControl<Border>("TileHost")!;
        var bigHost = view.FindControl<Border>("BigHost")!;
        var tile = view.FindControl<Border>("ModelTile")!;

        void AssertTileIsReachable()
        {
            Assert.True(tile.Bounds.Width > 0 && tile.Bounds.Height > 0, "the tile has no box");
            var center = tile.TranslatePoint(new Point(tile.Bounds.Width / 2, tile.Bounds.Height / 2), page.Window)!.Value;
            var hit = page.Window.GetVisualAt(center);
            Assert.NotNull(hit);
            Assert.True(ReferenceEquals(hit, tile) || tile.IsVisualAncestorOf(hit),
                "the tile cannot be reached by the pointer - there would be no way back");
        }

        Assert.Same(tileHost, model.Parent);
        Assert.Single(view.GetVisualDescendants().OfType<ModelPreviewView>());
        Assert.True(model.Bounds.Width >= 100, $"the 3D view is {model.Bounds.Width:0.#} wide in the tile");
        Assert.Equal("3D", page.Vm.TileCaption);
        AssertTileIsReachable();

        page.Vm.SwapPreviewCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(bigHost, model.Parent);
        Assert.Single(view.GetVisualDescendants().OfType<ModelPreviewView>());     // moved, never copied
        Assert.True(model.Bounds.Width >= 300, $"the 3D view is {model.Bounds.Width:0.#} wide when it fills the pane");
        Assert.True(model.Bounds.Height >= 300, $"the 3D view is {model.Bounds.Height:0.#} tall when it fills the pane");
        Assert.NotNull(model.Mesh);          // the bindings survived the move
        Assert.NotNull(model.Texture);
        Assert.Equal("CANVAS", page.Vm.TileCaption);
        Assert.Contains(tile.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "CANVAS");
        AssertTileIsReachable();

        page.Vm.SwapPreviewCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(tileHost, model.Parent);
        Assert.True(model.Bounds.Width >= 100);
    }

    // ------------------------------------------------------------------ 4. reserved geometry

    [AvaloniaFact]
    public void The_readout_row_is_the_same_height_with_and_without_a_hovered_pixel()
    {
        using var page = RisePages.Show(RisePages.VariantVm(), Min.Width, Min.Height);
        var readout = Descendants(page.View, "readout").Single();

        double without = readout.Bounds.Height;
        page.Vm.HoverPixel = (2, 2);
        Dispatcher.UIThread.RunJobs();
        Assert.NotEqual("", page.Vm.ReadoutText);          // it really had something to say
        Assert.Equal(without, readout.Bounds.Height);
        Assert.Equal(28, without);
    }

    [AvaloniaFact]
    public void Save_is_in_the_same_place_for_a_variant_and_a_recipe()
    {
        using var variant = RisePages.Show(RisePages.VariantVm(), Min.Width, Min.Height);
        using var recipe = RisePages.Show(RisePages.RecipeVm(), Min.Width, Min.Height);

        var a = SaveButton(variant.View);
        var b = SaveButton(recipe.View);
        Assert.Equal(a.Bounds.Size, b.Bounds.Size);
        Assert.Equal(At(a, variant.View), At(b, recipe.View));
        // And one of them is genuinely dark: the slot is reserved, not the button removed.
        Assert.True(a.GetVisualAncestors().OfType<Panel>().First(p => p.Classes.Contains("inkslot")).Opacity > 0.99);
        Assert.True(b.GetVisualAncestors().OfType<Panel>().First(p => p.Classes.Contains("inkslot")).Opacity < 0.01);
    }

    [AvaloniaFact]
    public void Ticking_a_second_model_moves_no_export_control()
    {
        using var page = RisePages.Show(RisePages.VariantVm(withPine: true), Min.Width, Min.Height);
        page.Vm.ShowExportTabCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(page.Vm.IsBatch);                      // the opening item alone is going

        var voxel = Voxel(page.View);
        var export = ExportButton(page.View);
        var shape = page.View.GetVisualDescendants().OfType<StackPanel>()
            .First(p => p.Classes.Contains("inkslot") && p.GetVisualChildren().OfType<TextBlock>().Any(t => t.Text == "SHAPE"));
        var before = (At(voxel, page.View), At(export, page.View), voxel.Bounds.Size, export.Bounds.Size);
        Assert.True(shape.Opacity < 0.01, "SHAPE is inked for a single model");

        page.Vm.Items.First(i => !i.IsChecked && i.IsRisableOrUnknown).IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(page.Vm.IsBatch);
        Assert.True(shape.Opacity > 0.99, "SHAPE is not inked for a batch");

        Assert.Equal(before.Item1, At(voxel, page.View));
        Assert.Equal(before.Item2, At(export, page.View));
        Assert.Equal(before.Item3, voxel.Bounds.Size);
        Assert.Equal(before.Item4, export.Bounds.Size);
    }

    // ------------------------------------------------------------------ 5. the rail foot

    [AvaloniaFact]
    public void The_rail_foot_is_47_for_every_source()
    {
        var (setVm, owner) = RisePages.SetVm();
        var pages = new[]
        {
            RisePages.Show(RisePages.VariantVm(), Min.Width, Min.Height),
            RisePages.Show(RisePages.RecipeVm(), Min.Width, Min.Height),
            RisePages.Show(setVm, Min.Width, Min.Height, owner: owner),
        };
        try
        {
            foreach (var p in pages)
            {
                var foot = RailFoot(p.View);
                Assert.Equal(47, foot.Bounds.Height);
                Assert.Equal(261, foot.Bounds.Width, 0.5);      // the pane's 262 less its own 1px rule
            }
        }
        finally { foreach (var p in pages) p.Dispose(); }
    }

    // ------------------------------------------------------------------ 6. compact numeric cells

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Every_compact_numeric_cell_is_26_tall_and_as_wide_as_it_says(bool exportTab)
    {
        using var page = RisePages.Show(RisePages.VariantVm(), Min.Width, Min.Height);
        if (exportTab) page.Vm.ShowExportTabCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var cells = page.View.GetVisualDescendants().OfType<NumericUpDown>()
            .Where(n => n.IsEffectivelyVisible && (n.Classes.Contains("nin") || n.Classes.Contains("qnt"))).ToList();
        Assert.True(cells.Count >= (exportTab ? 3 : 4), $"only found {cells.Count} numeric cells");

        double small = (double)Application.Current!.FindResource("FieldHeightSm")!;
        foreach (var n in cells)
        {
            Assert.Equal(small, n.Bounds.Height);
            if (!double.IsNaN(n.Width)) Assert.Equal(n.Width, n.Bounds.Width);
        }
    }

    // ------------------------------------------------------------------ 7. the toolstrip

    private static StripPanel Strip(RiseView view) => view.GetVisualDescendants().OfType<Border>()
        .First(b => b.Classes.Contains("pane-hrow") && b.Classes.Contains("toolstrip"))
        .GetVisualDescendants().OfType<StripPanel>().First();

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_toolstrip_is_one_line_at_the_window_the_app_opens_at(bool recipe)
    {
        double width = (1366 - 24) / ShellViewModel.BaseScale;
        using var page = RisePages.Show(recipe ? RisePages.RecipeVm() : RisePages.VariantVm(), width, 720);
        var lines = StripLines.Of(Strip(page.View));

        Assert.True(lines.Count == 1,
            $"the Rise toolstrip wrapped onto {lines.Count} lines at 1366 - its groups have outgrown the pane: "
            + string.Join(" | ", Strip(page.View).Children.OfType<Control>().Select(c => $"{c.GetType().Name} {c.Bounds.Width:0.#}")));
        Assert.True(lines[0].Count > 1, "the strip laid out nothing to measure");
        // Not merely one line: every control is inside the strip's content edge.
        var strip = Strip(page.View);
        foreach (var c in strip.Children.OfType<Control>().Where(c => c.IsVisible))
            Assert.True(c.Bounds.Right <= strip.Bounds.Width + 0.5, $"{c.GetType().Name} runs past the strip");
    }

    [AvaloniaFact]
    public void At_the_minimum_every_wrapped_line_carries_real_content()
    {
        using var page = RisePages.Show(RisePages.RecipeVm(), Min.Width, Min.Height);
        var panel = Strip(page.View);
        var lines = StripLines.Of(panel);
        if (lines.Count < 2) return;
        foreach (var line in lines)
            Assert.True(StripLines.Used(line) >= panel.Bounds.Width / 2,
                $"a wrapped line uses {StripLines.Used(line):0.#} of {panel.Bounds.Width:0.#}: a control is stranded");
    }

    // ------------------------------------------------------------------ 8. the cell labels

    private static Rgba32 PixelAt(Avalonia.Media.Imaging.WriteableBitmap frame, int x, int y)
    {
        using var fb = frame.Lock();
        var span = new byte[4];
        System.Runtime.InteropServices.Marshal.Copy(fb.Address + y * fb.RowBytes + x * 4, span, 0, 4);
        // The headless frame is BGRA or RGBA by platform; the test only compares darker/lighter, so
        // the channel order does not matter as long as it is read consistently.
        return new Rgba32(span[0], span[1], span[2], span[3]);
    }

    /// <summary>Sums of the channels of every pixel inside a cell of the labels' layer, in the frame.</summary>
    private static List<int> CellSums(RisePage page, DepthLabelLayer layer, int cx, int cy, Avalonia.Media.Imaging.WriteableBitmap frame)
    {
        var f = layer.Frame;
        var origin = layer.TranslatePoint(new Point(f.OffX + cx * f.Scale, f.OffY + cy * f.Scale), page.Window)!.Value;
        var sums = new List<int>();
        for (int dy = 4; dy < (int)f.Scale - 4; dy++)
            for (int dx = 4; dx < (int)f.Scale - 4; dx++)
            {
                var p = PixelAt(frame, (int)origin.X + dx, (int)origin.Y + dy);
                sums.Add(p.R + p.G + p.B);
            }
        return sums;
    }

    [AvaloniaFact]
    public void The_labels_are_drawn_in_ink_that_reads_on_the_pixel_under_them()
    {
        using var page = RisePages.Show(RisePages.VariantVm(oakDepth: RisePages.PaintedOak()), Min.Width, Min.Height);
        var layer = page.View.FindControl<DepthLabelLayer>("DepthLabels")!;
        Assert.True(layer.Frame.Scale >= DepthLabelLayer.MinCellPx, $"scale {layer.Frame.Scale:0.#}");

        using var frame = page.Window.CaptureRenderedFrame()!;
        Assert.Equal(page.Vm.Surface.SolidCount, layer.LastLabelCount);

        // Oak's top rows are light art (luma 133 > 125): dark ink, so some pixel is much DARKER than the art.
        // Its bottom rows are dark art (luma 67): light ink, so some pixel is much LIGHTER.
        var light = CellSums(page, layer, 2, 1, frame);      // top row, color A
        var dark = CellSums(page, layer, 2, 4, frame);       // lower row, color B
        int artLight = 178 + 122 + 70, artDark = 94 + 59 + 34;
        Assert.Contains(light, s => s < artLight - 120);
        Assert.DoesNotContain(light, s => s > artLight + 120);
        Assert.Contains(dark, s => s > artDark + 120);
        Assert.DoesNotContain(dark, s => s < artDark - 120);
    }

    [AvaloniaFact]
    public void A_big_canvas_at_fit_draws_no_labels_at_all()
    {
        // 512px: 262,144 cells, each 0.6 px at fit. The layer must refuse before it walks them.
        using var art = new Image<Rgba32>(512, 512, new Rgba32(200, 100, 50, 255));
        var surface = new DepthSurface(art, null);
        var layer = new DepthLabelLayer
        {
            Width = 320, Height = 320, Surface = surface,
            Frame = new CanvasFrame(320.0 / 512, 0, 0),
        };
        var window = new Window { Content = layer, Width = 340, Height = 340 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var clock = Stopwatch.StartNew();
        using var frame = window.CaptureRenderedFrame();
        clock.Stop();
        window.Close();

        Assert.Equal(0, layer.LastLabelCount);
        Assert.True(clock.ElapsedMilliseconds < 1500, $"rendering took {clock.ElapsedMilliseconds} ms");
    }

    [AvaloniaFact]
    public void A_deep_zoom_of_a_big_canvas_draws_only_the_cells_in_view()
    {
        using var art = new Image<Rgba32>(512, 512, new Rgba32(200, 100, 50, 255));
        var surface = new DepthSurface(art, null);
        // 16x the fitted scale: 10 px a cell, below the floor - so push past it: 20 px a cell.
        var layer = new DepthLabelLayer
        {
            Width = 320, Height = 320, Surface = surface,
            Frame = new CanvasFrame(20, -100 * 20, -100 * 20),     // the window sits over cells 100..116
        };
        var window = new Window { Content = layer, Width = 340, Height = 340 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        window.Close();

        Assert.InRange(layer.LastLabelCount, 200, 17 * 17);      // what 320px holds, never the whole picture
    }


    // ------------------------------------------------------------------ a picture that cannot rise

    [AvaloniaFact]
    public void A_picture_that_cannot_rise_blanks_the_3d_view_and_says_why_where_the_view_is()
    {
        using var page = RisePages.Show(RisePages.VariantVm(), Min.Width, Min.Height);
        var model = page.View.FindControl<ModelPreviewView>("ModelView")!;
        int Says() => page.View.GetVisualDescendants().OfType<TextBlock>()
            .Count(t => t.Text == "partial alpha cannot rise" && t.IsEffectivelyVisible);

        Assert.True(model.Opacity > 0.99);
        Assert.Equal(0, Says());

        page.Vm.SelectItemCommand.Execute(page.Vm.Items.First(i => i.Name == "Ghost"));
        Dispatcher.UIThread.RunJobs();
        // ModelPreviewView leaves its last frame standing when the mesh goes null, so the page blanks
        // it: otherwise the tile showed the PREVIOUS item's model under the "2D only" ink.
        Assert.Null(model.Mesh);
        Assert.True(model.Opacity < 0.01);
        Assert.False(model.IsHitTestVisible);
        Assert.Equal(1, Says());                                  // in the tile

        page.Vm.SwapPreviewCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, Says());                                  // in the big slot, and only there
        var tile = page.View.FindControl<Border>("ModelTile")!;
        Assert.DoesNotContain(tile.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text == "partial alpha cannot rise" && t.IsEffectivelyVisible);
    }

    // ------------------------------------------------------------------ what the rail binds follows the source

    [AvaloniaFact]
    public void A_reroll_replaces_the_layer_rows_and_the_seed_fact_on_the_page()
    {
        using var page = RisePages.Show(RisePages.RecipeVm(), Min.Width, Min.Height);
        var changed = new List<string>();
        page.Vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? "");
        var rowsBefore = page.Vm.LayerRows;
        Assert.Contains("seed 1", page.Vm.Facts);

        page.Vm.RerollCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(nameof(RiseViewModel.LayerRows), changed);
        Assert.Contains(nameof(RiseViewModel.Facts), changed);
        Assert.NotSame(rowsBefore, page.Vm.LayerRows);
        Assert.Contains("seed 2", page.Vm.Facts);
        // And it is what the screen shows, not only what the view model says.
        Assert.Contains(page.View.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "seed 2");
        Assert.Equal(2, page.View.GetVisualDescendants().OfType<Border>().Count(b => b.Classes.Contains("rrow")));
    }

    // ------------------------------------------------------------------ the rail virtualizes

    [AvaloniaFact]
    public async System.Threading.Tasks.Task A_row_nobody_sees_never_decodes_its_picture()
    {
        var (vm, owner) = RisePages.SetVm();
        using var page = RisePages.Show(vm, Min.Width, Min.Height, owner: owner);
        var src = (Nfty.App.Rise.SetRiseSource)vm.Source;

        // Decodes land off the UI thread and are posted back, so wait for them to land (a drain is not a wait).
        int Decoded() => vm.Items.Count(i => src.RowOf(i).IsThumbnailDecoded);
        for (int i = 0; i < 100 && Decoded() == 0; i++)
        {
            await System.Threading.Tasks.Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
        }
        await System.Threading.Tasks.Task.Delay(300);
        Dispatcher.UIThread.RunJobs();

        var realized = page.View.GetVisualDescendants().OfType<Border>().Count(b => b.Classes.Contains("tile"));
        Assert.True(Decoded() > 0, "nothing decoded at all");
        Assert.True(Decoded() <= realized,
            $"{Decoded()} pictures decoded for {realized} realized tiles: the rail reads thumbnails nobody can see");
        Assert.True(Decoded() < vm.Items.Count);
    }

    [AvaloniaFact]
    public void A_set_rail_realizes_only_the_rows_on_screen()
    {
        var (vm, owner) = RisePages.SetVm();
        using var page = RisePages.Show(vm, Min.Width, Min.Height, owner: owner);

        var list = page.View.GetVisualDescendants().OfType<ListBox>().Single(l => l.IsVisible);
        var tiles = list.GetVisualDescendants().OfType<Border>().Count(b => b.Classes.Contains("tile"));
        Assert.True(tiles > 0, "no tile was realized");
        Assert.True(tiles < vm.Items.Count, $"all {tiles} of {vm.Items.Count} tiles were realized: the rail does not virtualize");
        // Three to a row, so the realized rows hold a multiple of three (or the short last row).
        Assert.Equal(0, tiles % 3);
    }
}
