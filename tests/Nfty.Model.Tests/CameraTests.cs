using System.Numerics;
using Nfty.Model.Render;

namespace Nfty.Model.Tests;

public class CameraTests
{
    private const float Radius = 2f;

    private static Camera Cam(float distance, float yaw = 0f, float pitch = 0f, float radius = Radius) =>
        new(Vector3.Zero, distance, yaw, pitch, radius);

    [Fact]
    public void A_cube_viewed_down_minus_z_lands_its_eight_corners_at_hand_computed_pixels()
    {
        // Non-square viewport so the aspect ratio is pinned too. Camera on +Z at distance 4, looking
        // at the origin: a corner (x, y, z) sits at view depth 4 - z, and with f = 1 / tan(20 deg)
        //   x_ndc = (f / aspect) * x / depth        y_ndc = f * y / depth
        // then to pixels: sx = (x_ndc / 2 + 1/2) * width, sy = (1/2 - y_ndc / 2) * height.
        const int width = 300, height = 200;
        double f = 1.0 / Math.Tan(20.0 * Math.PI / 180.0);
        double aspect = width / (double)height;
        var vp = Cam(distance: 4f).ViewProjection(width, height);

        foreach (float x in new[] { -1f, 1f })
        foreach (float y in new[] { -1f, 1f })
        foreach (float z in new[] { -1f, 1f })
        {
            double depth = 4.0 - z;
            double sx = (f / aspect * x / depth / 2.0 + 0.5) * width;
            double sy = (0.5 - f * y / depth / 2.0) * height;

            var got = Project(new Vector3(x, y, z), vp, width, height);

            Assert.True(Math.Abs(got.X - sx) < 0.01, $"({x},{y},{z}) x: got {got.X}, expected {sx}");
            Assert.True(Math.Abs(got.Y - sy) < 0.01, $"({x},{y},{z}) y: got {got.Y}, expected {sy}");
        }
    }

    [Fact]
    public void Orbiting_past_the_pitch_limit_clamps_instead_of_flipping()
    {
        var camera = Cam(4f);
        float limit = Camera.PitchLimitDegrees * MathF.PI / 180f;

        var pushedPastTop = camera.Orbit(0f, deltaPitchRadians: limit + 1f);
        Assert.Equal(limit, pushedPastTop.Pitch, precision: 5);

        var pushedPastBottom = camera.Orbit(0f, deltaPitchRadians: -(limit + 1f));
        Assert.Equal(-limit, pushedPastBottom.Pitch, precision: 5);
    }

    [Fact]
    public void Orbit_never_clamps_yaw()
    {
        var spun = Cam(4f).Orbit(deltaYawRadians: 100f, 0f); // many full turns
        Assert.Equal(100f, spun.Yaw, precision: 5);
    }

    [Fact]
    public void Zoom_scales_distance_and_clamps_at_the_minimum()
    {
        var camera = Cam(4f);
        var closer = camera.Zoom(0.5f);
        Assert.Equal(2f, closer.Distance, precision: 4);

        var clamped = camera.Zoom(0.00001f);
        Assert.Equal(0.05f * Radius, camera.MinDistance, precision: 5);
        Assert.Equal(camera.MinDistance, clamped.Distance, precision: 5);
    }

    [Fact]
    public void Zooming_far_out_stops_at_twenty_radii_and_back_in_stops_at_a_twentieth()
    {
        var camera = Cam(4f, radius: 3f);
        Assert.Equal(60f, camera.MaxDistance, precision: 4);

        var farthest = camera.Zoom(1e6f);
        Assert.Equal(60f, farthest.Distance, precision: 4);
        Assert.Equal(60f, farthest.Zoom(1.1f).Distance, precision: 4);

        var nearest = farthest.Zoom(1e-6f);
        Assert.Equal(0.15f, nearest.Distance, precision: 5);
    }

    [Fact]
    public void Every_apparent_mutator_carries_the_radius_through()
    {
        var camera = Cam(4f, radius: 7f);
        var pointer = new Vector2(10f, 10f);

        Assert.Equal(7f, camera.Orbit(0.1f, 0.1f).Radius);
        Assert.Equal(7f, camera.Zoom(0.9f).Radius);
        Assert.Equal(7f, camera.Pan(new Vector2(5, 5), 100).Radius);
        Assert.Equal(7f, camera.ZoomAnchored(0.9f, pointer, 100, 100).Radius);
        // Reset reframes on new bounds, so it carries THEIR radius: half the box diagonal.
        Assert.Equal(MathF.Sqrt(3f), camera.Reset(-Vector3.One, Vector3.One).Radius, precision: 5);
    }

