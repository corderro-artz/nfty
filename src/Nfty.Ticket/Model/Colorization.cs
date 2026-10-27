namespace Nfty.Core.Model;

/// <summary>Which color space a layer's hue and saturation are interpreted in. Only H and S are
/// taken from the color — value/lightness always comes from the grayscale value-map, which is what
/// makes recoloring preserve the art's shading.</summary>
public enum ColorModel
{
    /// <summary>Hue, saturation, value.</summary>
    Hsv,

    /// <summary>Hue, saturation, lightness.</summary>
    Hsl,
}

/// <summary>A band of color a Dynamic layer can roll within.</summary>
/// <param name="HueMin">Lowest hue in degrees.</param>
/// <param name="HueMax">Highest hue in degrees. The interval is half-open — <c>ColorRoller</c>
/// samples <c>Min + r*(Max-Min)</c> with <c>r</c> in <c>[0,1)</c> — so Max itself is only reached by
/// a degenerate range where Min equals Max.</param>
/// <param name="SatMin">Lowest saturation, 0..100.</param>
/// <param name="SatMax">Highest saturation, 0..100; half-open in the same way.</param>
public record ColorRange(double HueMin, double HueMax, double SatMin, double SatMax);

/// <summary>One weighted color option. Exactly one of <see cref="Range"/> or <see cref="Fixed"/> is
/// set.</summary>
/// <param name="Weight">Relative weight against the other entries. Zero shelves the entry.</param>
/// <param name="Range">The band to roll within, or null when this entry is a fixed color.</param>
/// <param name="Fixed">A prefixed color spec (<c>hex:</c>, <c>rgb:</c>, <c>hsl:</c>, <c>hsv:</c>),
/// or null when this entry is a range. A missing or unknown prefix is a validation error, never a
/// guess.</param>
public record ColorEntry(double Weight, ColorRange? Range, string? Fixed);

/// <summary>How a Dynamic or Static layer is colored.</summary>
/// <param name="Model">The color space the entries are expressed in.</param>
/// <param name="HueQuantize">Hue bucket width in degrees, used when hashing the DNA. Quantizing is
/// what folds a continuous color space into something countable, so two assets whose hues fall in
/// one bucket are the same asset.</param>
/// <param name="SatQuantize">Saturation bucket width in percentage points, likewise.</param>
/// <param name="Entries">The weighted color options. A Static layer has exactly one, fixed.</param>
public record Colorization(
    ColorModel Model,
    int HueQuantize,
    int SatQuantize,
    IReadOnlyList<ColorEntry> Entries)
{
    /// <summary>The colorization a new layer of <paramref name="kind"/> starts with: one ranged entry
    /// for Dynamic, one fixed entry for Static, none for Custom (composited as-is).</summary>
    /// <param name="kind">The layer's kind.</param>
    /// <param name="model">The color space.</param>
    /// <param name="range">The band a Dynamic layer rolls within.</param>
    /// <param name="fixedSpec">The prefixed color a Static layer applies.</param>
    /// <returns>The colorization, or null for a Custom layer.</returns>
    public static Colorization? Starter(LayerKind kind, ColorModel model, ColorRange range, string fixedSpec) => kind switch
    {
        LayerKind.Dynamic => new Colorization(model, ColorDefaults.HueQuantize, ColorDefaults.SatQuantize,
            new[] { new ColorEntry(1, range, null) }),
        LayerKind.Static => new Colorization(model, ColorDefaults.HueQuantize, ColorDefaults.SatQuantize,
            new[] { new ColorEntry(1, null, fixedSpec) }),
        _ => null,
    };
}

/// <summary>What a new colorized layer starts with, stated once so the two ways of creating a layer
/// cannot produce two different layers. The editor is where these are tuned.</summary>
public static class ColorDefaults
{
    /// <summary>Hue bucket width in degrees.</summary>
    public const int HueQuantize = 12;

    /// <summary>Saturation bucket width in percentage points.</summary>
    public const int SatQuantize = 4;

    /// <summary>The band a new Dynamic layer rolls within.</summary>
    public static readonly ColorRange Range = new(0, 360, 40, 100);

    /// <summary>The color a new Static layer applies.</summary>
    public const string FixedSpec = "hex:d6249f";
}
