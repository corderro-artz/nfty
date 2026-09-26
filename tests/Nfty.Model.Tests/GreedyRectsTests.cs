namespace Nfty.Model.Tests;

public class GreedyRectsTests
{
    /// <summary>One char per cell: a digit is that cell's key, '.' is unset.</summary>
    private static (int Width, int Height, int[] Keys) Grid(params string[] rows)
    {
        int h = rows.Length, w = rows[0].Length;
        var keys = new int[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                keys[(y * w) + x] = rows[y][x] == '.' ? GreedyRects.Unset : rows[y][x] - '0';
        return (w, h, keys);
    }

    private static List<PixelRect> Merge(params string[] rows)
    {
        var (w, h, keys) = Grid(rows);
        return GreedyRects.Merge(w, h, keys);
    }

    [Fact]
    public void A_full_rectangle_of_one_key_merges_into_exactly_one()
    {
        Assert.Equal(new[] { new PixelRect(0, 0, 3, 2, 0) }, Merge("000", "000"));
    }

    [Fact]
    public void Cells_of_different_keys_never_merge()
    {
        // The whole reason the grid is keyed: two columns of different depth must not share a face.
        Assert.Equal(
            new[] { new PixelRect(0, 0, 1, 1, 2), new PixelRect(1, 0, 1, 1, 0) },
            Merge("20"));
    }

    [Fact]
    public void A_checkerboard_merges_into_one_rectangle_per_cell_which_is_the_worst_case()
    {
        var rects = Merge("0.0", ".0.", "0.0");
        Assert.Equal(5, rects.Count);
        Assert.All(rects, r => Assert.Equal((1, 1), (r.Width, r.Height)));
    }

    [Fact]
    public void An_empty_grid_yields_nothing()
    {
        Assert.Empty(Merge("...", "..."));
    }

    [Fact]
    public void Rectangles_come_in_row_major_discovery_order_growing_right_then_down()
    {
        // Pinned exactly: the order reaches the model file, and the file must be byte-identical.
        Assert.Equal(
            new[]
            {
                new PixelRect(1, 0, 1, 3, 0),
                new PixelRect(0, 1, 1, 1, 0),
                new PixelRect(2, 1, 1, 1, 0),
            },
            Merge(".0.", "000", ".0."));
    }

    /// <summary>
    /// The property that matters and that a shape-by-shape test cannot state: the rectangles TILE
    /// the set cells — each covered exactly once, by a rectangle of its own key, and no unset cell
    /// covered at all. Double coverage emits coplanar faces that z-fight; under-coverage leaves a
    /// hole. Neither raises anything.
    /// </summary>
    [Theory]
    [InlineData("000", "0.0", "000")]
    [InlineData("..0", ".00", "000")]
    [InlineData("0..", "000", "..0")]
    [InlineData("0011", "0011", "2222")]
    [InlineData("0101", "1010", "0101")]
    [InlineData("3332", "3.12", "3312")]
    public void The_rectangles_tile_every_set_cell_exactly_once_with_its_own_key(params string[] rows)
    {
        var (w, h, keys) = Grid(rows);
        var cover = new int[w * h];

        foreach (var r in GreedyRects.Merge(w, h, keys))
        {
            Assert.True(r.Width >= 1 && r.Height >= 1, $"empty rectangle {r}");
            for (int y = r.Y; y < r.Y + r.Height; y++)
                for (int x = r.X; x < r.X + r.Width; x++)
                {
                    Assert.Equal(r.Key, keys[(y * w) + x]);
                    cover[(y * w) + x]++;
                }
        }

        for (int i = 0; i < keys.Length; i++)
            Assert.Equal(keys[i] < 0 ? 0 : 1, cover[i]);
    }

    [Fact]
    public void A_key_array_of_the_wrong_length_is_refused()
    {
        Assert.Throws<ArgumentException>(() => GreedyRects.Merge(2, 2, new int[3]));
    }
}
