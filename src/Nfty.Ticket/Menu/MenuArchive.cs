using System.IO.Compression;
using Nfty.Core.Model;

namespace Nfty.Ticket;

/// <summary>
/// Reads a <c>.mnu</c> Menu: every weight, rule and colorization a rolled asset needs, plus its art,
/// with no imaging library involved.
/// </summary>
/// <remarks>
/// <para><b>This assembly decodes no pixels</b> â€” that is the whole point of <c>Nfty.Ticket</c> being
/// package-free. In atlas mode <see cref="ArtBytes"/> hands back the WHOLE atlas PNG's bytes and
/// <see cref="Cell"/> the rect inside it; the caller crops. In loose mode the "rect" is simply the
/// whole image, so a caller can treat both modes through one code path without ever asking which
/// mode a Menu was written in.</para>
///
/// <para><b>Owns the open archive.</b> <see cref="Open(string)"/> opens the file and keeps it open
/// for the life of this object â€” <see cref="ArtBytes"/> reads from it lazily rather than eagerly
/// decoding everything up front, which is what lets a host page through a large Menu's art without
/// holding it all in memory at once. Dispose frees the archive (and, for <see cref="Open(string)"/>,
/// the file it was opened from).</para>
/// <para>The stream is also closed when opening fails, including ZIP construction. Instance reads
/// are not safe to run concurrently: lazy entries share the archive's underlying stream.</para>
/// </remarks>
public sealed class MenuArchive : IDisposable
{
    // MenuJson reads the manifest on both targets without a JSON package. Required members are
    // checked while binding; nullable source/color fields and partialAlpha retain their defaults.
    // MenuWriter uses Json.Options; naming and enum spelling must agree at both ends.

    private readonly ZipArchive _zip;
    private readonly Dictionary<string, MenuCell> _cellsByKey;
    private readonly Dictionary<string, MenuRecipe> _recipesById;
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
        _recipesById = Lookup.ById(book.Recipes, r => r.Id);

