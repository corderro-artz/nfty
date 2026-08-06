using Nfty.App.ViewModels;

namespace Nfty.App.Services;

/// <summary>Shows and closes the modal dialog layer.</summary>
public interface IDialogService
{
    /// <summary>The dialog currently shown, or null.</summary>
    ViewModelBase? Active { get; }
    /// <summary>Raised when the active dialog changes.</summary>
    event Action? Changed;
    /// <summary>Shows a dialog and waits for its result.</summary>
    /// <typeparam name="TResult">What the dialog closes with.</typeparam>
    /// <param name="dialog">The dialog's ViewModel.</param>
    /// <returns>The result, or default when dismissed.</returns>
    Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog);
    /// <summary>Closes the active dialog.</summary>
    /// <param name="result">What to return to the awaiting caller.</param>
    void Close(object? result);
}

/// <inheritdoc cref="IDialogService"/>
public sealed class DialogService : IDialogService
{
    private ViewModelBase? _active;
    private TaskCompletionSource<object?>? _tcs;

    /// <inheritdoc />
    public ViewModelBase? Active => _active;
    /// <inheritdoc />
    public event Action? Changed;

    /// <inheritdoc />
    public Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog)
    {
        _active = dialog;
        _tcs = new TaskCompletionSource<object?>();
        Changed?.Invoke();
        return _tcs.Task.ContinueWith(t => (TResult?)(t.Result is TResult r ? r : default));
    }

    /// <inheritdoc />
    public void Close(object? result)
    {
        _active = null;
        Changed?.Invoke();
        _tcs?.TrySetResult(result);
        _tcs = null;
    }
}
