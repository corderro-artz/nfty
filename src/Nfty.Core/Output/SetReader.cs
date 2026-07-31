using System.IO.Compression;
using System.Text.Json;
using Nfty.Core.Formats;

namespace Nfty.Core.Output;

public record SetItem(int Number, string ImagePath, string Dna, string Recipe,
    IReadOnlyList<RarityAttribute> Rarity, IReadOnlyList<LayerColor> Layers);

/// <summary>A cooked Set read from disk for browsing: the manifest + per-item metadata and image
/// paths (images are NOT decoded here). If read from a .set archive, owns the extracted temp dir.</summary>
public sealed class LoadedSet : IDisposable
{
    public required SetManifest Manifest { get; init; }
    public required IReadOnlyList<SetItem> Items { get; init; }
    internal string? TempDir { get; init; }

    public void Dispose()
    {
        if (TempDir is not null && Directory.Exists(TempDir))
            try { Directory.Delete(TempDir, recursive: true); } catch { /* best effort */ }
    }
}

public static class SetReader
{
    public static LoadedSet Read(string path)
    {
        string dir = path;
        string? temp = null;
        if (File.Exists(path))   // a .set archive (or any file) → extract to a temp dir
        {
            temp = Directory.CreateTempSubdirectory("nfty-set-").FullName;
            ZipFile.ExtractToDirectory(path, temp);
            dir = temp;
        }

        try
        {
            string setJson = Path.Combine(dir, "set.json");
            if (!File.Exists(setJson))
                throw new FileNotFoundException($"Not a cooked Set — 'set.json' was not found in {path}.");

            var manifest = JsonSerializer.Deserialize<SetManifest>(File.ReadAllText(setJson), Json.Options)
                ?? throw new InvalidOperationException($"Could not read the Set manifest in {path}.");

            string nftyDir = Path.Combine(dir, "nfty");
            string imagesDir = Path.Combine(dir, "images");
            var items = new List<SetItem>();
            if (Directory.Exists(nftyDir))
            {
                foreach (var file in Directory.EnumerateFiles(nftyDir, "*.json")
                             .OrderBy(f => f, StringComparer.Ordinal))
                {
                    var m = JsonSerializer.Deserialize<NftyMetadata>(File.ReadAllText(file), Json.Options);
                    if (m is null) continue;
                    string stem = m.SetNumber.ToString("D4");
                    items.Add(new SetItem(m.SetNumber, Path.Combine(imagesDir, $"{stem}.png"),
                        m.Dna, m.Recipe, m.Rarity, m.Layers));
                }
            }

            return new LoadedSet { Manifest = manifest, Items = items, TempDir = temp };
        }
        catch
        {
            if (temp is not null) try { Directory.Delete(temp, recursive: true); } catch { }
            throw;
        }
    }

    public static Task<LoadedSet> ReadAsync(string path, CancellationToken ct = default) =>
        Task.Run(() => Read(path), ct);
}
