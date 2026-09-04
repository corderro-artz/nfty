namespace Nfty.Core.Editing;

/// <summary>Brush settings: stamp diameter in pixels and the pixel it paints. One type for both
/// surfaces — a gray brush is a <c>Brush&lt;GrayPixel&gt;</c>, a color brush a
/// <c>Brush&lt;Rgba32&gt;</c> — because nothing about a brush differs between them beyond its
/// payload. The payload carries its own alpha, which is what lets an unlocked session paint a
/// semi-transparent stroke; under <see cref="OpacityLock.Locked"/> it is snapped when applied.</summary>
/// <typeparam name="TPixel">The pixel the target surface stores.</typeparam>
/// <param name="Size">Stamp diameter in pixels.</param>
/// <param name="Pixel">What the brush paints.</param>
public readonly record struct Brush<TPixel>(int Size, TPixel Pixel) where TPixel : struct;
