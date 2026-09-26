using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nfty.Core.Model;

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
    /// reason.</para>
    ///
    /// <para><b><see cref="JsonSerializerOptions.RespectNullableAnnotations"/> does NOT cover a
    /// truncated manifest</b> — it rejects an explicit <c>null</c> for a non-nullable field, not an
    /// absent key, which a first draft of this comment claimed and was wrong about. Without
    /// <c>RespectRequiredConstructorParameters</c>, a manifest missing <c>canvas</c>, <c>recipes</c>,
    /// or any other required field simply deserializes with that member left at its CLR default
    /// (<c>null</c> for a reference type) and no exception — which is a Menu that opens fine and
    /// fails later with a <see cref="NullReferenceException"/> deep inside <c>Ticketer.Prepare</c>,
    /// nowhere near the actual problem. <see cref="ReadBook"/> closes that hole itself, explicitly,
    /// via <see cref="ValidateBook"/> — naming the missing member rather than letting the gap stand
    /// on the theory that nullable annotations alone were enough.</para>
    /// </remarks>
    private static readonly JsonSerializerOptions WireOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,

        // allowIntegerValues: false - the default JsonStringEnumConverter reads a raw integer
        // (e.g. "kind":1) just as readily as the named string, which would let a hand-edited or
        // corrupted manifest silently pick a different LayerKind/ColorModel/etc. than the one its
        // author wrote and typed as a name. Refused instead, the same way an unnamed value already
        // is.
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
        RespectNullableAnnotations = true,
    };

    private readonly ZipArchive _zip;
    private readonly Dictionary<string, MenuCell> _cellsByKey;
    private readonly Dictionary<string, Dictionary<string, MenuLayer>> _layersByRecipeId;
    private bool _disposed;

    /// <summary>The Menu's manifest: every weight, rule, colorization and cell rect.</summary>
    public MenuBook Book { get; }

    private MenuArchive(ZipArchive zip, MenuBook book)
    {
        _zip = zip;
        Book = book;
        _cellsByKey = new Dictionary<string, MenuCell>(StringComparer.Ordinal);
        foreach (var cell in book.Art.Cells) _cellsByKey[cell.Key] = cell;

        // Built once at Open, the same reasoning _cellsByKey already follows: a lookup a caller
        // (Plating.Plate, chiefly) will make once per layer per rolled asset must not re-scan the
        // recipe list and re-build a fresh dictionary on every call.
        _layersByRecipeId = new Dictionary<string, Dictionary<string, MenuLayer>>(StringComparer.Ordinal);
        foreach (var recipe in book.Recipes)
        {
            var byId = new Dictionary<string, MenuLayer>(StringComparer.Ordinal);
            foreach (var layer in recipe.Layers) byId[layer.Id] = layer;
            _layersByRecipeId[recipe.Id] = byId;
        }
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
        {
            // Below Oldest and above Current are different failures and read differently: a version
            // below Oldest is either a malformed manifest or a real, older Menu (schema 1, from
            // before depth maps and the 2D-only flag) written by a build of nfty this one no longer
            // reads - either way, "supports up to N" reads as an invitation to upgrade something
            // that might never have been valid at all, so that phrasing is reserved for the other
            // branch.
            string message = version < MenuSchema.Oldest
                ? $"Menu manifest declares menuSchemaVersion {version}, older than the oldest schema "
                    + $"this build of nfty reads ({MenuSchema.Oldest}); it is either malformed or was "
                    + "written by a build of nfty this one no longer reads."
                : $"Menu manifest declares menuSchemaVersion {version}; this build of nfty supports "
                    + $"up to {MenuSchema.Current}.";
            throw new NotSupportedException(message);
        }

        MenuBook? book;
        try
        {
            book = JsonSerializer.Deserialize<MenuBook>(bytes, WireOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"manifest.json is not a readable Menu: {ex.Message}", ex);
        }

        if (book is null) throw new InvalidDataException("manifest.json deserialized to null.");
        ValidateBook(book);
        return book;
    }

    /// <summary>
    /// Checks that every member <see cref="WireOptions"/> cannot enforce on its own is actually
    /// present, naming whichever one is not.
    /// </summary>
    /// <param name="book">The just-deserialized book.</param>
    /// <exception cref="InvalidDataException">A required member is missing or, for an atlas Menu,
    /// structurally empty.</exception>
    /// <remarks>
    /// <see cref="JsonSerializerOptions.RespectRequiredConstructorParameters"/> is deliberately off
    /// (see the remarks on <see cref="WireOptions"/>), which means <c>System.Text.Json</c> silently
    /// leaves a missing member at its CLR default instead of throwing. Every one of those defaults is
    /// distinguishable from a genuine value here — <c>null</c> for every reference-typed member, and
    /// zero for the two atlas dimensions, which a real atlas can never legitimately have — so this is
    /// a complete check, not a best-effort one, for exactly the members <see cref="MenuBook"/>
    /// declares.
    /// </remarks>
    private static void ValidateBook(MenuBook book)
    {
        if (book.Canvas is null)
            throw new InvalidDataException("Menu manifest is missing 'canvas'.");
        if (book.CollectionName is null)
            throw new InvalidDataException("Menu manifest is missing 'collectionName'.");
        if (book.CollectionSymbol is null)
            throw new InvalidDataException("Menu manifest is missing 'collectionSymbol'.");
        if (book.Recipes is null)
            throw new InvalidDataException("Menu manifest is missing 'recipes'.");
        if (book.Art is null)
            throw new InvalidDataException("Menu manifest is missing 'art'.");

        // Cells is required in BOTH modes - a loose Menu's rects are the whole image rather than a
        // sub-rect, but MenuWriter still writes one MenuCell per variant so a reader can treat both
        // modes through one code path (Cell() and ArtBytes() do not branch on Mode). A manifest
        // missing the key entirely deserializes this to null (RespectRequiredConstructorParameters
        // is off - see WireOptions), and MenuArchive's own constructor foreaches over it
        // unconditionally, so an unchecked null here reached the caller as a bare
        // NullReferenceException instead of a named refusal.
        if (book.Art.Cells is null)
            throw new InvalidDataException("Menu manifest's 'art' is missing 'cells'.");

        // Loose mode legitimately carries a zero-size atlas - every cell rect IS the whole image in
        // that mode, per MenuArt's own doc comment - so these two checks apply to atlas Menus only.
        if (book.Art.Mode == MenuArtMode.Atlas)
        {
            if (book.Art.AtlasWidth <= 0 || book.Art.AtlasHeight <= 0)
                throw new InvalidDataException(
                    "Menu manifest's 'art' is atlas mode but 'atlasWidth'/'atlasHeight' are not "
                    + "positive.");
            if (book.Art.Cells.Count == 0)
                throw new InvalidDataException("Menu manifest's 'art' is atlas mode but 'cells' is empty.");
        }

        // Per-recipe members a malformed manifest can omit the same way - each reached a bare
        // NullReferenceException downstream (MenuArchive's own constructor foreaches Layers;
        // AbsentPercentOf's dictionary lookup needs AbsentPercent; Ticketer walks LayerOrder and
        // Rules) rather than naming which recipe and which field was missing.
        foreach (var recipe in book.Recipes)
        {
            if (recipe.Id is null)
                throw new InvalidDataException("A recipe in the Menu manifest is missing 'id'.");
            if (recipe.LayerOrder is null)
                throw new InvalidDataException($"Recipe '{recipe.Id}' is missing 'layerOrder'.");
            if (recipe.Layers is null)
                throw new InvalidDataException($"Recipe '{recipe.Id}' is missing 'layers'.");
            if (recipe.Rules is null)
                throw new InvalidDataException($"Recipe '{recipe.Id}' is missing 'rules'.");
            if (recipe.AbsentPercent is null)
                throw new InvalidDataException($"Recipe '{recipe.Id}' is missing 'absentPercent'.");
        }
    }

    private static int ReadSchemaVersion(byte[] manifestBytes)
    {
        try
        {
            using var doc = JsonDocument.Parse(manifestBytes);
            if (!doc.RootElement.TryGetProperty("menuSchemaVersion", out var v))
                throw new InvalidDataException("manifest.json is missing menuSchemaVersion.");

            // v.GetInt32() on a non-number JsonElement throws InvalidOperationException, which the
            // JsonException catch below does not see - so a manifest carrying
            // "menuSchemaVersion":"1" (a string, not a number) reached the caller as a bare,
            // unnamed InvalidOperationException instead of the same InvalidDataException every
            // other malformed-manifest case throws. TryGetInt32 never throws: it reports false for
            // a non-number element and for a number with a fractional or out-of-range value alike.
            if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out int version))
                throw new InvalidDataException(
                    "manifest.json's menuSchemaVersion must be a whole number.");
            return version;
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

    /// <summary>
    /// One variant's depth map, when it has one.
    /// </summary>
    /// <param name="cellKey">A key from <see cref="MenuBook.CellKey"/>.</param>
    /// <returns><c>null</c> when the variant carries no depth data — "paper" for the Rise feature —
    /// otherwise a <see cref="DepthMap"/> sized to that cell's <see cref="MenuCell.Width"/> and
    /// <see cref="MenuCell.Height"/>.</returns>
    /// <exception cref="KeyNotFoundException">The Menu has no cell for <paramref name="cellKey"/> —
    /// the same check, and the same message, <see cref="Cell"/> makes.</exception>
    /// <exception cref="InvalidDataException">A stored depth entry's length disagrees with the
    /// cell's dimensions, naming the key.</exception>
    /// <remarks>
    /// <b>Read lazily, like <see cref="ArtBytes"/></b> — not eagerly at <see cref="Open(string)"/> —
    /// for the same reason: most variants carry no depth data at all, and this assembly stays
    /// package-free, so nothing here decodes a pixel either way; a <c>.dep</c> entry is already raw
    /// bytes in the exact layout <see cref="DepthMap.FromBytes"/> expects.
    /// </remarks>
    public DepthMap? Depth(string cellKey)
    {
        ArgumentNullException.ThrowIfNull(cellKey);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_cellsByKey.TryGetValue(cellKey, out var cell))
            throw new KeyNotFoundException($"No cell for '{cellKey}'.");

        var entry = _zip.GetEntry("depth/" + cellKey + ".dep");
        if (entry is null) return null;

        int expected = cell.Width * cell.Height * 2;

        // Refuse a malformed or hostile entry BEFORE it is inflated: entry.Length is the
        // uncompressed size from the central directory, known without reading a byte. The post-read
        // check just below stays as the authority — same exception, same message either way.
        if (entry.Length != expected)
            throw new InvalidDataException(
                $"Menu depth entry for '{cellKey}' is {entry.Length} bytes; its "
                + $"{cell.Width}x{cell.Height} cell needs {expected}.");

        byte[] bytes;
        using (var s = entry.Open())
        using (var ms = new MemoryStream())
        {
            s.CopyTo(ms);
            bytes = ms.ToArray();
        }

        if (bytes.Length != expected)
            throw new InvalidDataException(
                $"Menu depth entry for '{cellKey}' is {bytes.Length} bytes; its "
                + $"{cell.Width}x{cell.Height} cell needs {expected}.");

        return DepthMap.FromBytes(cell.Width, cell.Height, bytes);
    }

    /// <summary>
    /// One layer's definition — kind, colorization and variants — as the recipe it belongs to
    /// records it.
    /// </summary>
    /// <param name="recipeId">The recipe.</param>
    /// <param name="ingredientId">The layer within that recipe.</param>
    /// <returns>The layer, exactly as the Menu manifest carries it.</returns>
    /// <exception cref="KeyNotFoundException">The Menu has no recipe named <paramref name="recipeId"/>,
    /// or that recipe has no layer named <paramref name="ingredientId"/>. Both cases are reported by
    /// this single method — naming which of the two failed — so a caller (<c>Plating.Plate</c>,
    /// chiefly) never has to reach into <see cref="Book"/> itself to answer the question, and never
    /// hits the BCL's unnamed default message for either.</exception>
    public MenuLayer Layer(string recipeId, string ingredientId)
    {
        ArgumentNullException.ThrowIfNull(recipeId);
        ArgumentNullException.ThrowIfNull(ingredientId);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_layersByRecipeId.TryGetValue(recipeId, out var byId))
            throw new KeyNotFoundException($"No recipe '{recipeId}' in this Menu.");
        if (!byId.TryGetValue(ingredientId, out var layer))
            throw new KeyNotFoundException($"Recipe '{recipeId}' has no layer '{ingredientId}'.");
        return layer;
    }

    /// <summary>Closes the underlying archive, and the file it was opened from.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _zip.Dispose();
    }
}
