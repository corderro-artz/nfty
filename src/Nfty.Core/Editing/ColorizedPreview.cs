using Nfty.Core.Generation;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Editing;

/// <summary>
/// Renders what the cook produces from a variant's grayscale value-map: custom (or no colorization)
/// is returned as-is; dynamic/static roll (H,S) via the real <see cref="ColorRoller"/> and colorize via
/// the real <see cref="Colorizer"/>, so the preview matches generation. Returned image is live.
/// </summary>
public static class ColorizedPreview
{
    public static Image<Rgba32> Render(VariantDraft variant, LayerKind kind, Colorization? colorization, IRng rng)
    {
        Image<Rgba32> valueImg = variant.Map.ToImage();
        if (kind == LayerKind.Custom || colorization is null)
            return valueImg;

        using (valueImg)
        {
            var rolled = ColorRoller.Roll(colorization, rng);
            return Colorizer.Apply(valueImg, rolled.H, rolled.S, colorization.Model);
        }
    }
}
