using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using Nfty.App.Rise;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;
using Xunit;

namespace Nfty.App.Tests;

public class RiseViewModelTests
{
    private static RiseViewModel Vm(RiseSource src) =>
        new(src, new ImageBridge(), new FakeNav(), new FakeDialogs(), new FilePickerService(), new StatusService());

    private static RiseViewModel OakVm(string variant = "oak", bool editing = true)
    {
        var (s, _) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        return Vm(RiseFixtures.Variant(s, variant, editing));
    }

    [AvaloniaFact]
    public void A_refused_stroke_still_raises_GestureEnded()
    {
        using var recipe = RisePages.RecipeVm();
        int ended = 0;
        recipe.GestureEnded += () => ended++;
        recipe.ApplyDepthStroke(new[] { (2, 2) }, DepthSide.Front, lower: false);   // read-only source
        Assert.Equal(1, ended);

        using var oak = OakVm();
        oak.GestureEnded += () => ended++;
        oak.ApplyDepthStroke(System.Array.Empty<(int x, int y)>(), DepthSide.Front, lower: false);   // empty path
        Assert.Equal(2, ended);

        oak.ApplyDepthStroke(new[] { (2, 2) }, DepthSide.Front, lower: false);      // accepted: once, not twice
        Assert.Equal(3, ended);
    }

