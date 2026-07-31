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
    string? SourcePath { get; }
    event Action? Changed;
    void Open(LoadedCookBook book, string? sourcePath = null);
    void Replace(LoadedCookBook book);
    void Close();
}

public sealed class CookBookSession : ICookBookSession
{
    private LoadedCookBook? _current;
    private string? _sourcePath;
    public LoadedCookBook? Current => _current;
    public string? SourcePath => _sourcePath;
    public event Action? Changed;

    public void Open(LoadedCookBook book, string? sourcePath = null)
    {
        if (ReferenceEquals(_current, book)) { _sourcePath = sourcePath; return; }
        _current?.Dispose();
        _current = book;
        _sourcePath = sourcePath;
        Changed?.Invoke();
    }

    /// <summary>Swaps in a graph that shares the previous book's images (e.g. from
    /// CookBookEdits.UpsertIngredient) — so it must NOT dispose the previous book. The caller owns
    /// the lifetime of whatever images the new graph no longer references.</summary>
    public void Replace(LoadedCookBook book)
    {
        if (ReferenceEquals(_current, book)) return;
        _current = book;                 // deliberately no dispose
        Changed?.Invoke();
    }

    public void Close()
    {
        if (_current is null) return;
        _current.Dispose();
        _current = null;
        _sourcePath = null;
        Changed?.Invoke();
    }

    public void Dispose() => _current?.Dispose();
}
