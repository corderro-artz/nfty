using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nfty.Core.Formats;
using Nfty.Core.Output;

namespace Nfty.App.Services;

/// <summary>
/// Finds the CookBook a Set was cooked from.
/// </summary>
/// <remarks>
/// <para>A Set records its book's HASH and never the book, so there is no path to follow — which is
/// why the export dialog asked the author to go and find the file. In practice the file is almost
/// always one of a handful the app can already name: the book that is open, one the author opened
/// recently, or one sitting beside the Set. This tries those and keeps the one whose bytes hash to
/// what <c>set.json</c> recorded.</para>
///
/// <para><b>Matching on the hash is what makes guessing safe.</b> A candidate is accepted only when
/// it IS the book, so a folder holding a dozen <c>.cbk</c> files cannot produce a wrong answer - it
/// produces no answer, and the picker is still there. Shipping the wrong book under the label
/// "source CookBook" would be worse than shipping none.</para>
/// </remarks>
public static class CookBookLocator
{
    /// <summary>
    /// The first candidate that is the book the Set was cooked from, or null.
    /// </summary>
    /// <param name="recordedSha256">What the Set recorded — <c>SetManifest.CookbookSha256</c>. A
    /// null means the Set cannot say, so nothing can be matched against it.</param>
    /// <param name="candidates">Paths to try, best first. Missing files and unreadable ones are
    /// skipped rather than thrown on: this is a convenience, and a broken recents entry must not
    /// take the export dialog down with it.</param>
    /// <returns>The path of the matching CookBook, or null when none of them is it.</returns>
    public static string? Find(string? recordedSha256, IEnumerable<string> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (recordedSha256 is null) return null;

        foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            if (!string.Equals(Path.GetExtension(path), Archives.CookBookExtension,
                    StringComparison.OrdinalIgnoreCase)) continue;
            if (!File.Exists(path)) continue;

            string hash;
            try { hash = CookBookArchive.HashOf(path); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            if (SetProvenance.IsSameBook(recordedSha256, hash)) return path;
        }
        return null;
    }

    /// <summary>
    /// CookBooks sitting where a cook usually leaves them: in the Set's own folder, and in the
    /// folder that holds it.
    /// </summary>
    /// <remarks>
    /// <c>generate --out</c> writes a Set into a folder the author picked, and the book is very
    /// often the thing next to it. The parent is included because the Set is usually a subfolder of
    /// the working directory rather than a sibling of the book. It goes no deeper: a Kitchen can be
    /// large, and this is a convenience rather than a search.
    /// </remarks>
    /// <param name="setDirectory">Where the Set's files are.</param>
    /// <returns>Candidate paths, nearest first.</returns>
    public static IReadOnlyList<string> Nearby(string? setDirectory)
    {
        if (string.IsNullOrWhiteSpace(setDirectory)) return Array.Empty<string>();

        var found = new List<string>();
        void Scan(string? dir)
        {
            if (dir is null || !Directory.Exists(dir)) return;
            try { found.AddRange(Directory.EnumerateFiles(dir, "*" + Archives.CookBookExtension)); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        Scan(setDirectory);
        Scan(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(setDirectory)));
        return found;
    }
}
