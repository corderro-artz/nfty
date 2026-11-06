using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Render;

/// <summary>A plain pixel-and-depth surface <c>Raster</c> writes into. Construction leaves both
/// arrays zero-filled; call <see cref="Clear"/> before rendering to initialize the background and
/// infinite depth. The array references are fixed at construction (D7); their contents mutate freely
/// as <c>Raster</c> draws. Single owner; not thread-safe.</summary>
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
    /// be positive. Colors and depths are initially zero; call <see cref="Clear"/> before rendering.</summary>
    /// <param name="width">Width in pixels; must be positive.</param>
    /// <param name="height">Height in pixels; must be positive.</param>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is not positive.</exception>
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
    /// <param name="background">The color to fill with.</param>
    public void Clear(Rgba32 background)
    {
        Array.Fill(Color, background);
        Array.Fill(Depth, float.PositiveInfinity);
    }
}
