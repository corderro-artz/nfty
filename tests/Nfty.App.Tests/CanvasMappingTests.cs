using Avalonia;
using Avalonia.Media;
using Nfty.App.Services;
using Nfty.App.Views;
using Xunit;

namespace Nfty.App.Tests;

public class CanvasMappingTests
{
    [Fact]
    public void Fitted_square_art_fills_the_tile()
    {
        Assert.True(CanvasMapping.TryFrame(8, 8, 320, 320, 1, 0, 0, out var f));
        Assert.Equal(new CanvasFrame(40, 0, 0), f);
    }

    [Fact]
    public void Wide_art_letterboxes_vertically()
    {
        CanvasMapping.TryFrame(16, 8, 320, 320, 1, 0, 0, out var f);
        Assert.Equal(new CanvasFrame(20, 0, 80), f);
    }

    [Fact]
    public void Zoom_and_pan_compose_into_one_frame()
    {
        CanvasMapping.TryFrame(8, 8, 320, 320, 2, 10, 0, out var f);
        Assert.Equal(80, f.Scale);
        Assert.Equal(-150, f.OffX);
    }

    [Fact]
    public void A_pixel_centre_maps_back_to_its_pixel_and_off_art_is_null()
    {
        CanvasMapping.TryFrame(8, 8, 320, 320, 1, 0, 0, out var f);
        Assert.Equal((3, 5), CanvasMapping.PixelAt(f, 3.5 * 40, 5.5 * 40, 8, 8));
        Assert.Equal((7, 0), CanvasMapping.PixelAt(f, 999, -5, 8, 8));
        Assert.Null(CanvasMapping.PixelUnder(f, 999, 5, 8, 8));
        Assert.Null(CanvasMapping.PixelUnder(f, -5, 5, 8, 8));   // truncation would call this pixel 0
        Assert.Equal((3, 5), CanvasMapping.PixelUnder(f, 3.5 * 40, 5.5 * 40, 8, 8));
    }

    [Fact]
    public void Pan_is_zero_at_fit_and_bounded_by_half_the_overhang_when_zoomed()
    {
        Assert.Equal((0, 0), CanvasMapping.ClampPan(8, 8, 320, 320, 1, 50, -50));
        Assert.Equal((160, -160), CanvasMapping.ClampPan(8, 8, 320, 320, 2, 999, -999));
    }

    [Fact]
    public void An_anchored_zoom_keeps_the_point_under_the_pointer()
    {
        CanvasMapping.TryFrame(8, 8, 320, 320, 1, 0, 0, out var before);
        var (px, py) = CanvasMapping.PanKeeping(8, 8, 320, 320, before, 1, 2, 100, 60);
        CanvasMapping.TryFrame(8, 8, 320, 320, 2, px, py, out var after);
        Assert.Equal((100 - before.OffX) / before.Scale, (100 - after.OffX) / after.Scale, 9);
        Assert.Equal((60 - before.OffY) / before.Scale, (60 - after.OffY) / after.Scale, 9);
    }

    [Fact]
    public void No_frame_without_size() => Assert.False(CanvasMapping.TryFrame(8, 8, 0, 320, 1, 0, 0, out _));

    [Fact]
    public void The_lattice_is_refused_under_two_pixels_a_square()
    {
        Assert.Null(PixelLattice.Build(1.9, 0, 0, Brushes.White, Brushes.Black));
        Assert.NotNull(PixelLattice.Build(2, 0, 0, Brushes.White, Brushes.Black));
    }

    [Fact]
    public void The_lattice_starts_on_the_art_corner_modulo_the_tile()
    {
        // tile = 2 * 10 = 20; corner at -35 sits 5 into a tile, corner at 47 sits 7 into one.
        var b = PixelLattice.Build(10, -35, 47, Brushes.White, Brushes.Black)!;
        Assert.Equal(new RelativeRect(5, 7, 20, 20, RelativeUnit.Absolute), b.DestinationRect);
        Assert.Equal(new RelativeRect(0, 0, 20, 20, RelativeUnit.Absolute), b.SourceRect);
        Assert.Equal(TileMode.Tile, b.TileMode);
    }
}
