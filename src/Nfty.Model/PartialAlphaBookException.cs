using Nfty.Core.Stats;

namespace Nfty.Model;

/// <summary>
/// A CookBook with a partly transparent variant cannot be cooked into models: every asset's picture
/// would be refused one by one. Refused up front, once, naming the variants.
/// </summary>
public sealed class PartialAlphaBookException : InvalidOperationException
{
    private const int Listed = 5;

    /// <summary>Creates the exception.</summary>
    /// <param name="bookName">The CookBook's name.</param>
    /// <param name="variants">The variants with partly transparent pixels.</param>
    public PartialAlphaBookException(string bookName, IReadOnlyList<VoxelVariant> variants)
        : base(Describe(bookName, variants))
    {
        BookName = bookName;
        Variants = variants;
    }

    /// <summary>The CookBook's name.</summary>
    public string BookName { get; }

    /// <summary>The offending variants.</summary>
    public IReadOnlyList<VoxelVariant> Variants { get; }

    private static string Describe(string bookName, IReadOnlyList<VoxelVariant> variants)
    {
        var names = string.Join(", ", variants.Take(Listed).Select(v => $"{v.IngredientName} / {v.VariantName}"));
        if (variants.Count > Listed) names += $", and {variants.Count - Listed} more";
        string noun = variants.Count == 1 ? "variant has" : "variants have";
        return $"CookBook '{bookName}' is 2D only: {variants.Count} {noun} partly transparent pixels ({names}). "
            + "A model needs every pixel fully opaque or fully clear — run nfty inspect <book> --voxel for the list, "
            + "or use Make voxel-ready on the Rise page.";
    }
}
