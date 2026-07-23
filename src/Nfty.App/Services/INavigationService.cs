using Nfty.App.ViewModels;

namespace Nfty.App.Services;

public interface INavigationService
{
    ViewModelBase? Current { get; }
    event Action? Changed;
    void To(ViewModelBase page);
    void Back();
}

public sealed class NavigationService : INavigationService, IDisposable
{
    private readonly Stack<ViewModelBase> _stack = new();
    public ViewModelBase? Current => _stack.Count > 0 ? _stack.Peek() : null;
    public event Action? Changed;

    public void To(ViewModelBase page) { _stack.Push(page); Changed?.Invoke(); }

    public void Back()
    {
        if (_stack.Count <= 1) return;
        var popped = _stack.Pop();
        (popped as IDisposable)?.Dispose();
        Changed?.Invoke();
    }

    public void Dispose()
    {
        while (_stack.Count > 0) (_stack.Pop() as IDisposable)?.Dispose();
    }
}
