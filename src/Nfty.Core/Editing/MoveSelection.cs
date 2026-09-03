namespace Nfty.Core.Editing;

/// <summary>
/// Moves a rectangular selection by (dx, dy) on a single flat raster: the source area is cleared to
/// transparent and its pixels are re-stamped at the shifted position. Later writes win, so a pixel that
/// is both cleared and re-stamped ends up stamped.
/// </summary>
/// <remarks>
/// The re-stamped pixels are copied from the surface, so they arrive carrying whatever alpha they
/// already had — an imported soft edge included. Under <see cref="OpacityLock.Locked"/> the base
/// snaps them like any other painted pixel: moving a soft-edged sprite hardens its edge, which is
/// the lock doing what it says rather than a gap in it.
/// </remarks>
/// <typeparam name="TPixel">The pixel the target surface stores.</typeparam>
public sealed class MoveSelection<TPixel> : RegionEditCommand<TPixel> where TPixel : struct
{
    private readonly PixelRect _source;
    private readonly int _dx, _dy;

    /// <summary>Moves a rectangular selection, clearing what it left behind.</summary>
    /// <param name="source">The region to move.</param>
    /// <param name="dx">Horizontal offset in pixels.</param>
    /// <param name="dy">Vertical offset in pixels.</param>
    /// <param name="opacity">Whether partial alpha is admitted on the moved pixels; locked by default.</param>
    public MoveSelection(PixelRect source, int dx, int dy, OpacityLock opacity = OpacityLock.Locked)
        : base(opacity)
    {
        _source = source;
        _dx = dx;
        _dy = dy;
    }

    /// <inheritdoc />
    protected override IReadOnlyList<(int x, int y, TPixel pixel)> ComputePixels(IEditSurface<TPixel> surface)
    {
        // Build a keyed map so a destination pixel overrides the source-clear at the same coordinate.
        var result = new Dictionary<(int, int), TPixel>();
        for (int y = _source.Y; y < _source.Y + _source.Height; y++)
            for (int x = _source.X; x < _source.X + _source.Width; x++)
            {
                if (!surface.InBounds(x, y)) continue;
                result[(x, y)] = default;   // clear source: the blank pixel, transparent on both surfaces
            }
        for (int y = _source.Y; y < _source.Y + _source.Height; y++)
            for (int x = _source.X; x < _source.X + _source.Width; x++)
            {
                if (!surface.InBounds(x, y)) continue;
                int nx = x + _dx, ny = y + _dy;
                if (!surface.InBounds(nx, ny)) continue;
                result[(nx, ny)] = surface.Get(x, y);
            }
        var pixels = new List<(int, int, TPixel)>(result.Count);
        foreach (var kv in result)
            pixels.Add((kv.Key.Item1, kv.Key.Item2, kv.Value));
        return pixels;
    }
}
