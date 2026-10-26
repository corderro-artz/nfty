using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Tests;

/// <summary>
/// A validator, not a round-trip. With no glTF library there is nothing to re-open a model with, so
/// this parses the container itself and asserts what the glTF 2.0 spec requires of the one document
/// shape nfty writes. A test that only checks a file starts with "glTF" proves nothing; this list IS
/// the safety a library would have given, and <c>The_validator_is_not_vacuous</c> proves each check
/// can fail.
/// </summary>
internal static class GltfValidator
{
    public const uint Magic = 0x46546C67;
    public const uint JsonChunk = 0x4E4F534A;
    public const uint BinChunk = 0x004E4942;
    public const string DataUriPrefix = "data:application/octet-stream;base64,";
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static uint U32(byte[] b, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(offset));

    /// <summary>Checks the GLB container and returns its JSON and BIN chunk.</summary>
    public static (JsonNode Json, byte[] Bin) ReadGlb(byte[] glb)
    {
        Assert.True(glb.Length >= 28, "shorter than a header and two chunk headers");
        Assert.Equal(Magic, U32(glb, 0));
        Assert.Equal(2u, U32(glb, 4));
        Assert.Equal((uint)glb.Length, U32(glb, 8));

        int jsonLength = (int)U32(glb, 12);
        Assert.Equal(JsonChunk, U32(glb, 16));
        Assert.Equal(0, jsonLength % 4);
        byte[] jsonChunk = glb.AsSpan(20, jsonLength).ToArray();
        int end = Array.LastIndexOf(jsonChunk, (byte)'}') + 1;
        Assert.True(end > 0, "the JSON chunk holds no object");
        Assert.All(jsonChunk[end..], b => Assert.Equal((byte)0x20, b));   // padded with SPACES

        int binHeader = 20 + jsonLength;
        int binLength = (int)U32(glb, binHeader);
        Assert.Equal(BinChunk, U32(glb, binHeader + 4));
        Assert.Equal(0, binLength % 4);
        Assert.Equal(glb.Length, binHeader + 8 + binLength);

        var json = JsonNode.Parse(jsonChunk.AsSpan(0, end)) ?? throw new InvalidOperationException("null JSON");
        Assert.Null(json["buffers"]?[0]?["uri"]);   // a GLB's buffer IS the BIN chunk
        return (json, glb.AsSpan(binHeader + 8, binLength).ToArray());
    }

    /// <summary>Reads a text glTF and resolves its buffer — a sibling file or a data URI.</summary>
    public static (JsonNode Json, byte[] Bin) ReadGltf(IReadOnlyList<ModelFile> files)
    {
        var gltf = Assert.Single(files, f => f.FileName.EndsWith(".gltf", StringComparison.Ordinal));
        Assert.Equal((byte)'{', gltf.Bytes[0]);   // UTF-8, no BOM
        var json = JsonNode.Parse(gltf.Bytes) ?? throw new InvalidOperationException("null JSON");
        string uri = json["buffers"]![0]!["uri"]!.GetValue<string>();
        byte[] bin = uri.StartsWith(DataUriPrefix, StringComparison.Ordinal)
            ? Convert.FromBase64String(uri[DataUriPrefix.Length..])
            : Assert.Single(files, f => f.FileName == Uri.UnescapeDataString(uri)).Bytes;
        return (json, bin);
    }

