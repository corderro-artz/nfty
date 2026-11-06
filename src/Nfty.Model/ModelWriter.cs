using Nfty.Core.Diagnostics;
using Nfty.Model.Gltf;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model;

/// <summary>
/// Writes a risen mesh and its texture as model files. Every format is serialized from ONE glTF
/// object model; they differ only in where the buffer lives, and that is set in the object model,
/// never by editing serialized text.
/// </summary>
public static class ModelWriter
{
    // Stated in full so the source image's own PNG metadata (palette color type, bit depth, text,
    // resolution, interlacing) can never reach a model: the same pixels always encode to the same
    // bytes. SkipMetadata alone does not cover InterlaceMethod — ImageSharp 3.1.11 falls back to
    // the source image's own PngMetadata.InterlaceMethod when the encoder leaves it null — so it is
    // stated here explicitly, same as ColorType and BitDepth.
    private static readonly PngEncoder TextureEncoder = new()
    {
        ColorType = PngColorType.RgbWithAlpha,
        BitDepth = PngBitDepth.Bit8,
        InterlaceMethod = PngInterlaceMode.None,
        SkipMetadata = true,
    };

    private const string DataUriPrefix = "data:application/octet-stream;base64,";

    /// <summary>Writes one model, compacting bit-identical vertices before serialization.</summary>
    /// <param name="mesh">The mesh, from <see cref="Riser.TryBuild"/>.</param>
    /// <param name="art">The picture the mesh was risen from — the same size. Not modified. The
    /// texture is the <see cref="FaceAtlas"/> built from it.</param>
    /// <param name="options">Only <see cref="RiseOptions.Format"/> is used. The mesh already carries
    /// its pivot and scale.</param>
    /// <param name="baseName">The file name without extension or folder; must be safe on Windows
    /// and other supported platforms. Valid names are retained exactly, never sanitized.</param>
    /// <param name="faces">The stack's face owners (the atlas halves), or <c>null</c> for a single
    /// picture. Borrowed, not disposed.</param>
    /// <returns>The files: one for <see cref="ModelFormat.Glb"/> and <see cref="ModelFormat.GltfEmbedded"/>,
    /// two (<c>.gltf</c> then <c>.bin</c>) for <see cref="ModelFormat.GltfWithBin"/>.</returns>
    /// <exception cref="ArgumentException">The art or faces are another size than the mesh was risen from,
    /// or the base name is not a plain file name.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="options"/>'s
    /// <see cref="RiseOptions.Format"/> is not one of <see cref="ModelFormat"/>'s named values.</exception>
    public static IReadOnlyList<ModelFile> Write(Mesh mesh, Image<Rgba32> art, RiseOptions options, string baseName,
        StackFaces? faces = null)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(art);
        ArgumentNullException.ThrowIfNull(options);
        ValidateBaseName(baseName);
        if (FaceAtlas.WidthFor(art.Width) != mesh.TextureWidth || art.Height != mesh.TextureHeight)
            throw new ArgumentException(
                $"The mesh was risen from {(mesh.TextureWidth - 1) / 2}x{mesh.TextureHeight} art; this art is "
                + $"{art.Width}x{art.Height}.", nameof(art));

        using var _ = Perf.Measure("ModelWriter.Write");

