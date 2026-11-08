using Nfty.App.ViewModels;

namespace Nfty.App.Services;

/// <summary>Shows and closes the modal dialog layer.</summary>
public interface IDialogService
{
    /// <summary>The dialog currently shown, or null.</summary>
    ViewModelBase? Active { get; }
    /// <summary>Raised when the active dialog changes.</summary>
    event Action? Changed;
    /// <summary>Open dialogs, top first, including suspended parents.</summary>
    IReadOnlyList<ViewModelBase> OpenDialogs => Active is { } active ? [active] : [];
    /// <summary>Pushes a child dialog and waits for its result; closing it restores its parent.</summary>
    /// <typeparam name="TResult">What the dialog closes with.</typeparam>
    /// <param name="dialog">The dialog's ViewModel.</param>
    /// <returns>The result, or default when dismissed. Every open frame retains its own answer.</returns>
    Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog);
    /// <summary>Closes the active dialog.</summary>
    /// <param name="result">What to return to the awaiting caller.</param>
    void Close(object? result);
    /// <summary>Closes one owned dialog without dismissing a newer child.</summary>
    /// <param name="dialog">The card whose caller has finished.</param>
    /// <param name="result">Its answer.</param>
    void Close(ViewModelBase dialog, object? result) => Close(result);
}

/// <inheritdoc cref="IDialogService"/>
public sealed class DialogService : IDialogService
{
    private sealed record Frame(ViewModelBase Dialog, TaskCompletionSource<object?> Answer);
    private readonly List<Frame> _frames = [];

    /// <inheritdoc />
    public ViewModelBase? Active => _frames.Count == 0 ? null : _frames[^1].Dialog;
    /// <inheritdoc />
    public IReadOnlyList<ViewModelBase> OpenDialogs => _frames.Select(f => f.Dialog).Reverse().ToArray();
    /// <inheritdoc />
    public event Action? Changed;

    /// <summary>Pushes a child card; closing it restores its parent and completes only its own answer.</summary>
    /// <typeparam name="TResult">The answer type.</typeparam>
    /// <param name="dialog">The new card; the same instance cannot be open twice.</param>
    /// <returns>The card's answer, or default on dismissal.</returns>
    public async Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        if (_frames.Any(f => ReferenceEquals(f.Dialog, dialog)))
            throw new InvalidOperationException("This dialog is already open.");
        var frame = new Frame(dialog, new(TaskCreationOptions.RunContinuationsAsynchronously));
        _frames.Add(frame);
        Changed?.Invoke();
        var result = await frame.Answer.Task;
        return result is TResult answer ? answer : default;
    }

    /// <inheritdoc />
    public void Close(object? result)
    {
        if (Active is { } active) Close(active, result);
    }

    /// <inheritdoc />
    public void Close(ViewModelBase dialog, object? result)
    {
        int index = _frames.FindLastIndex(f => ReferenceEquals(f.Dialog, dialog));
        if (index < 0) return;
        var frame = _frames[index];
        _frames.RemoveAt(index);
        // Complete the captured frame before publishing the transition: Changed may open another card.
        frame.Answer.TrySetResult(result);
        Changed?.Invoke();
    }
}

/// <summary>Dialog helpers every ViewModel shares.</summary>
public static class DialogServiceExtensions
{
    /// <summary>Shows the standard error card and waits for it to close.</summary>
    /// <param name="dialogs">The dialog layer.</param>
    /// <param name="title">The card's title.</param>
    /// <param name="message">What went wrong, in words a person can act on.</param>
    /// <returns>A task that completes when the card is dismissed.</returns>
    public static Task ShowErrorAsync(this IDialogService dialogs, string title, string message) =>
        dialogs.ShowAsync<object>(new ErrorDialogViewModel(dialogs, title, message));
}


/// <summary>A running card whose dismissal requests cancellation and waits for cleanup.</summary>
public interface ICancellableDialog
{
    /// <summary>Requests a stop and completes after the accepted run has unwound.</summary>
    /// <returns>The cleanup lifetime; cancellation alone does not dismiss the card.</returns>
    Task CancelAndWaitAsync();
}
