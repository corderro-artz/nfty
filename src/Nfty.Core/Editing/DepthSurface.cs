using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Editing;

/// <summary>One pixel's depth: how far it stands in front of, and behind, the paper plane.</summary>
/// <param name="Front">Depth toward the viewer, 0..255.</param>
/// <param name="Back">Depth away from the viewer, 0..255.</param>
public readonly record struct DepthPixel(byte Front, byte Back);

/// <summary>Which side of the paper a depth edit raises or lowers.</summary>
public enum DepthSide
{
    /// <summary>Toward the viewer.</summary>
    Front,

    /// <summary>Away from the viewer.</summary>
    Back,
}

/// <summary>What a whole depth map looks like at a glance.</summary>
public enum DepthLook
{
    /// <summary>No solid pixel, so no depth to speak of.</summary>
    Empty,

    /// <summary>Every solid pixel is 0 front, 0 back: flat paper.</summary>
    Paper,

    /// <summary>Every solid pixel carries the same non-zero depth.</summary>
    Uniform,

    /// <summary>Solid pixels differ.</summary>
    Varied,
}

/// <summary>A depth map's look, with the shared depth when it is uniform.</summary>
/// <param name="Look">The overall look.</param>
/// <param name="Front">The shared front depth for <see cref="DepthLook.Uniform"/>, else 0.</param>
/// <param name="Back">The shared back depth for <see cref="DepthLook.Uniform"/>, else 0.</param>
public readonly record struct DepthSummary(DepthLook Look, byte Front, byte Back);

/// <summary>How many solid pixels share one total thickness.</summary>
/// <param name="Total">Thickness in voxel units: <c>1 + front + back</c>.</param>
/// <param name="Count">Solid pixels with that thickness.</param>
public readonly record struct ThicknessBucket(int Total, int Count);

/// <summary>
/// The surface the depth tools edit: one <see cref="DepthPixel"/> per art pixel, held beside a copy
/// of the art that says which pixels are solid and which share a color.
/// </summary>
/// <remarks>
/// Depth belongs to solid pixels only — a transparent or partially transparent pixel has no body to
/// give thickness to — so <see cref="Set"/> ignores them and a stored map's bytes under them are
/// dropped on load. The alpha members of <see cref="IEditSurface{TPixel}"/> are inert (ruling 1): a
/// depth pixel has no alpha, so <see cref="AlphaOf"/> is always opaque and <see cref="WithAlpha"/>
/// is the identity, which keeps the opacity lock a no-op here rather than a rule to keep in step.
/// </remarks>
public sealed class DepthSurface : IEditSurface<DepthPixel>
{
    private readonly bool[] _solid;
    private readonly byte[] _depth;
    private readonly ColorMap _art;

    /// <summary>Width in pixels.</summary>
    public int Width { get; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; }

    /// <summary>Pixels that are fully opaque, and so carry depth.</summary>
    public int SolidCount { get; }

    /// <summary>Pixels with partial alpha, which cannot be risen into a solid.</summary>
    public int PartialCount { get; }

