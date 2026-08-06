namespace Nfty.App.Services;

/// <summary>
/// Plain status-line messages: guidance the app genuinely means, shown verbatim.
/// <para>
/// Deliberately separate from <see cref="INotYetWired"/>. That one exists to say "this button does
/// nothing yet", and the shell prefixes it with "Not wired yet:". Routing ordinary guidance through
/// it — e.g. telling the user to switch on editing before adding a recipe — makes a working feature
/// announce itself as unimplemented, which is what this interface exists to stop.
/// </para>
/// </summary>
/// <summary>
/// The status bar's guidance channel. Deliberately separate from <see cref="INotYetWired"/>:
/// that one is for actions that genuinely do nothing yet, and routing real guidance through it
/// told users a working feature was unbuilt.
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
