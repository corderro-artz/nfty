namespace Nfty.App.Services;

/// <summary>
/// The Phase-1 stub notifier. Every Core-backed action calls Report; the shell surfaces the message
/// on the status line. Phase 2 replaces each caller's body with the real Core call.
/// </summary>
public interface INotYetWired
{
    /// <summary>The most recent report, for tests.</summary>
    string? Last { get; }
    /// <summary>Raised when an unbuilt action is invoked, so the shell can say so in the status bar.</summary>
    event Action<string>? Reported;
    /// <summary>Reports that an unbuilt action was invoked.</summary>
    /// <param name="action">What the user tried to do.</param>
    void Report(string action);
}

/// <inheritdoc cref="INotYetWired"/>
public sealed class NotYetWired : INotYetWired
{
    /// <summary>The most recent report, for tests.</summary>
    public string? Last { get; private set; }
    /// <inheritdoc />
    public event Action<string>? Reported;
    /// <inheritdoc />
    public void Report(string action) { Last = action; Reported?.Invoke(action); }
}
