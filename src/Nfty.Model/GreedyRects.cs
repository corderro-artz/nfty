namespace Nfty.Model;

/// <summary>A merged run of cells that share one key. <see cref="Width"/> and
/// <see cref="Height"/> are in cells and at least 1.</summary>
/// <param name="X">Left column.</param>
/// <param name="Y">Top row (image rows: 0 is the top).</param>
/// <param name="Width">Columns covered.</param>
/// <param name="Height">Rows covered.</param>
/// <param name="Key">The key every covered cell carries.</param>
internal readonly record struct PixelRect(int X, int Y, int Width, int Height, int Key);

/// <summary>
/// Merges the set cells of a keyed grid into maximal rectangles of equal key — what makes a risen
/// sprite a few dozen quads instead of one per pixel.
/// </summary>
/// <remarks>
/// <para>Taken, as an idea, from mixel's <c>GreedyRects</c> and generalized from a boolean mask to a
/// keyed grid, so the front faces of every depth level come out of ONE scan: a cell only joins a
/// rectangle of its own key.</para>
///
/// <para><b>The rectangles TILE the set cells</b> — each exactly once, none unset. Greedy: from the
/// first free cell in row-major order, extend right while the key continues, then down while every
/// cell of the strip continues. Not optimal, and optimal partitioning is not worth its cost here.
/// The discovery order is part of the contract: it reaches the model file, which must be
/// byte-identical for identical input.</para>
/// </remarks>
internal static class GreedyRects
{
    /// <summary>The key of a cell that belongs to no rectangle. Any negative key means the same.</summary>
    public const int Unset = -1;

    /// <summary>Merges a grid.</summary>
    /// <param name="width">Columns.</param>
    /// <param name="height">Rows.</param>
    /// <param name="keys">One key per cell, row-major, top row first; negative means unset.</param>
    /// <returns>The rectangles, in row-major discovery order.</returns>
    public static List<PixelRect> Merge(int width, int height, ReadOnlySpan<int> keys)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (keys.Length != width * height)
            throw new ArgumentException(
                $"A {width}x{height} grid has {width * height} cells; got {keys.Length} keys.", nameof(keys));

        var rects = new List<PixelRect>();
        var used = new bool[keys.Length];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int key = keys[(y * width) + x];
                if (key < 0 || used[(y * width) + x]) continue;

                int right = x + 1;
                while (right < width && Free(keys, used, (y * width) + right, key)) right++;

                int bottom = y + 1;
                while (bottom < height && RowFree(keys, used, width, bottom, x, right, key)) bottom++;

                for (int yy = y; yy < bottom; yy++)
                    for (int xx = x; xx < right; xx++)
                        used[(yy * width) + xx] = true;

                rects.Add(new PixelRect(x, y, right - x, bottom - y, key));
            }
        }
        return rects;
    }

    private static bool Free(ReadOnlySpan<int> keys, bool[] used, int i, int key) =>
        keys[i] == key && !used[i];

    private static bool RowFree(ReadOnlySpan<int> keys, bool[] used, int width, int y, int x0, int x1, int key)
    {
        for (int x = x0; x < x1; x++)
            if (!Free(keys, used, (y * width) + x, key)) return false;
        return true;
    }
}
