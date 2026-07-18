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

    /// <summary>Target pixels and their new (value, alpha). Only in-bounds pixels; computed before mutation.</summary>
    protected abstract IReadOnlyList<(int x, int y, byte value, byte alpha)> ComputePixels(ValueMap map);

    public void Apply(ValueMap map)
    {
        if (_after is null)
        {
            var px = ComputePixels(map);
            var after = new (int, int, byte, byte)[px.Count];
            var before = new (int, int, byte, byte)[px.Count];
            for (int i = 0; i < px.Count; i++)
            {
                var (x, y, v, a) = px[i];
                after[i] = (x, y, v, a);
                before[i] = (x, y, map.GetValue(x, y), map.GetAlpha(x, y));
            }
            _after = after;
            _before = before;
        }
        foreach (var (x, y, v, a) in _after)
            map.Set(x, y, v, a);
    }

    public void Undo(ValueMap map)
    {
        if (_before is null) return;
        foreach (var (x, y, v, a) in _before)
            map.Set(x, y, v, a);
    }
}
