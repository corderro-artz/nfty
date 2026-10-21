using Nfty.Core.Editing;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.Core.Tests;

public class DepthEditingTests
{
    /// <summary>'#' solid in color A, '*' solid in color B, '.' clear, '~' half alpha.</summary>
    private static Image<Rgba32> Art(params string[] rows)
    {
        var img = new Image<Rgba32>(rows[0].Length, rows.Length);
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[0].Length; x++)
                img[x, y] = rows[y][x] switch
                {
                    '#' => new Rgba32(200, 120, 40, 255),
                    '*' => new Rgba32(30, 60, 200, 255),
                    '~' => new Rgba32(200, 40, 40, 128),
                    _ => new Rgba32(0, 0, 0, 0),
                };
        return img;
    }

    private static DepthSurface Surface(params string[] rows) { using var a = Art(rows); return new DepthSurface(a, null); }

    [Fact]
    public void A_new_surface_is_paper_on_every_solid_pixel()
    {
        var s = Surface("#.", ".*");
        Assert.Equal(2, s.SolidCount);
        Assert.Equal(new DepthPixel(0, 0), s.Get(0, 0));
        Assert.False(s.IsSolid(1, 0));
        Assert.Null(s.ToDepthMap());                          // flat has ONE spelling: absent
        Assert.Equal(DepthLook.Paper, s.Summary().Look);
    }

    [Fact]
    public void Stored_depth_on_a_transparent_pixel_is_dropped()
    {
        using var art = Art("#.");
        var stored = DepthMap.FromBytes(2, 1, new byte[] { 2, 1, 9, 9 });
        var s = new DepthSurface(art, stored);
        Assert.Equal(new DepthPixel(2, 1), s.Get(0, 0));
        Assert.Equal(new DepthPixel(0, 0), s.Get(1, 0));
        Assert.Equal(new byte[] { 2, 1, 0, 0 }, s.ToDepthMap()!.ToBytes());
    }

    [Fact]
    public void Set_ignores_transparent_and_out_of_bounds_pixels()
    {
        var s = Surface("#.");
        s.Set(1, 0, new DepthPixel(5, 5));
        s.Set(9, 9, new DepthPixel(5, 5));
        Assert.Equal(new DepthPixel(0, 0), s.Get(1, 0));
        Assert.Null(s.ToDepthMap());
    }

    [Fact]
    public void A_step_raises_each_touched_solid_pixel_once_however_often_the_path_crosses_it()
    {
        var s = Surface("###");
        var history = new EditHistory<DepthPixel>();
        var pixels = new[] { (0, 0), (1, 0), (0, 0), (2, 0), (1, 0) };
        Assert.True(history.Do(new DepthStep(pixels, DepthSide.Front, +1), s));
        Assert.All(new[] { 0, 1, 2 }, x => Assert.Equal(new DepthPixel(1, 0), s.Get(x, 0)));
    }

    [Fact]
    public void A_step_hands_the_base_each_coordinate_at_most_once()
    {
        // The base contract ("each coordinate at most once") is invisible through Apply — repeated
        // writes of one value land the same — so ask ComputePixels directly.
        var s = Surface("###");
        var step = new DepthStep(new[] { (0, 0), (1, 0), (0, 0), (1, 0) }, DepthSide.Front, +1);
        var compute = typeof(RegionEditCommand<DepthPixel>).GetMethod(
            "ComputePixels", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var computed = (IReadOnlyList<(int x, int y, DepthPixel pixel)>)compute.Invoke(step, new object[] { s })!;
        Assert.Equal(2, computed.Count);
    }

    [Fact]
    public void Lowering_floors_at_zero_and_raising_caps_at_255()
    {
        using var art = Art("##");
        var s = new DepthSurface(art, DepthMap.FromBytes(2, 1, new byte[] { 0, 0, 255, 255 }));
        var h = new EditHistory<DepthPixel>();
        Assert.False(h.Do(new DepthStep(new[] { (0, 0) }, DepthSide.Back, -1), s));   // already 0: no-op
        Assert.False(h.Do(new DepthStep(new[] { (1, 0) }, DepthSide.Front, +1), s));  // already 255
        Assert.False(h.CanUndo);
    }

    [Fact]
    public void A_step_over_transparent_pixels_changes_nothing_and_is_not_an_undo_entry()
    {
        var s = Surface("#.");
        var h = new EditHistory<DepthPixel>();
        Assert.False(h.Do(new DepthStep(new[] { (1, 0) }, DepthSide.Front, +1), s));
        Assert.False(h.CanUndo);
        Assert.Equal(new DepthPixel(0, 0), s.Get(1, 0));
    }

    [Fact]
    public void Front_and_back_are_independent()
    {
        var s = Surface("#");
        var h = new EditHistory<DepthPixel>();
        h.Do(new DepthStep(new[] { (0, 0) }, DepthSide.Back, +1), s);
        h.Do(new DepthStep(new[] { (0, 0) }, DepthSide.Back, +1), s);
        h.Do(new DepthStep(new[] { (0, 0) }, DepthSide.Front, +1), s);
        Assert.Equal(new DepthPixel(1, 2), s.Get(0, 0));
    }

    [Fact]
    public void Undo_and_redo_walk_the_history_exactly()
    {
        var s = Surface("##");
        var h = new EditHistory<DepthPixel>();
        h.Do(new DepthStep(new[] { (0, 0), (1, 0) }, DepthSide.Front, +1), s);
        h.Do(new DepthStep(new[] { (1, 0) }, DepthSide.Front, +1), s);
        h.Undo(s);
        Assert.Equal(new DepthPixel(1, 0), s.Get(1, 0));
        h.Undo(s);
        Assert.Null(s.ToDepthMap());
        h.Redo(s); h.Redo(s);
        Assert.Equal(new DepthPixel(2, 0), s.Get(1, 0));
    }

    [Fact]
    public void Fill_all_sets_every_solid_pixel_and_only_solid_pixels()
    {
        var s = Surface("#.*");
        Assert.True(new EditHistory<DepthPixel>().Do(new DepthFillAll(3, 1), s));
        Assert.Equal(new byte[] { 3, 1, 0, 0, 3, 1 }, s.ToDepthMap()!.ToBytes());
        Assert.Equal(new DepthSummary(DepthLook.Uniform, 3, 1), s.Summary());
    }

    [Fact]
    public void Same_color_region_is_four_connected_and_exact()
    {
        var s = Surface("##*", "#.*", "**#");
        Assert.Equal(new[] { (0, 0), (0, 1), (1, 0) }, s.SameColorRegion(0, 0).OrderBy(p => p).ToArray());
        Assert.Empty(s.SameColorRegion(1, 1));                // a clear pixel has no color to fill by
    }

    [Fact]
    public void The_histogram_counts_solid_pixels_by_total_thickness()
    {
        using var art = Art("###.");
        var s = new DepthSurface(art, DepthMap.FromBytes(4, 1, new byte[] { 0, 0, 1, 1, 1, 1, 7, 7 }));
        Assert.Equal(new[] { new ThicknessBucket(1, 1), new ThicknessBucket(3, 2) }, s.Histogram());
        Assert.Equal(DepthLook.Varied, s.Summary().Look);
    }

    [Fact]
    public void Partial_alpha_is_counted_and_is_never_solid()
    {
        var s = Surface("#~~");
        Assert.Equal(2, s.PartialCount);
        Assert.Equal(1, s.SolidCount);
        Assert.False(s.IsSolid(1, 0));
        Assert.Equal(DepthLook.Empty, Surface("..").Summary().Look);
    }

    [Fact]
    public void Same_depth_treats_absent_as_flat()
    {
        var s = Surface("#");
        Assert.True(s.SameDepthAs(null));
        new DepthFillAll(1, 0).Apply(s);
        Assert.False(s.SameDepthAs(null));
        Assert.True(s.SameDepthAs(s.ToDepthMap()));
    }

    [Fact]
    public void A_step_refuses_a_delta_that_is_not_one() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new DepthStep(new[] { (0, 0) }, DepthSide.Front, 2));
}
