namespace Nfty.App.Services;

/// <summary>
/// A page that can hold work only the page itself has. Closing the window must ask every page on
/// the stack that does, not only the one on top: a dirty editor under a Rise page is as lost as one
/// in front.
/// </summary>
public interface IUnsavedWork
{
    /// <summary>Whether leaving this page now would lose something.</summary>
    bool HasUnsavedWork { get; }

    /// <summary>Asks (save / discard / stay) when there is unsaved work.</summary>
    /// <returns>True when the page may go.</returns>
    Task<bool> ConfirmLeaveAsync();
}
