using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
// SetTextAsync is an EXTENSION method in Avalonia 12 (ClipboardExtensions), not a member of
// IClipboard - the interface itself now speaks in IAsyncDataTransfer. Without this using the call
// below fails to compile with "IClipboard does not contain a definition for SetTextAsync".
using Avalonia.Input.Platform;
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
        // The clipboard hangs off a TopLevel, which is why this cannot live in Nfty.App: that
        // assembly is head-agnostic and has no window.
        var clipboard = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow is { } window
            ? TopLevel.GetTopLevel(window)?.Clipboard
            : null;

        if (clipboard is null) return;
        await clipboard.SetTextAsync(text);
    }
}
