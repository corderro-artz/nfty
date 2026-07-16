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

/// <summary>
/// One finished asset. Owns its composited <see cref="Image"/>: dispose the asset (or the
/// <see cref="GeneratedSet"/> holding it) once the pixels have been written or drawn.
/// </summary>
public class GeneratedAsset : IDisposable
{
    public required int SetNumber { get; init; }
    public required string Dna { get; init; }
    public required string RecipeId { get; init; }
    public required string RecipeName { get; init; }
    public required Image<Rgba32> Image { get; init; }
    public required IReadOnlyList<TraitSelection> Traits { get; init; }
    public required IReadOnlyList<ColorRoll> ColorRolls { get; init; }

    public void Dispose() => Image.Dispose();
}

/// <summary>
/// A whole generated collection, held in memory. Disposing it disposes every asset image, so
/// <c>using var set = Generator.Generate(...)</c> is the safe default. A caller that cannot
/// afford the whole set at once should use <see cref="Generator.GenerateStreaming"/> instead
/// and dispose each asset as it lands.
/// </summary>
public record GeneratedSet(
    string CollectionName, string Description, string Symbol, string Seed,
    IReadOnlyList<GeneratedAsset> Assets,
    string? CookbookSha256 = null) : IDisposable
{
    public void Dispose()
    {
        foreach (var asset in Assets) asset.Dispose();
    }
}

/// <summary>How far a generation run has got. <see cref="Fraction"/> suits a progress bar.</summary>
public readonly record struct GenerationProgress(int Completed, int Total)
{
    public double Fraction => Total <= 0 ? 0 : (double)Completed / Total;
}

public record GenerateOptions(int Count, string Seed, string? RecipeId = null, int MaxRerollsPerAsset = 10000);
