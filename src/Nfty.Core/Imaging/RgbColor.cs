namespace Nfty.Core.Imaging;

/// <summary>An 8-bit-per-channel color, used as the neutral currency between the color-space
/// conversions and ImageSharp.</summary>
/// <param name="R">Red, 0-255.</param>
/// <param name="G">Green, 0-255.</param>
/// <param name="B">Blue, 0-255.</param>
public readonly record struct RgbColor(byte R, byte G, byte B);
