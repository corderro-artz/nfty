using System.Numerics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.Model.Render;
using SixLabors.ImageSharp.PixelFormats;

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
    private Rgba32 _lastLightColor = new(255, 255, 255);

    /// <summary>Preview-only light intensity, clamped from zero through 100.</summary>
    [ObservableProperty]
    private int _lightValue = 100;

    /// <summary>The preview light's color as #RRGGBB. Partial or invalid input keeps the last valid light.</summary>
    [ObservableProperty]
    private string _lightColor = "#FFFFFF";

    /// <summary>Whether the color field contains a complete #RRGGBB value.</summary>
    public bool IsLightColorValid => TryLightColor(LightColor, out _);

    /// <summary>The accepted light color for the swatch; incomplete typing does not change its ink.</summary>
    public string LightColorSwatch => string.Create(CultureInfo.InvariantCulture,
        $"#{_lastLightColor.R:X2}{_lastLightColor.G:X2}{_lastLightColor.B:X2}");

    /// <summary>The current preview light. It never enters model export or authored data.</summary>
    public PreviewLighting Lighting => new(LightValue, _lastLightColor);

    partial void OnLightValueChanged(int value)
    {
        int clamped = Math.Clamp(value, 0, 100);
        if (value != clamped) { LightValue = clamped; return; }
        OnPropertyChanged(nameof(Lighting));
    }

    partial void OnLightColorChanged(string value)
    {
        OnPropertyChanged(nameof(IsLightColorValid));
        if (!TryLightColor(value, out var color)) return;
        _lastLightColor = color;
        OnPropertyChanged(nameof(LightColorSwatch));
        OnPropertyChanged(nameof(Lighting));
    }

    private static bool TryLightColor(string? value, out Rgba32 color)
    {
        color = default;
        if (value is not { Length: 7 } || value[0] != '#'
            || !uint.TryParse(value.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint rgb))
            return false;
        color = new Rgba32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    /// <summary>Restores full-strength white light without changing the camera, edges or turntable.</summary>
    [RelayCommand]
    private void ResetLighting()
    {
        LightValue = 100;
        LightColor = "#FFFFFF";
    }

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
    /// <param name="boundsMin">Minimum corner of the subject's bounding box.</param>
    /// <param name="boundsMax">Maximum corner of the subject's bounding box.</param>
    public ModelPreviewViewModel(Vector3 boundsMin, Vector3 boundsMax)
    {
        _boundsMin = boundsMin;
        _boundsMax = boundsMax;
        _cameraState = Camera.FramedOn(boundsMin, boundsMax);
    }

    /// <summary>A left-drag delta in logical control units, turned into an orbit. Dragging right should make
    /// content appear to follow the pointer, which means the camera itself orbits the opposite way.</summary>
    /// <param name="pointerDeltaPixels">Pointer movement in the viewport's logical control units.</param>
    public void Orbit(Vector2 pointerDeltaPixels) =>
        CameraState = CameraState.Orbit(
            -pointerDeltaPixels.X * OrbitRadiansPerPixel,
            -pointerDeltaPixels.Y * OrbitRadiansPerPixel);

    /// <summary>A right- or middle-drag delta in logical control units.</summary>
    /// <param name="pointerDeltaPixels">Pointer movement in the viewport's logical control units.</param>
    /// <param name="viewportHeight">Viewport height in the same units as the movement.</param>
    public void Pan(Vector2 pointerDeltaPixels, int viewportHeight) =>
        CameraState = CameraState.Pan(pointerDeltaPixels, viewportHeight);

    /// <summary>One wheel event, anchored on the pointer. The step honors the delta's magnitude (a
    /// trackpad sending many small deltas zooms smoothly, a notched wheel sends a whole 1.0): wheel up
    /// (positive) zooms in by <c>0.9^delta</c>.</summary>
    /// <param name="wheelDelta">Wheel movement; positive zooms in.</param>
    /// <param name="pointerScreen">Pointer position relative to the viewport.</param>
    /// <param name="width">Viewport width in the pointer position's units.</param>
    /// <param name="height">Viewport height in the pointer position's units.</param>
    public void Zoom(float wheelDelta, Vector2 pointerScreen, int width, int height)
    {
        if (wheelDelta == 0f) return;
        float factor = MathF.Pow(ZoomStepFactor, wheelDelta);
        CameraState = CameraState.ZoomAnchored(factor, pointerScreen, width, height);
    }

    /// <summary>The keyboard orbit path: one fixed step along the given axis
    /// (<c>(±1,0)</c> for Left/Right, <c>(0,±1)</c> for Up/Down).</summary>
    /// <param name="direction">Signed horizontal and vertical orbit direction.</param>
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
        // A camera nobody has touched still shows the default framing of the OLD box (the placeholder
        // a viewer opens with before its mesh arrives): follow the subject so it opens centered, not
        // left aiming at where the placeholder was.
        var def = Camera.FramedOn(_boundsMin, _boundsMax);
        bool untouched = CameraState.Target == def.Target && CameraState.Yaw == def.Yaw
            && CameraState.Pitch == def.Pitch && MathF.Abs(CameraState.Distance - def.Distance) < 1e-3f;
        _boundsMin = boundsMin;
        _boundsMax = boundsMax;
        if (untouched)
        {
            var fresh = Camera.FramedOn(boundsMin, boundsMax);
            if (CameraState.BoundsMin != fresh.BoundsMin || CameraState.BoundsMax != fresh.BoundsMax
                || CameraState.Target != fresh.Target) CameraState = fresh;
            return;
        }
        // The camera's distance limits, clip planes and target confinement all follow the subject's
        // box, so a depth edit that changes the extent must reach it. Orientation and distance are
        // kept; an unchanged box leaves the very same instance (no spurious repaint).
        var min = Vector3.Min(boundsMin, boundsMax);
        var max = Vector3.Max(boundsMin, boundsMax);
        if (CameraState.BoundsMin == min && CameraState.BoundsMax == max) return;
        // THE TARGET MOVES WITH THE BOX'S CENTER. Kept where it was, a reroll that dropped a layer
        // (or a deeper front) left the camera orbiting the OLD middle: the model sat off to one side
        // and swung around a point beside it - "slightly right, hard to rotate". Carrying the offset
        // keeps a deliberate pan exactly as panned, relative to the model.
        var shift = (min + max) / 2f - (CameraState.BoundsMin + CameraState.BoundsMax) / 2f;
        CameraState = new Camera(CameraState.Target + shift, CameraState.Distance, CameraState.Yaw,
            CameraState.Pitch, min, max);
    }

    /// <summary>Home or double-click: back to the framed camera on the current subject bounds,
    /// discarding orientation.</summary>
    /// <param name="aspect">The viewport's width divided by its height, so a non-square viewport is
    /// framed to fit its narrower axis (see <see cref="Camera.FramedOn"/>).</param>
    public void Reset(float aspect = 1f) => CameraState = Camera.FramedOn(_boundsMin, _boundsMax, aspect);
}
