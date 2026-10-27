namespace Nfty.Core.Formats;

/// <summary>
/// Replaces a file as one step: the new contents are written to a temp file beside it, then moved
/// over the original, so a failure or a crash leaves either the old file or the new one, never half
/// of one.
///
/// <para><b>The temp name is unique per write.</b> It used to be the fixed <c>path + ".tmp"</c> in
/// several places, which two problems shared. The cleanup deletes the temp file on failure, so a
/// writer that lost a race to the same path deleted the file the <i>winner</i> was still filling —
/// a no-op on Windows, where the winner's handle blocks the delete, and a success on POSIX, where it
/// would strand the winner's move. And <c>ZipArchiveMode.Create</c> opens <c>FileMode.CreateNew</c>,
/// so a temp file left behind by a crashed write made every later save fail with "the file already
/// exists" until someone deleted it by hand.</para>
///
/// <para>A per-write suffix makes "a writer only ever deletes the file it created" true by
/// construction rather than by timing. It does not serialize two writers to the same
/// <i>destination</i> — nothing here can, and the caller that owns the edit is the one that must
/// (see <c>ExplorerViewModel.MoveLayerAsync</c>'s in-flight guard).</para>
/// </summary>
public static class AtomicFile
{
    /// <summary>Writes through a unique temp file beside <paramref name="path"/>, then replaces it.</summary>
    /// <param name="path">The file to replace; created when absent.</param>
    /// <param name="writeTemp">Writes the new contents to the path it is given, which does not exist yet.</param>
    public static void Replace(string path, Action<string> writeTemp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(writeTemp);
        string tmp = TempBeside(path);
        try
        {
            writeTemp(tmp);
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            DeleteQuietly(tmp);
            throw;
        }
    }

    /// <summary>The awaiting twin of <see cref="Replace"/>.</summary>
    /// <param name="path">The file to replace; created when absent.</param>
    /// <param name="writeTemp">Writes the new contents to the path it is given, which does not exist yet.</param>
    /// <returns>A task that completes when the file has been replaced.</returns>
    public static async Task ReplaceAsync(string path, Func<string, Task> writeTemp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(writeTemp);
        string tmp = TempBeside(path);
        try
        {
            await writeTemp(tmp).ConfigureAwait(false);
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            DeleteQuietly(tmp);
            throw;
        }
    }

    /// <summary>A temp path beside <paramref name="path"/> that belongs to one write and no other.</summary>
    /// <param name="path">The destination.</param>
    /// <returns>The sibling temp path.</returns>
    internal static string TempBeside(string path) => $"{path}.{Guid.NewGuid():N}.tmp";

    private static void DeleteQuietly(string tmp)
    {
        try { if (File.Exists(tmp)) File.Delete(tmp); }
        catch { /* best effort: the original exception is the one worth surfacing */ }
    }
}
