namespace Nfty.Core.Editing;

/// <summary>Erases to transparency — sets alpha to 0, keeping each pixel's value — along a path.</summary>
public sealed class EraseStroke : RegionEditCommand
{
    private readonly int _size;
    private readonly IReadOnlyList<(int x, int y)> _path;

    public EraseStroke(int size, IReadOnlyList<(int x, int y)> path)
    {
        _size = size;
        _path = path;
    }

    protected override IReadOnlyList<(int x, int y, byte value, byte alpha)> ComputePixels(ValueMap map) =>
        StampDiscs(map, _path, _size, (x, y) => (map.GetValue(x, y), (byte)0));
}
