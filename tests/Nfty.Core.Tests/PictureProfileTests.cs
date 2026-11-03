using Nfty.Core.Editing;
using Nfty.Core.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.Core.Tests;

/// <summary>
/// An imported picture's color profile: read by nfty's own reader, converted to sRGB by one function.
/// Expected pixels come from PUBLISHED D65 matrices applied directly (no D50 round trip), so they are
/// not a restatement of the code under test.
/// </summary>
public class PictureProfileTests
{
    private static Rgba32 Px(byte r, byte g, byte b, byte a = 255) => new(r, g, b, a);

    private static Rgba32[] Convert(byte[] profile, params Rgba32[] pixels)
    {
        var png = IccProfiles.PngWith(profile, pixels);
        var p = PictureProfile.FromFile(png);
        Assert.Equal(PictureProfileKind.Convertible, p.Kind);
        using var img = ImageFormats.Load(png);
        p.ConvertToSrgb(img);
        return Enumerable.Range(0, pixels.Length).Select(x => img[x, 0]).ToArray();
    }

    private static byte[] P3 => IccProfiles.MatrixProfile("Display P3", IccProfiles.P3, IccProfiles.Trc.SrgbPara);

    private static byte[] Adobe => IccProfiles.MatrixProfile("Adobe RGB (1998)", IccProfiles.Adobe, IccProfiles.Trc.AdobeGamma);

    [Fact]
    public void Display_p3_converts_to_the_published_srgb_values()
    {
        var o = Convert(P3, Px(255, 0, 0), Px(0, 255, 0), Px(200, 150, 100), Px(30, 180, 90), Px(128, 128, 128), Px(255, 255, 255));
        Assert.Equal(Px(255, 0, 0), o[0]);       // out of sRGB gamut: clips per channel
        Assert.Equal(Px(0, 255, 0), o[1]);
        Assert.Equal(Px(209, 147, 91), o[2]);    // 209.175, 147.404, 91.019
        Assert.Equal(Px(0, 183, 78), o[3]);      // 0 (clipped), 183.267, 77.988
        Assert.Equal(Px(128, 128, 128), o[4]);   // white point shared: grays stay gray
        Assert.Equal(Px(255, 255, 255), o[5]);
    }

    [Fact]
    public void Adobe_rgb_mid_gray_brightens_by_one_code()
    {
        var o = Convert(Adobe, Px(128, 128, 128), Px(200, 150, 100));
        Assert.Equal(Px(129, 129, 129), o[0]);   // 128 at gamma 2.2 -> 129.03 in sRGB
        Assert.Equal(Px(217, 151, 97), o[1]);    // 217.229, 151.408, 97.076
    }

    [Fact]
    public void Alpha_is_untouched()
    {
        var o = Convert(P3, Px(200, 150, 100, 77), Px(10, 20, 30, 0));
        Assert.Equal(77, o[0].A);
        Assert.Equal(0, o[1].A);
    }

    [Fact]
    public void An_srgb_profile_is_recognised_and_asks_nothing()
    {
        foreach (var trc in new[] { IccProfiles.Trc.SrgbPara, IccProfiles.Trc.SrgbTable })
        {
            var p = PictureProfile.FromProfile(IccProfiles.MatrixProfile("my monitor", IccProfiles.Srgb, trc));
            Assert.Equal(PictureProfileKind.Srgb, p.Kind);
            Assert.Null(p.Note);
            Assert.False(p.CanConvert);
        }
    }

    [Fact]
    public void Run_through_the_math_an_srgb_profile_is_the_identity()
    {
        // Recognised as sRGB it is never converted; this proves the math would agree if it were.
        Assert.True(IccRgbProfile.TryRead(IccProfiles.MatrixProfile("x", IccProfiles.Srgb, IccProfiles.Trc.SrgbPara), out var rgb, out _));
        var p = PictureProfile.ConvertibleForTests(rgb!);
        using var img = new Image<Rgba32>(256, 1);
        for (int v = 0; v < 256; v++) img[v, 0] = Px((byte)v, (byte)(255 - v), (byte)(v * 7 % 256));
        p.ConvertToSrgb(img);
        for (int v = 0; v < 256; v++) Assert.Equal(Px((byte)v, (byte)(255 - v), (byte)(v * 7 % 256)), img[v, 0]);
    }

