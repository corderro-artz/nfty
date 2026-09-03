namespace Nfty.Core.Editing;

/// <summary>Fills a rectangle, inscribed ellipse, or upright triangle with one pixel.</summary>
/// <typeparam name="TPixel">The pixel the target surface stores.</typeparam>
public sealed class DrawShape<TPixel> : RegionEditCommand<TPixel> where TPixel : struct
{
    private readonly ShapeKind _kind;
    private readonly PixelRect _b;
    private readonly TPixel _pixel;

    /// <summary>Draws a filled shape.</summary>
    /// <param name="kind">Which shape.</param>
    /// <param name="bounds">Its bounding box.</param>
    /// <param name="pixel">The pixel to write.</param>
    /// <param name="opacity">Whether partial alpha is admitted; locked by default.</param>
    public DrawShape(ShapeKind kind, PixelRect bounds, TPixel pixel,
        OpacityLock opacity = OpacityLock.Locked)
        : base(opacity)
    {
        _kind = kind;
        _b = bounds;
        _pixel = pixel;
    }

    /// <inheritdoc />
    protected override IReadOnlyList<(int x, int y, TPixel pixel)> ComputePixels(IEditSurface<TPixel> surface)
    {
        var pixels = new List<(int, int, TPixel)>();
        for (int y = _b.Y; y < _b.Y + _b.Height; y++)
            for (int x = _b.X; x < _b.X + _b.Width; x++)
            {
                if (!surface.InBounds(x, y)) continue;
                if (Contains(x, y))
                    pixels.Add((x, y, _pixel));
            }
        return pixels;
    }

    private bool Contains(int x, int y)
    {
        switch (_kind)
        {
            case ShapeKind.Rectangle:
                return true;
            case ShapeKind.Ellipse:
            {
                double rx = _b.Width / 2.0, ry = _b.Height / 2.0;
                double cx = _b.X + rx - 0.5, cy = _b.Y + ry - 0.5;
                double nx = rx == 0 ? 0 : (x - cx) / rx, ny = ry == 0 ? 0 : (y - cy) / ry;
                return nx * nx + ny * ny <= 1.0;
            }
            case ShapeKind.Triangle:
            {
                // Upright: apex at top-center, base along the bottom edge. Half-width grows toward the base.
                double t = _b.Height <= 1 ? 1 : (y - _b.Y) / (double)(_b.Height - 1);
                double halfW = t * (_b.Width / 2.0);
                double cx = _b.X + _b.Width / 2.0 - 0.5;
                return Math.Abs(x - cx) <= halfW;
            }
            default:
                return false;
        }
    }
}
