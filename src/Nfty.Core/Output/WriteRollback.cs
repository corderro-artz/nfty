namespace Nfty.Core.Output;

/// <summary>
/// Undoes what one failed or canceled <see cref="SetWriter"/> run ADDED, and nothing else.
/// </summary>
/// <remarks>
/// <para>A canceled cook used to leave images, <c>metadata/</c> and <c>nfty/</c> files with no
/// <c>set.json</c> (written last): a folder that looked like a Set and was not one. Moving a temp
/// folder into place was the other way to fix it, and the wrong one here: the output folder belongs
/// to the author (often a Kitchen full of their own parts), and <c>extend</c> writes into a Set that
/// already exists.</para>
///
/// <para>So the rollback is a listing diff, scoped to what nfty owns: the files at the folder's root
/// and directly inside <see cref="SetLayout"/>'s four directories are listed before the run; on
/// failure every file there that was NOT listed is deleted, then any of those directories (and the
/// folder itself) this run created and left empty. A file that existed before is never deleted, so
/// the author's own files and an extended Set's earlier assets survive. What a canceled
/// <c>extend</c> cannot undo is the rarity rewrite of items that already existed. The previous
/// <c>set.json</c> bytes are restored and the index invalidated, so the Set stays readable even if
/// packing fails after the new manifest was written.</para>
/// </remarks>
internal sealed class WriteRollback
{
    private static readonly string[] Dirs =
        [SetLayout.ImagesDir, SetLayout.MetadataDir, SetLayout.NftyDir, SetLayout.ModelsDir];

    private readonly string _outDir;
    private readonly bool _outDirExisted;
    private readonly byte[]? _manifest;
    private bool _mutating;
    private readonly Dictionary<string, HashSet<string>?> _before = new(StringComparer.Ordinal);

    private WriteRollback(string outDir)
    {
        _outDir = outDir;
        _outDirExisted = Directory.Exists(outDir);
        string manifest = SetLayout.ManifestPath(outDir);
        _manifest = File.Exists(manifest) ? File.ReadAllBytes(manifest) : null;
        _before[outDir] = List(outDir);
        foreach (var d in Dirs)
        {
            var path = Path.Combine(outDir, d);
            _before[path] = List(path);
        }
    }

    /// <summary>Lists what is there now, before a run writes anything.</summary>
    public static WriteRollback Capture(string outDir) => new(outDir);

    /// <summary>Arms rollback immediately before the writer's first filesystem mutation.
    /// Preflight refusals leave every existing file, including the index, untouched.</summary>
    public void BeginMutation() => _mutating = true;

    private static HashSet<string>? List(string dir) =>
        Directory.Exists(dir)
            ? new HashSet<string>(Directory.EnumerateFiles(dir), StringComparer.OrdinalIgnoreCase)
            : null;

    /// <summary>Deletes what the run added. Never throws: the run's own failure is what the caller reports.</summary>
    public void Undo()
    {
        if (!_mutating) return;
        // Assets may have been rolled back after the manifest/index were already replaced (Pack
        // runs last). Restore the previous count and invalidate the cache of the failed run.
        try
        {
            if (_manifest is not null) File.WriteAllBytes(SetLayout.ManifestPath(_outDir), _manifest);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        SetIndex.Delete(_outDir);
        foreach (var d in Dirs.Select(d => Path.Combine(_outDir, d)).Append(_outDir))
        {
            var before = _before[d];
            if (!Directory.Exists(d)) continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(d).ToList())
                    if (before is null || !before.Contains(file))
                        TryDelete(file);
                bool created = d == _outDir ? !_outDirExisted : before is null;
                if (created && !Directory.EnumerateFileSystemEntries(d).Any())
                    Directory.Delete(d);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void TryDelete(string file)
    {
        try { File.Delete(file); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
