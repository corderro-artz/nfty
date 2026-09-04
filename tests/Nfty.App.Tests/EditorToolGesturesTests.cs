using System.IO;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The two tools whose gesture is not "stamp where the pointer went": <b>Line</b>, which uses only
/// the ends of the drag, and <b>Select</b>, which is two gestures (mark, then move) told apart by
/// where the drag starts. Both were affordances with nothing behind them — Select painted nothing at
/// all, and there was no line tool — so these pin the behavior a user would try first.
/// </summary>
public class EditorToolGesturesTests
{
    private sealed class NoPicker : IFilePickerService
    {
        public Task<string?> OpenFileAsync(string t, params string[] e) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string t, string e) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string t) => Task.FromResult<string?>(null);
    }

    private static IngredientEditorViewModel Editor(
        (string path, CookBookSession session, LoadedRecipe recipe, LoadedIngredient ing) f) =>
        new(f.ing, f.recipe, f.session.Current!, new ImageBridge(), new FakeNav(),
            f.session, new FakeDialogs(), new NoPicker());

    private static void Run(Action<IngredientEditorViewModel> body)
    {
        var f = IngredientEditorSaveTests.OnDisk(LayerKind.Dynamic);
        try
        {
            using var vm = Editor(f);
            vm.BrushSize = 1;                 // a one-pixel nib, so a stroke is exactly its path
            body(vm);
        }
        finally { f.session.Dispose(); Directory.Delete(Path.GetDirectoryName(f.path)!, true); }
    }

    // ---------------- Line ----------------

    /// <summary>
    /// The whole point of a line tool: the drag's middle is where the pointer wandered while aiming,
    /// not part of the mark. Freehand would paint the detour; Line must not.
    /// </summary>
    [AvaloniaFact]
    public void Line_uses_only_the_ends_of_the_drag_and_ignores_the_wander_between_them()
    {
        Run(vm =>
        {
            vm.ActiveTool = EditorTool.Line;
            vm.BrushValue = 200;
            // Along the top row, but by way of the bottom of the canvas.
            vm.ApplyToolStroke(new[] { (0, 0), (2, 6), (4, 7), (6, 5), (7, 0) });

            for (int x = 0; x <= 7; x++)
                Assert.Equal(200, vm.ValueAt(x, 0));
            // The detour's own pixels are untouched — this is the assertion the freehand path fails.
            Assert.Equal(0, vm.ValueAt(4, 7));
            Assert.Equal(0, vm.ValueAt(2, 6));
        });
    }

    /// <summary>A diagonal is joined, not two dots — the same Bresenham walk freehand uses.</summary>
    [AvaloniaFact]
    public void Line_joins_its_two_ends_diagonally()
    {
        Run(vm =>
        {
            vm.ActiveTool = EditorTool.Line;
            vm.BrushValue = 150;
            vm.ApplyToolStroke(new[] { (0, 0), (7, 7) });

            for (int i = 0; i <= 7; i++)
                Assert.Equal(150, vm.ValueAt(i, i));
        });
    }

    // ---------------- Select: marking ----------------

    [AvaloniaFact]
    public void A_drag_with_the_select_tool_marks_that_rectangle_and_paints_nothing()
    {
        Run(vm =>
        {
            vm.ActiveTool = EditorTool.Select;
            vm.BrushValue = 200;
            vm.ApplyToolStroke(new[] { (1, 1), (3, 4) });

            Assert.True(vm.HasSelection);
            var sel = vm.Selection!.Value;
            Assert.Equal((1, 1, 3, 4), (sel.X, sel.Y, sel.Width, sel.Height));
            Assert.Equal(0, vm.ValueAt(2, 2));       // marking is not painting
            Assert.False(vm.UndoCommand.CanExecute(null));                // …and so is not an undo step
        });
    }

    [AvaloniaFact]
    public void A_click_outside_the_marquee_drops_it()
    {
        Run(vm =>
        {
            vm.ActiveTool = EditorTool.Select;
            vm.ApplyToolStroke(new[] { (1, 1), (3, 3) });
            Assert.True(vm.HasSelection);

            vm.ApplyToolStroke(new[] { (7, 7) });    // a click, not a drag, away from the marquee
            Assert.False(vm.HasSelection);
        });
    }

    [AvaloniaFact]
    public void Escape_drops_the_marquee()
    {
        Run(vm =>
        {
            vm.ActiveTool = EditorTool.Select;
            vm.ApplyToolStroke(new[] { (1, 1), (3, 3) });
            Assert.True(vm.HasSelection);

            vm.ClearSelectionCommand.Execute(null);
            Assert.False(vm.HasSelection);
        });
    }

    /// <summary>A marquee left standing under the brush would be a control that looks live and is
    /// not — the exact thing this pass removed.</summary>
    [AvaloniaFact]
    public void Leaving_the_select_tool_drops_the_marquee()
    {
        Run(vm =>
        {
            vm.ActiveTool = EditorTool.Select;
            vm.ApplyToolStroke(new[] { (1, 1), (3, 3) });
            Assert.True(vm.HasSelection);

            vm.ActiveTool = EditorTool.Brush;
            Assert.False(vm.HasSelection);
        });
    }

    // ---------------- Select: moving ----------------

    [AvaloniaFact]
    public void Dragging_from_inside_the_marquee_moves_the_pixels_and_the_marquee_with_them()
    {
        Run(vm =>
        {
            vm.ActiveTool = EditorTool.Brush;
            vm.BrushValue = 200;
            vm.ApplyToolStroke(new[] { (1, 1) });
            Assert.Equal(200, vm.ValueAt(1, 1));

            vm.ActiveTool = EditorTool.Select;
            vm.ApplyToolStroke(new[] { (0, 0), (2, 2) });          // mark a 3x3 around it
            vm.ApplyToolStroke(new[] { (1, 1), (5, 4) });          // grab inside, drop at +4,+3

            Assert.Equal(0, vm.ValueAt(1, 1));                      // source cleared
            Assert.Equal(200, vm.ValueAt(5, 4));                    // landed
            var sel = vm.Selection!.Value;
            Assert.Equal((4, 3, 3, 3), (sel.X, sel.Y, sel.Width, sel.Height));
        });
    }

    /// <summary>A move IS an edit, so it has to be undoable like every other one.</summary>
    [AvaloniaFact]
    public void A_move_is_one_undo_step()
    {
        Run(vm =>
        {
            vm.ActiveTool = EditorTool.Brush;
            vm.BrushValue = 200;
            vm.ApplyToolStroke(new[] { (1, 1) });

            vm.ActiveTool = EditorTool.Select;
            vm.ApplyToolStroke(new[] { (0, 0), (2, 2) });
            vm.ApplyToolStroke(new[] { (1, 1), (5, 4) });
            Assert.Equal(200, vm.ValueAt(5, 4));

            vm.UndoCommand.Execute(null);

            Assert.Equal(200, vm.ValueAt(1, 1));
            Assert.Equal(0, vm.ValueAt(5, 4));
        });
    }

    /// <summary>Pressing inside the marquee and releasing on the same pixel is a click, not a
    /// grab. It must leave the marquee exactly where it was — the obvious wrong reading is to treat
    /// it as a drag starting outside and re-mark a 1x1 region — and add no undo step.</summary>
    [AvaloniaFact]
    public void A_click_inside_the_marquee_moves_nothing_and_keeps_it()
    {
        Run(vm =>
        {
            vm.ActiveTool = EditorTool.Select;
            vm.ApplyToolStroke(new[] { (0, 0), (2, 2) });
            vm.ApplyToolStroke(new[] { (1, 1) });

            Assert.True(vm.HasSelection);
            var sel = vm.Selection!.Value;
            Assert.Equal((0, 0, 3, 3), (sel.X, sel.Y, sel.Width, sel.Height));
            Assert.False(vm.UndoCommand.CanExecute(null));
        });
    }
}
