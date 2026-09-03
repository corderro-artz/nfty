using Nfty.Core.Editing;
using Nfty.Core.Model;

namespace Nfty.Core.Tests;

/// <summary>
/// A stroke is a line, not a row of dots.
/// </summary>
/// <remarks>
/// Found by drawing in the shipped app: a pointer reports samples at whatever rate the device and
/// the UI thread manage, so a stroke drawn at any real speed arrives as a handful of scattered
/// coordinates. Stamping only those left visible gaps — on a 512x512 canvas with a size-8 brush, a
/// normal drag produced a dotted line. Every stroke tool goes through <c>StampDiscs</c>, so the join
/// lives there and this holds for all of them.
/// </remarks>
public class StrokeContinuityTests
{
    private static ValueMap Map(int w = 64, int h = 64) => ValueMap.ForCanvas(new Dimensions(w, h));

    private static GrayPixel Ink => new(255, 255);

    /// <summary>
    /// Asserts the painted pixels form ONE 8-connected region reaching from <paramref name="a"/> to
    /// <paramref name="b"/>.
    /// </summary>
    /// <remarks>
    /// Connectivity, not "every linearly-interpolated step is painted". Bresenham and linear
    /// interpolation pick different pixels on a non-45 degree diagonal and both are correct lines;
    /// what makes a stroke a stroke is that it has no gaps. Flood-filling the painted set from one
    /// end and requiring the other end is exactly that property and nothing more.
    /// </remarks>
    private static void AssertJoined(ValueMap map, (int x, int y) a, (int x, int y) b)
    {
        Assert.Equal(255, map.GetAlpha(a.x, a.y));
        Assert.Equal(255, map.GetAlpha(b.x, b.y));

        var seen = new HashSet<(int, int)>();
        var queue = new Queue<(int x, int y)>();
        queue.Enqueue(a);
        seen.Add(a);
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (!map.InBounds(nx, ny) || map.GetAlpha(nx, ny) != 255) continue;
                    if (seen.Add((nx, ny))) queue.Enqueue((nx, ny));
                }
        }

        Assert.True(seen.Contains(b),
            $"({b.x},{b.y}) is not reachable from ({a.x},{a.y}) through painted pixels — the stroke is dotted");
    }

    [Fact]
    public void A_brush_joins_two_far_apart_samples_into_a_continuous_line()
    {
        var map = Map();
        // Two samples 40px apart: what a fast drag actually delivers.
        var cmd = new BrushStroke<GrayPixel>(new Brush<GrayPixel>(1, Ink), new[] { (5, 5), (45, 5) });

        Assert.True(cmd.Apply(map));

        AssertJoined(map, (5, 5), (45, 5));
    }

    [Fact]
    public void A_diagonal_stroke_joins_too()
    {
        var map = Map();
        var cmd = new BrushStroke<GrayPixel>(new Brush<GrayPixel>(1, Ink), new[] { (4, 4), (40, 28) });

        Assert.True(cmd.Apply(map));

        AssertJoined(map, (4, 4), (40, 28));
    }

    [Fact]
    public void A_multi_segment_stroke_joins_every_segment()
    {
        var map = Map();
        var path = new[] { (5, 5), (30, 5), (30, 30), (10, 40) };
        var cmd = new BrushStroke<GrayPixel>(new Brush<GrayPixel>(1, Ink), path);

        Assert.True(cmd.Apply(map));

        for (int i = 1; i < path.Length; i++) AssertJoined(map, path[i - 1], path[i]);
    }

    [Fact]
    public void The_eraser_joins_its_samples_as_well()
    {
        var map = Map();
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
                map.Set(x, y, 200, 255);

        Assert.True(new EraseStroke<GrayPixel>(1, new[] { (5, 10), (50, 10) }).Apply(map));

        for (int x = 5; x <= 50; x++)
            Assert.Equal(0, map.GetAlpha(x, 10));
    }

    /// <summary>A single-sample stroke is a dot — a click, not a drag — and must stay one.</summary>
    [Fact]
    public void A_single_sample_paints_one_disc_and_nothing_else()
    {
        var map = Map();

        Assert.True(new BrushStroke<GrayPixel>(new Brush<GrayPixel>(1, Ink), new[] { (20, 20) }).Apply(map));

        Assert.Equal(255, map.GetAlpha(20, 20));
        Assert.Equal(0, map.GetAlpha(21, 20));
        Assert.Equal(0, map.GetAlpha(19, 20));
    }

    /// <summary>Joining must not paint outside the segment: a stroke that leaves the surface and
    /// comes back is clipped, not wrapped.</summary>
    [Fact]
    public void A_stroke_running_off_the_edge_is_clipped_rather_than_wrapped()
    {
        var map = Map(16, 16);

        Assert.True(new BrushStroke<GrayPixel>(new Brush<GrayPixel>(1, Ink), new[] { (-20, 8), (35, 8) }).Apply(map));

        for (int x = 0; x < 16; x++) Assert.Equal(255, map.GetAlpha(x, 8));
        for (int x = 0; x < 16; x++) Assert.Equal(0, map.GetAlpha(x, 7));   // no wrap onto another row
    }

    /// <summary>The join is integer-only Bresenham, so the same gesture produces the same pixels
    /// everywhere — the determinism rule the rest of the engine keeps.</summary>
    [Fact]
    public void The_same_gesture_paints_the_same_pixels_every_time()
    {
        var a = Map();
        var b = Map();
        var path = new[] { (3, 61), (58, 7), (12, 33) };

        new BrushStroke<GrayPixel>(new Brush<GrayPixel>(3, Ink), path).Apply(a);
        new BrushStroke<GrayPixel>(new Brush<GrayPixel>(3, Ink), path).Apply(b);

        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
                Assert.Equal(a.GetAlpha(x, y), b.GetAlpha(x, y));
    }
}
