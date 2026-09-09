using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The modifier conventions, as arithmetic. What each tool does with a held key is decided in one
/// pure function, so it can be asserted without a window.
/// </summary>
public class StrokeConstraintTests
{
    private static (int x, int y)[] Path(params (int x, int y)[] p) => p;

    private static (int x, int y)[] Apply(EditorTool tool, (int x, int y)[] path,
        bool constrain = false, bool fromCenter = false, bool moving = false, int size = 64)
    {
        var result = StrokeConstraint.Apply(tool, constrain, fromCenter, moving, path, size, size);
        var copy = new (int x, int y)[result.Count];
        for (int i = 0; i < result.Count; i++) copy[i] = result[i];
        return copy;
    }

    /// <summary>No modifier is the identity, and it must be the identity on the SAME list — a brush
    /// stroke reduced to its ends would turn every freehand curve into a straight line.</summary>
    [Fact]
    public void With_no_modifier_the_path_is_untouched()
    {
        var path = Path((0, 0), (3, 9), (4, 2));
        Assert.Equal(path, Apply(EditorTool.Brush, path));
        Assert.Equal(path, Apply(EditorTool.Line, path));
        Assert.Equal(path, Apply(EditorTool.Rectangle, path));
    }

    [Theory]
    // Nearly horizontal snaps flat, nearly vertical snaps upright, and the middle snaps to the
    // diagonal at the length of the LONGER side, so the stroke still reaches the pointer.
    [InlineData(20, 3, 20, 0)]
    [InlineData(3, 20, 0, 20)]
    [InlineData(20, 14, 20, 20)]
    [InlineData(-20, 14, -20, 20)]
    [InlineData(-20, -14, -20, -20)]
    public void Constrain_snaps_a_line_to_the_nearest_eighth_turn(int dx, int dy, int wantX, int wantY)
    {
        var result = Apply(EditorTool.Line, Path((30, 30), (30 + dx, 30 + dy)), constrain: true);
        Assert.Equal(new[] { (30, 30), (30 + wantX, 30 + wantY) }, result);
    }

    /// <summary>A constrained stroke tool is a straight segment: two points, which
    /// <c>StampDiscs</c> joins with Bresenham. That is the same mechanism the Line tool uses, so
    /// there is no second definition of "straight" to keep in step.</summary>
    [Fact]
    public void Constrain_reduces_a_freehand_stroke_to_a_straight_segment()
    {
        var result = Apply(EditorTool.Brush, Path((2, 2), (5, 9), (9, 4), (22, 3)), constrain: true);
        Assert.Equal(new[] { (2, 2), (22, 2) }, result);
    }

    [Theory]
    [InlineData(EditorTool.Rectangle)]
    [InlineData(EditorTool.Circle)]
    [InlineData(EditorTool.Triangle)]
    public void Constrain_squares_a_shapes_box_off_its_longer_side(EditorTool tool)
    {
        var result = Apply(tool, Path((10, 10), (30, 16)), constrain: true);
        Assert.Equal(new[] { (10, 10), (30, 30) }, result);
    }

    /// <summary>Squaring keeps the direction it was dragged in — up-left stays up-left.</summary>
    [Fact]
    public void A_squared_box_keeps_the_direction_it_was_dragged()
    {
        var result = Apply(EditorTool.Rectangle, Path((30, 30), (10, 24)), constrain: true);
        Assert.Equal(new[] { (30, 30), (10, 10) }, result);
    }

    /// <summary>Alt makes the press point the CENTER: the far corner keeps its offset and the near
    /// one is reflected through the middle, so the shape grows both ways at once.</summary>
    [Fact]
    public void From_center_reflects_the_press_point_through_the_drag()
    {
        var result = Apply(EditorTool.Circle, Path((30, 30), (36, 34)), fromCenter: true);
        Assert.Equal(new[] { (24, 26), (36, 34) }, result);
    }

    /// <summary>Both together: a circle centered on the press point, and round.</summary>
    [Fact]
    public void From_center_and_constrain_compose()
    {
        var result = Apply(EditorTool.Circle, Path((30, 30), (36, 34)), constrain: true, fromCenter: true);
        Assert.Equal(new[] { (24, 24), (36, 36) }, result);
    }

    /// <summary>Select MARKS a square region but MOVES on an eighth turn — the two gestures share a
    /// tool and a key and mean different things by it, which is what every editor does.</summary>
    [Fact]
    public void Select_squares_a_mark_and_snaps_a_move()
    {
        Assert.Equal(new[] { (10, 10), (30, 30) },
            Apply(EditorTool.Select, Path((10, 10), (30, 16)), constrain: true));
        Assert.Equal(new[] { (10, 10), (30, 10) },
            Apply(EditorTool.Select, Path((10, 10), (30, 16)), constrain: true, moving: true));
    }

    /// <summary>Fill acts on the press point alone, so no modifier has anything to say about it.</summary>
    [Fact]
    public void Fill_is_never_constrained()
    {
        var path = Path((4, 4), (20, 9));
        Assert.Equal(path, Apply(EditorTool.Fill, path, constrain: true, fromCenter: true));
    }

    /// <summary>A constrained endpoint can point off the canvas — the raw path is clamped as it is
    /// collected, but a snap lengthens the short axis afterwards. The edit commands clip, so this is
    /// belt and braces; the marquee does not, which is what it is actually for.</summary>
    [Fact]
    public void A_constrained_endpoint_stays_on_the_canvas()
    {
        var result = Apply(EditorTool.Rectangle, Path((0, 0), (15, 4)), constrain: true, size: 8);
        Assert.Equal(new[] { (0, 0), (7, 7) }, result);
    }
}
