using System.Numerics;

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

    /// <summary>Starts a mesh over a <paramref name="width"/> × <paramref name="height"/> image.</summary>
    public MeshBuilder(int width, int height)
    {
        _width = width;
        _height = height;
    }

    /// <summary>A front face over a merged rectangle, at z = 0.5 + its key.</summary>
    public void FrontFace(PixelRect r)
    {
        float x0 = r.X, x1 = r.X + r.Width, ylo = _height - (r.Y + r.Height), yhi = _height - r.Y;
        float z = 0.5f + r.Key;
        Quad(new(x0, ylo, z), new(x1, ylo, z), new(x1, yhi, z), new(x0, yhi, z), PlusZ,
            Uv(x0, ylo), Uv(x1, ylo), Uv(x1, yhi), Uv(x0, yhi));
    }

    /// <summary>A back face over a merged rectangle, at z = −(0.5 + its key).</summary>
    public void BackFace(PixelRect r)
    {
        float x0 = r.X, x1 = r.X + r.Width, ylo = _height - (r.Y + r.Height), yhi = _height - r.Y;
        float z = -(0.5f + r.Key);
        Quad(new(x0, ylo, z), new(x0, yhi, z), new(x1, yhi, z), new(x1, ylo, z), MinusZ,
            Uv(x0, ylo), Uv(x0, yhi), Uv(x1, yhi), Uv(x1, ylo));
    }

    /// <summary>One exposed wall segment of pixel (x, y), spanning z0 &lt; z1. Every corner samples
    /// the pixel's center, so a wall takes exactly that pixel's color.</summary>
    public void Wall(WallFace face, int x, int y, float z0, float z1)
    {
        float x0 = x, x1 = x + 1, y0 = _height - 1 - y, y1 = _height - y;
        var uv = new Vector2((x + 0.5f) / _width, (y + 0.5f) / _height);
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

        return new Mesh(positions, _normals.ToArray(), _texCoords.ToArray(), _indices.ToArray(), _width, _height);
    }

    private Vector2 Uv(float worldX, float worldY) => new(worldX / _width, (_height - worldY) / _height);

    private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal,
        Vector2 ta, Vector2 tb, Vector2 tc, Vector2 td)
    {
        uint first = checked((uint)(_positions.Count / 3));
        Vertex(a, normal, ta);
        Vertex(b, normal, tb);
        Vertex(c, normal, tc);
        Vertex(d, normal, td);
        _indices.AddRange([first, first + 1, first + 2, first, first + 2, first + 3]);
    }

    private void Vertex(Vector3 p, Vector3 n, Vector2 t)
    {
        _positions.Add(p.X);
        _positions.Add(p.Y);
        _positions.Add(p.Z);
        _normals.Add(n.X);
        _normals.Add(n.Y);
        _normals.Add(n.Z);
        _texCoords.Add(t.X);
        _texCoords.Add(t.Y);
    }
}
