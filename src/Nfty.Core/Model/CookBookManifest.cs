namespace Nfty.Core.Model;

public record CookBookManifest(
    string Id,
    string Name,
    Dimensions Canvas,
    Collection Collection,
    IReadOnlyDictionary<string, double> RecipeWeights,
    int SchemaVersion = 1);
