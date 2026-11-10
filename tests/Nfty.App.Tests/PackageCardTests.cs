using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Controls;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>The shared manifest card: it reads Core's part strings as name + detail, and lays them out as equal cells.</summary>
public class PackageCardTests
{
    [Theory]
    [InlineData("images/  100 files", "images/", "100 files")]
    [InlineData("3D models (1,000)", "3D models", "1,000")]
    [InlineData("set.json", "set.json", "")]
    [InlineData("spritesheet.png  8 x 4 cells, 1 MB", "spritesheet.png", "8 x 4 cells, 1 MB")]
    [InlineData("pivot bottom-center", "pivot bottom-center", "")]
    public void A_part_splits_into_its_name_and_its_detail(string part, string name, string detail)
    {
        Assert.Equal((name, detail), PackageCard.Split(part));
    }

    [Theory]
    [InlineData("images/", "IconFolderOpen")]
    [InlineData("3D models", "IconRise")]
    [InlineData("ChestDemo.cbk", "IconTypeCookBook")]
    [InlineData("chest.set", "IconTypeSet")]
    [InlineData("spritesheet.png", "IconGrid")]
    [InlineData("set.json", "IconSymbol")]
    [InlineData("pivot bottom-center", null)]
    public void A_part_draws_the_glyph_of_what_it_is(string name, string? key)
    {
        Assert.Equal(key, PackageCard.GlyphOf(name));
    }

    [AvaloniaFact]
    public void Every_cell_is_one_height_and_the_columns_line_up()
    {
        var card = new PackageCard
        {
            Title = "chest.set",
            Size = "2 KB",
            Columns = 2,
            Parts = new[] { "images/  2 files", "metadata/  2 files", "nfty/  2 files", "set.json" },
        };
        var window = new Window { Content = card, Width = 420, Height = 200 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var cells = card.GetVisualDescendants().OfType<Grid>().Where(g => g.Classes.Contains("pkgcell")).ToList();
            Assert.Equal(4, cells.Count);
            Assert.All(cells, c => Assert.Equal(PackageCard.CellHeight, c.Bounds.Height));
            Assert.Equal(cells[0].Bounds.X, cells[2].Bounds.X);   // the second row starts where the first did
            Assert.Equal(cells[1].Bounds.X, cells[3].Bounds.X);
            Assert.Equal("images/  2 files", ToolTip.GetTip(cells[0]));   // the whole part, on hover
        }
        finally { window.Close(); }
    }
}
