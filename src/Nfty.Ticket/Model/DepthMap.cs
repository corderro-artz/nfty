namespace Nfty.Core.Model;

/// <summary>
/// A per-pixel front/back depth pair for one layer's artwork, so a 2D asset can be lifted into a
/// scene with real parallax. Two bytes per pixel — <c>front</c> then <c>back</c>, each 0..255 — laid
/// out row-major with the top row first and, within a row, left to right: pixel <c>(x, y)</c> lives
/// at byte offset <c>(y * Width + x) * 2</c>, front then back. A byte cannot spell more than 255
/// levels of depth; that is the whole precision this format carries.
/// </summary>
public sealed class DepthMap
{
    private readonly byte[] _bytes;

    /// <summary>The map's width in pixels.</summary>
    public int Width { get; }

    /// <summary>The map's height in pixels.</summary>
    public int Height { get; }

    /// <summary>Whether every byte is zero — a map nobody has painted, front and back both at the
    /// surface for every pixel. Computed once when the map is built, never lazily, so it is always
    /// consistent with the bytes it describes.</summary>
    public bool IsFlat { get; }

    private DepthMap(int width, int height, byte[] bytes)
    {
        Width = width;
        Height = height;
        _bytes = bytes;
        IsFlat = Array.TrueForAll(_bytes, b => b == 0);
    }

    /// <summary>Builds a flat map — every pixel's front and back at zero — for a canvas of the given
    /// size.</summary>
    /// <param name="width">Width in pixels; must be positive.</param>
    /// <param name="height">Height in pixels; must be positive.</param>
    /// <returns>A new, all-zero <see cref="DepthMap"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Either dimension is not positive.</exception>
    /// <exception cref="OverflowException">The byte count exceeds <see cref="int.MaxValue"/>.</exception>
    public static DepthMap Flat(int width, int height)
    {
        Guard.Positive(width, nameof(width));
        Guard.Positive(height, nameof(height));
        return new DepthMap(width, height, new byte[checked(width * height * 2)]);
    }

    /// <summary>Builds a map from its raw bytes, copying them so the caller cannot mutate the map
    /// afterwards.</summary>
    /// <param name="width">Width in pixels; must be positive.</param>
    /// <param name="height">Height in pixels; must be positive.</param>
    /// <param name="bytes">Front/back pairs, row-major, top row first — see the type summary for the
    /// exact layout. Must be exactly <c>width * height * 2</c> bytes long.</param>
    /// <returns>A new <see cref="DepthMap"/> holding a copy of <paramref name="bytes"/>.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="bytes"/> is not exactly <c>width * height * 2</c> bytes long.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">Either dimension is not positive.</exception>
    /// <exception cref="OverflowException">The byte count exceeds <see cref="int.MaxValue"/>.</exception>
    public static DepthMap FromBytes(int width, int height, ReadOnlySpan<byte> bytes)
    {
        Guard.Positive(width, nameof(width));
        Guard.Positive(height, nameof(height));
        int expected = checked(width * height * 2);
        if (bytes.Length != expected)
            throw new ArgumentException(
                $"A {width}x{height} depth map is {expected} bytes; got {bytes.Length}.");
        return new DepthMap(width, height, bytes.ToArray());
    }

    private int IndexOf(int x, int y)
    {
        if ((uint)x >= (uint)Width) throw new ArgumentOutOfRangeException(nameof(x));
        if ((uint)y >= (uint)Height) throw new ArgumentOutOfRangeException(nameof(y));
        return (y * Width + x) * 2;
    }

    /// <summary>The front depth (0..255) at a pixel.</summary>
    /// <param name="x">Column in <c>[0, Width)</c>.</param>
    /// <param name="y">Row in <c>[0, Height)</c>.</param>
    /// <returns>The pixel's front depth, 0 for flat paper.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Either coordinate is outside the map.</exception>
    public byte Front(int x, int y) => _bytes[IndexOf(x, y)];

    /// <summary>The back depth (0..255) at a pixel.</summary>
    /// <param name="x">Column in <c>[0, Width)</c>.</param>
    /// <param name="y">Row in <c>[0, Height)</c>.</param>
    /// <returns>The pixel's back depth, 0 for flat paper.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Either coordinate is outside the map.</exception>
    public byte Back(int x, int y) => _bytes[IndexOf(x, y) + 1];

    /// <summary>A copy of the map's raw bytes, in the same front/back row-major layout described on
    /// the type.</summary>
    /// <returns>A new array; mutating it does not affect this map.</returns>
    public byte[] ToBytes() => (byte[])_bytes.Clone();

    /// <summary>Returns a new map with one pixel's front and back depth replaced, leaving this map
    /// unchanged.</summary>
    /// <param name="x">Column in <c>[0, Width)</c>.</param>
    /// <param name="y">Row in <c>[0, Height)</c>.</param>
    /// <param name="front">The new front depth.</param>
    /// <param name="back">The new back depth.</param>
    /// <returns>A new <see cref="DepthMap"/> with the one pixel changed.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Either coordinate is outside the map.</exception>
    public DepthMap With(int x, int y, byte front, byte back)
    {
        int i = IndexOf(x, y);
        var next = (byte[])_bytes.Clone();
        next[i] = front;
        next[i + 1] = back;
        return new DepthMap(Width, Height, next);
    }
}

/// <summary>Helpers for collections of <see cref="DepthMap"/>s.</summary>
public static class DepthMaps
{
    /// <summary>A shared, empty depth-map dictionary — for an ingredient or a recipe that carries no
    /// depth data at all.</summary>
    public static IReadOnlyDictionary<string, DepthMap> None { get; } =
        new Dictionary<string, DepthMap>();
}
