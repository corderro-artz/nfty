namespace Nfty.Core.Editing;

/// <summary>Erases to transparency — sets alpha to 0, keeping every other channel — along a path.</summary>
/// <typeparam name="TPixel">The pixel the target surface stores.</typeparam>
public sealed class EraseStroke<TPixel> : RegionEditCommand<TPixel> where TPixel : struct
{
    private readonly int _size;
    private readonly IReadOnlyList<(int x, int y)> _path;

    /// <summary>Erases along a stroke.</summary>
    /// <param name="size">Brush diameter in pixels.</param>
    /// <param name="path">The gesture's pixel path.</param>
    /// <param name="opacity">Carried for symmetry with the other commands; erase writes alpha 0,
    /// which both modes admit unchanged.</param>
    public EraseStroke(int size, IReadOnlyList<(int x, int y)> path,
        OpacityLock opacity = OpacityLock.Locked)
        : base(opacity)
    {
        _size = size;
        _path = path;
    }

    /// <inheritdoc />
    protected override IReadOnlyList<(int x, int y, TPixel pixel)> ComputePixels(IEditSurface<TPixel> surface) =>
        StampDiscs(surface, _path, _size, (x, y) => surface.WithAlpha(surface.Get(x, y), 0));
}
