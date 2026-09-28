using Nfty.Core.Diagnostics;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model;

/// <summary>
/// The stack rule, in one place: a layered picture is risen as ONE solid, never as stacked slabs
/// (slabs z-fight wherever two layers cover a pixel).
/// </summary>
/// <remarks>
/// The art is the compositor's own composite. A pixel of it is solid by <see cref="Opacity.IsSolid"/>,
/// and a solid pixel takes its depth from the TOPMOST layer whose own pixel is solid there, walking
/// the stack top-down; a layer with no map there is paper. Every layer pixel must be fully opaque or
/// fully clear — <see cref="Combine"/> enforces the same one-solid-test rule <see cref="Riser"/>
/// enforces on a single picture, extended to every layer of the stack: a partially transparent pixel
/// in any layer throws <see cref="PartialAlphaException"/> naming that layer, before compositing.
/// </remarks>
public static class StackDepth
{
    /// <summary>Composites a stack and combines its depth.</summary>
    /// <param name="bottomFirst">The layers in paint order (index 0 paints first, sits furthest
    /// back), each with its own depth map or <c>null</c> for paper. All the same size.</param>
    /// <param name="source">How to name the stack in a refusal, e.g. <c>the Chest recipe</c>.</param>
    /// <returns>The composited art — the caller owns and disposes it — and its depth map.</returns>
    /// <exception cref="ArgumentException">The stack is empty, or a layer or map is another size.</exception>
    /// <exception cref="PartialAlphaException">Some layer has a partly transparent pixel.</exception>
    public static (Image<Rgba32> Art, DepthMap Depth) Combine(
        IReadOnlyList<(Image<Rgba32> Layer, DepthMap? Depth)> bottomFirst, string source = "the stack")
    {
        ArgumentNullException.ThrowIfNull(bottomFirst);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (bottomFirst.Count == 0)
            throw new ArgumentException("A stack needs at least one layer.", nameof(bottomFirst));

        int w = bottomFirst[0].Layer.Width, h = bottomFirst[0].Layer.Height;
        for (int i = 0; i < bottomFirst.Count; i++)
        {
            var (layer, map) = bottomFirst[i];
            ArgumentNullException.ThrowIfNull(layer);
            if (layer.Width != w || layer.Height != h)
                throw new ArgumentException(
                    $"Layer {i + 1} is {layer.Width}x{layer.Height}; the stack is {w}x{h}.", nameof(bottomFirst));
            if (map is not null && (map.Width != w || map.Height != h))
                throw new ArgumentException(
                    $"Layer {i + 1}'s depth map is {map.Width}x{map.Height}; the stack is {w}x{h}.", nameof(bottomFirst));
        }

        using var _ = Perf.Measure("StackDepth.Combine");

        for (int i = 0; i < bottomFirst.Count; i++)
            ScanForPartialAlpha(bottomFirst[i].Layer, i, source);

        var art = Compositor.Composite(new Dimensions(w, h), bottomFirst.Select(l => l.Layer).ToArray());
        try
        {
            var bytes = new byte[checked(w * h * 2)];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (!Opacity.IsSolid(art[x, y])) continue;
                    for (int i = bottomFirst.Count - 1; i >= 0; i--)
                    {
                        var (layer, map) = bottomFirst[i];
                        if (!Opacity.IsSolid(layer[x, y])) continue;
                        if (map is not null)
                        {
                            int k = ((y * w) + x) * 2;
                            bytes[k] = map.Front(x, y);
                            bytes[k + 1] = map.Back(x, y);
                        }
                        break;
                    }
                }
            }
            return (art, DepthMap.FromBytes(w, h, bytes));
        }
        catch
        {
            art.Dispose();
            throw;
        }
    }

    /// <summary>Scans one layer for a partly transparent pixel and throws naming it, matching
    /// <see cref="Riser"/>'s own scan-and-count shape.</summary>
    private static void ScanForPartialAlpha(Image<Rgba32> layer, int index, string source)
    {
        int partial = 0, firstX = -1, firstY = -1;
        byte firstAlpha = 0;
        layer.ProcessPixelRows(rows =>
        {
            for (int y = 0; y < rows.Height; y++)
            {
                Span<Rgba32> row = rows.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    Rgba32 p = row[x];
                    if (Opacity.IsPartial(p) && partial++ == 0)
                    {
                        firstX = x;
                        firstY = y;
                        firstAlpha = p.A;
                    }
                }
            }
        });
        if (partial > 0)
            throw new PartialAlphaException($"layer {index + 1} of {source}", firstX, firstY, firstAlpha, partial);
    }
}
