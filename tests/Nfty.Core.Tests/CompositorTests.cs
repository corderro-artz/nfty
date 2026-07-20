using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

public class CompositorTests
{
    [Fact]
    public void Top_opaque_layer_covers_bottom()
    {
        using var bottom = new Image<Rgba32>(2, 2, new Rgba32(255, 0, 0, 255));
        using var top = new Image<Rgba32>(2, 2, new Rgba32(0, 0, 255, 255));

        using var result = Compositor.Composite(new Dimensions(2, 2), new[] { bottom, top });

        Assert.Equal(new Rgba32(0, 0, 255, 255), result[0, 0]);
    }

    [Fact]
    public void Transparent_top_reveals_bottom()
    {
        using var bottom = new Image<Rgba32>(1, 1, new Rgba32(255, 0, 0, 255));
        using var top = new Image<Rgba32>(1, 1, new Rgba32(0, 0, 255, 0));

        using var result = Compositor.Composite(new Dimensions(1, 1), new[] { bottom, top });

        Assert.Equal(new Rgba32(255, 0, 0, 255), result[0, 0]);
    }

    // The whole stack is drawn inside a single Mutate rather than one Mutate per layer. The draws
    // must still land on the same image in the same bottom-to-top order, so these pin the ordering
    // over a stack long enough that a batched or reordered pass would show up.

    [Fact]
    public void A_long_stack_still_resolves_to_its_topmost_opaque_layer()
    {
        var layers = new List<Image<Rgba32>>();
        for (int i = 0; i < 8; i++)
            layers.Add(new Image<Rgba32>(2, 2, new Rgba32((byte)(i * 30), (byte)(255 - i * 30), (byte)i, 255)));

        using var result = Compositor.Composite(new Dimensions(2, 2), layers);

        // Layer 7 is opaque and last, so it wins outright at every pixel.
        Assert.Equal(new Rgba32(210, 45, 7, 255), result[0, 0]);
        Assert.Equal(new Rgba32(210, 45, 7, 255), result[1, 1]);
        foreach (var l in layers) l.Dispose();
    }

    [Fact]
    public void A_transparent_layer_mid_stack_leaves_the_layers_below_intact()
    {
        using var red = new Image<Rgba32>(1, 1, new Rgba32(255, 0, 0, 255));
        using var clear = new Image<Rgba32>(1, 1, new Rgba32(0, 255, 0, 0));
        using var alsoClear = new Image<Rgba32>(1, 1, new Rgba32(0, 0, 255, 0));

        using var result = Compositor.Composite(
            new Dimensions(1, 1), new[] { red, clear, alsoClear, clear });

        Assert.Equal(new Rgba32(255, 0, 0, 255), result[0, 0]);
    }

    [Fact]
    public void An_opaque_layer_above_a_transparent_one_still_wins()
    {
        // Ordering across a gap: the transparent layer must neither erase the red below it nor
        // suppress the green above it.
        using var red = new Image<Rgba32>(1, 1, new Rgba32(255, 0, 0, 255));
        using var clear = new Image<Rgba32>(1, 1, new Rgba32(0, 0, 255, 0));
        using var green = new Image<Rgba32>(1, 1, new Rgba32(0, 255, 0, 255));

        using var result = Compositor.Composite(new Dimensions(1, 1), new[] { red, clear, green });

        Assert.Equal(new Rgba32(0, 255, 0, 255), result[0, 0]);
    }

    [Fact]
    public void An_empty_stack_is_a_fully_transparent_canvas()
    {
        using var result = Compositor.Composite(new Dimensions(2, 2), Array.Empty<Image<Rgba32>>());

        Assert.Equal(new Rgba32(0, 0, 0, 0), result[0, 0]);
        Assert.Equal(new Rgba32(0, 0, 0, 0), result[1, 1]);
    }

    [Fact]
    public void Three_layers_composite_in_order_and_partial_alpha_blends()
    {
        using var red = new Image<Rgba32>(1, 1, new Rgba32(255, 0, 0, 255));
        using var green = new Image<Rgba32>(1, 1, new Rgba32(0, 255, 0, 255));
        using var blue = new Image<Rgba32>(1, 1, new Rgba32(0, 0, 255, 255));

        using var opaqueTop = Compositor.Composite(new Dimensions(1, 1), new[] { red, green, blue });
        Assert.Equal(new Rgba32(0, 0, 255, 255), opaqueTop[0, 0]);

        using var transparentBlue = new Image<Rgba32>(1, 1, new Rgba32(0, 0, 255, 0));
        using var transparentTop = Compositor.Composite(new Dimensions(1, 1), new[] { red, green, transparentBlue });
        Assert.Equal(new Rgba32(0, 255, 0, 255), transparentTop[0, 0]);

        using var halfAlphaBlue = new Image<Rgba32>(1, 1, new Rgba32(0, 0, 255, 128));
        using var blended = Compositor.Composite(new Dimensions(1, 1), new[] { red, halfAlphaBlue });
        var px = blended[0, 0];
        Assert.Equal(0, px.G);
        Assert.Equal(255, px.A);
        Assert.InRange(px.R, (byte)110, (byte)145);
        Assert.InRange(px.B, (byte)110, (byte)145);
    }
}
