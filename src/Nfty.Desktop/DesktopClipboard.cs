// SetTextAsync is an EXTENSION method in Avalonia 12 (ClipboardExtensions), not a member of
// IClipboard - the interface itself now speaks in IAsyncDataTransfer. Without this using the call
// below fails to compile with "IClipboard does not contain a definition for SetTextAsync".
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Nfty.App.Services;

namespace Nfty.Desktop;

/// <summary>
/// The system clipboard reached through a live desktop window. A copied image stays alive while
/// the clipboard owns its transfer, then releases its native bitmap when the platform releases it.
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
        var bitmap = new Bitmap(stream);
        var transfer = new BitmapTransfer(bitmap);
        try { await clipboard.SetDataAsync(transfer); }
        catch
        {
            transfer.Dispose();
            throw;
        }
    }

    // Avalonia's DataTransfer.Dispose is intentionally empty: it does not own the values placed in
    // it. The clipboard DOES dispose its transfer, so this wrapper carries bitmap ownership there.
    private sealed class BitmapTransfer : IAsyncDataTransfer
    {
        private readonly Bitmap _bitmap;
        private readonly DataTransfer _data = new();
        private bool _disposed;

        internal BitmapTransfer(Bitmap bitmap)
        {
            _bitmap = bitmap;
            _data.Add(DataTransferItem.Create(DataFormat.Bitmap, bitmap));
        }

        public IReadOnlyList<DataFormat> Formats => _data.Formats;
        public IReadOnlyList<IAsyncDataTransferItem> Items => _data.Items;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _bitmap.Dispose();
        }
    }

    // The clipboard hangs off a TopLevel, which is why this cannot live in Nfty.App: that assembly is
    // head-agnostic and has no window. The window in front is preferred, then the main window, then
    // any: a viewer can outlive the main window, and its Copy still has to reach a clipboard.
    private static IClipboard? CurrentClipboard() => DesktopWindowContext.Current?.Clipboard;
}
