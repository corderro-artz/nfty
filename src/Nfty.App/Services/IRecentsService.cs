using System.Text.Json;
using Nfty.App.Models;

namespace Nfty.App.Services;

public interface IRecentsService
{
    IReadOnlyList<RecentItem> Items { get; }
    void Add(RecentItem item);
    void Remove(string path);
}

/// <summary>Most-recently-opened files, persisted as JSON under the user's app-data folder. Purely
/// convenience state: a corrupt store loads as empty and a failed save is swallowed, so recents can
/// never block or crash the app. The storage directory is injectable so tests never touch %APPDATA%.</summary>
public sealed class RecentsService : IRecentsService
{
    private const int Cap = 10;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    private readonly string _file;
    private readonly List<RecentItem> _items = new();

    public RecentsService(string? storageDir = null)
    {
        var dir = storageDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "nfty");
        _file = Path.Combine(dir, "recents.json");
        try
        {
            if (File.Exists(_file))
                _items = JsonSerializer.Deserialize<List<RecentItem>>(File.ReadAllText(_file), Json) ?? new();
        }
        catch { _items = new(); }   // corrupt/unreadable → start empty, never throw
    }

    public IReadOnlyList<RecentItem> Items => _items;

    public void Add(RecentItem item)
    {
        var full = Path.GetFullPath(item.Path);
        var entry = item with { Path = full };
        _items.RemoveAll(i => string.Equals(i.Path, full, StringComparison.Ordinal));
        _items.Insert(0, entry);
        if (_items.Count > Cap) _items.RemoveRange(Cap, _items.Count - Cap);
        Save();
    }

    public void Remove(string path)
    {
        _items.RemoveAll(i => string.Equals(i.Path, path, StringComparison.Ordinal));
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
