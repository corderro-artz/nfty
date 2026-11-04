using System.Numerics;
using System.Runtime.InteropServices;
using Nfty.Core.Diagnostics;

namespace Nfty.Model;

/// <summary>The four side walls a column can expose.</summary>
internal enum WallFace
{
    /// <summary>Toward x − 1.</summary>
    Left,

    /// <summary>Toward x + 1.</summary>
    Right,

    /// <summary>Toward image row y − 1, which is world +Y.</summary>
    Top,

    /// <summary>Toward image row y + 1, which is world −Y.</summary>
    Bottom,
}

/// <summary>
/// Accumulates quads in voxel units and turns them into a <see cref="Mesh"/>. Every corner list here
/// is counter-clockwise seen from outside — checked once by hand in the plan, and by
/// <c>RiserSolidityTests</c> on every build.
/// </summary>
internal sealed class MeshBuilder
{
    private static readonly Vector3 PlusX = new(1, 0, 0), MinusX = new(-1, 0, 0);
    private static readonly Vector3 PlusY = new(0, 1, 0), MinusY = new(0, -1, 0);
    private static readonly Vector3 PlusZ = new(0, 0, 1), MinusZ = new(0, 0, -1);

    private readonly int _width;
    private readonly int _height;
    private readonly List<float> _positions = [];
    private readonly List<float> _normals = [];
    private readonly List<float> _texCoords = [];
    private readonly List<uint> _indices = [];
    private readonly Dictionary<VertexKey, uint> _seen;

    /// <summary>A vertex's eight floats as bit patterns: equal keys are bit-identical vertices.</summary>
    private readonly struct VertexKey : IEquatable<VertexKey>
    {
        private readonly uint px, py, pz, nx, ny, nz, u, v;

        public VertexKey(uint px, uint py, uint pz, uint nx, uint ny, uint nz, uint u, uint v)
        {
            this.px = px; this.py = py; this.pz = pz;
            this.nx = nx; this.ny = ny; this.nz = nz;
            this.u = u; this.v = v;
        }

        public bool Equals(VertexKey o) =>
            px == o.px && py == o.py && pz == o.pz && nx == o.nx && ny == o.ny && nz == o.nz && u == o.u && v == o.v;

        public override bool Equals(object? obj) => obj is VertexKey k && Equals(k);

        // Cheap multiplicative mix; deterministic (no per-process seed), though only equality matters
        // for the output — vertex numbers come from first-seen order, never from hash order.
        public override int GetHashCode()
        {
            ulong h = px;
            h = (h * 0x9E3779B97F4A7C15UL) ^ py;
            h = (h * 0x9E3779B97F4A7C15UL) ^ pz;
            h = (h * 0x9E3779B97F4A7C15UL) ^ (((ulong)nx << 32) | ny);
            h = (h * 0x9E3779B97F4A7C15UL) ^ (((ulong)nz << 32) | u);
            h = (h * 0x9E3779B97F4A7C15UL) ^ v;
            h *= 0x9E3779B97F4A7C15UL;
            return (int)(h >> 32);
        }
    }

    /// <summary>Starts a mesh over a <paramref name="width"/> × <paramref name="height"/> image.</summary>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="expectedVertices">A size hint for the vertex table; never a limit.</param>
    public MeshBuilder(int width, int height, int expectedVertices = 1024)
    {
        _seen = new(Math.Max(expectedVertices, 16));
        _width = width;
        _height = height;
    }

    /// <summary>A front face over a merged rectangle, at z = 0.5 + its key.</summary>
    public void FrontFace(PixelRect r)
    {
        float x0 = r.X, x1 = r.X + r.Width, ylo = _height - (r.Y + r.Height), yhi = _height - r.Y;
        float z = 0.5f + r.Key;
        Quad(new(x0, ylo, z), new(x1, ylo, z), new(x1, yhi, z), new(x0, yhi, z), PlusZ,
            FrontUv(x0, ylo), FrontUv(x1, ylo), FrontUv(x1, yhi), FrontUv(x0, yhi));
    }

    /// <summary>A back face over a merged rectangle, at z = −(0.5 + its key).</summary>
    public void BackFace(PixelRect r)
    {
        float x0 = r.X, x1 = r.X + r.Width, ylo = _height - (r.Y + r.Height), yhi = _height - r.Y;
        float z = -(0.5f + r.Key);
        Quad(new(x0, ylo, z), new(x0, yhi, z), new(x1, yhi, z), new(x1, ylo, z), MinusZ,
            BackUv(x0, ylo), BackUv(x0, yhi), BackUv(x1, yhi), BackUv(x1, ylo));
    }

