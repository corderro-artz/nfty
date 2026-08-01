using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Nfty.Core.Formats;

namespace Nfty.App.Services;

/// <summary>Persists an already-spliced cookbook graph back to the session's source archive:
/// crash-safe temp-then-atomic-replace, recompute the archive hash, and swap the session's Current
/// via its non-disposing Replace. Disposes nothing and shows no UI — the caller owns error handling
/// and the lifetime of whatever its mutation orphaned.</summary>
public static class CookBookPersistence
{
    public static async Task<LoadedCookBook> PersistAsync(ICookBookSession session, LoadedCookBook book2,
        CancellationToken ct = default)
    {
        if (session.SourcePath is not string dest)
            throw new InvalidOperationException("The cookbook has no source file to save to.");
        var tmp = dest + ".tmp";
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
}
