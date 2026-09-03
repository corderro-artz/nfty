using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Editing;

/// <summary>
/// Editable single-layer raster: one grayscale value (0–255) plus one alpha (0–255) per pixel,
/// bound to a fixed canvas size. Grayscale is guaranteed by construction — there is no way to
/// store independent R/G/B. Materialize an <see cref="Image{Rgba32}"/> only at export/preview.
/// </summary>
/// <remarks>
/// It is an <see cref="IEditSurface{GrayPixel}"/>, which is how the generic paint stack reaches it.
/// That costs the guarantee nothing: <see cref="GrayPixel"/> carries one value and one alpha, so a
/// command written against the generic surface has no colour to hand this map even if it holds one.
/// Nothing on this type accepts independent R/G/B, and nothing should be added that does.
/// </remarks>
public sealed class ValueMap : IEditSurface<GrayPixel>
{
    private readonly byte[] _value;
    private readonly byte[] _alpha;

    /// <summary>Width in pixels.</summary>
    public int Width { get; }
    /// <summary>Height in pixels.</summary>
    public int Height { get; }

    /// <summary>Creates a blank map.</summary>
    /// <param name="width">Width in pixels; must be positive.</param>
    /// <param name="height">Height in pixels; must be positive.</param>
    /// <exception cref="ArgumentOutOfRangeException">Either dimension is zero or negative.</exception>
    public ValueMap(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "ValueMap dimensions must be positive.");
        Width = width;
        Height = height;
        _value = new byte[width * height];
        _alpha = new byte[width * height];
    }

    /// <summary>A blank map at the CookBook's canvas size.</summary>
    /// <param name="canvas">The canvas to match.</param>
    /// <returns>A new map.</returns>
    public static ValueMap ForCanvas(Dimensions canvas) => new(canvas.Width, canvas.Height);

    /// <summary>An independent deep copy — cloned value/alpha buffers, same dimensions.</summary>
    public ValueMap Clone()
    {
        var c = new ValueMap(Width, Height);
        Array.Copy(_value, c._value, _value.Length);
        Array.Copy(_alpha, c._alpha, _alpha.Length);
        return c;
    }

    private int Index(int x, int y) => y * Width + x;
    /// <summary>Whether a coordinate is inside the map.</summary>
    /// <param name="x">Column.</param>
    /// <param name="y">Row.</param>
    /// <returns>True when the pixel exists.</returns>
    public bool InBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

    /// <summary>The grayscale value at a pixel.</summary>
    /// <param name="x">Column.</param>
    /// <param name="y">Row.</param>
    /// <returns>0-255.</returns>
    public byte GetValue(int x, int y) => _value[Index(x, y)];
    /// <summary>The alpha at a pixel.</summary>
    /// <param name="x">Column.</param>
    /// <param name="y">Row.</param>
    /// <returns>0-255.</returns>
    public byte GetAlpha(int x, int y) => _alpha[Index(x, y)];

    /// <summary>Writes one pixel. Out-of-bounds coordinates are ignored, so a brush may run off
    /// the edge without the caller clipping first.</summary>
    /// <param name="x">Column.</param>
    /// <param name="y">Row.</param>
    /// <param name="value">Grayscale value.</param>
    /// <param name="alpha">Alpha.</param>
    public void Set(int x, int y, byte value, byte alpha)
    {
        if (!InBounds(x, y)) return;
        int i = Index(x, y);
        _value[i] = value;
        _alpha[i] = alpha;
    }

    /// <summary>The pixel at a coordinate, as the generic paint stack sees it.</summary>
    /// <param name="x">Column.</param>
    /// <param name="y">Row.</param>
    /// <returns>Its value and alpha.</returns>
    public GrayPixel Get(int x, int y) => new(_value[Index(x, y)], _alpha[Index(x, y)]);

    /// <summary>Writes one pixel. Out-of-bounds coordinates are ignored.</summary>
    /// <param name="x">Column.</param>
    /// <param name="y">Row.</param>
    /// <param name="pixel">The value and alpha to store.</param>
    public void Set(int x, int y, GrayPixel pixel) => Set(x, y, pixel.Value, pixel.Alpha);

    /// <inheritdoc />
    public byte AlphaOf(GrayPixel pixel) => pixel.Alpha;

    /// <inheritdoc />
    public GrayPixel WithAlpha(GrayPixel pixel, byte alpha) => pixel with { Alpha = alpha };

    /// <summary>Renders to a grayscale RGBA image — the form an <c>.igt</c> stores.</summary>
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
                {
                    byte v = _value[Index(x, y)];
                    row[x] = new Rgba32(v, v, v, _alpha[Index(x, y)]);
                }
            }
        });
        return img;
    }

    /// <summary>Reads a map back out of a decoded variant image.</summary>
    /// <param name="img">The source image.</param>
    /// <returns>A new map.</returns>
    public static ValueMap FromImage(Image<Rgba32> img)
    {
        var map = new ValueMap(img.Width, img.Height);
        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < img.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < img.Width; x++)
                    map.Set(x, y, row[x].R, row[x].A);
            }
        });
        return map;
    }
}
