using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Nfty.Ticket;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Nfty.Core.Publish;

/// <summary>How <see cref="MenuWriter"/> packs a CookBook's art into a Menu.</summary>
public record MenuWriteOptions
{
    /// <summary>Atlas (the default) stitches every distinct variant image into one PNG, addressed
    /// by the rects the manifest records. Loose writes one PNG per variant, nested by recipe,
    /// ingredient and variant — the escape hatch for a book whose art would not fit one atlas.</summary>
    public MenuArtMode Art { get; init; } = MenuArtMode.Atlas;
}

/// <summary>How far a Menu write has got.</summary>
/// <param name="Written">Cells encoded, placed or written so far.</param>
/// <param name="Total">Cells in the book.</param>
/// <param name="Phase">What is happening, for a label.</param>
public record MenuWriteProgress(int Written, int Total, string Phase);

/// <summary>
/// Writes a CookBook's every weight, rule, colorization and art into a <c>.mnu</c> Menu — the file a
/// runtime host reads to roll and render an asset with no <c>.cbk</c> present.
/// </summary>
/// <remarks>
/// <para><b>Built on <see cref="MenuProjection"/>, never a second walk of the manifests.</b> That
/// type already resolves recipe weights, layer order, rules and absent percents correctly from a
/// <see cref="LoadedCookBook"/>; this fills in the one thing a projection for rolling alone does not
/// need — where the art is.</para>
///
/// <para><b>Identical art is packed once.</b> Two cell keys whose encoded PNG bytes hash the same
/// share one rect — the demo book's two chest recipes share several layers outright, and a Menu
/// should not pay for the same pixels twice. Placement is sorted by each shared image's own least
/// cell key, <see cref="StringComparer.Ordinal"/>, so the same book always produces the same atlas
/// regardless of the order the manifests happen to enumerate in.</para>
/// </remarks>
public static class MenuWriter
{
    private const string AtlasEntryName = "atlas.png";

    /// <summary>Writes a Menu. See <see cref="WriteAsync"/> for the real implementation.</summary>
    /// <param name="book">The book to project and pack. The caller keeps ownership.</param>
    /// <param name="path">The <c>.mnu</c> to write.</param>
    /// <param name="options">Atlas or loose.</param>
    /// <exception cref="InvalidOperationException">The atlas would exceed the pixel ceiling; retry
    /// with <see cref="MenuArtMode.Loose"/>.</exception>
    public static void Write(LoadedCookBook book, string path, MenuWriteOptions options) =>
        WriteAsync(book, path, options, null, default).GetAwaiter().GetResult();

    /// <summary>
    /// Writes a Menu, reporting progress and stopping when asked.
    /// </summary>
    /// <param name="book">The book to project and pack. The caller keeps ownership.</param>
    /// <param name="path">The <c>.mnu</c> to write.</param>
    /// <param name="options">Atlas or loose.</param>
    /// <param name="progress">One step per cell encoded, then per cell placed or written.</param>
    /// <param name="ct">Stops between cells.</param>
    /// <exception cref="InvalidOperationException">The atlas would exceed the pixel ceiling; retry
    /// with <see cref="MenuArtMode.Loose"/>.</exception>
    /// <remarks>
    /// <b>This is the real implementation and the sync twin blocks on it</b> — a Menu write is
    /// stream and PNG-codec work end to end, the same reasoning <c>SetExporter.ExportAsync</c> gives
    /// for an export. Blocking is safe because nothing in Core captures a synchronization context.
    /// </remarks>
    public static async Task WriteAsync(LoadedCookBook book, string path, MenuWriteOptions options,
        IProgress<MenuWriteProgress>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(options);

        var menuBook = MenuProjection.From(book);

        int total = book.Recipes.Sum(r => r.Ingredients.Sum(i => i.Manifest.Variants.Count));

        // Encode every variant to PNG bytes exactly once, keyed by its cell key.
        var encoded = new Dictionary<string, byte[]>(total, StringComparer.Ordinal);
        int written = 0;
        foreach (var recipe in book.Recipes)
        {
            foreach (var ing in recipe.Ingredients)
            {
                foreach (var v in ing.Manifest.Variants)
                {
                    ct.ThrowIfCancellationRequested();
                    string key = MenuBook.CellKey(recipe.Manifest.Id, ing.Manifest.Id, v.Id);
                    var img = ing.VariantImages[v.Id];
                    using var ms = new MemoryStream();
                    await img.SaveAsync(ms, new PngEncoder(), ct).ConfigureAwait(false);
                    encoded[key] = ms.ToArray();
                    written++;
                    progress?.Report(new MenuWriteProgress(written, total, "Encoding art…"));
                }
            }
        }

        // Group cell keys by the SHA-256 of their encoded bytes, so identical art shares one rect.
        var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (key, bytes) in encoded)
        {
            string hash = Convert.ToHexString(SHA256.HashData(bytes));
            if (!groups.TryGetValue(hash, out var keys))
                groups[hash] = keys = new List<string>();
            keys.Add(key);
        }

