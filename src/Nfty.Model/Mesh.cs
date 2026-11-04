using System.Numerics;

namespace Nfty.Model;

/// <summary>
/// A triangle mesh: what <c>Riser</c> builds, what the writers serialize, and what the step-6
/// preview rasterizes. Immutable — every array is copied in and exposed read-only — and every
/// derived value is computed in the constructor, never lazily (mixel's D7), so one mesh may be read
/// from many threads.
/// </summary>
/// <remarks>
/// <para><b>Vertex rule.</b> A mesh straight from <c>Riser</c> shares a vertex only between the two
/// triangles of its own quad: four vertices and six indices per quad, so <see cref="VertexCount"/> is
/// twice <see cref="TriangleCount"/>. That is the fast build the Rise page repeats per stroke.
/// <see cref="Compact"/> merges bit-identical (position, normal, uv) vertices for the written file.</para>
///
/// <para>Coordinates follow glTF: +Y up, +Z toward the viewer. Texture coordinates put (0,0) at the
/// image's top-left, so v grows downward with the image row.</para>
/// </remarks>
public sealed class Mesh
{
    // The private copies behind the read-only views above, kept so the rasterizer can read them as
    // spans instead of through IReadOnlyList's interface dispatch. Never exposed writable.
    private readonly float[] _positions;
    private readonly float[] _normals;
    private readonly float[] _texCoords;
    private readonly uint[] _indices;

    internal ReadOnlySpan<float> PositionData => _positions;

    internal ReadOnlySpan<float> NormalData => _normals;

    internal ReadOnlySpan<float> TexCoordData => _texCoords;

    internal ReadOnlySpan<uint> IndexData => _indices;

    /// <summary>Builds a mesh, copying every array.</summary>
    /// <param name="positions">xyz per vertex.</param>
    /// <param name="normals">One xyz unit normal per vertex.</param>
    /// <param name="texCoords">One uv per vertex.</param>
    /// <param name="indices">Three vertex indices per triangle, counter-clockwise seen from outside.</param>
    /// <param name="textureWidth">Width of the image the UVs address.</param>
    /// <param name="textureHeight">Height of the image the UVs address.</param>
    /// <exception cref="ArgumentException">The arrays disagree about the vertex count, an index names
    /// no vertex, or a position is not finite.</exception>
    public Mesh(float[] positions, float[] normals, float[] texCoords, uint[] indices,
        int textureWidth, int textureHeight)
        : this(positions, normals, texCoords, indices, textureWidth, textureHeight, copy: true)
    {
    }

    /// <summary>Builds a mesh that takes ownership of the arrays without copying. Only for a caller
    /// that never touches them again (<see cref="MeshBuilder"/>); same validation as the public constructor.</summary>
    internal static Mesh Adopt(float[] positions, float[] normals, float[] texCoords, uint[] indices,
        int textureWidth, int textureHeight)
        => new(positions, normals, texCoords, indices, textureWidth, textureHeight, copy: false);

    private Mesh(float[] positions, float[] normals, float[] texCoords, uint[] indices,
        int textureWidth, int textureHeight, bool copy)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(normals);
        ArgumentNullException.ThrowIfNull(texCoords);
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(textureWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(textureHeight);

        if (positions.Length == 0 || positions.Length % 3 != 0)
            throw new ArgumentException(
                $"Positions are xyz triples; {positions.Length} floats is not a whole, non-zero number of them.",
                nameof(positions));
        int vertexCount = positions.Length / 3;
        if (normals.Length != positions.Length)
            throw new ArgumentException(
                $"Every vertex needs one xyz normal: expected {positions.Length} floats, got {normals.Length}.",
                nameof(normals));
        if (texCoords.Length != vertexCount * 2)
            throw new ArgumentException(
                $"Every vertex needs one uv: expected {vertexCount * 2} floats, got {texCoords.Length}.",
                nameof(texCoords));
        if (indices.Length == 0 || indices.Length % 3 != 0)
            throw new ArgumentException(
                $"Indices come three per triangle; {indices.Length} is not a whole, non-zero number of triangles.",
                nameof(indices));
        foreach (uint i in indices)
            if (i >= (uint)vertexCount)
                throw new ArgumentException($"Index {i} names no vertex; the mesh has {vertexCount}.", nameof(indices));
        foreach (float f in positions)
            if (!float.IsFinite(f))
                throw new ArgumentException("Every position must be a finite number.", nameof(positions));

        float[] p = copy ? (float[])positions.Clone() : positions;
        Positions = Array.AsReadOnly(p);
        _positions = p;
        _normals = copy ? (float[])normals.Clone() : normals;
        _texCoords = copy ? (float[])texCoords.Clone() : texCoords;
        _indices = copy ? (uint[])indices.Clone() : indices;
        Normals = Array.AsReadOnly(_normals);
        TexCoords = Array.AsReadOnly(_texCoords);
        Indices = Array.AsReadOnly(_indices);
        VertexCount = vertexCount;
        TriangleCount = indices.Length / 3;
        TextureWidth = textureWidth;
        TextureHeight = textureHeight;

        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        for (int v = 0; v < p.Length; v += 3)
        {
            var q = new Vector3(p[v], p[v + 1], p[v + 2]);
            min = Vector3.Min(min, q);
            max = Vector3.Max(max, q);
        }
        Min = min;
        Max = max;
    }

