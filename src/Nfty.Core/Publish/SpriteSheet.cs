using System.Globalization;
using Nfty.Core.Output;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Nfty.Core.Publish;

/// <summary>
/// How a sheet is laid out: a grid of cells, in the Set's own canvas size.
/// </summary>
/// <param name="Columns">Cells across.</param>
/// <param name="Rows">Cells down.</param>
/// <param name="CellWidth">One cell's width — the Set's canvas width.</param>
/// <param name="CellHeight">One cell's height — the Set's canvas height.</param>
/// <remarks>
/// <b>The cell is the CANVAS, never a size the caller picks.</b> Every asset in a Set is the canvas
/// size by construction — the CookBook fixes it and <c>Validator</c> checks every variant against
/// it — so a cell that disagreed would mean either scaling the art or cropping it, and this project
/// resamples nothing anywhere. A sheet is a stitch, not a render.
/// </remarks>
public record SpriteSheetLayout(int Columns, int Rows, int CellWidth, int CellHeight)
{
    /// <summary>The finished sheet's width in pixels.</summary>
    public long Width => (long)Columns * CellWidth;

    /// <summary>The finished sheet's height in pixels.</summary>
    public long Height => (long)Rows * CellHeight;

    /// <summary>How many assets the grid holds.</summary>
    public long Capacity => (long)Columns * Rows;

    /// <summary>The sheet's pixel count, which is what the size limit is actually about.</summary>
    public long Pixels => Width * Height;

    /// <summary>The dimensions as a person reads them: <c>2048 x 2048</c>.</summary>
    /// <returns>The size as text, invariant like every other figure this product prints.</returns>
    public string SizeText() =>
        string.Create(CultureInfo.InvariantCulture, $"{Width} x {Height}");
}

/// <summary>How far a sheet has got.</summary>
/// <param name="Placed">Assets drawn onto the sheet so far.</param>
/// <param name="Total">Assets in the Set.</param>
/// <param name="Phase">What is happening, for a label.</param>
public record SpriteSheetProgress(int Placed, int Total, string Phase)
{
    /// <summary>Progress as a 0..1 fraction. Zero when there is nothing to do.</summary>
    public double Fraction => Total <= 0 ? 0 : (double)Placed / Total;
}

/// <summary>
/// Stitches a cooked Set's assets into one image, left to right and top to bottom.
/// </summary>
/// <remarks>
/// <para><b>The order is the Set's own numbering</b>, which is what makes a sheet addressable: cell
/// <c>n</c> is asset <c>n + 1</c>, so an engine indexing frames and a reader counting rows agree
/// without anything having to be recorded. <c>SetReader</c> orders by number rather than by
/// filename for the same reason.</para>
///
/// <para><b>It is built one asset at a time, never all of them at once.</b> A Set is a folder of
/// PNGs on disk and the sheet is the only large thing in memory here — decoding five hundred
/// thousand-pixel assets to composite them in one pass would cost gigabytes for an operation whose
/// output is a single image. Each asset is opened, drawn into its cell and disposed.</para>
///
/// <para><b>There is a hard pixel ceiling and it is not arbitrary.</b> A sheet is one contiguous
/// raster: a 500-asset Set at 1000px in a 23x23 grid is 23,000 square, which is 529 million pixels
/// and about 2 GB of RGBA before the encoder sees it. The limit is stated, checked before anything
/// is allocated, and reported with the numbers that would have to change — a refusal an author can
/// act on beats an <c>OutOfMemoryException</c> half a minute in.</para>
/// </remarks>
public static class SpriteSheet
{
    /// <summary>
    /// The largest sheet this will build, in pixels.
    /// </summary>
    /// <remarks>
    /// 16,384 square. Chosen because it is the maximum texture dimension on essentially every GPU
    /// that would consume a sheet, so a sheet past it is not merely large but unusable by the thing
    /// it exists for — and because at RGBA it is about 1 GB, which is already the outer edge of what
    /// is reasonable to hold for one image.
    /// </remarks>
    public const long MaxPixels = 16_384L * 16_384L;

    /// <summary>The longest side this will build, in pixels.</summary>
    /// <remarks>Checked separately from <see cref="MaxPixels"/>: a 1 x 400,000 strip is small in
    /// area and is still not an image anything can open.</remarks>
    public const int MaxSide = 16_384;

    /// <summary>
    /// The grid a Set falls into when nobody has chosen one: as square as it gets.
    /// </summary>
    /// <param name="count">How many assets.</param>
    /// <param name="cellWidth">The canvas width.</param>
    /// <param name="cellHeight">The canvas height.</param>
    /// <returns>A layout that holds every asset.</returns>
    /// <remarks>
    /// Square by COLUMN COUNT rather than by pixels, because a sheet is read as a grid of cells and
    /// a 4:1 canvas would otherwise produce a grid four cells wide. The last row is allowed to be
    /// short — that is what a spritesheet looks like — and the empty cells are transparent.
    /// </remarks>
    public static SpriteSheetLayout Fit(int count, int cellWidth, int cellHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellHeight);

