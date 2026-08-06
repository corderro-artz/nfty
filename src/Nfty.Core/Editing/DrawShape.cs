namespace Nfty.Core.Editing;

/// <summary>Fills a rectangle, inscribed ellipse, or upright triangle with a value at full alpha.</summary>
public sealed class DrawShape : RegionEditCommand
{
    private readonly ShapeKind _kind;
    private readonly PixelRect _b;
    private readonly byte _value;

    /// <summary>Draws a filled shape.</summary>
    /// <param name="kind">Which shape.</param>
    /// <param name="bounds">Its bounding box.</param>
    /// <param name="value">The value to write.</param>
    public DrawShape(ShapeKind kind, PixelRect bounds, byte value)
    {
        _kind = kind;
        _b = bounds;
        _value = value;
    }

    /// <inheritdoc />
    protected override IReadOnlyList<(int x, int y, byte value, byte alpha)> ComputePixels(ValueMap map)
    {
        var pixels = new List<(int, int, byte, byte)>();
        for (int y = _b.Y; y < _b.Y + _b.Height; y++)
            for (int x = _b.X; x < _b.X + _b.Width; x++)
            {
                if (!map.InBounds(x, y)) continue;
                if (Contains(x, y))
                    pixels.Add((x, y, _value, (byte)255));
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
