using Nfty.Core.Editing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.Core.Tests;

public class TargetedHistoryTests
{
    private static DepthSurface Surface()
    {
        using var art = new Image<Rgba32>(2, 1, new Rgba32(10, 10, 10, 255));
        return new DepthSurface(art, null);
    }

    private static DepthStep Raise(int x) => new(new[] { (x, 0) }, DepthSide.Front, 1);

    [Fact]
    public void Undo_restores_the_surface_the_step_touched_and_names_it()
    {
        var sa = Surface();
        var sb = Surface();
        var map = new Dictionary<string, DepthSurface> { ["a"] = sa, ["b"] = sb };
        var h = new TargetedHistory<string, DepthPixel>();
        Assert.True(h.Do("a", Raise(0), sa));
        Assert.True(h.Do("b", Raise(1), sb));
        Assert.Equal("b", h.PeekUndo);

        Assert.True(h.Undo(k => map[k], out var key));
        Assert.Equal("b", key);
        Assert.Equal(new DepthPixel(0, 0), sb.Get(1, 0));
        Assert.Equal(new DepthPixel(1, 0), sa.Get(0, 0));
        Assert.Equal("b", h.PeekRedo);

        Assert.True(h.Undo(k => map[k], out key));
        Assert.Equal("a", key);
        Assert.Equal(new DepthPixel(0, 0), sa.Get(0, 0));
        Assert.False(h.CanUndo);

        Assert.True(h.Redo(k => map[k], out key));
        Assert.Equal("a", key);
        Assert.Equal(new DepthPixel(1, 0), sa.Get(0, 0));
    }

    [Fact]
    public void A_no_op_edit_is_not_recorded()
    {
        var s = Surface();
        var h = new TargetedHistory<string, DepthPixel>();
        Assert.False(h.Do("a", new DepthStep(new[] { (0, 0) }, DepthSide.Front, -1), s));   // already at the floor
        Assert.False(h.CanUndo);
    }

    [Fact]
    public void A_new_edit_clears_redo()
    {
        var s = Surface();
        var h = new TargetedHistory<string, DepthPixel>();
        h.Do("a", Raise(0), s);
        h.Undo(_ => s, out _);
        Assert.True(h.CanRedo);
        h.Do("a", Raise(1), s);
        Assert.False(h.CanRedo);
    }
}
