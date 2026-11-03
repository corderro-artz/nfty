using Nfty.Core.Imaging;
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

    /// <summary>
    /// Reads a picture for import: decodes it, reads its embedded color profile with nfty's own reader,
    /// and converts it to sRGB when asked and possible.
    /// </summary>
    /// <remarks>The one load every picture import shares (the CLI's <c>add variant</c> and
    /// <c>new ingredient</c>, the editor's variant import); the Import image form holds both
    /// forms for its live preview and calls <see cref="PictureProfile.ConvertToSrgb"/> itself.</remarks>
    /// <param name="path">The PNG or JPEG.</param>
    /// <param name="convertToSrgb">Convert a non-sRGB profile's pixels to sRGB.</param>
    /// <param name="profile">What the picture's profile was.</param>
    /// <returns>The decoded picture, converted when <paramref name="convertToSrgb"/> and the profile
    /// allow; the caller owns it.</returns>
    /// <exception cref="PictureUnreadableException">The file is missing, locked, not a PNG/JPEG, or
    /// damaged — with a sentence in the product's words.</exception>
    public static Image<Rgba32> Load(string path, bool convertToSrgb, out PictureProfile profile)
    {
        ArgumentNullException.ThrowIfNull(path);
        byte[] bytes;
        Image<Rgba32> image;
        try
        {
            bytes = File.ReadAllBytes(path);
            image = ImageFormats.Load(bytes);
        }
        catch (Exception ex) when (Unreadable(Path.GetFileName(path), ex) is { } sentence)
        {
            throw new PictureUnreadableException(sentence);
        }
        profile = PictureProfile.FromFile(bytes);
        if (convertToSrgb && profile.CanConvert) profile.ConvertToSrgb(image);
        return image;
    }

    /// <summary>
    /// The product's sentence for a picture that could not be read, or null for an exception that is
    /// not about the file (a bug, which should surface as itself).
    /// </summary>
    /// <remarks>No exception's own message reaches a screen: a decoder's or the OS's wording names
    /// internals at someone who only chose the wrong file. Every sentence says what to do next.</remarks>
    /// <param name="leaf">The file's name, as the sentence prints it.</param>
    /// <param name="ex">What reading it threw.</param>
    /// <returns>The sentence, or null.</returns>
    public static string? Unreadable(string leaf, Exception ex) => ex switch
    {
        UnknownImageFormatException =>
            $"“{leaf}” is not a PNG or JPEG picture. Save it as PNG from your drawing program and import that.",
        InvalidImageContentException or ImageFormatException =>
            $"“{leaf}” looks like a picture, but its data is damaged. Export it again from your drawing program and import the new file.",
        FileNotFoundException or DirectoryNotFoundException =>
            $"“{leaf}” is no longer there. Choose the file again.",
        UnauthorizedAccessException =>
            $"nfty is not allowed to read “{leaf}”. Check the file's permissions, or copy it somewhere you own, and try again.",
        IOException =>
            $"“{leaf}” could not be opened — another program may be holding it. Close it there and try again.",
        _ => null,
    };
}

/// <summary>A picture could not be read for import. The message is a product sentence, safe to show
/// verbatim on any screen.</summary>
public sealed class PictureUnreadableException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">The product sentence.</param>
    public PictureUnreadableException(string message) : base(message) { }
}
