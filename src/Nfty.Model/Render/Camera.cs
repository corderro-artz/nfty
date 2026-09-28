using System.Numerics;

namespace Nfty.Model.Render;

/// <summary>
/// An immutable turntable-orbit camera: <see cref="ViewProjection"/> is the one function that owns
/// the view and projection mapping together, and every other mapping (picking, zoom-anchoring)
/// derives from it or its inverse rather than restating the matrices beside it.
/// </summary>
/// <remarks>
/// Immutable by the same rule <see cref="Mesh"/> states for itself: every value is fixed at
/// construction, so a <see cref="Camera"/> may be read from many threads and a caller can never hold
/// a half-updated one. Every apparent mutator (<see cref="Orbit"/>, <see cref="Pan"/>,
/// <see cref="Zoom"/>, <see cref="ZoomAnchored"/>, <see cref="Reset"/>) returns a new instance.
/// </remarks>
public sealed class Camera
{
    /// <summary>Pitch never exceeds this many degrees from level, either direction — an extruded
    /// sprite has a definite up, and a camera rolled onto its side shows an orientation that means
    /// nothing.</summary>
    public const float PitchLimitDegrees = 89f;

    /// <summary>The vertical field of view every projection this camera builds uses.</summary>
    public const float FovDegrees = 40f;

    /// <summary>Closest the eye may orbit to its target, in world units.</summary>
    public const float MinDistance = 0.5f;

    /// <summary>Farthest the eye may orbit from its target, in world units.</summary>
    public const float MaxDistance = 500f;

    private const float NearPlane = 0.01f;
    private const float FarPlane = 1000f;
    private static readonly float PitchLimitRadians = PitchLimitDegrees * MathF.PI / 180f;
    private static readonly float FovRadians = FovDegrees * MathF.PI / 180f;

    /// <summary>The point the camera orbits and looks at.</summary>
    public Vector3 Target { get; }

    /// <summary>World-unit distance from <see cref="Target"/> to <see cref="Eye"/>, clamped to
    /// <see cref="MinDistance"/>..<see cref="MaxDistance"/>.</summary>
    public float Distance { get; }

    /// <summary>Orbit angle around the vertical axis, radians. Unclamped — free spin.</summary>
    public float Yaw { get; }

    /// <summary>Orbit angle above/below level, radians, clamped to ±<see cref="PitchLimitDegrees"/>.</summary>
    public float Pitch { get; }

    /// <summary>Builds a camera, clamping <paramref name="distance"/> and <paramref name="pitch"/>
    /// so every instance that exists is already valid.</summary>
    public Camera(Vector3 target, float distance, float yaw, float pitch)
    {
        Target = target;
        Distance = Math.Clamp(distance, MinDistance, MaxDistance);
        Yaw = yaw;
        Pitch = Math.Clamp(pitch, -PitchLimitRadians, PitchLimitRadians);
    }

    /// <summary>World position of the eye, computed fresh on every access — cheap trigonometry, not
    /// the lazy-cache pattern that would need a mutable field.</summary>
    public Vector3 Eye
    {
        get
        {
            float cosPitch = MathF.Cos(Pitch);
            return Target + Distance * new Vector3(
                cosPitch * MathF.Sin(Yaw),
                MathF.Sin(Pitch),
                cosPitch * MathF.Cos(Yaw));
        }
    }

    /// <summary>A new camera orbited by the given deltas; yaw is free, pitch clamps.</summary>
    public Camera Orbit(float deltaYawRadians, float deltaPitchRadians) =>
        new(Target, Distance, Yaw + deltaYawRadians, Pitch + deltaPitchRadians);

    /// <summary>A new camera at <see cref="Distance"/> scaled by <paramref name="factor"/>, clamped.
    /// <paramref name="factor"/> below 1 moves the eye closer.</summary>
    public Camera Zoom(float factor) => new(Target, Distance * factor, Yaw, Pitch);

    /// <summary>A new camera panned so a <paramref name="screenDeltaPixels"/> drag moves content
    /// under the pointer by the same number of pixels, at the current <see cref="Distance"/> and
    /// <see cref="FovDegrees"/>.</summary>
    public Camera Pan(Vector2 screenDeltaPixels, int viewportHeight)
    {
        float worldPerPixel = 2f * Distance * MathF.Tan(FovRadians / 2f) / MathF.Max(viewportHeight, 1);
        var (right, up) = Basis();
        var delta = -right * screenDeltaPixels.X * worldPerPixel + up * screenDeltaPixels.Y * worldPerPixel;
        return new Camera(Target + delta, Distance, Yaw, Pitch);
    }

