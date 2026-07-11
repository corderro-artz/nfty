using Nfty.Core.Imaging;

namespace Nfty.Core.Tests;

public class ColorConvertTests
{
    [Fact]
    public void HsvToRgb_pure_red() =>
        Assert.Equal(new RgbColor(255, 0, 0), ColorConvert.HsvToRgb(0, 1, 1));

    [Fact]
    public void HsvToRgb_zero_value_is_black_for_any_hue() =>
        Assert.Equal(new RgbColor(0, 0, 0), ColorConvert.HsvToRgb(210, 0.8, 0.0));

    [Fact]
    public void HslToRgb_full_lightness_is_white() =>
        Assert.Equal(new RgbColor(255, 255, 255), ColorConvert.HslToRgb(120, 0.5, 1.0));

    [Fact]
    public void RgbToHsv_roundtrips_hue_saturation()
    {
        var (h, s, v) = ColorConvert.RgbToHsv(new RgbColor(214, 36, 159));
        Assert.InRange(h, 318.0, 320.0);
        Assert.InRange(s, 0.82, 0.84);
        Assert.InRange(v, 0.83, 0.85);
    }
}
