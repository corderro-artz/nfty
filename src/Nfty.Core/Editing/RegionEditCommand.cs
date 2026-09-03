namespace Nfty.Core.Editing;

/// <summary>
/// Base for edits expressed as "these pixels get these new values". The new pixels are computed
/// once, before any mutation, and the prior pixels are snapshotted for undo — so redo is just Apply
/// again. Region-scoped, so history stays memory-light even on a large canvas.
/// </summary>
/// <remarks>
/// Generic over the pixel rather than duplicated per surface specifically because of the undo
/// snapshot: two copies of that could diverge silently and corrupt history rather than failing to
/// compile. It is also where the <see cref="OpacityLock"/> is enforced — on the way into
/// <c>_after</c>, so <em>every</em> command obeys, including ones that take their alpha from the
/// surface (flood fill's seed match, move's copied pixels) rather than from a brush, and including
/// any command written later that never thinks about opacity at all.
/// </remarks>
/// <typeparam name="TPixel">The pixel the target surface stores.</typeparam>
public abstract class RegionEditCommand<TPixel> : IEditCommand<TPixel> where TPixel : struct
{
    // Alpha at or above this snaps opaque under the lock, below it snaps erased. The midpoint is the
    // ordinary alpha-test cutoff; a "anything not fully clear becomes opaque" rule would turn a
    // single-unit anti-aliasing fringe into solid ink.
    private const byte LockThreshold = 128;

    private readonly OpacityLock _opacity;
    private (int x, int y, TPixel pixel)[]? _after;
    private (int x, int y, TPixel pixel)[]? _before;
    private bool _changed;

    /// <summary>Creates the edit under an opacity mode.</summary>
    /// <param name="opacity">Whether partial alpha is admitted; locked by default everywhere.</param>
    protected RegionEditCommand(OpacityLock opacity) => _opacity = opacity;

    /// <summary>Target pixels and their new values. Only in-bounds pixels, each coordinate at most
    /// once; computed before any mutation.</summary>
    /// <param name="surface">The surface being edited, for reading the pixels the edit depends on.</param>
    /// <returns>The pixels to write, before the opacity lock is applied to them.</returns>
    protected abstract IReadOnlyList<(int x, int y, TPixel pixel)> ComputePixels(IEditSurface<TPixel> surface);

    /// <summary>
    /// Stamps a filled round disc of diameter <paramref name="size"/> at each point on
    /// <paramref name="path"/>, visiting each in-bounds pixel at most once. Only the pixel a
    /// coordinate takes differs between edits — brush paints its own pixel, erase keeps what is
    /// there and drops the alpha — so <paramref name="paint"/> supplies that per pixel and the disc
    /// geometry lives here once, payload-free.
    /// </summary>
    /// <param name="surface">The surface being edited; supplies bounds and any read-back the paint needs.</param>
    /// <param name="path">The gesture's pixel path; a disc is stamped at each point.</param>
    /// <param name="size">Disc diameter in pixels; anything below 1 is treated as 1.</param>
    /// <param name="paint">What the pixel at a coordinate becomes.</param>
    /// <returns>The stamped pixels, deduplicated and clipped to the surface.</returns>
    protected static List<(int x, int y, TPixel pixel)> StampDiscs(
        IEditSurface<TPixel> surface, IReadOnlyList<(int x, int y)> path, int size,
        Func<int, int, TPixel> paint)
    {
        int d = Math.Max(1, size);
        double r = d / 2.0;
        int ir = (int)Math.Ceiling(r);
        var seen = new HashSet<(int, int)>();
        var pixels = new List<(int, int, TPixel)>();
        foreach (var (cx, cy) in path)
            for (int dy = -ir; dy <= ir; dy++)
                for (int dx = -ir; dx <= ir; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (!surface.InBounds(x, y)) continue;
                    if (dx * dx + dy * dy > r * r) continue; // round disc; size 1 (r=0.5) collapses to one pixel
                    if (!seen.Add((x, y))) continue;
                    pixels.Add((x, y, paint(x, y)));
                }
        return pixels;
    }

    /// <summary>
    /// The pixel as this edit is actually allowed to write it: unchanged when unlocked, alpha
    /// snapped to 255 or 0 when locked. <see cref="Apply"/> puts every computed pixel through this,
    /// so a subclass never has to; it is exposed only so a command can ask "would this write change
    /// anything?" before doing expensive work, and it is idempotent, so asking twice is harmless.
    /// </summary>
    /// <param name="surface">The surface being edited; knows where its pixel keeps its alpha.</param>
    /// <param name="pixel">The pixel the edit computed.</param>
    /// <returns>The pixel the opacity mode admits.</returns>
    protected TPixel Admit(IEditSurface<TPixel> surface, TPixel pixel) =>
        _opacity == OpacityLock.Unlocked
            ? pixel
            : surface.WithAlpha(pixel, surface.AlphaOf(pixel) >= LockThreshold ? (byte)255 : (byte)0);

    /// <summary>Applies the edit, snapshotting the pixels it is about to overwrite so
    /// <see cref="Undo"/> can put them back.</summary>
    /// <param name="surface">The surface to edit.</param>
    /// <returns>True when the edit changed anything.</returns>
    public bool Apply(IEditSurface<TPixel> surface)
    {
        if (_after is null)
        {
            var px = ComputePixels(surface);
            var after = new (int x, int y, TPixel pixel)[px.Count];
            var before = new (int x, int y, TPixel pixel)[px.Count];
            bool changed = false;
            for (int i = 0; i < px.Count; i++)
            {
                var (x, y, p) = px[i];
                TPixel admitted = Admit(surface, p);
                after[i] = (x, y, admitted);
                // Snapshotted raw: undo restores exactly what was there, partial alpha included. The
                // lock governs what is painted, never what is put back.
                before[i] = (x, y, surface.Get(x, y));
                // A stamped pixel already holding what it would be given is not a change.
                if (!EqualityComparer<TPixel>.Default.Equals(before[i].pixel, admitted)) changed = true;
            }
            _after = after;
            _before = before;
            _changed = changed;
        }
        foreach (var (x, y, p) in _after)
            surface.Set(x, y, p);
        return _changed;
    }

    /// <summary>Restores the snapshot taken by <see cref="Apply"/>.</summary>
    /// <param name="surface">The surface to restore.</param>
    public void Undo(IEditSurface<TPixel> surface)
    {
        if (_before is null) return;
        foreach (var (x, y, p) in _before)
            surface.Set(x, y, p);
    }
}
