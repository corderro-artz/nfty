using Nfty.Core.Publish.Community;

namespace Nfty.App.Services;

/// <summary>What a gallery entry shows next to its name.</summary>
public enum SeenMark
{
    /// <summary>Nothing.</summary>
    None,
    /// <summary>Shared since the reader first visited, and not yet looked at.</summary>
    New,
    /// <summary>Looked at before, and changed since.</summary>
    Updated,
}

/// <summary>Remembers which gallery entries the reader has looked at, so new and changed ones can be marked.</summary>
public interface ICommunitySeen
{
    /// <summary>The mark for one entry.</summary>
    /// <param name="entry">The entry.</param>
    SeenMark MarkOf(CommunityEntry entry);

    /// <summary>How many entries are marked new and updated.</summary>
    /// <param name="index">The list.</param>
    (int New, int Updated) Count(CommunityIndex index);

    /// <summary>Marks one entry seen as it is now.</summary>
    /// <param name="entry">The entry.</param>
    void See(CommunityEntry entry);

    /// <summary>Marks every entry seen as it is now.</summary>
    /// <param name="index">The list.</param>
    void SeeAll(CommunityIndex index);

    /// <summary>Writes any marks still waiting for the debounce, so closing the page never loses one.</summary>
    void Flush();
}

/// <inheritdoc cref="ICommunitySeen"/>
public sealed class CommunitySeen : ICommunitySeen
{
    /// <summary>How long after the last mark the file is written, in milliseconds.</summary>
    public const int SaveDelayMs = 1000;

    /// <summary>How many times the file has been written, so a test can tell one write from many.</summary>
    internal int SaveCount { get; private set; }

    /// <summary>The write the debounce is waiting to make, so a test can settle it.</summary>
    internal Task PendingSave { get; private set; } = Task.CompletedTask;

    /// <summary>The file inside the community folder.</summary>
    public const string FileName = "seen.json";

    private sealed record Stored(DateTimeOffset? FirstVisit, Dictionary<string, DateTimeOffset>? Seen);

    private readonly string _path;
    private readonly DateTimeOffset _firstVisit;
    private readonly Dictionary<string, DateTimeOffset> _seen = new(StringComparer.Ordinal);
    private readonly Func<CancellationToken, Task> _delay;
    private readonly object _gate = new();
    private int _generation;
    private bool _dirty;

    /// <summary>Loads the file, or starts over (first visit now) when it is absent or unreadable.</summary>
    /// <param name="root">The <c>community</c> folder. Tests MUST pass a temp directory.</param>
    /// <param name="clock">The time source.</param>
    /// <param name="delay">The quiet time a change waits for before it is written; one second when null. Tests pass their own.</param>
    public CommunitySeen(string root, Func<DateTimeOffset>? clock = null, Func<CancellationToken, Task>? delay = null)
    {
        _delay = delay ?? (ct => Task.Delay(SaveDelayMs, ct));
        _path = Path.Combine(root, FileName);
        string? text = null;
        try { if (File.Exists(_path)) text = File.ReadAllText(_path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

        if (StateJson.TryRead<Stored>(text) is { FirstVisit: { } first, Seen: { } seen })
        {
            _firstVisit = first;
            foreach (var (id, at) in seen) _seen[id] = at;
        }
        else
        {
            _firstVisit = (clock ?? (() => DateTimeOffset.UtcNow))();
            Save();
        }
    }

    /// <inheritdoc />
    public SeenMark MarkOf(CommunityEntry entry)
    {
        DateTimeOffset at;
        bool known;
        lock (_gate) known = _seen.TryGetValue(entry.Id, out at);
        if (known) return entry.Updated > at ? SeenMark.Updated : SeenMark.None;
        return entry.Created > _firstVisit ? SeenMark.New : SeenMark.None;
    }

    /// <inheritdoc />
    public (int New, int Updated) Count(CommunityIndex index)
    {
        int n = 0, u = 0;
        foreach (var e in index.Entries)
            switch (MarkOf(e)) { case SeenMark.New: n++; break; case SeenMark.Updated: u++; break; }
        return (n, u);
    }

    /// <inheritdoc />
    public void See(CommunityEntry entry)
    {
        lock (_gate) _seen[entry.Id] = entry.Updated;
        Schedule();
    }

    /// <inheritdoc />
    public void SeeAll(CommunityIndex index)
    {
        lock (_gate) foreach (var e in index.Entries) _seen[e.Id] = e.Updated;
        Schedule();
    }

    /// <inheritdoc />
    public void Flush()
    {
        lock (_gate)
        {
            _generation++;   // whatever the debounce had waiting is superseded by this write
            if (_dirty) Save();
        }
    }

    // A mark is kept in memory at once and written once the reader has stopped selecting for a moment: a
    // keyboard walk through the grid selects a tile per key press, and each write was a synchronous file
    // write on the UI thread. Trailing debounce: every mark restarts the wait.
    private void Schedule()
    {
        int mine;
        lock (_gate)
        {
            _dirty = true;
            mine = ++_generation;
        }
        PendingSave = Task.Run(async () =>
        {
            try { await _delay(CancellationToken.None).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            lock (_gate)
            {
                if (mine == _generation && _dirty) Save();
            }
        });
    }

    private void Save()
    {
        _dirty = false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, StateJson.Write(new Stored(_firstVisit, _seen)));
            SaveCount++;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* convenience state */ }
    }
}
