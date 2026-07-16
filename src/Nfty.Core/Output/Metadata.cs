namespace Nfty.Core.Output;

public record MetadataAttribute(string Trait_type, string Value);

public record RarityAttribute(string Trait_type, string Value, double RarityPct);

/// <summary>
/// Standard ERC-721 / OpenSea per-item metadata. Written to <c>metadata/NNNN.json</c>.
/// Deliberately kept standards-pure — only the fields marketplaces expect, no nfty extras.
/// </summary>
public record OpenSeaMetadata(
    string Name,
    string Description,
    string Image,
    IReadOnlyList<MetadataAttribute> Attributes);

/// <summary>One layer's resolved color for the rich nfty metadata (all kinds represented).</summary>
public record LayerColor(string Layer, string Kind, string? Model, double? H, double? S);

/// <summary>
/// Rich nfty-specific per-item metadata. Written to <c>nfty/NNNN.json</c> alongside the
/// OpenSea file so the OpenSea file stays standards-pure. Carries the generator's extras.
/// </summary>
public record NftyMetadata(
    int SetNumber,
    string Recipe,
    string Dna,
    string Seed,
    IReadOnlyList<RarityAttribute> Rarity,
    IReadOnlyList<LayerColor> Layers);

public record RecipeCount(string Recipe, int Count, double Percent);

public record SetManifest(
    string Name,
    int Count,
    string Seed,
    /// <summary>SHA-256 of the source .cbk; null when the cookbook never came from a file.</summary>
    string? CookbookSha256,
    string GeneratorVersion,
    IReadOnlyList<RecipeCount> Distribution,
    IReadOnlyList<RarityAttribute> Rarity);
