using System.Linq;
using System.Text.Json;
using Nfty.App.Models;

namespace Nfty.App.Services;

/// <summary>The Landing screen's Recent list, persisted between sessions.</summary>
public interface IRecentsService
{
    /// <summary>The remembered entries, most recent first.</summary>
    IReadOnlyList<RecentItem> Items { get; }
    /// <summary>Records an opened item, moving it to the top if already present.</summary>
    /// <param name="item">What was opened.</param>
    void Add(RecentItem item);
    /// <summary>Forgets an entry — used when a remembered file has gone.</summary>
    /// <param name="path">The path to forget.</param>
    void Remove(string path);
}

/// <summary>Most-recently-opened files, persisted as JSON in the app's own <see cref="IStateStore"/>.
/// Purely convenience state: a corrupt store loads as empty and a failed save is swallowed, so
/// recents can never block or crash the app.
///
/// <para>It used to write <c>%APPDATA%/nfty/recents.json</c>, which contradicted the rule the store
/// exists to keep — this app is downloaded and run, not installed, so nothing it writes should leave
/// its own folder. The old list is read once and migrated so nobody loses their Landing screen.</para>
/// </summary>
/// <inheritdoc cref="IRecentsService"/>
public sealed class RecentsService : IRecentsService
{
    private const int Cap = 10;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    /// <summary>The store file this list lives in.</summary>
    public const string FileName = "recents.json";

    /// <summary>Where builds before the <c>.nfty</c> store kept the list. Read once and migrated,
    /// then left alone — never deleted, so a user who goes back to an older build still has it.
    ///
    /// <para>A property rather than a constructor default, so it can only ever be reached by a
    /// caller that names it: the composition root does, and no test does.</para></summary>
    public static string LegacyFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "nfty", FileName);

    private readonly IStateStore _store;
    private readonly List<RecentItem> _items;

    /// <summary>Creates the service, loading the list and migrating the pre-store one if this is the
    /// first run since the move.</summary>
    /// <param name="store">Where to persist the list.</param>
    /// <param name="legacyFile">The pre-store file to migrate from, or null for none. Null is the
    /// default so a test can never reach the developer's real app-data by omission — the composition
    /// root passes <see cref="LegacyFile"/> explicitly.</param>
    public RecentsService(IStateStore store, string? legacyFile = null)
    {
        _store = store;
        var stored = store.Read(FileName);
        _items = Parse(stored);

        // Migrate only when the store holds NO FILE AT ALL, not merely no entries. Keying off an
        // empty list would resurrect entries every launch for anyone who had cleared theirs, since
        // a cleared list and a never-written one look identical once loaded.
        if (stored is null && legacyFile is not null)
        {
            var legacy = Parse(ReadText(legacyFile));
            if (legacy.Count > 0)
            {
                _items = legacy;
                Save();   // the old file is deliberately left where it is
            }
        }
    }

    /// <summary>Creates a service persisting straight into <paramref name="storageDir"/>, with no
    /// discovery and no migration.</summary>
    /// <param name="storageDir">The folder to hold <c>recents.json</c>. Tests MUST pass a temp
    /// directory: this constructor exists so a test touches neither the real %APPDATA% nor the real
    /// <see cref="AppContext.BaseDirectory"/>.</param>
    public RecentsService(string storageDir) : this(StateStore.At(storageDir)) { }

    /// <inheritdoc />
    public IReadOnlyList<RecentItem> Items => _items;

    /// <inheritdoc />
    public void Add(RecentItem item)
    {
        var full = Path.GetFullPath(item.Path);
        var entry = item with { Path = full };
        _items.RemoveAll(i => string.Equals(i.Path, full, StringComparison.Ordinal));
        _items.Insert(0, entry);
        if (_items.Count > Cap) _items.RemoveRange(Cap, _items.Count - Cap);
        Save();
    }

    /// <inheritdoc />
    public void Remove(string path)
    {
        // Add stores full paths, so normalize here too or a raw/relative path silently no-ops.
        string full;
        try { full = Path.GetFullPath(path); } catch { full = path; }
        _items.RemoveAll(i => string.Equals(i.Path, full, StringComparison.Ordinal));
        Save();
    }

    private static List<RecentItem> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            // Well-formed JSON of the wrong shape ([null], [{}]) deserializes "successfully" —
            // drop anything without a usable path so a later Add can't NRE on it.
            return (JsonSerializer.Deserialize<List<RecentItem?>>(json, Json) ?? new())
                .Where(i => i is not null && !string.IsNullOrWhiteSpace(i.Path))
                .Select(i => i!)
                .ToList();
        }
        catch { return new(); }   // corrupt → start empty, never throw
    }

    private static string? ReadText(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : null; }
        catch { return null; }
    }

    private void Save() => _store.Write(FileName, JsonSerializer.Serialize(_items, Json));
}
