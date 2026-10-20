using System.Numerics;
using Nfty.App.ViewModels;
using Nfty.Model.Render;
using Xunit;

namespace Nfty.App.Tests;

public class ModelPreviewViewModelTests
{
    private static ModelPreviewViewModel New() =>
        new(new Vector3(-1, -1, -1), new Vector3(1, 1, 1));

    [Fact]
    public void Starts_framed_on_the_given_bounds()
    {
        var vm = New();
        Assert.Equal(Vector3.Zero, vm.CameraState.Target);
    }

    [Fact]
    public void Orbit_moves_yaw_opposite_the_drag_direction_so_content_follows_the_pointer()
    {
        var vm = New();
        float startYaw = vm.CameraState.Yaw;
        vm.Orbit(new Vector2(100f, 0f));
        Assert.True(vm.CameraState.Yaw < startYaw); // dragging right orbits the CAMERA left
    }

    [Fact]
    public void Zoom_step_in_moves_the_eye_closer()
    {
        var vm = New();
        float startDistance = vm.CameraState.Distance;
        vm.ZoomStep(sign: 1f);
        Assert.True(vm.CameraState.Distance < startDistance);
    }

    [Fact]
    public void Zoom_step_out_moves_the_eye_farther()
    {
        var vm = New();
        float startDistance = vm.CameraState.Distance;
        vm.ZoomStep(sign: -1f);
        Assert.True(vm.CameraState.Distance > startDistance);
    }

    [Fact]
    public void Reset_returns_to_the_original_framed_camera()
    {
        var vm = New();
        var original = vm.CameraState;
        vm.Orbit(new Vector2(500f, 300f));
        vm.ZoomStep(1f);
        vm.Reset();

        Assert.Equal(original.Target, vm.CameraState.Target);
        Assert.Equal(original.Distance, vm.CameraState.Distance, precision: 4);
        Assert.Equal(original.Yaw, vm.CameraState.Yaw, precision: 4);
        Assert.Equal(original.Pitch, vm.CameraState.Pitch, precision: 4);
    }

    [Fact]
    public void Orbit_step_up_changes_pitch_by_exactly_fifteen_degrees()
    {
        var vm = New();
        float startPitch = vm.CameraState.Pitch;
        vm.OrbitStep(new Vector2(0f, 1f));
        float expected = startPitch + 15f * MathF.PI / 180f;
        Assert.Equal(expected, vm.CameraState.Pitch, precision: 4);
    }

    /// <summary>Wheel magnitude counts: log of the distance ratio is proportional to the delta.</summary>
    [Fact]
    public void A_wheel_delta_of_half_zooms_half_as_far_in_log_terms_as_a_delta_of_one()
    {
        var half = New();
        var whole = New();
        float start = half.CameraState.Distance;

        half.Zoom(0.5f, new Vector2(100, 100), 200, 200);
        whole.Zoom(1f, new Vector2(100, 100), 200, 200);

        float logHalf = MathF.Log(half.CameraState.Distance / start);
        float logWhole = MathF.Log(whole.CameraState.Distance / start);
        Assert.True(logWhole < 0f); // wheel up still zooms IN
        Assert.Equal(logWhole / 2f, logHalf, 4);
    }

    [Fact]
    public void Wheel_down_zooms_out_by_the_same_log_amount()
    {
        var vm = New();
        float start = vm.CameraState.Distance;
        vm.Zoom(-1f, new Vector2(100, 100), 200, 200);
        Assert.Equal(-MathF.Log(0.9f), MathF.Log(vm.CameraState.Distance / start), 4);
    }

    private static void AssertAllCornersInView(Camera camera, Vector3 min, Vector3 max, int width, int height)
    {
        var vp = camera.ViewProjection(width, height);
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z);
            var clip = Vector4.Transform(new Vector4(corner, 1f), vp);
            Assert.True(clip.W > 0f);
            Assert.InRange(clip.X / clip.W, -1f, 1f);
            Assert.InRange(clip.Y / clip.W, -1f, 1f);
        }
    }

    [Fact]
    public void Set_subject_keeps_the_camera_exactly_where_it_was()
    {
        var vm = New();
        vm.Orbit(new Vector2(120f, 40f));
        vm.ZoomStep(1f);
        var before = vm.CameraState;

        vm.SetSubject(new Vector3(-50, -50, -2), new Vector3(50, 50, 2));

        Assert.Same(before, vm.CameraState);
    }

    [Fact]
    public void Home_after_set_subject_frames_the_new_bounds_not_the_construction_bounds()
    {
        var vm = New(); // ±1
        var min = new Vector3(-50, -50, -2);
        var max = new Vector3(50, 50, 2);
        vm.SetSubject(min, max);

        vm.Reset();

        AssertAllCornersInView(vm.CameraState, min, max, 200, 200);
    }

    [Fact]
    public void Reset_with_a_wide_aspect_still_frames_every_corner()
    {
        var vm = New();
        var min = new Vector3(-30, -3, -1);
        var max = new Vector3(30, 3, 1);
        vm.SetSubject(min, max);

        vm.Reset(aspect: 0.5f);

        AssertAllCornersInView(vm.CameraState, min, max, 100, 200);
    }

    [Fact]
    public void Changing_the_camera_raises_property_changed()
    {
        var vm = New();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.ZoomStep(1f);

        Assert.Contains(nameof(ModelPreviewViewModel.CameraState), raised);
    }
}
