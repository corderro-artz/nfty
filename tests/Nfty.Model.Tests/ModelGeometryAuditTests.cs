using System.Numerics;

namespace Nfty.Model.Tests;

public class ModelGeometryAuditTests
{
    [Theory]
    [InlineData(Pivot.Center, false)]
    [InlineData(Pivot.BottomCenter, false)]
    [InlineData(Pivot.Center, true)]
    [InlineData(Pivot.BottomCenter, true)]
    public void A_scale_that_collapses_positive_edges_is_refused(Pivot pivot, bool deepest)
    {
        using var art = Art.FromAscii("#");
        var depth = deepest ? DepthFill.All(art, 255, 255) : null;

        var error = Assert.Throws<ArgumentOutOfRangeException>(() => Riser.TryBuild(art, depth,
            new RiseOptions { VoxelSize = float.Epsilon, Pivot = pivot }));

        Assert.Equal("options", error.ParamName);
        Assert.Contains("represent", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    public static TheoryData<Pivot, float, bool> RepresentableScales()
    {
        var cases = new TheoryData<Pivot, float, bool>();
        foreach (var pivot in Enum.GetValues<Pivot>())
            foreach (float scale in new[]
            {
                float.BitIncrement(float.Epsilon), 1e-30f,
                float.BitDecrement(0.001f), 0.001f, float.BitIncrement(0.001f),
                1f, float.BitDecrement(1000f), 1000f, float.BitIncrement(1000f),
            })
                foreach (bool deepest in new[] { false, true })
                    cases.Add(pivot, scale, deepest);
        cases.Add(Pivot.MinCorner, float.Epsilon, false);
        cases.Add(Pivot.MinCorner, float.Epsilon, true);
        foreach (var pivot in Enum.GetValues<Pivot>())
            cases.Add(pivot, float.MaxValue, false);
        return cases;
    }

    [Theory]
    [MemberData(nameof(RepresentableScales))]
    public void Representable_scales_preserve_real_triangles_even_when_float_cross_products_underflow(
        Pivot pivot, float scale, bool deepest)
    {
        using var art = Art.FromAscii("#");
        var depth = deepest ? DepthFill.All(art, 255, 255) : null;
        var mesh = Riser.TryBuild(art, depth, new RiseOptions { VoxelSize = scale, Pivot = pivot });

        Assert.NotNull(mesh);
        Assert.Equal(12, mesh.TriangleCount);
        Assert.All(mesh.Positions, p => Assert.True(float.IsFinite(p)));
        AssertNondegenerate(mesh);
    }

    [Theory]
    [InlineData(Pivot.Center)]
    [InlineData(Pivot.BottomCenter)]
    [InlineData(Pivot.MinCorner)]
    public void An_overflowing_max_depth_transform_is_still_refused(Pivot pivot)
    {
        using var art = Art.FromAscii("#");
        var depth = DepthFill.All(art, 255, 255);

        Assert.ThrowsAny<ArgumentException>(() => Riser.TryBuild(art, depth,
            new RiseOptions { VoxelSize = float.MaxValue, Pivot = pivot }));
    }

    [Theory]
    [InlineData(Pivot.MinCorner, 1, 0, 0, 0, 1, 1, 1)]
    [InlineData(Pivot.Center, 2, -1, -1, -1, 1, 1, 1)]
    [InlineData(Pivot.BottomCenter, 2, -1, 0, -1, 1, 2, 1)]
    public void A_representable_tiny_transform_keeps_its_coordinates_without_clamping(Pivot pivot,
        int scaleUnits, int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
    {
        using var art = Art.FromAscii("#");
        var mesh = Riser.TryBuild(art, null,
            new RiseOptions { VoxelSize = scaleUnits * float.Epsilon, Pivot = pivot })!;

        Assert.Equal(new Vector3(minX * float.Epsilon, minY * float.Epsilon, minZ * float.Epsilon), mesh.Min);
        Assert.Equal(new Vector3(maxX * float.Epsilon, maxY * float.Epsilon, maxZ * float.Epsilon), mesh.Max);
    }

    [Fact]
    public void A_tiny_scale_that_collapses_a_wall_step_is_refused_even_when_bounds_have_volume()
    {
        using var art = Art.FromAscii("##", "##");
        var depth = Art.Depth(art, (x, _) => x == 0 ? (0, 0) : (255, 255));

        Assert.Throws<ArgumentOutOfRangeException>(() => Riser.TryBuild(art, depth,
            new RiseOptions { VoxelSize = float.Epsilon, Pivot = Pivot.Center }));
    }

    private static void AssertNondegenerate(Mesh mesh)
    {
        for (int i = 0; i < mesh.Indices.Count; i += 3)
        {
            int a = checked((int)mesh.Indices[i] * 3);
            int b = checked((int)mesh.Indices[i + 1] * 3);
            int c = checked((int)mesh.Indices[i + 2] * 3);
            // Subtract in double too: float arithmetic can underflow even for a real triangle.
            double ux = (double)mesh.Positions[b] - mesh.Positions[a];
            double uy = (double)mesh.Positions[b + 1] - mesh.Positions[a + 1];
            double uz = (double)mesh.Positions[b + 2] - mesh.Positions[a + 2];
            double vx = (double)mesh.Positions[c] - mesh.Positions[a];
            double vy = (double)mesh.Positions[c + 1] - mesh.Positions[a + 1];
            double vz = (double)mesh.Positions[c + 2] - mesh.Positions[a + 2];
            Assert.True((uy * vz) - (uz * vy) != 0 || (uz * vx) - (ux * vz) != 0 || (ux * vy) - (uy * vx) != 0,
                $"Triangle {i / 3} has no geometric area.");
        }
    }
}
