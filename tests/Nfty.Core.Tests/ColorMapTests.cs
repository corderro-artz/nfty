using Nfty.Core.Editing;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>The full-colour editable surface: same shape as ValueMap, but every channel survives.</summary>
public class ColorMapTests
{
    [Fact]
    public void New_map_is_fully_transparent_black()
    {
        var m = ColorMap.ForCanvas(new Dimensions(4, 3));
        Assert.Equal(4, m.Width);
        Assert.Equal(3, m.Height);
        Assert.Equal(new Rgba32(0, 0, 0, 0), m.Get(2, 1));
    }

    [Fact]
    public void Rejects_a_non_positive_dimension()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ColorMap(0, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ColorMap(4, -1));
    }

    [Fact]
    public void Round_trips_through_an_image_with_exact_pixels()
    {
        var m = new ColorMap(2, 1);
        m.Set(0, 0, new Rgba32(214, 36, 159, 255));
        m.Set(1, 0, new Rgba32(1, 2, 3, 128));

        using Image<Rgba32> img = m.ToImage();
        Assert.Equal(new Rgba32(214, 36, 159, 255), img[0, 0]);
        Assert.Equal(new Rgba32(1, 2, 3, 128), img[1, 0]);

        var back = ColorMap.FromImage(img);
        Assert.Equal(new Rgba32(214, 36, 159, 255), back.Get(0, 0));
        Assert.Equal(new Rgba32(1, 2, 3, 128), back.Get(1, 0));
    }

    [Fact]
    public void Clone_is_an_independent_deep_copy()
    {
        var a = new ColorMap(4, 4);
        a.Set(1, 2, new Rgba32(9, 8, 7, 255));
        var b = a.Clone();
        Assert.Equal(new Rgba32(9, 8, 7, 255), b.Get(1, 2));

        b.Set(1, 2, new Rgba32(1, 1, 1, 1));
        Assert.Equal(new Rgba32(9, 8, 7, 255), a.Get(1, 2));   // source untouched
        a.Set(0, 0, new Rgba32(5, 5, 5, 5));
        Assert.Equal(new Rgba32(0, 0, 0, 0), b.Get(0, 0));     // clone untouched
    }

    [Fact]
    public void Set_ignores_an_out_of_bounds_coordinate()
    {
        var m = new ColorMap(2, 2);
        m.Set(-1, 0, new Rgba32(255, 0, 0, 255));   // must not throw
        m.Set(0, 9, new Rgba32(255, 0, 0, 255));
        Assert.Equal(new Rgba32(0, 0, 0, 0), m.Get(0, 0));
    }

    [Fact]
    public void WithAlpha_replaces_only_the_alpha()
    {
        var m = new ColorMap(1, 1);
        Assert.Equal(new Rgba32(4, 5, 6, 7), m.WithAlpha(new Rgba32(4, 5, 6, 200), 7));
        Assert.Equal(200, m.AlphaOf(new Rgba32(4, 5, 6, 200)));
    }
}
