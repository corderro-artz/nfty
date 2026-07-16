namespace Nfty.Core.Model;

public record IngredientManifest(
    string Id,
    string Name,
    LayerKind Kind,
    Colorization? Colorization,
    IReadOnlyList<Variant> Variants,
    int SchemaVersion = Schema.Current) : ISchemaVersioned;
