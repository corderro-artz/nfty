using System;
using System.IO;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The window icon X11 and Wayland show: an embedded PNG, since only Windows reads the <c>.ico</c>
/// out of the executable. A resource URI that resolves to nothing fails silently at run time (the
/// window just shows a generic icon), so the resource is asserted here, decoded, at its drawn size.
/// </summary>
public class LinuxWindowIconTests
{
    private const string Uri = "avares://Nfty.Desktop/Assets/nfty.png";

    [AvaloniaFact]
    public void The_window_icon_resource_is_embedded_and_decodes_at_256()
    {
        var uri = new Uri(Uri);
        Assert.True(AssetLoader.Exists(uri));
        using var stream = AssetLoader.Open(uri);
        using var bitmap = new Bitmap(stream);
        Assert.Equal(256, bitmap.PixelSize.Width);
        Assert.Equal(256, bitmap.PixelSize.Height);
    }

    [Fact]
    public void The_embedded_png_is_the_one_the_icon_script_writes()
    {
        // AppIcon.cs names the resource by this path; the script writes it; the tarball ships it.
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "nfty.sln")))
            root = Path.GetDirectoryName(root);
        Assert.NotNull(root);
        Assert.True(File.Exists(Path.Combine(root, "src", "Nfty.Desktop", "Assets", "nfty.png")));
        Assert.Contains("Assets', 'nfty.png'",
            File.ReadAllText(Path.Combine(root, "tools", "icons", "make-app-icon.py")), StringComparison.Ordinal);
        Assert.Contains(Uri, File.ReadAllText(Path.Combine(root, "src", "Nfty.Desktop", "AppIcon.cs")), StringComparison.Ordinal);
    }
}
