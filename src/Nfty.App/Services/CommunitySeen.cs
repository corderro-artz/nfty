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
}

/// <inheritdoc cref="ICommunitySeen"/>
public sealed class CommunitySeen : ICommunitySeen
{
    /// <summary>The file inside the community folder.</summary>
    public const string FileName = "seen.json";

    private sealed record Stored(DateTimeOffset? FirstVisit, Dictionary<string, DateTimeOffset>? Seen);

    private readonly string _path;
    private readonly DateTimeOffset _firstVisit;
    private readonly Dictionary<string, DateTimeOffset> _seen = new(StringComparer.Ordinal);

    /// <summary>Loads the file, or starts over (first visit now) when it is absent or unreadable.</summary>
    /// <param name="root">The <c>community</c> folder. Tests MUST pass a temp directory.</param>
    /// <param name="clock">The time source.</param>
    public CommunitySeen(string root, Func<DateTimeOffset>? clock = null)
    {
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
        if (_seen.TryGetValue(entry.Id, out var at)) return entry.Updated > at ? SeenMark.Updated : SeenMark.None;
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
        _seen[entry.Id] = entry.Updated;
        Save();
    }

    /// <inheritdoc />
    public void SeeAll(CommunityIndex index)
    {
        foreach (var e in index.Entries) _seen[e.Id] = e.Updated;
        Save();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, StateJson.Write(new Stored(_firstVisit, _seen)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* convenience state */ }
    }
}
