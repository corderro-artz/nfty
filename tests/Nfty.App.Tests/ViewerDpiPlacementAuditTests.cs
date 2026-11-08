using Avalonia;
using Nfty.App.Services;
using Xunit;

namespace Nfty.App.Tests;

public class ViewerDpiPlacementAuditTests
{
    [Theory]
    [InlineData(1.0, 2880, 1620)]
    [InlineData(2.0, 1920, 1080)]
    public void Placement_converts_DIP_size_to_each_screens_pixel_scale(double scale, int x, int y)
    {
        var entry = new ViewerWindowEntry(960, 540, 3700, 2000, false);
        var actual = ViewerWindowPlacement.Resolve(entry, [(new PixelRect(0, 0, 3840, 2160), scale)]);
        Assert.Equal(new PixelPoint(x, y), actual);
    }

    [Fact]
    public void Mixed_DPI_overlap_uses_each_candidate_size_before_choosing_screen()
    {
        var left = (new PixelRect(0, 0, 1920, 1080), 1.0);
        var right = (new PixelRect(1920, 0, 3840, 2160), 2.0);
        var entry = new ViewerWindowEntry(960, 540, 1400, 100, false);
        // 520*540 on the left; 1400*1080 on the right once its scale is applied.
        Assert.Equal(new PixelPoint(1920, 100), ViewerWindowPlacement.Resolve(entry, [left, right]));
        // Unplugging the right screen clamps the same remembered pixel position on the left.
        Assert.Equal(new PixelPoint(960, 100), ViewerWindowPlacement.Resolve(entry, [left]));
        Assert.Null(ViewerWindowPlacement.Resolve(entry with { X = 2500 }, [left]));
    }

    [Fact]
    public void Scaling_never_rescales_remembered_pixel_position_or_JSON_size()
    {
        var entry = new ViewerWindowEntry(960, 540, 2300, 100, false);
        var state = new ViewerWindowStateService(StateStore.InMemory());
        state.Save(state.Current with { TwoD = entry });
        Assert.Equal(new PixelPoint(2300, 100), ViewerWindowPlacement.Resolve(state.Current.TwoD,
            [(new PixelRect(1920, 0, 3840, 2160), 2.0)]));
        Assert.Equal(entry, state.Current.TwoD);
    }

    [Fact]
    public void A_window_larger_than_scaled_working_area_keeps_screen_corner()
    {
        var entry = new ViewerWindowEntry(960, 540, 2100, 100, false);
        Assert.Equal(new PixelPoint(1920, 0), ViewerWindowPlacement.Resolve(entry,
            [(new PixelRect(1920, 0, 1280, 720), 2.0)]));
    }
}
