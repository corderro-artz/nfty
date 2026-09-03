namespace Nfty.Core.Editing;

/// <summary>Paints the brush's pixel as a filled disc stamped along a path.</summary>
/// <typeparam name="TPixel">The pixel the target surface stores.</typeparam>
public sealed class BrushStroke<TPixel> : RegionEditCommand<TPixel> where TPixel : struct
{
    private readonly Brush<TPixel> _brush;
    private readonly IReadOnlyList<(int x, int y)> _path;

    /// <summary>Paints a stroke.</summary>
    /// <param name="brush">Size and payload of the brush.</param>
    /// <param name="path">The gesture's pixel path.</param>
    /// <param name="opacity">Whether partial alpha is admitted; locked by default.</param>
    public BrushStroke(Brush<TPixel> brush, IReadOnlyList<(int x, int y)> path,
        OpacityLock opacity = OpacityLock.Locked)
        : base(opacity)
    {
        _brush = brush;
        _path = path;
    }

    /// <inheritdoc />
    protected override IReadOnlyList<(int x, int y, TPixel pixel)> ComputePixels(IEditSurface<TPixel> surface) =>
        StampDiscs(surface, _path, _brush.Size, (_, _) => _brush.Pixel);
}
