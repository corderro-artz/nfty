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
