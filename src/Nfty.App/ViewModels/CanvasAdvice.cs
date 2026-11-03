namespace Nfty.App.ViewModels;

/// <summary>
/// The one place the app decides what canvas a new CookBook or loose Ingredient starts at, and when a
/// canvas is big enough to warn about. nfty is a pixel-art tool, so the default is a sprite-sized 32 x 32.
/// Nothing is refused: a large canvas is legal, it is only slow to cook in bulk (every asset is
/// composited, encoded and written at full size), and the form says so before the art exists.
/// </summary>
public static class CanvasAdvice
{
    /// <summary>The side a new canvas starts at.</summary>
    public const int DefaultSide = 32;

    /// <summary>The pixel count past which a canvas is called large: 512 x 512.</summary>
    public const long LargePixels = 512L * 512L;

    /// <summary>The sentence a form shows under a large canvas.</summary>
    public const string LargeWarning = "Over 512 × 512: cooking many assets this size is slow and memory-hungry.";

    /// <summary>Whether a canvas is past <see cref="LargePixels"/>.</summary>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <returns>True when the canvas is large.</returns>
    public static bool IsLarge(long width, long height) => width > 0 && height > 0 && width * height > LargePixels;
}
