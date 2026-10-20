using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using Nfty.Model.Render;

namespace Nfty.App.ViewModels;

/// <summary>Camera state and gesture-to-camera-move resolution for the 3D preview control. Knows
/// nothing about pointer capture, key bindings or painting — that is <c>ModelPreviewView</c>'s job;
/// this class is the plain, fully unit-testable state the view is a thin wrapper over (the same split
/// CLAUDE.md already states for the rest of the app).</summary>
public sealed partial class ModelPreviewViewModel : ObservableObject
{
    private const float OrbitRadiansPerPixel = MathF.PI / 400f;
    private const float KeyOrbitStepRadians = 15f * MathF.PI / 180f;
    private const float ZoomStepFactor = 0.9f;

    private Vector3 _boundsMin;
    private Vector3 _boundsMax;

    [ObservableProperty]
    private Camera _cameraState;

    /// <summary>Starts framed on the given bounds — see <see cref="Camera.FramedOn"/>.</summary>
    public ModelPreviewViewModel(Vector3 boundsMin, Vector3 boundsMax)
    {
        _boundsMin = boundsMin;
        _boundsMax = boundsMax;
        _cameraState = Camera.FramedOn(boundsMin, boundsMax);
    }

    /// <summary>A left-drag delta in screen pixels, turned into an orbit. Dragging right should make
    /// content appear to follow the pointer, which means the camera itself orbits the opposite way.</summary>
    public void Orbit(Vector2 pointerDeltaPixels) =>
        CameraState = CameraState.Orbit(
            -pointerDeltaPixels.X * OrbitRadiansPerPixel,
            -pointerDeltaPixels.Y * OrbitRadiansPerPixel);

    /// <summary>A right- or middle-drag delta in screen pixels.</summary>
    public void Pan(Vector2 pointerDeltaPixels, int viewportHeight) =>
        CameraState = CameraState.Pan(pointerDeltaPixels, viewportHeight);

    /// <summary>One wheel event, anchored on the pointer. The step honors the delta's magnitude (a
    /// trackpad sending many small deltas zooms smoothly, a notched wheel sends a whole 1.0): wheel up
    /// (positive) zooms in by <c>0.9^delta</c>.</summary>
    public void Zoom(float wheelDelta, Vector2 pointerScreen, int width, int height)
    {
        if (wheelDelta == 0f) return;
        float factor = MathF.Pow(ZoomStepFactor, wheelDelta);
        CameraState = CameraState.ZoomAnchored(factor, pointerScreen, width, height);
    }

    /// <summary>The keyboard orbit path: one fixed step along the given axis
    /// (<c>(±1,0)</c> for Left/Right, <c>(0,±1)</c> for Up/Down).</summary>
    public void OrbitStep(Vector2 direction) =>
        CameraState = CameraState.Orbit(
            direction.X * KeyOrbitStepRadians,
            direction.Y * KeyOrbitStepRadians);

    /// <summary>The keyboard zoom path: <paramref name="sign"/> positive zooms in.</summary>
    public void ZoomStep(float sign) =>
        CameraState = CameraState.Zoom(sign > 0 ? ZoomStepFactor : 1f / ZoomStepFactor);

    /// <summary>Replaces the bounds Home/double-click frame, WITHOUT moving the camera: orientation,
    /// target and distance are kept. The view calls this whenever its mesh changes, so there is one
    /// source of truth for the subject's extent (the mesh) and a depth edit cannot leave Reset framing
    /// stale bounds.</summary>
    /// <param name="boundsMin">Minimum corner of the subject's bounding box.</param>
    /// <param name="boundsMax">Maximum corner of the subject's bounding box.</param>
    public void SetSubject(Vector3 boundsMin, Vector3 boundsMax)
    {
        _boundsMin = boundsMin;
        _boundsMax = boundsMax;
    }

    /// <summary>Home or double-click: back to the framed camera on the current subject bounds,
    /// discarding orientation.</summary>
    /// <param name="aspect">The viewport's width divided by its height, so a non-square viewport is
    /// framed to fit its narrower axis (see <see cref="Camera.FramedOn"/>).</param>
    public void Reset(float aspect = 1f) => CameraState = Camera.FramedOn(_boundsMin, _boundsMax, aspect);
}
