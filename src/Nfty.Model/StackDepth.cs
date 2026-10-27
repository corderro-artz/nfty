using Nfty.Core.Diagnostics;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model;

/// <summary>
/// The stack rule, in one place: a layered picture is risen as ONE solid, never as stacked slabs
/// (slabs z-fight wherever two layers cover a pixel). The geometry is the UNION of every solid layer's
/// column; each of the two faces takes its color from the layer that owns it.
/// </summary>
/// <remarks>
/// Per pixel, over the layers solid there: front = the max of their fronts, back = the max of their
/// backs (a layer with no map is paper, 0/0). The front face belongs to the topmost layer reaching
/// the max front, the back face to the topmost layer reaching the max back; ties go to the upper
/// layer. Where an owner is the topmost solid layer its color is the composite's own pixel. Every
/// layer pixel must be fully opaque or fully clear — <see cref="Combine"/> enforces the same
/// one-solid-test rule <see cref="Riser"/> enforces on a single picture, across the stack: a
/// partially transparent pixel in any layer throws <see cref="PartialAlphaException"/> naming that
/// layer, before compositing.
/// </remarks>
public static class StackDepth
{
    /// <summary>Composites a stack and combines its depth and face owners by the union rule.</summary>
    /// <param name="bottomFirst">The layers in paint order (index 0 paints first, sits furthest
    /// back), each with its own depth map or <c>null</c> for paper. All the same size. Not taken.</param>
    /// <param name="source">How to name the stack in a refusal, e.g. <c>the Chest recipe</c>.</param>
    /// <param name="finished">The finished picture, when there is one (a cooked asset); its pixels
    /// become <see cref="StackRise.Art"/> and the topmost owners' colors. Not taken.</param>
    /// <returns>The rise; the caller owns and disposes it.</returns>
    /// <exception cref="ArgumentException">The stack is empty, a layer, map or the finished picture is
    /// another size, or the finished picture's solid pixels disagree with the layers'.</exception>
    /// <exception cref="PartialAlphaException">Some layer has a partly transparent pixel.</exception>
    public static StackRise Combine(
        IReadOnlyList<(Image<Rgba32> Layer, DepthMap? Depth)> bottomFirst, string source = "the stack",
        Image<Rgba32>? finished = null)
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
        if (finished is not null && (finished.Width != w || finished.Height != h))
            throw new ArgumentException(
                $"The finished picture is {finished.Width}x{finished.Height}; the stack is {w}x{h}.", nameof(finished));

        using var _ = Perf.Measure("StackDepth.Combine");

        for (int i = 0; i < bottomFirst.Count; i++)
            ScanForPartialAlpha(bottomFirst[i].Layer, i, source);

