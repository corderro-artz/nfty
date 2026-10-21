using Nfty.Core.Editing;
using Xunit;

namespace Nfty.Core.Tests;

public class FootprintTests
{
    [Fact]
    public void A_size_one_disc_is_one_pixel() =>
        Assert.Equal(new[] { (2, 2) }, Footprint.Discs(5, 5, new[] { (2, 2) }, 1));

    [Fact]
    public void A_size_two_disc_is_a_plus_and_size_three_is_the_full_square()
    {
        Assert.Equal(5, Footprint.Discs(5, 5, new[] { (2, 2) }, 2).Count);
        var three = Footprint.Discs(5, 5, new[] { (2, 2) }, 3);
        Assert.Equal(9, three.Count);
        Assert.Contains((1, 1), three);
        Assert.Contains((3, 3), three);
    }

    [Fact]
    public void A_path_is_joined_so_a_fast_stroke_has_no_gaps() =>
        Assert.Equal(new[] { (0, 0), (1, 0), (2, 0), (3, 0), (4, 0), (5, 0) },
            Footprint.Discs(8, 1, new[] { (0, 0), (5, 0) }, 1).OrderBy(p => p.x).ToArray());

    [Fact]
    public void Discs_clip_to_the_canvas_and_never_repeat_a_pixel()
    {
        var cells = Footprint.Discs(3, 3, new[] { (0, 0), (0, 0), (2, 0), (0, 0) }, 3);
        Assert.All(cells, p => Assert.True(p.x is >= 0 and < 3 && p.y is >= 0 and < 3));
        Assert.Equal(cells.Count, cells.Distinct().Count());
    }

    [Fact]
    public void Region_is_the_four_connected_run_of_the_seed_pixel()
    {
        var map = new ValueMap(4, 1);
        new BrushStroke<GrayPixel>(new Brush<GrayPixel>(1, new GrayPixel(50, 255)), new[] { (2, 0) }).Apply(map);
        Assert.Equal(new[] { (0, 0), (1, 0) }, Footprint.Region(map, 0, 0).OrderBy(p => p.x).ToArray());
        Assert.Equal(new[] { (3, 0) }, Footprint.Region(map, 3, 0));          // walled off by (2,0)
    }

    [Fact]
    public void Region_of_an_out_of_bounds_seed_is_empty() =>
        Assert.Empty(Footprint.Region(new ValueMap(2, 2), 5, 5));

    [Fact]
    public void Box_covers_its_width_and_height_and_is_clipped()
    {
        Assert.Equal(6, Footprint.Box(new PixelRect(1, 1, 3, 2), 8, 8).Count);
        Assert.Equal(4, Footprint.Box(new PixelRect(-1, -1, 3, 3), 8, 8).Count);   // (0..1, 0..1)
    }

    [Fact]
    public void The_brush_and_the_fill_still_touch_exactly_their_footprint()
    {
        var map = new ValueMap(6, 6);
        new BrushStroke<GrayPixel>(new Brush<GrayPixel>(3, new GrayPixel(90, 255)), new[] { (2, 2), (4, 2) }).Apply(map);
        var painted = Enumerable.Range(0, 6).SelectMany(y => Enumerable.Range(0, 6).Select(x => (x, y)))
            .Where(p => map.GetValue(p.x, p.y) == 90).ToHashSet();
        Assert.True(painted.SetEquals(Footprint.Discs(6, 6, new[] { (2, 2), (4, 2) }, 3)));
    }
}