        using var atlas = FaceAtlas.Build(art, faces);
        return WriteAtlas(mesh, atlas, options, baseName);
    }

    /// <summary>Writes one model from a texture that is already the mesh's <see cref="FaceAtlas"/>:
    /// what a viewer holds when it was handed a mesh and its atlas but not the art they came from.
    /// The bytes are exactly those <see cref="Write"/> produces for the same atlas. Bit-identical
    /// vertices may be compacted, changing vertex numbering while preserving geometry.</summary>
    /// <param name="mesh">The mesh.</param>
    /// <param name="atlas">The atlas the mesh's UVs address; not modified.</param>
    /// <param name="options">Only <see cref="RiseOptions.Format"/> is used. The mesh already carries
    /// its pivot and scale.</param>
    /// <param name="baseName">The file name without extension or folder; must be safe on Windows
    /// and other supported platforms. Valid names are retained exactly, never sanitized.</param>
    /// <returns>The files, as for <see cref="Write"/>.</returns>
    /// <exception cref="ArgumentException">The atlas is not the size the mesh addresses, or the base
    /// name is not a plain file name.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="options"/>'s format is not named.</exception>
    public static IReadOnlyList<ModelFile> WriteAtlas(Mesh mesh, Image<Rgba32> atlas, RiseOptions options, string baseName)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(atlas);
        ArgumentNullException.ThrowIfNull(options);
        ValidateBaseName(baseName);
        if (atlas.Width != mesh.TextureWidth || atlas.Height != mesh.TextureHeight)
            throw new ArgumentException(
                $"The mesh addresses a {mesh.TextureWidth}x{mesh.TextureHeight} atlas; this one is "
                + $"{atlas.Width}x{atlas.Height}.", nameof(atlas));

        var (root, bin) = GltfBuilder.Build(mesh.Compact(), EncodeTexture(atlas));
        return options.Format switch
        {
            ModelFormat.Glb => new[]
            {
                new ModelFile(baseName + ".glb", GlbContainer.Frame(GltfJson.Serialize(root), bin)),
            },
            // The .bin is named by a RELATIVE URI, which glTF requires to be percent-encoded; the
            // file on disk keeps the name the caller gave.
            ModelFormat.GltfWithBin => new[]
            {
                new ModelFile(baseName + ".gltf", GltfJson.Serialize(WithBufferUri(root, Uri.EscapeDataString(baseName + ".bin")))),
                new ModelFile(baseName + ".bin", bin),
            },
            ModelFormat.GltfEmbedded => new[]
            {
                new ModelFile(baseName + ".gltf", GltfJson.Serialize(WithBufferUri(root, DataUriPrefix + Convert.ToBase64String(bin)))),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.Format, "Unknown model format."),
        };
    }

    // The explicit Windows restrictions are portable, unlike Path.GetInvalidFileNameChars.
    // Leading dots and internal spaces are valid names even though the batch's style sanitizer
    // trims outer dots/spaces. Public writers validate a name; they never rename it.
    internal static void ValidateBaseName(string baseName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseName);
        int dot = baseName.IndexOf('.');
        string stem = (dot < 0 ? baseName : baseName[..dot]).TrimEnd(' ');
        if (baseName.Any(c => c < 0x20 || "\\/:*?\"<>|".Contains(c))
            || baseName[^1] is '.' or ' ' || ModelBatch.IsDeviceName(stem))
            throw new ArgumentException(
                $"'{baseName}' is not a plain, portable file name; the writer names files, the caller chooses the folder.",
                nameof(baseName));
    }

    private static byte[] EncodeTexture(Image<Rgba32> texture)
    {
        // Image.Load of an animated/multi-frame PNG yields every frame, but Riser only ever rises
        // frame 0 (the root frame, via ProcessPixelRows). Encoding the whole multi-frame image would
        // embed frames nobody asked for and that have nothing to do with the mesh, breaking the
        // "same pixels, same bytes" determinism promise. Encode frame 0 alone.
        if (texture.Frames.Count > 1)
        {
            using var frame0 = texture.Frames.CloneFrame(0);
            return EncodeSingleFrame(frame0);
        }
        return EncodeSingleFrame(texture);
    }

    private static byte[] EncodeSingleFrame(Image<Rgba32> texture)
    {
        using var stream = new MemoryStream();
        texture.Save(stream, TextureEncoder);
        return stream.ToArray();
    }

    /// <summary>The same document with the buffer's location set — in the object model, never by
    /// editing serialized text (mixel's D2).</summary>
    private static GltfRoot WithBufferUri(GltfRoot root, string uri) =>
        root with { Buffers = [root.Buffers[0] with { Uri = uri }] };
}
