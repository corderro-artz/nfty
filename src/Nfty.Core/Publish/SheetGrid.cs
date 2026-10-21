using System.Globalization;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Png.Chunks;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Publish;

/// <summary>
/// The grid a spritesheet is made of, as a sheet carries it: written into the PNG itself.
/// </summary>
/// <param name="Columns">Cells across.</param>
/// <param name="Rows">Cells down.</param>
/// <param name="CellWidth">One cell's width in pixels.</param>
/// <param name="CellHeight">One cell's height in pixels.</param>
/// <param name="Count">How many cells hold a picture, in reading order; the rest are empty.</param>
/// <remarks>
/// <para><b>The grid travels IN the file</b>, as a <c>tEXt</c> chunk named <c>nfty-grid</c>, so a
/// sheet handed to somebody else (or reopened next week) still knows how to slice itself with
/// nothing recorded beside it. Readers that do not know the chunk ignore it, as the PNG spec
/// requires of any ancillary chunk.</para>
/// <para><b>The text is parsed leniently on keys and strictly on values.</b> Unknown keys are
/// ignored so a later build can add one without making this one refuse the sheet; every number
/// must be a positive invariant integer, and a count past the grid's capacity is malformed.</para>
/// </remarks>
public sealed record SheetGrid(int Columns, int Rows, int CellWidth, int CellHeight, int Count)
{
    /// <summary>The PNG text keyword the grid is stored under.</summary>
    public const string ChunkKeyword = "nfty-grid";

    /// <summary>The grid as chunk text: <c>cols=8;rows=4;cell=64x64;count=29</c>.</summary>
    /// <returns>The text, invariant, with the keys in this fixed order so the bytes never vary.</returns>
    public string ToChunkText() =>
        string.Create(CultureInfo.InvariantCulture,
            $"cols={Columns};rows={Rows};cell={CellWidth}x{CellHeight};count={Count}");

