using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using Nfty.Model.Render;

namespace Nfty.App.ViewModels;

/// <summary>A named camera angle on the subject.</summary>
public enum ViewAngle
{
    /// <summary>From the viewer's side, looking down negative Z.</summary>
    Front,
    /// <summary>From behind, looking down positive Z.</summary>
    Back,
    /// <summary>From the right, looking down negative X.</summary>
    Side,
    /// <summary>From above, looking down.</summary>
    Top,
}

/// <summary>How fast the turntable spins the camera around the subject.</summary>
public enum TurntableSpeed
{
    /// <summary>Not turning.</summary>
    Off,
    /// <summary>20 degrees a second.</summary>
    Slow,
    /// <summary>45 degrees a second.</summary>
    Medium,
    /// <summary>90 degrees a second.</summary>
    Fast,
}

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

    /// <summary>The turntable's speed. View state only: it moves the camera, never the model.</summary>
    [ObservableProperty]
    private TurntableSpeed _turntable;

    /// <summary>Whether the quad outlines are drawn over the shaded solid. View state only: it changes
    /// the picture on screen and in a saved still, never the model that is exported.</summary>
    [ObservableProperty]
    private bool _wireframe;

    /// <summary>The viewport's width over its height, kept by the view so a preset frames for the shape
    /// the pane really has.</summary>
    public float ViewportAspect { get; set; } = 1f;

    /// <summary>Whether the turntable is off.</summary>
    public bool TurnIsOff => Turntable == TurntableSpeed.Off;
    /// <summary>Whether the turntable is slow.</summary>
    public bool TurnIsSlow => Turntable == TurntableSpeed.Slow;
    /// <summary>Whether the turntable is medium.</summary>
    public bool TurnIsMedium => Turntable == TurntableSpeed.Medium;
    /// <summary>Whether the turntable is fast.</summary>
    public bool TurnIsFast => Turntable == TurntableSpeed.Fast;

    partial void OnTurntableChanged(TurntableSpeed value)
    {
        OnPropertyChanged(nameof(TurnIsOff));
        OnPropertyChanged(nameof(TurnIsSlow));
        OnPropertyChanged(nameof(TurnIsMedium));
        OnPropertyChanged(nameof(TurnIsFast));
    }

    /// <summary>Degrees a second for a turntable speed.</summary>
    /// <param name="speed">The speed.</param>
    /// <returns>Its angular rate.</returns>
    public static float DegreesPerSecond(TurntableSpeed speed) => speed switch
    {
        TurntableSpeed.Slow => 20f,
        TurntableSpeed.Medium => 45f,
        TurntableSpeed.Fast => 90f,
        _ => 0f,
    };

    /// <summary>Advances the turntable by the time since the last tick. A timer calls this; a test calls
    /// it with the span it wants, so nothing here waits on a clock.</summary>
    /// <param name="elapsed">Time since the previous tick.</param>
    public void Tick(TimeSpan elapsed)
    {
        float degrees = DegreesPerSecond(Turntable);
        if (degrees == 0f || elapsed <= TimeSpan.Zero) return;
        CameraState = CameraState.Orbit(degrees * (float)elapsed.TotalSeconds * MathF.PI / 180f, 0f);
    }

    /// <summary>Jumps to a named angle, framed on the current bounds like <see cref="Reset"/>.</summary>
    /// <param name="angle">Which side to look from.</param>
    /// <param name="aspect">The viewport's width divided by its height.</param>
    public void Preset(ViewAngle angle, float aspect = 1f)
    {
        var framed = Camera.FramedOn(_boundsMin, _boundsMax, aspect);
        var (yaw, pitch) = angle switch
        {
            ViewAngle.Front => (0f, 0f),
            ViewAngle.Back => (MathF.PI, 0f),
            ViewAngle.Side => (MathF.PI / 2f, 0f),
            _ => (0f, Camera.PitchLimitDegrees * MathF.PI / 180f),
        };
        CameraState = new Camera(framed.Target, framed.Distance, yaw, pitch, framed.BoundsMin, framed.BoundsMax);
    }

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

    /// <summary>Replaces the bounds Home/double-click frame and the camera's own box, WITHOUT moving the
    /// camera: orientation, target and distance are kept (only clamped if the new box demands it). The view calls this whenever its mesh changes, so there is one
    /// source of truth for the subject's extent (the mesh) and a depth edit cannot leave Reset framing
    /// stale bounds.</summary>
    /// <param name="boundsMin">Minimum corner of the subject's bounding box.</param>
    /// <param name="boundsMax">Maximum corner of the subject's bounding box.</param>
    public void SetSubject(Vector3 boundsMin, Vector3 boundsMax)
    {
        _boundsMin = boundsMin;
        _boundsMax = boundsMax;
        // The camera's distance limits, clip planes and target confinement all follow the subject's
        // box, so a depth edit that changes the extent must reach it. Orientation, target and distance
        // are kept; an unchanged box leaves the very same instance (no spurious repaint).
        if (CameraState.BoundsMin != Vector3.Min(boundsMin, boundsMax)
            || CameraState.BoundsMax != Vector3.Max(boundsMin, boundsMax))
            CameraState = CameraState.WithSubject(boundsMin, boundsMax);
    }

    /// <summary>Home or double-click: back to the framed camera on the current subject bounds,
    /// discarding orientation.</summary>
    /// <param name="aspect">The viewport's width divided by its height, so a non-square viewport is
    /// framed to fit its narrower axis (see <see cref="Camera.FramedOn"/>).</param>
    public void Reset(float aspect = 1f) => CameraState = Camera.FramedOn(_boundsMin, _boundsMax, aspect);
}
