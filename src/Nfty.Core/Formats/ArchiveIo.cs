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
        T? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<T>(s, Json.Options);
        }
        catch (JsonException ex)
        {
            // Includes the missing-required-member case that Json.Options' RespectNullableAnnotations
            // now raises. Reframed because the message is shown to a user verbatim (ErrorReport
            // prints ex.Message), and System.Text.Json's own phrasing talks about CLR types.
            throw new InvalidDataException($"manifest.json is not a readable {typeof(T).Name}: {ex.Message}", ex);
        }
        return Verified(manifest);
    }

    /// <summary>Shared tail of both manifest readers: reject a null document, then gate the schema
    /// version. Split out so the sync and async paths cannot drift on either check.</summary>
    private static T Verified<T>(T? manifest) where T : ISchemaVersioned
    {
        if (manifest is null) throw new InvalidDataException("manifest.json deserialized to null.");
        UnsupportedSchemaVersionException.Require(manifest);
        return manifest;
    }

    public static void WriteImage(ZipArchive zip, string name, Image<Rgba32> img)
    {
        var entry = zip.CreateEntry(name);
        using var s = entry.Open();
        img.Save(s, new PngEncoder());
    }

    public static Image<Rgba32> ReadImage(ZipArchive zip, string name) =>
        DecodeImage(ReadImageBytes(zip, name));

    /// <summary>
    /// Extracts one entry's bytes without decoding them.
    /// </summary>
    /// <param name="zip">The open archive.</param>
    /// <param name="name">The entry to read.</param>
    /// <returns>The entry's bytes.</returns>
    /// <remarks>
    /// Split from the decode because the two have opposite threading rules. A
    /// <see cref="ZipArchive"/>'s entries share one underlying stream, so it is <b>not</b>
    /// thread-safe and extraction must stay sequential; decoding a PNG from a byte array touches
    /// nothing shared and is where the time actually goes. Callers that read many images extract in
    /// order and then decode wide.
    /// </remarks>
    public static byte[] ReadImageBytes(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name) ?? throw new InvalidDataException($"Archive is missing {name}.");
        using var s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>Decodes PNG bytes. Pure, and safe to call from many threads at once.</summary>
    /// <param name="bytes">The encoded image.</param>
    /// <returns>The decoded image; the caller owns it.</returns>
    public static Image<Rgba32> DecodeImage(byte[] bytes) => Image.Load<Rgba32>(bytes);

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
        return ReadInner(ms, entryName, read);
    }

    /// <summary>
    /// Opens one nested archive's bytes and hands it to <paramref name="read"/>.
    ///
    /// <para>The failure is reframed because the framework's own is actively misleading here: a stray
    /// entry under <c>recipes/</c> — a README someone dropped in with 7-Zip, exercising the very
    /// property CLAUDE.md advertises, that "the custom extension is a renamed .zip, so any unzip tool
    /// can inspect it" — surfaces as <c>InvalidDataException: Central Directory corrupt</c>. That
    /// names no file and blames the OUTER archive's directory, which is intact. The user is told
    /// their CookBook is corrupt when one file inside it simply is not a Recipe.</para>
    /// </summary>
    private static T ReadInner<T>(MemoryStream bytes, string entryName, Func<ZipArchive, T> read)
    {
        ZipArchive inner;
        try { inner = new ZipArchive(bytes, ZipArchiveMode.Read); }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException(
                $"Entry '{entryName}' is not a readable nfty archive: {ex.Message} "
                + "(only nfty archives belong under this folder inside the file).", ex);
        }
        using (inner) return read(inner);
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
        T? manifest;
        try
        {
            manifest = await JsonSerializer.DeserializeAsync<T>(s, Json.Options, ct);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"manifest.json is not a readable {typeof(T).Name}: {ex.Message}", ex);
        }
        return Verified(manifest);
    }

    public static async Task WriteImageAsync(ZipArchive zip, string name, Image<Rgba32> img, CancellationToken ct)
    {
        var entry = zip.CreateEntry(name);
        await using var s = entry.Open();
        await img.SaveAsync(s, new PngEncoder(), ct);
    }

    public static async Task<Image<Rgba32>> ReadImageAsync(ZipArchive zip, string name, CancellationToken ct) =>
        DecodeImage(await ReadImageBytesAsync(zip, name, ct));

    /// <summary>Extracts one entry's bytes without decoding them.</summary>
    /// <param name="zip">The open archive.</param>
    /// <param name="name">The entry to read.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <returns>The entry's bytes.</returns>
    /// <inheritdoc cref="ReadImageBytes" path="/remarks"/>
    public static async Task<byte[]> ReadImageBytesAsync(ZipArchive zip, string name, CancellationToken ct)
    {
        var entry = zip.GetEntry(name) ?? throw new InvalidDataException($"Archive is missing {name}.");
        await using var s = entry.Open();
        using var ms = new MemoryStream();
        await s.CopyToAsync(ms, ct);
        return ms.ToArray();
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
        // Same reframing as the sync twin; see ReadInner for why "Central Directory corrupt" is the
        // wrong thing to tell someone who dropped a README into the archive.
        return await ReadInner(ms, entryName, inner => read(inner, ct));
    }

    public static IEnumerable<string> EntryNamesUnder(ZipArchive zip, string prefix) =>
        zip.Entries.Where(e => e.FullName.StartsWith(prefix, StringComparison.Ordinal)
                            && e.FullName.Length > prefix.Length)
                   .Select(e => e.FullName);
}
