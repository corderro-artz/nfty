using System.Numerics;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Tests;

public class RiserTests
{
    private static readonly RiseOptions LatticeOptions = new() { Pivot = Pivot.MinCorner };

    private static Mesh Rise(string[] rows, Func<int, int, (int Front, int Back)>? depth = null, RiseOptions? options = null)
    {
        using var art = Art.FromAscii(rows);
        DepthMap? map = depth is null ? null : Art.Depth(art, depth);
        return Riser.TryBuild(art, map, options ?? LatticeOptions)
            ?? throw new InvalidOperationException("the fixture has no solid pixel");
    }

    [Fact]
    public void A_single_paper_pixel_is_a_cube_of_twelve_triangles()
    {
        var mesh = Rise(["#"]);
        Assert.Equal(12, mesh.TriangleCount);
        Assert.Equal(24, mesh.VertexCount);
    }

    [Theory]
    [InlineData("single", 12)]
    [InlineData("strip", 16)]
    [InlineData("square", 28)]
    [InlineData("plus", 36)]
    [InlineData("L", 32)]
    [InlineData("hole", 48)]
    [InlineData("checkerboard", 60)]
    public void Uniform_depth_changes_the_height_and_never_the_triangle_count(string shape, int triangles)
    {
        foreach (string pattern in Shapes.Uniform)
            Assert.Equal(triangles, Rise(Shapes.All[shape], Shapes.Patterns[pattern]).TriangleCount);
    }

    [Fact]
    public void A_neighbor_inside_both_ends_of_a_column_splits_its_wall_in_two()
    {
        // Left column (2, 2) spans z [-2.5, 2.5]; its paper neighbor spans [-0.5, 0.5]. The seam wall
        // is the difference: one piece behind the paper and one in front of it. After MinCorner
        // shifts z by 2.5 those are [0, 2] and [3, 5].
        var mesh = Rise(["##"], (x, _) => x == 0 ? (2, 2) : (0, 0));

        var seam = MeshChecks.Quads(mesh)
            .Where(q => q.Normal == Vector3.UnitX && q.Corners.All(c => c.X == 1f))
            .Select(q => (Low: q.Corners.Min(c => c.Z), High: q.Corners.Max(c => c.Z)))
            .OrderBy(s => s.Low)
            .ToArray();

        Assert.Equal(new[] { (0f, 2f), (3f, 5f) }, seam);
        Assert.Equal(24, mesh.TriangleCount);
    }

    [Fact]
    public void Three_alternating_columns_are_thirty_six_triangles()
    {
        Assert.Equal(36, Rise(["###"], (x, y) => (x + y) % 2 == 0 ? (2, 2) : (0, 0)).TriangleCount);
    }

    [Fact]
    public void Front_rises_toward_the_viewer_and_back_away_from_them_around_one_paper_plane()
    {
        // Left paper (0,0): front at +0.5, back at -0.5. Right (2,1): front at 2.5, back at -1.5.
        // MinCorner shifts z by 1.5, so fronts land at 2 and 4, backs at 1 and 0.
        var mesh = Rise(["##"], (x, _) => x == 0 ? (0, 0) : (2, 1));
        var quads = MeshChecks.Quads(mesh);

        var fronts = quads.Where(q => q.Normal == Vector3.UnitZ).Select(q => q.Corners[0].Z).Order().ToArray();
        var backs = quads.Where(q => q.Normal == -Vector3.UnitZ).Select(q => q.Corners[0].Z).Order().ToArray();

        Assert.Equal(new[] { 2f, 4f }, fronts);
        Assert.Equal(new[] { 0f, 1f }, backs);
    }

