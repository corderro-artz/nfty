using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Nfty.Core.Formats;
using Nfty.Core.Imaging;
using Nfty.Core.Output;

namespace Nfty.Core.Publish.Community;

/// <summary>
/// Decides whether a packed Set may go in the community gallery. This half judges the ARCHIVE
/// itself, without extracting anything: a public gallery takes files from strangers, so a zip bomb,
/// a path that climbs out of its folder, a link, or a stray file must be refused before any reader
/// opens the contents.
/// </summary>
/// <remarks>Everything here reports; nothing throws, so every surface can show the same sentences
/// (<see cref="ShareRules"/>). Sizes and the entry count come from the zip's central directory,
/// which a hostile file can lie about, so a consumer that extracts must still enforce its own
/// limits while reading; this check refuses what is visibly wrong.</remarks>
public static partial class ShareCheck
{
    private const int MaxStrayReports = 10;

    [GeneratedRegex("^[A-Za-z]:")]
    private static partial Regex DriveLetter();

    /// <summary>Inspects the archive at <paramref name="setPath"/> and lists what keeps it out of the gallery.</summary>
    /// <param name="setPath">A packed <c>.set</c> file.</param>
    /// <returns>Problems in <see cref="ShareRules"/>' words; empty when the archive is safe and shaped like a Set. A hostile archive yields one problem and stops there.</returns>
    public static IReadOnlyList<string> CheckArchive(string setPath)
    {
        long length;
        try { length = new FileInfo(setPath).Length; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return [ShareRules.NotASet]; }
        if (length > ShareRules.MaxSetBytes) return [ShareRules.SetTooLarge(length, 0)];

        try
        {
            using var zip = ZipFile.OpenRead(setPath);
            return Inspect(zip);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException
                                      or NotSupportedException or InvalidOperationException)
        {
            return [ShareRules.NotASet];
        }
    }

    /// <summary>
    /// Judges a packed Set for the gallery: the archive first, then the Set inside it.
    /// </summary>
    /// <param name="setPath">A packed <c>.set</c> file.</param>
    /// <param name="ct">Stops the check; the only thing that throws.</param>
    /// <returns>The facts the gallery lists, or the problems that keep the Set out (facts then carry defaults). Bad input never throws: every failure becomes a sentence from <see cref="ShareRules"/>.</returns>
    public static Task<ShareFacts> CheckAsync(string setPath, CancellationToken ct = default) =>
        CheckAsync(setPath, ct, null);

    /// <summary>Test seams: what a check does at the points a test needs to reach.</summary>
    internal sealed class Hooks
    {
        /// <summary>Runs after every chunk the real-byte count reads.</summary>
        public Action? OnChunk { get; init; }
        /// <summary>Runs after every check, just before the file is looked at once more.</summary>
        public Action? AfterChecks { get; init; }
        /// <summary>Runs after the archive check, before the file is hashed.</summary>
        public Action? AfterArchiveCheck { get; init; }
    }

    internal static Task<ShareFacts> CheckAsync(string setPath, CancellationToken ct, Hooks? hooks) =>
        // The archive checks read the file synchronously (up to 1 GiB of inflating), so the whole
        // thing runs on the pool: a UI caller gets its thread back before the first byte is read.
        Task.Run(() => CheckCoreAsync(setPath, ct, hooks), ct);

