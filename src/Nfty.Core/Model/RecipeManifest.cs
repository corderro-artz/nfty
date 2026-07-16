namespace Nfty.Core.Model;

public record RecipeManifest(
    string Id,
    string Name,
    IReadOnlyList<string> LayerOrder,
    IReadOnlyList<IncompatibilityRule> Rules,
    int SchemaVersion = Schema.Current) : ISchemaVersioned;
