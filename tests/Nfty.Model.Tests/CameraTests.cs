using System.Numerics;
using Nfty.Model.Render;

namespace Nfty.Model.Tests;

public class CameraTests
{
    [Fact]
    public void A_camera_looking_along_minus_z_projects_a_unit_cubes_corners_to_known_screen_points()
    {
        // Camera sits on +Z looking at the origin, no yaw/pitch (Target=origin, Yaw=0, Pitch=0 means
        // Eye = Target + Distance * (0, 0, 1) given the spherical formula below — i.e. straight down
        // +Z toward -Z, matching glTF's +Z-toward-viewer convention the rest of Nfty.Model already
        // uses).
        var camera = new Camera(Vector3.Zero, distance: 4f, yaw: 0f, pitch: 0f);
        var vp = camera.ViewProjection(width: 200, height: 200);

        // The cube's own center (origin) must land exactly at the screen center.
        var center = Project(Vector3.Zero, vp, 200, 200);
        Assert.Equal(100f, center.X, precision: 3);
        Assert.Equal(100f, center.Y, precision: 3);

        // A point straight up from the origin lands above screen center (smaller Y — screen Y grows
        // down); a point to the +X side lands right of center (larger X).
        var up = Project(new Vector3(0, 1, 0), vp, 200, 200);
        Assert.True(up.Y < center.Y);
        Assert.Equal(100f, up.X, precision: 2);

        var right = Project(new Vector3(1, 0, 0), vp, 200, 200);
        Assert.True(right.X > center.X);
        Assert.Equal(100f, right.Y, precision: 2);
    }

    [Fact]
    public void Orbiting_past_the_pitch_limit_clamps_instead_of_flipping()
    {
        var camera = new Camera(Vector3.Zero, 4f, yaw: 0f, pitch: 0f);
        float limit = Camera.PitchLimitDegrees * MathF.PI / 180f;

        var pushedPastTop = camera.Orbit(0f, deltaPitchRadians: limit + 1f);
        Assert.Equal(limit, pushedPastTop.Pitch, precision: 5);

        var pushedPastBottom = camera.Orbit(0f, deltaPitchRadians: -(limit + 1f));
        Assert.Equal(-limit, pushedPastBottom.Pitch, precision: 5);
    }

    [Fact]
    public void Orbit_never_clamps_yaw()
    {
        var camera = new Camera(Vector3.Zero, 4f, 0f, 0f);
        var spun = camera.Orbit(deltaYawRadians: 100f, 0f); // many full turns
        Assert.Equal(100f, spun.Yaw, precision: 5);
    }

    [Fact]
    public void Zoom_scales_distance_and_clamps_at_the_minimum()
    {
        var camera = new Camera(Vector3.Zero, 4f, 0f, 0f);
        var closer = camera.Zoom(0.5f);
        Assert.Equal(2f, closer.Distance, precision: 4);

        var clamped = camera.Zoom(0.00001f);
        Assert.Equal(Camera.MinDistance, clamped.Distance, precision: 4);
    }

    [Fact]
    public void Camera_is_immutable_every_mutator_returns_a_new_instance()
    {
        var camera = new Camera(Vector3.Zero, 4f, 0f, 0f);
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
        var anchorWorld = Unproject(pointer, vpBefore, width, height, camera);

        var zoomed = camera.ZoomAnchored(factor: 0.7f, pointer, width, height);
        var vpAfter = zoomed.ViewProjection(width, height);
        var screenAfter = Project(anchorWorld, vpAfter, width, height);

        Assert.InRange(MathF.Abs(screenAfter.X - pointer.X), 0f, 1f);
        Assert.InRange(MathF.Abs(screenAfter.Y - pointer.Y), 0f, 1f);
        // And the distance actually changed — a no-op that "passes" by not zooming is not this test.
        Assert.True(zoomed.Distance < camera.Distance);
    }

    [Fact]
    public void Reset_reframes_on_the_given_bounds_regardless_of_current_orientation()
    {
        var camera = new Camera(new Vector3(50, 50, 50), 500f, yaw: 9f, pitch: 1.4f);
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

    private static Vector3 Unproject(Vector2 screen, Matrix4x4 vp, int width, int height, Camera camera)
    {
        Matrix4x4.Invert(vp, out var inv);
        float ndcX = screen.X / width * 2f - 1f;
        float ndcY = 1f - screen.Y / height * 2f;
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