    [Fact]
    public void A_v4_name_is_read_and_shown()
    {
        var p = PictureProfile.FromProfile(IccProfiles.MatrixProfile("Display P3", IccProfiles.P3, IccProfiles.Trc.SrgbPara, v4Name: true));
        Assert.Equal("Display P3", p.Name);
        Assert.Equal("This picture uses Display P3. Convert to sRGB so it looks the same everywhere.", p.Note);
    }

    [Fact]
    public void Profiles_nfty_cannot_convert_say_so_and_import_as_is()
    {
        foreach (var bytes in new[] { IccProfiles.LutOnlyProfile("Fancy LUT"), IccProfiles.CmykProfile("Fancy LUT") })
        {
            var p = PictureProfile.FromProfile(bytes);
            Assert.Equal(PictureProfileKind.Unsupported, p.Kind);
            Assert.Equal("Uses Fancy LUT; nfty can't convert this profile — imported as is.", p.Note);
            using var img = new Image<Rgba32>(1, 1);
            Assert.Throws<InvalidOperationException>(() => p.ConvertToSrgb(img));
        }
    }

    [Fact]
    public void A_lut_only_srgb_profile_is_taken_at_its_word()
    {
        Assert.Equal(PictureProfileKind.Srgb, PictureProfile.FromProfile(IccProfiles.LutOnlyProfile("sRGB v4 ICC preference")).Kind);
    }

    [Fact]
    public void A_picture_with_no_profile_has_nothing_to_say()
    {
        var p = PictureProfile.FromFile(IccProfiles.PngWith(null, Px(1, 2, 3)));
        Assert.Equal(PictureProfileKind.None, p.Kind);
        Assert.Null(p.Note);
    }

    [Fact]
    public void A_jpeg_profile_split_across_segments_is_reassembled()
    {
        var p = PictureProfile.FromFile(IccProfiles.JpegWith(P3, 3));
        Assert.Equal(PictureProfileKind.Convertible, p.Kind);
        Assert.Equal("Display P3", p.Name);
    }

    [Fact]
    public void A_jpeg_with_a_missing_segment_is_refused_not_guessed()
    {
        var jpg = IccProfiles.JpegWith(P3, 3);
        // Renumber segment 3 of 3 as 2 of 3: two "2"s, no "3".
        int third = FindNth(jpg, "ICC_PROFILE\0"u8.ToArray(), 3);
        jpg[third + 12] = 2;
        Assert.Equal(PictureProfileKind.Unsupported, PictureProfile.FromFile(jpg).Kind);
    }

    private static int FindNth(byte[] hay, byte[] needle, int n)
    {
        for (int i = 0, seen = 0; i <= hay.Length - needle.Length; i++)
            if (hay.AsSpan(i, needle.Length).SequenceEqual(needle) && ++seen == n) return i;
        return -1;
    }

    [Fact]
    public void An_oversized_profile_is_refused_before_it_is_inflated_past_the_cap()
    {
        var huge = new byte[IccProfileBytes.MaxProfileBytes + 10];   // zeros: inflates from a tiny stream
        var p = PictureProfile.FromFile(IccProfiles.PngWith(huge, Px(1, 2, 3)));
        Assert.Equal(PictureProfileKind.Unsupported, p.Kind);
        Assert.Equal("Uses an embedded color profile; nfty can't convert this profile — imported as is.", p.Note);
    }

