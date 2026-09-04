using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Imaging;

/// <summary>
/// One reference layer in a stack preview: which ingredient, which of its variants, and the color to
/// draw it in.
/// </summary>
/// <param name="Ingredient">The layer to draw. Its images stay owned by it —
/// <see cref="StackPreview.Render"/> renders through <see cref="VariantPreview"/>, which never hands
/// out a borrowed image, so nothing here ever disposes what an ingredient owns.</param>
/// <param name="VariantId">Which variant to draw. <see cref="StackPreview.PickVariant"/> is the
/// deterministic default when the caller has no opinion.</param>
/// <param name="ColorSpec">A prefixed color spec (<c>hex:</c>, <c>rgb:</c>, <c>hsl:</c>,
/// <c>hsv:</c>) for a Dynamic or Static layer; ignored by a Custom one, which is composited as-is and
/// never colorized. Which kinds need one is <see cref="VariantPreview.NeedsColor"/>'s answer and the
/// requirement is <c>VariantPreview.Render</c>'s to enforce — restating either here would be a
/// second copy of a rule that has exactly one owner.</param>
/// <param name="Rolled">The rolled hue and saturation, unrounded — set for a layer whose color was
/// <i>rolled</i> rather than authored. <b>When present it is what gets drawn, and
/// <paramref name="ColorSpec"/> becomes display only.</b>
///
/// <para>Both exist because they answer different questions. A spec is what a human reads, pastes and
/// re-runs; a <see cref="RolledColor"/> is what generation actually hands the colorizer. Every spec
/// resolves through 8-bit RGB, so a rolled hue that fell between two representable colors cannot be
/// spelled exactly — carrying only the string put the preview a rounding step off the asset it claims
/// to show. Carrying both keeps the printed spec readable AND the rendered pixel exact.</para>
///
/// <para>Null for Static (an author-written spec, resolved identically by generation, so already
/// exact) and for Custom (no color at all).</para></param>
public readonly record struct PreviewLayer(
    LoadedIngredient Ingredient, string VariantId, string? ColorSpec, RolledColor? Rolled = null);

/// <summary>
/// A recipe's reference layers split around the one being edited: what paints under it, and what
/// paints over it.
///
/// <para>The split is the whole point of the feature — sunglasses sit above a face and below a hat, so
/// a single flat underlay would show neither correctly. It is also the shape the caching wants: the
/// two stacks are composited once each and reused across every brush stroke, invalidated only when the
/// reference <i>selection</i> changes.</para>
/// </summary>
/// <param name="Below">Enabled layers painting beneath the edited one, bottom-to-top — ready to hand
/// straight to <see cref="StackPreview.Render"/>.</param>
/// <param name="Above">Enabled layers painting over the edited one, bottom-to-top, likewise.</param>
public readonly record struct StackSplit(IReadOnlyList<string> Below, IReadOnlyList<string> Above);

/// <summary>
/// Composites several layers at their real depths, so art authored one layer at a time can be lined up
/// against the layers it will actually sit between.
///
/// <para><b>It introduces no third rendering path.</b> Every layer goes through
/// <c>VariantPreview.Render</c> — the one rule for what a Variant looks like — and the stack
/// goes through <see cref="Compositor.Composite"/> — the one rule for how layers combine. That is the
/// property the project protects: the CLI's <c>preview</c> and the GUI's preview must produce the
/// <i>same</i> image, not a similar one, and two copies of a colorizer is precisely how that stops
/// being true.</para>
/// </summary>
public static class StackPreview
{
    /// <summary>
    /// Renders a stack of reference layers onto a transparent canvas.
    /// </summary>
    /// <param name="canvas">The output size. Every layer must match it exactly.</param>
    /// <param name="bottomToTop">The layers in paint order — index 0 paints first and everything after
    /// paints over it. An EMPTY list is legal and yields a fully transparent canvas: "no references on"
    /// is this feature's default state, not an error.</param>
    /// <returns>The composited preview. <b>The caller owns it</b> and must dispose it; every
    /// intermediate this method creates is disposed here, on the success path and on every failure
    /// path alike.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="canvas"/> or
    /// <paramref name="bottomToTop"/> is null.</exception>
    /// <exception cref="ArgumentException">A layer carries no ingredient. Reachable only through
    /// <c>default(PreviewLayer)</c> — a struct cannot enforce its own non-null field — which is why it
    /// is checked rather than assumed.</exception>
    /// <exception cref="InvalidOperationException">A layer's image does not match the canvas, or
    /// <c>VariantPreview.Render</c> refused it (unknown variant, colorized layer with no
    /// color).</exception>
    public static Image<Rgba32> Render(Dimensions canvas, IReadOnlyList<PreviewLayer> bottomToTop)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(bottomToTop);