        // Ordered by each group's own least key, ordinal - independent of dictionary iteration
        // order, so the same book always produces the same atlas.
        var ordered = groups.Values
            // Every group has at least one key, so Min cannot really return null here - the bang
            // is for the nullable-aware LINQ overload, not for a case that can occur.
            .Select(keys => (FirstKey: keys.Min(StringComparer.Ordinal)!, Keys: (IReadOnlyList<string>)keys))
            .OrderBy(g => g.FirstKey, StringComparer.Ordinal)
            .ToList();

        MenuArt art;
        byte[]? atlasBytes = null;
        if (options.Art == MenuArtMode.Atlas)
        {
            (art, atlasBytes) = await PackAtlasAsync(menuBook.Canvas, ordered, encoded, progress, ct)
                .ConfigureAwait(false);
        }
        else
        {
            art = PackLoose(menuBook.Canvas, ordered);
        }

        var finished = menuBook with { Art = art };

        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        await using (var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var zip = new ZipArchive(fileStream, ZipArchiveMode.Create))
        {
            await ArchiveIo.WriteManifestAsync(zip, finished, ct).ConfigureAwait(false);

            if (options.Art == MenuArtMode.Atlas)
            {
                var entry = zip.CreateEntry(AtlasEntryName);
                await using var s = entry.Open();
                await s.WriteAsync(atlasBytes!, ct).ConfigureAwait(false);
                progress?.Report(new MenuWriteProgress(total, total, "Writing…"));
            }
            else
            {
                int placed = 0;
                foreach (var g in ordered)
                {
                    byte[] bytes = encoded[g.FirstKey];
                    foreach (var key in g.Keys)
                    {
                        ct.ThrowIfCancellationRequested();
                        var entry = zip.CreateEntry("art/" + key + ".png");
                        await using var s = entry.Open();
                        await s.WriteAsync(bytes, ct).ConfigureAwait(false);
                        placed++;
                        progress?.Report(new MenuWriteProgress(placed, total, "Writing…"));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Refuses an atlas past the pixel ceiling before anything is allocated.
    /// </summary>
    /// <param name="columns">Cells across.</param>
    /// <param name="rows">Cells down.</param>
    /// <param name="cellWidth">One cell's width — the book's canvas width.</param>
    /// <param name="cellHeight">One cell's height — the book's canvas height.</param>
    /// <exception cref="InvalidOperationException">The grid is past
    /// <see cref="Publish.SpriteSheet.MaxSide"/> on a side, or past
    /// <see cref="Publish.SpriteSheet.MaxPixels"/> in area.</exception>
    /// <remarks>
    /// <b>Public so the refusal is testable without allocating a huge atlas</b> — the same reasoning
    /// <c>SpriteSheet.ProblemWith</c> is checked before <c>SpriteSheet.WriteAsync</c> allocates its
    /// sheet. It reuses that type's constants and reasoning directly: a Menu atlas is one contiguous
    /// raster exactly like a spritesheet, so the same ceiling applies for the same reasons.
    /// </remarks>
    public static void CheckAtlasFits(int columns, int rows, int cellWidth, int cellHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(columns);
        ArgumentOutOfRangeException.ThrowIfNegative(rows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellHeight);

        long width = (long)columns * cellWidth;
        long height = (long)rows * cellHeight;
        string size = string.Create(CultureInfo.InvariantCulture, $"{width} x {height}");

        if (width > SpriteSheet.MaxSide || height > SpriteSheet.MaxSide)
            throw new InvalidOperationException(
                $"A Menu atlas of {size} is past the {SpriteSheet.MaxSide} pixel limit on a side. "
                + "Most things that would load an atlas texture cannot open one larger, so this is "
                + "refused rather than built — write with --art loose instead.");

        long pixels = width * height;
        if (pixels > SpriteSheet.MaxPixels)
            throw new InvalidOperationException(
                $"A Menu atlas of {size} is {pixels / 1_000_000} megapixels, past the "
                + $"{SpriteSheet.MaxPixels / 1_000_000} megapixel limit. An atlas is one contiguous "
                + "image and this one would not fit in memory — write with --art loose instead.");
    }

    private static async Task<(MenuArt Art, byte[] AtlasBytes)> PackAtlasAsync(
        Dimensions canvas,
        List<(string FirstKey, IReadOnlyList<string> Keys)> ordered,
        IReadOnlyDictionary<string, byte[]> encoded,
        IProgress<MenuWriteProgress>? progress,
        CancellationToken ct)
    {
        int n = ordered.Count;
        int total = ordered.Sum(g => g.Keys.Count);

        if (n == 0)
        {
            using var empty = new Image<Rgba32>(1, 1, new Rgba32(0, 0, 0, 0));
            using var emptyStream = new MemoryStream();
            await empty.SaveAsync(emptyStream, new PngEncoder(), ct).ConfigureAwait(false);
            return (new MenuArt(MenuArtMode.Atlas, 1, 1, []), emptyStream.ToArray());
        }

        int columns = (int)Math.Ceiling(Math.Sqrt(n));
        int rows = (n + columns - 1) / columns;
        CheckAtlasFits(columns, rows, canvas.Width, canvas.Height);

        var cells = new List<MenuCell>(total);
        using (var atlas = new Image<Rgba32>(columns * canvas.Width, rows * canvas.Height,
            new Rgba32(0, 0, 0, 0)))
        {
            for (int i = 0; i < ordered.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                int column = i % columns;
                int row = i / columns;
                int x = column * canvas.Width;
                int y = row * canvas.Height;

                using (var img = Image.Load<Rgba32>(encoded[ordered[i].FirstKey]))
                    atlas.Mutate(ctx => ctx.DrawImage(img, new Point(x, y), 1f));

                foreach (var key in ordered[i].Keys)
                    cells.Add(new MenuCell(key, x, y, canvas.Width, canvas.Height));

                progress?.Report(new MenuWriteProgress(cells.Count, total, "Placing…"));
            }

            using var atlasStream = new MemoryStream();
            await atlas.SaveAsync(atlasStream, new PngEncoder(), ct).ConfigureAwait(false);

            var sortedCells = cells.OrderBy(c => c.Key, StringComparer.Ordinal).ToList();
            return (new MenuArt(MenuArtMode.Atlas, atlas.Width, atlas.Height, sortedCells),
                atlasStream.ToArray());
        }
    }

    private static MenuArt PackLoose(
        Dimensions canvas, List<(string FirstKey, IReadOnlyList<string> Keys)> ordered)
    {
        var cells = new List<MenuCell>();
        foreach (var g in ordered)
            foreach (var key in g.Keys)
                cells.Add(new MenuCell(key, 0, 0, canvas.Width, canvas.Height));

        var sorted = cells.OrderBy(c => c.Key, StringComparer.Ordinal).ToList();
        return new MenuArt(MenuArtMode.Loose, 0, 0, sorted);
    }
}
