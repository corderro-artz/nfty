using System.IO.Compression;
using System.Text;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model;

/// <summary>One picture to rise in a batch.</summary>
/// <param name="BaseName">The file name without extension; <see cref="ModelBatch.Build"/> sanitizes and de-duplicates it itself, so any text is safe here.</param>
/// <param name="Art">The finished picture. Borrowed: the batch does not dispose it.</param>
/// <param name="Faces">The stack's face owners and wall seams (the atlas halves), or <c>null</c> for a single picture. Borrowed.</param>
/// <param name="Depth">Its depth map, or <c>null</c> for paper.</param>
public sealed record ModelInput(string BaseName, Image<Rgba32> Art, DepthMap? Depth, StackFaces? Faces = null);

/// <summary>What a batch made.</summary>
/// <param name="Files">Every file written for the pictures that could rise.</param>
/// <param name="Skipped">One entry per picture that could not, naming it and why.</param>
public sealed record ModelBatchResult(IReadOnlyList<ModelFile> Files, IReadOnlyList<SkippedModel> Skipped);

/// <summary>Why a picture in a batch could not rise.</summary>
public enum SkipReason
{
    /// <summary>It has partly transparent pixels, which a voxel cannot be.</summary>
    PartialAlpha,

    /// <summary>Nothing in it is fully opaque.</summary>
    NothingSolid,
}

/// <summary>A picture a batch left out.</summary>
/// <param name="Name">The file name stem it would have had (already made safe and distinct).</param>
/// <param name="Reason">Why, decided by which check refused it, never by reading a message.</param>
/// <param name="Message">The sentence that says so, for people.</param>
public sealed record SkippedModel(string Name, SkipReason Reason, string Message)
{
    /// <summary>The line form: <c>name: message</c>.</summary>
    public override string ToString() => $"{Name}: {Message}";
}

/// <summary>Rises many pictures at once and delivers the files to a folder or a zip.</summary>
public static class ModelBatch
{
    private static readonly char[] Reserved = { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };

