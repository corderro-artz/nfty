using System.Numerics;

namespace Nfty.Model;

/// <summary>
/// A triangle mesh: what <c>Riser</c> builds, what the writers serialize, and what the step-6
/// preview rasterizes. Immutable — every array is copied in and exposed read-only — and every
/// derived value is computed in the constructor, never lazily (mixel's D7), so one mesh may be read
/// from many threads.
/// </summary>
/// <remarks>
/// <para><b>Vertex rule.</b> A vertex is shared only by the two triangles of its own quad: four
/// vertices and six indices per quad, so <see cref="VertexCount"/> is always twice
/// <see cref="TriangleCount"/> for a risen mesh. Vertices that share a position across quads are NOT
/// merged — their normals and UVs differ per face, and a merge keyed on position alone would be
/// wrong. Whether a merge keyed on (position, normal, uv) is worth its complexity is a size question
/// answered by <c>RisePerfTests</c>' measurement, not assumed.</para>
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

        float[] p = (float[])positions.Clone();
        Positions = Array.AsReadOnly(p);
        _positions = p;
        _normals = (float[])normals.Clone();
        _texCoords = (float[])texCoords.Clone();
        _indices = (uint[])indices.Clone();
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
