using System.Text.Json.Serialization;

namespace Nfty.Model.Gltf;

// THE ONE glTF OBJECT MODEL. Every format is serialized from a GltfRoot; the three differ only in
// Buffers[0].Uri, which is SET HERE, in the object model, never patched into serialized text (the
// reference implementation's D2 string-replace matched nothing the day its serializer changed
// spacing, and wrote a .gltf pointing at a file that was never written). Property names are stated
// explicitly — glTF mixes camelCase with "POSITION" and "TEXCOORD_0" — and the declaration order
// here is the order in the file.

/// <summary>The glTF document.</summary>
internal sealed record GltfRoot(
    [property: JsonPropertyName("asset")] GltfAsset Asset,
    [property: JsonPropertyName("scene")] int Scene,
    [property: JsonPropertyName("scenes")] IReadOnlyList<GltfScene> Scenes,
    [property: JsonPropertyName("nodes")] IReadOnlyList<GltfNode> Nodes,
    [property: JsonPropertyName("meshes")] IReadOnlyList<GltfMesh> Meshes,
    [property: JsonPropertyName("materials")] IReadOnlyList<GltfMaterial> Materials,
    [property: JsonPropertyName("textures")] IReadOnlyList<GltfTexture> Textures,
    [property: JsonPropertyName("samplers")] IReadOnlyList<GltfSampler> Samplers,
    [property: JsonPropertyName("images")] IReadOnlyList<GltfImage> Images,
    [property: JsonPropertyName("accessors")] IReadOnlyList<GltfAccessor> Accessors,
    [property: JsonPropertyName("bufferViews")] IReadOnlyList<GltfBufferView> BufferViews,
    [property: JsonPropertyName("buffers")] IReadOnlyList<GltfBuffer> Buffers);

/// <summary><c>asset</c>: the spec version and who wrote it — no build version, so a release
/// cannot change a model's bytes.</summary>
internal sealed record GltfAsset(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("generator")] string Generator);

/// <summary>A scene: the root nodes.</summary>
internal sealed record GltfScene([property: JsonPropertyName("nodes")] IReadOnlyList<int> Nodes);

/// <summary>A node placing one mesh.</summary>
internal sealed record GltfNode(
    [property: JsonPropertyName("mesh")] int Mesh,
    [property: JsonPropertyName("name")] string Name);

/// <summary>A mesh.</summary>
internal sealed record GltfMesh(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("primitives")] IReadOnlyList<GltfPrimitive> Primitives);

/// <summary>One primitive: attributes, indices, material, mode.</summary>
internal sealed record GltfPrimitive(
    [property: JsonPropertyName("attributes")] GltfAttributes Attributes,
    [property: JsonPropertyName("indices")] int Indices,
    [property: JsonPropertyName("material")] int Material,
    [property: JsonPropertyName("mode")] int Mode);

/// <summary>The vertex attributes, as a record rather than a dictionary so the key order is fixed.</summary>
internal sealed record GltfAttributes(
    [property: JsonPropertyName("POSITION")] int Position,
    [property: JsonPropertyName("NORMAL")] int Normal,
    [property: JsonPropertyName("TEXCOORD_0")] int TexCoord0);

/// <summary>The one material. It has no <c>doubleSided</c> member at all (D3): a closed solid never
/// shows a back face.</summary>
internal sealed record GltfMaterial(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("pbrMetallicRoughness")] GltfPbr PbrMetallicRoughness,
    [property: JsonPropertyName("alphaMode")] string AlphaMode,
    [property: JsonPropertyName("alphaCutoff")] float AlphaCutoff);

/// <summary>PBR block: the art as base color, not metallic, fully rough.</summary>
internal sealed record GltfPbr(
    [property: JsonPropertyName("baseColorTexture")] GltfTextureRef BaseColorTexture,
    [property: JsonPropertyName("metallicFactor")] float MetallicFactor,
    [property: JsonPropertyName("roughnessFactor")] float RoughnessFactor);

/// <summary>A reference to a texture by index.</summary>
internal sealed record GltfTextureRef([property: JsonPropertyName("index")] int Index);

/// <summary>A texture: a sampler over an image.</summary>
internal sealed record GltfTexture(
    [property: JsonPropertyName("sampler")] int Sampler,
    [property: JsonPropertyName("source")] int Source);

/// <summary>Sampling: filters and wrapping.</summary>
internal sealed record GltfSampler(
    [property: JsonPropertyName("magFilter")] int MagFilter,
    [property: JsonPropertyName("minFilter")] int MinFilter,
    [property: JsonPropertyName("wrapS")] int WrapS,
    [property: JsonPropertyName("wrapT")] int WrapT);

/// <summary>An image held in a bufferView.</summary>
internal sealed record GltfImage(
    [property: JsonPropertyName("bufferView")] int BufferView,
    [property: JsonPropertyName("mimeType")] string MimeType);

/// <summary>A typed view of a bufferView. <c>min</c>/<c>max</c> are omitted when null.</summary>
internal sealed record GltfAccessor(
    [property: JsonPropertyName("bufferView")] int BufferView,
    [property: JsonPropertyName("componentType")] int ComponentType,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("min")] float[]? Min,
    [property: JsonPropertyName("max")] float[]? Max);

/// <summary>A byte range of the buffer. <c>target</c> is omitted when null — the image's view has none.</summary>
internal sealed record GltfBufferView(
    [property: JsonPropertyName("buffer")] int Buffer,
    [property: JsonPropertyName("byteOffset")] int ByteOffset,
    [property: JsonPropertyName("byteLength")] int ByteLength,
    [property: JsonPropertyName("target")] int? Target);

/// <summary>The one buffer. <c>uri</c> is null in a GLB (the BIN chunk is the buffer), a relative
/// file name for glTF + .bin, a data URI for embedded glTF.</summary>
internal sealed record GltfBuffer(
    [property: JsonPropertyName("byteLength")] int ByteLength,
    [property: JsonPropertyName("uri")] string? Uri);
