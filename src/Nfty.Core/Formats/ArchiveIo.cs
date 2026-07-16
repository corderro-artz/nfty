using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Formats;

internal static class ArchiveIo
{
    public static void WriteManifest<T>(ZipArchive zip, T manifest)
    {
        var entry = zip.CreateEntry("manifest.json");
        using var s = entry.Open();
        JsonSerializer.Serialize(s, manifest, Json.Options);
    }

    public static T ReadManifest<T>(ZipArchive zip) where T : ISchemaVersioned
    {
        var entry = zip.GetEntry("manifest.json")
            ?? throw new InvalidDataException("Archive is missing manifest.json.");
        using var s = entry.Open();
        var manifest = JsonSerializer.Deserialize<T>(s, Json.Options)
            ?? throw new InvalidDataException("manifest.json deserialized to null.");
        UnsupportedSchemaVersionException.Require(manifest);
        return manifest;
    }

    public static void WriteImage(ZipArchive zip, string name, Image<Rgba32> img)
    {
        var entry = zip.CreateEntry(name);
        using var s = entry.Open();
        img.Save(s, new PngEncoder());
    }

    public static Image<Rgba32> ReadImage(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name) ?? throw new InvalidDataException($"Archive is missing {name}.");
        using var s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        ms.Position = 0;
        return Image.Load<Rgba32>(ms);
    }

    public static void WriteNested(ZipArchive zip, string entryName, Action<ZipArchive> build)
    {
        using var ms = new MemoryStream();
        using (var inner = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            build(inner);
        ms.Position = 0;
        var entry = zip.CreateEntry(entryName);
        using var s = entry.Open();
        ms.CopyTo(s);
    }

    public static T ReadNested<T>(ZipArchive zip, string entryName, Func<ZipArchive, T> read)
    {
        var entry = zip.GetEntry(entryName) ?? throw new InvalidDataException($"Archive is missing {entryName}.");
        using var s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        ms.Position = 0;
        using var inner = new ZipArchive(ms, ZipArchiveMode.Read);
        return read(inner);
    }

    /// <summary>SHA-256 of a file's bytes, lowercase hex. Streamed — archives can be large.</summary>
    public static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        byte[] hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // ---- async twins -------------------------------------------------------------------
    // ZipArchive's own directory parsing is synchronous with no async API; what genuinely
    // awaits here is the per-entry stream I/O, JSON, and PNG codec work.

    public static async Task WriteManifestAsync<T>(ZipArchive zip, T manifest, CancellationToken ct)
    {
        var entry = zip.CreateEntry("manifest.json");
        await using var s = entry.Open();
        await JsonSerializer.SerializeAsync(s, manifest, Json.Options, ct);
    }

    public static async Task<T> ReadManifestAsync<T>(ZipArchive zip, CancellationToken ct)
        where T : ISchemaVersioned
    {
        var entry = zip.GetEntry("manifest.json")
            ?? throw new InvalidDataException("Archive is missing manifest.json.");
        await using var s = entry.Open();
        var manifest = await JsonSerializer.DeserializeAsync<T>(s, Json.Options, ct)
            ?? throw new InvalidDataException("manifest.json deserialized to null.");
        UnsupportedSchemaVersionException.Require(manifest);
        return manifest;
    }

    public static async Task WriteImageAsync(ZipArchive zip, string name, Image<Rgba32> img, CancellationToken ct)
    {
        var entry = zip.CreateEntry(name);
        await using var s = entry.Open();
        await img.SaveAsync(s, new PngEncoder(), ct);
    }

    public static async Task<Image<Rgba32>> ReadImageAsync(ZipArchive zip, string name, CancellationToken ct)
    {
        var entry = zip.GetEntry(name) ?? throw new InvalidDataException($"Archive is missing {name}.");
        await using var s = entry.Open();
        using var ms = new MemoryStream();
        await s.CopyToAsync(ms, ct);
        ms.Position = 0;
        return await Image.LoadAsync<Rgba32>(ms, ct);
    }

    public static async Task WriteNestedAsync(
        ZipArchive zip, string entryName, Func<ZipArchive, Task> build, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        using (var inner = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            await build(inner);
        ms.Position = 0;
        var entry = zip.CreateEntry(entryName);
        await using var s = entry.Open();
        await ms.CopyToAsync(s, ct);
    }

    public static async Task<T> ReadNestedAsync<T>(
        ZipArchive zip, string entryName, Func<ZipArchive, CancellationToken, Task<T>> read, CancellationToken ct)
    {
        var entry = zip.GetEntry(entryName)
            ?? throw new InvalidDataException($"Archive is missing {entryName}.");
        await using var s = entry.Open();
        using var ms = new MemoryStream();
        await s.CopyToAsync(ms, ct);
        ms.Position = 0;
        using var inner = new ZipArchive(ms, ZipArchiveMode.Read);
        return await read(inner, ct);
    }

    public static IEnumerable<string> EntryNamesUnder(ZipArchive zip, string prefix) =>
        zip.Entries.Where(e => e.FullName.StartsWith(prefix, StringComparison.Ordinal)
                            && e.FullName.Length > prefix.Length)
                   .Select(e => e.FullName);
}
