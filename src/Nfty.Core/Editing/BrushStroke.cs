namespace Nfty.Core.Editing;

/// <summary>Paints the brush's value (at full alpha) as a filled disc stamped along a path.</summary>
public sealed class BrushStroke : RegionEditCommand
{
    private readonly Brush _brush;
    private readonly IReadOnlyList<(int x, int y)> _path;

    /// <summary>Paints a stroke.</summary>
    /// <param name="brush">Size and value of the brush.</param>
    /// <param name="path">The gesture's pixel path.</param>
    public BrushStroke(Brush brush, IReadOnlyList<(int x, int y)> path)
    {
        _brush = brush;
        _path = path;
    }

    /// <inheritdoc />
    protected override IReadOnlyList<(int x, int y, byte value, byte alpha)> ComputePixels(ValueMap map) =>
        StampDiscs(map, _path, _brush.Size, (_, _) => (_brush.Value, (byte)255));
}
