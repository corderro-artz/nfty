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

/// <summary>Most-recently-opened files, persisted as JSON under the user's app-data folder. Purely
/// convenience state: a corrupt store loads as empty and a failed save is swallowed, so recents can
/// never block or crash the app. The storage directory is injectable so tests never touch %APPDATA%.</summary>
/// <inheritdoc cref="IRecentsService"/>
public sealed class RecentsService : IRecentsService
{
    private const int Cap = 10;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    private readonly string _file;
    private readonly List<RecentItem> _items = new();

    /// <summary>Creates the service.</summary>
    /// <param name="storageDir">Where to persist the list. Tests MUST pass a temp directory — the
    /// default is the real per-user application-data folder, and a test that wrote there would
    /// alter the developer's own Recent list.</param>
    public RecentsService(string? storageDir = null)
    {
        var dir = storageDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "nfty");
        _file = Path.Combine(dir, "recents.json");
        try
        {
            if (File.Exists(_file))
                // Well-formed JSON of the wrong shape ([null], [{}]) deserialises "successfully" —
                // drop anything without a usable path so a later Add can't NRE on it.
                _items = (JsonSerializer.Deserialize<List<RecentItem?>>(File.ReadAllText(_file), Json) ?? new())
                    .Where(i => i is not null && !string.IsNullOrWhiteSpace(i.Path))
                    .Select(i => i!)
                    .ToList();
        }
        catch { _items = new(); }   // corrupt/unreadable → start empty, never throw
    }

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
        // Add stores full paths, so normalise here too or a raw/relative path silently no-ops.
        string full;
        try { full = Path.GetFullPath(path); } catch { full = path; }
        _items.RemoveAll(i => string.Equals(i.Path, full, StringComparison.Ordinal));
        Save();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, JsonSerializer.Serialize(_items, Json));
        }
        catch { /* convenience state — never surface */ }
    }
}
