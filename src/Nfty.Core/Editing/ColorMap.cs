using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Editing;

/// <summary>
/// Editable single-layer raster storing full-colour RGBA, bound to a fixed canvas size — the
/// colour-mode counterpart to <see cref="ValueMap"/>, and the only surface a Custom layer's artwork
/// can be painted on. It deliberately mirrors the value-map's shape (<see cref="ForCanvas"/>,
/// <see cref="Clone"/>, <see cref="ToImage"/>, <see cref="FromImage"/>, a bounds-tolerant
/// <see cref="Set(int,int,Rgba32)"/>) so the paint stack treats the two identically.
/// Materialize an <see cref="Image{Rgba32}"/> only at export/preview.
/// </summary>
public sealed class ColorMap : IEditSurface<Rgba32>
{
    private readonly Rgba32[] _pixels;

    /// <summary>Width in pixels.</summary>
    public int Width { get; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; }

    /// <summary>Creates a blank (fully transparent) map.</summary>
    /// <param name="width">Width in pixels; must be positive.</param>
    /// <param name="height">Height in pixels; must be positive.</param>
    /// <exception cref="ArgumentOutOfRangeException">Either dimension is zero or negative.</exception>
    public ColorMap(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "ColorMap dimensions must be positive.");
        Width = width;
        Height = height;
        _pixels = new Rgba32[width * height];
    }

    /// <summary>A blank map at the CookBook's canvas size.</summary>
    /// <param name="canvas">The canvas to match.</param>
    /// <returns>A new map.</returns>
    public static ColorMap ForCanvas(Dimensions canvas) => new(canvas.Width, canvas.Height);

    /// <summary>An independent deep copy — cloned pixel buffer, same dimensions.</summary>
    /// <returns>A new map sharing nothing with this one.</returns>
    public ColorMap Clone()
    {
        var c = new ColorMap(Width, Height);
        Array.Copy(_pixels, c._pixels, _pixels.Length);
        return c;
    }

    private int Index(int x, int y) => y * Width + x;

    /// <summary>Whether a coordinate is inside the map.</summary>
    /// <param name="x">Column.</param>
    /// <param name="y">Row.</param>
    /// <returns>True when the pixel exists.</returns>
    public bool InBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

    /// <summary>The colour at a pixel.</summary>
    /// <param name="x">Column.</param>
    /// <param name="y">Row.</param>
    /// <returns>Its RGBA.</returns>
    public Rgba32 Get(int x, int y) => _pixels[Index(x, y)];

    /// <summary>Writes one pixel. Out-of-bounds coordinates are ignored, so a brush may run off the
    /// edge without the caller clipping first.</summary>
    /// <param name="x">Column.</param>
    /// <param name="y">Row.</param>
    /// <param name="pixel">The colour to store.</param>
    public void Set(int x, int y, Rgba32 pixel)
    {
        if (!InBounds(x, y)) return;
        _pixels[Index(x, y)] = pixel;
    }

    /// <inheritdoc />
    public byte AlphaOf(Rgba32 pixel) => pixel.A;

    /// <inheritdoc />
    public Rgba32 WithAlpha(Rgba32 pixel, byte alpha) => new(pixel.R, pixel.G, pixel.B, alpha);

    /// <summary>Renders to a full-colour RGBA image — the form a Custom variant stores.</summary>
    /// <returns>A new image; the caller owns it.</returns>
    public Image<Rgba32> ToImage()
    {
        var img = new Image<Rgba32>(Width, Height);
        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < Width; x++)
                    row[x] = _pixels[Index(x, y)];
            }
        });
        return img;
    }

    /// <summary>
    /// Lifts a grayscale value-map into colour: each pixel becomes <c>(v, v, v, a)</c>, exactly what
    /// <see cref="ValueMap.ToImage"/> writes. This is what an author sees the instant they switch a
    /// Dynamic or Static layer into colour mode — the drawing they already have, in grey, ready to be
    /// painted over. Switching the palette must never alter a pixel, so this is a widening and not a
    /// recolouring: no hue, no saturation, no colorization is consulted.
    /// </summary>
    /// <param name="map">The value-map to widen.</param>
    /// <returns>A new colour map with the same dimensions and the same visible pixels.</returns>
    public static ColorMap FromValueMap(ValueMap map)
    {
        var color = new ColorMap(map.Width, map.Height);
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                byte v = map.GetValue(x, y);
                color.Set(x, y, new Rgba32(v, v, v, map.GetAlpha(x, y)));
            }
        return color;
    }

    /// <summary>Reads a map back out of a decoded variant image, every channel intact.</summary>
    /// <param name="img">The source image.</param>
    /// <returns>A new map.</returns>
    public static ColorMap FromImage(Image<Rgba32> img)
    {
        var map = new ColorMap(img.Width, img.Height);
        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < img.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < img.Width; x++)
                    map.Set(x, y, row[x]);
            }
        });
        return map;
    }
}