    /// <summary>One exposed wall segment of pixel (x, y), spanning z0 &lt; z1. Every corner samples
    /// the pixel's center in the front half of the atlas, or in the back half when
    /// <paramref name="backColor"/>, so a wall takes exactly one owner's color for that pixel.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="face"/> is not a defined wall face.</exception>
    public void Wall(WallFace face, int x, int y, float z0, float z1, bool backColor)
    {
        float x0 = x, x1 = x + 1, y0 = _height - 1 - y, y1 = _height - y;
        float u = (backColor ? _width + 1 + x + 0.5f : x + 0.5f) / Atlas;
        var uv = new Vector2(u, (y + 0.5f) / _height);
        switch (face)
        {
            case WallFace.Left:
                Quad(new(x0, y0, z0), new(x0, y0, z1), new(x0, y1, z1), new(x0, y1, z0), MinusX, uv, uv, uv, uv);
                break;
            case WallFace.Right:
                Quad(new(x1, y0, z0), new(x1, y1, z0), new(x1, y1, z1), new(x1, y0, z1), PlusX, uv, uv, uv, uv);
                break;
            case WallFace.Top:
                Quad(new(x0, y1, z0), new(x0, y1, z1), new(x1, y1, z1), new(x1, y1, z0), PlusY, uv, uv, uv, uv);
                break;
            case WallFace.Bottom:
                Quad(new(x0, y0, z0), new(x1, y0, z0), new(x1, y0, z1), new(x0, y0, z1), MinusY, uv, uv, uv, uv);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(face), face, "Unknown wall face.");
        }
    }

    /// <summary>Applies the pivot and the voxel size, and freezes the result.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The options name an undefined pivot.</exception>
    public Mesh ToMesh(RiseOptions options)
    {
        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        for (int i = 0; i < _positions.Count; i += 3)
        {
            var p = new Vector3(_positions[i], _positions[i + 1], _positions[i + 2]);
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        Vector3 pivot = options.Pivot switch
        {
            Pivot.MinCorner => min,
            Pivot.Center => (min + max) / 2,
            Pivot.BottomCenter => new Vector3((min.X + max.X) / 2, min.Y, (min.Z + max.Z) / 2),
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.Pivot, "Unknown pivot."),
        };

        float s = options.VoxelSize;
        var positions = new float[_positions.Count];
        for (int i = 0; i < positions.Length; i += 3)
        {
            positions[i] = (_positions[i] - pivot.X) * s;
            positions[i + 1] = (_positions[i + 1] - pivot.Y) * s;
            positions[i + 2] = (_positions[i + 2] - pivot.Z) * s;
        }

        // The copies BACKLOG M5 asks about, and only them: the three ToArray calls and the clones Mesh's
        // constructor makes. Its own scope so a Perf report can say what share of the build they are.
        using var copies = Perf.Measure("MeshBuilder.ToMesh.copies");
        return Mesh.Adopt(positions, CollectionsMarshal.AsSpan(_normals).ToArray(), CollectionsMarshal.AsSpan(_texCoords).ToArray(),
            CollectionsMarshal.AsSpan(_indices).ToArray(), FaceAtlas.WidthFor(_width), _height);
    }

    // THE UV RULE (A = FaceAtlas.WidthFor(W)): front faces u = x / A, back faces u = (W + 1 + x) / A,
    // v = row / H. Wall pixel centers add the half pixel. Nothing else in the library maps texels.
    private float Atlas => FaceAtlas.WidthFor(_width);

    private Vector2 FrontUv(float worldX, float worldY) => new(worldX / Atlas, (_height - worldY) / _height);

    private Vector2 BackUv(float worldX, float worldY) => new((_width + 1 + worldX) / Atlas, (_height - worldY) / _height);

    private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal,
        Vector2 ta, Vector2 tb, Vector2 tc, Vector2 td)
    {
        uint ia = Vertex(a, normal, ta);
        uint ib = Vertex(b, normal, tb);
        uint ic = Vertex(c, normal, tc);
        uint id = Vertex(d, normal, td);
        _indices.AddRange([ia, ib, ic, ia, ic, id]);
    }

    private uint Vertex(Vector3 p, Vector3 n, Vector2 t)
    {
        var key = new VertexKey(
            BitConverter.SingleToUInt32Bits(p.X), BitConverter.SingleToUInt32Bits(p.Y), BitConverter.SingleToUInt32Bits(p.Z),
            BitConverter.SingleToUInt32Bits(n.X), BitConverter.SingleToUInt32Bits(n.Y), BitConverter.SingleToUInt32Bits(n.Z),
            BitConverter.SingleToUInt32Bits(t.X), BitConverter.SingleToUInt32Bits(t.Y));
        ref uint slot = ref CollectionsMarshal.GetValueRefOrAddDefault(_seen, key, out bool exists);
        if (exists) return slot;
        uint index = checked((uint)(_positions.Count / 3));
        slot = index;
        _positions.Add(p.X);
        _positions.Add(p.Y);
        _positions.Add(p.Z);
        _normals.Add(n.X);
        _normals.Add(n.Y);
        _normals.Add(n.Z);
        _texCoords.Add(t.X);
        _texCoords.Add(t.Y);
        return index;
    }
}
