using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Tests;

public class StackDepthTests
{
    private static readonly Rgba32 Red = new(200, 0, 0, 255);
    private static readonly Rgba32 Blue = new(0, 0, 200, 255);
    private static readonly Rgba32 Clear = new(0, 0, 0, 0);

    private static Image<Rgba32> Row(params Rgba32[] pixels)
    {
        var img = new Image<Rgba32>(pixels.Length, 1);
        for (int x = 0; x < pixels.Length; x++) img[x, 0] = pixels[x];
        return img;
    }

    private static DepthMap Map(params byte[] frontBackPairs) =>
        DepthMap.FromBytes(frontBackPairs.Length / 2, 1, frontBackPairs);

    [Fact]
    public void The_topmost_layer_solid_at_a_pixel_owns_its_depth()
    {
        using var bottom = Row(Red, Red);
        using var top = Row(Blue, Clear);

        var (art, depth) = StackDepth.Combine([(bottom, Map(1, 0, 1, 0)), (top, Map(3, 2, 0, 0))]);
        using (art)
        {
            Assert.Equal((3, 2), (depth.Front(0, 0), depth.Back(0, 0)));   // top is solid there
            Assert.Equal((1, 0), (depth.Front(1, 0), depth.Back(1, 0)));   // top is clear: defers down
        }
    }

    [Fact]
    public void A_solid_top_pixel_with_no_map_is_paper_even_over_a_raised_layer()
    {
        // "Topmost solid layer wins" includes a layer nobody raised: it covers the pixel, so it owns
        // the pixel's depth, and its depth is paper.
        using var bottom = Row(Red, Red);
        using var top = Row(Blue, Clear);

        var (art, depth) = StackDepth.Combine([(bottom, Map(4, 4, 4, 4)), (top, null)]);
        using (art)
        {
            Assert.Equal((0, 0), (depth.Front(0, 0), depth.Back(0, 0)));
            Assert.Equal((4, 4), (depth.Front(1, 0), depth.Back(1, 0)));
        }
    }

    [Fact]
    public void A_pixel_no_layer_covers_carries_no_depth()
    {
        using var only = Row(Red, Clear);
        var (art, depth) = StackDepth.Combine([(only, Map(0, 0, 4, 4))]);
        using (art)
        {
            Assert.Equal((0, 0), (depth.Front(1, 0), depth.Back(1, 0)));
            Assert.Equal(Clear, art[1, 0]);
        }
    }

    [Fact]
    public void A_partial_pixel_in_any_layer_is_refused_before_compositing()
    {
        // Combine enforces the same one-solid-test rule Riser enforces on a single picture, extended
        // to every layer of the stack: a partial pixel throws, it is never silently skipped.
        using var half = Row(new Rgba32(200, 0, 0, 128));
        Assert.Throws<PartialAlphaException>(() => StackDepth.Combine([(half, Map(2, 2))]));
    }

    [Fact]
    public void A_partial_pixel_over_an_opaque_layer_is_refused_not_silently_composited()
    {
        // Source-over of a partial pixel over an opaque one composites to alpha 255, so the composite
        // alone can't catch this — Combine must scan each layer's own pixels, not just the result.
        using var bottom = Row(Red);
        using var top = Row(new Rgba32(0, 0, 200, 128));

        var ex = Assert.Throws<PartialAlphaException>(
            () => StackDepth.Combine([(bottom, null), (top, null)]));
        Assert.Contains("layer 2", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_partial_pixel_in_a_non_topmost_layer_is_still_caught()
    {
        using var bottom = Row(Red);
        using var middle = Row(new Rgba32(0, 200, 0, 128));
        using var top = Row(Clear);

        var ex = Assert.Throws<PartialAlphaException>(
            () => StackDepth.Combine([(bottom, null), (middle, null), (top, null)]));
        Assert.Contains("layer 2", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_art_is_exactly_what_the_compositor_draws()
    {
        using var bottom = Row(Red, Red, Clear);
        using var top = Row(Blue, Clear, Clear);
        using var expected = Compositor.Composite(new Dimensions(3, 1), [bottom, top]);

        var (art, _) = StackDepth.Combine([(bottom, null), (top, null)]);
        using (art)
            for (int x = 0; x < 3; x++)
                Assert.Equal(expected[x, 0], art[x, 0]);
    }

    [Fact]
    public void An_empty_stack_is_refused()
    {
        Assert.Throws<ArgumentException>(() => StackDepth.Combine([]));
    }

    [Fact]
    public void Layers_of_different_sizes_are_refused()
    {
        using var two = Row(Red, Red);
        using var three = Row(Red, Red, Red);
        Assert.Throws<ArgumentException>(() => StackDepth.Combine([(two, null), (three, null)]));
    }

    [Fact]
    public void A_depth_map_of_another_size_is_refused()
    {
        using var two = Row(Red, Red);
        Assert.Throws<ArgumentException>(() => StackDepth.Combine([(two, Map(1, 1))]));
    }
}
