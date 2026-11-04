using System.Globalization;
using System.Text;

namespace Nfty.Core.Output;

/// <summary>One line of a Set's DNA index: an <c>nfty/*.json</c> file as it stood when the index was written.</summary>
/// <param name="Name">The file name inside <c>nfty/</c>, for example <c>0042.json</c>.</param>
/// <param name="Size">The file's length in bytes.</param>
/// <param name="Number">The set number inside it.</param>
/// <param name="Dna">The DNA inside it.</param>
internal readonly record struct SetIndexEntry(string Name, long Size, int Number, string Dna);

/// <summary>
/// A cache of what <c>extend</c> needs from every already-minted asset (its DNA and number), kept so
/// a large Set need not be walked file by file.
/// </summary>
/// <remarks>
/// <para><b>A cache, never a record.</b> The per-asset <c>nfty/NNNN.json</c> files remain the truth;
/// the index is read only when it provably describes them. <see cref="TryRead"/> enumerates
/// <c>nfty/*.json</c> once (names and sizes, no file opened) and accepts the index only if that
/// listing matches it EXACTLY: every file listed, same size, none extra, none missing. A hand-deleted,
/// hand-added or hand-edited (size-changing) item, a truncated or garbled index, or a missing one
/// all answer null, and the caller falls back to the walk. Known limit: an edit that keeps a file's
/// byte length unchanged is not seen; nothing nfty writes does that.</para>
///
/// <para><b>Where.</b> <c>set-index.txt</c> at the Set's root, beside <c>set.json</c>, deliberately NOT
/// inside <c>nfty/</c>: that directory is taken whole by <see cref="SetLayout"/>'s include list and
/// listed by <c>*.json</c> by every reader, so an index there would be packed, exported, and one
/// wrong pattern from being read as an asset. At the root it is outside the include list, so the
/// packer and exporter never see it and no reader lists it. A packed <c>.set</c> therefore carries no
/// index; an extend over an unpacked one walks once and writes a fresh index.</para>
///
/// <para><b>Format.</b> Text, LF lines: a header <c>nfty-set-index 1 {count}</c>, then
/// <c>name TAB size TAB number TAB dna</c> per file in ordinal name order, the last line newline-
/// terminated. A missing final newline or a line count other than the header's is a partial write.</para>
/// </remarks>
internal static class SetIndex
{
    /// <summary>The index file's name at a Set's root.</summary>
    internal const string FileName = "set-index.txt";

    private const string Magic = "nfty-set-index";
    private const int Version = 1;

    /// <summary>Where the index of <paramref name="setDir"/> lives.</summary>
    internal static string PathOf(string setDir) => Path.Combine(setDir, FileName);

