namespace Nfty.Core.Model;

/// <param name="TargetSupply">How many assets this collection intends to mint, or null when the
/// author has not committed to a number. Purely declarative: nothing in the generation pipeline
/// reads it — <c>generate</c> takes its count from the caller — so setting it can never change what
/// a cook produces. It exists so the book can state its intent, and so a UI can hold that intent up
/// against <see cref="Generation.UniqueSpace"/>'s count and say whether it is actually achievable.
///
/// Added after schemaVersion 1 and therefore OPTIONAL with a null default, which is what lets a v1
/// archive still read as valid — see UnsupportedSchemaVersionException.Require. Schema.Current is
/// deliberately NOT bumped for it: the change is purely additive, System.Text.Json ignores unknown
/// properties so older builds read these archives fine, and a bump would instead make every already
/// shipped build reject them.</param>
public record CookBookManifest(
    string Id,
    string Name,
    Dimensions Canvas,
    Collection Collection,
    IReadOnlyDictionary<string, double> RecipeWeights,
    int SchemaVersion = Schema.Current,
    int? TargetSupply = null) : ISchemaVersioned;