    /// <summary>Makes a name safe as a file name on every platform.</summary>
    /// <param name="name">What the caller wants to call it.</param>
    /// <returns>The name with reserved and control characters replaced by <c>-</c>, outer spaces and
    /// dots trimmed; <c>model</c> when nothing is left.</returns>
    /// <remarks>An explicit set, not <see cref="Path.GetInvalidFileNameChars"/>, which differs by OS.</remarks>
    public static string SafeBaseName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
            sb.Append(c < 0x20 || Array.IndexOf(Reserved, c) >= 0 ? '-' : c);
        var trimmed = sb.ToString().Trim(' ', '.');
        if (trimmed.Length == 0) return "model";
        // Windows treats CON, CON.txt, con.x … as the device whatever the extension.
        int dot = trimmed.IndexOf('.');
        string stem = (dot < 0 ? trimmed : trimmed[..dot]).TrimEnd(' ');
        return IsDeviceName(stem) ? stem + "_" + trimmed[stem.Length..] : trimmed;
    }

    private static bool IsDeviceName(string stem)
    {
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase))
            return true;
        return stem.Length == 4 && stem[3] is >= '1' and <= '9'
            && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Sanitizes <paramref name="name"/> and makes it distinct from every name already in
    /// <paramref name="taken"/> (ignoring case), recording it there.</summary>
    private static string Claim(HashSet<string> taken, string name)
    {
        var safe = SafeBaseName(name);
        var candidate = safe;
        for (int i = 2; !taken.Add(candidate); i++) candidate = $"{safe}-{i}";
        return candidate;
    }

    /// <summary>Throws unless <paramref name="fileName"/> stays inside whatever folder it is given.</summary>
    private static void CheckPlain(string fileName)
    {
        // A fake root: the check is about the NAME, so no real folder is needed.
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "nfty-root"));
        bool bad = string.IsNullOrWhiteSpace(fileName) || Path.IsPathRooted(fileName)
            || fileName.AsSpan().IndexOfAny('\\', ':') >= 0;
        if (!bad)
        {
            string full = Path.GetFullPath(Path.Combine(root, fileName));
            bad = !full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        }
        if (bad)
            throw new ArgumentException($"'{fileName}' is rooted or leaves the destination folder.", nameof(fileName));
    }

    /// <summary>Makes a list of names safe and distinct, ignoring case.</summary>
    /// <param name="names">The wanted names.</param>
    /// <returns>Each name made safe; a collision gets <c>-2</c>, <c>-3</c>, … skipping any suffix already taken.</returns>
    public static IReadOnlyList<string> UniqueBaseNames(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return names.Select(n => Claim(taken, n)).ToList();
    }

    /// <summary>The file names <see cref="ModelWriter.Write"/> writes for a base name.</summary>
    /// <param name="baseName">The base name.</param>
    /// <param name="format">The format.</param>
    /// <returns>The names, in the writer's order.</returns>
    public static IReadOnlyList<string> FileNamesFor(string baseName, ModelFormat format)
    {
        ArgumentNullException.ThrowIfNull(baseName);
        return format switch
        {
            ModelFormat.Glb => new[] { baseName + ".glb" },
            ModelFormat.GltfWithBin => new[] { baseName + ".gltf", baseName + ".bin" },
            ModelFormat.GltfEmbedded => new[] { baseName + ".gltf" },
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown model format."),
        };
    }

    /// <summary>Rises every input that can rise.</summary>
    /// <param name="inputs">The pictures. Enumerated once, each finished before the next is asked
    /// for — a lazy sequence may free an input when the next is requested.</param>
    /// <param name="options">How to rise and write.</param>
    /// <returns>The files, and a line for each picture skipped.</returns>
    public static ModelBatchResult Build(IEnumerable<ModelInput> inputs, RiseOptions options)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(options);
        var files = new List<ModelFile>();
        var skipped = new List<SkippedModel>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in inputs)
        {
            // The one gate: whatever the caller named it, the file name is made plain and distinct here.
            string name = Claim(taken, input.BaseName);
            try
            {
                var mesh = Riser.TryBuild(input.Art, input.Depth, options, $"'{name}'", input.Faces);
                if (mesh is null)
                    skipped.Add(new SkippedModel(name, SkipReason.NothingSolid, "nothing in it is fully opaque, so there is nothing to rise"));
                else
                    files.AddRange(ModelWriter.Write(mesh, input.Art, options, name, input.Faces));
            }
            catch (PartialAlphaException ex)
            {
                skipped.Add(new SkippedModel(name, SkipReason.PartialAlpha, ex.Message));
            }
        }
        return new ModelBatchResult(files, skipped);
    }

    /// <summary>Rises a batch and writes it as it goes: each model is built, written and let go before
    /// the next picture is asked for, so a batch of ten thousand holds one model, never ten thousand.</summary>
    /// <param name="baseNames">One wanted name per picture; made safe and distinct exactly as
    /// <see cref="Build"/> does, in this order.</param>
    /// <param name="open">Hands over picture <c>i</c>. Called once per index, on the calling thread,
    /// never before the previous picture has been released. Its <see cref="ModelInput.BaseName"/> is
    /// ignored: the name is <paramref name="baseNames"/>'s.</param>
    /// <param name="release">Called once for every opened picture as soon as its files are written (or
    /// its build failed); null when the caller keeps ownership.</param>
    /// <param name="options">How to rise and write.</param>
    /// <param name="destination">A folder or a zip.</param>
    /// <param name="progress">Told how many pictures are done (written or skipped) after each one.</param>
    /// <param name="onWritten">Told each file's name once it is in the destination, on the thread
    /// doing the export; how a caller knows whether a stopped batch wrote anything.</param>
    /// <param name="cancellation">Checked before each picture. A canceled zip is never left behind (it is
    /// written beside its destination and moved over it only when complete); a canceled folder keeps
    /// the models finished before the stop, each whole, since a file is written only once its model is.</param>
    /// <returns>The file names written, the pictures skipped, and the files that already existed.</returns>
    /// <remarks>Models are written in ordinal order of their first file name, each model's files in
    /// the writer's order: for a one-file format that is exactly ordinal order of every entry, so a
    /// zip has the bytes <see cref="WriteZip"/> would give the built files. Identical input gives
    /// identical bytes either way.</remarks>
    public static ModelExportResult Export(IReadOnlyList<string> baseNames, Func<int, ModelInput> open,
        Action<ModelInput>? release, RiseOptions options, ModelDestination destination,
        IProgress<int>? progress = null, CancellationToken cancellation = default, Action<string>? onWritten = null)
    {
        ArgumentNullException.ThrowIfNull(baseNames);
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(destination);

        var names = UniqueBaseNames(baseNames);
        var order = Enumerable.Range(0, names.Count)
            .OrderBy(i => FileNamesFor(names[i], options.Format)[0], StringComparer.Ordinal).ToList();
        foreach (var n in names)
            foreach (var f in FileNamesFor(n, options.Format)) CheckPlain(f);

        var written = new List<string>();
        var skipped = new List<SkippedModel>();
        var replaced = new List<string>();
        using var sink = destination.Open(replaced);
        int done = 0;
        foreach (int i in order)
        {
            cancellation.ThrowIfCancellationRequested();
            string name = names[i];
            var input = open(i);
            try
            {
                var mesh = Riser.TryBuild(input.Art, input.Depth, options, $"'{name}'", input.Faces);
                if (mesh is null)
                    skipped.Add(new SkippedModel(name, SkipReason.NothingSolid, "nothing in it is fully opaque, so there is nothing to rise"));
                else
                {
                    foreach (var f in ModelWriter.Write(mesh, input.Art, options, name, input.Faces))
                    {
                        sink.Add(f);
                        onWritten?.Invoke(f.FileName);
                        written.Add(f.FileName);
                    }
                }
            }
            catch (PartialAlphaException ex)
            {
                skipped.Add(new SkippedModel(name, SkipReason.PartialAlpha, ex.Message));
            }
            finally
            {
                release?.Invoke(input);
            }
            progress?.Report(++done);
        }
        sink.Complete();
        // Skips are reported in the caller's order, as Build reports them.
        var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int k = 0; k < names.Count; k++) rank[names[k]] = k;
        skipped.Sort((x, y) => rank[x.Name].CompareTo(rank[y.Name]));
        return new ModelExportResult(written, skipped, replaced);
    }

    /// <summary>Writes the files into a folder, creating it.</summary>
    /// <param name="directory">The folder.</param>
    /// <param name="files">The files.</param>
    public static void WriteFolder(string directory, IReadOnlyList<ModelFile> files)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(files);
        foreach (var f in files) CheckPlain(f.FileName);
        Directory.CreateDirectory(directory);
        foreach (var f in files)
        {
            string target = Path.Combine(directory, f.FileName);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, f.Bytes);
        }
    }

    /// <summary>Writes the files into a zip: ordinal-sorted entries, fixed timestamp, so identical
    /// input gives identical bytes.</summary>
    /// <param name="zipPath">The zip to create or replace.</param>
    /// <param name="files">The files.</param>
    public static void WriteZip(string zipPath, IReadOnlyList<ModelFile> files)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        ArgumentNullException.ThrowIfNull(files);
        foreach (var f in files) CheckPlain(f.FileName);
        var stamp = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var stream = new FileStream(zipPath, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var f in files.OrderBy(f => f.FileName, StringComparer.Ordinal))
        {
            var entry = zip.CreateEntry(f.FileName, CompressionLevel.Optimal);
            entry.LastWriteTime = stamp;
            using var es = entry.Open();
            es.Write(f.Bytes);
        }
    }
}