        // Built once at Open, the same reasoning _cellsByKey already follows: a lookup a caller
        // (Plating.Plate, chiefly) will make once per layer per rolled asset must not re-scan the
        // recipe list and re-build a fresh dictionary on every call.
        _layersByRecipeId = new Dictionary<string, Dictionary<string, MenuLayer>>(StringComparer.Ordinal);
        foreach (var recipe in book.Recipes)
        {
            _layersByRecipeId[recipe.Id] = Lookup.ById(recipe.Layers, l => l.Id);
        }
    }

    /// <summary>Opens a <c>.mnu</c> file for reading.</summary>
    /// <param name="path">The Menu to open.</param>
    /// <returns>The open archive; the caller disposes it.</returns>
    /// <exception cref="NotSupportedException">The manifest declares a schema outside
    /// <see cref="MenuSchema.Oldest"/> through <see cref="MenuSchema.Current"/>.</exception>
    /// <exception cref="InvalidDataException">The file is not a readable Menu.</exception>
    public static MenuArchive Open(string path)
    {
        Guard.NotBlank(path, nameof(path));
        return Open(File.OpenRead(path));
    }

    /// <summary>Opens a <c>.mnu</c> from an already-open stream.</summary>
    /// <param name="stream">A seekable stream over the archive's bytes. Ownership passes to the
    /// returned <see cref="MenuArchive"/> â€” it is closed when that object is disposed, or immediately
    /// if opening fails.</param>
    /// <returns>The open archive; the caller disposes it.</returns>
    /// <exception cref="NotSupportedException">The manifest declares a schema outside
    /// <see cref="MenuSchema.Oldest"/> through <see cref="MenuSchema.Current"/>.</exception>
    /// <exception cref="InvalidDataException">The stream is not a readable Menu.</exception>
    public static MenuArchive Open(Stream stream)
    {
        Guard.NotNull(stream, nameof(stream));

        ZipArchive? zip = null;
        try
        {
            zip = new ZipArchive(stream, ZipArchiveMode.Read);
            var book = ReadBook(zip);
            return new MenuArchive(zip, book);
        }
        catch
        {
            if (zip is null) stream.Dispose();
            else zip.Dispose();
            throw;
        }
    }

    private static MenuBook ReadBook(ZipArchive zip)
    {
        var entry = zip.GetEntry("manifest.json")
            ?? throw new InvalidDataException("Menu is missing manifest.json.");

        byte[] bytes = ReadBytes(entry);

        // A manifest written through Encoding.UTF8 (rather than a BOM-less UTF8Encoding) carries a
        // three-byte preamble the reader would otherwise see as text; stripped here once so neither
        // the version peek nor the full parse below has to know about it.
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            bytes = bytes.AsSpan(3).ToArray();

        // The schema is gated BEFORE the strongly-typed parse, deliberately. A manifest declaring a
        // newer schema might not carry any of the fields MenuBook expects at all â€” a future version
        // is free to change what the file means â€” so checking the version first is what lets the
        // refusal say "too new" instead of a confusing JSON error about missing fields.
        object? root;
        try { root = MenuJson.Parse(bytes); }
        catch (MenuJsonException ex)
        {
            throw new InvalidDataException($"manifest.json is not readable JSON: {ex.Message}", ex);
        }
        int version = ReadSchemaVersion(root);
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
            book = MenuJson.Book(root);
        }
        catch (MenuJsonException ex)
        {
            throw new InvalidDataException($"manifest.json is not a readable Menu: {ex.Message}", ex);
        }

        if (book is null) throw new InvalidDataException("manifest.json deserialized to null.");
        MenuValidation.Validate(book);
        return book;
    }

    private static int ReadSchemaVersion(object? root)
    {
        if (root is not Dictionary<string, object?>)
            throw new InvalidDataException("manifest.json must contain a Menu object.");
        int? version = MenuJson.SchemaVersion(root, out bool present);
        if (!present)
            throw new InvalidDataException("manifest.json is missing menuSchemaVersion.");
        // A string, a fraction or an out-of-range number is a named malformed-manifest refusal.
        return version ?? throw new InvalidDataException(
            "manifest.json's menuSchemaVersion must be a whole number.");
    }

    /// <summary>
    /// One cell's art, as raw PNG bytes.
    /// </summary>
    /// <param name="cellKey">A key from <see cref="MenuBook.CellKey"/>.</param>
    /// <returns>In atlas mode, the whole atlas PNG's bytes â€” use <see cref="Cell"/> to find where
    /// this cell sits inside them. In loose mode, that one variant's own PNG bytes in full.</returns>
    /// <exception cref="KeyNotFoundException">The Menu has no cell for <paramref name="cellKey"/>.</exception>
    /// <exception cref="InvalidDataException">The archive is missing the entry the manifest points
    /// at.</exception>
    public byte[] ArtBytes(string cellKey)
    {
        Guard.NotNull(cellKey, nameof(cellKey));
        Guard.NotDisposed(_disposed, this);

        if (!_cellsByKey.ContainsKey(cellKey))
            throw new KeyNotFoundException($"No cell for '{cellKey}'.");

        string entryName = Book.Art.Mode == MenuArtMode.Atlas ? "atlas.png" : "art/" + cellKey + ".png";
        var entry = _zip.GetEntry(entryName)
            ?? throw new InvalidDataException($"Menu is missing {entryName}.");

        return ReadBytes(entry);
    }

    /// <summary>
    /// Where <paramref name="cellKey"/>'s art sits inside the bytes <see cref="ArtBytes"/> returns.
    /// </summary>
    /// <param name="cellKey">A key from <see cref="MenuBook.CellKey"/>.</param>
    /// <returns>The cell rect â€” the whole image in loose mode, so a caller can treat both modes with
    /// one code path.</returns>
    /// <exception cref="KeyNotFoundException">The Menu has no cell for <paramref name="cellKey"/>.</exception>
    public MenuCell Cell(string cellKey)
    {
        Guard.NotNull(cellKey, nameof(cellKey));
        Guard.NotDisposed(_disposed, this);

        return _cellsByKey.TryGetValue(cellKey, out var cell)
            ? cell
            : throw new KeyNotFoundException($"No cell for '{cellKey}'.");
    }

    /// <summary>
    /// One variant's depth map, when it has one.
    /// </summary>
    /// <param name="cellKey">A key from <see cref="MenuBook.CellKey"/>.</param>
    /// <returns><c>null</c> when the variant carries no depth data â€” "paper" for the Rise feature â€”
    /// otherwise a <see cref="DepthMap"/> sized to that cell's <see cref="MenuCell.Width"/> and
    /// <see cref="MenuCell.Height"/>.</returns>
    /// <exception cref="KeyNotFoundException">The Menu has no cell for <paramref name="cellKey"/> â€”
    /// the same check, and the same message, <see cref="Cell"/> makes.</exception>
    /// <exception cref="InvalidDataException">A stored depth entry's length disagrees with the
    /// cell's dimensions, naming the key.</exception>
    /// <remarks>
    /// <b>Read lazily, like <see cref="ArtBytes"/></b> â€” not eagerly at <see cref="Open(string)"/> â€”
    /// for the same reason: most variants carry no depth data at all, and this assembly stays
    /// package-free, so nothing here decodes a pixel either way; a <c>.dep</c> entry is already raw
    /// bytes in the exact layout <see cref="DepthMap.FromBytes"/> expects.
    /// </remarks>
    public DepthMap? Depth(string cellKey)
    {
        Guard.NotNull(cellKey, nameof(cellKey));
        Guard.NotDisposed(_disposed, this);

        if (!_cellsByKey.TryGetValue(cellKey, out var cell))
            throw new KeyNotFoundException($"No cell for '{cellKey}'.");

        var entry = _zip.GetEntry("depth/" + cellKey + ".dep");
        if (entry is null) return null;

        long expected = (long)cell.Width * cell.Height * 2;

        // Refuse a malformed or hostile entry BEFORE it is inflated: entry.Length is the
        // uncompressed size from the central directory, known without reading a byte. The post-read
        // check just below stays as the authority â€” same exception, same message either way.
        if (entry.Length != expected)
            throw new InvalidDataException(
                $"Menu depth entry for '{cellKey}' is {entry.Length} bytes; its "
                + $"{cell.Width}x{cell.Height} cell needs {expected}.");

        byte[] bytes = ReadBytes(entry);

        if (bytes.Length != expected)
            throw new InvalidDataException(
                $"Menu depth entry for '{cellKey}' is {bytes.Length} bytes; its "
                + $"{cell.Width}x{cell.Height} cell needs {expected}.");

        return DepthMap.FromBytes(cell.Width, cell.Height, bytes);
    }

    private static byte[] ReadBytes(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }

    internal MenuRecipe Recipe(string recipeId)
    {
        Guard.NotNull(recipeId, nameof(recipeId));
        Guard.NotDisposed(_disposed, this);
        return _recipesById.TryGetValue(recipeId, out var recipe)
            ? recipe : throw new KeyNotFoundException($"No recipe '{recipeId}' in this Menu.");
    }

    /// <summary>
    /// One layer's definition â€” kind, colorization and variants â€” as the recipe it belongs to
    /// records it.
    /// </summary>
    /// <param name="recipeId">The recipe.</param>
    /// <param name="ingredientId">The layer within that recipe.</param>
    /// <returns>The layer, exactly as the Menu manifest carries it.</returns>
    /// <exception cref="KeyNotFoundException">The Menu has no recipe named <paramref name="recipeId"/>,
    /// or that recipe has no layer named <paramref name="ingredientId"/>. Both cases are reported by
    /// this single method â€” naming which of the two failed â€” so a caller (<c>Plating.Plate</c>,
    /// chiefly) never has to reach into <see cref="Book"/> itself to answer the question, and never
    /// hits the BCL's unnamed default message for either.</exception>
    public MenuLayer Layer(string recipeId, string ingredientId)
    {
        Guard.NotNull(recipeId, nameof(recipeId));
        Guard.NotNull(ingredientId, nameof(ingredientId));
        Guard.NotDisposed(_disposed, this);

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