    [Theory]
    [InlineData(16f, 1f)]
    [InlineData(64f, 1f)]
    [InlineData(512f, 1f)]
    [InlineData(1000f, 1f)]
    [InlineData(512f, 0.5f)]
    [InlineData(1000f, 0.5f)]
    [InlineData(1000f, 2f)]
    public void Framing_a_bounds_puts_all_eight_corners_inside_the_viewport(float w, float aspect)
    {
        // A W x W x 3 slab, BottomCenter placement: the shape Riser makes of W-pixel art.
        var min = new Vector3(-w / 2f, 0f, -1.5f);
        var max = new Vector3(w / 2f, w, 1.5f);
        var camera = Camera.FramedOn(min, max, aspect);

        int height = 400, width = (int)(400 * aspect);
        var vp = camera.ViewProjection(width, height);

        foreach (float x in new[] { min.X, max.X })
        foreach (float y in new[] { min.Y, max.Y })
        foreach (float z in new[] { min.Z, max.Z })
        {
            var clip = Vector4.Transform(new Vector4(x, y, z, 1f), vp);
            Assert.True(clip.W > 0f, $"corner ({x},{y},{z}) is behind the eye");
            float ndcX = clip.X / clip.W, ndcY = clip.Y / clip.W;
            Assert.InRange(ndcX, -1f, 1f);
            Assert.InRange(ndcY, -1f, 1f);
        }
    }

    [Fact]
    public void A_tall_viewport_is_framed_farther_back_than_a_square_one()
    {
        var min = new Vector3(-8, 0, -1.5f);
        var max = new Vector3(8, 16, 1.5f);

        float square = Camera.FramedOn(min, max, 1f).Distance;
        float tall = Camera.FramedOn(min, max, 0.5f).Distance;
        float wide = Camera.FramedOn(min, max, 2f).Distance;

        Assert.True(tall > square);
        Assert.Equal(square, wide, precision: 4); // wide viewports are limited by the vertical fov
        Assert.Equal(tall, Camera.FramedOn(min, max, 0.5f).Reset(min, max, 0.5f).Distance, precision: 4);
    }

    [Fact]
    public void The_viewport_mapping_round_trips_and_flips_y()
    {
        var ndc = new Vector2(0.25f, -0.5f);
        var screen = Viewport.NdcToScreen(ndc, 200, 100);
        Assert.Equal(125f, screen.X, precision: 4);
        Assert.Equal(75f, screen.Y, precision: 4); // ndc y -0.5 is BELOW center, so a larger row

        var back = Viewport.ScreenToNdc(screen, 200, 100);
        Assert.Equal(ndc.X, back.X, precision: 5);
        Assert.Equal(ndc.Y, back.Y, precision: 5);
    }

    [Fact]
    public void The_head_light_is_a_unit_vector_that_faces_the_viewer_at_any_orbit_angle()
    {
        foreach (var camera in new[] { Cam(4f), Cam(4f, 0.7f, -0.3f), Cam(9f, -2.5f, 1.2f) })
        {
            var light = camera.LightDirection;
            var toEye = Vector3.Normalize(camera.Eye - camera.Target);
            Assert.Equal(1f, light.Length(), precision: 4);
            // 0.85 / |(0.85, 0.35, -0.25)| = 0.8923: a face looking straight at the eye lands on
            // the top quantization level (lit >= 0.75) whatever the camera is doing.
            Assert.Equal(0.8923f, Vector3.Dot(light, toEye), precision: 3);
        }
    }

    [Fact]
    public void The_head_light_sits_up_and_to_the_left_of_the_eye()
    {
        // Yaw 0, pitch 0: the eye is on +Z, screen-right is +X, up is +Y. The light direction is
        // normalize(0.85 * toEye + 0.35 * up - 0.25 * right) = (-0.25, 0.35, 0.85) / 0.9526.
        var light = Cam(4f).LightDirection;
        float norm = MathF.Sqrt((0.25f * 0.25f) + (0.35f * 0.35f) + (0.85f * 0.85f));
        Assert.Equal(-0.25f / norm, light.X, precision: 4);
        Assert.Equal(0.35f / norm, light.Y, precision: 4);
        Assert.Equal(0.85f / norm, light.Z, precision: 4);
    }

