namespace Nfty.Core.Editing;

/// <summary>
/// A fixed-size, randomly-addressable raster the edit commands paint on, generic over the pixel it
/// stores: <see cref="ValueMap"/> supplies <see cref="GrayPixel"/>, <see cref="ColorMap"/> supplies
/// <c>Rgba32</c>. The commands hold geometry only — every "what does this pixel become" decision
/// travels as a <typeparamref name="TPixel"/>, so one implementation of disc stamping, shape
/// rasterising, region scanning and the undo snapshot serves both surfaces.
/// </summary>
/// <remarks>
/// <see cref="AlphaOf"/> and <see cref="WithAlpha"/> are here rather than on the pixel because
/// <c>Rgba32</c> is ImageSharp's type and cannot be made to implement an interface of ours. They
/// carry format knowledge only — where this surface's pixel keeps its alpha — never policy: the
/// opacity lock's rule lives once in <see cref="RegionEditCommand{TPixel}"/>, so the two surfaces
/// have nothing to keep in step and nothing to drift apart on.
/// </remarks>
/// <typeparam name="TPixel">The pixel this surface stores.</typeparam>
public interface IEditSurface<TPixel> where TPixel : struct
{
    /// <summary>Width in pixels.</summary>
    int Width { get; }

    /// <summary>Height in pixels.</summary>
    int Height { get; }

    /// <summary>Whether a coordinate is inside the surface.</summary>
    /// <param name="x">Column.</param>
    /// <param name="y">Row.</param>
    /// <returns>True when the pixel exists.</returns>
    bool InBounds(int x, int y);

    /// <summary>Reads one pixel. The coordinate must be in bounds.</summary>
    /// <param name="x">Column.</param>
    /// <param name="y">Row.</param>
    /// <returns>The stored pixel.</returns>
    TPixel Get(int x, int y);

    /// <summary>Writes one pixel. Out-of-bounds coordinates are ignored, so a brush may run off the
    /// edge without the caller clipping first.</summary>
    /// <param name="x">Column.</param>
    /// <param name="y">Row.</param>
    /// <param name="pixel">The pixel to store.</param>
    void Set(int x, int y, TPixel pixel);

    /// <summary>The alpha channel of a pixel in this surface's format.</summary>
    /// <param name="pixel">The pixel to read.</param>
    /// <returns>Alpha, 0-255.</returns>
    byte AlphaOf(TPixel pixel);

    /// <summary>The same pixel with its alpha replaced and every other channel untouched.</summary>
    /// <param name="pixel">The pixel to rewrite.</param>
    /// <param name="alpha">The alpha it should carry.</param>
    /// <returns>A pixel identical to <paramref name="pixel"/> apart from its alpha.</returns>
    TPixel WithAlpha(TPixel pixel, byte alpha);
}
