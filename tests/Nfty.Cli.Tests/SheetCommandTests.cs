using System.CommandLine;
using Nfty.Core.Publish;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Cli.Tests;

/// <summary><c>nfty sheet grid</c>: read, write and clear the grid a spritesheet carries.</summary>
public class SheetCommandTests
{
    private static readonly InvocationConfiguration NonThrowing = new() { EnableDefaultExceptionHandler = false };

    private static int Run(params string[] args) => CommandFactory.Build().Parse(args).Invoke(NonThrowing);

    private static ParseResult Parse(string line) => CommandFactory.Build().Parse(line);

    private static (int Code, string Out) RunCaptured(params string[] args)
    {
        var orig = Console.Out;
        var o = new StringWriter();
        Console.SetOut(o);
        try { return (Run(args), o.ToString()); }
        finally { Console.SetOut(orig); }
    }

    /// <summary>8x4: two 4x4 cells, each a 3-wide opaque block, a transparent gutter column after it.</summary>
    private static string WriteSheet()
    {
        string path = Path.Combine(Directory.CreateTempSubdirectory().FullName, "sheet.png");
        using var img = new Image<Rgba32>(8, 4);
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 3; x++)
            {
                img[x, y] = new Rgba32(200, 30, 30, 255);
                img[x + 4, y] = new Rgba32(30, 30, 200, 255);
            }
        img.SaveAsPng(path);
        return path;
    }

    private static byte[] Pixels(string path)
    {
        using var img = Image.Load<Rgba32>(path);
        var bytes = new byte[img.Width * img.Height * 4];
        img.CopyPixelDataTo(bytes);
        return bytes;
    }

    [Fact]
    public void Sheet_grid_prints_none_and_the_detected_grid()
    {
        string sheet = WriteSheet();

        var (code, text) = RunCaptured("sheet", "grid", sheet);

        Assert.Equal(0, code);
        Assert.Contains("embedded: none", text);
        Assert.Contains("detected: cols=2;rows=1;cell=4x4;count=2", text);
    }

    [Fact]
    public void Sheet_grid_writes_and_clears_the_chunk()
    {
        string sheet = WriteSheet();
        var before = Pixels(sheet);

        Assert.Equal(0, RunCaptured("sheet", "grid", sheet, "--cols", "2", "--rows", "1").Code);
        Assert.Equal(new SheetGrid(2, 1, 4, 4, 2), SheetGrid.Read(sheet));
        Assert.Equal(before, Pixels(sheet));

        // The cell form says the same grid another way.
        Assert.Equal(0, RunCaptured("sheet", "grid", sheet, "--cell", "4x4").Code);
        Assert.Equal(new SheetGrid(2, 1, 4, 4, 2), SheetGrid.Read(sheet));

        var (code, text) = RunCaptured("sheet", "grid", sheet);
        Assert.Equal(0, code);
        Assert.Contains("embedded: cols=2;rows=1;cell=4x4;count=2", text);

        Assert.Equal(0, RunCaptured("sheet", "grid", sheet, "--clear").Code);
        Assert.Null(SheetGrid.Read(sheet));
        Assert.Equal(before, Pixels(sheet));
    }

    [Fact]
    public void Sheet_grid_refuses_cols_with_cell()
    {
        Assert.NotEmpty(Parse("sheet grid s.png --cols 2 --rows 1 --cell 4x4").Errors);
        Assert.NotEmpty(Parse("sheet grid s.png --cols 2").Errors);
        Assert.NotEmpty(Parse("sheet grid s.png --clear --cols 2 --rows 1").Errors);
        Assert.NotEmpty(Parse("sheet grid s.png --clear --cell 4x4").Errors);
        Assert.Empty(Parse("sheet grid s.png --cols 2 --rows 1").Errors);
    }

    [Fact]
    public void A_grid_that_does_not_divide_the_picture_is_refused_with_the_core_sentence()
    {
        string sheet = WriteSheet();

        var ex = Assert.Throws<InvalidOperationException>(() => Run("sheet", "grid", sheet, "--cols", "3", "--rows", "1"));

        Assert.Equal("8 px does not divide into 3 columns", ex.Message);
        Assert.Null(SheetGrid.Read(sheet));
    }
}
