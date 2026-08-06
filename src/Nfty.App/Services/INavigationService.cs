using Nfty.App.ViewModels;

namespace Nfty.App.Services;

/// <summary>
/// The page stack. Landing is the root; opening a CookBook or a Set pushes on top of it, and
/// closing one pops back — which is also what disposes the page that held the images.
/// </summary>
public interface INavigationService
{
    /// <summary>The page on top, or null before the first navigation.</summary>
    ViewModelBase? Current { get; }
    /// <summary>Raised whenever the top of the stack changes.</summary>
    event Action? Changed;
    /// <summary>Pushes a page.</summary>
    /// <param name="page">The page to show.</param>
    void To(ViewModelBase page);
    /// <summary>Pops the top page and disposes it, if it is disposable. The root is never popped,
    /// so this is a no-op on Landing.</summary>
    void Back();
}

/// <inheritdoc cref="INavigationService"/>
public sealed class NavigationService : INavigationService, IDisposable
{
    private readonly Stack<ViewModelBase> _stack = new();
    /// <inheritdoc />
    public ViewModelBase? Current => _stack.Count > 0 ? _stack.Peek() : null;
    /// <inheritdoc />
    public event Action? Changed;

    /// <inheritdoc />
    public void To(ViewModelBase page) { _stack.Push(page); Changed?.Invoke(); }

    /// <inheritdoc />
    public void Back()
    {
        if (_stack.Count <= 1) return;
        var popped = _stack.Pop();
        (popped as IDisposable)?.Dispose();
        Changed?.Invoke();
    }

    /// <summary>Pops and disposes every page at shutdown.</summary>
    public void Dispose()
    {
        while (_stack.Count > 0) (_stack.Pop() as IDisposable)?.Dispose();
    }
}
