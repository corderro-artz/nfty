using System.Numerics;
using Nfty.Model.Render;

namespace Nfty.Model.Tests;

public class CameraTests
{
    private const float Radius = 2f;

    // A cube centered on the origin whose half-diagonal (the camera's Radius) is `radius`.
    private static (Vector3 Min, Vector3 Max) CubeOf(float radius) =>
        (Vector3.One * (-radius / MathF.Sqrt(3f)), Vector3.One * (radius / MathF.Sqrt(3f)));

    private static Camera Cam(float distance, float yaw = 0f, float pitch = 0f, float radius = Radius)
    {
        var (min, max) = CubeOf(radius);
        return new(Vector3.Zero, distance, yaw, pitch, min, max);
    }

    // A 512 x 512 x 3 slab, BottomCenter placement: the box Riser makes of 512px art.
    private static readonly Vector3 SlabMin = new(-256f, 0f, -1.5f);
    private static readonly Vector3 SlabMax = new(256f, 512f, 1.5f);

    private static bool Inside(Vector3 p, Vector3 min, Vector3 max) =>
        p.X >= min.X && p.X <= max.X && p.Y >= min.Y && p.Y <= max.Y && p.Z >= min.Z && p.Z <= max.Z;

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
        // Head-on at yaw 0 / pitch 0 the ray exits the +Z face: half-extent h = R / sqrt(3), plus the
        // 0.02 R margin.
        Assert.Equal((Radius / MathF.Sqrt(3f)) + (0.02f * Radius), camera.MinDistance, precision: 5);
        Assert.Equal(camera.MinDistance, clamped.Distance, precision: 5);
    }

    [Fact]
    public void Zooming_far_out_stops_at_twenty_radii_and_back_in_stops_at_the_box_floor()
    {
        var camera = Cam(4f, radius: 3f);
        Assert.Equal(60f, camera.MaxDistance, precision: 4);

        var farthest = camera.Zoom(1e6f);
        Assert.Equal(60f, farthest.Distance, precision: 4);
        Assert.Equal(60f, farthest.Zoom(1.1f).Distance, precision: 4);

        var nearest = farthest.Zoom(1e-6f);
        Assert.Equal((3f / MathF.Sqrt(3f)) + (0.02f * 3f), nearest.Distance, precision: 5);
    }

    [Fact]
    public void Every_apparent_mutator_carries_the_box_and_radius_through()
    {
        var camera = Cam(12f, radius: 7f);
        var pointer = new Vector2(10f, 10f);

        foreach (var moved in new[]
        {
            camera.Orbit(0.1f, 0.1f), camera.Zoom(0.9f), camera.Pan(new Vector2(5, 5), 100),
            camera.ZoomAnchored(0.9f, pointer, 100, 100),
        })
        {
            Assert.Equal(7f, moved.Radius);
            Assert.Equal(camera.BoundsMin, moved.BoundsMin);
            Assert.Equal(camera.BoundsMax, moved.BoundsMax);
        }
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
        var camera = new Camera(new Vector3(1, 2, 3), 12f, 0.3f, 0.2f, new Vector3(-5), new Vector3(5));
        var panned = camera.Pan(Vector2.Zero, viewportHeight: 200);
        Assert.Equal(camera.Target, panned.Target);
    }

    [Fact]
    public void Reset_reframes_on_the_given_bounds_regardless_of_current_orientation()
    {
        var camera = new Camera(new Vector3(50, 50, 50), 500f, yaw: 9f, pitch: 1.4f, new Vector3(0), new Vector3(100));
        var reset = camera.Reset(new Vector3(-1, -1, -1), new Vector3(1, 1, 1));

        Assert.Equal(Vector3.Zero, reset.Target);
        Assert.True(reset.Distance < 10f); // framed tight on a 2-unit box, not left at 500
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(30f, 20f)]
    [InlineData(90f, 0f)]
    [InlineData(-135f, -60f)]
    [InlineData(10f, 89f)]
    [InlineData(200f, -89f)]
    public void At_the_floor_the_eye_is_outside_the_box_from_every_angle(float yawDeg, float pitchDeg)
    {
        foreach (var (min, max) in new[] { (SlabMin, SlabMax), (new Vector3(-8, 0, -1.5f), new Vector3(8, 16, 1.5f)), CubeOf(2f) })
        {
            var framed = Camera.FramedOn(min, max);
            var camera = new Camera(framed.Target, framed.Distance, yawDeg * MathF.PI / 180f,
                pitchDeg * MathF.PI / 180f, min, max).Zoom(1e-6f);

            Assert.Equal(camera.MinDistance, camera.Distance, precision: 4);
            Assert.False(Inside(camera.Eye, min, max), $"eye {camera.Eye} is inside {min}..{max}");
        }
    }

    [Fact]
    public void Orbiting_ninety_degrees_at_the_floor_pushes_the_eye_out_instead_of_into_the_box()
    {
        var framed = Camera.FramedOn(SlabMin, SlabMax);
        var head = new Camera(framed.Target, framed.Distance, 0f, 0f, SlabMin, SlabMax).Zoom(1e-6f);
        float floorHeadOn = head.Distance; // z half-thickness 1.5 plus margin: a few units

        foreach (var (dyaw, dpitch) in new[] { (MathF.PI / 2, 0f), (-MathF.PI / 2, 0f), (0f, MathF.PI / 2), (0f, -MathF.PI / 2) })
        {
            var turned = head.Orbit(dyaw, dpitch);
            Assert.False(Inside(turned.Eye, SlabMin, SlabMax), $"eye {turned.Eye} entered the slab");
            Assert.True(turned.Distance >= turned.MinDistance - 1e-3f);
            Assert.True(turned.Distance > floorHeadOn, "the eye should have been pushed out as the exit distance grew");
        }
    }

    [Fact]
    public void Panning_far_past_an_edge_keeps_the_target_in_the_box_and_slides_along_it()
    {
        var framed = Camera.FramedOn(SlabMin, SlabMax);
        var camera = new Camera(framed.Target, framed.Distance, 0f, 0f, SlabMin, SlabMax);

        var panned = camera.Pan(new Vector2(-1e6f, 1e6f), 200);
        Assert.True(Inside(panned.Target, SlabMin, SlabMax));
        Assert.Equal(SlabMax.X, panned.Target.X, precision: 3); // dragging left moves the target right
        Assert.Equal(SlabMax.Y, panned.Target.Y, precision: 3); // dragging down moves the target up

        // Idempotent at the wall: more of the same drag changes nothing, so there is no jitter.
        var again = panned.Pan(new Vector2(-50f, 50f), 200);
        Assert.Equal(panned.Target, again.Target);
        Assert.Equal(panned.Distance, again.Distance, precision: 5);
    }

    [Fact]
    public void Panning_along_the_floor_never_makes_the_eye_distance_jitter()
    {
        var framed = Camera.FramedOn(SlabMin, SlabMax);
        var camera = new Camera(framed.Target, framed.Distance, 0f, 0f, SlabMin, SlabMax).Zoom(1e-6f);
        float floor = camera.Distance;
        for (int i = 0; i < 100; i++)
        {
            camera = camera.Pan(new Vector2(7f, -3f), 400);
            Assert.Equal(floor, camera.Distance, precision: 3); // head-on exit distance does not depend on where on the face you are
            Assert.True(Inside(camera.Target, SlabMin, SlabMax));
        }
    }

    [Fact]
    public void Zoom_anchored_holds_the_pointer_when_the_target_is_away_from_the_box_edge()
    {
        // Oblique view of a big slab at working distance, target mid-box (far from every clamp wall):
        // the clamp must not disturb step 6's within-a-pixel anchoring guarantee.
        var framed = Camera.FramedOn(SlabMin, SlabMax);
        var camera = new Camera(framed.Target, 60f, 0.6f, 0.3f, SlabMin, SlabMax);
        int width = 400, height = 300;
        var pointer = new Vector2(260f, 130f);

        var anchorWorld = Unproject(pointer, camera.ViewProjection(width, height), camera);
        var zoomed = camera.ZoomAnchored(0.7f, pointer, width, height);
        var screenAfter = Project(anchorWorld, zoomed.ViewProjection(width, height), width, height);

        Assert.True(zoomed.Distance < camera.Distance);
        Assert.InRange(MathF.Abs(screenAfter.X - pointer.X), 0f, 1f);
        Assert.InRange(MathF.Abs(screenAfter.Y - pointer.Y), 0f, 1f);
        Assert.True(Inside(zoomed.Target, SlabMin, SlabMax));
    }

    [Fact]
    public void With_subject_keeps_the_view_and_follows_the_new_box()
    {
        var camera = new Camera(new Vector3(1, 1, 0), 12f, 0.4f, 0.3f, new Vector3(-2), new Vector3(2));
        var moved = camera.WithSubject(new Vector3(-50, -50, -2), new Vector3(50, 50, 2));

        Assert.Equal(camera.Target, moved.Target);
        Assert.Equal(camera.Yaw, moved.Yaw);
        Assert.Equal(camera.Pitch, moved.Pitch);
        Assert.Equal(Vector3.Distance(new(-50, -50, -2), new(50, 50, 2)) / 2f, moved.Radius, precision: 3);
        Assert.True(moved.Distance >= moved.MinDistance);

        // A target the new box does not contain is pulled in.
        var shifted = camera.WithSubject(new Vector3(10, 10, 10), new Vector3(20, 20, 20));
        Assert.Equal(new Vector3(10, 10, 10), shifted.Target);
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
