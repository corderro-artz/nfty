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
    /// <summary>Whether an authoring operation is borrowing and replacing the open graph.</summary>
    bool IsWriting { get; }
    /// <summary>Begins an exclusive authoring operation, before reading its graph snapshot.</summary>
    /// <returns>A lease to release after adoption and refresh, or null when another writer owns it.</returns>
    IDisposable? TryBeginWrite();
    /// <summary>Waits for the current authoring operation to release its graph.</summary>
    /// <returns>The current writer's completion, or an already completed task.</returns>
    Task WaitForWriteAsync();
    /// <summary>Opens a book, disposing the previous one; refuses while a write lease is active.</summary>
    /// <param name="book">The book to take ownership of.</param>
    /// <param name="sourcePath">Where it came from, if anywhere.</param>
    void Open(LoadedCookBook book, string? sourcePath = null);
    /// <summary>Swaps in a graph that SHARES the previous book's images, so the previous one must
    /// not be disposed. Used after an edit.</summary>
    /// <param name="book">The replacement graph.</param>
    void Replace(LoadedCookBook book);
    /// <summary>Closes the book and frees its images; refuses while a write lease is active.</summary>
    void Close();
}

/// <inheritdoc cref="ICookBookSession"/>
public sealed class CookBookSession : ICookBookSession
{
    private readonly CookBookWriteState _writes = new();
    private LoadedCookBook? _current;
    private string? _sourcePath;
    /// <inheritdoc />
    public LoadedCookBook? Current => _current;
    /// <inheritdoc />
    public string? SourcePath => _sourcePath;
    /// <inheritdoc />
    public event Action? Changed;
    /// <inheritdoc />
    public bool IsWriting => _writes.IsWriting;
    /// <inheritdoc />
    public IDisposable? TryBeginWrite() => _writes.TryBegin();
    /// <inheritdoc />
    public Task WaitForWriteAsync() => _writes.WaitAsync();

    /// <inheritdoc />
    public void Open(LoadedCookBook book, string? sourcePath = null)
    {
        RefuseWhileWriting();
        if (ReferenceEquals(_current, book)) { _sourcePath = sourcePath; return; }
        _current?.Dispose();
        _current = book;
        _sourcePath = sourcePath;
        Changed?.Invoke();
    }

    /// <summary>Swaps in a graph that shares the previous book's images (e.g. from
    /// CookBookEdits.UpsertIngredient) — so it must NOT dispose the previous book. The caller owns
    /// the lifetime of whatever images the new graph no longer references.</summary>
    /// <param name="book">The adopted graph; the caller releases only images absent from this graph.</param>
    public void Replace(LoadedCookBook book)
    {
        if (ReferenceEquals(_current, book)) return;
        _current = book;                 // deliberately no dispose
        Changed?.Invoke();
    }

    /// <inheritdoc />
    public void Close()
    {
        RefuseWhileWriting();
        if (_current is null) return;
        _current.Dispose();
        _current = null;
        _sourcePath = null;
        Changed?.Invoke();
    }

    /// <summary>Disposes the open book at shutdown; the host must first await any active writer.</summary>
    public void Dispose() { RefuseWhileWriting(); _current?.Dispose(); }

    private void RefuseWhileWriting()
    {
        if (IsWriting) throw new InvalidOperationException("Wait for the book's current save before closing it.");
    }
}

// Busy state deliberately does not raise Changed: its subscribers rebuild graph projections.
internal sealed class CookBookWriteState
{
    private TaskCompletionSource? _finished;
    internal bool IsWriting { get { lock (this) return _finished is not null; } }
    internal IDisposable? TryBegin()
    {
        lock (this)
        {
            if (_finished is not null) return null;
            _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return new Lease(this);
        }
    }
    internal Task WaitAsync() { lock (this) return _finished?.Task ?? Task.CompletedTask; }
    private sealed class Lease(CookBookWriteState owner) : IDisposable
    {
        private CookBookWriteState? _owner = owner;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _owner, null) is not { } state) return;
            TaskCompletionSource? finished;
            lock (state) { finished = state._finished; state._finished = null; }
            finished?.SetResult();
        }
    }
}