    [Fact]
    public void Image_row_zero_is_the_top_of_the_model()
    {
        // "#." over "##": the tall rectangle is column 0; the lone rectangle is the BOTTOM-right
        // pixel, which must sit at world Y [0, 1], not [1, 2].
        var mesh = Rise(["#.", "##"]);
        var fronts = MeshChecks.Quads(mesh)
            .Where(q => q.Normal == Vector3.UnitZ)
            .Select(q => (q.Corners.Min(c => c.X), q.Corners.Min(c => c.Y), q.Corners.Max(c => c.X), q.Corners.Max(c => c.Y)))
            .ToArray();

        Assert.Equal(new[] { (0f, 0f, 1f, 2f), (1f, 0f, 2f, 1f) }, fronts);
    }

    [Theory]
    [InlineData(Pivot.BottomCenter, -1.5f, 0f, -0.5f, 1.5f, 3f, 0.5f)]
    [InlineData(Pivot.Center, -1.5f, -1.5f, -0.5f, 1.5f, 1.5f, 0.5f)]
    [InlineData(Pivot.MinCorner, 0f, 0f, 0f, 3f, 3f, 1f)]
    public void Each_pivot_puts_its_named_point_of_the_bounding_box_on_the_origin(
        Pivot pivot, float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
    {
        var mesh = Rise(Shapes.All["plus"], options: new RiseOptions { Pivot = pivot });
        Assert.Equal(new Vector3(minX, minY, minZ), mesh.Min);
        Assert.Equal(new Vector3(maxX, maxY, maxZ), mesh.Max);
    }

    [Fact]
    public void Bottom_center_centers_the_solid_in_depth_not_the_paper()
    {
        // front 2, back 1: z spans [-1.5, 2.5] raw, 4 deep, so centered it is [-2, 2].
        var mesh = Rise(["#"], (_, _) => (2, 1), new RiseOptions());
        Assert.Equal(new Vector3(-0.5f, 0f, -2f), mesh.Min);
        Assert.Equal(new Vector3(0.5f, 1f, 2f), mesh.Max);
    }

    [Fact]
    public void Voxel_size_scales_every_coordinate()
    {
        var mesh = Rise(Shapes.All["plus"], options: new RiseOptions { Pivot = Pivot.MinCorner, VoxelSize = 0.25f });
        Assert.Equal(Vector3.Zero, mesh.Min);
        Assert.Equal(new Vector3(0.75f, 0.75f, 0.25f), mesh.Max);
    }

    [Fact]
    public void Where_the_sprite_sits_on_its_canvas_does_not_move_the_model()
    {
        var alone = Rise(["#"], options: new RiseOptions());
        var inCanvas = Rise([".....", "...#.", "....."], options: new RiseOptions());
        Assert.Equal(alone.Positions, inCanvas.Positions);
        Assert.Equal(alone.Indices, inCanvas.Indices);
    }

    [Fact]
    public void Faces_sample_the_art_they_cover()
    {
        // ".##." : one merged rectangle over columns 1..2 of a 4-wide, 1-tall image.
        var mesh = Rise([".##."]);
        var expected = new[] { new Vector2(0.25f, 1f), new Vector2(0.75f, 1f), new Vector2(0.75f, 0f), new Vector2(0.25f, 0f) };

        var front = Assert.Single(MeshChecks.Quads(mesh), q => q.Normal == Vector3.UnitZ);
        Assert.Equal(expected, front.Uvs);

        var back = Assert.Single(MeshChecks.Quads(mesh), q => q.Normal == -Vector3.UnitZ);
        Assert.Equal(expected.ToHashSet(), back.Uvs.ToHashSet());
    }

    [Theory]
    [InlineData(".#", 0.75f)]
    [InlineData("#.", 0.25f)]
    public void Walls_take_their_pixels_center_color(string row, float u)
    {
        var walls = MeshChecks.Quads(Rise([row])).Where(q => q.Normal.Z == 0f).ToList();
        Assert.Equal(4, walls.Count);
        Assert.All(walls, w => Assert.All(w.Uvs, uv => Assert.Equal(new Vector2(u, 0.5f), uv)));
    }

    [Fact]
    public void Vertices_are_shared_within_a_quad_and_nowhere_else()
    {
        var mesh = Rise(Shapes.All["plus"], Shapes.Patterns["alternate"]);
        Assert.Equal(2 * mesh.TriangleCount, mesh.VertexCount);
        for (int q = 0; q < mesh.TriangleCount / 2; q++)
        {
            uint b = (uint)(q * 4);
            Assert.Equal(new[] { b, b + 1, b + 2, b, b + 2, b + 3 }, mesh.Indices.Skip(q * 6).Take(6));
        }
    }

    [Fact]
    public void Depth_on_a_transparent_pixel_is_ignored()
    {
        var plain = Rise(["#."]);
        var withStrayDepth = Rise(["#."], (x, _) => x == 1 ? (5, 5) : (0, 0));
        Assert.Equal(plain.Positions, withStrayDepth.Positions);
    }

    [Fact]
    public void Art_with_nothing_solid_returns_null_rather_than_throwing()
    {
        // D5. A rolled asset can legitimately be empty; a batch must be able to skip and name it.
        using var art = Art.FromAscii("..", "..");
        Assert.Null(Riser.TryBuild(art, null, new RiseOptions()));
    }

    [Fact]
    public void Partial_alpha_is_refused_by_name()
    {
        using var art = Art.FromAscii("#~");
        var ex = Assert.Throws<PartialAlphaException>(() =>
            Riser.TryBuild(art, null, new RiseOptions(), "variant 'red' of hat.igt"));

        Assert.Equal("variant 'red' of hat.igt", ex.ItemName);
        Assert.Equal((1, 0, (byte)128, 1), (ex.X, ex.Y, ex.Alpha, ex.Count));
        Assert.Contains("variant 'red' of hat.igt", ex.Message, StringComparison.Ordinal);
        Assert.Contains("(1, 0)", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Partial_alpha_is_checked_before_emptiness_so_it_is_refused_not_skipped()
    {
        using var art = Art.FromAscii("~~");
        var ex = Assert.Throws<PartialAlphaException>(() => Riser.TryBuild(art, null, new RiseOptions()));
        Assert.Equal(2, ex.Count);
    }

    [Fact]
    public void Alpha_254_is_partial_and_only_255_is_solid()
    {
        // D1: one threshold, Opacity's. Nothing rounds 254 up.
        var nearly = new Rgba32(1, 2, 3, 254);
        Assert.True(Opacity.IsPartial(nearly));
        using var art = new Image<Rgba32>(1, 1, nearly);
        Assert.Throws<PartialAlphaException>(() => Riser.TryBuild(art, null, new RiseOptions()));

        using var solid = new Image<Rgba32>(1, 1, new Rgba32(1, 2, 3, 255));
        Assert.NotNull(Riser.TryBuild(solid, null, new RiseOptions()));
    }

    [Fact]
    public void A_depth_map_of_another_size_is_refused()
    {
        using var art = Art.FromAscii("##");
        Assert.Throws<ArgumentException>(() => Riser.TryBuild(art, DepthMap.Flat(1, 1), new RiseOptions()));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void A_voxel_size_that_is_not_positive_and_finite_is_refused(float size)
    {
        using var art = Art.FromAscii("#");
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Riser.TryBuild(art, null, new RiseOptions { VoxelSize = size }));
    }

    [Fact]
    public void Rising_the_same_input_twice_gives_identical_meshes()
    {
        var a = Rise(Shapes.All["hole"], Shapes.Patterns["staircase"]);
        var b = Rise(Shapes.All["hole"], Shapes.Patterns["staircase"]);
        Assert.Equal(a.Positions, b.Positions);
        Assert.Equal(a.Normals, b.Normals);
        Assert.Equal(a.TexCoords, b.TexCoords);
        Assert.Equal(a.Indices, b.Indices);
    }
}
