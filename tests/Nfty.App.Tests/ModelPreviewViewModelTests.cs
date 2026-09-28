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
