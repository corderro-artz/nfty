namespace Nfty.Core.Editing;

/// <summary>
/// Which pixels a gesture touches, independent of what it writes there. The brush, eraser, flood
/// fill and the depth tools all ask "where" the same way; they differ only in what each pixel
/// becomes, so the geometry lives here once, payload-free.
/// </summary>
public static class Footprint
{
    /// <summary>
    /// A filled round disc of diameter <paramref name="size"/> stamped along <paramref name="path"/>,
    /// each in-bounds pixel at most once.
    /// </summary>
    /// <remarks>
    /// The path is <b>joined</b>, not merely visited: a disc is stamped at every pixel on the line
    /// between consecutive points, not only at the points themselves. A pointer reports samples at
    /// whatever rate the device and the UI thread manage, so a stroke drawn at any speed arrives as
    /// a handful of scattered coordinates — stamping only those turns a fast stroke into a row of
    /// dots with gaps between them, which is what the editor shipped before this. Joining here
    /// rather than in the front-end makes it a property of "a stroke" rather than of one caller's
    /// sample rate, and fixes every stroke tool at once.
    /// </remarks>
    /// <param name="width">Surface width.</param>
    /// <param name="height">Surface height.</param>
    /// <param name="path">The gesture's pixel path.</param>
    /// <param name="size">Disc diameter in pixels; anything below 1 is treated as 1.</param>
    /// <returns>The stamped pixels in first-visit order, deduplicated and clipped to the surface.</returns>
    public static List<(int x, int y)> Discs(int width, int height, IReadOnlyList<(int x, int y)> path, int size)
    {
        int d = Math.Max(1, size);
        double r = d / 2.0;
        int ir = (int)Math.Ceiling(r);
        var seen = new HashSet<(int, int)>();
        var cells = new List<(int x, int y)>();

        void Stamp(int cx, int cy)
        {
            for (int dy = -ir; dy <= ir; dy++)
                for (int dx = -ir; dx <= ir; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || y < 0 || x >= width || y >= height) continue;
                    if (dx * dx + dy * dy > r * r) continue; // round disc; size 1 (r=0.5) collapses to one pixel
                    if (!seen.Add((x, y))) continue;
                    cells.Add((x, y));
                }
        }

        if (path.Count == 0) return cells;
        Stamp(path[0].x, path[0].y);
        for (int i = 1; i < path.Count; i++)
            foreach (var (x, y) in Between(path[i - 1], path[i]))
                Stamp(x, y);
        return cells;
    }

    /// <summary>
    /// The 4-connected run of pixels exactly equal to the seed pixel, alpha included, so a region
    /// spreads across an erased area without leaking into a painted one.
    /// </summary>
    /// <typeparam name="TPixel">The pixel the surface stores.</typeparam>
    /// <param name="surface">The surface to scan.</param>
    /// <param name="seedX">Seed column.</param>
    /// <param name="seedY">Seed row.</param>
    /// <returns>The matching pixels in breadth-first order; empty when the seed is out of bounds.</returns>
    public static List<(int x, int y)> Region<TPixel>(IEditSurface<TPixel> surface, int seedX, int seedY)
        where TPixel : struct
    {
        var cells = new List<(int x, int y)>();
        if (!surface.InBounds(seedX, seedY)) return cells;

        var cmp = EqualityComparer<TPixel>.Default;
        TPixel target = surface.Get(seedX, seedY);
        var seen = new HashSet<(int, int)>();
        var queue = new Queue<(int, int)>();
        queue.Enqueue((seedX, seedY));
        seen.Add((seedX, seedY));
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            if (!surface.InBounds(x, y)) continue;
            if (!cmp.Equals(surface.Get(x, y), target)) continue;
            cells.Add((x, y));

            // Enqueued one at a time rather than by iterating a freshly-allocated 4-tuple array:
            // that array was allocated once per VISITED PIXEL, so a 2048x2048 fill made roughly four
            // million of them for nothing.
            Visit(x + 1, y);
            Visit(x - 1, y);
            Visit(x, y + 1);
            Visit(x, y - 1);
        }
        return cells;

        void Visit(int nx, int ny)
        {
            if (surface.InBounds(nx, ny) && seen.Add((nx, ny))) queue.Enqueue((nx, ny));
        }
    }

    /// <summary>Every pixel of a rectangle, clipped to the surface, rows then columns.</summary>
    /// <param name="rect">The rectangle.</param>
    /// <param name="width">Surface width.</param>
    /// <param name="height">Surface height.</param>
    /// <returns>The covered in-bounds pixels.</returns>
    public static List<(int x, int y)> Box(PixelRect rect, int width, int height)
    {
        var cells = new List<(int x, int y)>();
        int x0 = Math.Max(0, rect.X), y0 = Math.Max(0, rect.Y);
        int x1 = Math.Min(width, rect.X + rect.Width), y1 = Math.Min(height, rect.Y + rect.Height);
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
                cells.Add((x, y));
        return cells;
    }

    /// <summary>Every pixel from <paramref name="a"/> to <paramref name="b"/> inclusive of the end
    /// and exclusive of the start (the caller has already stamped it), by Bresenham — integer only,
    /// so the same gesture joins up identically on every machine.</summary>
    private static IEnumerable<(int x, int y)> Between((int x, int y) a, (int x, int y) b)
    {
        int x = a.x, y = a.y;
        int dx = Math.Abs(b.x - x), dy = -Math.Abs(b.y - y);
        int sx = x < b.x ? 1 : -1, sy = y < b.y ? 1 : -1;
        int err = dx + dy;
        while (x != b.x || y != b.y)
        {
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x += sx; }
            if (e2 <= dx) { err += dx; y += sy; }
            yield return (x, y);
        }
    }
}
