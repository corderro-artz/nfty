using Nfty.Core.Output;
using Nfty.Core.Publish;
using Nfty.Core.Publish.Community;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.Core.Tests.Community;

public class CommunityPreviewTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("nfty-preview-");

    public void Dispose() => _dir.Delete(recursive: true);

    private List<SetItem> Items(int count, int side)
    {
        var list = new List<SetItem>();
        for (int i = 1; i <= count; i++)
        {
            string path = Path.Combine(_dir.FullName, $"{i}.png");
            using (var img = new Image<Rgba32>(side, side, new Rgba32((byte)(i * 3), 10, 10, 255)))
                img.SaveAsPng(path);
            list.Add(new SetItem(i, path, "dna" + i, "r", Array.Empty<RarityAttribute>(), Array.Empty<LayerColor>()));
        }
        return list;
    }

    [Fact]
    public void Thirty_two_px_art_takes_sixty_four_cells() =>
        Assert.Equal(64, CommunityPreview.CellCount(600, 32, 32));

    [Fact]
    public void Five_twelve_px_art_takes_sixteen() =>
        Assert.Equal(16, CommunityPreview.CellCount(600, 512, 512));

    [Fact]
    public void Never_more_than_the_set() =>
        Assert.Equal(5, CommunityPreview.CellCount(5, 32, 32));

    [Fact]
    public void Sheet_side_never_exceeds_2048()
    {
        for (int s = 1; s <= 512; s++)
        {
            int n = CommunityPreview.CellCount(600, s, s);
            Assert.InRange(n, 1, 64);
            var l = SpriteSheet.Fit(n, s, s);
            Assert.True(l.Width <= 2048 && l.Height <= 2048, $"{s}px -> {l.SizeText()}");
        }
    }

    [Fact]
    public async Task The_written_preview_carries_its_grid_and_passes_check()
    {
        var items = Items(10, 4);
        string dest = Path.Combine(_dir.FullName, "preview.png");
        await CommunityPreview.WriteAsync(items, dest, CancellationToken.None);

        var grid = SheetGrid.Read(dest);
        Assert.NotNull(grid);
        Assert.Equal(10, grid!.Count);
        Assert.Equal(4, grid.CellWidth);
        Assert.Empty(CommunityPreview.Check(dest, 4, 4));
    }

    [Fact]
    public async Task Check_refuses_wrong_cell_size()
    {
        string dest = Path.Combine(_dir.FullName, "preview.png");
        await CommunityPreview.WriteAsync(Items(4, 4), dest, CancellationToken.None);
        Assert.Equal(new[] { ShareRules.PreviewBad }, CommunityPreview.Check(dest, 8, 8));
    }

    [Fact]
    public void Check_refuses_missing_not_png_and_gridless_files()
    {
        Assert.Equal(new[] { ShareRules.PreviewBad },
            CommunityPreview.Check(Path.Combine(_dir.FullName, "nope.png"), 4, 4));

        string junk = Path.Combine(_dir.FullName, "junk.png");
        File.WriteAllText(junk, "not a png");
        Assert.Equal(new[] { ShareRules.PreviewBad }, CommunityPreview.Check(junk, 4, 4));

        string plain = Path.Combine(_dir.FullName, "plain.png");
        using (var img = new Image<Rgba32>(4, 4)) img.SaveAsPng(plain);
        Assert.Equal(new[] { ShareRules.PreviewBad }, CommunityPreview.Check(plain, 4, 4));
    }

    [Fact]
    public async Task Check_refuses_a_grid_bigger_than_the_picture_it_sits_in()
    {
        string dest = Path.Combine(_dir.FullName, "preview.png");
        await CommunityPreview.WriteAsync(Items(4, 4), dest, CancellationToken.None); // an 8 x 8 sheet of 4 px cells
        SheetGrid.WriteInto(dest, new SheetGrid(80, 2, 4, 4, 4));   // 320 px of columns in an 8 px picture
        Assert.Equal(new[] { ShareRules.PreviewBad }, CommunityPreview.Check(dest, 4, 4));
        SheetGrid.WriteInto(dest, new SheetGrid(2, 80, 4, 4, 4));   // 320 px of rows
        Assert.Equal(new[] { ShareRules.PreviewBad }, CommunityPreview.Check(dest, 4, 4));
        SheetGrid.WriteInto(dest, new SheetGrid(2, 2, 4, 4, 4));    // the true grid is fine again
        Assert.Empty(CommunityPreview.Check(dest, 4, 4));
    }

    [Fact]
    public void FitsWithin_asks_whether_the_cells_stay_inside_the_picture()
    {
        Assert.True(new SheetGrid(2, 2, 4, 4, 4).FitsWithin(8, 8));
        Assert.True(new SheetGrid(2, 2, 4, 4, 3).FitsWithin(9, 9));
        Assert.False(new SheetGrid(2, 2, 4, 4, 4).FitsWithin(7, 8));
        Assert.False(new SheetGrid(2, 2, 4, 4, 4).FitsWithin(8, 7));
        Assert.False(new SheetGrid(2, 2, 4, 4, 5).FitsWithin(8, 8)); // more cells than the grid holds
        Assert.False(new SheetGrid(int.MaxValue, 2, 4, 4, 1).FitsWithin(8, 8)); // never overflows
    }

    [Fact]
    public async Task Check_refuses_a_file_over_two_megabytes()
    {
        string dest = Path.Combine(_dir.FullName, "preview.png");
        await CommunityPreview.WriteAsync(Items(4, 4), dest, CancellationToken.None);
        using (var fs = new FileStream(dest, FileMode.Append)) fs.Write(new byte[2_200_000]);
        Assert.Equal(new[] { ShareRules.PreviewBad }, CommunityPreview.Check(dest, 4, 4));
    }

    [Fact]
    public async Task A_preview_takes_the_first_cells_in_number_order()
    {
        var items = Items(3, 4);
        items.Reverse();
        string dest = Path.Combine(_dir.FullName, "preview.png");
        await CommunityPreview.WriteAsync(items, dest, CancellationToken.None);
        using var sheet = Image.Load<Rgba32>(dest);
        Assert.Equal(3, sheet[0, 0].R);
    }
}
