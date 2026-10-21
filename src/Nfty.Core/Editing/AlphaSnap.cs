namespace Nfty.Core.Editing;

/// <summary>
/// Snaps every partially transparent pixel at 50%: alpha at or above <see cref="VoxelReady.Threshold"/>
/// becomes 255, below it becomes 0. Color channels are untouched. Pixels already fully opaque or
/// fully clear are not written, so snapping clean art changes nothing and records no undo step.
/// </summary>
/// <remarks>
/// It runs <see cref="OpacityLock.Unlocked"/> because it <em>is</em> the alpha edit; the one rule for
/// "which side of 50%" is <see cref="VoxelReady.Threshold"/>, the same cutoff the opacity lock uses.
/// </remarks>
/// <typeparam name="TPixel">The pixel the target surface stores.</typeparam>
public sealed class AlphaSnap<TPixel> : RegionEditCommand<TPixel> where TPixel : struct
{
    /// <summary>Creates the snap over the whole surface.</summary>
    public AlphaSnap() : base(OpacityLock.Unlocked) { }

    /// <inheritdoc />
    protected override IReadOnlyList<(int x, int y, TPixel pixel)> ComputePixels(IEditSurface<TPixel> surface)
    {
        var changes = new List<(int x, int y, TPixel pixel)>();
        for (int y = 0; y < surface.Height; y++)
            for (int x = 0; x < surface.Width; x++)
            {
                var p = surface.Get(x, y);
                byte a = surface.AlphaOf(p);
                if (a == 0 || a == 255) continue;
                changes.Add((x, y, surface.WithAlpha(p, a >= VoxelReady.Threshold ? (byte)255 : (byte)0)));
            }
        return changes;
    }
}
