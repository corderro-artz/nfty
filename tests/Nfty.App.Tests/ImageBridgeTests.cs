using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using Nfty.App.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public class ImageBridgeTests
{
    [AvaloniaFact]
    public void ToBitmap_matches_source_size_and_pixels()
    {
        using var src = new Image<Rgba32>(2, 2);
        src[0, 0] = new Rgba32(10, 20, 30, 255);
        src[1, 0] = new Rgba32(40, 50, 60, 255);

        var bmp = new ImageBridge().ToBitmap(src);

        Assert.Equal(new PixelSize(2, 2), bmp.PixelSize);

        // Read back the top-left pixel through a locked framebuffer.
        var buffer = new byte[2 * 2 * 4];
        unsafe
        {
            fixed (byte* p = buffer)
                bmp.CopyPixels(new PixelRect(0, 0, 2, 2), (nint)p, buffer.Length, 2 * 4);
        }
        // Rgba8888 unpremultiplied: bytes are R,G,B,A in order.
        Assert.Equal(10, buffer[0]); Assert.Equal(20, buffer[1]); Assert.Equal(30, buffer[2]); Assert.Equal(255, buffer[3]);
        bmp.Dispose();
    }
}
