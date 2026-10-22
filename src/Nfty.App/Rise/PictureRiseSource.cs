using System.Globalization;
using Nfty.App.Services;
using Nfty.Core.Publish;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Nfty.App.Rise;

/// <summary>Where a sheet's grid came from.</summary>
public enum GridOrigin
{
    /// <summary>There is no grid: the file is one picture.</summary>
    None,

    /// <summary>The file carried it (an <c>nfty-grid</c> chunk).</summary>
    Chunk,

    /// <summary>The file said nothing and the cells were found by their transparent gutters.</summary>
    Detected,

    /// <summary>Somebody typed it.</summary>
    Manual,
}

/// <summary>
/// One loose picture, or a spritesheet of them, risen as it is on disk. A sheet is cut into cells in
/// reading order, each cell its own item; a picture is one item.
/// </summary>
/// <remarks>
/// <para><b>The grid is learned once and then travels in the file.</b> A sheet that already says how it
/// is cut is never rewritten (no write on every open). One that does not is searched for its grid,
/// and a grid found — or typed — is written into the PNG so the next opening needs no search. A file
/// with no grid is a picture and is never touched. A file that cannot be written keeps the grid for
/// this session, and <see cref="GridWriteNote"/> says which happened.</para>
/// <para>Depth painted here has nowhere to go yet: <see cref="CanSave"/> is false and the footer says so.</para>
/// </remarks>
public sealed class PictureRiseSource : RiseSource
{
    private readonly IImageBridge _bridge;
    private readonly string _stem;
    private readonly Image<Rgba32> _image;
    private readonly List<RiseItem> _retired = new();
    private List<RiseItem> _items = new();
    private RiseItem _initial = null!;

    private PictureRiseSource(string path, Image<Rgba32> image, IImageBridge bridge)
    {
        Path = path;
        _image = image;
        _bridge = bridge;
        _stem = System.IO.Path.GetFileNameWithoutExtension(path);
    }

    /// <summary>Loads the picture, finds its grid (chunk, then detection, then none), slices its cells,
    /// and — for a foreign sheet — writes the grid into the file.</summary>
    /// <param name="path">The <c>.png</c>.</param>
    /// <param name="bridge">Makes the row thumbnails.</param>
    /// <returns>The source; the caller disposes it.</returns>
    /// <exception cref="InvalidOperationException">The file is not a picture that can be read.</exception>
    public static PictureRiseSource Open(string path, IImageBridge bridge)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(bridge);

