using Nfty.Core.Model;

namespace Nfty.Core.Editing;

/// <summary>
/// The whole ingredient being edited: identity, layer kind, colorization, the fixed canvas size, and
/// its variants. Every variant's raster is created at <see cref="Canvas"/> — the single source of truth.
/// </summary>
public sealed class IngredientDraft
{
    /// <summary>Stable identifier for the ingredient being built.</summary>
    public string Id { get; }
    /// <summary>Display name.</summary>
    public string Name { get; set; }
    /// <summary>Which kind of layer this will be.</summary>
    public LayerKind Kind { get; set; }
    /// <summary>How it is coloured; null for a Custom layer.</summary>
    public Colorization? Colorization { get; set; }
    /// <summary>The canvas every variant must match.</summary>
    public Dimensions Canvas { get; }
    /// <summary>The variants being edited.</summary>
    public List<VariantDraft> Variants { get; }

    /// <summary>Creates a draft ingredient.</summary>
    /// <param name="id">Stable identifier.</param>
    /// <param name="name">Display name.</param>
    /// <param name="kind">Layer kind.</param>
    /// <param name="colorization">Colour configuration, or null for Custom.</param>
    /// <param name="canvas">Canvas size every variant must match.</param>
    /// <param name="variants">Initial variants.</param>
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

    /// <summary>Adds a blank variant sized to the canvas.</summary>
    /// <param name="id">Stable identifier.</param>
    /// <param name="name">Display name.</param>
    /// <param name="weight">Roll weight.</param>
    /// <returns>The new variant, already added.</returns>
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
