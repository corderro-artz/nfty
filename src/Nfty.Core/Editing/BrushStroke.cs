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

    protected override IReadOnlyList<(int x, int y, byte value, byte alpha)> ComputePixels(ValueMap map) =>
        StampDiscs(map, _path, _brush.Size, (_, _) => (_brush.Value, (byte)255));
}
