using Nfty.Core.Editing;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.Core.Tests;

public class ValueMapTests
{
    [Fact]
    public void New_map_is_fully_transparent_and_zero_value()
    {
        var m = ValueMap.ForCanvas(new Dimensions(4, 3));
        Assert.Equal(4, m.Width);
        Assert.Equal(3, m.Height);
        Assert.Equal(0, m.GetValue(2, 1));
        Assert.Equal(0, m.GetAlpha(2, 1));
    }

    [Fact]
    public void ToImage_writes_grayscale_R_equals_G_equals_B_and_preserves_alpha()
    {
        var m = new ValueMap(2, 1);
        m.Set(0, 0, 200, 255);
        m.Set(1, 0, 40, 128);
        using Image<Rgba32> img = m.ToImage();
        Assert.Equal(new Rgba32(200, 200, 200, 255), img[0, 0]);
        Assert.Equal(new Rgba32(40, 40, 40, 128), img[1, 0]);
    }

    [Fact]
    public void FromImage_reads_R_as_value_and_A_as_alpha()
    {
        using var img = new Image<Rgba32>(1, 1, new Rgba32(150, 10, 10, 90));
        var m = ValueMap.FromImage(img);
        Assert.Equal(150, m.GetValue(0, 0));
        Assert.Equal(90, m.GetAlpha(0, 0));
    }
}