    /// <summary>Builds the surface for an image and the depth it already carries.</summary>
    /// <param name="art">The finished art; copied, so the caller keeps ownership.</param>
    /// <param name="stored">The layer's stored depth, or null for flat paper.</param>
    /// <exception cref="ArgumentException"><paramref name="stored"/> is another size than the art.</exception>
    public DepthSurface(Image<Rgba32> art, DepthMap? stored)
    {
        ArgumentNullException.ThrowIfNull(art);
        if (stored is not null && (stored.Width != art.Width || stored.Height != art.Height))
            throw new ArgumentException(
                $"Depth map is {stored.Width}x{stored.Height} but the art is {art.Width}x{art.Height}.",
                nameof(stored));
        Width = art.Width;
        Height = art.Height;
        _art = ColorMap.FromImage(art);
        _solid = new bool[Width * Height];
        _depth = new byte[Width * Height * 2];
        int solid = 0, partial = 0;
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                var p = _art.Get(x, y);
                int i = y * Width + x;
                if (Opacity.IsSolid(p))
                {
                    _solid[i] = true;
                    solid++;
                    if (stored is not null)
                    {
                        _depth[i * 2] = stored.Front(x, y);
                        _depth[i * 2 + 1] = stored.Back(x, y);
                    }
                }
                else if (Opacity.IsPartial(p)) partial++;
            }
        SolidCount = solid;
        PartialCount = partial;
    }

    /// <inheritdoc />
    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    /// <summary>Whether a pixel is fully opaque. Out of bounds is not solid.</summary>
    /// <param name="x">Column.</param>
    /// <param name="y">Row.</param>
    /// <returns>True for a solid pixel.</returns>
    public bool IsSolid(int x, int y) => InBounds(x, y) && _solid[y * Width + x];

    /// <summary>The art pixel at a coordinate, in bounds.</summary>
    /// <param name="x">Column.</param>
    /// <param name="y">Row.</param>
    /// <returns>The art color.</returns>
    public Rgba32 ArtAt(int x, int y) => _art.Get(x, y);

    /// <inheritdoc />
    public DepthPixel Get(int x, int y)
    {
        int i = (y * Width + x) * 2;
        return new DepthPixel(_depth[i], _depth[i + 1]);
    }

    /// <summary>Writes a pixel's depth. Out-of-bounds and non-solid pixels are ignored: transparent
    /// pixels have no depth.</summary>
    /// <param name="x">Column.</param>
    /// <param name="y">Row.</param>
    /// <param name="pixel">The depth to store.</param>
    public void Set(int x, int y, DepthPixel pixel)
    {
        if (!IsSolid(x, y)) return;
        int i = (y * Width + x) * 2;
        _depth[i] = pixel.Front;
        _depth[i + 1] = pixel.Back;
    }

    /// <inheritdoc />
    public byte AlphaOf(DepthPixel pixel) => 255;

    /// <inheritdoc />
    public DepthPixel WithAlpha(DepthPixel pixel, byte alpha) => pixel;

    /// <summary>The 4-connected run of solid pixels with exactly the seed's art color.</summary>
    /// <param name="x">Seed column.</param>
    /// <param name="y">Seed row.</param>
    /// <returns>The region; empty when the seed is not solid.</returns>
    public List<(int x, int y)> SameColorRegion(int x, int y) =>
        IsSolid(x, y) ? Footprint.Region(_art, x, y) : new List<(int x, int y)>();

    /// <summary>Replaces the whole depth with a stored map, in place: every solid pixel takes the
    /// map's pair, non-solid pixels stay 0. The instance is unchanged, so references to it stay valid.</summary>
    /// <param name="map">The depth to take, or null for flat paper (0/0 everywhere).</param>
    /// <exception cref="ArgumentException"><paramref name="map"/> is another size than the surface.</exception>
    public void Overwrite(DepthMap? map)
    {
        if (map is not null && (map.Width != Width || map.Height != Height))
            throw new ArgumentException(
                $"Depth map is {map.Width}x{map.Height} but the surface is {Width}x{Height}.", nameof(map));
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int i = y * Width + x;
                bool take = map is not null && _solid[i];
                _depth[i * 2] = take ? map!.Front(x, y) : (byte)0;
                _depth[i * 2 + 1] = take ? map!.Back(x, y) : (byte)0;
            }
    }

    /// <summary>The depth as a map to store.</summary>
    /// <returns>Null when every solid pixel is 0 front and 0 back, so flat has one spelling: absent.</returns>
    public DepthMap? ToDepthMap() =>
        _depth.AsSpan().IndexOfAnyExcept((byte)0) < 0 ? null : DepthMap.FromBytes(Width, Height, _depth);

    /// <summary>Whether this surface's depth equals a stored map, absent counting as all zero.</summary>
    /// <param name="other">The stored map, or null for flat.</param>
    /// <returns>True when the bytes agree.</returns>
    public bool SameDepthAs(DepthMap? other)
    {
        if (other is null) return _depth.AsSpan().IndexOfAnyExcept((byte)0) < 0;
        return other.Width == Width && other.Height == Height && other.ToBytes().AsSpan().SequenceEqual(_depth);
    }

    /// <summary>Solid pixels counted by total thickness (<c>1 + front + back</c>).</summary>
    /// <returns>Buckets ascending by thickness.</returns>
    public IReadOnlyList<ThicknessBucket> Histogram()
    {
        var counts = new SortedDictionary<int, int>();
        for (int i = 0; i < _solid.Length; i++)
        {
            if (!_solid[i]) continue;
            int total = 1 + _depth[i * 2] + _depth[i * 2 + 1];
            counts[total] = counts.GetValueOrDefault(total) + 1;
        }
        return counts.Select(kv => new ThicknessBucket(kv.Key, kv.Value)).ToList();
    }

    /// <summary>What the depth looks like overall.</summary>
    /// <returns>Empty, paper, uniform (with its depth) or varied.</returns>
    public DepthSummary Summary()
    {
        if (SolidCount == 0) return new DepthSummary(DepthLook.Empty, 0, 0);
        DepthPixel? first = null;
        bool uniform = true;
        for (int i = 0; i < _solid.Length && uniform; i++)
        {
            if (!_solid[i]) continue;
            var p = new DepthPixel(_depth[i * 2], _depth[i * 2 + 1]);
            if (first is null) first = p;
            else if (first.Value != p) uniform = false;
        }
        if (!uniform) return new DepthSummary(DepthLook.Varied, 0, 0);
        var f = first!.Value;
        return f is { Front: 0, Back: 0 }
            ? new DepthSummary(DepthLook.Paper, 0, 0)
            : new DepthSummary(DepthLook.Uniform, f.Front, f.Back);
    }
}
