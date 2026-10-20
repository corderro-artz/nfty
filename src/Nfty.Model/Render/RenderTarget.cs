using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Render;

/// <summary>A plain pixel-and-depth surface <c>Raster</c> writes into: one background-filled
/// color per pixel and a z-buffer initialized to "nothing here yet". The array references are fixed
/// at construction (D7); their contents mutate freely as <c>Raster</c> draws. Single owner; not
/// thread-safe.</summary>
public sealed class RenderTarget
{
    /// <summary>Pixels across.</summary>
    public int Width { get; }

    /// <summary>Pixels down.</summary>
    public int Height { get; }

    /// <summary>One color per pixel, row-major, length <see cref="Width"/> × <see cref="Height"/>.</summary>
    public Rgba32[] Color { get; }

    /// <summary>One depth value per pixel, same shape as <see cref="Color"/>. Smaller is closer;
    /// <see cref="float.PositiveInfinity"/> means nothing has drawn there yet.</summary>
    public float[] Depth { get; }

    /// <summary>Allocates a <see cref="Width"/> × <see cref="Height"/> surface. Both dimensions must
    /// be positive.</summary>
    public RenderTarget(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        Color = new Rgba32[width * height];
        Depth = new float[width * height];
    }

    /// <summary>Resets every pixel to <paramref name="background"/> and every depth to infinity.</summary>
    public void Clear(Rgba32 background)
    {
        Array.Fill(Color, background);
        Array.Fill(Depth, float.PositiveInfinity);
    }
}
