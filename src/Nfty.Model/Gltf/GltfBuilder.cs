using System.Buffers.Binary;

namespace Nfty.Model.Gltf;

/// <summary>Builds the one document shape nfty writes, and the buffer it describes.</summary>
internal static class GltfBuilder
{
    private const int ArrayBuffer = 34962;
    private const int ElementArrayBuffer = 34963;
    private const int Float = 5126;
    private const int UnsignedInt = 5125;
    private const int Nearest = 9728;
    private const int ClampToEdge = 33071;
    private const int Triangles = 4;

    /// <summary>The document (buffer with no URI) and the buffer's bytes, padded to four with zeros.</summary>
    public static (GltfRoot Root, byte[] Bin) Build(Mesh mesh, byte[] png)
    {
        int vertices = mesh.VertexCount;
        int positionLength = checked(vertices * 12);
        int normalLength = positionLength;
        int uvLength = checked(vertices * 8);
        int indexLength = checked(mesh.Indices.Count * 4);

        int positionOffset = 0;
        int normalOffset = positionLength;
        int uvOffset = checked(normalOffset + normalLength);
        int indexOffset = checked(uvOffset + uvLength);
        int pngOffset = checked(indexOffset + indexLength);
        int binLength = GlbContainer.Pad4(checked(pngOffset + png.Length));

        var bin = new byte[binLength];
        WriteFloats(bin.AsSpan(positionOffset, positionLength), mesh.Positions);
        WriteFloats(bin.AsSpan(normalOffset, normalLength), mesh.Normals);
        WriteFloats(bin.AsSpan(uvOffset, uvLength), mesh.TexCoords);
        for (int i = 0; i < mesh.Indices.Count; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(bin.AsSpan(indexOffset + (i * 4)), mesh.Indices[i]);
        png.CopyTo(bin.AsSpan(pngOffset));

        var root = new GltfRoot(
            Asset: new GltfAsset("2.0", "nfty"),
            Scene: 0,
            Scenes: [new GltfScene([0])],
            Nodes: [new GltfNode(0, "nfty")],
            Meshes: [new GltfMesh("nfty", [new GltfPrimitive(new GltfAttributes(0, 1, 2), 3, 0, Triangles)])],
            Materials: [new GltfMaterial("nfty", new GltfPbr(new GltfTextureRef(0), 0f, 1f), "MASK", 0.5f)],
            Textures: [new GltfTexture(0, 0)],
            Samplers: [new GltfSampler(Nearest, Nearest, ClampToEdge, ClampToEdge)],
            Images: [new GltfImage(4, "image/png")],
            Accessors:
            [
                new GltfAccessor(0, Float, vertices, "VEC3",
                    [mesh.Min.X, mesh.Min.Y, mesh.Min.Z], [mesh.Max.X, mesh.Max.Y, mesh.Max.Z]),
                new GltfAccessor(1, Float, vertices, "VEC3", null, null),
                new GltfAccessor(2, Float, vertices, "VEC2", null, null),
                new GltfAccessor(3, UnsignedInt, mesh.Indices.Count, "SCALAR", null, null),
            ],
            BufferViews:
            [
                new GltfBufferView(0, positionOffset, positionLength, ArrayBuffer),
                new GltfBufferView(0, normalOffset, normalLength, ArrayBuffer),
                new GltfBufferView(0, uvOffset, uvLength, ArrayBuffer),
                new GltfBufferView(0, indexOffset, indexLength, ElementArrayBuffer),
                new GltfBufferView(0, pngOffset, png.Length, null),
            ],
            Buffers: [new GltfBuffer(binLength, null)]);

        return (root, bin);
    }

    private static void WriteFloats(Span<byte> target, IReadOnlyList<float> values)
    {
        for (int i = 0; i < values.Count; i++)
            BinaryPrimitives.WriteSingleLittleEndian(target[(i * 4)..], values[i]);
    }
}
