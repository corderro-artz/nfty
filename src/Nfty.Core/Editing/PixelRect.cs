namespace Nfty.Core.Editing;

/// <summary>An integer pixel rectangle (top-left origin).</summary>
/// <param name="X">Left edge, in pixels from the image's left.</param>
/// <param name="Y">Top edge, in pixels from the image's top.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
public readonly record struct PixelRect(int X, int Y, int Width, int Height);
