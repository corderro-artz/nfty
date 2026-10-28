using Nfty.Core.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.Core.Tests;

public class UpscaleTests
{
    [Fact]
    public void Upscale_by_four_repeats_every_pixel_in_a_four_by_four_block()
    {
        using var src = new Image<Rgba32>(2, 2);
        src[0, 0] = new Rgba32(1, 0, 0, 255);
        src[1, 0] = new Rgba32(0, 2, 0, 255);
        src[0, 1] = new Rgba32(0, 0, 3, 255);
        src[1, 1] = new Rgba32(4, 4, 4, 128);

        using var big = Upscale.By(src, 4);

        Assert.Equal(8, big.Width);
        Assert.Equal(8, big.Height);
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                Assert.Equal(src[x / 4, y / 4], big[x, y]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(16)]
    [InlineData(-2)]
    public void Upscale_refuses_a_factor_that_is_not_1_2_4_or_8(int factor)
    {
        using var src = new Image<Rgba32>(1, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => Upscale.By(src, factor));
    }
}