        Image<Rgba32> image;
        try { image = Image.Load<Rgba32>(path); }
        catch (Exception ex) when (ex is ImageFormatException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"{System.IO.Path.GetFileName(path)} could not be read as a picture — {ex.Message}", ex);
        }

        var source = new PictureRiseSource(path, image, bridge);
        try
        {
            // A chunk is trusted only if it tiles THIS picture: a sheet edited after it was stamped, or a
            // hand-made chunk, falls through to detection instead of slicing unevenly.
            var chunk = SheetGrid.Read(path);
            if (chunk is not null && IsGrid(chunk) && SheetGrid.ProblemWith(chunk, image.Width, image.Height) is null)
            {
                source.Grid = chunk;
                source.GridOrigin = GridOrigin.Chunk;
            }
            else
            {
                var found = SheetGrid.Detect(image);
                if (found is not null)
                {
                    source.Grid = found;
                    source.GridOrigin = GridOrigin.Detected;
                    source.WriteGrid(found);
                }
            }

            source.Rebuild();
            return source;
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    /// <summary>The picture's path.</summary>
    public string Path { get; }

    /// <summary>The cutting, or null when the file is one picture.</summary>
    public SheetGrid? Grid { get; private set; }

    /// <summary>Where <see cref="Grid"/> came from.</summary>
    public GridOrigin GridOrigin { get; private set; }

    /// <summary>What happened to the file when the grid was learned or changed ("Wrote the grid into
    /// sheet.png (+74 bytes).", "sheet.png is read-only — the grid is kept for this session."), or null
    /// when the file was not written (a chunk already there, a plain picture).</summary>
    public string? GridWriteNote { get; private set; }

    /// <summary>Changes how the picture is cut, re-slices it and writes the result into the file: the
    /// grid for a sheet, and for a picture nothing, with any old grid taken out.</summary>
    /// <param name="grid">The new cutting; null (or one cell) makes the file one picture.</param>
    /// <exception cref="ArgumentException">The grid does not tile the picture; the message says how.</exception>
    /// <remarks>Every cell's depth is discarded when the grid changes — the caller asks first. The same
    /// grid again is a no-op, so nothing is rewritten and no depth is lost.</remarks>
    public void ApplyGrid(SheetGrid? grid)
    {
        if (grid is not null && !IsGrid(grid)) grid = null;
        if (grid is not null)
        {
            string? problem = SheetGrid.ProblemWith(grid, _image.Width, _image.Height);
            if (problem is null && (grid.Count < 1 || grid.Count > grid.Columns * grid.Rows))
                problem = $"A {grid.Columns} × {grid.Rows} grid holds 1 to {grid.Columns * grid.Rows} cells, not {grid.Count}.";
            if (problem is not null) throw new ArgumentException(problem, nameof(grid));
        }

        if (Equals(grid, Grid) && GridOrigin != GridOrigin.None) return;
        if (grid is null && Grid is null) return;

        Grid = grid;
        GridOrigin = grid is null ? GridOrigin.None : GridOrigin.Manual;
        GridWriteNote = null;
        if (grid is not null) WriteGrid(grid);
        else RemoveGrid();
        Rebuild();
        RaiseChanged();
    }

    private static bool IsGrid(SheetGrid g) => (long)g.Columns * g.Rows > 1;

    // Back to one picture means the FILE says so too: a chunk left behind would bring the sheet back
    // the next time the file is opened.
    private void RemoveGrid()
    {
        string name = System.IO.Path.GetFileName(Path);
        try
        {
            long delta = SheetGrid.RemoveFrom(Path);
            if (delta == 0) return;     // the file carried no grid (a detected one that could not be written)
            GridWriteNote = string.Create(CultureInfo.InvariantCulture,
                $"Removed the grid from {name} ({(delta < 0 ? "-" : "+")}{Math.Abs(delta):N0} bytes).");
        }
        catch (UnauthorizedAccessException)
        {
            GridWriteNote = $"{name} is read-only — it keeps its grid, and this session treats it as one picture.";
        }
        catch (Exception ex) when (ex is IOException or ImageFormatException)
        {
            GridWriteNote = $"{name} could not be written ({ex.Message}) — it keeps its grid, and this session treats it as one picture.";
        }
    }

    private void WriteGrid(SheetGrid grid)
    {
        string name = System.IO.Path.GetFileName(Path);
        try
        {
            long delta = SheetGrid.WriteInto(Path, grid);
            string sign = delta < 0 ? "-" : "+";
            GridWriteNote = string.Create(CultureInfo.InvariantCulture,
                $"Wrote the grid into {name} ({sign}{Math.Abs(delta):N0} bytes).");
        }
        catch (UnauthorizedAccessException)
        {
            GridWriteNote = $"{name} is read-only — the grid is kept for this session.";
        }
        catch (Exception ex) when (ex is IOException or ImageFormatException)
        {
            GridWriteNote = $"{name} could not be written ({ex.Message}) — the grid is kept for this session.";
        }
    }

    private void Rebuild()
    {
        // The old rows are let go at Dispose, not now: the page may still be reading them until it has
        // been told the source changed.
        _retired.AddRange(_items);
        var items = new List<RiseItem>();
        var inv = CultureInfo.InvariantCulture;

        if (Grid is { } g)
        {
            for (int n = 1; n <= g.Count; n++)
            {
                int index = n - 1;
                var rect = new Rectangle(index % g.Columns * g.CellWidth, index / g.Columns * g.CellHeight,
                    g.CellWidth, g.CellHeight);
                var cell = _image.Clone(ctx => ctx.Crop(rect));
                var item = new RiseItem(string.Create(inv, $"cell-{n}"), string.Create(inv, $"#{n}"),
                    string.Create(inv, $"cell {n} of {g.Count}"), string.Create(inv, $"{_stem}-{n}"),
                    _bridge.ToBitmap(cell), ownsThumbnail: true);
                item.Attach(new RiseSubject(cell, null, ownsArt: true));
                items.Add(item);
            }
        }
        else
        {
            var art = _image.Clone();
            var item = new RiseItem("picture", _stem, string.Create(inv, $"{art.Width}×{art.Height}"), _stem,
                _bridge.ToBitmap(art), ownsThumbnail: true);
            item.Attach(new RiseSubject(art, null, ownsArt: true));
            items.Add(item);
        }

        _items = items;
        _initial = items[0];
        _initial.IsSelected = true;
    }

    private string OriginWord => GridOrigin switch
    {
        GridOrigin.Chunk => "from the file",
        GridOrigin.Detected => "detected",
        GridOrigin.Manual => "entered",
        _ => "",
    };

    private string Size => Grid is { } g
        ? string.Create(CultureInfo.InvariantCulture, $"{g.CellWidth}×{g.CellHeight}")
        : string.Create(CultureInfo.InvariantCulture, $"{_image.Width}×{_image.Height}");

    /// <inheritdoc />
    public override RiseSourceKind Kind => Grid is null ? RiseSourceKind.Picture : RiseSourceKind.Sheet;

    /// <inheritdoc />
    public override string Kicker => $"{_stem.ToUpperInvariant()} · {(Grid is null ? "PICTURE" : "CELLS")}";

    /// <inheritdoc />
    public override string Title => _stem;

    /// <inheritdoc />
    public override IReadOnlyList<RiseItem> Items => _items;

    /// <inheritdoc />
    public override RiseItem InitialItem => _initial;

    /// <inheritdoc />
    public override IReadOnlyList<string> Facts => Grid is { } g
        ? new[]
        {
            string.Create(CultureInfo.InvariantCulture, $"sheet {g.Columns} × {g.Rows} · {Size}"),
            OriginWord,
        }
        : new[] { $"picture · {Size}" };

    /// <inheritdoc />
    public override bool IsTwoDOnly => _items.Any(i => i.Subject is { Depth.PartialCount: > 0 });

    /// <inheritdoc />
    public override bool IsEditable => true;

    /// <inheritdoc />
    public override bool IsLocked => false;

    /// <inheritdoc />
    public override bool CanBatch => Grid is not null;

    /// <inheritdoc />
    public override bool CanSave => false;

    /// <inheritdoc />
    public override string? UnsavableNote => "depth on loose art is kept for this session";

    /// <inheritdoc />
    public override string RailFootNote => "A single picture — no grid.";

    /// <inheritdoc />
    public override bool RailFootIsLock => false;

    /// <inheritdoc />
    public override string BatchStem => $"{_stem}-models";

    /// <inheritdoc />
    public override string DepthNote =>
        "Thickness is in voxels: 1 + front + back. A flat sprite is paper, one voxel thick. "
        + "Depth on loose art is kept for this session.";

    /// <inheritdoc />
    public override string OpeningLine => Grid is { } g
        ? string.Create(CultureInfo.InvariantCulture,
            $"Rising “{Title}”, a sheet of {g.Count} cells ({g.Columns} × {g.Rows}, {Size}, {OriginWord}) — left raises the front, right the back, {KeyHints.LowerDepth} lowers.")
        : $"Rising “{Title}” — left raises the front, right the back, {KeyHints.LowerDepth} lowers.";

    /// <inheritdoc />
    public override RiseSubject Open(RiseItem item) =>
        item.Subject ?? throw new InvalidOperationException($"Item '{item.Id}' was not loaded.");

    /// <inheritdoc />
    public override void Dispose()
    {
        base.Dispose();
        foreach (var item in _retired) item.Dispose();
        _retired.Clear();
        _image.Dispose();
    }
}
