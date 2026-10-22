using Nfty.Core.Output;

namespace Nfty.Model;

/// <summary>
/// The text spellings of <see cref="ModelFormat"/> and <see cref="Pivot"/>, in one place. The CLI's
/// <c>--format</c> / <c>--pivot</c> and <c>set.json</c>'s <c>models</c> block both use these tokens, so
/// the two can never disagree about what a recorded Set was made with.
/// </summary>
public static class ModelTokens
{
    private static readonly (ModelFormat Format, string Token)[] Formats =
    [
        (ModelFormat.Glb, "glb"),
        (ModelFormat.GltfWithBin, "gltf"),
        (ModelFormat.GltfEmbedded, "gltf-embedded"),
    ];

    private static readonly (Pivot Pivot, string Token)[] Pivots =
    [
        (Pivot.BottomCenter, "bottom-center"),
        (Pivot.Center, "center"),
        (Pivot.MinCorner, "min-corner"),
    ];

    /// <summary>Every format token, in the order the CLI lists them.</summary>
    public static IReadOnlyList<string> FormatNames { get; } = Formats.Select(f => f.Token).ToArray();

    /// <summary>Every pivot token, in the order the CLI lists them.</summary>
    public static IReadOnlyList<string> PivotNames { get; } = Pivots.Select(p => p.Token).ToArray();

    /// <summary>The token for a format.</summary>
    /// <param name="format">The format.</param>
    /// <returns>Its token.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Not a named format.</exception>
    public static string NameOf(ModelFormat format) =>
        Formats.Where(f => f.Format == format).Select(f => f.Token).FirstOrDefault()
            ?? throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown model format.");

    /// <summary>The token for a pivot.</summary>
    /// <param name="pivot">The pivot.</param>
    /// <returns>Its token.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Not a named pivot.</exception>
    public static string NameOf(Pivot pivot) =>
        Pivots.Where(p => p.Pivot == pivot).Select(p => p.Token).FirstOrDefault()
            ?? throw new ArgumentOutOfRangeException(nameof(pivot), pivot, "Unknown pivot.");

    /// <summary>The format a token names.</summary>
    /// <param name="token">A token from <see cref="FormatNames"/>.</param>
    /// <returns>The format.</returns>
    /// <exception cref="ArgumentException">The token is unknown; nothing is guessed.</exception>
    public static ModelFormat FormatOf(string token)
    {
        foreach (var (format, name) in Formats)
            if (string.Equals(name, token, StringComparison.Ordinal)) return format;
        throw new ArgumentException(
            $"Unknown model format '{token}'; expected {string.Join(", ", FormatNames)}.", nameof(token));
    }

    /// <summary>The pivot a token names.</summary>
    /// <param name="token">A token from <see cref="PivotNames"/>.</param>
    /// <returns>The pivot.</returns>
    /// <exception cref="ArgumentException">The token is unknown; nothing is guessed.</exception>
    public static Pivot PivotOf(string token)
    {
        foreach (var (pivot, name) in Pivots)
            if (string.Equals(name, token, StringComparison.Ordinal)) return pivot;
        throw new ArgumentException(
            $"Unknown pivot '{token}'; expected {string.Join(", ", PivotNames)}.", nameof(token));
    }

    /// <summary>The <c>set.json</c> block describing a cook made with these options.</summary>
    /// <param name="options">The options.</param>
    /// <returns>Tokens plus voxel size, with nothing skipped yet.</returns>
    public static SetModels Record(RiseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new SetModels(NameOf(options.Format), NameOf(options.Pivot), options.VoxelSize);
    }

    /// <summary>The options a recorded block describes.</summary>
    /// <param name="models">The block read from <c>set.json</c>.</param>
    /// <returns>The options.</returns>
    /// <exception cref="ArgumentException">A token is unknown.</exception>
    public static RiseOptions OptionsOf(SetModels models)
    {
        ArgumentNullException.ThrowIfNull(models);
        return new RiseOptions
        {
            Format = FormatOf(models.Format),
            Pivot = PivotOf(models.Pivot),
            VoxelSize = models.VoxelSize,
        };
    }
}
