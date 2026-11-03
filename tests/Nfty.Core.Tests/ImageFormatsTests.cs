using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Nfty.Core.Formats;
using Nfty.Core.Imaging;
using Nfty.Core.Publish;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.Core.Tests;

/// <summary>
/// The six ImageSharp 3.1.11 advisories are suppressed in Directory.Build.props ONLY because these
/// hold: no TIFF decoder is reachable from any file a user hands us, and ICC profiles are not parsed.
/// </summary>
public class ImageFormatsTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "nfty.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>A little-endian TIFF header with an empty IFD and padding: enough for format detection.</summary>
    private static byte[] TiffBytes()
    {
        var b = new byte[128];
        b[0] = (byte)'I'; b[1] = (byte)'I'; b[2] = 42; b[4] = 8;
        return b;
    }

    private static string TempFile(string name, byte[] bytes)
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        string path = Path.Combine(dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] Png(PngEncoder? enc = null)
    {
        using var img = new Image<Rgba32>(2, 2);
        img[0, 0] = new Rgba32(10, 20, 30, 255);
        img[1, 0] = new Rgba32(0, 0, 0, 0);
        img[0, 1] = new Rgba32(200, 100, 50, 255);
        img[1, 1] = new Rgba32(1, 2, 3, 255);
        using var ms = new MemoryStream();
        img.Save(ms, enc ?? new PngEncoder());
        return ms.ToArray();
    }

    private static uint Crc(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        return ~crc;
    }

    /// <summary>The PNG with one extra chunk spliced in right after IHDR.</summary>
    private static byte[] WithChunk(byte[] png, string type, byte[] data)
    {
        const int afterIhdr = 8 + 4 + 4 + 13 + 4;
        var chunk = new byte[12 + data.Length];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)data.Length);
        for (int i = 0; i < 4; i++) chunk[4 + i] = (byte)type[i];
        data.CopyTo(chunk, 8);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + data.Length), Crc(chunk.AsSpan(4, 4 + data.Length)));
        return [.. png[..afterIhdr], .. chunk, .. png[afterIhdr..]];
    }

    /// <summary>An iCCP chunk whose profile claims a tag table of 268 million entries: parsing it is the advisory.</summary>
    private static byte[] HostileIcc()
    {
        var profile = new byte[132];
        BinaryPrimitives.WriteUInt32BigEndian(profile, 132);
        profile[36] = (byte)'a'; profile[37] = (byte)'c'; profile[38] = (byte)'s'; profile[39] = (byte)'p';
        BinaryPrimitives.WriteUInt32BigEndian(profile.AsSpan(128), 0x0FFFFFFF);
        using var ms = new MemoryStream();
        ms.Write("p\0\0"u8);
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true)) z.Write(profile);
        return ms.ToArray();
    }

    private static void SamePixels(Image<Rgba32> a, Image<Rgba32> b)
    {
        Assert.Equal(a.Width, b.Width);
        Assert.Equal(a.Height, b.Height);
        for (int y = 0; y < a.Height; y++)
            for (int x = 0; x < a.Width; x++)
                Assert.Equal(a[x, y], b[x, y]);
    }

    [Fact]
    public async Task A_tiff_renamed_png_is_refused_in_the_products_words_everywhere_a_picture_is_read()
    {
        string path = TempFile("art.png", TiffBytes());
        var bytes = TiffBytes();

        Assert.Equal(ImageFormats.Refusal, Assert.Throws<UnknownImageFormatException>(() => ImageFormats.Load(path)).Message);
        Assert.Throws<UnknownImageFormatException>(() => ImageFormats.Load(bytes));
        Assert.Throws<UnknownImageFormatException>(() => ImageFormats.LoadAny(path));
        Assert.Throws<UnknownImageFormatException>(() => ImageFormats.Identify(path));
        Assert.Throws<UnknownImageFormatException>(() => ImageFormats.Identify(bytes));
        Assert.Throws<UnknownImageFormatException>(() => ImageFormats.DetectFormat(path));
        Assert.Throws<UnknownImageFormatException>(() => ArchiveIo.DecodeImage(bytes));
        Assert.Null(SheetGrid.Read(path));
        await Assert.ThrowsAsync<UnknownImageFormatException>(() => ImageFormats.LoadAsync(path));
    }

    [Fact]
    public void An_archive_whose_variant_is_a_tiff_is_refused_on_read()
    {
        string copy = TempFile("aura.igt", File.ReadAllBytes(Path.Combine(RepoRoot(), "tests", "fixtures", "aura.igt")));
        using (var zip = ZipFile.Open(copy, ZipArchiveMode.Update))
        {
            zip.GetEntry("variants/glow.png")!.Delete();
            using var s = zip.CreateEntry("variants/glow.png").Open();
            s.Write(TiffBytes());
        }
        Assert.Throws<UnknownImageFormatException>(() => IngredientArchive.Read(copy));
    }

    [Fact]
    public void A_png_with_a_hostile_icc_profile_decodes_to_the_same_pixels_and_the_profile_is_never_read()
    {
        byte[] plain = Png();
        byte[] hostile = WithChunk(plain, "iCCP", HostileIcc());
        using var reference = Image.Load<Rgba32>(plain);

        using var viaHelper = ImageFormats.Load(hostile);
        SamePixels(reference, viaHelper);
        Assert.Null(viaHelper.Metadata.IccProfile);               // SkipMetadata really skips the chunk

        // Even where metadata is kept (the sheet grid lives in a PNG text chunk) the profile is only
        // stored, never parsed: decoding returns at once and the pixels are the same.
        string path = TempFile("kept.png", hostile);
        using var kept = (Image<Rgba32>)ImageFormats.LoadAny(path, keepMetadata: true);
        SamePixels(reference, kept);
        Assert.Equal(2, ImageFormats.Identify(path, keepMetadata: true).Width);
    }

    [Fact]
    public void Palette_transparency_survives_skipping_metadata()
    {
        byte[] palette = Png(new PngEncoder { ColorType = PngColorType.Palette });
        using var reference = Image.Load<Rgba32>(palette);
        using var viaHelper = ImageFormats.Load(palette);
        SamePixels(reference, viaHelper);
        Assert.Equal(0, viaHelper[1, 0].A);
    }

    [Fact]
    public void A_jpeg_is_still_read()
    {
        using var img = new Image<Rgba32>(8, 8, new Rgba32(120, 60, 30, 255));
        using var ms = new MemoryStream();
        img.Save(ms, new JpegEncoder());
        using var back = ImageFormats.Load(ms.ToArray());
        Assert.Equal(8, back.Width);
    }

    /// <summary>Every decode in src/ goes through <see cref="ImageFormats"/>, and nothing reaches the
    /// code the suppressed advisories live in.</summary>
    [Fact]
    public void No_source_file_decodes_outside_the_shared_helper_or_touches_the_advisory_code()
    {
        var bad = new List<string>();
        var decode = new Regex(
            @"\bImage\.(Load|LoadAsync|Identify|IdentifyAsync|DetectFormat|DetectFormatAsync)\b|Configuration\.Default\.ImageFormats|new\s+(Png|Jpeg|Tiff|Bmp|Gif|Webp|Tga|Qoi|Pbm)Decoder\b|HistogramEqualization|\bTiff\w*|CcittT[46]|IccProfile\.Entries|CheckIntegrity",
            RegexOptions.Compiled);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                || Path.GetFileName(file) == "ImageFormats.cs") continue;
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string t = lines[i].TrimStart();
                if (t.StartsWith("//") || t.StartsWith('*')) continue;
                if (decode.IsMatch(lines[i])) bad.Add($"{Path.GetRelativePath(RepoRoot(), file)}:{i + 1}: {t}");
            }
        }
        Assert.True(bad.Count == 0, "Decode through Nfty.Core.Imaging.ImageFormats (see Directory.Build.props):\n" + string.Join('\n', bad));
    }
}
