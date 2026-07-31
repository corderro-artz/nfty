using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Editing;

/// <summary>
/// Editable single-layer raster: one grayscale value (0–255) plus one alpha (0–255) per pixel,
/// bound to a fixed canvas size. Grayscale is guaranteed by construction — there is no way to
/// store independent R/G/B. Materialize an <see cref="Image{Rgba32}"/> only at export/preview.
/// </summary>
public sealed class ValueMap
{
    private readonly byte[] _value;
    private readonly byte[] _alpha;

    public int Width { get; }
    public int Height { get; }

    public ValueMap(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "ValueMap dimensions must be positive.");
        Width = width;
        Height = height;
        _value = new byte[width * height];
        _alpha = new byte[width * height];
    }

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
    public bool InBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

    public byte GetValue(int x, int y) => _value[Index(x, y)];
    public byte GetAlpha(int x, int y) => _alpha[Index(x, y)];

    public void Set(int x, int y, byte value, byte alpha)
    {
        int i = Index(x, y);
        _value[i] = value;
        _alpha[i] = alpha;
    }

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
