using System.IO.Compression;
using System.Text.RegularExpressions;
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
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return [ShareRules.NotASet];
        }
    }

    private static IReadOnlyList<string> Inspect(ZipArchive zip)
    {
        var entries = zip.Entries;
        if (entries.Count > ShareRules.MaxZipEntries)
            return [ShareRules.UnsafeArchive(ShareRules.ReasonTooManyFiles)];

        foreach (var e in entries)
        {
            if (LeavesArchive(e.FullName)) return [ShareRules.UnsafeArchive(ShareRules.ReasonNameLeaves)];
            if (IsLink(e)) return [ShareRules.UnsafeArchive(ShareRules.ReasonHoldsLink)];
        }

        var sizes = entries.Where(e => !IsDirectory(e.FullName)).Select(e => (e.CompressedLength, e.Length));
        if (CheckSizes(sizes) is { } tooFar) return [tooFar];

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

    private static bool LeavesArchive(string name) =>
        name.StartsWith('/') || name.Contains('\\') || DriveLetter().IsMatch(name)
        || name.Split('/').Any(s => s == "..");

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
