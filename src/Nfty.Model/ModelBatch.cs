using System.IO.Compression;
using System.Text;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model;

/// <summary>One picture to rise in a batch.</summary>
/// <param name="BaseName">The file name without extension; <see cref="ModelBatch.Build"/> sanitizes and de-duplicates it itself, so any text is safe here.</param>
/// <param name="Art">The finished picture. Borrowed: the batch does not dispose it.</param>
/// <param name="Depth">Its depth map, or <c>null</c> for paper.</param>
public sealed record ModelInput(string BaseName, Image<Rgba32> Art, DepthMap? Depth);

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
                var mesh = Riser.TryBuild(input.Art, input.Depth, options, $"'{name}'");
                if (mesh is null)
                    skipped.Add(new SkippedModel(name, SkipReason.NothingSolid, "nothing in it is fully opaque, so there is nothing to rise"));
                else
                    files.AddRange(ModelWriter.Write(mesh, input.Art, options, name));
            }
            catch (PartialAlphaException ex)
            {
                skipped.Add(new SkippedModel(name, SkipReason.PartialAlpha, ex.Message));
            }
        }
        return new ModelBatchResult(files, skipped);
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