        var rendered = new List<Image<Rgba32>>(bottomToTop.Count);
        try
        {
            for (int i = 0; i < bottomToTop.Count; i++)
            {
                var layer = bottomToTop[i];
                if (layer.Ingredient is null)
                    throw new ArgumentException(
                        $"Preview layer {i} has no ingredient.", nameof(bottomToTop));

                // Registered in the accumulator the instant it exists, BEFORE anything can throw
                // about it: from here on the only thing that frees it is the finally below.
                //
                // A rolled color wins over the spec beside it. The spec is the readable spelling of
                // that same color, but it can only spell what 8-bit RGB can represent, so rendering
                // through it would quantize a value generation never quantizes — and this whole class
                // exists to show what generation would draw, not something within a step of it.
                var image = layer.Rolled is { } rolled
                    ? VariantPreview.Render(layer.Ingredient, layer.VariantId, rolled)
                    : VariantPreview.Render(layer.Ingredient, layer.VariantId, layer.ColorSpec);
                rendered.Add(image);

                // Rejected, never scaled. A preview that quietly resizes a layer is lying about
                // alignment, which is the single thing this feature exists to show.
                //
                // Nothing downstream would catch it. Compositor.Composite draws every layer at the
                // origin and clips to the canvas, raising nothing for a mismatch: with this check
                // deleted, a 4x4 layer on a 2x2 canvas renders its top-left quarter and reports
                // success, and an undersized one sits silently in that corner. Both look like art
                // that is merely positioned oddly — the worst possible failure mode for the one
                // tool whose job is showing where the art lands.
                if (image.Width != canvas.Width || image.Height != canvas.Height)
                    throw new InvalidOperationException(
                        $"Ingredient '{layer.Ingredient.Manifest.Name}' variant '{layer.VariantId}' is "
                        + $"{image.Width}x{image.Height}, but the canvas is {canvas.Width}x{canvas.Height}. "
                        + "A stack preview never scales a layer: it exists to show how the art lines up, "
                        + "and a resized layer would line up in the preview and nowhere else.");
            }

            return Compositor.Composite(canvas, rendered);
        }
        finally
        {
            // finally rather than catch-and-repeat: once Composite has copied them onto the result
            // these are dead on the success path too, so one line frees them for all outcomes
            // instead of two that would have to be kept in agreement.
            foreach (var image in rendered) image.Dispose();
        }
    }

    /// <summary>
    /// The variant to show when the caller has not named one: <b>highest weight, ties broken by
    /// ordinal-first id</b>.
    ///
    /// <para>Deliberately not a roll. A reference layer that changed appearance between repaints would
    /// make the alignment this feature exists to show unreadable, so there is no RNG here — and the
    /// tie-break is ordinal because a culture-sensitive one would pick differently on another
    /// machine.</para>
    ///
    /// <para>A zero weight shelves a variant from generation but does not hide it here: the most
    /// probable variant is simply the one with the largest weight, and an ingredient whose variants are
    /// all shelved still previews as its ordinal-first one rather than failing.</para>
    /// </summary>
    /// <param name="ingredient">The layer to pick a variant from.</param>
    /// <returns>The chosen variant's id.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ingredient"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The ingredient declares no variants, so there is
    /// nothing to draw.</exception>
    public static string PickVariant(LoadedIngredient ingredient)
    {
        ArgumentNullException.ThrowIfNull(ingredient);

        var variants = ingredient.Manifest.Variants;
        if (variants.Count == 0)
            throw new InvalidOperationException(
                $"Ingredient '{ingredient.Manifest.Name}' has no variants, so there is nothing to preview.");

        return variants
            .OrderByDescending(v => v.Weight)
            .ThenBy(v => v.Id, StringComparer.Ordinal)
            .First().Id;
    }

    /// <summary>
    /// Splits a recipe's enabled reference layers around the layer being edited.
    ///
    /// <para>Ordering comes from <see cref="LayerDepth"/> — the same 1-based, bottom-first projection of
    /// <c>layerOrder</c> that a layer table numbers its rows with — so the preview cannot disagree with
    /// the depths the author is looking at. It lives in Core precisely so the CLI and the GUI cannot
    /// compute the split two different ways.</para>
    /// </summary>
    /// <param name="recipe">The recipe whose stack is being previewed.</param>
    /// <param name="editedIngredientId">The layer being authored. It is <b>pinned at its own depth</b>
    /// and never returned in either list, even if the caller also names it as enabled — it is the
    /// subject of the preview, not a reference in it.</param>
    /// <param name="enabledIngredientIds">Which reference layers the author has switched on. An id the
    /// recipe does not stack is IGNORED rather than rejected — it has no depth here, so there is no
    /// position to put it at — which keeps a stale checkbox left over from a removed layer out of the
    /// panel instead of throwing in the middle of a repaint.
    ///
    /// <para><b>This is not how a Kitchen layer gets placed.</b> Only ids the recipe stacks are ever
    /// returned, so passing a loose <c>.igt</c> id in here yields nothing at all. A Kitchen layer has
    /// no canonical depth by design, and the caller places it <i>in the gaps around</i> what this
    /// returns — entirely outside this method. An earlier version of this comment claimed the
    /// tolerance above WAS the Kitchen case; it is not, and a caller who believed it would silently
    /// drop every loose layer.</para></param>
    /// <returns>The enabled layers below and above, each bottom-to-top.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="recipe"/> or
    /// <paramref name="enabledIngredientIds"/> is null.</exception>
    /// <exception cref="KeyNotFoundException">The recipe does not stack
    /// <paramref name="editedIngredientId"/>. Unlike an unknown reference, this one has no sane
    /// reading: there is no depth to split around.</exception>
    public static StackSplit Split(
        RecipeManifest recipe, string editedIngredientId, IEnumerable<string> enabledIngredientIds)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(enabledIngredientIds);

        // Ordinal, like every id comparison that can reach an output file: under a culture-sensitive
        // set two distinct ids could collide and silently drop a layer from the preview.
        var enabled = new HashSet<string>(enabledIngredientIds, StringComparer.Ordinal);
        int pivot = LayerDepth.DepthOf(recipe, editedIngredientId);

        var below = new List<string>();
        var above = new List<string>();
        foreach (var (depth, id) in LayerDepth.Ordered(recipe))
        {
            if (string.Equals(id, editedIngredientId, StringComparison.Ordinal)) continue;
            if (!enabled.Contains(id)) continue;
            (depth < pivot ? below : above).Add(id);
        }

        return new StackSplit(below, above);
    }
}
