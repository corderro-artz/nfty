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