/// <summary>What a streamed batch wrote.</summary>
/// <param name="FileNames">Every file written, in the order written.</param>
/// <param name="Skipped">One entry per picture that could not rise, in the caller's order.</param>
/// <param name="Replaced">Files (or the zip) that existed before and were overwritten.</param>
public sealed record ModelExportResult(IReadOnlyList<string> FileNames, IReadOnlyList<SkippedModel> Skipped,
    IReadOnlyList<string> Replaced);

/// <summary>Where a streamed batch goes: a folder of files, or one zip.</summary>
public abstract class ModelDestination
{
    private protected ModelDestination() { }

    /// <summary>A folder, created on the first file.</summary>
    /// <param name="directory">The folder.</param>
    /// <returns>The destination.</returns>
    public static ModelDestination Folder(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        return new FolderDestination(directory);
    }

    /// <summary>One zip: ordinal entries, fixed timestamps, in place only once complete.</summary>
    /// <param name="zipPath">The zip to create or replace.</param>
    /// <returns>The destination.</returns>
    public static ModelDestination Zip(string zipPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        return new ZipDestination(zipPath);
    }

    internal abstract Sink Open(List<string> replaced);

    // Every field here is readonly (rule D7): state that changes lives in collections the sink owns,
    // never in a field written after construction.
    internal abstract class Sink : IDisposable
    {
        public abstract void Add(ModelFile file);
        public abstract void Complete();
        public abstract void Dispose();
    }

