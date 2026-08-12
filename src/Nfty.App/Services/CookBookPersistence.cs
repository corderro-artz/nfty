using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Nfty.Core.Formats;
using Nfty.Core.Model;

namespace Nfty.App.Services;

/// <summary>Persists an already-spliced cookbook graph back to the session's source archive:
/// crash-safe temp-then-atomic-replace, recompute the archive hash, and swap the session's Current
/// via its non-disposing Replace. Disposes nothing and shows no UI — the caller owns error handling
/// and the lifetime of whatever its mutation orphaned.</summary>
public static class CookBookPersistence
{
    /// <summary>
    /// A temp path beside <paramref name="dest"/> that belongs to <b>this</b> write and no other.
    ///
    /// <para>It used to be the fixed <c>dest + ".tmp"</c>, which two problems shared. The cleanup
    /// below deletes the temp file on failure, so a writer that lost a race to the same path deleted
    /// the file the <i>winner</i> was still filling — a no-op on Windows, where the winner's handle
    /// blocks the delete, and a success on POSIX, where it would strand the winner's
    /// <see cref="File.Move(string,string,bool)"/>. And <c>ZipArchiveMode.Create</c> opens
    /// <c>FileMode.CreateNew</c>, so a temp file left behind by a crashed write made every later save
    /// fail with "the file already exists" until someone deleted it by hand.</para>
    ///
    /// <para>A per-write suffix makes "a writer only ever deletes the file it created" true by
    /// construction rather than by timing. It does not serialize two writers to the same
    /// <i>destination</i> — nothing here can, and the caller that owns the edit is the one that must
    /// (see <c>ExplorerViewModel.MoveLayerAsync</c>'s in-flight guard).</para>
    /// </summary>
    private static string TempBeside(string dest) => $"{dest}.{Guid.NewGuid():N}.tmp";

    /// <summary>Writes the open book back to where it came from.</summary>
    /// <param name="session">Holds the book and its source path.</param>
    /// <param name="book2">The graph to write.</param>
    /// <param name="ct">Cancels the write.</param>
    /// <returns>A task that completes when the archive is written.</returns>
    public static async Task<LoadedCookBook> PersistAsync(ICookBookSession session, LoadedCookBook book2,
        CancellationToken ct = default)
    {
        if (session.SourcePath is not string dest)
            throw new InvalidOperationException("The cookbook has no source file to save to.");
        var tmp = TempBeside(dest);
        try
        {
            await CookBookArchive.WriteAsync(tmp, book2.Manifest, book2.Recipes, ct);
            File.Move(tmp, dest, overwrite: true);
            string sha;
            using (var s = File.OpenRead(dest)) sha = Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
            var book3 = new LoadedCookBook { Manifest = book2.Manifest, Recipes = book2.Recipes, SourceSha256 = sha };
            session.Replace(book3);
            return book3;
        }
        catch
        {
            if (File.Exists(tmp)) { try { File.Delete(tmp); } catch { /* best effort */ } }
            throw;
        }
    }

    /// <summary>Writes a cookbook to a user-chosen path, replacing an existing file: sibling temp plus an
    /// atomic move (CookBookArchive.Write opens CreateNew and would throw on an existing path). Used when
    /// creating a new .cbk.</summary>
    public static void WriteNew(string path, CookBookManifest manifest, IReadOnlyList<LoadedRecipe> recipes)
    {
        var tmp = TempBeside(path);
        try
        {
            CookBookArchive.Write(tmp, manifest, recipes);
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp)) { try { File.Delete(tmp); } catch { /* best effort */ } }
        }
    }
}
