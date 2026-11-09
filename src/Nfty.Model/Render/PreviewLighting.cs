using System.Numerics;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Render;

/// <summary>Preview-only intensity and color for the rasterizer's existing camera-relative light.
/// Does not modify a mesh, texture, archive, or exported model.</summary>
public readonly record struct PreviewLighting
{
    /// <summary>The original full-strength white lighting, preserving the original rendered pixels.</summary>
    public static PreviewLighting Default { get; } = new(100, new Rgba32(255, 255, 255));

    /// <summary>Light intensity from zero (black) through 100 (full strength).</summary>
    public int Value { get; }

    /// <summary>The light's RGB color. Alpha is ignored; rendered texture alpha is preserved.</summary>
    public Rgba32 Color { get; }

    /// <summary>Creates preview lighting.</summary>
    /// <param name="value">Intensity from zero through 100.</param>
    /// <param name="color">RGB light color; alpha is ignored.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is outside zero through 100.</exception>
    public PreviewLighting(int value, Rgba32 color)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 100);
        Value = value;
        Color = new Rgba32(color.R, color.G, color.B);
    }

    internal Vector3 Channels => new Vector3(Color.R / 255f, Color.G / 255f, Color.B / 255f) * (Value / 100f);
}
