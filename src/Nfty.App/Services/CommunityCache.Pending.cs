using System.Text.Json;
using Nfty.Core.Formats;

namespace Nfty.App.Services;

public sealed partial class CommunityCache
{
    private IReadOnlyList<PendingRecord>? _pending;

    private string PendingPath => Path.Combine(_root, "pending.json");

    private sealed record PendingFile(int SchemaVersion, List<PendingRecord> Pending);

    /// <inheritdoc />
    public IReadOnlyList<PendingRecord> Pending => _pending ??= LoadPending();

    private List<PendingRecord> LoadPending()
    {
        try
        {
            if (!File.Exists(PendingPath)) return [];
            var file = JsonSerializer.Deserialize<PendingFile>(File.ReadAllBytes(PendingPath), Json.Options);
            // Anything that does not read as a record this build wrote is dropped: a pending list is a convenience, and a
            // half-understood one would show the wrong tile.
            return file is { SchemaVersion: 1 } ? file.Pending.Where(p => p is { Slug.Length: > 0, ClosedReasons: not null }).ToList() : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        { return []; }
    }

    /// <inheritdoc />
    public void SavePending(IReadOnlyList<PendingRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        _pending = records.ToList();
        try
        {
            if (records.Count == 0)
            {
                if (File.Exists(PendingPath)) File.Delete(PendingPath);
                return;
            }
            Directory.CreateDirectory(_root);
            WriteAtomic(PendingPath, JsonSerializer.SerializeToUtf8Bytes(new PendingFile(1, records.ToList()), Json.Options));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The list stays in memory for this session; a share is not undone because its note could not be written.
        }
    }
}
