using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Generation;

public record TraitSelection(string IngredientId, string IngredientName, string VariantId, string VariantName);

/// <summary>
/// The resolved color of one layer, for the rich nfty metadata. Represents every kind:
/// Dynamic and Static carry (Model, H, S); Custom carries none (all null, composited as-is).
/// </summary>
public record ColorRoll(string LayerId, LayerKind Kind, ColorModel? Model, double? H, double? S);

public class GeneratedAsset
{
    public required int SetNumber { get; init; }
    public required string Dna { get; init; }
    public required string RecipeId { get; init; }
    public required string RecipeName { get; init; }
    public required Image<Rgba32> Image { get; init; }
    public required IReadOnlyList<TraitSelection> Traits { get; init; }
    public required IReadOnlyList<ColorRoll> ColorRolls { get; init; }
}

public record GeneratedSet(
    string CollectionName, string Description, string Symbol, string Seed,
    IReadOnlyList<GeneratedAsset> Assets);

public record GenerateOptions(int Count, string Seed, string? RecipeId = null, int MaxRerollsPerAsset = 10000);
