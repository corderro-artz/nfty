using System.Diagnostics;
using System.Runtime.InteropServices;
using Nfty.App.Services;

namespace Nfty.Desktop;

/// <summary>Opens a path in the platform's file manager. Best-effort by design: revealing a folder
/// is a convenience, and a failure here must never take the app down with it.</summary>
public sealed class DesktopFolderRevealer : IFolderRevealer
{
    /// <summary>Shows <paramref name="path"/> in Explorer, Finder or the XDG default.</summary>
    /// <param name="path">The file or folder to reveal.</param>
    public void Reveal(string path)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Process.Start(new ProcessStartInfo("open", $"\"{path}\"") { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo("xdg-open", $"\"{path}\"") { UseShellExecute = true });
        }
        catch { /* best effort — reveal must never crash the app */ }
    }
}
