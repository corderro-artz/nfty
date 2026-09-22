namespace Nfty.Core.Model;

/// <summary>One layer, or trait-category: a kind, how it is colored, and its weighted variants.</summary>
/// <param name="Id">Stable identifier, unique within its Recipe. Becomes the archive entry name.</param>
/// <param name="Name">Display name. Also the trait type published in the asset metadata, which is
/// why <c>Validator</c> reserves the name "Type" — that is the Recipe's own pseudo-trait.</param>
/// <param name="Kind">Whether the layer rolls a color, applies one fixed color, or is composited
/// as-is; see <see cref="LayerKind"/>.</param>
/// <param name="Colorization">How a Dynamic or Static layer is colored. Must be null for
/// <see cref="LayerKind.Custom"/>, and must be present for the other two.</param>
/// <param name="Variants">The weighted images this layer can roll.</param>
/// <param name="SchemaVersion">The manifest format version; see <see cref="Schema"/>.</param>
public record IngredientManifest(
    string Id,
    string Name,
    LayerKind Kind,
    Colorization? Colorization,
    IReadOnlyList<Variant> Variants,
    int SchemaVersion = Schema.Current) : ISchemaVersioned;
