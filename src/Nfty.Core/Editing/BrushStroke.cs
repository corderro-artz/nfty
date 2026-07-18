namespace Nfty.Core.Editing;

/// <summary>Paints the brush's value (at full alpha) as a filled disc stamped along a path.</summary>
public sealed class BrushStroke : RegionEditCommand
{
    private readonly Brush _brush;
    private readonly IReadOnlyList<(int x, int y)> _path;

    public BrushStroke(Brush brush, IReadOnlyList<(int x, int y)> path)
    {
        _brush = brush;
        _path = path;
    }

    protected override IReadOnlyList<(int x, int y, byte value, byte alpha)> ComputePixels(ValueMap map)
    {
        int d = Math.Max(1, _brush.Size);
        double r = d / 2.0;
        int ir = (int)Math.Ceiling(r);
        var seen = new HashSet<(int, int)>();
        var pixels = new List<(int, int, byte, byte)>();
        foreach (var (cx, cy) in _path)
            for (int dy = -ir; dy <= ir; dy++)
                for (int dx = -ir; dx <= ir; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (!map.InBounds(x, y)) continue;
                    if (dx * dx + dy * dy > r * r) continue; // round disc; size 1 (r=0.5) collapses to one pixel
                    if (seen.Add((x, y)))
                        pixels.Add((x, y, _brush.Value, (byte)255));
                }
        return pixels;
    }
}
