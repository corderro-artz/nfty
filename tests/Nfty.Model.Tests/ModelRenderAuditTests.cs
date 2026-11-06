using System.Numerics;
using Nfty.Model.Render;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Tests;

public class ModelRenderAuditTests
{
    [Theory]
    [InlineData(13, 6, 7, 6)]
    [InlineData(23, 7, 8, 7)]
    public void Half_open_face_sampling_recovers_encoded_integer_texel_boundaries(int width, int min, int max, int expected)
    {
        var target = new RenderTarget(4, 4);
        target.Clear(default);
        var texels = Enumerable.Range(0, width).Select(x => new Rgba32((byte)x, 0, 0, 255)).ToArray();
        // The first row's centers lie exactly on the included top edge. The 13-wide case samples
        // the exclusive UV maximum; the 23-wide case samples the included UV minimum. Their encoded
        // grid coordinates multiply back to 7.0000005 and 6.9999995 respectively in float arithmetic.
        float boundary = 7f / width;
        float opposite = (width == 13 ? min : max) / (float)width;

        Raster.FillTriangle(new(0, .5f, 1, boundary, .5f), new(4, .5f, 1, boundary, .5f),
            new(0, 4.5f, 1, opposite, .5f), 1, texels, width, 1, target);

        Assert.Equal(expected, target.Color[0].R);
    }

    [Fact]
    public void A_non_grid_UV_limit_keeps_the_texel_reached_by_its_exact_float_value()
    {
        var target = new RenderTarget(4, 4);
        target.Clear(default);
        var texels = Enumerable.Range(0, 6).Select(x => new Rgba32((byte)x, 0, 0, 255)).ToArray();
        float max = MathF.BitIncrement(5f / 6);

        Raster.FillTriangle(new(0, .5f, 1, max, .5f), new(4, .5f, 1, max, .5f),
            new(0, 4.5f, 1, 4f / 6, .5f), 1, texels, 6, 1, target);

        Assert.Equal(5, target.Color[0].R);
    }

    [Fact]
    public void Greedy_front_face_T_junction_does_not_leave_an_interior_hole()
    {
        using var art = new Image<Rgba32>(8, 8);
        for (int y = 0; y < 8; y++)
        for (int x = 0; x < 8; x++)
            if (y < 4 || x < 4) art[x, y] = new Rgba32(200, 150, 100, 255);
        var mesh = Riser.TryBuild(art, null, new RiseOptions())!;
        using var texture = FaceAtlas.Build(art);
        var camera = new Camera(new Vector3(-.8502983f, 3.240854f, -.25174284f),
            16, -.9575553f, .62739354f, mesh.Min, mesh.Max);
        var target = new RenderTarget(64, 64);
        target.Clear(default);

        Raster.Render(mesh, texture, camera, target);

        // The upper rectangle's long lower edge meets two shorter edges. All 49 pixel rays land
        // inside the opaque L, including the formerly missing center at their projected junction.
        for (int y = 29; y <= 35; y++)
        for (int x = 29; x <= 35; x++)
            Assert.Equal(255, target.Color[(y * 64) + x].A);

        // Render each front triangle into its own target. A shared depth buffer would conceal
        // overdraw, so count the independent coverage to require exactly one owner at every sample.
        var coverage = new int[64 * 64];
        for (int t = 0; t < mesh.Indices.Count; t += 3)
        {
            if (mesh.Normals[((int)mesh.Indices[t] * 3) + 2] != 1) continue;
            var triangle = new Mesh(mesh.Positions.ToArray(), mesh.Normals.ToArray(), mesh.TexCoords.ToArray(),
                [mesh.Indices[t], mesh.Indices[t + 1], mesh.Indices[t + 2]], mesh.TextureWidth, mesh.TextureHeight);
            target.Clear(default);
            Raster.Render(triangle, texture, camera, target);
            for (int p = 0; p < coverage.Length; p++) if (target.Color[p].A > 0) coverage[p]++;
        }
        for (int y = 29; y <= 35; y++)
        for (int x = 29; x <= 35; x++)
            Assert.Equal(1, coverage[(y * 64) + x]);
    }

