using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

public class ColorizerTests
{
    [Fact]
    public void Hsv_maps_grayscale_value_to_v_with_rolled_hue()
    {
        using var map = new Image<Rgba32>(1, 1);
        map[0, 0] = new Rgba32(128, 128, 128, 255); // g ~ 0.502

        using var outImg = Colorizer.Apply(map, h: 0, s: 1.0, ColorModel.Hsv);

        var px = outImg[0, 0];
        Assert.Equal(128, px.R);
        Assert.Equal(0, px.G);
        Assert.Equal(0, px.B);
        Assert.Equal(255, px.A);
    }

    /// <summary>Colorizes a single pixel of grayscale <paramref name="gray"/> and returns it.</summary>
    private static Rgba32 Colorize(byte gray, double h, double s, ColorModel model)
    {
        using var map = new Image<Rgba32>(1, 1);
        map[0, 0] = new Rgba32(gray, gray, gray, 255);
        using var outImg = Colorizer.Apply(map, h, s, model);
        return outImg[0, 0];
    }

    // --- edge grays (spec 5.3 / 10) ---

    [Theory]
    [InlineData(0)]
    [InlineData(120)]
    [InlineData(322)]
    public void Hsv_g0_is_black_for_any_hue(double hue)
    {
        // g = 0 => V = 0 => black regardless of hue or saturation.
        var px = Colorize(0, hue, s: 1.0, ColorModel.Hsv);

        Assert.Equal(new Rgba32(0, 0, 0, 255), px);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(120)]
    [InlineData(322)]
    public void Hsl_g0_is_black_for_any_hue(double hue)
    {
        // g = 0 => L = 0 => black regardless of hue or saturation.
        var px = Colorize(0, hue, s: 1.0, ColorModel.Hsl);

        Assert.Equal(new Rgba32(0, 0, 0, 255), px);
    }

    [Fact]
    public void Hsl_g1_is_white()
    {
        // Documented HSL edge: L = 1 washes out to white whatever the hue/saturation.
        var px = Colorize(255, h: 322, s: 1.0, ColorModel.Hsl);

        Assert.Equal(new Rgba32(255, 255, 255, 255), px);
    }

    [Fact]
    public void Hsv_g1_stays_fully_colored()
    {
        // Documented HSV edge, and the reason dynamic layers use HSV: V = 1 is the pure hue,
        // NOT white — this is exactly where the two models diverge.
        var px = Colorize(255, h: 120, s: 1.0, ColorModel.Hsv);

        Assert.Equal(new Rgba32(0, 255, 0, 255), px);
    }

    [Fact]
    public void Hsl_maps_grayscale_value_to_l_with_rolled_hue()
    {
        // g ~ 0.502 becomes L, and in HSL L = 0.5 at full saturation is the VIVID hue — so mid
        // gray colorizes to bright green, not the dark green the same gray gives under HSV
        // (see Hsv_maps_grayscale_value_to_v_with_rolled_hue, where 128 => R=128). This is the
        // substantive difference between the two models at the same value-map input.
        var px = Colorize(128, h: 120, s: 1.0, ColorModel.Hsl);

        Assert.Equal(1, px.R);
        Assert.Equal(255, px.G);
        Assert.Equal(1, px.B);
        Assert.Equal(255, px.A);
    }

    [Fact]
    public void Zero_saturation_is_gray_at_the_value_map_level()
    {
        // S = 0 keeps the value-map's own lightness: the colorizer preserves value exactly.
        var px = Colorize(128, h: 322, s: 0.0, ColorModel.Hsv);

        Assert.Equal(new Rgba32(128, 128, 128, 255), px);
    }

    // --- the colorization table (spec 5.3) ---
    //
    // Apply resolves the (h, s, model) conversion once into a 256-entry table keyed on the source
    // byte and indexes it per pixel, instead of re-running the HSV/HSL maths for every pixel. That
    // is only legal if the table reproduces the per-pixel conversion exactly, for every gray and
    // every hue sector, under BOTH colour models. These tests pin that equivalence against
    // ColorConvert directly, so they fail if the table is ever built from the wrong key, sized
    // wrong, quantized, cached across calls, or allowed to drift from the conversion it stands in for.