    [AvaloniaFact]
    public void A_click_raises_the_front_by_one_and_is_one_undo_step()
    {
        using var vm = OakVm();
        vm.ApplyDepthStroke(new[] { (2, 1) }, DepthSide.Front, lower: false);
        Assert.Equal(new DepthPixel(1, 0), vm.Surface.Get(2, 1));
        Assert.True(vm.UndoCommand.CanExecute(null));
        vm.UndoCommand.Execute(null);
        Assert.Equal(new DepthPixel(0, 0), vm.Surface.Get(2, 1));
        Assert.False(vm.UndoCommand.CanExecute(null));
        Assert.True(vm.RedoCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void A_long_stroke_raises_every_pixel_it_crosses_exactly_once()
    {
        using var vm = OakVm();
        var path = new[] { (1, 2), (6, 2), (1, 2), (6, 2) };
        for (int i = 1; i <= path.Length; i++) vm.PreviewDepthStroke(path[..i], DepthSide.Back, false);
        vm.ApplyDepthStroke(path, DepthSide.Back, false);
        Assert.All(Enumerable.Range(1, 6), x => Assert.Equal(new DepthPixel(0, 1), vm.Surface.Get(x, 2)));
        vm.UndoCommand.Execute(null);
        Assert.All(Enumerable.Range(1, 6), x => Assert.Equal(new DepthPixel(0, 0), vm.Surface.Get(x, 2)));
        Assert.False(vm.UndoCommand.CanExecute(null));    // the whole stroke was ONE step
    }

    [AvaloniaFact]
    public void Shift_lowers_and_floors_at_zero()
    {
        using var vm = OakVm();
        new DepthFillAll(2, 0).Apply(vm.Surface);
        for (int i = 0; i < 3; i++) vm.ApplyDepthStroke(new[] { (2, 2) }, DepthSide.Front, lower: true);
        Assert.Equal(0, vm.Surface.Get(2, 2).Front);
        vm.UndoCommand.Execute(null);                      // undoes the 1 -> 0 step
        Assert.Equal(1, vm.Surface.Get(2, 2).Front);
        vm.UndoCommand.Execute(null);                      // undoes the 2 -> 1 step
        Assert.Equal(2, vm.Surface.Get(2, 2).Front);
        Assert.False(vm.UndoCommand.CanExecute(null));     // the third, floored click made no step
    }

    [AvaloniaFact]
    public void A_cancelled_gesture_leaves_the_depth_exactly_as_it_was()
    {
        using var vm = OakVm();
        vm.PreviewDepthStroke(new[] { (2, 2), (5, 2) }, DepthSide.Front, false);
        Assert.Equal(1, vm.Surface.Get(3, 2).Front);          // the standing preview IS on the surface
        vm.CancelDepthPreview();
        Assert.Null(vm.Surface.ToDepthMap());
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void A_preview_replaces_the_previous_one_instead_of_stacking()
    {
        using var vm = OakVm();
        vm.PreviewDepthStroke(new[] { (2, 2), (5, 2) }, DepthSide.Front, false);
        vm.PreviewDepthStroke(new[] { (2, 2), (3, 2) }, DepthSide.Front, false);
        Assert.Equal(1, vm.Surface.Get(3, 2).Front);
        Assert.Equal(0, vm.Surface.Get(5, 2).Front);
    }

    [AvaloniaFact]
    public void Select_marks_then_a_click_inside_applies_to_every_solid_pixel_once()
    {
        using var vm = OakVm();
        vm.ActiveTool = EditorTool.Select;
        vm.ApplyDepthStroke(new[] { (1, 1), (3, 3) }, DepthSide.Front, false);
        Assert.Equal(new PixelRect(1, 1, 3, 3), vm.Selection);
        Assert.Null(vm.Surface.ToDepthMap());              // marking changes no depth
        Assert.False(vm.UndoCommand.CanExecute(null));

        vm.ApplyDepthStroke(new[] { (2, 2) }, DepthSide.Front, false);
        int raised = 0;
        for (int y = 1; y <= 3; y++)
            for (int x = 1; x <= 3; x++)
                if (vm.Surface.Get(x, y).Front == 1) raised++;
        Assert.Equal(8, raised);
        Assert.Equal(0, vm.Surface.Get(1, 1).Front);       // clear pixel: no body, no depth
        Assert.Equal(0, vm.Surface.Get(4, 2).Front);       // outside the mark
        vm.UndoCommand.Execute(null);
        Assert.Null(vm.Surface.ToDepthMap());
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void Same_colour_fill_raises_one_region()
    {
        using var vm = OakVm();
        vm.ActiveTool = EditorTool.Fill;
        vm.PreviewDepthStroke(new[] { (3, 2) }, DepthSide.Front, false);   // commit-only: no preview
        Assert.Null(vm.Surface.ToDepthMap());
        vm.ApplyDepthStroke(new[] { (3, 2) }, DepthSide.Front, false);
        Assert.Equal(1, vm.Surface.Get(3, 1).Front);        // 'A' region
        Assert.Equal(1, vm.Surface.Get(6, 2).Front);
        Assert.Equal(0, vm.Surface.Get(3, 5).Front);        // 'B' region untouched
    }

    [AvaloniaFact]
    public void The_line_tool_raises_the_pixels_between_the_ends_only()
    {
        using var vm = OakVm();
        vm.ActiveTool = EditorTool.Line;
        vm.ApplyDepthStroke(new[] { (1, 2), (3, 3), (6, 2) }, DepthSide.Front, false);   // middle point is ignored
        Assert.All(Enumerable.Range(1, 6), x => Assert.Equal(1, vm.Surface.Get(x, 2).Front));
        Assert.Equal(0, vm.Surface.Get(3, 3).Front);
    }

    [AvaloniaFact]
    public void The_rectangle_tool_raises_the_box_between_the_ends()
    {
        using var vm = OakVm();
        vm.ActiveTool = EditorTool.Rectangle;
        vm.ApplyDepthStroke(new[] { (2, 2), (3, 3) }, DepthSide.Back, false);
        Assert.Equal(1, vm.Surface.Get(2, 3).Back);
        Assert.Equal(1, vm.Surface.Get(3, 2).Back);
        Assert.Equal(0, vm.Surface.Get(4, 2).Back);
    }

    [AvaloniaFact]
    public void Every_depth_change_rebuilds_the_mesh_and_the_thickness_reaches_it()
    {
        using var vm = OakVm();
        var flatDepth = vm.Mesh!.Max.Z - vm.Mesh.Min.Z;
        vm.ApplyDepthStroke(new[] { (2, 2) }, DepthSide.Front, false);
        Assert.True(vm.Mesh!.Max.Z - vm.Mesh.Min.Z > flatDepth);
        vm.UndoCommand.Execute(null);
        Assert.Equal(flatDepth, vm.Mesh!.Max.Z - vm.Mesh.Min.Z);
    }

    [AvaloniaFact]
    public void A_cancelled_preview_puts_the_mesh_back_too()
    {
        using var vm = OakVm();
        var flatDepth = vm.Mesh!.Max.Z - vm.Mesh.Min.Z;
        vm.PreviewDepthStroke(new[] { (2, 2) }, DepthSide.Front, false);    // first move always rebuilds
        Assert.True(vm.Mesh!.Max.Z - vm.Mesh.Min.Z > flatDepth);
        vm.CancelDepthPreview();
        Assert.Equal(flatDepth, vm.Mesh!.Max.Z - vm.Mesh.Min.Z);
    }

    [AvaloniaFact]
    public void A_two_d_only_item_cannot_be_painted_and_has_no_mesh()
    {
        using var vm = OakVm("ghost");
        Assert.False(vm.IsPaintable);
        Assert.Null(vm.Mesh);
        Assert.True(vm.ShowTwoDOnly);
        vm.ApplyDepthStroke(new[] { (2, 2) }, DepthSide.Front, false);
        Assert.False(vm.UndoCommand.CanExecute(null));
        Assert.Null(vm.Surface.ToDepthMap());
        // marking still works: it changes no depth
        vm.ActiveTool = EditorTool.Select;
        vm.ApplyDepthStroke(new[] { (1, 1), (3, 3) }, DepthSide.Front, false);
        Assert.NotNull(vm.Selection);
    }

    [AvaloniaFact]
    public void A_locked_variant_still_paints_as_a_scratch_surface()
    {
        using var vm = OakVm(editing: false);
        Assert.True(vm.IsPaintable);
        vm.ApplyDepthStroke(new[] { (2, 2) }, DepthSide.Front, false);
        Assert.Equal(1, vm.Surface.Get(2, 2).Front);
    }

    [AvaloniaFact]
    public void A_recipe_source_ignores_strokes_and_offers_the_die()
    {
        var (s, _) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        var book = s.Current!;
        var recipe = book.Recipes.Single();
        using var vm = Vm(new RecipeRiseSource(book, recipe, 3, new ImageBridge()));
        Assert.True(vm.HasDie);
        Assert.True(vm.IsRecipeRail);
        Assert.False(vm.IsPaintable);
        vm.ApplyDepthStroke(new[] { (3, 3) }, DepthSide.Front, false);
        Assert.Null(vm.Surface.ToDepthMap());

        var before = vm.Subject;
        for (int i = 0; i < 12; i++)
        {
            int face = vm.DieFace;
            Assert.True(vm.RerollCommand.CanExecute(null));
            vm.RerollCommand.Execute(null);
            Assert.NotEqual(face, vm.DieFace);
            Assert.InRange(vm.DieFace, 1, 6);
        }
        Assert.NotSame(before, vm.Subject);
        Assert.Same(vm.SelectedItem.Subject!.Depth, vm.Surface);   // re-read after Changed, not held
    }

    [AvaloniaFact]
    public void A_reroll_rebuilds_the_canvas_the_mesh_and_reframes_once()
    {
        var (s, _) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        using var vm = Vm(new RecipeRiseSource(s.Current!, s.Current!.Recipes.Single(), 3, new ImageBridge()));
        var canvas = vm.Canvas;
        var mesh = vm.Mesh;
        int reframed = 0, depth = 0;
        vm.SubjectChanged += () => reframed++;
        vm.DepthChanged += () => depth++;
        vm.RerollCommand.Execute(null);
        Assert.NotSame(canvas, vm.Canvas);
        Assert.NotSame(mesh, vm.Mesh);
        Assert.Equal(1, reframed);
        Assert.True(depth >= 1);       // the label overlay repaints for the new picture
    }

    [AvaloniaFact]
    public void A_reroll_refreshes_the_note_and_the_thumbnails()
    {
        var (s, _) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        using var vm = Vm(new RecipeRiseSource(s.Current!, s.Current!.Recipes.Single(), 3, new ImageBridge()));
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        var itemChanged = new List<string?>();
        vm.SelectedItem.PropertyChanged += (_, e) => itemChanged.Add(e.PropertyName);
        vm.RerollCommand.Execute(null);
        Assert.Contains(nameof(RiseViewModel.DepthNote), changed);
        Assert.Contains(nameof(RiseItem.Thumbnail), itemChanged);
    }

    [AvaloniaFact]
    public void A_non_recipe_source_has_no_die()
    {
        using var vm = OakVm();
        Assert.False(vm.HasDie);
        Assert.False(vm.RerollCommand.CanExecute(null));
        Assert.True(vm.IsVariantRail);
        Assert.False(vm.IsTileRail);
    }

    [AvaloniaFact]
    public void The_readout_names_the_pixel_under_the_pointer_and_is_blank_elsewhere()
    {
        using var vm = OakVm();
        vm.ApplyDepthStroke(new[] { (2, 1) }, DepthSide.Front, false);
        vm.HoverPixel = (2, 1);
        Assert.Equal("front 1 · back 0 · total 2", vm.ReadoutText);
        vm.HoverPixel = (0, 0);
        Assert.Equal("", vm.ReadoutText);
        vm.HoverPixel = null;
        Assert.Equal("", vm.ReadoutText);
        Assert.Equal("8 × 8 · 32 solid", vm.DimensionsText);
    }

    [AvaloniaFact]
    public void The_readout_follows_a_gesture_that_changes_the_pixel_it_names()
    {
        using var vm = OakVm();
        vm.HoverPixel = (2, 1);
        var seen = new List<string?>();
        vm.PropertyChanged += (_, e) => seen.Add(e.PropertyName);
        vm.PreviewDepthStroke(new[] { (2, 1) }, DepthSide.Back, false);
        Assert.Equal("front 0 · back 1 · total 2", vm.ReadoutText);
        Assert.Contains(nameof(RiseViewModel.ReadoutText), seen);
    }

    [AvaloniaFact]
    public void Changing_items_cancels_the_preview_drops_the_marquee_and_reframes()
    {
        using var vm = OakVm();
        var oakSurface = vm.Surface;
        var ghost = vm.Items.Single(i => i.Id == "ghost");
        vm.PreviewDepthStroke(new[] { (2, 2), (5, 2) }, DepthSide.Front, false);
        vm.Selection = new PixelRect(1, 1, 2, 2);
        vm.Zoom = 4;
        int reframed = 0;
        vm.SubjectChanged += () => reframed++;
        vm.SelectItemCommand.Execute(ghost);
        Assert.Null(oakSurface.ToDepthMap());               // no standing preview left behind
        Assert.Null(vm.Selection);
        Assert.Equal(1, reframed);
        Assert.Same(ghost, vm.SelectedItem);
        Assert.True(ghost.IsSelected);
        Assert.False(vm.Items.Single(i => i.Id == "oak").IsSelected);
        Assert.Equal(CanvasMappingZoom.Fit, vm.Zoom);       // the new picture opens fitted
        Assert.Null(vm.Mesh);
    }

    [AvaloniaFact]
    public void Swapping_keeps_both_halves_on_screen_and_names_the_other()
    {
        using var vm = OakVm();
        Assert.Equal("3D", vm.TileCaption);
        Assert.True(vm.ShowPaintCanvas);
        vm.SwapPreviewCommand.Execute(null);
        Assert.Equal("CANVAS", vm.TileCaption);
        Assert.False(vm.ShowPaintCanvas);
        Assert.Equal(120, vm.TileSize);
        vm.EnlargePreviewCommand.Execute(null);
        Assert.Equal(208, vm.TileSize);
        vm.EnlargePreviewCommand.Execute(null);
        Assert.Equal(120, vm.TileSize);
    }

    [AvaloniaFact]
    public void Only_the_five_rise_tools_can_be_armed()
    {
        using var vm = OakVm();
        vm.SelectToolCommand.Execute(EditorTool.Eraser);
        Assert.Equal(EditorTool.Brush, vm.ActiveTool);
        vm.SelectToolCommand.Execute(EditorTool.Circle);
        Assert.Equal(EditorTool.Brush, vm.ActiveTool);
        vm.SelectToolCommand.Execute(EditorTool.Line);
        Assert.Equal(EditorTool.Line, vm.ActiveTool);
        Assert.True(vm.IsToolLine);
        Assert.False(vm.IsToolBrush);

        vm.SelectToolCommand.Execute(EditorTool.Select);
        vm.Selection = new PixelRect(1, 1, 2, 2);
        vm.SelectToolCommand.Execute(EditorTool.Brush);
        Assert.Null(vm.Selection);
    }

    [AvaloniaFact]
    public void Select_all_arms_select_and_marks_the_whole_picture()
    {
        using var vm = OakVm();
        vm.SelectAllCommand.Execute(null);
        Assert.Equal(EditorTool.Select, vm.ActiveTool);
        Assert.Equal(new PixelRect(0, 0, 8, 8), vm.Selection);
    }

    [AvaloniaFact]
    public void Zoom_and_brush_and_grid_are_clamped_at_the_property()
    {
        using var vm = OakVm();
        vm.Zoom = 100;
        Assert.Equal(RiseViewModel.ZoomMax, vm.Zoom);
        vm.Zoom = 0.1;
        Assert.Equal(RiseViewModel.ZoomMin, vm.Zoom);
        vm.Zoom = double.NaN;
        Assert.Equal(RiseViewModel.ZoomMin, vm.Zoom);
        vm.BrushSize = 99;
        Assert.Equal(8, vm.BrushSize);
        vm.BrushSize = 0;
        Assert.Equal(1, vm.BrushSize);
        vm.GridSize = 99;
        Assert.Equal(8, vm.GridSize);
    }

    [AvaloniaFact]
    public void Panning_is_a_no_op_while_fitted_and_zoom_and_pan_raise_the_backdrop_event_without_dirtying()
    {
        using var vm = OakVm();
        int raised = 0;
        vm.BackdropChanged += () => raised++;
        vm.PanBy(10, 10);
        Assert.Equal(0, vm.PanX);
        Assert.Equal(0, raised);
        vm.ZoomInCommand.Execute(null);
        Assert.True(vm.IsZoomed);
        vm.PanBy(10, 0);
        Assert.Equal(10, vm.PanX);
        vm.ZoomToFitCommand.Execute(null);
        Assert.Equal(0, vm.PanX);
        Assert.True(raised >= 3);
        vm.ToggleGridCommand.Execute(null);
        Assert.False(vm.ShowPixelGrid);
        Assert.False(vm.Source.IsDirty);
    }

    [AvaloniaFact]
    public void Dispose_cancels_a_standing_preview_and_is_idempotent()
    {
        var (s, _) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        var vm = Vm(RiseFixtures.Variant(s));
        var surface = vm.Surface;
        vm.PreviewDepthStroke(new[] { (2, 2) }, DepthSide.Front, false);
        vm.Dispose();
        vm.Dispose();
        Assert.Null(surface.ToDepthMap());
    }

    [AvaloniaFact]
    public void The_mesh_throttle_counts_from_the_end_of_a_rebuild_not_its_start()
    {
        using var vm = OakVm();
        long t = 1000;
        int rebuilds = 0;
        vm.NowMs = () => t;
        vm.MeshRebuilt = () => { rebuilds++; t += 50; };       // each rebuild costs more than the budget
        vm.PreviewDepthStroke(new[] { (2, 2), (3, 2) }, DepthSide.Front, false);
        Assert.Equal(1, rebuilds);                             // first move of a gesture always rebuilds
        vm.PreviewDepthStroke(new[] { (2, 2), (4, 2) }, DepthSide.Front, false);
        Assert.Equal(1, rebuilds);                             // the rebuild just finished: not due again
        t += RiseViewModel.MeshBudgetMs;
        vm.PreviewDepthStroke(new[] { (2, 2), (5, 2) }, DepthSide.Front, false);
        Assert.Equal(2, rebuilds);
        Assert.Equal(1, vm.Surface.Get(5, 2).Front);           // the labels were never throttled
    }

    [AvaloniaFact]
    public void A_preview_on_a_page_that_cannot_paint_leaves_the_surface_untouched()
    {
        var (s, _) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        using var recipe = Vm(new RecipeRiseSource(s.Current!, s.Current!.Recipes.Single(), 3, new ImageBridge()));
        recipe.PreviewDepthStroke(new[] { (3, 3), (4, 3) }, DepthSide.Front, false);
        Assert.Null(recipe.Surface.ToDepthMap());

        using var ghost = OakVm("ghost");
        ghost.PreviewDepthStroke(new[] { (2, 2), (4, 2) }, DepthSide.Front, false);
        Assert.Null(ghost.Surface.ToDepthMap());
    }

    [AvaloniaFact]
    public void A_select_drag_with_no_mark_previews_nothing_and_does_not_throw()
    {
        using var vm = OakVm();
        vm.ActiveTool = EditorTool.Select;
        vm.PreviewDepthStroke(new[] { (1, 1), (3, 3) }, DepthSide.Front, false);
        Assert.Null(vm.Surface.ToDepthMap());
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void A_click_that_changes_nothing_pushes_no_history_and_rebuilds_nothing()
    {
        using var vm = OakVm();
        int rebuilds = 0, depth = 0;
        vm.MeshRebuilt = () => rebuilds++;
        vm.DepthChanged += () => depth++;
        vm.ApplyDepthStroke(new[] { (2, 2) }, DepthSide.Front, lower: true);   // paper: floored at 0
        vm.ApplyDepthStroke(new[] { (0, 0) }, DepthSide.Front, lower: false);  // clear pixel
        Assert.Equal(0, rebuilds);
        Assert.Equal(0, depth);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void A_click_that_ends_a_preview_that_changed_things_does_rebuild()
    {
        using var vm = OakVm();
        vm.PreviewDepthStroke(new[] { (2, 2) }, DepthSide.Front, false);
        int rebuilds = 0;
        vm.MeshRebuilt = () => rebuilds++;
        vm.ApplyDepthStroke(new[] { (2, 2) }, DepthSide.Front, lower: true);   // undoes preview, then floors: net no change
        Assert.Equal(1, rebuilds);
        Assert.Equal(0, vm.Surface.Get(2, 2).Front);
    }

    [AvaloniaFact]
    public void Undo_and_redo_bodies_refuse_on_a_page_that_cannot_paint()
    {
        var (s, _) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        using var vm = Vm(new RecipeRiseSource(s.Current!, s.Current!.Recipes.Single(), 3, new ImageBridge()));
        var subject = vm.Subject;
        subject.History.Do(new DepthStep(new[] { (3, 3) }, DepthSide.Front, 1), subject.Depth);
        Assert.Equal(1, vm.Surface.Get(3, 3).Front);
        vm.UndoCommand.Execute(null);                  // Execute does not consult CanExecute
        Assert.Equal(1, vm.Surface.Get(3, 3).Front);
        subject.History.Undo(subject.Depth);
        vm.RedoCommand.Execute(null);
        Assert.Equal(0, vm.Surface.Get(3, 3).Front);
    }

    [AvaloniaFact]
    public void A_marquee_is_clamped_to_the_art()
    {
        using var vm = OakVm();
        vm.ActiveTool = EditorTool.Select;
        vm.ApplyDepthStroke(new[] { (5, 5), (20, 20) }, DepthSide.Front, false);
        Assert.Equal(new PixelRect(5, 5, 3, 3), vm.Selection);
        vm.Selection = null;
        vm.ApplyDepthStroke(new[] { (10, 10), (12, 12) }, DepthSide.Front, false);
        Assert.Null(vm.Selection);
    }

    private static class CanvasMappingZoom
    {
        public const double Fit = RiseViewModel.ZoomMin;
    }
}
