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

    /// <summary>Writes one model.</summary>
    /// <param name="mesh">The mesh, from <see cref="Riser.TryBuild"/>.</param>
    /// <param name="texture">The picture the mesh was risen from — the same size. Not modified.</param>
    /// <param name="options">Which format to write.</param>
    /// <param name="baseName">The file name without extension, and without any folder.</param>
    /// <returns>The files: one for <see cref="ModelFormat.Glb"/> and <see cref="ModelFormat.GltfEmbedded"/>,
    /// two (<c>.gltf</c> then <c>.bin</c>) for <see cref="ModelFormat.GltfWithBin"/>.</returns>
    /// <exception cref="ArgumentException">The texture is another size than the mesh was risen from,
    /// or the base name is not a plain file name.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="options"/>'s
    /// <see cref="RiseOptions.Format"/> is not one of <see cref="ModelFormat"/>'s named values.</exception>
    public static IReadOnlyList<ModelFile> Write(Mesh mesh, Image<Rgba32> texture, RiseOptions options, string baseName)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(texture);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseName);
        if (baseName.AsSpan().IndexOfAny('/', '\\') >= 0)
            throw new ArgumentException(
                $"'{baseName}' is not a plain file name; the writer names files, the caller chooses the folder.",
                nameof(baseName));
        if (texture.Width != mesh.TextureWidth || texture.Height != mesh.TextureHeight)
            throw new ArgumentException(
                $"The mesh was risen from {mesh.TextureWidth}x{mesh.TextureHeight} art; this texture is "
                + $"{texture.Width}x{texture.Height}.", nameof(texture));

        using var _ = Perf.Measure("ModelWriter.Write");

        var (root, bin) = GltfBuilder.Build(mesh, EncodeTexture(texture));
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

    private static byte[] EncodeTexture(Image<Rgba32> texture)
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
