using Nfty.Core.Model;

namespace Nfty.Core.Editing;

/// <summary>
/// The whole ingredient being edited: identity, layer kind, colorization, the fixed canvas size, and
/// its variants. Every variant's raster is created at <see cref="Canvas"/> — the single source of truth.
/// </summary>
public sealed class IngredientDraft
{
    public string Id { get; }
    public string Name { get; set; }
    public LayerKind Kind { get; set; }
    public Colorization? Colorization { get; set; }
    public Dimensions Canvas { get; }
    public List<VariantDraft> Variants { get; }

    public IngredientDraft(string id, string name, LayerKind kind, Colorization? colorization,
        Dimensions canvas, IEnumerable<VariantDraft> variants)
    {
        Id = id;
        Name = name;
        Kind = kind;
        Colorization = colorization;
        Canvas = canvas;
        Variants = variants.ToList();
    }

    public VariantDraft AddVariant(string id, string name, double weight)
    {
        var v = new VariantDraft(id, name, weight, ValueMap.ForCanvas(Canvas));
        Variants.Add(v);
        return v;
    }
}
