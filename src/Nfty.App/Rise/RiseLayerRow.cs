using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Nfty.Core.Model;

namespace Nfty.App.Rise;

/// <summary>One layer of a rolled recipe, as the Rise page's layer rail lists it: where it paints,
/// which variant the roll picked, and what depth that variant carries on its own.</summary>
public sealed partial class RiseLayerRow : ObservableObject, IDisposable
{
    /// <summary>Creates a row.</summary>
    /// <param name="depth">1-based position, bottom first: depth 1 paints first, sits furthest back.</param>
    /// <param name="name">The layer's name.</param>
    /// <param name="variantName">The rolled variant's name, or the left-out wording.</param>
    /// <param name="kind">The layer's kind.</param>
    /// <param name="depthText">The variant's own depth, worded like the item chip.</param>
    /// <param name="isAbsent">Whether the roll left this layer out.</param>
    /// <param name="thumbnail">The rolled variant as generation colorizes it; the row owns it.</param>
    /// <param name="ingredientId">The layer's id; empty when the row does not name one.</param>
    /// <param name="variantId">The rolled variant's id, or null for an absent layer.</param>
    /// <param name="isPickable">Whether clicking the row may make this layer the one being painted.</param>
    public RiseLayerRow(int depth, string name, string variantName, LayerKind kind, string depthText,
        bool isAbsent, Bitmap? thumbnail, string ingredientId = "", string? variantId = null, bool isPickable = false)
    {
        IngredientId = ingredientId;
        VariantId = variantId;
        IsPickable = isPickable && !isAbsent;
        Depth = depth;
        Name = name;
        VariantName = variantName;
        Kind = kind;
        KindMark = Nfty.App.Models.LayerKindMark.For(kind);
        DepthText = depthText;
        IsAbsent = isAbsent;
        Thumbnail = thumbnail;
    }

    /// <summary>The layer's id.</summary>
    public string IngredientId { get; }

    /// <summary>The rolled variant's id, or null when the roll left the layer out.</summary>
    public string? VariantId { get; }

    /// <summary>Whether the row can become the painted layer: present in the roll, and the source is editable.</summary>
    public bool IsPickable { get; }

    /// <summary>Whether this is the layer the page paints on.</summary>
    [ObservableProperty] private bool _isTarget;

    /// <summary>1-based position, bottom first.</summary>
    public int Depth { get; }

    /// <summary>The layer's name.</summary>
    public string Name { get; }

    /// <summary>The rolled variant's name.</summary>
    public string VariantName { get; }

    /// <summary>One-letter kind mark.</summary>
    public string KindMark { get; }

    /// <summary>The variant's own depth, worded like the item chip ("paper", "1 · 0", "varied").</summary>
    public string DepthText { get; }

    /// <summary>The layer's kind.</summary>
    public LayerKind Kind { get; }

    /// <summary>The layer is Dynamic: the kind mark's color class.</summary>
    public bool IsDynamic => Kind == LayerKind.Dynamic;

    /// <summary>The layer is Static: the kind mark's color class.</summary>
    public bool IsStatic => Kind == LayerKind.Static;

    /// <summary>The layer is Custom: the kind mark's color class.</summary>
    public bool IsCustom => Kind == LayerKind.Custom;

    /// <summary>The second line of the row: the rolled variant and its own depth, or the left-out
    /// wording on its own.</summary>
    public string Caption => IsAbsent || DepthText.Length == 0 ? VariantName : $"{VariantName} · {DepthText}";

    /// <summary>Whether the roll left this layer out entirely.</summary>
    public bool IsAbsent { get; }

    /// <summary>The rolled variant, or null for an absent layer.</summary>
    public Bitmap? Thumbnail { get; }

    /// <inheritdoc />
    public void Dispose() => Thumbnail?.Dispose();
}
