namespace Nfty.App.Services;

/// <summary>
/// The Phase-1 stub notifier. Every Core-backed action calls Report; the shell surfaces the message
/// on the status line. Phase 2 replaces each caller's body with the real Core call.
/// </summary>
public interface INotYetWired
{
    string? Last { get; }
    event Action<string>? Reported;
    void Report(string action);
}

public sealed class NotYetWired : INotYetWired
{
    public string? Last { get; private set; }
    public event Action<string>? Reported;
    public void Report(string action) { Last = action; Reported?.Invoke(action); }
}
