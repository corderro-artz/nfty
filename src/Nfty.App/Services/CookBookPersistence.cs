using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Nfty.Core.Formats;
using Nfty.Core.Model;

namespace Nfty.App.Services;

/// <summary>Persists an already-spliced cookbook graph back to the session's source archive:
/// atomically replace the archive, recompute its hash, and adopt the graph through the session's
/// non-disposing Replace. Hashing and adoption follow the file commit; a failure there does not undo
/// the written archive. Disposes nothing and shows no UI: callers own error handling and orphaned images.</summary>
public static class CookBookPersistence
{
    /// <summary>Writes the open book back to where it came from.</summary>
    /// <param name="session">Holds the book and its source path.</param>
    /// <param name="book2">The graph to write.</param>
    /// <param name="ct">Cancels the write.</param>
    /// <returns>The adopted graph carrying the hash of the written archive.</returns>
    public static async Task<LoadedCookBook> PersistAsync(ICookBookSession session, LoadedCookBook book2,
        CancellationToken ct = default)
    {
        if (session.SourcePath is not string dest)
            throw new InvalidOperationException("The cookbook has no source file to save to.");
        await AtomicFile.ReplaceAsync(dest, tmp => CookBookArchive.WriteAsync(tmp, book2.Manifest, book2.Recipes, ct));
        string sha = await CookBookArchive.HashOfAsync(dest, ct);
        var book3 = new LoadedCookBook { Manifest = book2.Manifest, Recipes = book2.Recipes, SourceSha256 = sha };
        session.Replace(book3);
        return book3;
    }

    /// <summary>Writes a cookbook to a user-chosen path, replacing an existing file: sibling temp plus an
    /// atomic move (CookBookArchive.Write opens CreateNew and would throw on an existing path). Used when
    /// creating a new .cbk.</summary>
    /// <param name="path">The destination archive path.</param>
    /// <param name="manifest">The new book's manifest.</param>
    /// <param name="recipes">Borrowed recipes to serialize; their images are not disposed.</param>
    public static void WriteNew(string path, CookBookManifest manifest, IReadOnlyList<LoadedRecipe> recipes)
    {
        AtomicFile.Replace(path, tmp => CookBookArchive.Write(tmp, manifest, recipes));
    }
}
