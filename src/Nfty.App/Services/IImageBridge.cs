using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.App.Services;

/// <summary>Converts an ImageSharp image into an Avalonia <see cref="Bitmap"/>. The returned bitmap
/// owns an independent pixel copy, so the caller disposes the source image immediately after.</summary>
public interface IImageBridge
{
    /// <summary>Converts an ImageSharp frame to an Avalonia bitmap.</summary>
    /// <param name="image">The source frame. Not disposed.</param>
    /// <returns>A new bitmap; the caller owns it.</returns>
    Bitmap ToBitmap(Image<Rgba32> image);
}

/// <inheritdoc cref="IImageBridge"/>
public sealed class ImageBridge : IImageBridge
{
    /// <inheritdoc />
    public Bitmap ToBitmap(Image<Rgba32> image)
    {
        var wb = new WriteableBitmap(
            new PixelSize(image.Width, image.Height),
            new Vector(96, 96),
            PixelFormat.Rgba8888,          // ImageSharp Rgba32 is byte order R,G,B,A — a 1:1 match.
            AlphaFormat.Unpremul);

        using (var fb = wb.Lock())
        {
            int bytes = image.Width * image.Height * 4;
            unsafe
            {
                var span = new Span<byte>((void*)fb.Address, bytes);
                image.CopyPixelDataTo(span);
            }
        }
        return wb;
    }
}
