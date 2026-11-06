using Nfty.Core.Imaging;
using System.Buffers.Binary;
using System.Text.Json;
using Nfty.Model.Gltf;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;

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
    /// <returns>The mesh and encoded PNG bytes; no decoded image needs disposal.</returns>
    /// <exception cref="UnsupportedModelException">The content is malformed, truncated or outside
    /// nfty's supported subset, or a referenced companion buffer is missing.</exception>
    /// <remarks>Ordinary file I/O and permission failures propagate, including failure to open
    /// <paramref name="path"/> itself. They are not unsupported-content errors.</remarks>
    public static ReadModel Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        byte[] bytes = File.ReadAllBytes(path);
        string ext = Path.GetExtension(path);
        if (string.Equals(ext, ".glb", StringComparison.OrdinalIgnoreCase)) return Guarded(() => ReadGlb(bytes));
        if (!string.Equals(ext, ".gltf", StringComparison.OrdinalIgnoreCase))
            throw new UnsupportedModelException($"the file type '{ext}' (only .glb and .gltf)");
        string dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
        return Guarded(() => Parse(bytes, uri => Beside(dir, uri), null));
    }

    // nfty names its .bin by a bare file name beside the .gltf. Anything else (a path into another
    // folder, a rooted path) is a file nfty never wrote, and is refused rather than read.
    private static byte[] Beside(string dir, string uri)
    {
        if (uri.IndexOfAny(['#', '?']) >= 0)
            throw new UnsupportedModelException("a buffer uri with a query or fragment");
        string name = Uri.UnescapeDataString(uri);
        try { ModelWriter.ValidateBaseName(name); }
        catch (ArgumentException)
        {
            throw new UnsupportedModelException($"a buffer uri that is not a file beside the .gltf ('{uri}')");
        }
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
    /// <returns>The mesh and encoded PNG bytes; no decoded image needs disposal.</returns>
    /// <exception cref="UnsupportedModelException">The content is malformed, truncated or outside
    /// nfty's supported GLB subset.</exception>
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
        if (total != glb.Length) throw new UnsupportedModelException("a GLB whose total length does not match its storage");

        int pos = 12;
        byte[]? json = null, bin = null;
        while (pos < glb.Length)
        {
            if (glb.Length - pos < 8) throw new UnsupportedModelException("an incomplete GLB chunk header");
            int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(glb[pos..]));
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(glb[(pos + 4)..]);
            if (length % 4 != 0) throw new UnsupportedModelException("an unaligned GLB chunk");
            if ((long)pos + 8 + length > total) throw new UnsupportedModelException("a truncated GLB chunk");
            var body = glb.Slice(pos + 8, length);
            if (type == GlbContainer.JsonChunk && json is null && bin is null) json = body.ToArray();
            else if (type == GlbContainer.BinChunk && json is not null && bin is null) bin = body.ToArray();
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
        if (root is null)
            throw new UnsupportedModelException("a structure nfty never writes (a required array is missing)");
        ValidateStructure(root);

        if (root.Buffers.Count != 1) throw new UnsupportedModelException("more than one buffer");
        var buffer = root.Buffers[0];
        if (buffer.ByteLength <= 0) throw new UnsupportedModelException("a buffer without a positive byteLength");
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
        if (glbBin is not null && data.Length - buffer.ByteLength > 3)
            throw new UnsupportedModelException("a GLB buffer with more than three padding bytes");
        for (int i = 0; i < root.BufferViews.Count; i++) _ = View(root, i);

        var prim = root.Meshes[0].Primitives[0];
        if (prim.Mode != 4) throw new UnsupportedModelException($"primitive mode {prim.Mode} (only triangles)");

        float[] positions = Floats(root, data, prim.Attributes.Position, "VEC3", 3);
        float[] normals = Floats(root, data, prim.Attributes.Normal, "VEC3", 3);
        float[] uvs = Floats(root, data, prim.Attributes.TexCoord0, "VEC2", 2);
        uint[] indices = Indices(root, data, prim.Indices);

        var image = root.Images[0];
        if (image.MimeType != "image/png") throw new UnsupportedModelException($"a '{image.MimeType}' texture (only PNG)");
        var (offset, length) = View(root, image.BufferView);
        byte[] png = data.AsSpan(offset, length).ToArray();

        int width, height;
        try
        {
            using var texture = ImageFormats.Load(png);
            if (texture.Metadata.DecodedImageFormat is not PngFormat)
                throw new UnsupportedModelException("a texture that is not PNG");
            width = texture.Width;
            height = texture.Height;
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException or IOException or NotSupportedException)
        {
            throw new UnsupportedModelException("a texture that is not a readable PNG");
        }
        try
        {
            return new ReadModel(new Mesh(positions, normals, uvs, indices, width, height), png);
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
        Only(root.GetProperty("asset"), "the asset", "version", "generator");
        foreach (var scene in Items(root, "scenes")) Only(scene, "a scene", "nodes");
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
        {
            Only(m, "a material", "name", "pbrMetallicRoughness", "alphaMode", "alphaCutoff");
            var pbr = m.GetProperty("pbrMetallicRoughness");
            Only(pbr, "a material's PBR block", "baseColorTexture", "metallicFactor", "roughnessFactor");
            Only(pbr.GetProperty("baseColorTexture"), "a base color texture", "index");
        }
        foreach (var t in Items(root, "textures")) Only(t, "a texture", "sampler", "source");
        foreach (var s in Items(root, "samplers")) Only(s, "a sampler", "magFilter", "minFilter", "wrapS", "wrapT");
    }

    private static IEnumerable<JsonElement> Items(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            throw new UnsupportedModelException($"a missing or invalid '{name}' array");
        return arr.EnumerateArray();
    }

    private static void Only(JsonElement el, string where, params string[] allowed)
    {
        if (el.ValueKind != JsonValueKind.Object) throw new UnsupportedModelException($"{where} that is not an object");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var prop in el.EnumerateObject())
        {
            if (!seen.Add(prop.Name)) throw new UnsupportedModelException($"a duplicate '{prop.Name}' in {where}");
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
        // These are optional metadata/storage members in the writer's record model. All other
        // represented members must be explicit: a missing integer must not silently become zero
        // and happen to name the first scene, material or bufferView.
        foreach (string name in allowed)
            if (name is not ("name" or "generator" or "min" or "max" or "uri" or "target") && !seen.Contains(name))
                throw new UnsupportedModelException($"a missing '{name}' in {where}");
    }

    private static void ValidateStructure(GltfRoot root)
    {
        if (root.Asset.Version != "2.0") throw new UnsupportedModelException("an asset version other than 2.0");
        if (root.Scene != 0 || root.Scenes.Count != 1 || root.Scenes[0].Nodes.Count != 1 || root.Scenes[0].Nodes[0] != 0
            || root.Nodes.Count != 1 || root.Nodes[0].Mesh != 0)
            throw new UnsupportedModelException("a scene other than one root node containing the mesh");
        if (root.Meshes.Count != 1) throw new UnsupportedModelException("more than one mesh");
        if (root.Meshes[0].Primitives.Count != 1) throw new UnsupportedModelException("more than one primitive");
        var primitive = root.Meshes[0].Primitives[0];
        if (primitive.Material != 0 || root.Materials.Count != 1)
            throw new UnsupportedModelException("a primitive without the single material");
        var material = root.Materials[0];
        var pbr = material.PbrMetallicRoughness;
        if (material.AlphaMode != "MASK" || material.AlphaCutoff != 0.5f || pbr.MetallicFactor != 0f
            || pbr.RoughnessFactor != 1f || pbr.BaseColorTexture.Index != 0)
            throw new UnsupportedModelException("material settings other than nfty's textured, nonmetallic, rough MASK material");
        if (root.Textures.Count != 1 || root.Textures[0].Sampler != 0 || root.Textures[0].Source != 0
            || root.Images.Count != 1 || root.Samplers.Count != 1)
            throw new UnsupportedModelException("texture references other than one image and sampler");
        var sampler = root.Samplers[0];
        if (sampler.MagFilter != 9728 || sampler.MinFilter != 9728 || sampler.WrapS != 33071 || sampler.WrapT != 33071)
            throw new UnsupportedModelException("sampling other than NEAREST and CLAMP_TO_EDGE");
        if (root.Accessors.Count != 4 || root.BufferViews.Count != 5)
            throw new UnsupportedModelException("accessors or bufferViews other than the four mesh arrays and texture");
        int[] accessors = [primitive.Attributes.Position, primitive.Attributes.Normal, primitive.Attributes.TexCoord0, primitive.Indices];
        if (accessors.Distinct().Count() != 4)
            throw new UnsupportedModelException("mesh attributes that share an accessor");
        var position = Accessor(root, accessors[0], "VEC3", Float);
        var normal = Accessor(root, accessors[1], "VEC3", Float);
        var uv = Accessor(root, accessors[2], "VEC2", Float);
        var indices = Accessor(root, accessors[3], "SCALAR", UnsignedInt);
        if (indices.Min is not null || indices.Max is not null)
            throw new UnsupportedModelException("min or max bounds on an index accessor");
        if (position.Min is null || position.Max is null)
            throw new UnsupportedModelException("positions without min and max bounds");
        int[] views = [position.BufferView, normal.BufferView, uv.BufferView, indices.BufferView, root.Images[0].BufferView];
        if (views.Distinct().Count() != 5) throw new UnsupportedModelException("mesh arrays and texture that share a bufferView");
        for (int i = 0; i < views.Length; i++)
        {
            if (views[i] < 0 || views[i] >= root.BufferViews.Count) throw new UnsupportedModelException("a bufferView that does not exist");
            var view = root.BufferViews[views[i]];
            int? expected = i < 3 ? 34962 : i == 3 ? 34963 : null;
            if (view.Target != expected || (i < 4 && view.ByteOffset % 4 != 0))
                throw new UnsupportedModelException("a bufferView with an unsupported target or alignment");
        }
    }

    private static (int Offset, int Length) View(GltfRoot root, int index)
    {
        if (index < 0 || index >= root.BufferViews.Count) throw new UnsupportedModelException("a bufferView that does not exist");
        var v = root.BufferViews[index];
        if (v.Buffer != 0 || v.ByteOffset < 0 || v.ByteLength <= 0
            || (long)v.ByteOffset + v.ByteLength > root.Buffers[0].ByteLength)
            throw new UnsupportedModelException("a bufferView outside its buffer");
        return (v.ByteOffset, v.ByteLength);
    }

    private static GltfAccessor Accessor(GltfRoot root, int index, string type, int component)
    {
        if (index < 0 || index >= root.Accessors.Count) throw new UnsupportedModelException("an accessor that does not exist");
        var a = root.Accessors[index];
        if (a.ComponentType != component || a.Type != type)
            throw new UnsupportedModelException($"an accessor of type {a.Type}/{a.ComponentType} (expected {type}/{component})");
        int width = type == "VEC3" ? 3 : type == "VEC2" ? 2 : 1;
        if (a.Count <= 0 || a.Min is { } min && (min.Length != width || min.Any(v => !float.IsFinite(v)))
            || a.Max is { } max && (max.Length != width || max.Any(v => !float.IsFinite(v))))
            throw new UnsupportedModelException("an accessor with an invalid count or bounds");
        if (a.Min is not null && a.Max is not null)
            for (int i = 0; i < width; i++)
                if (a.Min[i] > a.Max[i]) throw new UnsupportedModelException("an accessor with reversed bounds");
        return a;
    }

    private static float[] Floats(GltfRoot root, byte[] data, int accessor, string type, int width)
    {
        var a = Accessor(root, accessor, type, Float);
        var (offset, length) = View(root, a.BufferView);
        if (a.Count < 0 || (long)a.Count * width * 4 > length)
            throw new UnsupportedModelException("an accessor larger than its bufferView");
        var result = new float[a.Count * width];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + (i * 4)));
            if (!float.IsFinite(result[i])) throw new UnsupportedModelException("a nonfinite mesh attribute");
        }
        // The writer's bounds describe the actual samples, not merely an enclosing box. Ignoring
        // false POSITION bounds would hide culling semantics a different glTF viewer observes.
        if (a.Min is not null || a.Max is not null)
            for (int component = 0; component < width; component++)
            {
                float min = result[component], max = min;
                for (int i = component + width; i < result.Length; i += width)
                {
                    min = Math.Min(min, result[i]);
                    max = Math.Max(max, result[i]);
                }
                if (a.Min is not null && a.Min[component] != min || a.Max is not null && a.Max[component] != max)
                    throw new UnsupportedModelException("accessor bounds that do not match its samples");
            }
        return result;
    }

    private static uint[] Indices(GltfRoot root, byte[] data, int accessor)
    {
        var a = Accessor(root, accessor, "SCALAR", UnsignedInt);
        var (offset, length) = View(root, a.BufferView);
        if (a.Count < 0 || (long)a.Count * 4 > length)
            throw new UnsupportedModelException("an accessor larger than its bufferView");
        var result = new uint[a.Count];
        for (int i = 0; i < result.Length; i++)
            result[i] = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + (i * 4)));
        return result;
    }
}