    private static async Task<ShareFacts> CheckCoreAsync(string setPath, CancellationToken ct, Hooks? hooks)
    {
        // The stamp comes before EVERY read, so the name and link checks, the hash and the facts all
        // describe one file or the whole check is refused.
        var before = TryStamp(setPath);

        // Sealed FIRST. A .tin is a zip holding seal.json, which the archive check would call a stray
        // file (or, for something that is no zip at all, "not a Set"); the sentence has to say sealed.
        if (IsSealed(setPath)) return Refuse(ShareRules.Sealed);

        var archive = CheckArchive(setPath);
        if (archive.Count > 0) return Refuse([.. archive]);
        hooks?.AfterArchiveCheck?.Invoke();

        try
        {
            if (before is null) return Refuse(ShareRules.FileChanged);
            // Hashed before the deeper reads, and the stamp is looked at again here and before facts
            // go out, so a file swapped mid-check is refused, not vouched for.
            string fileSha;
            using (var fs = File.OpenRead(setPath))
                fileSha = Convert.ToHexStringLower(await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false));
            if (TryStamp(setPath) != before) return Refuse(ShareRules.FileChanged);

            using (var zip = ZipFile.OpenRead(setPath))
                if (CheckActualBytes(zip, ShareRules.MaxUncompressedBytes, ct, hooks?.OnChunk) is { } tooFar)
                    return Refuse(tooFar);

            using var set = await SetReader.ReadAsync(setPath, ct).ConfigureAwait(false);
            var facts = Judge(setPath, set, fileSha, ct);
            hooks?.AfterChecks?.Invoke();
            return TryStamp(setPath) != before ? Refuse(ShareRules.FileChanged) : facts;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            // CorruptSetException, damaged JSON, a file vanishing mid-read: the reader's words are for
            // the author at a terminal, not for a gallery card, so they all say one thing.
            return Refuse(ShareRules.NotASet);
        }
    }

    private static (long Length, DateTime Written)? TryStamp(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return (info.Length, info.LastWriteTimeUtc);
        }
        catch (Exception) { return null; }
    }

    private static ShareFacts Refuse(params string[] problems) =>
        new(problems, 0, 0, 0, false, false, null, "", "", "");

    private static ShareFacts Judge(string setPath, LoadedSet set, string fileSha, CancellationToken ct)
    {
        var manifest = set.Manifest;
        if (manifest.Files is not null) return Refuse(ShareRules.RenamedExport);

        var items = set.Items;
        if (items.Count == 0) return Refuse(ShareRules.NotASet);
        if (manifest.Count != items.Count || ImageFileCount(set.SourceDirectory) != items.Count)
            return Refuse(ShareRules.CountMismatch);
        if (items.Count > ShareRules.MaxAssets) return Refuse(ShareRules.TooManyAssets(items.Count));

        var ordered = items.OrderBy(i => i.Number).ToList();
        var problems = new List<string>();

        if (!TryIdentify(ordered[0].ImagePath, out int width, out int height))
            return Refuse(ShareRules.ImageUnreadable(Path.GetFileName(ordered[0].ImagePath)));
        if (width > ShareRules.MaxCanvas || height > ShareRules.MaxCanvas)
            return Refuse(ShareRules.CanvasTooLarge(width, height));

        foreach (var item in ordered.Skip(1))
        {
            ct.ThrowIfCancellationRequested();
            if (problems.Count >= MaxStrayReports) break;
            string name = Path.GetFileName(item.ImagePath);
            if (!TryIdentify(item.ImagePath, out int w, out int h)) problems.Add(ShareRules.ImageUnreadable(name));
            else if (w != width || h != height) problems.Add(ShareRules.ImageWrongSize(name));
        }
        if (problems.Count > 0) return Refuse([.. problems]);

        bool book = false;
        using (var zip = ZipFile.OpenRead(setPath))
        {
            var cbk = zip.Entries.FirstOrDefault(e => !e.FullName.Contains('/')
                && e.FullName.EndsWith(".cbk", StringComparison.OrdinalIgnoreCase));
            if (cbk is not null)
            {
                book = true;
                string sha;
                using (var s = cbk.Open()) sha = Convert.ToHexStringLower(SHA256.HashData(s));
                // A Set records its book's hash and nothing else, so a book it did not record - or
                // one recorded as nothing - is somebody else's file riding along in the gallery.
                if (!string.Equals(sha, manifest.CookbookSha256, StringComparison.OrdinalIgnoreCase))
                    return Refuse(ShareRules.ForeignBook);
            }
        }

        string fingerprint = Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(string.Join('\n', ordered.Select(i => i.Dna)))));

        return new ShareFacts([], ordered.Count, width, height, manifest.Models is not null, book,
            manifest.CookbookSha256, manifest.Name, fingerprint, fileSha);
    }

    private static int ImageFileCount(string dir)
    {
        string images = Path.Combine(dir, SetLayout.ImagesDir);
        return Directory.Exists(images) ? Directory.EnumerateFiles(images, "*", SearchOption.AllDirectories).Count() : 0;
    }

    private static bool TryIdentify(string path, out int width, out int height)
    {
        width = height = 0;
        try
        {
            var info = ImageFormats.Identify(path);
            width = info.Width;
            height = info.Height;
            return width > 0 && height > 0;
        }
        catch (Exception) { return false; }
    }

    private static bool IsSealed(string path)
    {
        if (Archives.TryKindOf(path, out var kind) && kind == ArchiveKind.Sealed) return true;
        try
        {
            if (!File.Exists(path)) return false;
            using var zip = ZipFile.OpenRead(path);
            return zip.Entries.Any(e => string.Equals(e.FullName, Publish.Seal.ManifestEntry, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// Counts the bytes each entry REALLY inflates to. The central directory's sizes are the file's own
    /// claim, and the reader that follows extracts with no limit of its own, so a lying archive is
    /// stopped here: reading stops the moment one entry passes its declared length or the total passes
    /// <paramref name="maxTotal"/>, never inflating further.
    /// </summary>
    /// <param name="zip">The opened archive.</param>
    /// <param name="maxTotal">The real decompressed total allowed.</param>
    /// <param name="ct">Checked after every chunk, so a stop never waits out a gigabyte.</param>
    /// <param name="onChunk">Test seam, called after each chunk.</param>
    /// <returns>The refusal sentence, or null when every entry is as small as it said.</returns>
    internal static string? CheckActualBytes(ZipArchive zip, long maxTotal = ShareRules.MaxUncompressedBytes,
        CancellationToken ct = default, Action? onChunk = null)
    {
        long total = 0;
        foreach (var e in zip.Entries)
        {
            if (IsDirectory(e.FullName)) continue;
            using var s = e.Open();
            if (CountEntry(s, e.Length, ref total, maxTotal, ct, onChunk) is { } refusal) return refusal;
        }
        return null;
    }

    /// <summary>Reads one entry's stream, stopping at the first byte past its declared length or the total.</summary>
    internal static string? CountEntry(Stream s, long declared, ref long total, long maxTotal,
        CancellationToken ct, Action? onChunk)
    {
        var buffer = new byte[81920];
        long entry = 0;
        int n;
        while ((n = s.Read(buffer, 0, buffer.Length)) > 0)
        {
            onChunk?.Invoke();
            ct.ThrowIfCancellationRequested();
            entry += n;
            total += n;
            if (entry > declared || total > maxTotal)
                return ShareRules.UnsafeArchive(ShareRules.ReasonExpandsTooFar);
        }
        return null;
    }

    private static IReadOnlyList<string> Inspect(ZipArchive zip)
    {
        var entries = zip.Entries;
        if (entries.Count > ShareRules.MaxZipEntries)
            return [ShareRules.UnsafeArchive(ShareRules.ReasonTooManyFiles)];

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in entries)
        {
            if (!names.Add(e.FullName)) return [ShareRules.UnsafeArchive(ShareRules.ReasonDuplicateName)];
            if (LeavesArchive(e.FullName)) return [ShareRules.UnsafeArchive(ShareRules.ReasonNameLeaves)];
            if (IsLink(e)) return [ShareRules.UnsafeArchive(ShareRules.ReasonHoldsLink)];
        }

        var sizes = entries.Where(e => !IsDirectory(e.FullName)).Select(e => (e.CompressedLength, e.Length));
        if (CheckSizes(sizes) is { } tooFar) return [tooFar];

        if (!names.Contains(SetLayout.ManifestFile)) return [ShareRules.NotASet];

        var problems = new List<string>();
        bool book = false;
        foreach (var e in entries)
        {
            string name = e.FullName;
            if (IsDirectory(name))
            {
                if (!InSetDirectory(name.TrimEnd('/'), isDirectory: true)) Stray(name);
            }
            else if (!name.Contains('/'))
            {
                if (name == SetLayout.ManifestFile) continue;
                if (name.EndsWith(".cbk", StringComparison.OrdinalIgnoreCase) && !book) { book = true; continue; }
                Stray(name);
            }
            else if (!InSetDirectory(name, isDirectory: false)) Stray(name);
        }
        return problems;

        void Stray(string path)
        {
            if (problems.Count < MaxStrayReports) problems.Add(ShareRules.UnexpectedFile(path));
        }
    }

    /// <summary>The expansion test: refuses an entry that grows past <see cref="ShareRules.MaxEntryRatio"/> to one, or entries that together grow past <see cref="ShareRules.MaxUncompressedBytes"/>.</summary>
    /// <param name="entries">Each file entry's compressed and uncompressed size; directories carry no ratio and are left out.</param>
    /// <returns>The refusal sentence, or null when the sizes are fine.</returns>
    internal static string? CheckSizes(IEnumerable<(long Compressed, long Length)> entries)
    {
        long total = 0;
        foreach (var (compressed, length) in entries)
        {
            if (length > (long)ShareRules.MaxEntryRatio * Math.Max(compressed, 1))
                return ShareRules.UnsafeArchive(ShareRules.ReasonExpandsTooFar);
            total += length;
            if (total > ShareRules.MaxUncompressedBytes)
                return ShareRules.UnsafeArchive(ShareRules.ReasonExpandsTooFar);
        }
        return null;
    }

    private static bool IsDirectory(string name) => name.EndsWith('/');

    // Also refuses ':' anywhere (drive letters, NTFS streams) and an empty or '.' segment, which two
    // readers can resolve differently ("images//x", "images/./x").
    private static bool LeavesArchive(string name) =>
        name.StartsWith('/') || name.Contains('\\') || name.Contains(':') || DriveLetter().IsMatch(name)
        || (IsDirectory(name) ? name[..^1] : name).Split('/').Any(s => s is "" or "." or "..");

    // The Unix file type sits in the high 16 bits of the external attributes; 0xA000 is a symlink.
    private static bool IsLink(ZipArchiveEntry e) => ((e.ExternalAttributes >> 16) & 0xF000) == 0xA000;

    // A path below one of the Set's directories. A bare file name at the root is judged by the caller.
    private static bool InSetDirectory(string path, bool isDirectory)
    {
        int slash = path.IndexOf('/');
        if (slash < 0 && !isDirectory) return false;
        string top = slash < 0 ? path : path[..slash];
        return top == SetLayout.ModelsDir || SetLayout.Directories.Contains(top, StringComparer.Ordinal);
    }
}
