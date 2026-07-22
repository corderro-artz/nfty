using Nfty.App.ViewModels;

namespace Nfty.App.Services;

public interface INavigationService
{
    ViewModelBase? Current { get; }
    event Action? Changed;
    void To(ViewModelBase page);
    void Back();
}

public sealed class NavigationService : INavigationService
{
    private readonly Stack<ViewModelBase> _stack = new();
    public ViewModelBase? Current => _stack.Count > 0 ? _stack.Peek() : null;
    public event Action? Changed;

    public void To(ViewModelBase page) { _stack.Push(page); Changed?.Invoke(); }
    public void Back() { if (_stack.Count > 1) { _stack.Pop(); Changed?.Invoke(); } }
}
