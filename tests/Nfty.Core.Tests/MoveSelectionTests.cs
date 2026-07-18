using Nfty.Core.Editing;
using Xunit;

namespace Nfty.Core.Tests;

public class MoveSelectionTests
{
    [Fact]
    public void Moves_pixels_and_clears_the_source()
    {
        var map = new ValueMap(4, 1);
        new BrushStroke(new Brush(1, 210), new[] { (0, 0) }).Apply(map);
        new MoveSelection(new PixelRect(0, 0, 1, 1), 2, 0).Apply(map);
        Assert.Equal(0, map.GetAlpha(0, 0));   // source cleared
        Assert.Equal(210, map.GetValue(2, 0)); // moved here
        Assert.Equal(255, map.GetAlpha(2, 0));
    }

    [Fact]
    public void Undo_restores_original_position()
    {
        var map = new ValueMap(4, 1);
        new BrushStroke(new Brush(1, 210), new[] { (0, 0) }).Apply(map);
        var move = new MoveSelection(new PixelRect(0, 0, 1, 1), 2, 0);
        move.Apply(map);
        move.Undo(map);
        Assert.Equal(210, map.GetValue(0, 0));
        Assert.Equal(0, map.GetAlpha(2, 0));
    }
}
