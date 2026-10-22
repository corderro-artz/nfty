using System.Globalization;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
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
/// <para><b>Depth leaves as a Custom ingredient.</b> A picture's depth has no book to go into, so Save
/// asks for a path and writes an <c>.igt</c>: every cell a variant (a lone picture one variant named for
/// the file), the art as it is and each cell's depth beside it. With no way to ask for a path
/// (<c>pickPath</c> null) nothing can be saved and the footer says so.</para>
/// </remarks>
public sealed class PictureRiseSource : RiseSource
{
    private readonly IImageBridge _bridge;
    private readonly string _stem;
    private readonly Image<Rgba32> _image;
    private readonly Func<Task<string?>>? _pickPath;
    private readonly List<RiseItem> _retired = new();
    private Task? _inFlight;
    private string? _saveResultNote;
    private List<RiseItem> _items = new();
    private RiseItem _initial = null!;

    private PictureRiseSource(string path, Image<Rgba32> image, IImageBridge bridge, Func<Task<string?>>? pickPath)
    {
        _pickPath = pickPath;
        Path = path;
        _image = image;
        _bridge = bridge;
        _stem = System.IO.Path.GetFileNameWithoutExtension(path);
    }

    /// <summary>Loads the picture, finds its grid (chunk, then detection, then none), slices its cells,
    /// and — for a foreign sheet — writes the grid into the file.</summary>
    /// <param name="path">The <c>.png</c>.</param>
    /// <param name="bridge">Makes the row thumbnails.</param>
    /// <param name="pickPath">Asks the person where to save the depth as an <c>.igt</c>; a null result
    /// is a canceled dialog. Null means this source cannot save.</param>
    /// <returns>The source; the caller disposes it.</returns>
    /// <exception cref="InvalidOperationException">The file is not a picture that can be read.</exception>
    public static PictureRiseSource Open(string path, IImageBridge bridge, Func<Task<string?>>? pickPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(bridge);

        Image<Rgba32> image;
        try { image = Image.Load<Rgba32>(path); }
        catch (Exception ex) when (ex is ImageFormatException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"{System.IO.Path.GetFileName(path)} could not be read as a picture — {ex.Message}", ex);
        }

        var source = new PictureRiseSource(path, image, bridge, pickPath);
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

    /// <summary>The picture's width in pixels.</summary>
    public int ImageWidth => _image.Width;

    /// <summary>The picture's height in pixels.</summary>
    public int ImageHeight => _image.Height;

    /// <summary>Looks for the grid by the transparent gutters between cells, without changing anything.</summary>
    /// <returns>The grid found, or null when the picture shows none.</returns>
    public SheetGrid? DetectGrid() => SheetGrid.Detect(_image);

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
    public override bool CanSave => _pickPath is not null;

    /// <inheritdoc />
    public override string? UnsavableNote => "depth on loose art is kept for this session";

    /// <inheritdoc />
    public override string SaveTargetName => ".igt file";

    /// <inheritdoc />
    public override string SaveLabel => "Save as .igt…";

    /// <inheritdoc />
    public override string? SaveResultNote => _saveResultNote;

    /// <inheritdoc />
    public override string RailFootNote => "A single picture — no grid.";

    /// <inheritdoc />
    public override bool RailFootIsLock => false;

    /// <inheritdoc />
    public override string BatchStem => $"{_stem}-models";

    /// <inheritdoc />
    public override string DepthNote =>
        "Thickness is in voxels: 1 + front + back. A flat sprite is paper, one voxel thick. "
        + (CanSave ? "Save as .igt keeps the depth beside the art." : "Depth on loose art is kept for this session.");

    /// <inheritdoc />
    public override string OpeningLine => Grid is { } g
        ? string.Create(CultureInfo.InvariantCulture,
            $"Rising “{Title}”, a sheet of {g.Count} cells ({g.Columns} × {g.Rows}, {Size}, {OriginWord}) — left raises the front, right the back, {KeyHints.LowerDepth} lowers.")
        : $"Rising “{Title}” — left raises the front, right the back, {KeyHints.LowerDepth} lowers.";

    /// <inheritdoc />
    public override RiseSubject Open(RiseItem item) =>
        item.Subject ?? throw new InvalidOperationException($"Item '{item.Id}' was not loaded.");

    /// <summary>Writes the art and every cell's depth into a Custom <c>.igt</c> the person names.</summary>
    /// <returns>A task that completes when the file is written and every cell is marked saved, or at
    /// once when the dialog was canceled (nothing written, the depth still unsaved). A second call
    /// while one runs returns that same task.</returns>
    /// <exception cref="InvalidOperationException">The ingredient does not validate.</exception>
    public override Task SaveAsync()
    {
        if (_pickPath is null) return Task.CompletedTask;
        if (_inFlight is { } running) return running;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _inFlight = done.Task;
        _ = RunSaveAsync(done);
        return done.Task;
    }

    private async Task RunSaveAsync(TaskCompletionSource done)
    {
        Exception? failure = null;
        try { await SaveCoreAsync(); }
        catch (Exception ex) { failure = ex; }
        _inFlight = null;
        if (failure is null) done.SetResult(); else done.SetException(failure);
    }

    private async Task SaveCoreAsync()
    {
        string? chosen = await _pickPath!();
        if (string.IsNullOrWhiteSpace(chosen)) return;    // canceled: nothing written, depth stays dirty
        string path = string.Equals(System.IO.Path.GetExtension(chosen), ".igt", StringComparison.OrdinalIgnoreCase)
            ? chosen : chosen + ".igt";

        string id = WizardViewModelBase.DeriveId(_stem);
        if (id.Length == 0) id = "picture";
        var variants = new List<Variant>();
        var images = new Dictionary<string, Image<Rgba32>>(StringComparer.Ordinal);
        var depths = new Dictionary<string, DepthMap>(StringComparer.Ordinal);
        // What is written is what is marked saved afterwards: a stroke painted while the disk is busy
        // stays dirty.
        var written = new List<(RiseSubject Subject, DepthMap? Map)>();
        foreach (var item in _items)
        {
            var subject = item.Subject ?? throw new InvalidOperationException($"Item '{item.Id}' was not loaded.");
            string variantId = Grid is null ? id : item.Id;
            string variantName = Grid is null
                ? _stem
                : string.Create(CultureInfo.InvariantCulture, $"Cell {item.Id["cell-".Length..]}");
            variants.Add(new Variant(variantId, variantName, 1));
            images[variantId] = subject.Art;
            var map = subject.Depth.ToDepthMap();
            if (map is not null) depths[variantId] = map;
            written.Add((subject, map));
        }

        var manifest = new IngredientManifest(id, _stem, LayerKind.Custom, null, variants);
        var problems = Validator.ValidateIngredient(
            new LoadedIngredient { Manifest = manifest, VariantImages = images, Depths = depths });
        if (problems.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, problems));

        var tmp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await IngredientArchive.WriteAsync(tmp, manifest, images, depths);
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp)) { try { File.Delete(tmp); } catch { /* best effort */ } }
        }

        // Marked before anyone is told: the write is done.
        foreach (var (subject, map) in written) subject.MarkSaved(map);
        foreach (var item in _items) item.RefreshDepth();
        _saveResultNote = string.Create(CultureInfo.InvariantCulture,
            $"Saved {variants.Count} {(variants.Count == 1 ? "variant" : "variants")} and their depth to {System.IO.Path.GetFileName(path)}.");
        RaiseChanged();
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        base.Dispose();
        foreach (var item in _retired) item.Dispose();
        _retired.Clear();
        _image.Dispose();
    }
}
