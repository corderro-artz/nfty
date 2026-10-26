using System.Numerics;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Tests;

/// <summary>
/// THE anchor test for the geometry: whatever the shape and whatever the depth, the risen mesh is a
/// closed, consistently oriented surface enclosing exactly its voxels. One assertion that catches a
/// whole class of wall bug, none of which raises anything on its own.
/// </summary>
public class RiserSolidityTests
{
    private static readonly RiseOptions LatticeOptions = new() { Pivot = Pivot.MinCorner };

    private static Mesh Rise(string shape, string pattern)
    {
        using var art = Art.FromAscii(Shapes.All[shape]);
        return Riser.TryBuild(art, Art.Depth(art, Shapes.Patterns[pattern]), LatticeOptions)!;
    }

    public static TheoryData<string, string> EveryShapeAndPattern() => Shapes.EveryShapeAndPattern();

    public static TheoryData<string, string> UnpinchedShapesUnderUniformDepth() => Shapes.UnpinchedShapesUnderUniformDepth();

    [Theory]
    [MemberData(nameof(EveryShapeAndPattern))]
    public void Every_rise_is_closed_consistently_oriented_and_exactly_as_big_as_its_voxels(string shape, string pattern)
    {
        var mesh = Rise(shape, pattern);
        MeshChecks.AssertClosed(mesh);
        MeshChecks.AssertNormalsMatchWinding(mesh);
        Assert.Equal((double)Shapes.Voxels(shape, pattern), MeshChecks.Volume(mesh));
    }

    [Theory]
    [MemberData(nameof(UnpinchedShapesUnderUniformDepth))]
    public void A_rise_with_no_corner_only_contact_is_a_two_manifold(string shape, string pattern)
    {
        MeshChecks.AssertTwoManifold(Rise(shape, pattern));
    }

    [Fact]
    public void Greedy_faces_meet_per_pixel_walls_at_t_junctions_which_is_why_edges_are_refined()
    {
        // The worked example in the plan. The merged front face walks (2,1,1)->(0,1,1) in ONE edge;
        // the two top walls walk (0,1,1)->(1,1,1) and (1,1,1)->(2,1,1). Unrefined, the long edge has
        // no partner; refined, every unit segment does.
        var mesh = Rise("strip", "paper");
        var raw = MeshChecks.RawEdges(mesh);
        var p = (From: new Point3(2, 1, 1), To: new Point3(0, 1, 1));

        Assert.Equal(1, raw.GetValueOrDefault(p));
        Assert.Equal(0, raw.GetValueOrDefault((p.To, p.From)));

        var unit = MeshChecks.UnitSegments(mesh);
        Assert.Equal(1, unit.GetValueOrDefault((new Point3(2, 1, 1), new Point3(1, 1, 1))));
        Assert.Equal(1, unit.GetValueOrDefault((new Point3(1, 1, 1), new Point3(2, 1, 1))));
        MeshChecks.AssertTwoManifold(mesh);
    }

    [Fact]
    public void Pixels_touching_only_at_a_corner_pinch_one_edge_between_four_faces()
    {
        // "#." over ".#": the two cubes share the line X=1, Y=1. It is walked up by A's bottom wall
        // and B's top wall, and down by A's right wall and B's left wall — twice each way. Closed and
        // oriented; genuinely not a 2-manifold, and no correct geometry would make it one.
        using var art = Art.FromAscii("#.", ".#");
        var mesh = Riser.TryBuild(art, null, LatticeOptions)!;
        var unit = MeshChecks.UnitSegments(mesh);

        var up = (new Point3(1, 1, 0), new Point3(1, 1, 1));
        var down = (new Point3(1, 1, 1), new Point3(1, 1, 0));
        Assert.Equal(2, unit.GetValueOrDefault(up));
        Assert.Equal(2, unit.GetValueOrDefault(down));
        MeshChecks.AssertClosed(mesh);
    }

    [Fact]
    public void The_mesh_volume_is_the_sum_of_one_plus_max_front_plus_max_back()
    {
        using var rise = StackFixtures.BodyAndStrapOverPlus();
        var mesh = Riser.TryBuild(rise.Art, rise.Depth, LatticeOptions, "stack", rise.Faces)!;

        int voxels = 0;
        for (int y = 0; y < rise.Art.Height; y++)
            for (int x = 0; x < rise.Art.Width; x++)
                if (rise.Art[x, y].A == 255)
                    voxels += 1 + rise.Depth.Front(x, y) + rise.Depth.Back(x, y);

        Assert.Equal((double)voxels, MeshChecks.Volume(mesh));
        MeshChecks.AssertClosed(mesh);
        MeshChecks.AssertNormalsMatchWinding(mesh);
    }

    [Theory]
    [InlineData(0, 2)]   // all front colored
    [InlineData(1, 3)]   // seam inside the back segment
    [InlineData(2, 3)]
    [InlineData(3, 2)]   // seam at the end of the back segment: nothing to cut
    [InlineData(4, 2)]   // seam at the start of the front segment
    [InlineData(5, 3)]   // seam inside the front segment
    [InlineData(6, 3)]
    [InlineData(7, 2)]   // all back colored
    public void A_wall_crossing_the_seam_on_both_segments_stays_closed(int backColored, int seamWallQuads)
    {
        // The left column spans z [-3.5, 3.5]; its paper neighbor [-0.5, 0.5], so the shared wall has
        // a back segment [-3.5, -0.5] and a front segment [0.5, 3.5]. The seam may fall in either.
        using var art = Art.FromAscii("##");
        using var faces = new StackFaces(art.Clone(), art.Clone(), [backColored, 0]);
        var depth = Art.Depth(art, (x, _) => x == 0 ? (3, 3) : (0, 0));
        var mesh = Riser.TryBuild(art, depth, LatticeOptions, "x", faces)!;

        var shared = MeshChecks.Quads(mesh).Where(q => q.Normal == Vector3.UnitX && q.Corners.All(c => c.X == 1f)).ToList();
        Assert.Equal(seamWallQuads, shared.Count);
        MeshChecks.AssertClosed(mesh);
        MeshChecks.AssertNormalsMatchWinding(mesh);
        Assert.Equal(7.0 + 1.0, MeshChecks.Volume(mesh));
    }

    [Fact]
    public void A_single_picture_has_the_same_quad_count_as_before()
    {
        // L under "both" (1, 2) is 32 triangles = 16 quads on main, before any atlas or seam existed.
        using var art = Art.FromAscii(Shapes.All["L"]);
        var depth = Art.Depth(art, Shapes.Patterns["both"]);
        Assert.Equal(16, Riser.TryBuild(art, depth, LatticeOptions)!.TriangleCount / 2);

        using var uniform = StackFaces.Uniform(art);
        Assert.Equal(16, Riser.TryBuild(art, depth, LatticeOptions, "L", uniform)!.TriangleCount / 2);
    }
}
