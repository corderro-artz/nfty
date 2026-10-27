namespace Nfty.Core.Model;

/// <summary>Canvas size in pixels. Held by the CookBook and the single source of truth for the whole
/// book: every variant image in every nested Recipe is validated against it, so a Set cannot contain
/// two assets of different sizes.</summary>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
public record Dimensions(int Width, int Height)
{
    /// <summary>The most pixels a canvas may have, so allocating a raster for one cannot overflow an
    /// <c>int</c> or exhaust memory.</summary>
    public const long MaxPixels = 100_000_000;
}