    /// <summary>Removes the index, so an interrupted write leaves none rather than a stale one.</summary>
    internal static void Delete(string setDir)
    {
        try { File.Delete(PathOf(setDir)); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>Writes the index for exactly these entries. Best effort: a cache that cannot be saved is skipped.</summary>
    /// <param name="setDir">The Set folder.</param>
    /// <param name="entries">Every <c>nfty/*.json</c> now on disk.</param>
    internal static void Write(string setDir, IReadOnlyList<SetIndexEntry> entries)
    {
        var text = Render(entries);
        if (text is null) return;
        string path = PathOf(setDir), temp = path + ".tmp";
        try
        {
            File.WriteAllText(temp, text, new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
        }
        catch (IOException) { TryDelete(temp); }
        catch (UnauthorizedAccessException) { TryDelete(temp); }
    }

    /// <summary>The awaiting twin of <see cref="Write"/>.</summary>
    internal static async Task WriteAsync(string setDir, IReadOnlyList<SetIndexEntry> entries, CancellationToken ct)
    {
        var text = Render(entries);
        if (text is null) return;
        string path = PathOf(setDir), temp = path + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, text, new UTF8Encoding(false), ct).ConfigureAwait(false);
            File.Move(temp, path, overwrite: true);
        }
        catch (IOException) { TryDelete(temp); }
        catch (UnauthorizedAccessException) { TryDelete(temp); }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static string? Render(IReadOnlyList<SetIndexEntry> entries)
    {
        var sorted = entries.OrderBy(e => e.Name, StringComparer.Ordinal).ToList();
        var sb = new StringBuilder(sorted.Count * 96 + 32);
        sb.Append(Magic).Append(' ').Append(Version.ToString(CultureInfo.InvariantCulture)).Append(' ')
            .Append(sorted.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (var e in sorted)
        {
            // A value that would break the line format means no index at all; the walk still works.
            if (Bad(e.Name) || Bad(e.Dna)) return null;
            sb.Append(e.Name).Append('\t').Append(e.Size.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(e.Number.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(e.Dna).Append('\n');
        }
        return sb.ToString();
    }

    private static bool Bad(string s) => s.Length == 0 || s.AsSpan().IndexOfAny('\t', '\n', '\r') >= 0;

    /// <summary>
    /// Reads the index of <paramref name="setDir"/> if, and only if, it matches <c>nfty/*.json</c> exactly.
    /// </summary>
    /// <param name="setDir">The Set folder.</param>
    /// <param name="nftyDir">Its <c>nfty/</c> folder, which exists.</param>
    /// <param name="needDnas">False to skip keeping DNA strings; only the numbers are wanted.</param>
    /// <returns>Entries in ordinal name order, or null when absent, partial, unreadable or stale.</returns>
    internal static List<SetIndexEntry>? TryRead(string setDir, string nftyDir, bool needDnas = true)
    {
        string text;
        try
        {
            string path = PathOf(setDir);
            if (!File.Exists(path)) return null;
            text = File.ReadAllText(path, new UTF8Encoding(false));
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        return Parse(text, nftyDir, needDnas);
    }

    /// <summary>The awaiting twin of <see cref="TryRead"/>.</summary>
    internal static async Task<List<SetIndexEntry>?> TryReadAsync(
        string setDir, string nftyDir, bool needDnas, CancellationToken ct)
    {
        string text;
        try
        {
            string path = PathOf(setDir);
            if (!File.Exists(path)) return null;
            text = await File.ReadAllTextAsync(path, new UTF8Encoding(false), ct).ConfigureAwait(false);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        return Parse(text, nftyDir, needDnas);
    }

    private static List<SetIndexEntry>? Parse(string text, string nftyDir, bool needDnas)
    {
        if (text.Length == 0 || text[^1] != '\n') return null; // partial last line
        var lines = text.Split('\n');
        // Split leaves one empty tail after the final newline.
        int lineCount = lines.Length - 1;
        var head = lines[0].Split(' ');
        if (head.Length != 3 || head[0] != Magic
            || !int.TryParse(head[1], NumberStyles.None, CultureInfo.InvariantCulture, out int version) || version != Version
            || !int.TryParse(head[2], NumberStyles.None, CultureInfo.InvariantCulture, out int count)
            || count != lineCount - 1) return null;

        // Names and sizes of what is really there: one enumeration, no file opened.
        var actual = new Dictionary<string, long>(count, StringComparer.Ordinal);
        try
        {
            foreach (var f in new DirectoryInfo(nftyDir).EnumerateFiles("*.json"))
                actual[f.Name] = f.Length;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        if (actual.Count != count) return null;

        var entries = new List<SetIndexEntry>(count);
        string? previous = null;
        for (int i = 1; i <= count; i++)
        {
            var parts = lines[i].Split('\t');
            if (parts.Length != 4 || parts[0].Length == 0 || parts[3].Length == 0) return null;
            if (!long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out long size)) return null;
            if (!int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int number)) return null;
            string name = parts[0];
            // Sorted and unique, so `count` distinct names that all exist on disk equal the listing.
            if (previous is not null && string.CompareOrdinal(previous, name) >= 0) return null;
            previous = name;
            if (!actual.TryGetValue(name, out long onDisk) || onDisk != size) return null;
            entries.Add(new SetIndexEntry(name, size, number, needDnas ? parts[3] : string.Empty));
        }
        return entries;
    }
}
