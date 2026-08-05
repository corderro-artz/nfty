namespace Nfty.App.Services;

/// <summary>Writing text to the system clipboard.
///
/// An interface rather than a direct Avalonia call so a ViewModel can be tested without a window —
/// the clipboard hangs off a TopLevel, which a headless unit test does not have.
/// </summary>
public interface IClipboardService
{
    Task SetTextAsync(string text);
}

/// <summary>Used when no real clipboard is reachable — the headless test host, and any future
/// surface without a TopLevel. Silently doing nothing is correct here: the alternative is throwing
/// from a Copy button in an environment where copying is meaningless.</summary>
public sealed class NoopClipboardService : IClipboardService
{
    public string? Last { get; private set; }
    public Task SetTextAsync(string text) { Last = text; return Task.CompletedTask; }
}