    /// <summary>A copy with every set of bit-identical (position, normal, uv) vertices merged into one,
    /// no epsilon. Vertices are numbered in first-seen order, so the same mesh always compacts to the
    /// same bytes; triangle count, winding and bounds are unchanged. It costs a hash pass, which is why
    /// the writers pay it and the live preview does not.</summary>
    /// <returns>The compacted mesh; this mesh itself when nothing merges.</returns>
    public Mesh Compact()
    {
        var seen = new Dictionary<VertexKey, uint>(VertexCount);
        var remap = new uint[VertexCount];
        var keep = new List<int>(VertexCount);
        for (int v = 0; v < VertexCount; v++)
        {
            var key = new VertexKey(
                BitConverter.SingleToUInt32Bits(_positions[v * 3]), BitConverter.SingleToUInt32Bits(_positions[(v * 3) + 1]),
                BitConverter.SingleToUInt32Bits(_positions[(v * 3) + 2]), BitConverter.SingleToUInt32Bits(_normals[v * 3]),
                BitConverter.SingleToUInt32Bits(_normals[(v * 3) + 1]), BitConverter.SingleToUInt32Bits(_normals[(v * 3) + 2]),
                BitConverter.SingleToUInt32Bits(_texCoords[v * 2]), BitConverter.SingleToUInt32Bits(_texCoords[(v * 2) + 1]));
            ref uint slot = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(seen, key, out bool exists);
            if (!exists)
            {
                slot = (uint)keep.Count;
                keep.Add(v);
            }
            remap[v] = slot;
        }
        if (keep.Count == VertexCount) return this;

        var p = new float[keep.Count * 3];
        var n = new float[keep.Count * 3];
        var t = new float[keep.Count * 2];
        for (int k = 0; k < keep.Count; k++)
        {
            int v = keep[k];
            Array.Copy(_positions, v * 3, p, k * 3, 3);
            Array.Copy(_normals, v * 3, n, k * 3, 3);
            Array.Copy(_texCoords, v * 2, t, k * 2, 2);
        }
        var idx = new uint[_indices.Length];
        for (int i = 0; i < idx.Length; i++) idx[i] = remap[_indices[i]];
        return Adopt(p, n, t, idx, TextureWidth, TextureHeight);
    }

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

        // Cheap multiplicative mix; only equality affects output (numbering is first-seen order).
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

    /// <summary>xyz per vertex, in world units.</summary>
    public IReadOnlyList<float> Positions { get; }

    /// <summary>One xyz unit normal per vertex, pointing out of the solid.</summary>
    public IReadOnlyList<float> Normals { get; }

    /// <summary>One uv per vertex; (0,0) is the texture's top-left.</summary>
    public IReadOnlyList<float> TexCoords { get; }

    /// <summary>Three vertex indices per triangle, counter-clockwise seen from outside.</summary>
    public IReadOnlyList<uint> Indices { get; }

    /// <summary>How many vertices the mesh has.</summary>
    public int VertexCount { get; }

    /// <summary>How many triangles the mesh has.</summary>
    public int TriangleCount { get; }

    /// <summary>The smallest x, y and z over every vertex — exactly the values glTF's
    /// <c>POSITION</c> accessor must carry as <c>min</c>.</summary>
    public Vector3 Min { get; }

    /// <summary>The largest x, y and z over every vertex — glTF's <c>POSITION</c> <c>max</c>.</summary>
    public Vector3 Max { get; }

    /// <summary>Width of the image the UVs address; a writer refuses a texture of another size.</summary>
    public int TextureWidth { get; }

    /// <summary>Height of the image the UVs address.</summary>
    public int TextureHeight { get; }
}
