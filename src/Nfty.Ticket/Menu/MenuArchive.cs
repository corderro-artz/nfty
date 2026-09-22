using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nfty.Ticket;

/// <summary>
/// Reads a <c>.mnu</c> Menu: every weight, rule and colorization a rolled asset needs, plus its art,
/// with no imaging library involved.
/// </summary>
/// <remarks>
/// <para><b>This assembly decodes no pixels</b> — that is the whole point of <c>Nfty.Ticket</c> being
/// package-free. In atlas mode <see cref="ArtBytes"/> hands back the WHOLE atlas PNG's bytes and
/// <see cref="Cell"/> the rect inside it; the caller crops. In loose mode the "rect" is simply the
/// whole image, so a caller can treat both modes through one code path without ever asking which
/// mode a Menu was written in.</para>
///
/// <para><b>Owns the open archive.</b> <see cref="Open(string)"/> opens the file and keeps it open
/// for the life of this object — <see cref="ArtBytes"/> reads from it lazily rather than eagerly
/// decoding everything up front, which is what lets a host page through a large Menu's art without
/// holding it all in memory at once. Dispose frees the archive (and, for <see cref="Open(string)"/>,
/// the file it was opened from).</para>
/// </remarks>
public sealed class MenuArchive : IDisposable
{
    /// <summary>
    /// The wire policy this reader parses a Menu manifest with.
    /// </summary>
    /// <remarks>
    /// <para><b>A local copy, not a shared one</b> — <c>Nfty.Ticket</c> is deliberately package-free
    /// and cannot reference <c>Nfty.Core</c> (a runtime host that only needs to roll takes this
    /// assembly and nothing else), so it cannot reuse <c>Nfty.Core.Formats.Json.Options</c>, which
    /// <c>MenuWriter</c> (in <c>Nfty.Core.Publish</c>) writes with. <b>The two must agree</b> on the
    /// property naming policy and the enum string conversion — they are the two ends of one format,
    /// and a divergence there is a Menu that writes and does not read back.</para>
    ///
    /// <para><b>Deliberately narrower than <c>Json.Options</c> in one respect.</b> Core's options
    /// also set <c>RespectRequiredConstructorParameters</c>, which demands every constructor
    /// parameter without a C# default be present in the JSON. <see cref="MenuBook"/>'s
    /// <c>SourceCookBookSha256</c> sits second in its primary constructor — not last — so it cannot
    /// carry a default without reordering the parameter list, which would break every existing
    /// positional <c>new MenuBook(...)</c> call in this repo (including pre-existing tests) for a
    /// field that is legitimately absent whenever a book never came from a file. Left off for that
    /// reason; <see cref="JsonSerializerOptions.RespectNullableAnnotations"/> alone still rejects an
    /// explicit <c>null</c> for a genuinely non-nullable field, which is the defensive behavior that
    /// matters most for a hand-edited or truncated manifest.</para>
    /// </remarks>
    private static readonly JsonSerializerOptions WireOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        RespectNullableAnnotations = true,
    };

    private readonly ZipArchive _zip;
    private readonly Dictionary<string, MenuCell> _cellsByKey;
    private bool _disposed;

    /// <summary>The Menu's manifest: every weight, rule, colorization and cell rect.</summary>
    public MenuBook Book { get; }

    private MenuArchive(ZipArchive zip, MenuBook book)
    {
        _zip = zip;
        Book = book;
        _cellsByKey = new Dictionary<string, MenuCell>(StringComparer.Ordinal);
        foreach (var cell in book.Art.Cells) _cellsByKey[cell.Key] = cell;
    }

    /// <summary>Opens a <c>.mnu</c> file for reading.</summary>
    /// <param name="path">The Menu to open.</param>
    /// <returns>The open archive; the caller disposes it.</returns>
    /// <exception cref="NotSupportedException">The manifest declares a schema newer than this build
    /// reads.</exception>
    /// <exception cref="InvalidDataException">The file is not a readable Menu.</exception>
    public static MenuArchive Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Open(File.OpenRead(path));
    }

    /// <summary>Opens a <c>.mnu</c> from an already-open stream.</summary>
    /// <param name="stream">A seekable stream over the archive's bytes. Ownership passes to the
    /// returned <see cref="MenuArchive"/> — it is closed when that object is disposed.</param>
    /// <returns>The open archive; the caller disposes it.</returns>
    /// <exception cref="NotSupportedException">The manifest declares a schema newer than this build
    /// reads.</exception>
    /// <exception cref="InvalidDataException">The stream is not a readable Menu.</exception>
    public static MenuArchive Open(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        bool ok = false;
        try
        {
            var book = ReadBook(zip);
            var result = new MenuArchive(zip, book);
            ok = true;
            return result;
        }
        finally
        {
            if (!ok) zip.Dispose();
        }
    }

    private static MenuBook ReadBook(ZipArchive zip)
    {
        var entry = zip.GetEntry("manifest.json")
            ?? throw new InvalidDataException("Menu is missing manifest.json.");

        byte[] bytes;
        using (var s = entry.Open())
        using (var ms = new MemoryStream())
        {
            s.CopyTo(ms);
            bytes = ms.ToArray();
        }

        // A manifest written through Encoding.UTF8 (rather than a BOM-less UTF8Encoding) carries a
        // three-byte preamble Utf8JsonReader does not skip on its own; stripped here once so neither
        // the version peek nor the full parse below has to know about it.
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            bytes = bytes[3..];

        // The schema is gated BEFORE the strongly-typed parse, deliberately. A manifest declaring a
        // newer schema might not carry any of the fields MenuBook expects at all — a future version
        // is free to change what the file means — so checking the version first is what lets the
        // refusal say "too new" instead of a confusing JSON error about missing fields.
        int version = ReadSchemaVersion(bytes);
        if (!MenuSchema.IsSupported(version, MenuSchema.Current))
            throw new NotSupportedException(
                $"Menu manifest declares menuSchemaVersion {version}; this build of nfty supports "
                + $"up to {MenuSchema.Current}.");

        MenuBook? book;
        try
        {
            book = JsonSerializer.Deserialize<MenuBook>(bytes, WireOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"manifest.json is not a readable Menu: {ex.Message}", ex);
        }

        return book ?? throw new InvalidDataException("manifest.json deserialized to null.");
    }

    private static int ReadSchemaVersion(byte[] manifestBytes)
    {
        try
        {
            using var doc = JsonDocument.Parse(manifestBytes);
            if (!doc.RootElement.TryGetProperty("menuSchemaVersion", out var v))
                throw new InvalidDataException("manifest.json is missing menuSchemaVersion.");
            return v.GetInt32();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"manifest.json is not readable JSON: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// One cell's art, as raw PNG bytes.
    /// </summary>
    /// <param name="cellKey">A key from <see cref="MenuBook.CellKey"/>.</param>
    /// <returns>In atlas mode, the whole atlas PNG's bytes — use <see cref="Cell"/> to find where
    /// this cell sits inside them. In loose mode, that one variant's own PNG bytes in full.</returns>
    /// <exception cref="KeyNotFoundException">The Menu has no cell for <paramref name="cellKey"/>.</exception>
    /// <exception cref="InvalidDataException">The archive is missing the entry the manifest points
    /// at.</exception>
    public byte[] ArtBytes(string cellKey)
    {
        ArgumentNullException.ThrowIfNull(cellKey);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_cellsByKey.ContainsKey(cellKey))
            throw new KeyNotFoundException($"No cell for '{cellKey}'.");

        string entryName = Book.Art.Mode == MenuArtMode.Atlas ? "atlas.png" : "art/" + cellKey + ".png";
        var entry = _zip.GetEntry(entryName)
            ?? throw new InvalidDataException($"Menu is missing {entryName}.");

        using var s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Where <paramref name="cellKey"/>'s art sits inside the bytes <see cref="ArtBytes"/> returns.
    /// </summary>
    /// <param name="cellKey">A key from <see cref="MenuBook.CellKey"/>.</param>
    /// <returns>The cell rect — the whole image in loose mode, so a caller can treat both modes with
    /// one code path.</returns>
    /// <exception cref="KeyNotFoundException">The Menu has no cell for <paramref name="cellKey"/>.</exception>
    public MenuCell Cell(string cellKey)
    {
        ArgumentNullException.ThrowIfNull(cellKey);
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _cellsByKey.TryGetValue(cellKey, out var cell)
            ? cell
            : throw new KeyNotFoundException($"No cell for '{cellKey}'.");
    }

    /// <summary>Closes the underlying archive, and the file it was opened from.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _zip.Dispose();
    }
}
