using System.Buffers.Binary;
using System.Text.Json;
using Nfty.Model.Gltf;
using SixLabors.ImageSharp;

namespace Nfty.Model;

/// <summary>
/// Reads back the models <see cref="ModelWriter"/> writes (GLB, glTF + .bin, embedded glTF) and
/// nothing else. It goes through the same record model the writer serializes, so there is one schema;
/// every member that model does not state is refused by name with <see cref="UnsupportedModelException"/>
/// instead of being skipped, because a skipped feature (a second mesh, a sparse accessor) would show
/// the reader a different model from the file's.
/// </summary>
public static class GltfReader
{
    private const int Float = 5126;
    private const int UnsignedInt = 5125;
    private const string DataUriPrefix = "data:application/octet-stream;base64,";

    private static readonly JsonSerializerOptions Options = new();

    /// <summary>Reads a model file; the extension (.glb or .gltf) says which.</summary>
    /// <param name="path">The file. A .gltf's .bin sits beside it.</param>
    /// <returns>The mesh and texture.</returns>
    /// <exception cref="UnsupportedModelException">The file is not one nfty could have written.</exception>
    public static ReadModel Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        byte[] bytes = File.ReadAllBytes(path);
        string ext = Path.GetExtension(path);
        if (string.Equals(ext, ".glb", StringComparison.OrdinalIgnoreCase)) return Read(bytes);
        if (!string.Equals(ext, ".gltf", StringComparison.OrdinalIgnoreCase))
            throw new UnsupportedModelException($"the file type '{ext}' (only .glb and .gltf)");
        string dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
        return Guarded(() => Parse(bytes, uri => Beside(dir, uri), null));
    }

    // nfty names its .bin by a bare file name beside the .gltf. Anything else (a path into another
    // folder, a rooted path) is a file nfty never wrote, and is refused rather than read.
    private static byte[] Beside(string dir, string uri)
    {
        string name = Uri.UnescapeDataString(uri);
        if (name.Length == 0 || name.IndexOfAny(['/', '\\', ':']) >= 0 || name is "." or ".."
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new UnsupportedModelException($"a buffer uri that is not a file beside the .gltf ('{uri}')");
        try { return File.ReadAllBytes(Path.Combine(dir, name)); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new UnsupportedModelException($"a .gltf whose buffer '{name}' is not beside it");
        }
    }

    // Hostile bytes must surface as the one exception the contract names: a caller that catches
    // UnsupportedModelException (a Set tile falling back to rising its asset) must never meet an
    // OverflowException, a FormatException or a null member of a hollow structure instead.
    private static ReadModel Guarded(Func<ReadModel> read)
    {
        try { return read(); }
        catch (Exception ex) when (ex is OverflowException or FormatException or NullReferenceException
                                       or IndexOutOfRangeException or ArgumentException or InvalidCastException
                                       or InvalidOperationException)
        {
            throw new UnsupportedModelException($"a structure nfty never writes ({ex.Message})");
        }
    }

    /// <summary>Reads a GLB held in memory.</summary>
    /// <param name="glb">The whole file.</param>
    /// <returns>The mesh and texture.</returns>
    /// <exception cref="UnsupportedModelException">The bytes are not a GLB nfty could have written.</exception>
    public static ReadModel Read(ReadOnlySpan<byte> glb)
    {
        byte[] copy = glb.ToArray();
        return Guarded(() => ReadGlb(copy));
    }

    private static ReadModel ReadGlb(ReadOnlySpan<byte> glb)
    {
        if (glb.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(glb) != GlbContainer.Magic)
            throw new UnsupportedModelException("a container that is not GLB");
        if (BinaryPrimitives.ReadUInt32LittleEndian(glb[4..]) != GlbContainer.Version)
            throw new UnsupportedModelException("a GLB version other than 2");
        uint total = BinaryPrimitives.ReadUInt32LittleEndian(glb[8..]);
        if (total > glb.Length) throw new UnsupportedModelException("a truncated GLB");

        int pos = 12;
        byte[]? json = null, bin = null;
        while (pos + 8 <= total)
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(glb[pos..]));
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(glb[(pos + 4)..]);
            if ((long)pos + 8 + length > total) throw new UnsupportedModelException("a truncated GLB chunk");
            var body = glb.Slice(pos + 8, length);
            if (type == GlbContainer.JsonChunk && json is null) json = body.ToArray();
            else if (type == GlbContainer.BinChunk && bin is null) bin = body.ToArray();
            else throw new UnsupportedModelException("a GLB chunk other than one JSON and one BIN");
            pos += 8 + length;
        }
        if (json is null || bin is null) throw new UnsupportedModelException("a GLB without both JSON and BIN chunks");
        return Parse(json, _ => throw new UnsupportedModelException("a GLB whose buffer has a uri"), bin);
    }

    private static ReadModel Parse(byte[] json, Func<string, byte[]> external, byte[]? glbBin)
    {
        using (var doc = ParseDocument(json)) Guard(doc.RootElement);

        GltfRoot? root;
        try { root = JsonSerializer.Deserialize<GltfRoot>(json, Options); }
        catch (JsonException ex) { throw new UnsupportedModelException($"a structure nfty never writes ({ex.Message})"); }
        if (root is null || root.Meshes is null || root.Accessors is null || root.BufferViews is null
            || root.Buffers is null || root.Images is null)
            throw new UnsupportedModelException("a structure nfty never writes (a required array is missing)");

        if (root.Buffers.Count != 1) throw new UnsupportedModelException("more than one buffer");
        var buffer = root.Buffers[0];
        byte[] data;
        if (glbBin is not null)
        {
            if (buffer.Uri is not null) throw new UnsupportedModelException("a GLB whose buffer has a uri");
            data = glbBin;
        }
        else
        {
            if (buffer.Uri is null) throw new UnsupportedModelException("a .gltf whose buffer has no uri");
            if (buffer.Uri.StartsWith(DataUriPrefix, StringComparison.Ordinal))
                data = Convert.FromBase64String(buffer.Uri[DataUriPrefix.Length..]);
            else if (buffer.Uri.StartsWith("data:", StringComparison.Ordinal))
                throw new UnsupportedModelException("a data uri that is not application/octet-stream base64");
            else
                data = external(buffer.Uri);
        }
        if (data.Length < buffer.ByteLength) throw new UnsupportedModelException("a buffer shorter than it declares");

        if (root.Meshes.Count != 1) throw new UnsupportedModelException("more than one mesh");
        if (root.Meshes[0].Primitives.Count != 1) throw new UnsupportedModelException("more than one primitive");
        var prim = root.Meshes[0].Primitives[0];
        if (prim.Mode != 4) throw new UnsupportedModelException($"primitive mode {prim.Mode} (only triangles)");

        float[] positions = Floats(root, data, prim.Attributes.Position, "VEC3", 3);
        float[] normals = Floats(root, data, prim.Attributes.Normal, "VEC3", 3);
        float[] uvs = Floats(root, data, prim.Attributes.TexCoord0, "VEC2", 2);
        uint[] indices = Indices(root, data, prim.Indices);

        if (root.Images.Count != 1) throw new UnsupportedModelException("more than one image");
        var image = root.Images[0];
        if (image.MimeType != "image/png") throw new UnsupportedModelException($"a '{image.MimeType}' texture (only PNG)");
        var (offset, length) = View(root, data, image.BufferView);
        byte[] png = data.AsSpan(offset, length).ToArray();

        ImageInfo info;
        try { info = Image.Identify(png); }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException)
        {
            throw new UnsupportedModelException("a texture that is not a readable PNG");
        }
        try
        {
            return new ReadModel(new Mesh(positions, normals, uvs, indices, info.Width, info.Height), png);
        }
        catch (ArgumentException ex)
        {
            throw new UnsupportedModelException($"a mesh nfty would not write ({ex.Message})");
        }
    }

    private static JsonDocument ParseDocument(byte[] json)
    {
        try { return JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new UnsupportedModelException($"JSON that does not parse ({ex.Message})"); }
    }

    // Every member nfty writes is named; any other is refused with its name, so nothing is skipped.
    private static void Guard(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new UnsupportedModelException("a document that is not an object");
        Only(root, "the document", "asset", "scene", "scenes", "nodes", "meshes", "materials", "textures",
            "samplers", "images", "accessors", "bufferViews", "buffers");
        foreach (var node in Items(root, "nodes")) Only(node, "a node", "mesh", "name");
        foreach (var mesh in Items(root, "meshes"))
        {
            Only(mesh, "a mesh", "name", "primitives");
            foreach (var p in Items(mesh, "primitives"))
            {
                Only(p, "a primitive", "attributes", "indices", "material", "mode");
                if (p.TryGetProperty("attributes", out var a))
                    Only(a, "a primitive's attributes", "POSITION", "NORMAL", "TEXCOORD_0");
            }
        }
        foreach (var a in Items(root, "accessors"))
            Only(a, "an accessor", "bufferView", "componentType", "count", "type", "min", "max");
        foreach (var v in Items(root, "bufferViews"))
            Only(v, "a bufferView", "buffer", "byteOffset", "byteLength", "target");
        foreach (var b in Items(root, "buffers")) Only(b, "a buffer", "byteLength", "uri");
        foreach (var i in Items(root, "images")) Only(i, "an image", "bufferView", "mimeType");
        foreach (var m in Items(root, "materials"))
            Only(m, "a material", "name", "pbrMetallicRoughness", "alphaMode", "alphaCutoff");
    }

    private static IEnumerable<JsonElement> Items(JsonElement root, string name) =>
        root.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.EnumerateArray() : Enumerable.Empty<JsonElement>();

    private static void Only(JsonElement el, string where, params string[] allowed)
    {
        if (el.ValueKind != JsonValueKind.Object) return;
        foreach (var prop in el.EnumerateObject())
        {
            if (Array.IndexOf(allowed, prop.Name) >= 0) continue;
            throw new UnsupportedModelException(prop.Name switch
            {
                "sparse" => "sparse accessors",
                "byteStride" => "interleaved vertex data (byteStride)",
                "extensions" or "extensionsUsed" or "extensionsRequired" => "glTF extensions",
                "animations" => "animations",
                "skins" => "skins",
                "cameras" => "cameras",
                _ => $"'{prop.Name}' in {where}",
            });
        }
    }

    private static (int Offset, int Length) View(GltfRoot root, byte[] data, int index)
    {
        if (index < 0 || index >= root.BufferViews.Count) throw new UnsupportedModelException("a bufferView that does not exist");
        var v = root.BufferViews[index];
        if (v.Buffer != 0 || v.ByteOffset < 0 || v.ByteLength < 0 || (long)v.ByteOffset + v.ByteLength > data.Length)
            throw new UnsupportedModelException("a bufferView outside its buffer");
        return (v.ByteOffset, v.ByteLength);
    }

    private static GltfAccessor Accessor(GltfRoot root, int index, string type, int component)
    {
        if (index < 0 || index >= root.Accessors.Count) throw new UnsupportedModelException("an accessor that does not exist");
        var a = root.Accessors[index];
        if (a.ComponentType != component || a.Type != type)
            throw new UnsupportedModelException($"an accessor of type {a.Type}/{a.ComponentType} (expected {type}/{component})");
        return a;
    }

    private static float[] Floats(GltfRoot root, byte[] data, int accessor, string type, int width)
    {
        var a = Accessor(root, accessor, type, Float);
        var (offset, length) = View(root, data, a.BufferView);
        if (a.Count < 0 || (long)a.Count * width * 4 > length)
            throw new UnsupportedModelException("an accessor larger than its bufferView");
        var result = new float[a.Count * width];
        for (int i = 0; i < result.Length; i++)
            result[i] = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + (i * 4)));
        return result;
    }

    private static uint[] Indices(GltfRoot root, byte[] data, int accessor)
    {
        var a = Accessor(root, accessor, "SCALAR", UnsignedInt);
        var (offset, length) = View(root, data, a.BufferView);
        if (a.Count < 0 || (long)a.Count * 4 > length)
            throw new UnsupportedModelException("an accessor larger than its bufferView");
        var result = new uint[a.Count];
        for (int i = 0; i < result.Length; i++)
            result[i] = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + (i * 4)));
        return result;
    }
}
