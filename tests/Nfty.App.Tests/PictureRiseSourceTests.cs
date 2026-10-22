using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Rise;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Publish;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public class PictureRiseSourceTests
{
    private const int Cell = 16;

    private static string Temp() => Directory.CreateTempSubdirectory("nfty-picture-rise-").FullName;

    /// <summary>A sheet whose cells have a transparent margin, so detection finds the grid. The first
    /// <paramref name="filled"/> cells (reading order) hold an opaque 8 x 8 square; the rest are empty.
    /// <paramref name="partialCell"/> (1-based) gets one half-transparent pixel.</summary>
    private static Image<Rgba32> Sheet(int cols, int rows, int filled, int partialCell = 0)
    {
        var img = new Image<Rgba32>(cols * Cell, rows * Cell);
        for (int n = 0; n < filled; n++)
        {
            int ox = n % cols * Cell, oy = n / cols * Cell;
            var color = new Rgba32((byte)(40 + n * 30), (byte)(200 - n * 20), 90, 255);
            for (int y = 4; y < 12; y++)
                for (int x = 4; x < 12; x++)
                    img[ox + x, oy + y] = color;
            if (n + 1 == partialCell) img[ox + 5, oy + 5] = new Rgba32(10, 20, 30, 128);
        }
        return img;
    }

    private static string Save(Image<Rgba32> img, string dir, string name)
    {
        var path = Path.Combine(dir, name);
        img.Save(path);
        img.Dispose();
        return path;
    }

    private static byte[] Pixels(string path)
    {
        using var img = Image.Load<Rgba32>(path);
        var bytes = new byte[img.Width * img.Height * 4];
        img.CopyPixelDataTo(bytes);
        return bytes;
    }

    [AvaloniaFact]
    public void A_plain_picture_opens_as_one_item()
    {
        var dir = Temp();
        var img = new Image<Rgba32>(8, 8, new Rgba32(200, 30, 30, 255));
        var path = Save(img, dir, "gem.png");
        var before = File.ReadAllBytes(path);

        using var src = PictureRiseSource.Open(path, new ImageBridge());

        Assert.Equal(RiseSourceKind.Picture, src.Kind);
        Assert.Null(src.Grid);
        Assert.Equal(GridOrigin.None, src.GridOrigin);
        Assert.Null(src.GridWriteNote);
        var item = Assert.Single(src.Items);
        Assert.Equal("picture", item.Id);
        Assert.Equal("gem", item.Name);
        Assert.Equal("gem", item.ExportBaseName);
        Assert.False(src.CanBatch);
        Assert.True(src.IsEditable);
        Assert.False(src.IsLocked);
        Assert.False(src.CanSave);
        Assert.Equal("gem-models", src.BatchStem);
        // A plain picture is never touched.
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [AvaloniaFact]
    public void A_sheet_with_a_chunk_opens_its_cells_and_is_not_rewritten()
    {
        var dir = Temp();
        var path = Save(Sheet(3, 2, 5), dir, "hero.png");
        var grid = new SheetGrid(3, 2, Cell, Cell, 5);
        SheetGrid.WriteInto(path, grid);
        var before = File.ReadAllBytes(path);

        using var src = PictureRiseSource.Open(path, new ImageBridge());

        Assert.Equal(RiseSourceKind.Sheet, src.Kind);
        Assert.Equal(grid, src.Grid);
        Assert.Equal(GridOrigin.Chunk, src.GridOrigin);
        Assert.Null(src.GridWriteNote);
        Assert.Equal(5, src.Items.Count);
        Assert.Equal(before, File.ReadAllBytes(path));    // no write on every open
        Assert.Contains("from the file", src.Facts);
        Assert.Contains("sheet 3 × 2 · 16×16", src.Facts);
    }

    [AvaloniaFact]
    public void A_foreign_sheet_is_detected_and_the_grid_written_in()
    {
        var dir = Temp();
        var path = Save(Sheet(2, 1, 2), dir, "foreign.png");
        long length = new FileInfo(path).Length;
        var pixels = Pixels(path);

        using var src = PictureRiseSource.Open(path, new ImageBridge());

        var expected = new SheetGrid(2, 1, Cell, Cell, 2);
        Assert.Equal(expected, src.Grid);
        Assert.Equal(GridOrigin.Detected, src.GridOrigin);
        Assert.Equal(expected, SheetGrid.Read(path));
        Assert.Equal(pixels, Pixels(path));
        long delta = new FileInfo(path).Length - length;
        string sign = delta < 0 ? "-" : "+";
        Assert.Equal(string.Create(CultureInfo.InvariantCulture,
            $"Wrote the grid into foreign.png ({sign}{Math.Abs(delta):N0} bytes)."), src.GridWriteNote);
        Assert.Contains("detected", src.Facts);
    }

    [AvaloniaFact]
    public void A_read_only_foreign_sheet_keeps_the_grid_for_the_session()
    {
        // Moving a temp file over a read-only one fails only where the attribute is enforced on replace.
        if (!OperatingSystem.IsWindows()) return;

        var dir = Temp();
        var path = Save(Sheet(2, 1, 2), dir, "locked.png");
        var before = File.ReadAllBytes(path);
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            using var src = PictureRiseSource.Open(path, new ImageBridge());

            Assert.Equal(2, src.Grid!.Count);
            Assert.Equal(GridOrigin.Detected, src.GridOrigin);
            Assert.Equal("locked.png is read-only — the grid is kept for this session.", src.GridWriteNote);
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Equal(2, src.Items.Count);
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));     // the temp does not outlive the failure
        }
        finally { File.SetAttributes(path, FileAttributes.Normal); }
    }

    [AvaloniaFact]
    public void Cells_past_the_count_are_not_items()
    {
        var dir = Temp();
        var path = Save(Sheet(3, 2, 5), dir, "short.png");     // sixth cell empty

        using var src = PictureRiseSource.Open(path, new ImageBridge());

        Assert.Equal(6, src.Grid!.Columns * src.Grid.Rows);
        Assert.Equal(5, src.Grid.Count);
        Assert.Equal(new[] { "cell-1", "cell-2", "cell-3", "cell-4", "cell-5" }, src.Items.Select(i => i.Id));
        Assert.Equal(new[] { "#1", "#2", "#3", "#4", "#5" }, src.Items.Select(i => i.Name));
        Assert.Equal("cell 4 of 5", src.Items[3].Caption);
        Assert.Equal("short-4", src.Items[3].ExportBaseName);
        Assert.True(src.CanBatch);
        Assert.Equal(src.Items[0], src.InitialItem);
        // Reading order: the second row starts at the fourth cell.
        Assert.Equal(new Rgba32(40 + 3 * 30, 200 - 3 * 20, 90, 255), src.Open(src.Items[3]).Art[5, 5]);
    }

    [AvaloniaFact]
    public void A_partial_alpha_cell_is_not_risable_but_its_neighbors_are()
    {
        var dir = Temp();
        var path = Save(Sheet(3, 1, 3, partialCell: 2), dir, "ghost.png");

        using var src = PictureRiseSource.Open(path, new ImageBridge());

        Assert.True(src.IsTwoDOnly);
        Assert.True(src.Items[0].IsRisableOrUnknown);
        Assert.False(src.Items[1].IsRisableOrUnknown);
        Assert.True(src.Items[2].IsRisableOrUnknown);
        Assert.Equal(1, src.Items[1].PartialCount);
        Assert.Single(src.PartialAlphaRows);
    }

    [AvaloniaFact]
    public void A_typed_grid_slices_the_picture_and_is_written_in()
    {
        // One sprite with an internal gap wider than a cell: detection cannot know. The typed grid can.
        var dir = Temp();
        var img = new Image<Rgba32>(32, 16, new Rgba32(10, 200, 10, 255));
        var path = Save(img, dir, "sprite.png");
        using var src = PictureRiseSource.Open(path, new ImageBridge());
        Assert.Equal(RiseSourceKind.Picture, src.Kind);
        int changed = 0;
        src.Changed += () => changed++;

        src.ApplyGrid(SheetGrid.FromColumnsRows(2, 1, 32, 16));

        Assert.Equal(RiseSourceKind.Sheet, src.Kind);
        Assert.Equal(GridOrigin.Manual, src.GridOrigin);
        Assert.Equal(2, src.Items.Count);
        Assert.Equal(new SheetGrid(2, 1, 16, 16, 2), SheetGrid.Read(path));
        Assert.StartsWith("Wrote the grid into sprite.png", src.GridWriteNote);
        Assert.Contains("entered", src.Facts);
        Assert.Equal(1, changed);

        // The same grid again changes nothing: no rewrite, no lost depth.
        var bytes = File.ReadAllBytes(path);
        src.ApplyGrid(SheetGrid.FromColumnsRows(2, 1, 32, 16));
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal(1, changed);

        // Back to one picture: the chunk stays (a file is never stripped), the session reads it as one.
        src.ApplyGrid(null);
        Assert.Equal(RiseSourceKind.Picture, src.Kind);
        Assert.Single(src.Items);
        Assert.Null(src.GridWriteNote);
    }

    [AvaloniaFact]
    public void A_grid_that_does_not_tile_the_picture_is_refused_and_the_file_is_left_alone()
    {
        var dir = Temp();
        var path = Save(new Image<Rgba32>(10, 16, new Rgba32(1, 2, 3, 255)), dir, "odd.png");
        var before = File.ReadAllBytes(path);
        using var src = PictureRiseSource.Open(path, new ImageBridge());

        var ex = Assert.Throws<ArgumentException>(() => src.ApplyGrid(new SheetGrid(3, 1, 3, 16, 3)));

        Assert.Contains("10 px does not divide into 3 columns", ex.Message);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(RiseSourceKind.Picture, src.Kind);
    }

    [AvaloniaFact]
    public void An_unreadable_file_throws_in_words()
    {
        var path = Path.Combine(Temp(), "not.png");
        File.WriteAllText(path, "this is not a picture");

        var ex = Assert.Throws<InvalidOperationException>(() => PictureRiseSource.Open(path, new ImageBridge()));

        Assert.Contains("not.png", ex.Message);
    }

    [AvaloniaFact]
    public void The_footer_says_depth_on_loose_art_is_kept_for_this_session()
    {
        var path = Save(Sheet(2, 1, 2), Temp(), "foot.png");
        using var vm = RisePages.Vm(PictureRiseSource.Open(path, new ImageBridge()));
        var subject = vm.Source.Open(vm.Items[0]);
        subject.History.Do(new Nfty.Core.Editing.DepthStep(new[] { (5, 5) }, Nfty.Core.Editing.DepthSide.Front, +1), subject.Depth);
        vm.Items[0].RefreshDepth();

        Assert.True(vm.HasUnsavedDepth);
        Assert.False(vm.CanSave);
        Assert.Equal("depth on loose art is kept for this session", vm.FooterText);
    }

    private sealed class FolderPicker(string folder) : IFilePickerService
    {
        public Task<string?> OpenFileAsync(string title, params string[] extensions) => throw new InvalidOperationException();
        public Task<string?> SaveFileAsync(string title, string defaultExtension) => throw new InvalidOperationException();
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(folder);
    }

    [AvaloniaFact]
    public async Task Exporting_checked_cells_writes_one_model_each()
    {
        var dir = Temp();
        var root = Temp();
        var path = Save(Sheet(3, 1, 3), dir, "crew.png");
        using var vm = new RiseViewModel(PictureRiseSource.Open(path, new ImageBridge()), new ImageBridge(),
            new FakeNav(), new FakeDialogs(), new FolderPicker(root), new StatusService());
        vm.Items[0].IsChecked = true;
        vm.Items[2].IsChecked = true;
        vm.Packed = false;

        await vm.ExportCommand.ExecuteAsync(null);

        var written = Directory.GetFiles(Path.Combine(root, "crew-models"))
            .Select(f => Path.GetFileName(f)!).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "crew-1.glb", "crew-3.glb" }, written);
    }
}
