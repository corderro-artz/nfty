namespace Nfty.Core.Output;

public record MetadataAttribute(string Trait_type, string Value);

public record RarityAttribute(string Trait_type, string Value, double RarityPct);

public record ColorRollDto(string Layer, string Model, double H, double S);

public record ItemMetadata(
    string Name,
    string Description,
    string Image,
    IReadOnlyList<MetadataAttribute> Attributes,
    int SetNumber,
    string Recipe,
    string Dna,
    string Seed,
    IReadOnlyList<RarityAttribute> Rarity,
    IReadOnlyList<ColorRollDto> ColorRolls);

public record RecipeCount(string Recipe, int Count, double Percent);

public record SetManifest(
    string Name,
    int Count,
    string Seed,
    string GeneratorVersion,
    IReadOnlyList<RecipeCount> Distribution,
    IReadOnlyList<RarityAttribute> Rarity);
