namespace Nfty.Core.Model;

public enum ColorModel { Hsv, Hsl }

public record ColorRange(double HueMin, double HueMax, double SatMin, double SatMax);

/// <summary>Exactly one of <see cref="Range"/> or <see cref="Fixed"/> is set.</summary>
public record ColorEntry(double Weight, ColorRange? Range, string? Fixed);

public record Colorization(
    ColorModel Model,
    int HueQuantize,
    int SatQuantize,
    IReadOnlyList<ColorEntry> Entries);