    private sealed class FolderDestination : ModelDestination
    {
        private readonly string _directory;
        public FolderDestination(string directory) => _directory = directory;
        internal override Sink Open(List<string> replaced) => new FolderSink(_directory, replaced);

        private sealed class FolderSink : Sink
        {
            private readonly string _directory;
            private readonly List<string> _replaced;

            public FolderSink(string directory, List<string> replaced)
            {
                _directory = directory;
                _replaced = replaced;
            }

            public override void Add(ModelFile file)
            {
                Directory.CreateDirectory(_directory);
                string target = Path.Combine(_directory, file.FileName);
                if (File.Exists(target)) _replaced.Add(file.FileName);
                File.WriteAllBytes(target, file.Bytes);
            }

            public override void Complete() { }

            public override void Dispose() { }
        }
    }

    private sealed class ZipDestination : ModelDestination
    {
        private readonly string _path;
        public ZipDestination(string path) => _path = path;
        internal override Sink Open(List<string> replaced) => new ZipSink(_path, replaced);

        private sealed class ZipSink : Sink
        {
            private static readonly DateTimeOffset Stamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
            private readonly string _path;
            private readonly string _temp;
            private readonly List<string> _replaced;
            private readonly List<string> _entries = new();
            private readonly FileStream _stream;
            private readonly ZipArchive _zip;

            public ZipSink(string path, List<string> replaced)
            {
                _path = Path.GetFullPath(path);
                // Written BESIDE the destination and moved over it only when complete: a canceled or
                // failed batch never leaves half a zip, and never touches the zip that was there.
                _temp = $"{_path}.{Guid.NewGuid():N}.tmp";
                _replaced = replaced;
                _stream = new FileStream(_temp, FileMode.CreateNew, FileAccess.Write);
                try { _zip = new ZipArchive(_stream, ZipArchiveMode.Create); }
                catch
                {
                    _stream.Dispose();
                    DeleteTemp();
                    throw;
                }
            }

            public override void Add(ModelFile file)
            {
                var entry = _zip.CreateEntry(file.FileName, CompressionLevel.Optimal);
                entry.LastWriteTime = Stamp;
                using (var es = entry.Open()) es.Write(file.Bytes);
                _entries.Add(file.FileName);
            }

            public override void Complete()
            {
                _zip.Dispose();
                _stream.Dispose();
                if (_entries.Count == 0) return;      // nothing could rise: no zip, as before (Dispose removes the temp)
                if (File.Exists(_path)) _replaced.Add(Path.GetFileName(_path));
                File.Move(_temp, _path, overwrite: true);
            }

            public override void Dispose()
            {
                // Runs on the failure path too, possibly after a Complete that threw part-way: it must
                // never throw over the exception that brought it here.
                try { _zip.Dispose(); }
                catch (IOException) { /* the zip is being thrown away */ }
                catch (InvalidDataException) { /* the zip is being thrown away */ }
                catch (ObjectDisposedException) { /* already closed */ }
                try { _stream.Dispose(); }
                catch (IOException) { /* the zip is being thrown away */ }
                DeleteTemp();
            }

            private void DeleteTemp()
            {
                try { if (File.Exists(_temp)) File.Delete(_temp); }
                catch (IOException) { /* best effort */ }
                catch (UnauthorizedAccessException) { /* best effort */ }
            }
        }
    }
}
