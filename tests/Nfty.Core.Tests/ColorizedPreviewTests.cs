using Nfty.Core.Editing;
using Nfty.Core.Generation;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.Core.Tests;

public class ColorizedPreviewTests
{
    private static VariantDraft Gray(byte value)
    {
        var map = new ValueMap(1, 1);
        map.Set(0, 0, value, 255);
        return new VariantDraft("v", "V", 1, map);
    }

    [Fact]
    public void Static_fixed_colour_matches_the_colorizer_output_exactly()
    {
        var colorization = new Colorization(ColorModel.Hsv, 1, 1,
            new[] { new ColorEntry(1, null, "hsv:200,50,50") });
        var rgb = ColorSpec.Parse("hsv:200,50,50");
        var (h, s, _) = ColorConvert.RgbToHsv(rgb);
        var expected = ColorConvert.HsvToRgb(h, s, 128 / 255.0);

        using Image<Rgba32> preview = ColorizedPreview.Render(Gray(128), LayerKind.Static, colorization,
            new SplitMix64Rng(1));
        Assert.Equal(new Rgba32(expected.R, expected.G, expected.B, 255), preview[0, 0]);
    }

    [Fact]
    public void Dynamic_is_deterministic_for_a_fixed_seed()
    {
        var colorization = new Colorization(ColorModel.Hsv, 12, 4,
            new[] { new ColorEntry(1, new ColorRange(196, 348, 45, 70), null) });
        using var a = ColorizedPreview.Render(Gray(128), LayerKind.Dynamic, colorization, new SplitMix64Rng(42));
        using var b = ColorizedPreview.Render(Gray(128), LayerKind.Dynamic, colorization, new SplitMix64Rng(42));
        Assert.Equal(a[0, 0], b[0, 0]);
    }
}
