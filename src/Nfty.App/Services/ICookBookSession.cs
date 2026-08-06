using Nfty.Core.Formats;

namespace Nfty.App.Services;

/// <summary>
/// Owns the currently-open CookBook. A <see cref="LoadedCookBook"/> holds every decoded variant image,
/// so this is the single place that frees them: <see cref="Open"/> disposes the previous book before
/// swapping. Registered as a singleton and disposed at shutdown. No ViewModel disposes the book.
/// </summary>
public interface ICookBookSession : IDisposable
{
    /// <summary>The open book, or null.</summary>
    LoadedCookBook? Current { get; }
    /// <summary>Where it was opened from, or null for an in-memory book.</summary>
    string? SourcePath { get; }
    /// <summary>Raised when the open book changes.</summary>
    event Action? Changed;
    /// <summary>Opens a book, disposing the previous one.</summary>
    /// <param name="book">The book to take ownership of.</param>
    /// <param name="sourcePath">Where it came from, if anywhere.</param>
    void Open(LoadedCookBook book, string? sourcePath = null);
    /// <summary>Swaps in a graph that SHARES the previous book's images, so the previous one must
    /// not be disposed. Used after an edit.</summary>
    /// <param name="book">The replacement graph.</param>
    void Replace(LoadedCookBook book);
    /// <summary>Closes the book and frees its images.</summary>
    void Close();
}

/// <inheritdoc cref="ICookBookSession"/>
public sealed class CookBookSession : ICookBookSession
{
    private LoadedCookBook? _current;
    private string? _sourcePath;
    /// <inheritdoc />
    public LoadedCookBook? Current => _current;
    /// <inheritdoc />
    public string? SourcePath => _sourcePath;
    /// <inheritdoc />
    public event Action? Changed;

    /// <inheritdoc />
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

    /// <inheritdoc />
    public void Close()
    {
        if (_current is null) return;
        _current.Dispose();
        _current = null;
        _sourcePath = null;
        Changed?.Invoke();
    }

    /// <summary>Disposes the open book at shutdown.</summary>
    public void Dispose() => _current?.Dispose();
}
