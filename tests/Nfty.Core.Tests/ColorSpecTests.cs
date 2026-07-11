using Nfty.Core.Imaging;

namespace Nfty.Core.Tests;

public class ColorSpecTests
{
    [Fact]
    public void Parses_hex() =>
        Assert.Equal(new RgbColor(214, 36, 159), ColorSpec.Parse("hex:d6249f"));

    [Fact]
    public void Parses_rgb() =>
        Assert.Equal(new RgbColor(214, 36, 159), ColorSpec.Parse("rgb:214,36,159"));

    [Fact]
    public void Parses_hsv_to_expected_rgb() =>
        Assert.Equal(new RgbColor(255, 0, 0), ColorSpec.Parse("hsv:0,100,100"));

    [Fact]
    public void Missing_prefix_throws() =>
        Assert.Throws<FormatException>(() => ColorSpec.Parse("d6249f"));

    [Fact]
    public void Unknown_prefix_throws() =>
        Assert.Throws<FormatException>(() => ColorSpec.Parse("cmyk:1,2,3,4"));
}
