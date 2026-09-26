using System.Numerics;

namespace Nfty.Model.Tests;

internal readonly record struct Point3(int X, int Y, int Z);

/// <summary>One quad of a risen mesh: every four consecutive vertices are one quad.</summary>
internal sealed record QuadView(Vector3 Normal, Vector3[] Corners, Vector2[] Uvs);

/// <summary>
/// The solidity checks. See Task 3 of the Rise step-5 plan for why "every edge is shared by exactly
/// two triangles" is not the property to assert: greedy faces meet per-pixel walls at T-junctions,
/// and pixels that touch only at a corner pinch four faces onto one edge. What IS true, and what
/// these assert, is that the surface is closed and consistently oriented at the level of unit
/// lattice segments, and that it encloses exactly the voxels it should.
/// </summary>
internal static class MeshChecks
{
    public static Vector3 Position(Mesh m, int v) =>
        new(m.Positions[v * 3], m.Positions[(v * 3) + 1], m.Positions[(v * 3) + 2]);

    public static Vector3 Normal(Mesh m, int v) =>
        new(m.Normals[v * 3], m.Normals[(v * 3) + 1], m.Normals[(v * 3) + 2]);

    public static Vector2 Uv(Mesh m, int v) => new(m.TexCoords[v * 2], m.TexCoords[(v * 2) + 1]);

    public static IReadOnlyList<QuadView> Quads(Mesh m)
    {
        var quads = new List<QuadView>();
        for (int q = 0; q < m.VertexCount / 4; q++)
        {
            int v = q * 4;
            quads.Add(new QuadView(
                Normal(m, v),
                [Position(m, v), Position(m, v + 1), Position(m, v + 2), Position(m, v + 3)],
                [Uv(m, v), Uv(m, v + 1), Uv(m, v + 2), Uv(m, v + 3)]));
        }
        return quads;
    }

    /// <summary>A vertex as an integer lattice point. Only meaningful for a mesh built with
    /// <see cref="Pivot.MinCorner"/> and a voxel size of 1, where every coordinate is an integer.</summary>
    public static Point3 Lattice(Mesh m, int vertex)
    {
        int Axis(int k)
        {
            float f = m.Positions[(vertex * 3) + k];
            int n = (int)MathF.Round(f);
            Assert.True(f == n,
                $"vertex {vertex} axis {k} is {f}, not a lattice value; build with Pivot.MinCorner and VoxelSize 1");
            return n;
        }
        return new Point3(Axis(0), Axis(1), Axis(2));
    }

    /// <summary>Every directed triangle edge, unrefined.</summary>
    public static Dictionary<(Point3 From, Point3 To), int> RawEdges(Mesh m)
    {
        var counts = new Dictionary<(Point3 From, Point3 To), int>();
        for (int t = 0; t < m.Indices.Count; t += 3)
            for (int e = 0; e < 3; e++)
            {
                var key = (Lattice(m, (int)m.Indices[t + e]), Lattice(m, (int)m.Indices[t + ((e + 1) % 3)]));
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        return counts;
    }

    /// <summary>
    /// Every directed triangle edge, with each axis-aligned edge split into unit lattice segments —
    /// which is what makes a T-junction countable. A diagonal (a quad's own split) is kept whole: it
    /// is walked once each way by the quad's two triangles and never touches another quad.
    /// </summary>
    public static Dictionary<(Point3 From, Point3 To), int> UnitSegments(Mesh m)
    {
        var counts = new Dictionary<(Point3 From, Point3 To), int>();
        void Add(Point3 a, Point3 b) => counts[(a, b)] = counts.GetValueOrDefault((a, b)) + 1;

        for (int t = 0; t < m.Indices.Count; t += 3)
            for (int e = 0; e < 3; e++)
            {
                var a = Lattice(m, (int)m.Indices[t + e]);
                var b = Lattice(m, (int)m.Indices[t + ((e + 1) % 3)]);
                int dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z;
                int moving = (dx != 0 ? 1 : 0) + (dy != 0 ? 1 : 0) + (dz != 0 ? 1 : 0);
                Assert.True(moving > 0, $"triangle {t / 3} has a zero-length edge at {a}");
                if (moving > 1)
                {
                    Add(a, b);
                    continue;
                }
                int steps = Math.Abs(dx + dy + dz);
                int sx = Math.Sign(dx), sy = Math.Sign(dy), sz = Math.Sign(dz);
                for (int k = 0; k < steps; k++)
                    Add(new Point3(a.X + (k * sx), a.Y + (k * sy), a.Z + (k * sz)),
                        new Point3(a.X + ((k + 1) * sx), a.Y + ((k + 1) * sy), a.Z + ((k + 1) * sz)));
            }
        return counts;
    }

    /// <summary>Closed and consistently oriented: every unit segment is walked exactly as often in
    /// one direction as in the other. A hole, a duplicated face or a flipped face all break it.</summary>
    public static void AssertClosed(Mesh m)
    {
        var segments = UnitSegments(m);
        foreach (var ((from, to), n) in segments)
        {
            int back = segments.GetValueOrDefault((to, from));
            Assert.True(back == n,
                $"{from}->{to} is walked {n} time(s) but {to}->{from} {back}: a hole, a duplicate face or a flipped one");
        }
    }

    /// <summary>The strict form, for shapes with no pinch: every unit segment exactly once each way.</summary>
    public static void AssertTwoManifold(Mesh m)
    {
        var segments = UnitSegments(m);
        foreach (var ((from, to), n) in segments)
        {
            Assert.True(n == 1, $"{from}->{to} is walked {n} times");
            Assert.True(segments.GetValueOrDefault((to, from)) == 1, $"{to}->{from} is not walked exactly once");
        }
    }

    /// <summary>Signed volume by the divergence theorem. Exact for lattice meshes; positive only
    /// when every face winds outward.</summary>
    public static double Volume(Mesh m)
    {
        double total = 0;
        for (int t = 0; t < m.Indices.Count; t += 3)
        {
            var a = Position(m, (int)m.Indices[t]);
            var b = Position(m, (int)m.Indices[t + 1]);
            var c = Position(m, (int)m.Indices[t + 2]);
            double cx = ((double)b.Y * c.Z) - ((double)b.Z * c.Y);
            double cy = ((double)b.Z * c.X) - ((double)b.X * c.Z);
            double cz = ((double)b.X * c.Y) - ((double)b.Y * c.X);
            total += (a.X * cx) + (a.Y * cy) + (a.Z * cz);
        }
        return total / 6;
    }

    /// <summary>Every triangle has area, and each of its vertices' stored normals is the unit normal
    /// its winding implies — so a face drawn inside-out is caught even where the volume is not.</summary>
    public static void AssertNormalsMatchWinding(Mesh m)
    {
        for (int t = 0; t < m.Indices.Count; t += 3)
        {
            int ia = (int)m.Indices[t], ib = (int)m.Indices[t + 1], ic = (int)m.Indices[t + 2];
            var a = Position(m, ia);
            var cross = Vector3.Cross(Position(m, ib) - a, Position(m, ic) - a);
            Assert.True(cross.LengthSquared() > 0, $"triangle {t / 3} is degenerate");
            var expected = Vector3.Normalize(cross);
            foreach (int v in new[] { ia, ib, ic })
                Assert.Equal(expected, Normal(m, v));
        }
    }
}