    [Fact]
    public void Camera_is_immutable_every_mutator_returns_a_new_instance()
    {
        var camera = Cam(4f);
        var orbited = camera.Orbit(1f, 0f);
        // The original is untouched — this is what "no field written after construction" buys.
        Assert.Equal(0f, camera.Yaw, precision: 5);
        Assert.Equal(1f, orbited.Yaw, precision: 5);
        Assert.NotSame(camera, orbited);
    }

    [Fact]
    public void Zoom_anchored_on_the_pointer_leaves_that_screen_point_unchanged_to_within_a_pixel()
    {
        var camera = Camera.FramedOn(new Vector3(-2, -2, -2), new Vector3(2, 2, 2));
        int width = 400, height = 300;
        var pointer = new Vector2(260f, 130f); // off-center on purpose — the case that matters

        var vpBefore = camera.ViewProjection(width, height);
        var anchorWorld = Unproject(pointer, vpBefore, camera);

        var zoomed = camera.ZoomAnchored(factor: 0.7f, pointer, width, height);
        var vpAfter = zoomed.ViewProjection(width, height);
        var screenAfter = Project(anchorWorld, vpAfter, width, height);

        Assert.InRange(MathF.Abs(screenAfter.X - pointer.X), 0f, 1f);
        Assert.InRange(MathF.Abs(screenAfter.Y - pointer.Y), 0f, 1f);
        // And the distance actually changed — a no-op that "passes" by not zooming is not this test.
        Assert.True(zoomed.Distance < camera.Distance);
    }

    [Fact]
    public void Pan_moves_the_target_along_the_cameras_own_screen_aligned_axes()
    {
        var camera = Cam(4f);
        var panned = camera.Pan(new Vector2(50f, 0f), viewportHeight: 200);

        Assert.NotEqual(Vector3.Zero, panned.Target);
        // A pure horizontal screen delta at zero yaw/pitch moves the target along the camera's own
        // right axis only — no vertical (Y) component.
        Assert.Equal(0f, panned.Target.Y, precision: 5);
        Assert.Equal(camera.Distance, panned.Distance, precision: 5);
        Assert.Equal(camera.Yaw, panned.Yaw, precision: 5);
        Assert.Equal(camera.Pitch, panned.Pitch, precision: 5);
    }

    [Fact]
    public void Panning_by_zero_leaves_the_target_unchanged()
    {
        var camera = new Camera(new Vector3(1, 2, 3), 4f, 0.3f, 0.2f, Radius);
        var panned = camera.Pan(Vector2.Zero, viewportHeight: 200);
        Assert.Equal(camera.Target, panned.Target);
    }

    [Fact]
    public void Reset_reframes_on_the_given_bounds_regardless_of_current_orientation()
    {
        var camera = new Camera(new Vector3(50, 50, 50), 500f, yaw: 9f, pitch: 1.4f, radius: 50f);
        var reset = camera.Reset(new Vector3(-1, -1, -1), new Vector3(1, 1, 1));

        Assert.Equal(Vector3.Zero, reset.Target);
        Assert.True(reset.Distance < 10f); // framed tight on a 2-unit box, not left at 500
    }

    private static Vector2 Project(Vector3 world, Matrix4x4 vp, int width, int height)
    {
        var clip = Vector4.Transform(new Vector4(world, 1f), vp);
        var ndc = new Vector2(clip.X / clip.W, clip.Y / clip.W);
        return new Vector2((ndc.X * 0.5f + 0.5f) * width, (1f - (ndc.Y * 0.5f + 0.5f)) * height);
    }

    private static Vector3 Unproject(Vector2 screen, Matrix4x4 vp, Camera camera)
    {
        Matrix4x4.Invert(vp, out var inv);
        // The test's own copy of the pixel -> NDC arithmetic, so a bug in Viewport cannot hide itself.
        float ndcX = screen.X / 400f * 2f - 1f;
        float ndcY = 1f - screen.Y / 300f * 2f;
        Vector3 At(float z)
        {
            var w = Vector4.Transform(new Vector4(ndcX, ndcY, z, 1f), inv);
            return new Vector3(w.X, w.Y, w.Z) / w.W;
        }
        var near = At(0f);
        var far = At(1f);
        var dir = Vector3.Normalize(far - near);
        return camera.Eye + dir * camera.Distance;
    }
}