    /// <summary>
    /// The grays that can break a lookup table: both ends, the values either side of the 0.5
    /// rounding midpoint, and a spread between. Laid out as one 4x4 image so a single Apply call
    /// covers all sixteen.
    /// </summary>
    private static readonly byte[] GraySpread =
        { 0, 1, 2, 17, 42, 63, 64, 85, 127, 128, 129, 170, 200, 253, 254, 255 };

    private static Image<Rgba32> SpreadMap()
    {
        var img = new Image<Rgba32>(4, 4);
        for (int i = 0; i < GraySpread.Length; i++)
        {
            byte g = GraySpread[i];
            // Alpha varies per pixel, so a table that also wrote alpha could not pass.
            img[i % 4, i / 4] = new Rgba32(g, g, g, (byte)(i * 17));
        }
        return img;
    }

    public static TheoryData<double, double, ColorModel> ColorCases()
    {
        var data = new TheoryData<double, double, ColorModel>();
        // Hues on and either side of every 60-degree sector boundary, plus values that must wrap.
        double[] hues =
        {
            -720.5, -0.5, 0, 0.5, 59.999, 60, 60.001, 119.9, 120, 179.5, 180,
            239.99, 240, 299.999, 300, 359.9999, 360, 361.25, 1080.75,
        };
        double[] sats = { 0.0, 0.0001, 0.25, 0.5, 0.83, 0.999, 1.0 };
        foreach (var model in new[] { ColorModel.Hsv, ColorModel.Hsl })
            foreach (double h in hues)
                foreach (double s in sats)
                    data.Add(h, s, model);
        return data;
    }

    [Theory]
    [MemberData(nameof(ColorCases))]
    public void Colorized_pixels_match_a_direct_per_pixel_conversion(double h, double s, ColorModel model)
    {
        using var map = SpreadMap();

        using var outImg = Colorizer.Apply(map, h, s, model);

        for (int i = 0; i < GraySpread.Length; i++)
        {
            int x = i % 4, y = i / 4;
            var src = map[x, y];
            // Exactly what the per-pixel path computed: the conversion of this pixel's own value,
            // carrying this pixel's own alpha through untouched.
            RgbColor expected = model == ColorModel.Hsv
                ? ColorConvert.HsvToRgb(h, s, src.R / 255.0)
                : ColorConvert.HslToRgb(h, s, src.R / 255.0);
            Assert.Equal(new Rgba32(expected.R, expected.G, expected.B, src.A), outImg[x, y]);
        }
    }

    [Fact]
    public void Value_is_read_from_the_red_channel_alone()
    {
        // This is what makes a 256-entry table sufficient: the conversion's only per-pixel input
        // is the source byte R, so two pixels sharing an R colorize identically however their
        // other channels differ. Value-maps are validated grayscale, but the table's key would be
        // wrong if this ever stopped holding.
        using var gray = new Image<Rgba32>(1, 1, new Rgba32(128, 128, 128, 255));
        using var notGray = new Image<Rgba32>(1, 1, new Rgba32(128, 0, 255, 255));

        using var a = Colorizer.Apply(gray, h: 322, s: 0.83, ColorModel.Hsv);
        using var b = Colorizer.Apply(notGray, h: 322, s: 0.83, ColorModel.Hsv);

        Assert.Equal(a[0, 0], b[0, 0]);
    }

    [Fact]
    public void Each_call_resolves_its_own_colors_and_does_not_reuse_the_previous_calls()
    {
        // A table hoisted into a static or cached across calls would make the second Apply repeat
        // the first one's colour. Same source, two hues, one after the other.
        using var map = SpreadMap();

        using var red = Colorizer.Apply(map, h: 0, s: 1.0, ColorModel.Hsv);
        using var green = Colorizer.Apply(map, h: 120, s: 1.0, ColorModel.Hsv);

        Assert.Equal(new Rgba32(255, 0, 0, 255), red[3, 3]);   // gray 255 at the last pixel
        Assert.Equal(new Rgba32(0, 255, 0, 255), green[3, 3]);
    }

    [Fact]
    public void Preserves_alpha_and_does_not_mutate_input()
    {
        using var map = new Image<Rgba32>(1, 1);
        map[0, 0] = new Rgba32(200, 200, 200, 64);

        using var outImg = Colorizer.Apply(map, h: 180, s: 0.5, ColorModel.Hsl);

        Assert.Equal(64, outImg[0, 0].A);
        Assert.Equal(new Rgba32(200, 200, 200, 64), map[0, 0]);
    }
}