    /// <summary>Every spec-compliance point the Rise spec lists, and the invariants behind them.</summary>
    public static void AssertValid(JsonNode root, byte[] bin)
    {
        Assert.Equal("2.0", root["asset"]!["version"]!.GetValue<string>());
        Assert.Equal("nfty", root["asset"]!["generator"]!.GetValue<string>());
        Assert.Equal(0, root["scene"]!.GetValue<int>());
        Assert.Equal(0, root["scenes"]![0]!["nodes"]![0]!.GetValue<int>());
        Assert.Equal(0, root["nodes"]![0]!["mesh"]!.GetValue<int>());

        var buffers = root["buffers"]!.AsArray();
        Assert.Single(buffers);
        int bufferLength = buffers[0]!["byteLength"]!.GetValue<int>();
        Assert.InRange(bin.Length - bufferLength, 0, 3);

        // Buffer views: inside the buffer, 4-aligned, disjoint; every byte no view claims is padding
        // and must be zero.
        var views = root["bufferViews"]!.AsArray();
        var covered = new bool[bin.Length];
        foreach (var view in views)
        {
            Assert.Equal(0, view!["buffer"]!.GetValue<int>());
            int offset = view["byteOffset"]?.GetValue<int>() ?? 0;
            int length = view["byteLength"]!.GetValue<int>();
            Assert.True(length > 0, "empty bufferView");
            Assert.True(offset % 4 == 0, $"bufferView at {offset} is not 4-byte aligned");
            Assert.True(offset + length <= bufferLength, $"bufferView {offset}+{length} overruns a {bufferLength}-byte buffer");
            for (int i = offset; i < offset + length; i++)
            {
                Assert.False(covered[i], $"two bufferViews claim byte {i}");
                covered[i] = true;
            }
        }
        for (int i = 0; i < bin.Length; i++)
            if (!covered[i])
                Assert.Equal(0, bin[i]);

        // Accessors: only FLOAT and UNSIGNED_INT, each starting on its component size, inside its view.
        var accessors = root["accessors"]!.AsArray();
        foreach (var a in accessors)
        {
            int componentType = a!["componentType"]!.GetValue<int>();
            Assert.Contains(componentType, new[] { 5126, 5125 });
            var view = views[a["bufferView"]!.GetValue<int>()]!;
            int accessorOffset = a["byteOffset"]?.GetValue<int>() ?? 0;
            int start = (view["byteOffset"]?.GetValue<int>() ?? 0) + accessorOffset;
            Assert.True(start % 4 == 0, $"accessor starts at {start}, not on its 4-byte component boundary");
            int count = a["count"]!.GetValue<int>();
            Assert.True(count > 0, "empty accessor");
            Assert.True(accessorOffset + (count * Components(a["type"]!.GetValue<string>()) * 4) <= view["byteLength"]!.GetValue<int>(),
                "accessor overruns its bufferView");
        }

        var primitive = root["meshes"]![0]!["primitives"]![0]!;
        Assert.Equal(4, primitive["mode"]!.GetValue<int>());
        var attributes = primitive["attributes"]!;
        int position = attributes["POSITION"]!.GetValue<int>();
        int normal = attributes["NORMAL"]!.GetValue<int>();
        int uv = attributes["TEXCOORD_0"]!.GetValue<int>();
        int vertexCount = accessors[position]!["count"]!.GetValue<int>();
        Assert.Equal("VEC3", accessors[position]!["type"]!.GetValue<string>());
        Assert.Equal("VEC3", accessors[normal]!["type"]!.GetValue<string>());
        Assert.Equal("VEC2", accessors[uv]!["type"]!.GetValue<string>());
        Assert.Equal(vertexCount, accessors[normal]!["count"]!.GetValue<int>());
        Assert.Equal(vertexCount, accessors[uv]!["count"]!.GetValue<int>());
        foreach (int attribute in new[] { position, normal, uv })
            Assert.Equal(34962, views[accessors[attribute]!["bufferView"]!.GetValue<int>()]!["target"]!.GetValue<int>());

        // Every texture coordinate addresses the atlas: nothing outside [0, 1].
        Assert.All(ReadFloats(root, bin, uv), t => Assert.InRange(t, 0f, 1f));

        // POSITION MUST carry min and max, and they must be the real extents: a viewer culls by them.
        var min = accessors[position]!["min"]?.AsArray();
        var max = accessors[position]!["max"]?.AsArray();
        Assert.NotNull(min);
        Assert.NotNull(max);
        float[] p = ReadFloats(root, bin, position);
        for (int axis = 0; axis < 3; axis++)
        {
            var values = p.Where((_, i) => i % 3 == axis).ToArray();
            Assert.Equal(values.Min(), min[axis]!.GetValue<float>());
            Assert.Equal(values.Max(), max[axis]!.GetValue<float>());
        }

        // Indices: UNSIGNED_INT, whole triangles, every one a real vertex.
        var indices = accessors[primitive["indices"]!.GetValue<int>()]!;
        Assert.Equal(5125, indices["componentType"]!.GetValue<int>());
        Assert.Equal("SCALAR", indices["type"]!.GetValue<string>());
        Assert.Equal(0, indices["count"]!.GetValue<int>() % 3);
        Assert.Equal(34963, views[indices["bufferView"]!.GetValue<int>()]!["target"]!.GetValue<int>());
        Assert.All(ReadUInts(root, bin, primitive["indices"]!.GetValue<int>()), i => Assert.True(i < (uint)vertexCount));

        // Material: named for nfty (D4), single-sided (D3), alpha as a mask.
        var material = root["materials"]![primitive["material"]!.GetValue<int>()]!;
        Assert.Contains("nfty", material["name"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.False(material["doubleSided"]?.GetValue<bool>() ?? false, "D3: a closed solid is single-sided");
        Assert.Equal("MASK", material["alphaMode"]!.GetValue<string>());
        Assert.Equal(0.5f, material["alphaCutoff"]!.GetValue<float>());

        // Texture: NEAREST and CLAMP_TO_EDGE, so pixel art stays crisp; the PNG in a target-less view.
        int textureIndex = material["pbrMetallicRoughness"]!["baseColorTexture"]!["index"]!.GetValue<int>();
        var texture = root["textures"]![textureIndex]!;
        var sampler = root["samplers"]![texture["sampler"]!.GetValue<int>()]!;
        Assert.Equal(9728, sampler["magFilter"]!.GetValue<int>());
        Assert.Equal(9728, sampler["minFilter"]!.GetValue<int>());
        Assert.Equal(33071, sampler["wrapS"]!.GetValue<int>());
        Assert.Equal(33071, sampler["wrapT"]!.GetValue<int>());
        var image = root["images"]![texture["source"]!.GetValue<int>()]!;
        Assert.Equal("image/png", image["mimeType"]!.GetValue<string>());
        Assert.Null(image["uri"]);
        var imageView = views[image["bufferView"]!.GetValue<int>()]!;
        Assert.Null(imageView["target"]);
        int imageOffset = imageView["byteOffset"]?.GetValue<int>() ?? 0;
        Assert.Equal(PngSignature, bin.AsSpan(imageOffset, 8).ToArray());
        AssertAtlas(bin.AsSpan(imageOffset, imageView["byteLength"]!.GetValue<int>()).ToArray());
    }

    /// <summary>The embedded PNG is a face atlas: <c>[front k][gutter 1][back k]</c>, so an odd width
    /// 2k+1 whose column k repeats column k−1.</summary>
    public static void AssertAtlas(byte[] png)
    {
        using var atlas = Image.Load<Rgba32>(png);
        Assert.True(atlas.Width % 2 == 1, $"the atlas is {atlas.Width} wide, not 2k+1");
        int k = atlas.Width / 2;
        for (int y = 0; y < atlas.Height; y++)
            Assert.True(atlas[k, y] == atlas[k - 1, y], $"the gutter differs from the front half's last column at row {y}");
    }

    public static float[] ReadFloats(JsonNode root, byte[] bin, int accessor)
    {
        var a = root["accessors"]![accessor]!;
        var view = root["bufferViews"]![a["bufferView"]!.GetValue<int>()]!;
        int start = (view["byteOffset"]?.GetValue<int>() ?? 0) + (a["byteOffset"]?.GetValue<int>() ?? 0);
        int n = a["count"]!.GetValue<int>() * Components(a["type"]!.GetValue<string>());
        var values = new float[n];
        for (int i = 0; i < n; i++)
            values[i] = BinaryPrimitives.ReadSingleLittleEndian(bin.AsSpan(start + (i * 4)));
        return values;
    }

    public static uint[] ReadUInts(JsonNode root, byte[] bin, int accessor)
    {
        var a = root["accessors"]![accessor]!;
        var view = root["bufferViews"]![a["bufferView"]!.GetValue<int>()]!;
        int start = (view["byteOffset"]?.GetValue<int>() ?? 0) + (a["byteOffset"]?.GetValue<int>() ?? 0);
        int n = a["count"]!.GetValue<int>();
        var values = new uint[n];
        for (int i = 0; i < n; i++)
            values[i] = BinaryPrimitives.ReadUInt32LittleEndian(bin.AsSpan(start + (i * 4)));
        return values;
    }

    public static byte[] ReadImage(JsonNode root, byte[] bin)
    {
        var view = root["bufferViews"]![root["images"]![0]!["bufferView"]!.GetValue<int>()]!;
        int offset = view["byteOffset"]?.GetValue<int>() ?? 0;
        return bin.AsSpan(offset, view["byteLength"]!.GetValue<int>()).ToArray();
    }

    /// <summary>Re-frames a GLB after editing its JSON — how the validator's own tests plant a defect.</summary>
    public static byte[] Reframe(byte[] glb, Action<JsonNode> edit, Action<JsonNode, byte[]>? editBin = null)
    {
        var (json, bin) = ReadGlb(glb);
        editBin?.Invoke(json, bin);
        edit(json);
        byte[] text = Encoding.UTF8.GetBytes(json.ToJsonString());
        int jsonLength = (text.Length + 3) & ~3;
        var result = new byte[28 + jsonLength + bin.Length];
        var span = result.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span, Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], 2);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], (uint)result.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[12..], (uint)jsonLength);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], JsonChunk);
        text.CopyTo(span[20..]);
        span.Slice(20 + text.Length, jsonLength - text.Length).Fill(0x20);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(20 + jsonLength)..], (uint)bin.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(24 + jsonLength)..], BinChunk);
        bin.CopyTo(span[(28 + jsonLength)..]);
        return result;
    }

    private static int Components(string type)
    {
        switch (type)
        {
            case "SCALAR": return 1;
            case "VEC2": return 2;
            case "VEC3": return 3;
            default:
                Assert.Fail($"unexpected accessor type '{type}'");
                return 0;
        }
    }
}
