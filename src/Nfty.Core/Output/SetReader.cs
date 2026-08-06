using System.IO.Compression;
using System.Text.Json;
using Nfty.Core.Formats;

namespace Nfty.Core.Output;

/// <summary>One asset in a Set already on disk.</summary>
/// <param name="Number">Its set number.</param>
/// <param name="ImagePath">Path to its PNG. A path, not a decoded image, so listing a Set does
/// not pull the whole collection into memory.</param>
/// <param name="Dna">Its identity hash.</param>
/// <param name="Recipe">The recipe it came from.</param>
/// <param name="Rarity">Its traits with collection-wide rarity.</param>
/// <param name="Layers">The per-layer colour record.</param>
public record SetItem(int Number, string ImagePath, string Dna, string Recipe,
    IReadOnlyList<RarityAttribute> Rarity, IReadOnlyList<LayerColor> Layers);

/// <summary>A cooked Set read from disk for browsing: the manifest + per-item metadata and image
/// paths (images are NOT decoded here). If read from a .set archive, owns the extracted temp dir.</summary>
public sealed class LoadedSet : IDisposable
{
    /// <summary>The Set's manifest.</summary>
    public required SetManifest Manifest { get; init; }
    /// <summary>Its assets, as metadata plus image paths.</summary>
    public required IReadOnlyList<SetItem> Items { get; init; }
    internal string? TempDir { get; init; }

    /// <summary>Releases anything the reader holds. A Set is read as paths, so this frees the
    /// temporary extraction directory when the Set came from a packed <c>.set</c>.</summary>
    public void Dispose()
    {
        if (TempDir is not null && Directory.Exists(TempDir))
            try { Directory.Delete(TempDir, recursive: true); } catch { /* best effort */ }
    }
}

/// <summary>Opens a cooked Set — a folder, or a packed <c>.set</c> archive.</summary>
public static class SetReader
{
    /// <summary>Reads a Set.</summary>
    /// <param name="path">A Set folder, or a <c>.set</c> archive.</param>
    /// <returns>The manifest and items; the caller owns it and must dispose it.</returns>
    /// <exception cref="CorruptSetException">The Set is missing or malformed.</exception>
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

    /// <summary>Reads a Set off the calling thread. Extracting and parsing is I/O-bound but the
    /// underlying API is synchronous, so this is a <see cref="Task.Run(Action)"/> over
    /// <see cref="Read"/> rather than genuine async — it exists to keep a UI thread free.</summary>
    /// <param name="path">A Set folder, or a <c>.set</c> archive.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <returns>The manifest and items; the caller owns it and must dispose it.</returns>
    public static Task<LoadedSet> ReadAsync(string path, CancellationToken ct = default) =>
        Task.Run(() => Read(path), ct);
}