    /// <summary>Reads chunk text back into a grid.</summary>
    /// <param name="text">The chunk text.</param>
    /// <returns>The grid, or null when anything is missing or malformed.</returns>
    public static SheetGrid? Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        int? cols = null, rows = null, count = null, cw = null, ch = null;
        foreach (string part in text.Split(';'))
        {
            int eq = part.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0) continue;
            string key = part[..eq], value = part[(eq + 1)..];
            switch (key)
            {
                case "cols": cols = PositiveInt(value); if (cols is null) return null; break;
                case "rows": rows = PositiveInt(value); if (rows is null) return null; break;
                case "count": count = PositiveInt(value); if (count is null) return null; break;
                case "cell":
                    string[] wh = value.Split('x');
                    if (wh.Length != 2) return null;
                    cw = PositiveInt(wh[0]);
                    ch = PositiveInt(wh[1]);
                    if (cw is null || ch is null) return null;
                    break;
            }
        }

        if (cols is null || rows is null || count is null || cw is null || ch is null) return null;
        if ((long)count.Value > (long)cols.Value * rows.Value) return null;
        return new SheetGrid(cols.Value, rows.Value, cw.Value, ch.Value, count.Value);
    }

    private static int? PositiveInt(string s) =>
        int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n > 0 ? n : null;

    /// <summary>Reads the grid a PNG carries, without decoding its pixels.</summary>
    /// <param name="pngPath">The sheet.</param>
    /// <returns>The grid, or null when the file has no <c>nfty-grid</c> entry, it is malformed, or the file is not an image.</returns>
    public static SheetGrid? Read(string pngPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pngPath);
        try
        {
            var info = Image.Identify(pngPath);
            var entry = info.Metadata.GetPngMetadata().TextData
                .FirstOrDefault(t => t.Keyword == ChunkKeyword);
            return entry.Keyword is null ? null : Parse(entry.Value);
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Finds a grid in a picture that does not say: equal cells separated by transparent gutters.
    /// </summary>
    /// <param name="image">The picture.</param>
    /// <returns>The grid, or null when the picture is empty or is one picture (a 1 x 1 grid).</returns>
    /// <remarks>
    /// Per axis, the SMALLEST divisor of the side such that no run of content (a maximal span of
    /// columns, or rows, holding any pixel with alpha above zero) crosses a multiple of it. Smallest,
    /// not largest: the largest divisor is always the whole side, which always "works" and says
    /// nothing. Equal cells hold by construction. <c>Count</c> runs to the last cell holding content
    /// in reading order, so a short last row is counted as it was drawn.
    /// </remarks>
    public static SheetGrid? Detect(Image<Rgba32> image)
    {
        ArgumentNullException.ThrowIfNull(image);

        int w = image.Width, h = image.Height;
        var colHas = new bool[w];
        var rowHas = new bool[h];
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < h; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < w; x++)
                {
                    if (row[x].A == 0) continue;
                    colHas[x] = true;
                    rowHas[y] = true;
                }
            }
        });

        if (!rowHas.Contains(true)) return null;

        int cw = SmallestClearDivisor(colHas);
        int ch = SmallestClearDivisor(rowHas);
        int cols = w / cw, rows = h / ch;
        if (cols == 1 && rows == 1) return null;

        int last = -1;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < h; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < w; x++)
                {
                    if (row[x].A == 0) continue;
                    int index = y / ch * cols + x / cw;
                    if (index > last) last = index;
                }
            }
        });

        return new SheetGrid(cols, rows, cw, ch, last + 1);
    }

    private static int SmallestClearDivisor(bool[] has)
    {
        int side = has.Length;
        for (int c = 1; c < side; c++)
        {
            if (side % c != 0) continue;

            bool clear = true;
            for (int i = 0; i < side && clear; i++)
            {
                // A run crosses a multiple of c exactly when two adjacent content positions
                // sit on either side of one.
                if (i % c == 0 && i > 0 && has[i] && has[i - 1]) clear = false;
            }

            if (clear) return c;
        }

        return side;
    }

    /// <summary>Why a grid does not tile a picture exactly, or null when it does.</summary>
    /// <param name="grid">The grid.</param>
    /// <param name="width">The picture's width.</param>
    /// <param name="height">The picture's height.</param>
    /// <returns>A sentence naming both numbers, or null.</returns>
    /// <remarks>Returns a problem rather than throwing, like <c>Validator</c>: a dialog binds this
    /// while someone is still typing a column count.</remarks>
    public static string? ProblemWith(SheetGrid grid, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var inv = CultureInfo.InvariantCulture;

        if (grid.Columns <= 0 || grid.Rows <= 0 || grid.CellWidth <= 0 || grid.CellHeight <= 0)
            return "A grid needs at least one column and one row of cells with a size.";
        if (width % grid.Columns != 0)
            return string.Create(inv, $"{width} px does not divide into {grid.Columns} columns");
        if (height % grid.Rows != 0)
            return string.Create(inv, $"{height} px does not divide into {grid.Rows} rows");
        if (width / grid.Columns != grid.CellWidth || height / grid.Rows != grid.CellHeight)
            return string.Create(inv, $"{grid.Columns} x {grid.Rows} cells of {grid.CellWidth} x {grid.CellHeight} px do not fill {width} x {height} px");
        return null;
    }

    /// <summary>A grid from a column and row count, every cell filled.</summary>
    /// <param name="columns">Cells across.</param>
    /// <param name="rows">Cells down.</param>
    /// <param name="width">The picture's width.</param>
    /// <param name="height">The picture's height.</param>
    /// <returns>The grid, with <c>Count</c> = columns x rows.</returns>
    /// <exception cref="ArgumentException">The grid does not divide the picture; the message says how.</exception>
    public static SheetGrid FromColumnsRows(int columns, int rows, int width, int height)
    {
        if (columns <= 0 || rows <= 0)
            throw new ArgumentException("A grid needs at least one column and one row.");
        if (width % columns != 0)
            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"{width} px does not divide into {columns} columns"));
        if (height % rows != 0)
            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"{height} px does not divide into {rows} rows"));
        return new SheetGrid(columns, rows, width / columns, height / rows, columns * rows);
    }

    /// <summary>A grid from a cell size, every cell filled.</summary>
    /// <param name="cellWidth">One cell's width.</param>
    /// <param name="cellHeight">One cell's height.</param>
    /// <param name="width">The picture's width.</param>
    /// <param name="height">The picture's height.</param>
    /// <returns>The grid, with <c>Count</c> = columns x rows.</returns>
    /// <exception cref="ArgumentException">The cell size does not divide the picture; the message says how.</exception>
    public static SheetGrid FromCellSize(int cellWidth, int cellHeight, int width, int height)
    {
        if (cellWidth <= 0 || cellHeight <= 0)
            throw new ArgumentException("A cell needs a width and a height.");
        if (width % cellWidth != 0)
            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"{width} px does not divide into cells {cellWidth} px wide"));
        if (height % cellHeight != 0)
            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"{height} px does not divide into cells {cellHeight} px tall"));
        int cols = width / cellWidth, rows = height / cellHeight;
        return new SheetGrid(cols, rows, cellWidth, cellHeight, cols * rows);
    }

    /// <summary>Writes a grid into an existing PNG, keeping every pixel exactly.</summary>
    /// <param name="pngPath">The sheet to rewrite.</param>
    /// <param name="grid">The grid to record.</param>
    /// <returns>How many bytes the file grew by; negative when it shrank.</returns>
    /// <remarks>
    /// Loads WITHOUT naming a pixel type, so a 16-bit or palette sheet is re-encoded at its own depth
    /// instead of being rounded to 8-bit. Saves beside the file and moves over it, so a failure
    /// leaves the original untouched; the temp is removed either way. I/O errors propagate: the
    /// caller decides whether "could not write" means "keep the grid for the session".
    /// </remarks>
    public static long WriteInto(string pngPath, SheetGrid grid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pngPath);
        ArgumentNullException.ThrowIfNull(grid);

        long before = new FileInfo(pngPath).Length;
        string tmp = $"{pngPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var image = Image.Load(pngPath))
            {
                var text = image.Metadata.GetPngMetadata().TextData;
                for (int i = text.Count - 1; i >= 0; i--)
                    if (text[i].Keyword == ChunkKeyword) text.RemoveAt(i);
                text.Add(new PngTextData(ChunkKeyword, grid.ToChunkText(), "", ""));
                image.Save(tmp, new PngEncoder());
            }

            File.Move(tmp, pngPath, overwrite: true);
            return new FileInfo(pngPath).Length - before;
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }
}
