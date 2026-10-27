using Nfty.Core.Output;

namespace Nfty.Model.Tests;

/// <summary>Recorded colors become picks: exact when the item carries them, rounded for an old Set.</summary>
public class StackPickTests
{
    private static readonly (string, string)[] Present = [("body", "oak"), ("lock", "gold")];

    [Fact]
    public void Unrounded_colors_win_over_the_rounded_ones()
    {
        var picks = StackPick.WithColors(Present,
            [new LayerColor("body", "dynamic", "hsv", 123.4, 0.567, 123.4567891, 0.5671234)]);

        Assert.Equal(123.4567891, picks[0].Color!.Value.H);
        Assert.Equal(0.5671234, picks[0].Color!.Value.S);
        Assert.Null(picks[1].Color);
    }

    [Fact]
    public void A_Set_cooked_before_exact_colors_falls_back_to_the_rounded_ones()
    {
        var picks = StackPick.WithColors(Present, [new LayerColor("body", "dynamic", "hsv", 123.4, 0.567)]);

        Assert.Equal(123.4, picks[0].Color!.Value.H);
        Assert.Equal(0.567, picks[0].Color!.Value.S);
    }

    [Fact]
    public void A_layer_with_no_recorded_color_stays_uncolored()
    {
        var picks = StackPick.WithColors(Present, [new LayerColor("body", "dynamic", "hsv", null, null)]);

        Assert.Null(picks[0].Color);
    }
}
