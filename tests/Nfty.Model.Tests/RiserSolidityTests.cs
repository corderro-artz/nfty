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
}
