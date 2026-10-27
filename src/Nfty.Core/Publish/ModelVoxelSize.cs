using System.Globalization;

namespace Nfty.Core.Publish;

/// <summary>
/// The one range a model's voxel size may take, so the CLI, the Rise page and the cook dialog cannot
/// disagree about it.
/// </summary>
public static class ModelVoxelSize
{
    /// <summary>The smallest voxel size: world units per pixel.</summary>
    public const double Min = 0.001;

    /// <summary>The largest voxel size.</summary>
    public const double Max = 1000;

    /// <summary>How many decimal places a typed voxel size keeps.</summary>
    public const int Decimals = 3;

    /// <summary>Whether a voxel size is usable.</summary>
    /// <param name="value">The size in world units per pixel.</param>
    /// <returns>Null when finite and within <see cref="Min"/>..<see cref="Max"/>; otherwise a sentence.</returns>
    public static string? ProblemWith(double value) =>
        double.IsFinite(value) && value >= Min && value <= Max
            ? null
            : string.Create(CultureInfo.InvariantCulture,
                $"Voxel size must be between {Min} and {Max}, but {value} was given.");
}
