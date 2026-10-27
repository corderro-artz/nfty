using Nfty.Core.Imaging;

namespace Nfty.App.Imaging;

/// <summary>Core's color, as the Avalonia one a brush or swatch wants.</summary>
public static class RgbColorExtensions
{
    /// <summary>Converts to an Avalonia color.</summary>
    /// <param name="rgb">The color.</param>
    /// <param name="alpha">Opacity, 255 opaque.</param>
    /// <returns>The same color for Avalonia.</returns>
    public static Avalonia.Media.Color ToAvalonia(this RgbColor rgb, byte alpha = 255) =>
        Avalonia.Media.Color.FromArgb(alpha, rgb.R, rgb.G, rgb.B);
}
