namespace Nfty.Core.Output;

/// <summary>One OpenSea trait. The property name is <c>Trait_type</c> because the published JSON
/// key is <c>trait_type</c>, which the shared serializer options map to.</summary>
/// <param name="Trait_type">The trait category.</param>
/// <param name="Value">Its value for this asset.</param>
public record MetadataAttribute(string Trait_type, string Value);

/// <summary>One trait with how common it is across the whole collection.</summary>
/// <param name="Trait_type">The trait category.</param>
/// <param name="Value">Its value for this asset.</param>
/// <param name="RarityPct">Percentage of the collection carrying it.</param>
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
    IReadOnlyList<LayerColor> Layers,
    IReadOnlyList<string>? AbsentLayers = null);

/// <summary>How many assets one recipe produced.</summary>
/// <param name="Recipe">The recipe's display name.</param>
/// <param name="Count">How many assets it produced.</param>
/// <param name="Percent">Its share of the collection.</param>
public record RecipeCount(string Recipe, int Count, double Percent);

/// <param name="Name">The collection's name.</param>
/// <param name="Count">How many assets the Set holds.</param>
/// <param name="Seed">The seed that produced it; the same seed and cookbook reproduce it exactly.</param>
/// <param name="CookbookSha256">SHA-256 of the source <c>.cbk</c> — what ties a Set back to the exact
/// archive that produced it. Null when the cookbook never came from a file (an in-memory book).
///
/// <para>Documented here rather than on the parameter itself: a <c>&lt;summary&gt;</c> inside a
/// positional record's parameter list is not a valid documentation location, so the compiler
/// discarded it with CS1587 and no tooling ever saw it — on the one field CLAUDE.md insists stays
/// threaded through.</para></param>
/// <param name="GeneratorVersion">The engine version stamp.</param>
/// <param name="Distribution">Per-recipe counts and shares across the Set.</param>
/// <param name="Rarity">The collection-wide rarity table.</param>
/// <param name="UniqueDna">Whether every asset was required to have distinct DNA.
///
/// <para>Recorded because the seed does NOT reproduce a run on its own. The same cookbook and the
/// same seed generate a different collection depending on this switch — with uniqueness on, a
/// colliding roll is discarded and re-rolled, which consumes RNG draws that the unlimited run
/// never spends, so the two diverge from the first collision onward. Without this field a Set
/// carried two thirds of its own recipe.</para>
///
/// <para><b>Null means "not recorded"</b>, not "false": Sets written before this field existed
/// cannot say, and claiming they were unique would be inventing a fact. Additive and optional, so
/// <c>Schema.Current</c> is deliberately NOT bumped — older builds ignore unknown properties,
/// whereas a bump would make them reject the file outright.</para></param>
public record SetManifest(
    string Name,
    int Count,
    string Seed,
    string? CookbookSha256,
    string GeneratorVersion,
    IReadOnlyList<RecipeCount> Distribution,
    IReadOnlyList<RarityAttribute> Rarity,
    bool? UniqueDna = null);
