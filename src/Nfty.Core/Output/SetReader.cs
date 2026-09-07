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
/// <param name="Layers">The per-layer color record.</param>
/// <param name="AbsentLayers">The layers the roll left out of this asset entirely, by display
/// name. Null for a Set written before optional layers existed, and for any collection that does
/// not use them.</param>
public record SetItem(int Number, string ImagePath, string Dna, string Recipe,
    IReadOnlyList<RarityAttribute> Rarity, IReadOnlyList<LayerColor> Layers,
    IReadOnlyList<string>? AbsentLayers = null);

/// <summary>A cooked Set read from disk for browsing: the manifest + per-item metadata and image
/// paths (images are NOT decoded here). If read from a .set archive, owns the extracted temp dir.</summary>
/// <remarks>
/// Not sealed, so <c>Publish.SealedSet</c> can BE one while also carrying the seal it came out of.
/// A sealed Set is a loaded Set with provenance, and every screen that shows one has to show what it
/// is permitted to do with it — a policy handed back beside the data, for the caller to remember to
/// carry, is a policy that eventually arrives nowhere. There is no protected state and
/// <see cref="Dispose"/> frees only what this type owns, so subclassing costs nothing.
/// </remarks>
public class LoadedSet : IDisposable
{
    /// <summary>The Set's manifest.</summary>
    public required SetManifest Manifest { get; init; }
    /// <summary>Its assets, as metadata plus image paths.</summary>
    public required IReadOnlyList<SetItem> Items { get; init; }

    /// <summary>
    /// The folder this Set's files are actually in — the folder it was opened from, or the
    /// temporary directory a <c>.set</c> was unpacked into.
    /// </summary>
    /// <remarks>
    /// So a caller that already has the Set open does not have to unpack it a second time to do
    /// something with its files. The export dialog recomputes what it is about to ship on every
    /// checkbox, and re-extracting a ten-thousand-asset archive per keystroke is not a thing that
    /// can be made fast afterwards.
    ///
    /// <para>Empty for a <see cref="LoadedSet"/> that was never read from disk — which is only ever
    /// a test fixture, since every reader here sets it. Not <c>required</c> for that reason: making
    /// it so would force every hand-built fixture to name a directory it does not have and would
    /// not use.</para>
    /// </remarks>
    public string SourceDirectory { get; init; } = string.Empty;

    internal string? TempDir { get; init; }

    /// <summary>Releases anything the reader holds. A Set is read as paths, so this frees the
    /// temporary extraction directory when the Set came from a packed <c>.set</c> or a seal.</summary>
    public void Dispose()
    {
        if (TempDir is not null && Directory.Exists(TempDir))
            try { Directory.Delete(TempDir, recursive: true); } catch { /* best effort */ }
    }
}

/// <summary>Opens a cooked Set — a folder, or a packed <c>.set</c> archive.</summary>
public static class SetReader
{
    /// <summary>
    /// Whether <paramref name="path"/> is a directory holding a cooked Set.
    /// </summary>
    /// <param name="path">The path to test.</param>
    /// <returns>True when it is a folder with a <c>set.json</c> in it.</returns>
    /// <remarks>
    /// <para>A Set is the one archive kind that is also a FOLDER — <c>generate --out</c> writes one
    /// and <c>--pack</c> is optional — so a caller dispatching on the file type has a question
    /// <c>Archives.KindOf</c> cannot answer: that method resolves an EXTENSION, and a directory has
    /// none. This is outside its domain rather than a second copy of it.</para>
    ///
    /// <para>It asks for <c>set.json</c> rather than merely for a directory, so an ordinary folder
    /// still falls through to whatever the caller does with a path it does not recognise instead of
    /// being guessed at — the same rule the extension table follows.</para>
    /// </remarks>
    public static bool IsSetFolder(string path) =>
        !string.IsNullOrWhiteSpace(path)
        && Directory.Exists(path)
        && File.Exists(SetLayout.ManifestPath(path));

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
            string setJson = SetLayout.ManifestPath(dir);
            if (!File.Exists(setJson))
                throw new FileNotFoundException($"Not a cooked Set — 'set.json' was not found in {path}.");

            var manifest = JsonSerializer.Deserialize<SetManifest>(File.ReadAllText(setJson), Json.Options)
                ?? throw new InvalidOperationException($"Could not read the Set manifest in {path}.");

            string nftyDir = Path.Combine(dir, SetLayout.NftyDir);
            string imagesDir = Path.Combine(dir, SetLayout.ImagesDir);
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
                        m.Dna, m.Recipe, m.Rarity, m.Layers, m.AbsentLayers));
                }
            }

            return new LoadedSet
            {
                Manifest = manifest,
                Items = items,
                SourceDirectory = dir,
                TempDir = temp,
            };
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
