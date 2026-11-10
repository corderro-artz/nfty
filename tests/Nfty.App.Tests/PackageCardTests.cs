using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Controls;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>The shared manifest card: it reads Core's part strings as name + detail, lays them out as tiles whose
/// column count follows the width, and pages them behind its pager when the host limits the rows.</summary>
public class PackageCardTests
{
    private static readonly string[] Eight =
        ["ChestDemo.cbk", "images/  100 files", "metadata/  100 files", "3D models (100)", "nfty/  100 files", "set.json",
         "collection.json", "spritesheet.png  10 x 10 cells, 1.2 MB"];

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

    private static (Window Window, PackageCard Card) Show(double width, int maxRows)
    {
        var card = new PackageCard { Title = "chest.set", Size = "2 KB", Parts = Eight, MaxRows = maxRows, MinTileWidth = 140 };
        var window = new Window { Content = card, Width = width, Height = 400 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, card);
    }

    private static List<Border> ShownTiles(PackageCard card) =>
        card.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("pkgtile") && b.IsVisible).ToList();

    [AvaloniaTheory]
    [InlineData(320)]
    [InlineData(620)]
    [InlineData(1100)]
    public void Tiles_are_one_height_and_line_up_in_as_many_columns_as_the_width_allows(double width)
    {
        var (window, card) = Show(width, 0);
        try
        {
            var tiles = ShownTiles(card);
            Assert.Equal(Eight.Length, tiles.Count);
            Assert.All(tiles, t => Assert.Equal(PackageCard.TileHeight, t.Bounds.Height));
            var columns = tiles.Select(t => t.Bounds.X).Distinct().Count();
            Assert.True(columns > 1, "even the narrow card puts tiles side by side");
            Assert.All(tiles, t => Assert.True(t.Bounds.Width >= 140 - 0.5, $"a tile {t.Bounds.Width:0} wide at {width}"));
            // a wider card holds more per row, never fewer
            if (width > 1000) Assert.True(columns >= 6, $"{columns} columns at {width}");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_host_short_of_height_pages_the_parts_and_the_card_never_changes_size()
    {
        var (window, card) = Show(320, 1);
        try
        {
            var tiles = ShownTiles(card);
            int perPage = tiles.Count;
            Assert.True(perPage is > 0 and < 8);
            Assert.True(card.PageCount > 1);
            double height = card.Bounds.Height;
            var seen = new HashSet<string>(tiles.Select(t => (string)ToolTip.GetTip(t)!));
            for (int p = 1; p < card.PageCount; p++)
            {
                card.Turn(+1);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(height, card.Bounds.Height);
                foreach (var t in ShownTiles(card)) seen.Add((string)ToolTip.GetTip(t)!);
            }
            Assert.Equal(Eight.Length, seen.Count);          // every part reachable, none twice
            card.Turn(+1);                                    // past the last page: stays
            Assert.Equal(card.PageCount - 1, card.Page);
        }
        finally { window.Close(); }
    }
}
