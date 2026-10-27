using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Nfty.Core.Imaging;

/// <summary>Stacks finished layer images into one asset.</summary>
public static class Compositor
{
    /// <summary>Composites layers bottom-to-top onto a transparent canvas.</summary>
    /// <param name="canvas">The output size; every layer must match it.</param>
    /// <param name="layersBottomToTop">Layer images in draw order.</param>
    /// <param name="configuration">The ImageSharp configuration to draw under, to cap its own
    /// row-parallelism; null uses <see cref="Configuration.Default"/>. Never changes the pixels.</param>
    /// <returns>The composited asset; the caller owns it.</returns>
    public static Image<Rgba32> Composite(Dimensions canvas, IReadOnlyList<Image<Rgba32>> layersBottomToTop,
        Configuration? configuration = null)
    {
        var result = new Image<Rgba32>(canvas.Width, canvas.Height, new Rgba32(0, 0, 0, 0));
        // One Mutate for the whole stack rather than one per layer: the draws still apply in the
        // same bottom-to-top order onto the same image, but the processing context is built and
        // torn down once instead of once per layer.
        result.Mutate(configuration ?? Configuration.Default, ctx =>
        {
            foreach (var layer in layersBottomToTop)
                ctx.DrawImage(layer, 1f);
        });
        return result;
    }
}
