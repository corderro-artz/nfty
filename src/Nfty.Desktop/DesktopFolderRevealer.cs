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
            using var process = Process.Start(StartInfo(path));
        }
        catch { /* best effort — reveal must never crash the app */ }
    }

    /// <summary>Builds the platform opener request without starting a process.</summary>
    /// <param name="path">The path to pass as one argument.</param>
    /// <returns>The native launch request.</returns>
    internal static ProcessStartInfo StartInfo(string path)
    {
        var executable = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "explorer.exe"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "open" : "xdg-open";
        var request = new ProcessStartInfo(executable) { UseShellExecute = true };
        request.ArgumentList.Add(path);
        return request;
    }
}
