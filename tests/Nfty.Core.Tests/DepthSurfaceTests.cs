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
    public void TakeDirty_is_the_bounding_box_of_what_changed_and_then_forgets_it()
    {
        using var art = new Image<Rgba32>(6, 6, new Rgba32(1, 2, 3, 255));
        var s = new DepthSurface(art, null);
        Assert.Null(s.TakeDirty());
        s.Set(1, 4, new DepthPixel(3, 0));
        s.Set(3, 2, new DepthPixel(0, 7));
        Assert.Equal(new PixelRect(1, 2, 3, 3), s.TakeDirty());
        Assert.Null(s.TakeDirty());
    }

    [Fact]
    public void Writing_the_value_a_pixel_already_holds_or_a_clear_pixel_dirties_nothing()
    {
        using var art = TwoByTwoOneClear();
        var s = new DepthSurface(art, null);
        s.Set(0, 0, new DepthPixel(0, 0));      // same value
        s.Set(1, 1, new DepthPixel(9, 9));      // clear pixel, ignored
        s.Set(-1, 0, new DepthPixel(9, 9));     // off the surface
        Assert.Null(s.TakeDirty());
    }

    [Fact]
    public void Overwrite_dirties_the_whole_surface()
    {
        using var art = TwoByTwoOneClear();
        var s = new DepthSurface(art, null);
        s.Overwrite(null);
        Assert.Equal(new PixelRect(0, 0, 2, 2), s.TakeDirty());
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