        int columns = (int)Math.Ceiling(Math.Sqrt(count));
        int rows = (count + columns - 1) / columns;
        return new SpriteSheetLayout(columns, rows, cellWidth, cellHeight);
    }

    /// <summary>
    /// Why a layout cannot be built, or null when it can.
    /// </summary>
    /// <param name="layout">The grid.</param>
    /// <param name="count">How many assets have to fit in it.</param>
    /// <returns>A sentence to show the user, or null.</returns>
    /// <remarks>
    /// <b>Returns a problem rather than throwing</b>, the way <c>Validator</c> does and for the same
    /// reason: a dialog binds this live while someone is still typing into the row and column
    /// boxes, and a half-typed grid is a normal state to be in rather than an error to raise. The
    /// writer below throws on the same conditions, so a caller that skips the check still cannot
    /// produce a broken sheet.
    /// </remarks>
    public static string? ProblemWith(SpriteSheetLayout layout, int count)
    {
        ArgumentNullException.ThrowIfNull(layout);

        if (layout.Columns <= 0 || layout.Rows <= 0)
            return "A sheet needs at least one column and one row.";

        if (layout.Capacity < count)
            return string.Create(CultureInfo.InvariantCulture, $"{layout.Columns} x {layout.Rows} holds {layout.Capacity} assets, and this Set has {count}. Add rows or columns until the grid holds every one.");

        if (layout.Width > MaxSide || layout.Height > MaxSide)
            return string.Create(CultureInfo.InvariantCulture, $"{layout.SizeText()} is past the {MaxSide} pixel limit on a side. Most things that read a spritesheet cannot open one larger, so this is refused rather than built.");

        if (layout.Pixels > MaxPixels)
            return string.Create(CultureInfo.InvariantCulture, $"{layout.SizeText()} is {layout.Pixels / 1_000_000} megapixels, past the {MaxPixels / 1_000_000} megapixel limit. A sheet is one contiguous image and this one would not fit in memory.");

        return null;
    }

    /// <summary>
    /// <see cref="WriteAsync"/>, for a caller that is not on a UI thread.
    /// </summary>
    /// <param name="items">The Set's assets, in the order they should be placed.</param>
    /// <param name="layout">The grid.</param>
    /// <param name="destination">The <c>.png</c> to write.</param>
    /// <param name="progress">Reports one step per asset placed.</param>
    /// <param name="cancellationToken">Stops between assets.</param>
    /// <returns>The layout that was written.</returns>
    /// <exception cref="ArgumentException">The grid cannot hold the Set, or is too large to build.</exception>
    /// <remarks>
    /// <b>The async one is the real implementation and this blocks on it</b>, which is the opposite
    /// of <c>Generator.GenerateAsync</c> and right for the opposite reason: generation is CPU-bound
    /// with nothing to await, and this is PNG codec work against the disk for every asset in the
    /// collection. Blocking is safe here because nothing in Core captures a synchronization context
    /// — every await inside is <c>ConfigureAwait(false)</c> — so there is no context for a
    /// continuation to deadlock against.
    /// </remarks>
    public static SpriteSheetLayout Write(
        IReadOnlyList<SetItem> items,
        SpriteSheetLayout layout,
        string destination,
        IProgress<SpriteSheetProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        WriteAsync(items, layout, destination, progress, cancellationToken).GetAwaiter().GetResult();

    /// <summary>
    /// Stitches a Set into a sheet and writes it as a PNG.
    /// </summary>
    /// <param name="items">The Set's assets, in the order they should be placed.</param>
    /// <param name="layout">The grid.</param>
    /// <param name="destination">The <c>.png</c> to write.</param>
    /// <param name="progress">Reports one step per asset placed.</param>
    /// <param name="cancellationToken">Stops between assets.</param>
    /// <returns>The layout that was written.</returns>
    /// <exception cref="ArgumentException">The grid cannot hold the Set, or is too large to build.</exception>
    /// <remarks>
    /// <b>A missing or unreadable asset leaves its cell EMPTY rather than failing the sheet.</b> The
    /// Set browser already takes that position — a browser over a damaged Set shows the damage
    /// rather than refusing to open — and a sheet is more useful with a hole in it than not at all.
    /// The hole is visible, which is the point: a transparent cell among drawn ones is unmistakable,
    /// where a sheet that silently closed the gap would renumber every frame after it.
    /// </remarks>
    public static async Task<SpriteSheetLayout> WriteAsync(
        IReadOnlyList<SetItem> items,
        SpriteSheetLayout layout,
        string destination,
        IProgress<SpriteSheetProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        if (ProblemWith(layout, items.Count) is { } problem)
            throw new ArgumentException(problem, nameof(layout));

        progress?.Report(new SpriteSheetProgress(0, items.Count, "Stitching…"));

        using var sheet = new Image<Rgba32>((int)layout.Width, (int)layout.Height, new Rgba32(0, 0, 0, 0));

        // Sequential, and deliberately so. Every asset draws into the SAME image, which ImageSharp's
        // processing context is not safe to share across threads - and the work here is dominated by
        // decoding one PNG at a time off disk anyway. ParallelWork's own contract rules this out in
        // one line: every body must be a pure function of its own item, and these all write to one
        // accumulator.
        for (int i = 0; i < items.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int column = i % layout.Columns;
            int row = i / layout.Columns;
            var at = new Point(column * layout.CellWidth, row * layout.CellHeight);

            try
            {
                using var asset = await Image.LoadAsync<Rgba32>(items[i].ImagePath, cancellationToken)
                    .ConfigureAwait(false);
                sheet.Mutate(ctx => ctx.DrawImage(asset, at, 1f));
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException
                                           or UnknownImageFormatException or InvalidImageContentException)
            {
                // The cell stays transparent. See the remarks: a hole is information.
            }

            progress?.Report(new SpriteSheetProgress(i + 1, items.Count, "Stitching…"));
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new SpriteSheetProgress(items.Count, items.Count, "Encoding…"));

        string? dir = Path.GetDirectoryName(Path.GetFullPath(destination));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        await sheet.SaveAsync(destination, new PngEncoder(), cancellationToken).ConfigureAwait(false);

        return layout;
    }
}
