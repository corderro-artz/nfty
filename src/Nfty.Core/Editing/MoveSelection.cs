namespace Nfty.Core.Editing;

/// <summary>
/// Moves a rectangular selection by (dx, dy) on a single flat raster: the source area is cleared to
/// transparent and its pixels are re-stamped at the shifted position. Later writes win, so a pixel that
/// is both cleared and re-stamped ends up stamped.
/// </summary>
public sealed class MoveSelection : RegionEditCommand
{
    private readonly PixelRect _source;
    private readonly int _dx, _dy;

    /// <summary>Moves a rectangular selection, clearing what it left behind.</summary>
    /// <param name="source">The region to move.</param>
    /// <param name="dx">Horizontal offset in pixels.</param>
    /// <param name="dy">Vertical offset in pixels.</param>
    public MoveSelection(PixelRect source, int dx, int dy)
    {
        _source = source;
        _dx = dx;
        _dy = dy;
    }

    /// <inheritdoc />
    protected override IReadOnlyList<(int x, int y, byte value, byte alpha)> ComputePixels(ValueMap map)
    {
        // Build a keyed map so a destination pixel overrides the source-clear at the same coordinate.
        var result = new Dictionary<(int, int), (byte v, byte a)>();
        for (int y = _source.Y; y < _source.Y + _source.Height; y++)
            for (int x = _source.X; x < _source.X + _source.Width; x++)
            {
                if (!map.InBounds(x, y)) continue;
                result[(x, y)] = (0, 0); // clear source
            }
        for (int y = _source.Y; y < _source.Y + _source.Height; y++)
            for (int x = _source.X; x < _source.X + _source.Width; x++)
            {
                if (!map.InBounds(x, y)) continue;
                int nx = x + _dx, ny = y + _dy;
                if (!map.InBounds(nx, ny)) continue;
                result[(nx, ny)] = (map.GetValue(x, y), map.GetAlpha(x, y));
            }
        var pixels = new List<(int, int, byte, byte)>(result.Count);
        foreach (var kv in result)
            pixels.Add((kv.Key.Item1, kv.Key.Item2, kv.Value.v, kv.Value.a));
        return pixels;
    }
}