    [Fact]
    public void Greedy_T_junction_coverage_agrees_with_voxel_rays_across_views_and_near_clipping()
    {
        using var art = new Image<Rgba32>(8, 8);
        for (int y = 0; y < 8; y++)
        for (int x = 0; x < 8; x++)
            if (y < 4 || x < 4) art[x, y] = new Rgba32(200, 150, 100, 255);
        var mesh = Riser.TryBuild(art, null, new RiseOptions())!;
        using var texture = FaceAtlas.Build(art);
        var target = new RenderTarget(64, 64);
        int checkedInside = 0, checkedOutside = 0;
        foreach (float yaw in new[] { -1.2f, -.6f, 0, .6f, 1.2f })
        foreach (float pitch in new[] { -.8f, -.4f, 0, .4f, .8f })
        foreach (float distance in new[] { 1f, 4f, 16f })
        foreach (float x in new[] { -3.5f, -2f, -.5f, .5f, 2f, 3.5f })
        {
            var camera = new Camera((mesh.Min + mesh.Max) / 2, distance, yaw, pitch, mesh.Min, mesh.Max);
            var q = new Vector3(x, 4, .5f);
            var clip = Vector4.Transform(new Vector4(q, 1), camera.ViewProjection(64, 64));
            if (clip.W <= .01f * camera.Radius) continue;
            var screen = new Vector2((clip.X / clip.W + 1) * 32, (1 - clip.Y / clip.W) * 32);
            var forward = Vector3.Normalize(camera.Target - camera.Eye);
            var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
            var up = Vector3.Cross(right, forward);
            float worldPerPixel = 2 * clip.W * MathF.Tan(Camera.FovDegrees * MathF.PI / 360) / 64;
            var delta = -right * (32.5f - screen.X) * worldPerPixel + up * (32.5f - screen.Y) * worldPerPixel;
            camera = new Camera(camera.Target + delta, camera.Distance, yaw, pitch, mesh.Min, mesh.Max);
            target.Clear(default);
            Raster.Render(mesh, texture, camera, target);

            for (int py = 30; py <= 34; py++)
            for (int px = 30; px <= 34; px++)
            {
                var (entry, length) = VoxelRay(camera, art, px, py);
                if (entry > .01 * camera.Radius + .001 && double.IsFinite(entry) && length > .0001)
                {
                    Assert.True(target.Color[(py * 64) + px].A == 255,
                        $"T-junction hole ({px},{py}), yaw={yaw}, pitch={pitch}, distance={distance}, x={x}");
                    checkedInside++;
                }
                else if (double.IsPositiveInfinity(entry) && length < -.001)
                {
                    Assert.True(target.Color[(py * 64) + px].A == 0,
                        $"stray coverage ({px},{py}), yaw={yaw}, pitch={pitch}, distance={distance}, x={x}, gap={length}");
                    checkedOutside++;
                }
            }
        }
        Assert.True(checkedInside > 1000, $"Only {checkedInside} solid rays were checked");
        Assert.True(checkedOutside > 100, $"Only {checkedOutside} empty rays were checked");
    }

    [Theory]
    [InlineData(-1246.5706f, 1373.958f, 4678.8613f, -4679.2466f)]
    [InlineData(-5000.5f, -5000.5f, 5000.5f, 5000.5f)]
    public void Large_shared_edges_cover_each_pixel_once_without_epsilon_overdraw(float ax, float ay, float bx, float by)
    {
        // Two independently drawn halves of a convex quadrilateral which surrounds the viewport.
        // Separate targets reveal overlap even though an ordinary depth test would hide it.
        var a = new Raster.ScreenVertex(ax, ay, 1, 0, 0);
        var b = new Raster.ScreenVertex(bx, by, 1, 0, 0);
        float mx = (ax + bx) / 2, my = (ay + by) / 2;
        var c = new Raster.ScreenVertex(mx + (by - ay) * 2, my - (bx - ax) * 2, 1, 0, 0);
        var d = new Raster.ScreenVertex(mx - (by - ay) * 2, my + (bx - ax) * 2, 1, 0, 0);
        var first = new RenderTarget(128, 128);
        var second = new RenderTarget(128, 128);
        first.Clear(default);
        second.Clear(default);
        Rgba32[] texels = [new(255, 255, 255, 255)];

        Raster.FillTriangle(a, b, c, 1, texels, 1, 1, first);
        Raster.FillTriangle(b, a, d, 1, texels, 1, 1, second);

        for (int pixel = 0; pixel < first.Color.Length; pixel++)
            Assert.Equal(1, (first.Color[pixel].A > 0 ? 1 : 0) + (second.Color[pixel].A > 0 ? 1 : 0));
    }

    [Fact]
    public void Near_clipped_opaque_paper_has_no_hole_at_the_audited_pixel()
    {
        using var art = new Image<Rgba32>(16, 16, new Rgba32(200, 150, 100, 255));
        var mesh = Riser.TryBuild(art, null, new RiseOptions())!;
        using var texture = FaceAtlas.Build(art);
        var camera = new Camera((mesh.Min + mesh.Max) / 2, 0, Radians(6), Radians(-1), mesh.Min, mesh.Max).Zoom(1.2f);
        var target = new RenderTarget(128, 128);
        target.Clear(default);
        Raster.Render(mesh, texture, camera, target);

        // M01's isolated pinhole: every neighbor is covered, and the analytic ray enters the front
        // face well inside its rectangle. This must not depend on the face's shared diagonal.
        Assert.Equal(255, target.Color[(76 * 128) + 23].A);
        Assert.True(float.IsFinite(target.Depth[(76 * 128) + 23]));
    }

