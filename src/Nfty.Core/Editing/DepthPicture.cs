using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Editing;

/// <summary>
/// Turns an ordinary picture into a <see cref="DepthMap"/>: the way an author who draws depth in
/// any pixel editor hands it to nfty. The red channel is the front depth, the green channel the back
/// depth; blue and alpha are ignored, so a picture that happens to carry them (a preview, a screenshot
/// of the map) reads the same as one that does not.
/// </summary>
public static class DepthPicture
{
    /// <summary>Reads front (red) and back (green) depth, pixel for pixel, from a picture.</summary>
    /// <param name="picture">The depth picture; its size becomes the map's size.</param>
    /// <returns>A new <see cref="DepthMap"/> the size of the picture.</returns>
    public static DepthMap ToMap(Image<Rgba32> picture)
    {
        ArgumentNullException.ThrowIfNull(picture);
        int width = picture.Width, height = picture.Height;
        var bytes = new byte[checked(width * height * 2)];
        int i = 0;
        picture.ProcessPixelRows(rows =>
        {
            for (int y = 0; y < rows.Height; y++)
            {
                foreach (var px in rows.GetRowSpan(y))
                {
                    bytes[i++] = px.R;
                    bytes[i++] = px.G;
                }
            }
        });
        return DepthMap.FromBytes(width, height, bytes);
    }
}
