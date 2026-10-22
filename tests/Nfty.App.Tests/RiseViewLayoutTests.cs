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
    public void The_swapped_tile_shows_the_whole_of_a_wide_picture_not_a_crop()
    {
        string[] wide = { "AAAAAAAA", "AAAAAAAA", "BBBBBBBB", "BBBBBBBB" };
        var ing = new Nfty.Core.Formats.LoadedIngredient
        {
            Manifest = new Nfty.Core.Model.IngredientManifest("band", "Band", Nfty.Core.Model.LayerKind.Custom, null,
                new[] { new Nfty.Core.Model.Variant("w", "Wide", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["w"] = RiseFixtures.Picture(wide) },
        };
        var recipe = new Nfty.Core.Formats.LoadedRecipe
        {
            Manifest = new Nfty.Core.Model.RecipeManifest("r", "R", new[] { "band" }, Array.Empty<Nfty.Core.Model.IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        var src = new Nfty.App.Rise.VariantRiseSource(recipe, ing, "w", new ImageBridge(), new CookBookSession(),
            looseSavePath: System.IO.Path.Combine(System.IO.Path.GetTempPath(), "band.igt"));
        using var page = RisePages.Show(RisePages.Vm(src), Min.Width, Min.Height);
        page.Vm.SwapPreviewCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var tile = page.View.FindControl<Border>("ModelTile")!;
        var image = tile.GetVisualDescendants().OfType<Avalonia.Controls.Image>().Single(i => i.IsVisible && ReferenceEquals(i.Source, page.Vm.Canvas));
        var host = (Control)image.GetVisualParent()!;
        // Fitted, the picture's whole width sits inside the tile and its height is half of it; cropped
        // to fill, it would be as tall as the tile and twice as wide, its ends cut off.
        Assert.True(image.Bounds.Width <= host.Bounds.Width + 0.5,
            $"the picture is {image.Bounds.Width:0.#} wide in a {host.Bounds.Width:0.#} tile");
        Assert.Equal(2.0, image.Bounds.Width / image.Bounds.Height, 2);
        ing.VariantImages["w"].Dispose();
    }

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

        model.RepaintNow();
        Assert.NotNull(model.Surface.Source);
        Assert.Equal(0, Says());

        page.Vm.SelectItemCommand.Execute(page.Vm.Items.First(i => i.Name == "Ghost"));
        Dispatcher.UIThread.RunJobs();
        model.RepaintNow();
        // The control itself clears its frame when the mesh goes null (ModelPreviewViewTests), so the
        // tile shows no model under the "2D only" ink - not the PREVIOUS item's.
        Assert.Null(model.Mesh);
        Assert.Null(model.Surface.Source);
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

    // ------------------------------------------------------------------ fix round 1

    private static Rect RectOf(Control c, Visual relativeTo) =>
        new(c.TranslatePoint(default, relativeTo)!.Value, c.Bounds.Size);

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Fit_never_puts_the_art_under_the_tile_or_the_grid_chip(bool minimum, bool enlarged)
    {
        double w = minimum ? Min.Width : 1180, h = minimum ? Min.Height : 720;
        using var page = RisePages.Show(RisePages.VariantVm(oakDepth: RisePages.PaintedOak()), w, h);
        if (enlarged) page.Vm.EnlargePreviewCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var backdrop = page.View.FindControl<Panel>("CanvasBackdrop")!;
        var host = RectOf(page.View.FindControl<Border>("CanvasHost")!, backdrop);
        var tile = RectOf(page.View.FindControl<Border>("ModelTile")!, backdrop);
        var chip = RectOf(page.View.FindControl<Border>("GridChip")!, backdrop);

        Assert.False(host.Intersects(tile), $"the canvas {host} runs under the 3D tile {tile}");
        Assert.False(host.Intersects(chip), $"the canvas {host} runs under the grid chip {chip}");
        Assert.True(host.Width >= 160 && host.Left >= 0 && host.Top >= 0
                    && host.Right <= backdrop.Bounds.Width && host.Bottom <= backdrop.Bounds.Height);
        // Where there is room it is still the centered 320 the editor draws.
        if (!minimum && !enlarged) Assert.Equal(320, host.Width);
    }

    [AvaloniaFact]
    public void The_art_is_mapped_through_the_host_it_was_moved_to()
    {
        using var page = RisePages.Show(RisePages.VariantVm(oakDepth: RisePages.PaintedOak()), Min.Width, Min.Height);
        var layer = page.View.FindControl<DepthLabelLayer>("DepthLabels")!;
        var surface = page.View.FindControl<Panel>("CanvasSurface")!;
        // The labels' frame is the one the pointer would use: its scale is the host's side over 8 pixels.
        Assert.Equal(surface.Bounds.Width / 8, layer.Frame.Scale, 0.01);
        Assert.True(layer.Frame.Scale >= DepthLabelLayer.MinCellPx);
    }

    [AvaloniaFact]
    public void The_paint_tools_are_disabled_where_nothing_can_be_painted_and_live_where_it_can()
    {
        Button Brush(RisePage p) => p.View.GetVisualDescendants().OfType<Button>()
            .First(b => b.Classes.Contains("ttool") && b.Classes.Contains("on"));

        using var variant = RisePages.Show(RisePages.VariantVm(), Min.Width, Min.Height);
        Assert.True(Brush(variant).IsEffectivelyEnabled);

        // A locked book still paints, as a scratch surface (the page says so in its footer).
        using var locked = RisePages.Show(RisePages.VariantVm(isEditing: () => false), Min.Width, Min.Height);
        Assert.True(Brush(locked).IsEffectivelyEnabled);

        variant.Vm.SelectItemCommand.Execute(variant.Vm.Items.First(i => i.Name == "Ghost"));
        Dispatcher.UIThread.RunJobs();
        Assert.False(Brush(variant).IsEffectivelyEnabled);          // partial alpha: nothing to paint

        using var recipe = RisePages.Show(RisePages.RecipeVm(), Min.Width, Min.Height);
        Assert.False(Brush(recipe).IsEffectivelyEnabled);
        var (setVm, owner) = RisePages.SetVm();
        using var set = RisePages.Show(setVm, Min.Width, Min.Height, owner: owner);
        Assert.False(Brush(set).IsEffectivelyEnabled);
        var size = set.View.GetVisualDescendants().OfType<NumericUpDown>().First(n => n.Classes.Contains("nin") && n.IsEffectivelyVisible);
        Assert.False(size.IsEffectivelyEnabled);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_rails_fold_never_cuts_a_section_in_half_at_any_window_height(bool exportTab)
    {
        // Every height from the smallest the app allows up: a fold that happens to land between two
        // sections at one height proves nothing about the next.
        for (double h = Min.Height; h <= Min.Height + 120; h += 9)
        {
            using var page = RisePages.Show(RisePages.VariantVm(oakDepth: RisePages.PaintedOak(), withPine: true), Min.Width, h);
            if (exportTab) page.Vm.ShowExportTabCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            var scroll = page.View.FindControl<ScrollViewer>("RailScroll")!;
            var stack = page.View.FindControl<StackPanel>(exportTab ? "ExportStack" : "DepthStack")!;
            double viewport = scroll.Bounds.Height;
            foreach (var section in stack.Children.Where(c => c.IsVisible && c.Bounds.Height > 0))
            {
                double top = section.TranslatePoint(default, scroll)!.Value.Y;
                double bottom = top + section.Bounds.Height;
                Assert.True(bottom <= viewport + 0.5 || top >= viewport - 0.5,
                    $"at height {h:0}: a section runs {top:0.#}..{bottom:0.#} across the fold at {viewport:0.#}");
            }
        }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_wheel_scrolls_each_rail_to_its_end_and_back_at_the_smallest_window(bool exportTab)
    {
        // The EXPORT rail overflows at the smallest window itself. The DEPTH rail fits there with this
        // fixture, so it is shown 120px shorter: the wheel has to reach the end of a rail that scrolls.
        using var page = RisePages.Show(RisePages.VariantVm(oakDepth: RisePages.PaintedOak(), withPine: true),
            Min.Width, exportTab ? Min.Height : Min.Height - 120);
        if (exportTab) page.Vm.ShowExportTabCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var scroll = page.View.FindControl<ScrollViewer>("RailScroll")!;
        double max = scroll.Extent.Height - scroll.Viewport.Height;
        Assert.True(max > 20, $"nothing to scroll ({scroll.Extent.Height:0} in {scroll.Viewport.Height:0}): the test proves nothing");

        // REAL wheel events at a point over the rail, never Offset assigned: a snap or a handler that
        // swallows the wheel only shows up through the pointer.
        var over = scroll.TranslatePoint(new Point(scroll.Bounds.Width / 2, scroll.Bounds.Height / 2), page.Window)!.Value;
        for (int i = 0; i < 40; i++) { page.Window.MouseWheel(over, new Vector(0, -1)); Dispatcher.UIThread.RunJobs(); }
        Assert.True(scroll.Offset.Y >= max - 1, $"the wheel stopped at {scroll.Offset.Y:0.#} of {max:0.#}: the rail's end is unreachable");

        for (int i = 0; i < 40; i++) { page.Window.MouseWheel(over, new Vector(0, 1)); Dispatcher.UIThread.RunJobs(); }
        Assert.Equal(0, scroll.Offset.Y, 0.5);
    }

    [AvaloniaFact]
    public void Pivot_is_fully_visible_on_the_export_tab_at_the_smallest_window()
    {
        using var page = RisePages.Show(RisePages.VariantVm(oakDepth: RisePages.PaintedOak(), withPine: true), Min.Width, Min.Height);
        page.Vm.ShowExportTabCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var scroll = page.View.FindControl<ScrollViewer>("RailScroll")!;
        var stack = page.View.FindControl<StackPanel>("ExportStack")!;
        var pivot = stack.Children.OfType<StackPanel>()
            .First(c => c.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "PIVOT"));
        var y = pivot.TranslatePoint(default, scroll)!.Value.Y;
        Assert.True(y + pivot.Bounds.Height <= scroll.Bounds.Height + 0.5, "PIVOT is not fully visible at the smallest window");
    }

    [AvaloniaFact]
    public void The_legend_and_its_tooltip_are_the_key_hints_not_literals()
    {
        using var page = RisePages.Show(RisePages.VariantVm(), Min.Width, Min.Height);
        var legend = page.View.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == KeyHints.RiseLegend);
        Assert.Contains(KeyHints.LowerDepth, legend.Text);
        var tip = Assert.IsType<string>(ToolTip.GetTip(legend));
        Assert.Equal(KeyHints.RiseLegendTip, tip);
        Assert.Contains("mouse button", tip);                          // the editor's wording for the mouse
        Assert.Contains(KeyHints.LowerDepth, tip);
        Assert.Contains(KeyHints.RiseConstrain, tip);
    }

    [AvaloniaFact]
    public void The_tile_caption_is_the_editors_caption_class()
    {
        using var page = RisePages.Show(RisePages.VariantVm(), Min.Width, Min.Height);
        var rise = page.View.FindControl<Border>("ModelTile")!.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => t.Text == "3D");

        var (book, recipe, ing) = VisualCapture.DynamicIngredient();
        var vm = new IngredientEditorViewModel(ing, recipe, book, new ImageBridge(), new FakeNav(),
            new CookBookSession(), new FakeDialogs(), new FilePickerService());
        var editorView = new IngredientEditorView { DataContext = vm };
        var window = new Window { Content = editorView, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var editor = editorView.FindControl<Border>("PreviewTile")!.GetVisualDescendants().OfType<TextBlock>()
                .First(t => t.Classes.Contains("tilecap"));
            Assert.Contains("tilecap", rise.Classes);
            Assert.Equal(editor.FontSize, rise.FontSize);
            Assert.Equal(editor.Margin, rise.Margin);
            Assert.Equal(7.5, rise.FontSize);
        }
        finally { window.Close(); }
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