    [Theory]
    [InlineData(6, -1, 1.2f)]
    [InlineData(-6, 1, 1.2f)]
    [InlineData(12, -4, 1.05f)]
    [InlineData(-12, 4, 1.05f)]
    [InlineData(40, 20, 1.1f)]
    [InlineData(70, 50, 1.11f)]
    [InlineData(135, -30, 1.15f)]
    public void Opaque_box_coverage_agrees_with_independent_pixel_rays(float yaw, float pitch, float floorFactor)
    {
        using var art = new Image<Rgba32>(16, 16, new Rgba32(200, 150, 100, 255));
        var mesh = Riser.TryBuild(art, null, new RiseOptions())!;
        using var texture = FaceAtlas.Build(art);
        var camera = new Camera((mesh.Min + mesh.Max) / 2, 0, Radians(yaw), Radians(pitch), mesh.Min, mesh.Max).Zoom(floorFactor);
        const int width = 128, height = 128;
        var target = new RenderTarget(width, height);
        target.Clear(default);
        Raster.Render(mesh, texture, camera, target);

        // Analytic double-precision ray/slab intersections, without raster edges, clipping polygons,
        // mesh triangles or inverse matrices. Skip the silhouette/near boundary by a world-space
        // margin so the oracle does not impose a different convention on exact edge samples.
        double cy = Math.Cos(camera.Yaw), sy = Math.Sin(camera.Yaw);
        double cp = Math.Cos(camera.Pitch), sp = Math.Sin(camera.Pitch);
        double[] eyeDirection = [cp * sy, sp, cp * cy];
        double[] right = [cy, 0, -sy], up = [-sp * sy, cp, -sp * cy];
        double tan = Math.Tan(Camera.FovDegrees * Math.PI / 360);
        int checkedInside = 0, checkedOutside = 0;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            double sx = ((x + .5) / width * 2 - 1) * tan * width / height;
            double syScreen = (1 - (y + .5) / height * 2) * tan;
            double enter = double.NegativeInfinity, exit = double.PositiveInfinity;
            for (int axis = 0; axis < 3; axis++)
            {
                double origin = camera.Target[axis] + camera.Distance * eyeDirection[axis];
                double direction = -eyeDirection[axis] + sx * right[axis] + syScreen * up[axis];
                double a = (mesh.Min[axis] - origin) / direction;
                double b = (mesh.Max[axis] - origin) / direction;
                enter = Math.Max(enter, Math.Min(a, b));
                exit = Math.Min(exit, Math.Max(a, b));
            }

            double near = .01 * camera.Radius;
            if (enter > near + .001 && exit > enter + .001)
            {
                Assert.True(target.Color[(y * width) + x].A == 255,
                    $"hole at ({x},{y}), yaw={yaw}, pitch={pitch}, enter={enter}, exit={exit}");
                checkedInside++;
            }
            else if (exit < enter - .001 || exit < 0)
            {
                Assert.Equal(0, target.Color[(y * width) + x].A);
                checkedOutside++;
            }
        }
        Assert.True(checkedInside > 100);
        // The zoomed box can cover the entire viewport; outside assertions apply when rays miss.
        Assert.True(checkedInside + checkedOutside > 1000);
    }

    [Fact]
    public void Near_floor_anchored_zoom_does_not_reverse_and_keeps_the_audited_anchor()
    {
        var min = new Vector3(-8, 0, -1.5f);
        var max = new Vector3(8, 16, 1.5f);
        var camera = new Camera((min + max) / 2, 0, Radians(70), Radians(50), min, max).Zoom(1.11f);
        var pointer = new Vector2(300, 120);
        var anchor = Anchor(camera, pointer);

        var zoomed = camera.ZoomAnchored(.9f, pointer, 400, 300);

        Assert.True(zoomed.Distance < camera.Distance, $"{camera.Distance} -> {zoomed.Distance}");
        Assert.InRange(Vector2.Distance(pointer, Project(zoomed, anchor)), 0, 1);
        AssertCameraConfinement(zoomed);
    }

    [Fact]
    public void Anchored_wheel_in_preserves_direction_and_anchor_across_bounds_and_angles()
    {
        foreach (var (min, max) in new[]
        {
            (new Vector3(-8, 0, -1.5f), new Vector3(8, 16, 1.5f)),
            (new Vector3(-256, 0, -1.5f), new Vector3(256, 512, 1.5f)),
            (-Vector3.One * 2, Vector3.One * 2),
        })
        foreach (float yaw in new[] { -135f, -70, -6, 0, 6, 70, 135 })
        foreach (float pitch in new[] { -89f, -50, 0, 50, 89 })
        foreach (float floorFactor in new[] { 1f, 1.01f, 1.11f, 2f })
        foreach (var pointer in new[] { new Vector2(300, 120), new Vector2(100, 180), new Vector2(200, 150) })
        {
            var camera = new Camera((min + max) / 2, 0, Radians(yaw), Radians(pitch), min, max).Zoom(floorFactor);
            var anchor = Anchor(camera, pointer);
            var zoomed = camera.ZoomAnchored(.9f, pointer, 400, 300);
            Assert.True(zoomed.Distance <= camera.Distance, $"outward at yaw={yaw}, pitch={pitch}, floor={floorFactor}");
            Assert.InRange(Vector2.Distance(pointer, Project(zoomed, anchor)), 0, 1);
            AssertCameraConfinement(zoomed);
        }
    }

    [Fact]
    public void Extremely_narrow_framing_keeps_the_documented_maximum_and_is_best_effort()
    {
        var camera = Camera.FramedOn(new Vector3(-8, 0, -1.5f), new Vector3(8, 16, 1.5f), .05f);
        Assert.Equal(camera.MaxDistance, camera.Distance);
        var clip = Vector4.Transform(new Vector4(8, 16, 1.5f, 1), camera.ViewProjection(50, 1000));
        Assert.True(clip.X / clip.W > 1);
    }

    private static float Radians(float degrees) => degrees * MathF.PI / 180;

    private static (double Entry, double Length) VoxelRay(Camera camera, Image<Rgba32> art, int x, int y)
    {
        // Independent double-precision camera rays against each occupied unit voxel. No mesh,
        // projected edges, inverse projection or clipping implementation participates in the oracle.
        double cy = Math.Cos(camera.Yaw), sy = Math.Sin(camera.Yaw);
        double cp = Math.Cos(camera.Pitch), sp = Math.Sin(camera.Pitch);
        double[] eyeDirection = [cp * sy, sp, cp * cy];
        double[] right = [cy, 0, -sy], up = [-sp * sy, cp, -sp * cy];
        double tan = Math.Tan(Camera.FovDegrees * Math.PI / 360);
        double sx = ((x + .5) / 64 * 2 - 1) * tan, screenY = (1 - (y + .5) / 64 * 2) * tan;
        double nearest = double.PositiveInfinity, length = 0, closestMiss = double.NegativeInfinity;
        for (int yy = 0; yy < art.Height; yy++)
        for (int xx = 0; xx < art.Width; xx++)
        {
            if (art[xx, yy].A == 0) continue;
            double[] min = [xx - art.Width / 2.0, art.Height - yy - 1, -.5];
            double[] max = [min[0] + 1, min[1] + 1, .5];
            double enter = double.NegativeInfinity, exit = double.PositiveInfinity;
            for (int axis = 0; axis < 3; axis++)
            {
                double origin = camera.Target[axis] + camera.Distance * eyeDirection[axis];
                double direction = -eyeDirection[axis] + sx * right[axis] + screenY * up[axis];
                double a = (min[axis] - origin) / direction, b = (max[axis] - origin) / direction;
                enter = Math.Max(enter, Math.Min(a, b));
                exit = Math.Min(exit, Math.Max(a, b));
            }
            if (enter > 0) closestMiss = Math.Max(closestMiss, exit - enter);
            if (exit <= enter || enter <= 0 || enter >= nearest) continue;
            nearest = enter;
            length = exit - enter;
        }
        return (nearest, double.IsPositiveInfinity(nearest) ? closestMiss : length);
    }

    private static Vector3 Anchor(Camera camera, Vector2 pointer)
    {
        Matrix4x4.Invert(camera.ViewProjection(400, 300), out var inverse);
        Vector3 At(float z)
        {
            var p = Vector4.Transform(new Vector4(pointer.X / 200 - 1, 1 - pointer.Y / 150, z, 1), inverse);
            return new Vector3(p.X, p.Y, p.Z) / p.W;
        }
        return camera.Eye + Vector3.Normalize(At(1) - At(0)) * camera.Distance;
    }

    private static Vector2 Project(Camera camera, Vector3 anchor)
    {
        var p = Vector4.Transform(new Vector4(anchor, 1), camera.ViewProjection(400, 300));
        return new Vector2((p.X / p.W + 1) * 200, (1 - p.Y / p.W) * 150);
    }

    private static void AssertCameraConfinement(Camera camera)
    {
        for (int axis = 0; axis < 3; axis++)
            Assert.InRange(camera.Target[axis], camera.BoundsMin[axis], camera.BoundsMax[axis]);
        Assert.True(camera.Distance >= camera.MinDistance);
        Assert.Contains(Enumerable.Range(0, 3), axis =>
            camera.Eye[axis] < camera.BoundsMin[axis] || camera.Eye[axis] > camera.BoundsMax[axis]);
    }
}
