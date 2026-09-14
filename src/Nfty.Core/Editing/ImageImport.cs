using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Nfty.Core.Editing;

/// <summary>
/// Turning a picture somebody drew elsewhere into something a layer can hold.
/// </summary>
/// <remarks>
/// <para>Two kinds of layer take an image two different ways, and the difference is the whole
/// reason this is one place rather than two. A <b>Custom</b> layer keeps every channel: the art IS
/// the art, composited untouched. A <b>Dynamic</b> or <b>Static</b> layer is a value-map, which
/// stores lightness only and is colorized at generation time — so foreign art has to be collapsed to
/// one channel first, and whatever color it had is gone.</para>
///
/// <para>It lives in Core because two screens now do it: the editor replaces one variant's raster
/// from a file, and importing a picture as a whole new layer builds the first one. Two copies of
/// "what happens to a color image on a value-map layer" is how the same PNG comes to import
/// differently depending on which button was pressed.</para>
/// </remarks>
public static class ImageImport
{
    /// <summary>
    /// The value-map a picture becomes on a Dynamic or Static layer.
    /// </summary>
    /// <remarks>
    /// <b>Desaturated FIRST, rather than handing the color image straight to
    /// <see cref="ValueMap.FromImage"/>.</b> That reads the RED channel — exact and lossless for its
    /// real job, round-tripping a layer's own already-grayscale PNG, and arbitrary for foreign art:
    /// pure green would import as pure BLACK and pure red as pure WHITE, though both read as
    /// mid-bright to the eye. <c>Grayscale()</c> is ITU-R BT.709 luminance, so R==G==B afterwards and
    /// <c>FromImage</c>'s own contract is left exactly as it was. Alpha is untouched either way.
    /// </remarks>
    /// <param name="image">The source picture. It is NOT modified.</param>
    /// <returns>Its lightness, as a value-map.</returns>
    public static ValueMap ToValueMap(Image<Rgba32> image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!HasColor(image)) return ValueMap.FromImage(image);

        using var gray = image.Clone(x => x.Grayscale());
        return ValueMap.FromImage(gray);
    }

    /// <summary>
    /// Whether any pixel carries color — channels that are not all equal.
    /// </summary>
    /// <remarks>
    /// What a value-map import WARNS about: the conversion is lossy exactly when this is true, and
    /// silently lossless when it is not. A fully transparent pixel cannot show color, so it is
    /// skipped — otherwise a stray colored pixel under zero alpha would warn about art that looks
    /// identical before and after.
    /// </remarks>
    /// <param name="image">The picture to examine.</param>
    /// <returns>True when at least one visible pixel is not gray.</returns>
    public static bool HasColor(Image<Rgba32> image)
    {
        ArgumentNullException.ThrowIfNull(image);
        bool found = false;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height && !found; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    var p = row[x];
                    if (p.A != 0 && (p.R != p.G || p.G != p.B)) { found = true; break; }
                }
            }
        });
        return found;
    }
}
