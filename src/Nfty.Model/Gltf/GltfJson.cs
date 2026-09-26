using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nfty.Model.Gltf;

/// <summary>The one serializer configuration every format uses.</summary>
internal static class GltfJson
{
    // Compact; nulls omitted (an absent target / min / uri is how glTF says "none"); relaxed escaping
    // so a base64 '+' in an embedded buffer stays '+' rather than becoming + — the file is not
    // HTML, and the relaxed encoder is deterministic just the same.
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>UTF-8 JSON, no BOM.</summary>
    public static byte[] Serialize(GltfRoot root) => JsonSerializer.SerializeToUtf8Bytes(root, Options);
}
