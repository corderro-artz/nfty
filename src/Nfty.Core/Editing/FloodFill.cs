namespace Nfty.Core.Editing;

/// <summary>4-connected flood fill of the region matching the seed pixel's (value, alpha).</summary>
public sealed class FloodFill : RegionEditCommand
{
    private readonly int _seedX, _seedY;
    private readonly byte _value;

    public FloodFill(int seedX, int seedY, byte value)
    {
        _seedX = seedX;
        _seedY = seedY;
        _value = value;
    }

    protected override IReadOnlyList<(int x, int y, byte value, byte alpha)> ComputePixels(ValueMap map)
    {
        var pixels = new List<(int, int, byte, byte)>();
        if (!map.InBounds(_seedX, _seedY)) return pixels;

        byte tv = map.GetValue(_seedX, _seedY), ta = map.GetAlpha(_seedX, _seedY);
        if (tv == _value && ta == 255) return pixels; // no-op fill

        var seen = new HashSet<(int, int)>();
        var queue = new Queue<(int, int)>();
        queue.Enqueue((_seedX, _seedY));
        seen.Add((_seedX, _seedY));
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            if (!map.InBounds(x, y)) continue;
            if (map.GetValue(x, y) != tv || map.GetAlpha(x, y) != ta) continue;
            pixels.Add((x, y, _value, (byte)255));
            foreach (var (nx, ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
                if (map.InBounds(nx, ny) && seen.Add((nx, ny)))
                    queue.Enqueue((nx, ny));
        }
        return pixels;
    }
}
