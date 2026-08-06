namespace Nfty.Core.Model;

/// <summary>Which colour space a layer's hue and saturation are interpreted in. Only H and S are
/// taken from the colour — value/lightness always comes from the grayscale value-map, which is what
/// makes recolouring preserve the art's shading.</summary>
public enum ColorModel
{
    /// <summary>Hue, saturation, value.</summary>
    Hsv,

    /// <summary>Hue, saturation, lightness.</summary>
    Hsl,
}

/// <summary>A band of colour a Dynamic layer can roll within.</summary>
/// <param name="HueMin">Lowest hue in degrees.</param>
/// <param name="HueMax">Highest hue in degrees. The interval is half-open — <c>ColorRoller</c>
/// samples <c>Min + r*(Max-Min)</c> with <c>r</c> in <c>[0,1)</c> — so Max itself is only reached by
/// a degenerate range where Min equals Max.</param>
/// <param name="SatMin">Lowest saturation, 0..100.</param>
/// <param name="SatMax">Highest saturation, 0..100; half-open in the same way.</param>
public record ColorRange(double HueMin, double HueMax, double SatMin, double SatMax);

/// <summary>One weighted colour option. Exactly one of <see cref="Range"/> or <see cref="Fixed"/> is
/// set.</summary>
/// <param name="Weight">Relative weight against the other entries. Zero shelves the entry.</param>
/// <param name="Range">The band to roll within, or null when this entry is a fixed colour.</param>
/// <param name="Fixed">A prefixed colour spec (<c>hex:</c>, <c>rgb:</c>, <c>hsl:</c>, <c>hsv:</c>),
/// or null when this entry is a range. A missing or unknown prefix is a validation error, never a
/// guess.</param>
public record ColorEntry(double Weight, ColorRange? Range, string? Fixed);

/// <summary>How a Dynamic or Static layer is coloured.</summary>
/// <param name="Model">The colour space the entries are expressed in.</param>
/// <param name="HueQuantize">Hue bucket width in degrees, used when hashing the DNA. Quantizing is
/// what folds a continuous colour space into something countable, so two assets whose hues fall in
/// one bucket are the same asset.</param>
/// <param name="SatQuantize">Saturation bucket width in percentage points, likewise.</param>
/// <param name="Entries">The weighted colour options. A Static layer has exactly one, fixed.</param>
public record Colorization(
    ColorModel Model,
    int HueQuantize,
    int SatQuantize,
    IReadOnlyList<ColorEntry> Entries);