    [Fact]
    public void A_profile_claiming_too_many_tags_is_refused()
    {
        var bytes = (byte[])P3.Clone();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(128), 0x0FFFFFFF);
        Assert.Equal(PictureProfileKind.Unsupported, PictureProfile.FromProfile(bytes).Kind);
    }

    [Fact]
    public void Truncated_and_corrupted_profiles_never_throw()
    {
        var profile = P3;
        for (int n = 0; n <= profile.Length; n++) _ = PictureProfile.FromProfile(profile.AsSpan(0, n));

        var png = IccProfiles.PngWith(profile, Px(1, 2, 3));
        for (int n = 0; n <= png.Length; n++) _ = PictureProfile.FromFile(png.AsSpan(0, n));
        var jpg = IccProfiles.JpegWith(profile, 2);
        for (int n = 0; n <= Math.Min(jpg.Length, 2000); n++) _ = PictureProfile.FromFile(jpg.AsSpan(0, n));

        var rng = new Random(1234);
        for (int i = 0; i < 3000; i++)
        {
            var bad = (byte[])profile.Clone();
            for (int k = 0; k < 4; k++) bad[rng.Next(bad.Length)] = (byte)rng.Next(256);
            var p = PictureProfile.FromProfile(bad);
            if (p.CanConvert)
            {
                using var img = new Image<Rgba32>(4, 1, new Rgba32(10, 128, 250, 255));
                p.ConvertToSrgb(img);   // whatever the numbers say, conversion runs
            }
            var badJpg = (byte[])jpg.Clone();
            for (int k = 0; k < 4; k++) badJpg[rng.Next(Math.Min(badJpg.Length, profile.Length + 64))] = (byte)rng.Next(256);
            _ = PictureProfile.FromFile(badJpg);
        }
    }

    [Fact]
    public void The_profile_name_cannot_reshape_the_sentence()
    {
        var p = PictureProfile.FromProfile(IccProfiles.MatrixProfile("Evil\nline\ttwo", IccProfiles.P3, IccProfiles.Trc.SrgbPara));
        Assert.DoesNotContain('\n', p.Note!);
        Assert.Equal("Evillinetwo", p.Name);
    }

    [Fact]
    public void Import_load_converts_by_default_rule_and_keeps_when_asked()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        string path = Path.Combine(dir, "art.png");
        File.WriteAllBytes(path, IccProfiles.PngWith(P3, Px(200, 150, 100)));

        using (var conv = ImageImport.Load(path, convertToSrgb: true, out var p))
        {
            Assert.Equal(PictureProfileKind.Convertible, p.Kind);
            Assert.Equal(Px(209, 147, 91), conv[0, 0]);
        }
        using var kept = ImageImport.Load(path, convertToSrgb: false, out _);
        Assert.Equal(Px(200, 150, 100), kept[0, 0]);
    }

    [Fact]
    public void An_unreadable_picture_is_reported_in_product_words_never_the_decoders()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        string tiff = Path.Combine(dir, "art.png");
        File.WriteAllBytes(tiff, [(byte)'I', (byte)'I', 42, 0, 8, 0, 0, 0, .. new byte[120]]);
        var e1 = Assert.Throws<PictureUnreadableException>(() => ImageImport.Load(tiff, true, out _));
        Assert.Equal("“art.png” is not a PNG or JPEG picture. Save it as PNG from your drawing program and import that.", e1.Message);

        string damaged = Path.Combine(dir, "broken.png");
        var png = IccProfiles.PngWith(null, Px(1, 2, 3));
        File.WriteAllBytes(damaged, png[..(png.Length - 20)]);
        var e2 = Assert.Throws<PictureUnreadableException>(() => ImageImport.Load(damaged, true, out _));
        Assert.Equal("“broken.png” looks like a picture, but its data is damaged. Export it again from your drawing program and import the new file.", e2.Message);

        var e3 = Assert.Throws<PictureUnreadableException>(() => ImageImport.Load(Path.Combine(dir, "gone.png"), true, out _));
        Assert.Equal("“gone.png” is no longer there. Choose the file again.", e3.Message);
        Assert.Null(e3.InnerException);
    }
}
