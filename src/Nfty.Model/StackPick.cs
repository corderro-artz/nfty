using Nfty.Core.Generation;
using Nfty.Core.Output;

namespace Nfty.Model;

/// <summary>One present layer of a selection: its ingredient, the variant chosen, and the color it
/// rolled (null for the stored pixels).</summary>
/// <param name="IngredientId">The layer's ingredient id.</param>
/// <param name="VariantId">The chosen variant id.</param>
/// <param name="Color">The rolled hue and saturation, or null to use the stored pixels.</param>
public readonly record struct StackPick(string IngredientId, string VariantId, RolledColor? Color = null)
{
    /// <summary>A pick with no color from an (ingredient, variant) pair.</summary>
    /// <param name="t">The pair.</param>
    public static implicit operator StackPick((string IngredientId, string VariantId) t) =>
        new(t.IngredientId, t.VariantId);

    /// <summary>Attaches recorded colors to a selection.</summary>
    /// <param name="present">The present layers in paint order.</param>
    /// <param name="colors">Recorded per-layer colors; <see cref="LayerColor.Layer"/> is the ingredient id.</param>
    /// <returns>The picks; a color is set only when both H and S were recorded.</returns>
    public static IReadOnlyList<StackPick> WithColors(
        IReadOnlyList<(string IngredientId, string VariantId)> present, IReadOnlyList<LayerColor> colors)
    {
        ArgumentNullException.ThrowIfNull(present);
        ArgumentNullException.ThrowIfNull(colors);
        var byLayer = new Dictionary<string, LayerColor>(StringComparer.Ordinal);
        foreach (var c in colors) byLayer[c.Layer] = c;
        var picks = new List<StackPick>(present.Count);
        foreach (var (ing, variant) in present)
        {
            RolledColor? color = byLayer.TryGetValue(ing, out var c) && c is { H: { } h, S: { } s }
                ? new RolledColor(h, s)
                : null;
            picks.Add(new StackPick(ing, variant, color));
        }
        return picks;
    }
}
