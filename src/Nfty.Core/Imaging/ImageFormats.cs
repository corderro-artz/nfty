using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Imaging;

/// <summary>
/// THE one way nfty decodes a picture. Every <c>Image.Load</c>, <c>Image.Identify</c> and
/// <c>DetectFormat</c> in the product goes through here, so exactly two decoders (PNG, JPEG) are
/// reachable and no other.
/// </summary>
/// <remarks>
/// <para>Why: ImageSharp is pinned at 3.1.11 (see Directory.Packages.props) and that version carries
/// six advisories whose fixes exist only in 3.2.0+ (which needs a license key). Five are TIFF
/// decoders/encoders or <c>HistogramEqualization</c>, none of which nfty ever calls; the sixth is ICC
/// CLUT parsing, reached only through <c>IccProfile.Entries</c>/<c>CheckIntegrity</c>, which nothing here
/// touches. Registering only PNG + JPEG makes the TIFF half unreachable from any file a user hands us,
/// whatever its extension says. <c>SkipMetadata</c> skips optional metadata, but the pinned PNG decoder
/// still retains raw ICC bytes. Typed loads explicitly drop that lazy profile with <c>NoIcc</c>;
/// no overload reads its parsed entries or header.</para>
/// <para>A file in any other format is refused with <see cref="UnknownImageFormatException"/> (the type
/// every existing caller already handles) carrying a message in the product's words. The
/// <c>keepMetadata</c> overloads exist for ONE reason: the sprite sheet's <c>nfty-grid</c> text chunk
/// lives in PNG metadata. There a PNG's ICC chunk is stored unparsed and written back untouched.</para>
/// <para>Encoders are untouched, so no output byte changes.</para>
/// </remarks>
public static class ImageFormats
{
    /// <summary>What a refused file is told.</summary>
    public const string Refusal =
        "nfty opens only PNG and JPEG pictures; this file is in another format (or is not a picture).";

    private static readonly Configuration Config = new(new PngConfigurationModule(), new JpegConfigurationModule());

    private static readonly DecoderOptions Skipping = new() { Configuration = Config, SkipMetadata = true };
    private static readonly DecoderOptions Keeping = new() { Configuration = Config, SkipMetadata = false };

    private static DecoderOptions Pick(bool keepMetadata) => keepMetadata ? Keeping : Skipping;

    /// <summary>The PNG decoder in 3.1.11 stores an <c>iCCP</c> chunk even under <c>SkipMetadata</c> (parsing
    /// is lazy, on <c>Entries</c>/<c>Header</c>, which nothing here reads). Dropping it makes that a fact
    /// rather than a habit, and keeps a stray profile out of every PNG nfty re-encodes.</summary>
    private static Image<Rgba32> NoIcc(Image<Rgba32> image)
    {
        image.Metadata.IccProfile = null;
        return image;
    }

    private static UnknownImageFormatException Refused() => new(Refusal);

    /// <summary>Decodes a picture file.</summary>
    /// <param name="path">The PNG or JPEG file.</param>
    /// <returns>The decoded image; the caller owns it.</returns>
    /// <exception cref="UnknownImageFormatException">The file is neither PNG nor JPEG.</exception>
    public static Image<Rgba32> Load(string path)
    {
        try { return NoIcc(Image.Load<Rgba32>(Skipping, path)); }
        catch (UnknownImageFormatException) { throw Refused(); }
    }

    /// <summary>Decodes picture bytes.</summary>
    /// <param name="bytes">The encoded PNG or JPEG.</param>
    /// <returns>The decoded image; the caller owns it.</returns>
    /// <exception cref="UnknownImageFormatException">The bytes are neither PNG nor JPEG.</exception>
    public static Image<Rgba32> Load(byte[] bytes)
    {
        try { return NoIcc(Image.Load<Rgba32>(Skipping, bytes)); }
        catch (UnknownImageFormatException) { throw Refused(); }
    }

    /// <summary>Decodes a picture file without fixing the pixel type.</summary>
    /// <param name="path">The PNG or JPEG file.</param>
    /// <param name="keepMetadata">Whether PNG text chunks are read (the sheet grid lives in one).</param>
    /// <returns>The decoded image; the caller owns it.</returns>
    /// <exception cref="UnknownImageFormatException">The file is neither PNG nor JPEG.</exception>
    public static Image LoadAny(string path, bool keepMetadata = false)
    {
        try { return Image.Load(Pick(keepMetadata), path); }
        catch (UnknownImageFormatException) { throw Refused(); }
    }

    /// <summary>Decodes a picture file asynchronously.</summary>
    /// <param name="path">The PNG or JPEG file.</param>
    /// <param name="ct">Cancels the decode.</param>
    /// <returns>The decoded image; the caller owns it.</returns>
    /// <exception cref="UnknownImageFormatException">The file is neither PNG nor JPEG.</exception>
    public static async Task<Image<Rgba32>> LoadAsync(string path, CancellationToken ct = default)
    {
        try { return NoIcc(await Image.LoadAsync<Rgba32>(Skipping, path, ct).ConfigureAwait(false)); }
        catch (UnknownImageFormatException) { throw Refused(); }
    }

    /// <summary>Reads a picture's size (and, when asked, its metadata) without decoding pixels.</summary>
    /// <param name="path">The PNG or JPEG file.</param>
    /// <param name="keepMetadata">Whether PNG text chunks are read.</param>
    /// <returns>What the header says.</returns>
    /// <exception cref="UnknownImageFormatException">The file is neither PNG nor JPEG.</exception>
    public static ImageInfo Identify(string path, bool keepMetadata = false)
    {
        try { return Image.Identify(Pick(keepMetadata), path); }
        catch (UnknownImageFormatException) { throw Refused(); }
    }

    /// <summary>Reads a picture's size without decoding pixels.</summary>
    /// <param name="bytes">The encoded PNG or JPEG.</param>
    /// <returns>What the header says.</returns>
    /// <exception cref="UnknownImageFormatException">The bytes are neither PNG nor JPEG.</exception>
    public static ImageInfo Identify(byte[] bytes)
    {
        try { return Image.Identify(Skipping, bytes); }
        catch (UnknownImageFormatException) { throw Refused(); }
    }

    /// <summary>Names a file's format, among the two nfty reads.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The format.</returns>
    /// <exception cref="UnknownImageFormatException">The file is neither PNG nor JPEG.</exception>
    public static IImageFormat DetectFormat(string path)
    {
        try { return Image.DetectFormat(Skipping, path); }
        catch (UnknownImageFormatException) { throw Refused(); }
    }
}
