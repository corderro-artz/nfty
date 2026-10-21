using System.IO.Compression;
using System.Text;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model;

/// <summary>One picture to rise in a batch.</summary>
/// <param name="BaseName">The file name without extension; run through <see cref="ModelBatch.SafeBaseName"/> by the caller.</param>
/// <param name="Art">The finished picture. Borrowed: the batch does not dispose it.</param>
/// <param name="Depth">Its depth map, or <c>null</c> for paper.</param>
public sealed record ModelInput(string BaseName, Image<Rgba32> Art, DepthMap? Depth);

/// <summary>What a batch made.</summary>
/// <param name="Files">Every file written for the pictures that could rise.</param>
/// <param name="Skipped">One line per picture that could not, naming it and why.</param>
public sealed record ModelBatchResult(IReadOnlyList<ModelFile> Files, IReadOnlyList<string> Skipped);

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
        return trimmed.Length == 0 ? "model" : trimmed;
    }

    /// <summary>Makes a list of names safe and distinct, ignoring case.</summary>
    /// <param name="names">The wanted names.</param>
    /// <returns>Each name made safe; a collision gets <c>-2</c>, <c>-3</c>, … skipping any suffix already taken.</returns>
    public static IReadOnlyList<string> UniqueBaseNames(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(names.Count);
        foreach (var n in names)
        {
            var safe = SafeBaseName(n);
            var candidate = safe;
            for (int i = 2; !taken.Add(candidate); i++) candidate = $"{safe}-{i}";
            result.Add(candidate);
        }
        return result;
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
        var skipped = new List<string>();
        foreach (var input in inputs)
        {
            try
            {
                var mesh = Riser.TryBuild(input.Art, input.Depth, options, $"'{input.BaseName}'");
                if (mesh is null)
                    skipped.Add($"{input.BaseName}: nothing in it is fully opaque, so there is nothing to rise");
                else
                    files.AddRange(ModelWriter.Write(mesh, input.Art, options, input.BaseName));
            }
            catch (PartialAlphaException ex)
            {
                skipped.Add($"{input.BaseName}: {ex.Message}");
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
        Directory.CreateDirectory(directory);
        foreach (var f in files) File.WriteAllBytes(Path.Combine(directory, f.FileName), f.Bytes);
    }

    /// <summary>Writes the files into a zip: ordinal-sorted entries, fixed timestamp, so identical
    /// input gives identical bytes.</summary>
    /// <param name="zipPath">The zip to create or replace.</param>
    /// <param name="files">The files.</param>
    public static void WriteZip(string zipPath, IReadOnlyList<ModelFile> files)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        ArgumentNullException.ThrowIfNull(files);
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
