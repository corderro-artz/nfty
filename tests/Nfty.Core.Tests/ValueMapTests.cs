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

    [Fact]
    public void Set_ignores_an_out_of_bounds_coordinate()
    {
        var m = new ValueMap(2, 2);
        m.Set(-1, 0, 200, 255);   // must not throw
        m.Set(0, 9, 200, 255);
        // The sharp one: on a 2-wide map, unguarded index arithmetic puts x=2,y=0 onto (0,1) —
        // a brush running off the right edge would silently paint the row below.
        m.Set(2, 0, 200, 255);
        Assert.Equal(0, m.GetValue(0, 1));
        Assert.Equal(0, m.GetAlpha(0, 1));
        Assert.Equal(0, m.GetValue(0, 0));
    }

    [Fact]
    public void Clone_is_an_independent_deep_copy()
    {
        var a = new ValueMap(4, 4);
        a.Set(1, 2, 200, 255);
        var b = a.Clone();
        Assert.Equal(200, b.GetValue(1, 2));
        Assert.Equal(255, b.GetAlpha(1, 2));
        b.Set(1, 2, 10, 10);                  // mutate the clone
        Assert.Equal(200, a.GetValue(1, 2));  // source untouched
        a.Set(0, 0, 50, 50);                  // mutate the source
        Assert.Equal(0, b.GetValue(0, 0));    // clone untouched
    }
}
