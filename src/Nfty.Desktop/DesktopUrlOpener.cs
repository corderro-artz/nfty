using System.Diagnostics;
using Nfty.App.Services;

namespace Nfty.Desktop;

/// <summary>Opens an https address in the default browser. Best-effort: the Settings card also shows
/// the address to type, so a failure here must never take the app down.</summary>
public sealed class DesktopUrlOpener : IUrlOpener
{
    /// <inheritdoc />
    public void Open(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps) return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose(); }
        catch { /* best effort */ }
    }
}
