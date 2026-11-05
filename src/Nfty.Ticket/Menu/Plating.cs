using Nfty.Core.Model;

namespace Nfty.Ticket;

/// <summary>One layer to draw.</summary>
/// <param name="ImageKey">What to ask <see cref="MenuArchive.ArtBytes"/> for.</param>
/// <param name="Source">Where this layer's art sits inside those bytes.</param>
/// <param name="Kind">Custom draws as-is; Dynamic and Static are tinted.</param>
/// <param name="Hue">Hue in degrees. Meaningless for Custom.</param>
/// <param name="Sat">Saturation, 0..1. Meaningless for Custom.</param>
/// <param name="Model">The color space. Meaningless for Custom.</param>
public readonly record struct Draw(
    string ImageKey, MenuCell Source, LayerKind Kind, double Hue, double Sat, ColorModel Model);

/// <summary>
/// Turns a ticket into the ordered list of draws that reproduces it.
/// </summary>
/// <remarks>
/// <para><b>Bottom-first, in <c>layerOrder</c></b> â€” generation's own paint order, which the ticket
/// normally carries because <see cref="Ticketer"/> walks the layers in it. The Menu remains
/// authoritative when wire fields arrive in another order.</para>
///
/// <para>Nothing here touches a pixel. The colorize rule a renderer must implement is one line:
/// <c>rgb = HsvToRgb(hue, sat, source.R / 255)</c> for HSV, or
/// <c>rgb = HslToRgb(hue, sat, source.R / 255)</c> for HSL (source red supplies lightness).
/// In both cases the source's alpha is carried through
/// untouched â€” which is exactly what <c>Colorizer.Apply</c> computes, via a 256-entry table it
/// builds because the red channel is the only per-pixel input.</para>
/// </remarks>
public static class Plating
{
    /// <summary>The draws that reproduce a ticket.</summary>
    /// <param name="menu">The Menu the ticket was rolled against.</param>
    /// <param name="ticket">The roll.</param>
    /// <returns>One draw per present layer, bottom-first.</returns>
    /// <exception cref="KeyNotFoundException">The Menu has no recipe named by
    /// <see cref="AssetTicket.RecipeId"/>; that recipe has no layer for one of the ticket's
    /// ingredient ids; a layer has no variant named by the ticket; or the Menu has no art cell for
    /// one of the ticket's rolled variants. The message names the specific id that was not found.</exception>
    /// <exception cref="FormatException">A colorized layer's hue or saturation quantize differs
    /// from the Menu's colorization for that layer, or a ticket repeats an ingredient id.
    /// The message names the layer.</exception>
    public static IReadOnlyList<Draw> Plate(MenuArchive menu, AssetTicket ticket)
    {
        Guard.NotNull(menu, nameof(menu));
        Guard.NotNull(ticket, nameof(ticket));
        var recipe = menu.Recipe(ticket.RecipeId);
        var present = new Dictionary<string, (TicketLayer Roll, MenuLayer Definition)>(StringComparer.Ordinal);
        foreach (var layer in ticket.Layers)
        {
            var definition = menu.Layer(recipe.Id, layer.IngredientId);
            if (!present.TryAdd(layer.IngredientId, (layer, definition)))
                throw new FormatException($"Recipe '{recipe.Id}' ticket repeats layer '{layer.IngredientId}'.");
            if (!definition.Variants.Any(v => string.Equals(v.Id, layer.VariantId, StringComparison.Ordinal)))
                throw new KeyNotFoundException(
                    $"Layer '{layer.IngredientId}' of recipe '{recipe.Id}' has no variant '{layer.VariantId}'.");
        }

        // KIND AND COLOR MODEL COME FROM THE MENU, NEVER FROM THE TICKET, and that is a
        // correctness rule rather than a tidiness one. Dna.Compute hashes LayerSelection, which
        // carries the variant id and the quantized (H,S) and NOT the kind or the model - so a wire
        // altered in only those two fields recomputes to the same DNA and Parse accepts it. Reading
        // them from the ticket would then let a tampered ticket keep its provable identity while
        // rendering different pixels: flip Custom to Dynamic and the client colorizes art that
        // should composite as-is.
        //
        // The Menu is authoritative and the redundancy is not real: for a given
        // (recipeId, ingredientId) the kind and the color model are fixed BY THE BOOK and cannot
        // legitimately vary per asset. The ticket says what was ROLLED; the Menu says what the layer
        // IS.
        //
        // QUANTIZE IS THE SAME KIND OF FIELD, AND IS CHECKED RATHER THAN MERELY IGNORED. Dna.Compute
        // hashes the BUCKET a color lands in, not the color, so the bucket width decides how far a
        // hue may move without changing the DNA: hue 100 at a 10-degree step is bucket 10, and so is
        // hue 205 at a 20-degree step. A wire that re-quantizes carries a DNA its own contents really
        // do produce, so Parse cannot see it - and the hue is the one field this method must take
        // from the ticket, because it is what was rolled. So the step it was bucketed at must be the
        // Menu's own, or a forged ticket keeps its identity while drawing a different color.
        var draws = new List<Draw>(ticket.Layers.Count);
        foreach (string id in recipe.LayerOrder)
        {
            if (!present.TryGetValue(id, out var selection)) continue;
            var (layer, definition) = selection;
            string key = MenuBook.CellKey(ticket.RecipeId, layer.IngredientId, layer.VariantId);
            if (definition.Colorization is { } c
                && (layer.HueQuantize != c.HueQuantize || layer.SatQuantize != c.SatQuantize))
                throw new FormatException(
                    $"Layer '{layer.IngredientId}' of recipe '{ticket.RecipeId}' carries quantize " +
                    $"{layer.HueQuantize}/{layer.SatQuantize}, but the Menu colorizes it at " +
                    $"{c.HueQuantize}/{c.SatQuantize}; the ticket was not rolled against this Menu.");
            draws.Add(new Draw(key, menu.Cell(key), definition.Kind,
                layer.Hue ?? 0, layer.Sat ?? 0, definition.Colorization?.Model ?? ColorModel.Hsv));
        }
        return draws;
    }
}
