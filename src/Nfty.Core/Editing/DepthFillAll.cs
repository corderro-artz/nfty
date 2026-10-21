namespace Nfty.Core.Editing;

/// <summary>Sets every solid pixel to one front/back depth, replacing whatever was there.</summary>
public sealed class DepthFillAll : RegionEditCommand<DepthPixel>
{
    private readonly DepthPixel _depth;

    /// <summary>Creates the fill.</summary>
    /// <param name="front">Front depth to give every solid pixel.</param>
    /// <param name="back">Back depth to give every solid pixel.</param>
    public DepthFillAll(byte front, byte back)
        : base(OpacityLock.Unlocked) => _depth = new DepthPixel(front, back);

    /// <inheritdoc />
    protected override IReadOnlyList<(int x, int y, DepthPixel pixel)> ComputePixels(IEditSurface<DepthPixel> surface)
    {
        if (surface is not DepthSurface depth)
            throw new ArgumentException("A depth fill edits a DepthSurface.", nameof(surface));
        var result = new List<(int x, int y, DepthPixel pixel)>();
        for (int y = 0; y < depth.Height; y++)
            for (int x = 0; x < depth.Width; x++)
                if (depth.IsSolid(x, y) && depth.Get(x, y) != _depth) result.Add((x, y, _depth));
        return result;
    }
}
