namespace Nfty.Core.Editing;

/// <summary>4-connected flood fill of the region matching the seed pixel exactly.</summary>
/// <remarks>
/// The region's extent is defined by the seed's <em>whole</em> pixel, alpha included, so a fill
/// spreads across an erased area without leaking into a painted one. The alpha it writes therefore
/// depends on what the caller hands it rather than on a brush constant, which is exactly why the
/// <see cref="OpacityLock"/> is enforced in the base and not in each command.
/// </remarks>
/// <typeparam name="TPixel">The pixel the target surface stores.</typeparam>
public sealed class FloodFill<TPixel> : RegionEditCommand<TPixel> where TPixel : struct
{
    private readonly int _seedX, _seedY;
    private readonly TPixel _fill;

    /// <summary>Fills the region matching the seed pixel.</summary>
    /// <param name="seedX">Seed column.</param>
    /// <param name="seedY">Seed row.</param>
    /// <param name="fill">The pixel to write.</param>
    /// <param name="opacity">Whether partial alpha is admitted; locked by default.</param>
    public FloodFill(int seedX, int seedY, TPixel fill, OpacityLock opacity = OpacityLock.Locked)
        : base(opacity)
    {
        _seedX = seedX;
        _seedY = seedY;
        _fill = fill;
    }

    /// <inheritdoc />
    protected override IReadOnlyList<(int x, int y, TPixel pixel)> ComputePixels(IEditSurface<TPixel> surface)
    {
        var pixels = new List<(int, int, TPixel)>();
        if (!surface.InBounds(_seedX, _seedY)) return pixels;

        var cmp = EqualityComparer<TPixel>.Default;
        TPixel target = surface.Get(_seedX, _seedY);
        // Early-out so a mis-click on an already-filled region does not enumerate the whole canvas
        // into a snapshot pair. Compared against the ADMITTED fill: under the lock a fill carrying
        // partial alpha still changes the region, so comparing the raw one would skip a real edit.
        if (cmp.Equals(target, Admit(surface, _fill))) return pixels;

        foreach (var (x, y) in Footprint.Region(surface, _seedX, _seedY)) pixels.Add((x, y, _fill));
        return pixels;
    }
}
