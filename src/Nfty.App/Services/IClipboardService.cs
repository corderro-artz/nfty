namespace Nfty.App.Services;

/// <summary>Writing text to the system clipboard.
///
/// An interface rather than a direct Avalonia call so a ViewModel can be tested without a window —
/// the clipboard hangs off a TopLevel, which a headless unit test does not have.
/// </summary>
public interface IClipboardService
{
    /// <summary>Copies text to the system clipboard.</summary>
    /// <param name="text">The text to copy.</param>
    /// <returns>A task that completes once the clipboard has been written.</returns>
    Task SetTextAsync(string text);

    /// <summary>Copies a PNG image to the system clipboard. The default does nothing, so a head
    /// without image support still works.</summary>
    /// <param name="png">The PNG file bytes.</param>
    /// <returns>A task that completes once the clipboard has been written.</returns>
    Task SetImageAsync(byte[] png) => Task.CompletedTask;
}

/// <summary>Used when no real clipboard is reachable — the headless test host, and any future
/// surface without a TopLevel. Silently doing nothing is correct here: the alternative is throwing
/// from a Copy button in an environment where copying is meaningless.</summary>
public sealed class NoopClipboardService : IClipboardService
{
    /// <summary>The last text "copied", so a test can assert what would have been.</summary>
    public string? Last { get; private set; }
    /// <inheritdoc />
    public Task SetTextAsync(string text) { Last = text; return Task.CompletedTask; }
    /// <summary>The last image "copied", so a test can assert what would have been.</summary>
    public byte[]? LastImage { get; private set; }
    /// <inheritdoc />
    public Task SetImageAsync(byte[] png) { LastImage = png; return Task.CompletedTask; }
}