        Image<Rgba32>? art = null, front = null, back = null;
        try
        {
            using var composite = Compositor.Composite(new Dimensions(w, h), bottomFirst.Select(l => l.Layer).ToArray());
            if (finished is not null)
            {
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        if (Opacity.IsSolid(finished[x, y]) != Opacity.IsSolid(composite[x, y]))
                            throw new ArgumentException(
                                $"The finished picture disagrees with the layers about whether pixel ({x},{y}) is solid.",
                                nameof(finished));
            }
            art = (finished ?? composite).Clone();
            front = new Image<Rgba32>(w, h);
            back = new Image<Rgba32>(w, h);

            var bytes = new byte[checked(w * h * 2)];
            var seams = new int[w * h];
            var frontOwners = new int[w * h];
            var backOwners = new int[w * h];
            Array.Fill(frontOwners, -1);
            Array.Fill(backOwners, -1);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (!Opacity.IsSolid(art[x, y])) continue;

                    int top = -1, maxF = -1, maxB = -1, ownF = -1, ownB = -1;
                    for (int i = bottomFirst.Count - 1; i >= 0; i--)
                    {
                        var (layer, map) = bottomFirst[i];
                        if (!Opacity.IsSolid(layer[x, y])) continue;
                        if (top < 0) top = i;
                        int f = map?.Front(x, y) ?? 0, b = map?.Back(x, y) ?? 0;
                        if (f > maxF) { maxF = f; ownF = i; }   // strict: the upper layer keeps a tie
                        if (b > maxB) { maxB = b; ownB = i; }
                    }

                    frontOwners[(y * w) + x] = ownF;
                    backOwners[(y * w) + x] = ownB;
                    int k = ((y * w) + x) * 2;
                    bytes[k] = (byte)maxF;
                    bytes[k + 1] = (byte)maxB;

                    front[x, y] = ownF == top ? art[x, y] : bottomFirst[ownF].Layer[x, y];
                    back[x, y] = ownB == top ? art[x, y] : bottomFirst[ownB].Layer[x, y];

                    // The back owner owns what the upper-owns rule leaves it: behind the front owner's
                    // own back when the front owner is above; else its whole interval.
                    seams[(y * w) + x] = ownF >= ownB
                        ? maxB - (bottomFirst[ownF].Depth?.Back(x, y) ?? 0)
                        : maxB + 1 + (bottomFirst[ownB].Depth?.Front(x, y) ?? 0);
                }
            }
            var faces = new StackFaces(front, back, seams);
            var rise = new StackRise(art, DepthMap.FromBytes(w, h, bytes), faces, frontOwners, backOwners);
            art = front = back = null;   // owned by the rise now
            return rise;
        }
        finally
        {
            art?.Dispose();
            front?.Dispose();
            back?.Dispose();
        }
    }

    /// <summary>The union rise of one rolled selection of a recipe.</summary>
    /// <param name="recipe">The recipe the selection was rolled from.</param>
    /// <param name="presentBottomFirst">The present layers in paint order, each with the color it
    /// rolled or null. An absent layer is simply not listed.</param>
    /// <param name="source">How to name the selection in a refusal.</param>
    /// <param name="finished">The finished picture, when there is one.</param>
    /// <returns>The rise; the caller disposes it.</returns>
    /// <exception cref="KeyNotFoundException">A named layer or variant is not in the recipe.</exception>
    /// <exception cref="PartialAlphaException">Some layer has a partly transparent pixel.</exception>
    /// <remarks>
    /// A pick with a color, on a colorized layer, is recolorized exactly as generation did
    /// (<see cref="VariantPreview"/>); a pick with none, or a Custom layer, uses the stored pixels. An
    /// all-absent roll has no layers — callers treat it as paper and do not call this.
    /// </remarks>
    public static StackRise ForSelection(LoadedRecipe recipe, IReadOnlyList<StackPick> presentBottomFirst,
        string source = "the selection", Image<Rgba32>? finished = null)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(presentBottomFirst);

        var byId = new Dictionary<string, LoadedIngredient>(StringComparer.Ordinal);
        foreach (var ing in recipe.Ingredients) byId[ing.Manifest.Id] = ing;   // last wins, as StackRoll

        var owned = new List<Image<Rgba32>>(presentBottomFirst.Count);
        try
        {
            var pairs = new List<(Image<Rgba32>, DepthMap?)>(presentBottomFirst.Count);
            using (Perf.Measure("StackDepth.Colorize"))
            {
                foreach (var pick in presentBottomFirst)
                {
                    if (!byId.TryGetValue(pick.IngredientId, out var ing))
                        throw new KeyNotFoundException($"{source}: recipe '{recipe.Manifest.Id}' has no layer '{pick.IngredientId}'.");
                    if (!ing.VariantImages.TryGetValue(pick.VariantId, out var image))
                        throw new KeyNotFoundException($"{source}: layer '{pick.IngredientId}' has no variant '{pick.VariantId}'.");
                    // Combine takes nothing, so an uncolored layer is borrowed; only a recolored one is ours.
                    var layer = image;
                    if (pick.Color is { } color && VariantPreview.NeedsColor(ing))
                    {
                        layer = VariantPreview.Render(ing, pick.VariantId, color);
                        owned.Add(layer);
                    }
                    pairs.Add((layer, ing.Depths.GetValueOrDefault(pick.VariantId)));
                }
            }
            return Combine(pairs, source, finished);
        }
        finally
        {
            foreach (var image in owned) image.Dispose();
        }
    }

    /// <summary>The union rise of one generated asset.</summary>
    /// <param name="recipe">The recipe the asset was rolled from.</param>
    /// <param name="asset">The asset; its traits are its present layers, in paint order, and its
    /// color rolls recolorize them.</param>
    /// <param name="source">How to name the asset in a refusal.</param>
    /// <returns>The rise, or <c>null</c> for an all-absent roll (nothing painted: paper).</returns>
    /// <exception cref="KeyNotFoundException">A trait names a layer or variant the recipe lacks.</exception>
    /// <exception cref="PartialAlphaException">Some layer has a partly transparent pixel.</exception>
    public static StackRise? ForAsset(LoadedRecipe recipe, GeneratedAsset asset, string source = "the asset")
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(asset);
        if (asset.Traits.Count == 0) return null;
        var picks = asset.Traits.Select(t =>
        {
            var roll = asset.ColorRolls.FirstOrDefault(r => r.LayerId == t.IngredientId);
            RolledColor? color = roll is { H: { } h, S: { } s } ? new RolledColor(h, s) : null;
            return new StackPick(t.IngredientId, t.VariantId, color);
        }).ToList();
        return ForSelection(recipe, picks, source, asset.Image);
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
