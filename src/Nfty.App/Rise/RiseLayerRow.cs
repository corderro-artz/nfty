using Avalonia.Media.Imaging;
using Nfty.Core.Model;

namespace Nfty.App.Rise;

/// <summary>One layer of a rolled recipe, as the Rise page's layer rail lists it: where it paints,
/// which variant the roll picked, and what depth that variant carries on its own.</summary>
public sealed class RiseLayerRow : IDisposable
{
    /// <summary>Creates a row.</summary>
    /// <param name="depth">1-based position, bottom first: depth 1 paints first, sits furthest back.</param>
    /// <param name="name">The layer's name.</param>
    /// <param name="variantName">The rolled variant's name, or the left-out wording.</param>
    /// <param name="kind">The layer's kind.</param>
    /// <param name="depthText">The variant's own depth, worded like the item chip.</param>
    /// <param name="isAbsent">Whether the roll left this layer out.</param>
    /// <param name="thumbnail">The rolled variant as generation colorizes it; the row owns it.</param>
    public RiseLayerRow(int depth, string name, string variantName, LayerKind kind, string depthText,
        bool isAbsent, Bitmap? thumbnail)
    {
        Depth = depth;
        Name = name;
        VariantName = variantName;
        Kind = kind;
        KindMark = Nfty.App.Models.LayerKindMark.For(kind);
        DepthText = depthText;
        IsAbsent = isAbsent;
        Thumbnail = thumbnail;
    }

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

    /// <summary>Whether the roll left this layer out entirely.</summary>
    public bool IsAbsent { get; }

    /// <summary>The rolled variant, or null for an absent layer.</summary>
    public Bitmap? Thumbnail { get; }

    /// <inheritdoc />
    public void Dispose() => Thumbnail?.Dispose();
}
