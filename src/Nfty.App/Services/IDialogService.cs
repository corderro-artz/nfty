using Nfty.App.ViewModels;

namespace Nfty.App.Services;

public interface IDialogService
{
    ViewModelBase? Active { get; }
    event Action? Changed;
    Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog);
    void Close(object? result);
}

public sealed class DialogService : IDialogService
{
    private ViewModelBase? _active;
    private TaskCompletionSource<object?>? _tcs;

    public ViewModelBase? Active => _active;
    public event Action? Changed;

    public Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog)
    {
        _active = dialog;
        _tcs = new TaskCompletionSource<object?>();
        Changed?.Invoke();
        return _tcs.Task.ContinueWith(t => (TResult?)(t.Result is TResult r ? r : default));
    }

    public void Close(object? result)
    {
        _active = null;
        Changed?.Invoke();
        _tcs?.TrySetResult(result);
        _tcs = null;
    }
}
