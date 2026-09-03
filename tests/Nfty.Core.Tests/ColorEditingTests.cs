using Nfty.Core.Editing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>
/// The same five commands, over the full-colour surface. Geometry is written once and shared with
/// the grayscale side, so what is asserted here is the payload: colour arrives intact, region
/// matching sees the whole pixel rather than one channel, and history restores exactly.
/// </summary>
public class ColorEditingTests
{
    private static readonly Rgba32 Magenta = new(214, 36, 159, 255);
    private static readonly Rgba32 Cyan = new(43, 214, 205, 255);

    [Fact]
    public void A_brush_paints_the_exact_colour_it_carries()
    {
        var map = new ColorMap(3, 3);
        new BrushStroke<Rgba32>(new Brush<Rgba32>(1, Magenta), new[] { (1, 1) }).Apply(map);
        Assert.Equal(Magenta, map.Get(1, 1));
        Assert.Equal(new Rgba32(0, 0, 0, 0), map.Get(0, 0));   // untouched
    }

    [Fact]
    public void A_shape_fills_its_bounds_in_colour()
    {
        var map = new ColorMap(4, 4);
        new DrawShape<Rgba32>(ShapeKind.Rectangle, new PixelRect(1, 1, 2, 2), Cyan).Apply(map);
        Assert.Equal(Cyan, map.Get(1, 1));
        Assert.Equal(Cyan, map.Get(2, 2));
        Assert.Equal(new Rgba32(0, 0, 0, 0), map.Get(0, 0));
    }

    [Fact]
    public void Erasing_drops_the_alpha_and_keeps_the_colour()
    {
        var map = new ColorMap(3, 3);
        new BrushStroke<Rgba32>(new Brush<Rgba32>(1, Magenta), new[] { (1, 1) }).Apply(map);
        new EraseStroke<Rgba32>(1, new[] { (1, 1) }).Apply(map);
        Assert.Equal(new Rgba32(214, 36, 159, 0), map.Get(1, 1));
    }

    [Fact]
    public void A_fill_matches_the_whole_pixel_not_one_channel()
    {
        // Two colours sharing a red channel: a fill keyed on R alone would leak across the wall.
        var map = new ColorMap(3, 1);
        var a = new Rgba32(100, 0, 0, 255);
        var wall = new Rgba32(100, 200, 0, 255);
        map.Set(0, 0, a);
        map.Set(1, 0, a);
        map.Set(2, 0, wall);

        new FloodFill<Rgba32>(0, 0, Cyan).Apply(map);
        Assert.Equal(Cyan, map.Get(0, 0));
        Assert.Equal(Cyan, map.Get(1, 0));
        Assert.Equal(wall, map.Get(2, 0));
    }

    [Fact]
    public void A_move_carries_the_colour_and_clears_the_source()
    {
        var map = new ColorMap(4, 1);
        new BrushStroke<Rgba32>(new Brush<Rgba32>(1, Magenta), new[] { (0, 0) }).Apply(map);
        new MoveSelection<Rgba32>(new PixelRect(0, 0, 1, 1), 2, 0).Apply(map);
        Assert.Equal(new Rgba32(0, 0, 0, 0), map.Get(0, 0));
        Assert.Equal(Magenta, map.Get(2, 0));
    }

    [Fact]
    public void Undo_and_redo_restore_a_colour_surface_exactly()
    {
        var map = new ColorMap(2, 2);
        map.Set(0, 0, Cyan);   // pre-existing art the edit must be able to put back

        var hist = new EditHistory<Rgba32>();
        Assert.True(hist.Do(new DrawShape<Rgba32>(ShapeKind.Rectangle, new PixelRect(0, 0, 2, 2), Magenta), map));
        Assert.Equal(Magenta, map.Get(0, 0));
        Assert.Equal(Magenta, map.Get(1, 1));

        hist.Undo(map);
        Assert.Equal(Cyan, map.Get(0, 0));
        Assert.Equal(new Rgba32(0, 0, 0, 0), map.Get(1, 1));

        hist.Redo(map);
        Assert.Equal(Magenta, map.Get(0, 0));
        Assert.Equal(Magenta, map.Get(1, 1));
        Assert.True(hist.CanUndo);
        Assert.False(hist.CanRedo);
    }

    [Fact]
    public void A_redundant_colour_edit_is_not_recorded()
    {
        var map = new ColorMap(2, 2);
        new BrushStroke<Rgba32>(new Brush<Rgba32>(1, Magenta), new[] { (0, 0) }).Apply(map);
        var hist = new EditHistory<Rgba32>();
        Assert.False(hist.Do(new BrushStroke<Rgba32>(new Brush<Rgba32>(1, Magenta), new[] { (0, 0) }), map));
        Assert.False(hist.CanUndo);
    }

    [Fact]
    public void An_edited_colour_surface_round_trips_to_an_image_with_exact_pixels()
    {
        var map = new ColorMap(2, 2);
        new DrawShape<Rgba32>(ShapeKind.Rectangle, new PixelRect(0, 0, 2, 1), Magenta).Apply(map);
        new DrawShape<Rgba32>(ShapeKind.Rectangle, new PixelRect(0, 1, 2, 1), Cyan).Apply(map);

        using Image<Rgba32> img = map.ToImage();
        Assert.Equal(Magenta, img[0, 0]);
        Assert.Equal(Magenta, img[1, 0]);
        Assert.Equal(Cyan, img[0, 1]);
        Assert.Equal(Cyan, img[1, 1]);

        var back = ColorMap.FromImage(img);
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 2; x++)
                Assert.Equal(map.Get(x, y), back.Get(x, y));
    }
}
