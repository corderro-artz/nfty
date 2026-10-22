using Nfty.Core.Editing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

public class DepthPictureTests
{
    [Fact]
    public void Red_is_front_and_green_is_back()
    {
        using var pic = new Image<Rgba32>(2, 2);
        pic[1, 0] = new Rgba32(7, 9, 200, 13);

        var map = DepthPicture.ToMap(pic);

        Assert.Equal(2, map.Width);
        Assert.Equal(2, map.Height);
        Assert.Equal(7, map.Front(1, 0));
        Assert.Equal(9, map.Back(1, 0));
        Assert.Equal(0, map.Front(0, 0));
        Assert.Equal(0, map.Back(0, 1));
        Assert.Equal(0, map.Front(1, 1));
    }

    [Fact]
    public void Blue_and_alpha_are_ignored()
    {
        using var a = new Image<Rgba32>(2, 1, new Rgba32(4, 5, 0, 255));
        using var b = new Image<Rgba32>(2, 1, new Rgba32(4, 5, 250, 3));

        Assert.Equal(DepthPicture.ToMap(a).ToBytes(), DepthPicture.ToMap(b).ToBytes());
    }
}
