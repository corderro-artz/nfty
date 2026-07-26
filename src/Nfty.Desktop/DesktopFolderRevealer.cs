using System.Diagnostics;
using System.Runtime.InteropServices;
using Nfty.App.Services;

namespace Nfty.Desktop;

public sealed class DesktopFolderRevealer : IFolderRevealer
{
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
