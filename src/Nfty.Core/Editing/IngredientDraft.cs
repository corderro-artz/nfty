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

    /// <summary>Appends a copy of an existing variant (same weight, cloned pixels) under a new id/name.</summary>
    public VariantDraft DuplicateVariant(string sourceId, string newId, string newName)
    {
        var src = Variants.FirstOrDefault(v => v.Id == sourceId)
            ?? throw new InvalidOperationException($"No variant '{sourceId}' in ingredient '{Id}'.");
        if (Variants.Any(v => v.Id == newId))
            throw new InvalidOperationException($"Variant id '{newId}' already exists in ingredient '{Id}'.");
        var copy = new VariantDraft(newId, newName, src.Weight, src.Map.Clone());
        Variants.Add(copy);
        return copy;
    }

    /// <summary>Removes a variant by id. Throws if it is absent (the caller enforces any minimum count).</summary>
    public void RemoveVariant(string id)
    {
        var v = Variants.FirstOrDefault(x => x.Id == id)
            ?? throw new InvalidOperationException($"No variant '{id}' in ingredient '{Id}'.");
        Variants.Remove(v);
    }
}
