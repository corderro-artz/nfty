namespace Nfty.Core.Editing;

/// <summary>
/// One pixel of a <see cref="ValueMap"/>: a single grayscale value plus an alpha. This is what keeps
/// the value-map's guarantee true now that the paint stack is generic — a command painting a
/// value-map can only ever hand it a <c>GrayPixel</c>, and a <c>GrayPixel</c> has nowhere to put
/// independent R/G/B. The guarantee is a property of the type, not of the care each command takes.
/// </summary>
/// <param name="Value">Grayscale value, 0-255; written to R, G and B alike on export.</param>
/// <param name="Alpha">Alpha, 0-255.</param>
public readonly record struct GrayPixel(byte Value, byte Alpha);
