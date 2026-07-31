namespace Nfty.Core.Editing;

/// <summary>
/// Base for edits expressed as "these pixels get these new (value, alpha)". The new pixels are
/// computed once, before any mutation, and the prior pixels are snapshotted for undo — so redo is
/// just Apply again. Region-scoped, so history stays memory-light even on a large canvas.
/// </summary>
public abstract class RegionEditCommand : IEditCommand
{
    private (int x, int y, byte v, byte a)[]? _after;
    private (int x, int y, byte v, byte a)[]? _before;
    private bool _changed;

    /// <summary>Target pixels and their new (value, alpha). Only in-bounds pixels; computed before mutation.</summary>
    protected abstract IReadOnlyList<(int x, int y, byte value, byte alpha)> ComputePixels(ValueMap map);

    /// <summary>
    /// Stamps a filled round disc of diameter <paramref name="size"/> at each point on
    /// <paramref name="path"/>, visiting each in-bounds pixel at most once. Only the (value, alpha)
    /// a pixel takes differs between edits — brush paints a fixed value at full alpha, erase keeps
    /// the value and drops alpha to zero — so <paramref name="paint"/> supplies that per pixel and
    /// the disc geometry lives here once.
    /// </summary>
    protected static List<(int x, int y, byte value, byte alpha)> StampDiscs(
        ValueMap map, IReadOnlyList<(int x, int y)> path, int size,
        Func<int, int, (byte value, byte alpha)> paint)
    {
        int d = Math.Max(1, size);
        double r = d / 2.0;
        int ir = (int)Math.Ceiling(r);
        var seen = new HashSet<(int, int)>();
        var pixels = new List<(int, int, byte, byte)>();
        foreach (var (cx, cy) in path)
            for (int dy = -ir; dy <= ir; dy++)
                for (int dx = -ir; dx <= ir; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (!map.InBounds(x, y)) continue;
                    if (dx * dx + dy * dy > r * r) continue; // round disc; size 1 (r=0.5) collapses to one pixel
                    if (!seen.Add((x, y))) continue;
                    var (value, alpha) = paint(x, y);
                    pixels.Add((x, y, value, alpha));
                }
        return pixels;
    }

    public bool Apply(ValueMap map)
    {
        if (_after is null)
        {
            var px = ComputePixels(map);
            var after = new (int, int, byte, byte)[px.Count];
            var before = new (int, int, byte, byte)[px.Count];
            bool changed = false;
            for (int i = 0; i < px.Count; i++)
            {
                var (x, y, v, a) = px[i];
                after[i] = (x, y, v, a);
                before[i] = (x, y, map.GetValue(x, y), map.GetAlpha(x, y));
                if (before[i] != after[i]) changed = true; // a stamped pixel already at (v,a) is not a change
            }
            _after = after;
            _before = before;
            _changed = changed;
        }
        foreach (var (x, y, v, a) in _after)
            map.Set(x, y, v, a);
        return _changed;
    }

    public void Undo(ValueMap map)
    {
        if (_before is null) return;
        foreach (var (x, y, v, a) in _before)
            map.Set(x, y, v, a);
    }
}
