namespace Nfty.App.Services;

/// <summary>
/// The status bar's guidance channel: what just happened, or what to do next, shown verbatim.
/// <para>
/// Verbatim is the whole point. There was once a second channel that prefixed every message with
/// "Not wired yet:", and gated-but-working features spoke through it — so telling a user to switch
/// editing on before adding a recipe announced the feature as unimplemented. That channel is gone;
/// this one says only what the app means.
/// </para>
/// </summary>
public interface IStatusService
{
    /// <summary>The most recent message, for tests.</summary>
    string? Last { get; }
    /// <summary>Raised when there is something to show the user.</summary>
    event Action<string>? Said;
    /// <summary>Shows a message in the status bar.</summary>
    /// <param name="message">What to say.</param>
    void Say(string message);
}

/// <inheritdoc cref="IStatusService"/>
public sealed class StatusService : IStatusService
{
    /// <inheritdoc />
    public string? Last { get; private set; }
    /// <inheritdoc />
    public event Action<string>? Said;
    /// <inheritdoc />
    public void Say(string message) { Last = message; Said?.Invoke(message); }
}
