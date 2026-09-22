using System.Linq;

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
/// <param name="AbsentPercent">How often each layer is left out entirely, as a PERCENT, keyed by
/// ingredient id. A layer not named here always appears, which is why the whole dictionary is
/// optional and null by default.
///
/// <para><b>It lives on the Recipe, and it could not live anywhere else.</b> An <c>.igt</c> is a
/// standalone file that a Kitchen hands to any project, so whether a Hat <i>appears</i> is not a
/// property of the hat artwork — the same ingredient is guaranteed in one recipe and a chase item in
/// another. This is the same shape <see cref="CookBookManifest.RecipeWeights"/> already uses one
/// level up: a dictionary on the parent, keyed by the child's id.</para>
///
/// <para><b>A percent, not a probability, and the unit is in the name.</b> 0..1 would mean a
/// conversion at every boundary a human touches — the CLI option, the form field, the report — and
/// each one is a chance to divide by a hundred twice or not at all. This converts exactly once, in
/// the roller.</para>
///
/// <para><b>A chance, not an absent WEIGHT</b>, because a weight is unstable: store one and then
/// double every variant weight, and the layer silently gets commoner without anyone touching it. A
/// chance survives variant edits, which is what an author means by "15% of the time".</para>
///
/// <para>100 means the layer never appears — shelved without deleting, the same thing a recipe
/// weight of 0 already means one level up. Added after schemaVersion 1 and therefore OPTIONAL with
/// a null default, for the same reasons spelled out on <see cref="CookBookManifest.TargetSupply"/>:
/// <c>Schema.Current</c> is deliberately NOT bumped.</para></param>
public record RecipeManifest(
    string Id,
    string Name,
    IReadOnlyList<string> LayerOrder,
    IReadOnlyList<IncompatibilityRule> Rules,
    int SchemaVersion = Schema.Current,
    IReadOnlyDictionary<string, double>? AbsentPercent = null) : ISchemaVersioned
{
    /// <summary>How often one layer is left out, as a percent. Zero for a layer that always
    /// appears, which is every layer of a recipe that does not use the feature.</summary>
    /// <param name="ingredientId">The layer to ask about.</param>
    /// <returns>0..100.</returns>
    public double AbsentPercentOf(string ingredientId) =>
        AbsentPercent is not null && AbsentPercent.TryGetValue(ingredientId, out var p) ? p : 0;

    /// <summary>Whether any layer of this recipe can be left out. This is what the GUI's "optional
    /// layers" toggle reads: the toggle is DERIVED from the data rather than stored beside it, so
    /// the two can never disagree about whether the feature is on.</summary>
    public bool HasOptionalLayers =>
        AbsentPercent is not null && AbsentPercent.Values.Any(p => p > 0);
}
