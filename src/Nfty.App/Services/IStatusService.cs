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
public interface IStatusService
{
    string? Last { get; }
    event Action<string>? Said;
    void Say(string message);
}

public sealed class StatusService : IStatusService
{
    public string? Last { get; private set; }
    public event Action<string>? Said;
    public void Say(string message) { Last = message; Said?.Invoke(message); }
}
