namespace Nfty.Core.Model;

/// <summary>
/// A Kitchen: the top-level workspace, and the sixth domain word. It is the folder loose Recipes and
/// Ingredients are saved into, given a name — the creation-flows spec settled that the "loose items
/// folder" and the "workspace" are one concept rather than two.
/// </summary>
/// <param name="Id">Stable identifier, derived from the name the way every other id here is.</param>
/// <param name="Name">What the titlebar's workspace chip shows.</param>
/// <param name="Description">Optional free text; empty when the author did not write one.</param>
/// <remarks>
/// This manifest deliberately does NOT list what the Kitchen contains. Membership is whatever sits
/// in the folder beside the .ktn, discovered on open — see <see cref="Formats.Kitchen"/>. A recorded
/// list would go stale the moment a file was renamed or moved outside the app, and the Kitchen would
/// then be describing a workspace that no longer exists.
/// </remarks>
public record KitchenManifest(
    string Id,
    string Name,
    string Description = "",
    int SchemaVersion = Schema.Current) : ISchemaVersioned;
