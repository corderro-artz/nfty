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

    /// <summary>Whether the page is writing something right now (a save, an export). Closing the
    /// window over such a page loses the write even when no work is unsaved.</summary>
    bool IsBusy { get; }

    /// <summary>Asks (save / discard / stay) when there is unsaved work.</summary>
    /// <returns>True when the page may go.</returns>
    Task<bool> ConfirmLeaveAsync();
}
