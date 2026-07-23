using Nfty.Core.Formats;

namespace Nfty.App.Services;

/// <summary>
/// Owns the currently-open CookBook. A <see cref="LoadedCookBook"/> holds every decoded variant image,
/// so this is the single place that frees them: <see cref="Open"/> disposes the previous book before
/// swapping. Registered as a singleton and disposed at shutdown. No ViewModel disposes the book.
/// </summary>
public interface ICookBookSession : IDisposable
{
    LoadedCookBook? Current { get; }
    event Action? Changed;
    void Open(LoadedCookBook book);
    void Close();
}

public sealed class CookBookSession : ICookBookSession
{
    private LoadedCookBook? _current;
    public LoadedCookBook? Current => _current;
    public event Action? Changed;

    public void Open(LoadedCookBook book)
    {
        if (ReferenceEquals(_current, book)) return;
        _current?.Dispose();
        _current = book;
        Changed?.Invoke();
    }

    public void Close()
    {
        if (_current is null) return;
        _current.Dispose();
        _current = null;
        Changed?.Invoke();
    }

    public void Dispose() => _current?.Dispose();
}
