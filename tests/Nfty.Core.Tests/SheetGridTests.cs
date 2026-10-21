using System.IO;
using Nfty.Core.Publish;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.Core.Tests;

/// <summary>
/// The grid a sheet carries: the chunk text, its parse, auto-detect, and the foreign-file rewrite.
/// </summary>
public class SheetGridTests
{
    private static string Temp() => Directory.CreateTempSubdirectory("nfty-grid-").FullName;

    /// <summary>Cells of 8x8 on a 3x2 grid; each cell holds a 4x4 blob at an offset that differs
    /// per cell, chosen so no divisor smaller than the cell clears every blob.</summary>
    private static Image<Rgba32> Cells(int cols, int rows, int cell, int lastFilled)
    {
        var img = new Image<Rgba32>(cols * cell, rows * cell, new Rgba32(0, 0, 0, 0));
        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            if (r * cols + c >= lastFilled) continue;
            int xo = 1 + (c + r) % 3, yo = 1 + (c * 2 + r) % 3;
            for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                img[c * cell + xo + x, r * cell + yo + y] = new Rgba32(200, 50, 50, 255);
        }
        return img;
    }

    [Fact]
    public void The_chunk_text_is_the_spec_form() =>
        Assert.Equal("cols=8;rows=4;cell=64x64;count=29", new SheetGrid(8, 4, 64, 64, 29).ToChunkText());

    [Fact]
    public void Parse_round_trips_and_ignores_unknown_keys() =>
        Assert.Equal(new SheetGrid(8, 4, 64, 64, 29),
            SheetGrid.Parse("count=29;future=1;cell=64x64;rows=4;cols=8"));

    [Theory]
    [InlineData("cols=8;rows=4;cell=64x64")]
    [InlineData("cols=0;rows=4;cell=64x64;count=1")]
    [InlineData("cols=2;rows=2;cell=8x8;count=5")]
    [InlineData("cols=2;rows=2;cell=8by8;count=1")]
    public void Parse_refuses_malformed_text(string text) => Assert.Null(SheetGrid.Parse(text));

    [Fact]
    public void Detect_finds_equal_cells_between_transparent_gutters()
    {
        using var img = Cells(3, 2, 8, 6);
        Assert.Equal(new SheetGrid(3, 2, 8, 8, 6), SheetGrid.Detect(img));
    }

    [Fact]
    public void Detect_takes_the_smallest_clear_cell_not_a_multiple_of_it()
    {
        // 32 wide: 16 also clears every blob, so a largest-divisor rule would answer 2 columns.
        using var img = Cells(4, 2, 8, 8);
        Assert.Equal(new SheetGrid(4, 2, 8, 8, 8), SheetGrid.Detect(img));
    }

    [Fact]
    public void Detect_counts_to_the_last_cell_with_content()
    {
        using var img = Cells(3, 2, 8, 4);
        Assert.Equal(new SheetGrid(3, 2, 8, 8, 4), SheetGrid.Detect(img));
    }

    [Fact]
    public void Detect_says_nothing_for_a_single_picture()
    {
        using var img = new Image<Rgba32>(16, 16, new Rgba32(0, 0, 0, 0));
        for (int y = 6; y < 10; y++)
        for (int x = 6; x < 10; x++)
            img[x, y] = new Rgba32(1, 2, 3, 255);
        Assert.Null(SheetGrid.Detect(img));
    }

    [Fact]
    public void Detect_says_nothing_for_an_empty_image()
    {
        using var img = new Image<Rgba32>(16, 16, new Rgba32(0, 0, 0, 0));
        Assert.Null(SheetGrid.Detect(img));
    }

    [Fact]
    public void A_grid_that_does_not_divide_the_picture_is_refused()
    {
        string? problem = SheetGrid.ProblemWith(new SheetGrid(3, 1, 21, 64, 3), 64, 64);
        Assert.NotNull(problem);
        Assert.Contains("64", problem);
        Assert.Contains("3", problem);
        Assert.Null(SheetGrid.ProblemWith(new SheetGrid(4, 1, 16, 64, 4), 64, 64));
        Assert.Throws<ArgumentException>(() => SheetGrid.FromColumnsRows(3, 1, 64, 64));
        Assert.Throws<ArgumentException>(() => SheetGrid.FromCellSize(24, 64, 64, 64));
        Assert.Equal(new SheetGrid(4, 2, 16, 32, 8), SheetGrid.FromColumnsRows(4, 2, 64, 64));
        Assert.Equal(new SheetGrid(4, 2, 16, 32, 8), SheetGrid.FromCellSize(16, 32, 64, 64));
    }

    private static Image<Rgba32> Foreign()
    {
        var img = new Image<Rgba32>(16, 8);
        for (int y = 0; y < 8; y++)
        for (int x = 0; x < 16; x++)
            img[x, y] = new Rgba32((byte)(x * 13), (byte)(y * 29), (byte)(x ^ y), (byte)(x % 5 == 0 ? 0 : 255));
        return img;
    }

    [Fact]
    public void Rewriting_a_foreign_sheet_keeps_every_pixel_and_adds_the_grid()
    {
        string dir = Temp();
        try
        {
            string path = Path.Combine(dir, "s.png");
            using (var src = Foreign()) src.SaveAsPng(path);
            long before = new FileInfo(path).Length;
            Assert.Null(SheetGrid.Read(path));

            var grid = new SheetGrid(2, 1, 8, 8, 2);
            long delta = SheetGrid.WriteInto(path, grid);

            Assert.Equal(new FileInfo(path).Length - before, delta);
            Assert.Equal(grid, SheetGrid.Read(path));
            using var expected = Foreign();
            using var actual = Image.Load<Rgba32>(path);
            for (int y = 0; y < 8; y++)
            for (int x = 0; x < 16; x++)
                Assert.Equal(expected[x, y], actual[x, y]);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Rewriting_a_16_bit_sheet_keeps_every_pixel()
    {
        string dir = Temp();
        try
        {
            string path = Path.Combine(dir, "s16.png");
            using (var src = new Image<Rgba64>(4, 2))
            {
                for (int y = 0; y < 2; y++)
                for (int x = 0; x < 4; x++)
                    src[x, y] = new Rgba64((ushort)(1000 + x * 257 + 1), (ushort)(2000 + y * 3001), (ushort)(40001 + x), 65535);
                src.SaveAsPng(path, new PngEncoder { BitDepth = PngBitDepth.Bit16, ColorType = PngColorType.RgbWithAlpha });
            }

            SheetGrid.WriteInto(path, new SheetGrid(2, 1, 2, 2, 2));

            using var expected = new Image<Rgba64>(4, 2);
            for (int y = 0; y < 2; y++)
            for (int x = 0; x < 4; x++)
                expected[x, y] = new Rgba64((ushort)(1000 + x * 257 + 1), (ushort)(2000 + y * 3001), (ushort)(40001 + x), 65535);
            using var actual = Image.Load<Rgba64>(path);
            for (int y = 0; y < 2; y++)
            for (int x = 0; x < 4; x++)
                Assert.Equal(expected[x, y], actual[x, y]);
            Assert.Equal(new SheetGrid(2, 1, 2, 2, 2), SheetGrid.Read(path));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Rewriting_replaces_an_existing_grid_rather_than_adding_a_second()
    {
        string dir = Temp();
        try
        {
            string path = Path.Combine(dir, "s.png");
            using (var src = Foreign()) src.SaveAsPng(path);
            SheetGrid.WriteInto(path, new SheetGrid(2, 1, 8, 8, 2));
            var second = new SheetGrid(4, 1, 4, 8, 3);
            SheetGrid.WriteInto(path, second);

            var info = Image.Identify(path);
            Assert.Single(info.Metadata.GetPngMetadata().TextData, t => t.Keyword == SheetGrid.ChunkKeyword);
            Assert.Equal(second, SheetGrid.Read(path));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_failed_rewrite_leaves_the_file_untouched_and_no_temp_behind()
    {
        if (!OperatingSystem.IsWindows()) return; // a read-only attribute blocks overwrite-by-rename only there
        string dir = Temp();
        string path = Path.Combine(dir, "s.png");
        try
        {
            using (var src = Foreign()) src.SaveAsPng(path);
            byte[] before = File.ReadAllBytes(path);
            File.SetAttributes(path, FileAttributes.ReadOnly);

            Assert.ThrowsAny<Exception>(() => SheetGrid.WriteInto(path, new SheetGrid(2, 1, 8, 8, 2)));

            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(dir, recursive: true);
        }
    }
}
