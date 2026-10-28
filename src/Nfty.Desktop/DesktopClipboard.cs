using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
// SetTextAsync is an EXTENSION method in Avalonia 12 (ClipboardExtensions), not a member of
// IClipboard - the interface itself now speaks in IAsyncDataTransfer. Without this using the call
// below fails to compile with "IClipboard does not contain a definition for SetTextAsync".
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Nfty.App.Services;

namespace Nfty.Desktop;

/// <summary>
/// The real system clipboard, reached through the desktop head's main window.
///
/// <para>Without this the desktop app resolved <see cref="NoopClipboardService"/> — the null object
/// meant for the headless test host — so the report dialog's Copy button was present, enabled, and
/// did nothing at all: no clipboard write, no error, no status message. A user pressed Copy and
/// believed their stats or inspect report had been copied.</para>
///
/// <para>Registered beside <see cref="DesktopFilePicker"/> and <see cref="DesktopFolderRevealer"/>,
/// the two other services whose real implementation only exists once there is a window.</para>
/// </summary>
public sealed class DesktopClipboard : IClipboardService
{
    /// <summary>Copies <paramref name="text"/> to the system clipboard.</summary>
    /// <param name="text">The text to copy.</param>
    /// <returns>A task that completes once the clipboard has been written, or immediately if no
    /// window is available to reach a clipboard through.</returns>
    public async Task SetTextAsync(string text)
    {
        if (CurrentClipboard() is not { } clipboard) return;
        await clipboard.SetTextAsync(text);
    }

    /// <summary>Copies a PNG image to the system clipboard as a bitmap, which the platform layer
    /// encodes for each OS's own image format.</summary>
    /// <param name="png">The PNG file bytes.</param>
    /// <returns>A task that completes once the clipboard has been written, or immediately if no
    /// window is available to reach a clipboard through.</returns>
    public async Task SetImageAsync(byte[] png)
    {
        if (CurrentClipboard() is not { } clipboard) return;
        using var stream = new MemoryStream(png);
        // The clipboard keeps the bitmap for pasting, so it is not disposed here.
        var bitmap = new Bitmap(stream);
        await clipboard.SetValueAsync(DataFormat.Bitmap, bitmap);
    }

    // The clipboard hangs off a TopLevel, which is why this cannot live in Nfty.App: that assembly is
    // head-agnostic and has no window. The window in front is preferred, then the main window, then
    // any: a viewer can outlive the main window, and its Copy still has to reach a clipboard.
    private static IClipboard? CurrentClipboard()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;
        var window = desktop.Windows.FirstOrDefault(w => w.IsActive) ?? desktop.MainWindow
                     ?? desktop.Windows.FirstOrDefault();
        return window is null ? null : TopLevel.GetTopLevel(window)?.Clipboard;
    }
}
