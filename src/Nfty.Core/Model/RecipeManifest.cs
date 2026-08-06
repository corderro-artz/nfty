namespace Nfty.Core.Model;

/// <summary>A complete template for one character type: which layers, in what order, and which
/// combinations are illegal.</summary>
/// <param name="Id">Stable identifier, unique within its CookBook.</param>
/// <param name="Name">Display name.</param>
/// <param name="LayerOrder">Ingredient ids bottom-to-top. This — not the ingredient collection's own
/// order — is what generation rolls and composites, so an ingredient missing from it is never drawn.</param>
/// <param name="Rules">Incompatibility rules applied to a rolled selection; a violation costs a
/// re-roll.</param>
/// <param name="SchemaVersion">The manifest format version; see <see cref="Schema"/>.</param>
public record RecipeManifest(
    string Id,
    string Name,
    IReadOnlyList<string> LayerOrder,
    IReadOnlyList<IncompatibilityRule> Rules,
    int SchemaVersion = Schema.Current) : ISchemaVersioned;
