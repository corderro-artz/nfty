using Nfty.Core.Editing;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>
/// The lock is binary alpha — every painted pixel comes out 255 or 0 — enforced at the paint layer
/// so no command can miss it, and refusing nothing when unlocked.
/// </summary>
public class OpacityLockTests
{
    // A map whose every pixel already carries partial alpha, as an imported PNG's would. Commands
    // that copy alpha from the surface (erase, move) and fills that match on it start from here.
    private static ValueMap Translucent(int w, int h, byte value, byte alpha)
    {
        var m = new ValueMap(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                m.Set(x, y, value, alpha);
        return m;
    }

    [Fact]
    public void Locked_is_what_a_command_gets_when_nobody_says_otherwise()
    {
        Assert.Equal(OpacityLock.Locked, default(OpacityLock));

        var map = new ValueMap(2, 2);
        // No opacity argument at all — the safe mode has to be the one that needs no ceremony.
        new BrushStroke<GrayPixel>(new Brush<GrayPixel>(1, new GrayPixel(120, 200)), new[] { (0, 0) })
            .Apply(map);
        Assert.Equal(255, map.GetAlpha(0, 0));
    }

    [Theory]
    [InlineData(255, 255)]
    [InlineData(200, 255)]
    [InlineData(128, 255)]   // the threshold itself rounds up
    [InlineData(127, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 0)]
    public void A_locked_brush_snaps_its_alpha_to_an_end(byte painted, byte expected)
    {
        var map = new ValueMap(2, 2);
        new BrushStroke<GrayPixel>(new Brush<GrayPixel>(1, new GrayPixel(120, painted)),
            new[] { (0, 0) }, OpacityLock.Locked).Apply(map);
        Assert.Equal(expected, map.GetAlpha(0, 0));
        Assert.Equal(120, map.GetValue(0, 0));   // only alpha is touched
    }

    [Fact]
    public void The_lock_admits_only_0_and_255_across_every_command()
    {
        // Brush, shape, fill, erase and move, each handed something partial, on one map.
        var map = Translucent(6, 6, 10, 100);

        new BrushStroke<GrayPixel>(new Brush<GrayPixel>(1, new GrayPixel(200, 200)),
            new[] { (0, 0) }, OpacityLock.Locked).Apply(map);
        new DrawShape<GrayPixel>(ShapeKind.Rectangle, new PixelRect(1, 0, 2, 1),
            new GrayPixel(150, 130), OpacityLock.Locked).Apply(map);
        new FloodFill<GrayPixel>(0, 5, new GrayPixel(60, 90), OpacityLock.Locked).Apply(map);
        new EraseStroke<GrayPixel>(1, new[] { (5, 0) }, OpacityLock.Locked).Apply(map);
        new MoveSelection<GrayPixel>(new PixelRect(3, 0, 1, 1), 0, 1, OpacityLock.Locked).Apply(map);

        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                Assert.True(map.GetAlpha(x, y) is 0 or 255,
                    $"({x},{y}) alpha {map.GetAlpha(x, y)} is neither erased nor opaque");
    }

    [Fact]
    public void A_locked_flood_fill_snaps_the_alpha_it_takes_from_its_payload()
    {
        // Fill is the easy miss: its region is defined by the seed pixel's alpha, so it is the one
        // command whose alpha does not come from a brush constant.
        var map = Translucent(3, 1, 10, 100);   // the whole row matches the seed at alpha 100
        new FloodFill<GrayPixel>(0, 0, new GrayPixel(200, 140), OpacityLock.Locked).Apply(map);

        for (int x = 0; x < 3; x++)
        {
            Assert.Equal(200, map.GetValue(x, 0));
            Assert.Equal(255, map.GetAlpha(x, 0));   // 140 admitted as fully opaque
        }
    }

    [Fact]
    public void A_locked_flood_fill_still_fills_a_region_it_only_differs_from_in_alpha()
    {
        // The early-out compares against the ADMITTED fill, not the raw one: seed and fill are the
        // same pixel here, yet under the lock the fill genuinely changes the region to opaque.
        var map = Translucent(3, 1, 10, 200);
        var fill = new FloodFill<GrayPixel>(0, 0, new GrayPixel(10, 200), OpacityLock.Locked);
        Assert.True(fill.Apply(map));
        for (int x = 0; x < 3; x++)
            Assert.Equal(255, map.GetAlpha(x, 0));
    }

    [Fact]
    public void A_locked_move_snaps_the_alpha_it_copies_off_the_surface()
    {
        var map = new ValueMap(4, 1);
        map.Set(0, 0, 210, 200);   // an imported soft pixel
        map.Set(1, 0, 40, 100);    // and one under the threshold
        new MoveSelection<GrayPixel>(new PixelRect(0, 0, 2, 1), 2, 0, OpacityLock.Locked).Apply(map);

        Assert.Equal(210, map.GetValue(2, 0));
        Assert.Equal(255, map.GetAlpha(2, 0));   // 200 hardens
        Assert.Equal(40, map.GetValue(3, 0));
        Assert.Equal(0, map.GetAlpha(3, 0));     // 100 drops out
    }

    [Fact]
    public void Unlocked_writes_partial_alpha_through_unchanged()
    {
        // Proves the lock is doing real work rather than the pipeline being incapable of partial alpha.
        var map = new ValueMap(6, 1);
        new BrushStroke<GrayPixel>(new Brush<GrayPixel>(1, new GrayPixel(120, 200)),
            new[] { (0, 0) }, OpacityLock.Unlocked).Apply(map);
        new DrawShape<GrayPixel>(ShapeKind.Rectangle, new PixelRect(1, 0, 1, 1),
            new GrayPixel(150, 130), OpacityLock.Unlocked).Apply(map);
        new FloodFill<GrayPixel>(2, 0, new GrayPixel(60, 90), OpacityLock.Unlocked).Apply(map);

        Assert.Equal(200, map.GetAlpha(0, 0));
        Assert.Equal(130, map.GetAlpha(1, 0));
        Assert.Equal(90, map.GetAlpha(2, 0));
    }

    [Fact]
    public void Unlocked_move_carries_a_soft_edge_across_intact()
    {
        var map = new ValueMap(4, 1);
        map.Set(0, 0, 210, 200);
        new MoveSelection<GrayPixel>(new PixelRect(0, 0, 1, 1), 2, 0, OpacityLock.Unlocked).Apply(map);
        Assert.Equal(200, map.GetAlpha(2, 0));
    }

    [Fact]
    public void Undo_restores_partial_alpha_the_lock_would_never_have_painted()
    {
        // The lock governs what is painted, never what is put back — otherwise undoing a stroke over
        // an imported soft edge would quietly harden the pixels the stroke did not create.
        var map = Translucent(2, 1, 70, 90);
        var stroke = new BrushStroke<GrayPixel>(new Brush<GrayPixel>(1, new GrayPixel(255, 255)),
            new[] { (0, 0) }, OpacityLock.Locked);
        stroke.Apply(map);
        Assert.Equal(255, map.GetAlpha(0, 0));

        stroke.Undo(map);
        Assert.Equal(90, map.GetAlpha(0, 0));   // exactly what was there
        Assert.Equal(70, map.GetValue(0, 0));
    }

    [Fact]
    public void The_lock_applies_to_the_colour_surface_too()
    {
        var map = new ColorMap(4, 1);
        new BrushStroke<Rgba32>(new Brush<Rgba32>(1, new Rgba32(10, 20, 30, 200)),
            new[] { (0, 0) }, OpacityLock.Locked).Apply(map);
        new FloodFill<Rgba32>(1, 0, new Rgba32(40, 50, 60, 100), OpacityLock.Locked).Apply(map);

        Assert.Equal(new Rgba32(10, 20, 30, 255), map.Get(0, 0));   // RGB untouched, alpha snapped up
        Assert.Equal(new Rgba32(40, 50, 60, 0), map.Get(1, 0));     // and down
    }

    [Fact]
    public void Unlocked_colour_painting_keeps_partial_alpha()
    {
        var map = new ColorMap(2, 1);
        new BrushStroke<Rgba32>(new Brush<Rgba32>(1, new Rgba32(10, 20, 30, 200)),
            new[] { (0, 0) }, OpacityLock.Unlocked).Apply(map);
        Assert.Equal(new Rgba32(10, 20, 30, 200), map.Get(0, 0));
    }
}
