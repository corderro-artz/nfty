using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Generation;

/// <summary>One layer's contribution as published metadata.</summary>
/// <param name="IngredientId">The layer's id.</param>
/// <param name="IngredientName">The trait type shown to a marketplace.</param>
/// <param name="VariantId">The variant's id.</param>
/// <param name="VariantName">The trait value shown to a marketplace.</param>
public record TraitSelection(string IngredientId, string IngredientName, string VariantId, string VariantName);

/// <summary>
/// The resolved color of one layer, for the rich nfty metadata. Represents every kind:
/// Dynamic and Static carry (Model, H, S); Custom carries none (all null, composited as-is).
/// </summary>
/// <param name="LayerId">The layer's ingredient id.</param>
/// <param name="Kind">The layer's kind.</param>
/// <param name="Model">The color model rolled in; null for a Custom layer.</param>
/// <param name="H">The rolled hue in degrees; null for a Custom layer.</param>
/// <param name="S">The rolled saturation as a 0..1 fraction; null for a Custom layer.</param>
public record ColorRoll(string LayerId, LayerKind Kind, ColorModel? Model, double? H, double? S);

/// <summary>
/// One layer the roll left out of this asset entirely.
/// </summary>
/// <param name="IngredientId">The layer's id.</param>
/// <param name="IngredientName">Its display name — the trait type it would have been published
/// under had it appeared, which is what a rarity table needs to name it.</param>
/// <remarks>
/// Recorded because an absent layer is otherwise INVISIBLE in a finished Set: it contributes no
/// trait, so nothing downstream can tell "this asset has no hat" from "this collection has no hat
/// layer". The OpenSea file still omits it — that is the standard reading of not having something,
/// and it is built from <see cref="GeneratedAsset.Traits"/>, which an absent layer never enters.
/// This travels in the rich nfty file instead, which is what the dual-metadata split is for.
/// </remarks>
public record AbsentLayer(string IngredientId, string IngredientName);

/// <summary>
/// One finished asset. Owns its composited <see cref="Image"/>: dispose the asset (or the
/// <see cref="GeneratedSet"/> holding it) once the pixels have been written or drawn.
/// </summary>
public class GeneratedAsset : IDisposable
{
    /// <summary>The asset's number within the Set.</summary>
    public required int SetNumber { get; init; }
    /// <summary>Its identity hash.</summary>
    public required string Dna { get; init; }
    /// <summary>The recipe it was rolled from.</summary>
    public required string RecipeId { get; init; }
    /// <summary>That recipe's display name, published as the "Type" trait.</summary>
    public required string RecipeName { get; init; }
    /// <summary>The composited artwork. Owned by this asset.</summary>
    public required Image<Rgba32> Image { get; init; }
    /// <summary>What each layer contributed.</summary>
    public required IReadOnlyList<TraitSelection> Traits { get; init; }
    /// <summary>The per-layer color record for the rich metadata.</summary>
    public required IReadOnlyList<ColorRoll> ColorRolls { get; init; }

    /// <summary>The layers this roll left out entirely. Empty for every asset of a recipe with no
    /// optional layers, which is every asset generated before they existed.</summary>
    public IReadOnlyList<AbsentLayer> AbsentLayers { get; init; } = Array.Empty<AbsentLayer>();

    /// <summary>Frees the composited artwork.</summary>
    public void Dispose() => Image.Dispose();
}

/// <summary>
/// A whole generated collection, held in memory. Disposing it disposes every asset image, so
/// <c>using var set = Generator.Generate(...)</c> is the safe default. A caller that cannot
/// afford the whole set at once should use <see cref="Generator.GenerateStreaming"/> instead
/// and dispose each asset as it lands.
/// </summary>
/// <param name="CollectionName">The collection's published name.</param>
/// <param name="Description">The collection's description.</param>
/// <param name="Symbol">The collection's ticker-style symbol.</param>
/// <param name="Seed">The seed that produced the run.</param>
/// <param name="Assets">The finished assets, in set-number order. Owned by this set.</param>
/// <param name="CookbookSha256">Hash of the source <c>.cbk</c>; null for a book never read from a file.</param>
/// <param name="UniqueDna">Whether the run enforced distinct DNA.</param>
public record GeneratedSet(
    string CollectionName, string Description, string Symbol, string Seed,
    IReadOnlyList<GeneratedAsset> Assets,
    string? CookbookSha256 = null,
    bool UniqueDna = true) : IDisposable
{
    /// <summary>Frees every asset image in the set.</summary>
    public void Dispose()
    {
        foreach (var asset in Assets) asset.Dispose();
    }
}

/// <summary>How far a generation run has got. <see cref="Fraction"/> suits a progress bar.</summary>
/// <param name="Completed">Assets finished so far.</param>
/// <param name="Total">Assets the run will produce.</param>
public readonly record struct GenerationProgress(int Completed, int Total)
{
    /// <summary>Completion as 0..1, suitable for a progress bar.</summary>
    public double Fraction => Total <= 0 ? 0 : (double)Completed / Total;
}

/// <summary>Everything one generation run needs to know.</summary>
/// <param name="Count">How many assets to produce.</param>
/// <param name="Seed">The string seed driving the RNG. The same cookbook and seed reproduce a run
/// byte for byte, across machine locales and CPU architectures alike.</param>
/// <param name="RecipeId">Restrict the run to one Recipe, overriding the cookbook's weights
/// entirely. Null rolls a Recipe per asset, which is the normal case.</param>
/// <param name="MaxRerollsPerAsset">How many times a single asset may be re-rolled before the run
/// gives up — a rule violation and a DNA collision both cost an attempt. Exhausting it raises
/// <see cref="RuleConflictException"/> when all combinations are known to be forbidden,
/// <see cref="UniqueSpaceExhaustedException"/> when uniqueness is enabled, or an ordinary
/// <see cref="InvalidOperationException"/> when uniqueness is disabled and rules reject every attempt.</param>
/// <param name="EnforceUniqueDna">
/// When true (the default), every asset must have distinct DNA: a roll colliding with one already
/// produced is discarded and re-rolled, and a run that cannot fill its quota from the unique space
/// fails with <see cref="UniqueSpaceExhaustedException"/>. When false, every roll is accepted even
/// if its DNA repeats, and identity is carried by the sequential token id (the output number) as
/// ERC-721 defines it — so any Count is producible regardless of the unique space. This skips both
/// the dedup bookkeeping and the space-counting, which is the expensive part of a large run on a
/// slow machine. Incompatibility rules are still enforced either way.
/// </param>
/// <param name="Parallelism">How wide the render step may run; null means
/// <see cref="Nfty.Core.Parallelism.Default"/>. Never changes a result, and is not recorded anywhere.</param>
public record GenerateOptions(
    int Count, string Seed, string? RecipeId = null,
    int MaxRerollsPerAsset = GenerateOptions.DefaultMaxRerolls,
    bool EnforceUniqueDna = true,
    Parallelism? Parallelism = null)
{
    /// <summary>The reroll budget when a caller does not set one. Shared with the CLI's
    /// <c>--max-rerolls</c> default so the two cannot drift apart.</summary>
    public const int DefaultMaxRerolls = 10000;
}
