using Avalonia.Controls;
using Avalonia.Platform;

namespace Nfty.Desktop;

/// <summary>
/// The window icon where the OS cannot read it out of the executable. On Windows the shell takes the
/// <c>nfty.ico</c> PE resource (<c>ApplicationIcon</c>) with its hand-tuned 16 and 32 px frames, so
/// nothing is set there and Windows behavior is unchanged. X11 and Wayland have no such resource: a
/// window with no <see cref="Window.Icon"/> shows a generic one in the taskbar and the Alt-Tab list,
/// so every nfty window is given the same 256 px mark, drawn by <c>tools/icons/make-app-icon.py</c>
/// beside the <c>.ico</c>.
/// </summary>
internal static class AppIcon
{
    /// <summary>The embedded PNG, as an Avalonia resource URI.</summary>
    internal const string Uri = "avares://Nfty.Desktop/Assets/nfty.png";

    private static WindowIcon? _icon;

    /// <summary>Sets the icon on a window, off Windows only.</summary>
    /// <param name="window">The window.</param>
    public static void Apply(Window window)
    {
        if (OperatingSystem.IsWindows()) return;
        try
        {
            _icon ??= Load();
            window.Icon = _icon;
        }
        catch (Exception)
        {
            // An icon is decoration: a missing or unreadable resource leaves the generic one, never a
            // window that fails to open.
        }
    }

    /// <summary>Reads the embedded mark.</summary>
    /// <returns>The icon.</returns>
    internal static WindowIcon Load()
    {
        using var stream = AssetLoader.Open(new System.Uri(Uri));
        return new WindowIcon(stream);
    }
}
