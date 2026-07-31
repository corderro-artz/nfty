using System.Collections.Generic;
using Nfty.Core.Editing;
using Xunit;

namespace Nfty.Core.Tests;

public class EditHistoryTests
{
    // Minimal concrete command: set one pixel to (value, alpha).
    private sealed class Poke : RegionEditCommand
    {
        private readonly int _x, _y; private readonly byte _v, _a;
        public Poke(int x, int y, byte v, byte a) { _x = x; _y = y; _v = v; _a = a; }
        protected override IReadOnlyList<(int x, int y, byte value, byte alpha)> ComputePixels(ValueMap map)
            => new[] { (_x, _y, _v, _a) };
    }

    [Fact]
    public void Do_then_undo_then_redo_restores_and_reapplies()
    {
        var map = new ValueMap(2, 2);
        var hist = new EditHistory();
        hist.Do(new Poke(1, 1, 123, 255), map);
        Assert.Equal(123, map.GetValue(1, 1));

        hist.Undo(map);
        Assert.Equal(0, map.GetValue(1, 1));
        Assert.Equal(0, map.GetAlpha(1, 1));

        hist.Redo(map);
        Assert.Equal(123, map.GetValue(1, 1));
        Assert.Equal(255, map.GetAlpha(1, 1));
    }

    [Fact]
    public void No_op_flood_fill_is_not_recorded()
    {
        var map = new ValueMap(2, 2);
        // Paint every pixel to value 0, alpha 255 so a fill to 0 finds nothing to change.
        new BrushStroke(new Brush(1, 0), new[] { (0, 0), (1, 0), (0, 1), (1, 1) }).Apply(map);
        var hist = new EditHistory();
        Assert.False(hist.Do(new FloodFill(0, 0, 0), map)); // empty fill — changed nothing
        Assert.False(hist.CanUndo);
    }

    [Fact]
    public void Redundant_brush_over_the_target_value_is_not_recorded()
    {
        var map = new ValueMap(2, 2);
        new BrushStroke(new Brush(1, 90), new[] { (0, 0) }).Apply(map); // (0,0) => value 90, alpha 255
        var hist = new EditHistory();
        // Stamps the same pixel with the value it already holds: after == before, so no change.
        Assert.False(hist.Do(new BrushStroke(new Brush(1, 90), new[] { (0, 0) }), map));
        Assert.False(hist.CanUndo);
    }

    [Fact]
    public void New_edit_clears_the_redo_stack()
    {
        var map = new ValueMap(2, 2);
        var hist = new EditHistory();
        hist.Do(new Poke(0, 0, 10, 255), map);
        hist.Undo(map);
        hist.Do(new Poke(1, 0, 20, 255), map);
        Assert.False(hist.CanRedo);
        hist.Redo(map); // no-op
        Assert.Equal(0, map.GetValue(0, 0));
        Assert.Equal(20, map.GetValue(1, 0));
    }
}
