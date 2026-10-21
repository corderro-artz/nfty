namespace Nfty.Core.Editing;

/// <summary>
/// Raises or lowers one side of the depth of every solid pixel a gesture touches, by exactly one.
/// </summary>
/// <remarks>
/// A pixel crossed several times by a stroke steps once: the footprint is deduplicated, so the result
/// does not depend on pointer sample rate. Only pixels whose value actually changes are emitted, so a
/// gesture over transparent pixels or against a floor/ceiling is not an undo entry.
/// </remarks>
public sealed class DepthStep : RegionEditCommand<DepthPixel>
{
    private readonly (int x, int y)[] _pixels;
    private readonly DepthSide _side;
    private readonly int _delta;

    /// <summary>Creates the step.</summary>
    /// <param name="pixels">The gesture's footprint; copied.</param>
    /// <param name="side">Which side of the paper to move.</param>
    /// <param name="delta">+1 to raise, -1 to lower.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="delta"/> is not 1 or -1, or
    /// <paramref name="side"/> is undefined.</exception>
    public DepthStep(IReadOnlyCollection<(int x, int y)> pixels, DepthSide side, int delta)
        : base(OpacityLock.Unlocked)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (delta is not (1 or -1)) throw new ArgumentOutOfRangeException(nameof(delta), "A depth step is +1 or -1.");
        if (!Enum.IsDefined(side)) throw new ArgumentOutOfRangeException(nameof(side));
        _pixels = pixels.ToArray();
        _side = side;
        _delta = delta;
    }

    /// <inheritdoc />
    protected override IReadOnlyList<(int x, int y, DepthPixel pixel)> ComputePixels(IEditSurface<DepthPixel> surface)
    {
        if (surface is not DepthSurface depth)
            throw new ArgumentException("A depth step edits a DepthSurface.", nameof(surface));
        var seen = new HashSet<(int, int)>();
        var result = new List<(int x, int y, DepthPixel pixel)>();
        foreach (var (x, y) in _pixels)
        {
            if (!depth.IsSolid(x, y)) continue;
            if (!seen.Add((x, y))) continue;
            var old = depth.Get(x, y);
            byte current = _side == DepthSide.Front ? old.Front : old.Back;
            byte next = (byte)Math.Clamp(current + _delta, 0, 255);
            if (next == current) continue;
            result.Add((x, y, _side == DepthSide.Front ? old with { Front = next } : old with { Back = next }));
        }
        return result;
    }
}