    /// <summary>A new camera zoomed by <paramref name="factor"/> such that the world point under
    /// <paramref name="pointerScreen"/> stays under it, to within a pixel. Two passes: zoom around
    /// <see cref="Target"/> (trivially exact for whatever <see cref="Target"/> itself projects to,
    /// since <see cref="ViewProjection"/> always centers <see cref="Target"/> on screen), then a
    /// corrective pan that cancels the screen-space error the zoom introduced for the off-center
    /// anchor point. That correction does NOT go through the public <see cref="Pan"/> — <see cref="Pan"/>
    /// converts pixels to world units using <see cref="Distance"/> (the eye-to-Target depth), which is
    /// exact only for a point sitting exactly at <see cref="Target"/>'s depth; the anchor is generally
    /// off that plane. Because a pan perpendicular to the forward axis never changes a fixed point's
    /// depth along that axis (the shift has zero component along forward by construction), the
    /// anchor's own forward-axis depth is constant through the correction, so converting the
    /// screen-space error to world units at THAT depth — instead of at <see cref="Distance"/> — makes
    /// the correction exact in one pass rather than an approximation that needs iterating.</summary>
    public Camera ZoomAnchored(float factor, Vector2 pointerScreen, int width, int height)
    {
        var anchor = Unproject(pointerScreen, width, height);
        var zoomed = Zoom(factor);
        return zoomed.CorrectTowardAnchor(anchor, pointerScreen, width, height);
    }

    private Camera CorrectTowardAnchor(Vector3 anchor, Vector2 pointerScreen, int width, int height)
    {
        var vp = ViewProjection(width, height);
        var clip = Vector4.Transform(new Vector4(anchor, 1f), vp);
        if (MathF.Abs(clip.W) < 1e-6f)
            return this; // anchor landed behind the eye; nothing sane to correct toward
        var ndc = new Vector2(clip.X / clip.W, clip.Y / clip.W);
        var screenNow = new Vector2(
            (ndc.X * 0.5f + 0.5f) * width,
            (1f - (ndc.Y * 0.5f + 0.5f)) * height);

        var error = pointerScreen - screenNow;

        var forward = Vector3.Normalize(Target - Eye);
        float anchorDepth = Vector3.Dot(anchor - Eye, forward);
        if (anchorDepth <= 0f)
            return this; // anchor is behind the eye along the view axis; nothing sane to correct toward

        float worldPerPixel = 2f * anchorDepth * MathF.Tan(FovRadians / 2f) / MathF.Max(height, 1);
        var (right, up) = Basis();
        // Same sign convention as Pan: a positive screen-space error along X moves the camera along
        // -right (so the anchor's projection shifts by +error.X), and along +up for Y.
        var delta = -right * error.X * worldPerPixel + up * error.Y * worldPerPixel;
        return new Camera(Target + delta, Distance, Yaw, Pitch);
    }

    /// <summary>The one function that owns view and projection together.</summary>
    public Matrix4x4 ViewProjection(int width, int height)
    {
        var view = Matrix4x4.CreateLookAt(Eye, Target, Vector3.UnitY);
        float aspect = width / (float)MathF.Max(height, 1);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(FovRadians, aspect, NearPlane, FarPlane);
        return view * projection;
    }

    /// <summary>A camera framing the given bounds: centered on the box, pulled back to fit its
    /// bounding sphere in the vertical field of view with 15% margin, at a three-quarter starting
    /// angle rather than a flat silhouette.</summary>
    public static Camera FramedOn(Vector3 min, Vector3 max)
    {
        var center = (min + max) / 2f;
        float radius = Vector3.Distance(min, max) / 2f;
        float distance = radius <= 0f
            ? MinDistance
            : radius / MathF.Sin(FovRadians / 2f) * 1.15f;
        return new Camera(center, distance, yaw: 30f * MathF.PI / 180f, pitch: 20f * MathF.PI / 180f);
    }

    /// <summary>Reframes on new bounds, discarding the current orientation — what "reset" means when
    /// the subject itself has bounds, as opposed to re-centering the existing view.</summary>
    public Camera Reset(Vector3 min, Vector3 max) => FramedOn(min, max);

    private (Vector3 Right, Vector3 Up) Basis()
    {
        var forward = Vector3.Normalize(Target - Eye);
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        var up = Vector3.Cross(right, forward);
        return (right, up);
    }

    private Vector3 Unproject(Vector2 pointerScreen, int width, int height)
    {
        var vp = ViewProjection(width, height);
        Matrix4x4.Invert(vp, out var inv);
        float ndcX = pointerScreen.X / width * 2f - 1f;
        float ndcY = 1f - pointerScreen.Y / height * 2f;
        Vector3 At(float z)
        {
            var world = Vector4.Transform(new Vector4(ndcX, ndcY, z, 1f), inv);
            return new Vector3(world.X, world.Y, world.Z) / world.W;
        }
        var near = At(0f);
        var far = At(1f);
        var dir = Vector3.Normalize(far - near);
        return Eye + dir * Distance; // depth heuristic: the pick ray at the current orbit distance
    }
}
