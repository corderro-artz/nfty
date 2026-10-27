using Nfty.Core.Editing;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.Core.Tests;

public class DepthSurfaceTests
{
    private static Image<Rgba32> TwoByTwoOneClear()
    {
        var img = new Image<Rgba32>(2, 2);
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 2; x++)
                img[x, y] = new Rgba32(200, 100, 50, 255);
        img[1, 1] = new Rgba32(0, 0, 0, 0);
        return img;
    }

    [Fact]
    public void Overwrite_sets_solid_pixels_and_keeps_the_instance()
    {
        using var art = TwoByTwoOneClear();
        var s = new DepthSurface(art, null);
        var same = s;
        s.Overwrite(DepthMap.FromBytes(2, 2, new byte[] { 5, 6, 5, 6, 5, 6, 5, 6 }));
        Assert.Same(same, s);
        Assert.Equal(new DepthPixel(5, 6), s.Get(0, 0));
        Assert.Equal(new DepthPixel(5, 6), s.Get(1, 0));
        Assert.Equal(new DepthPixel(0, 0), s.Get(1, 1));
        Assert.Equal(new byte[] { 5, 6, 5, 6, 5, 6, 0, 0 }, s.ToDepthMap()!.ToBytes());
        s.Overwrite(null);
        Assert.Null(s.ToDepthMap());
    }

    [Fact]
    public void Overwrite_refuses_a_map_of_another_size()
    {
        using var art = TwoByTwoOneClear();
        var s = new DepthSurface(art, null);
        Assert.Throws<ArgumentException>(() => s.Overwrite(DepthMap.FromBytes(1, 1, new byte[] { 1, 1 })));
    }
}
