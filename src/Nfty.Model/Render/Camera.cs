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

    private const float EyeMarginFactor = 0.02f;
    private const float MaxDistanceFactor = 20f;
    private const float NearFactor = 0.01f;
    private const float FarFactor = 50f;
    private const float MinRadius = 1e-3f;
    private static readonly float PitchLimitRadians = PitchLimitDegrees * MathF.PI / 180f;
    private static readonly float FovRadians = FovDegrees * MathF.PI / 180f;

    /// <summary>The point the camera orbits and looks at.</summary>
    public Vector3 Target { get; }

    /// <summary>World-unit distance from <see cref="Target"/> to <see cref="Eye"/>, clamped to
    /// <see cref="MinDistance"/>..<see cref="MaxDistance"/>.</summary>
    public float Distance { get; }

    /// <summary>Minimum corner of the subject's bounding box. <see cref="Target"/> never leaves the box.</summary>
    public Vector3 BoundsMin { get; }

    /// <summary>Maximum corner of the subject's bounding box.</summary>
    public Vector3 BoundsMax { get; }

    /// <summary>Half the box diagonal, in world units (at least 1e-3). The far limit and every clip
    /// plane scale with it, so a 16px sprite and a 1000px one behave alike.</summary>
    public float Radius { get; }

    /// <summary>Closest the eye may orbit to its target for the CURRENT orientation: the distance from
    /// <see cref="Target"/> along the eye direction to where that ray exits the subject's box, plus
    /// <c>0.02 × <see cref="Radius"/></c>. With the target inside the box this keeps the eye outside
    /// it from every angle, yet lets a flat sprite viewed head-on be inspected from just in front of
    /// its face. Depends on the orientation, so orbiting at the floor pushes the eye out as the exit
    /// distance grows.</summary>
    public float MinDistance => ExitDistance(Target, EyeDirection(Yaw, Pitch), BoundsMin, BoundsMax) + EyeMarginFactor * Radius;

    /// <summary>Farthest the eye may orbit from its target: <c>20 × <see cref="Radius"/></c>.</summary>
    public float MaxDistance => MaxDistanceFactor * Radius;

    /// <summary>Orbit angle around the vertical axis, radians. Unclamped — free spin.</summary>
    public float Yaw { get; }

    /// <summary>Orbit angle above/below level, radians, clamped to ±<see cref="PitchLimitDegrees"/>.</summary>
    public float Pitch { get; }

    /// <summary>Builds a camera, clamping <paramref name="target"/> into the box, and
    /// <paramref name="distance"/> and <paramref name="pitch"/> into range. Inputs must be finite;
    /// this constructor does not validate NaN or infinity. For finite inputs the box sets the
    /// distance limits, target confinement and clip planes.</summary>
    /// <param name="target">The point orbited and looked at; clamped into the box.</param>
    /// <param name="distance">Eye-to-target distance; clamped to <see cref="MinDistance"/>..<see cref="MaxDistance"/>.</param>
    /// <param name="yaw">Orbit angle around the vertical axis, radians.</param>
    /// <param name="pitch">Orbit angle above level, radians; clamped.</param>
    /// <param name="boundsMin">Minimum corner of the subject's box.</param>
    /// <param name="boundsMax">Maximum corner of the subject's box.</param>
    public Camera(Vector3 target, float distance, float yaw, float pitch, Vector3 boundsMin, Vector3 boundsMax)
    {
        BoundsMin = Vector3.Min(boundsMin, boundsMax);
        BoundsMax = Vector3.Max(boundsMin, boundsMax);
        Radius = RadiusOf(BoundsMin, BoundsMax);
        Target = Vector3.Clamp(target, BoundsMin, BoundsMax);
        Yaw = yaw;
        Pitch = Math.Clamp(pitch, -PitchLimitRadians, PitchLimitRadians);
        Distance = Math.Clamp(distance, MinDistance, MaxDistance);
    }

    private static float RadiusOf(Vector3 min, Vector3 max)
    {
        float r = Vector3.Distance(min, max) / 2f;
        return MathF.Max(float.IsFinite(r) ? r : MinRadius, MinRadius);
    }

    private static Vector3 EyeDirection(float yaw, float pitch)
    {
        float p = Math.Clamp(pitch, -PitchLimitRadians, PitchLimitRadians);
        float cosPitch = MathF.Cos(p);
        return new Vector3(cosPitch * MathF.Sin(yaw), MathF.Sin(p), cosPitch * MathF.Cos(yaw));
    }

    // Slab method: how far from `origin` (inside the box) along unit `direction` until the ray leaves it.
    private static float ExitDistance(Vector3 origin, Vector3 direction, Vector3 min, Vector3 max)
    {
        float exit = float.PositiveInfinity;
        for (int axis = 0; axis < 3; axis++)
        {
            float d = direction[axis];
            if (MathF.Abs(d) < 1e-6f) continue;
            float t = ((d > 0f ? max[axis] : min[axis]) - origin[axis]) / d;
            exit = MathF.Min(exit, MathF.Max(t, 0f));
        }
        return float.IsFinite(exit) ? exit : 0f;
    }

    /// <summary>World position of the eye, computed fresh on every access — cheap trigonometry, not
    /// the lazy-cache pattern that would need a mutable field.</summary>
    public Vector3 Eye
    {
        get
        {
            return Target + Distance * EyeDirection(Yaw, Pitch);
        }
    }

    /// <summary>The direction TOWARD the light, in world space: a head light riding the camera, up and
    /// a little to the left of the eye, so whatever faces the viewer is brightest at any orbit angle.
    /// Always a unit vector.</summary>
    public Vector3 LightDirection
    {
        get
        {
            var (right, up) = Basis();
            var toEye = Vector3.Normalize(Eye - Target);
            return Vector3.Normalize(0.85f * toEye + 0.35f * up - 0.25f * right);
        }
    }

    /// <summary>A new camera orbited by the given deltas; yaw is free, pitch clamps.</summary>
    /// <param name="deltaYawRadians">Change in yaw, in radians.</param>
    /// <param name="deltaPitchRadians">Change in pitch, in radians; the result is clamped.</param>
    /// <returns>The orbited camera; this one is unchanged.</returns>
    public Camera Orbit(float deltaYawRadians, float deltaPitchRadians) =>
        new(Target, Distance, Yaw + deltaYawRadians, Pitch + deltaPitchRadians, BoundsMin, BoundsMax);

    /// <summary>A new camera at <see cref="Distance"/> scaled by <paramref name="factor"/>, clamped.
    /// <paramref name="factor"/> below 1 moves the eye closer.</summary>
    /// <param name="factor">Multiplier for the eye distance.</param>
    /// <returns>The zoomed camera; this one is unchanged.</returns>
    public Camera Zoom(float factor) => new(Target, Distance * factor, Yaw, Pitch, BoundsMin, BoundsMax);

    /// <summary>A new camera panned so a <paramref name="screenDeltaPixels"/> drag moves content
    /// under the pointer by the same number of pixels, at the current <see cref="Distance"/> and
    /// <see cref="FovDegrees"/>.</summary>
    /// <param name="screenDeltaPixels">The drag, in screen pixels (x right, y down).</param>
    /// <param name="viewportHeight">Height of the viewport in pixels, which fixes how much world one pixel covers.</param>
    /// <returns>The panned camera; this one is unchanged.</returns>
    public Camera Pan(Vector2 screenDeltaPixels, int viewportHeight)
    {
        float worldPerPixel = WorldPerPixel(Distance, viewportHeight);
        var (right, up) = Basis();
        var delta = -right * screenDeltaPixels.X * worldPerPixel + up * screenDeltaPixels.Y * worldPerPixel;
        return new Camera(Target + delta, Distance, Yaw, Pitch, BoundsMin, BoundsMax);
    }

    /// <summary>A new camera zoomed by <paramref name="factor"/> while holding a heuristic anchor
    /// under <paramref name="pointerScreen"/> to within a pixel. The anchor is the pointer's pick ray
    /// sampled at the current eye-to-target distance, not an intersection with the mesh. A corrective
    /// pan uses the anchor's own view depth. If the correction would leave the subject box or raise
    /// the distance floor, zoom progress is limited along the anchored path; a fully constrained
    /// camera can remain unchanged. Zoom-in never increases the distance. Extreme numeric inputs
    /// or an anchor at/behind the eye can prevent the requested zoom from being completed.</summary>
    /// <param name="factor">Multiplier for the eye distance.</param>
    /// <param name="pointerScreen">The pointer position in screen pixels.</param>
    /// <param name="width">Viewport width in pixels.</param>
    /// <param name="height">Viewport height in pixels.</param>
    /// <returns>The zoomed camera; this one is unchanged.</returns>
    public Camera ZoomAnchored(float factor, Vector2 pointerScreen, int width, int height)
    {
        var anchor = Unproject(pointerScreen, width, height);
        var zoomed = Zoom(factor);
        if (zoomed.Distance == Distance) return this;
        var corrected = zoomed.CorrectTowardAnchor(anchor, pointerScreen, width, height);
        if (corrected is not null) return corrected;

        // Along an anchored zoom the corrective pan is affine in distance. Search that path from
        // the known valid original view rather than letting the constructor's new target-dependent
        // floor push the requested zoom backward. Each accepted candidate satisfies both clamps.
        var best = this;
        float accepted = 0f, rejected = 1f;
        for (int i = 0; i < 24; i++)
        {
            float progress = (accepted + rejected) / 2f;
            float distance = Distance + ((zoomed.Distance - Distance) * progress);
            var step = new Camera(Target, distance, Yaw, Pitch, BoundsMin, BoundsMax);
            var candidate = step.CorrectTowardAnchor(anchor, pointerScreen, width, height);
            if (candidate is not null)
            {
                accepted = progress;
                best = candidate;
            }
            else rejected = progress;
        }
        return best;
    }

    private Camera? CorrectTowardAnchor(Vector3 anchor, Vector2 pointerScreen, int width, int height)
    {
        var vp = ViewProjection(width, height);
        var clip = Vector4.Transform(new Vector4(anchor, 1f), vp);
        if (MathF.Abs(clip.W) < 1e-6f)
            return null; // no feasible anchored correction at this distance
        var screenNow = Viewport.NdcToScreen(new Vector2(clip.X / clip.W, clip.Y / clip.W), width, height);

        var error = pointerScreen - screenNow;

        var forward = Vector3.Normalize(Target - Eye);
        float anchorDepth = Vector3.Dot(anchor - Eye, forward);
        if (anchorDepth <= 0f)
            return null; // anchor is behind the eye along the view axis

        float worldPerPixel = WorldPerPixel(anchorDepth, height);
        var (right, up) = Basis();
        // Same sign convention as Pan: a positive screen-space error along X moves the camera along
        // -right (so the anchor's projection shifts by +error.X), and along +up for Y.
        var delta = -right * error.X * worldPerPixel + up * error.Y * worldPerPixel;
        var correctedTarget = Target + delta;
        var corrected = new Camera(correctedTarget, Distance, Yaw, Pitch, BoundsMin, BoundsMax);
        return corrected.Target == correctedTarget && corrected.Distance == Distance ? corrected : null;
    }

    /// <summary>The one function that owns view and projection together. The near and far planes
    /// scale with <see cref="Radius"/>.</summary>
    /// <param name="width">Viewport width in pixels; fixes the aspect ratio.</param>
    /// <param name="height">Viewport height in pixels; fixes the aspect ratio.</param>
    /// <returns>View multiplied by projection.</returns>
    public Matrix4x4 ViewProjection(int width, int height)
    {
        var view = Matrix4x4.CreateLookAt(Eye, Target, Vector3.UnitY);
        float aspect = width / (float)MathF.Max(height, 1);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(
            FovRadians, aspect, NearFactor * Radius, FarFactor * Radius);
        return view * projection;
    }

    /// <summary>A camera framing the given bounds: centered on the box, pulled back to fit its
    /// bounding sphere in the narrower of the vertical and horizontal fields of view with 15%
    /// margin, subject to <see cref="MaxDistance"/>. Framing is best effort: an extremely narrow
    /// viewport can still crop at that limit. Looks square-on at the front face (yaw 0, pitch 0)
    /// with the box center on the viewport center.</summary>
    /// <param name="min">Minimum corner of the subject box.</param>
    /// <param name="max">Maximum corner of the subject box.</param>
    /// <param name="aspect">Viewport width over height; a non-positive or non-finite value counts as 1.</param>
    /// <returns>The framing camera.</returns>
    public static Camera FramedOn(Vector3 min, Vector3 max, float aspect = 1f)
    {
        var center = (min + max) / 2f;
        float radius = RadiusOf(min, max);
        float safeAspect = aspect > 0f && float.IsFinite(aspect) ? aspect : 1f;
        float horizontalFov = 2f * MathF.Atan(MathF.Tan(FovRadians / 2f) * safeAspect);
        float fittedFov = MathF.Min(FovRadians, horizontalFov);
        float distance = radius / MathF.Sin(fittedFov / 2f) * 1.15f;
        return new Camera(center, distance, yaw: 0f, pitch: 0f, min, max);
    }

    /// <summary>The same view (target, distance, orientation) over a new subject box: the radius, the
    /// distance limits and the target's confinement follow the box, the camera does not jump (the
    /// target moves only if it would fall outside; the distance is raised to the new floor or reduced
    /// to the new maximum when necessary).</summary>
    /// <returns>The camera over the new box.</returns>
    /// <param name="min">Minimum corner of the new box.</param>
    /// <param name="max">Maximum corner of the new box.</param>
    public Camera WithSubject(Vector3 min, Vector3 max) => new(Target, Distance, Yaw, Pitch, min, max);

    /// <summary>Reframes on new bounds, discarding the current orientation — what "reset" means when
    /// the subject itself has bounds, as opposed to re-centering the existing view.</summary>
    /// <param name="min">Minimum corner of the subject box.</param>
    /// <param name="max">Maximum corner of the subject box.</param>
    /// <param name="aspect">Viewport width over height.</param>
    /// <returns>The framing camera, as <see cref="FramedOn"/> builds it.</returns>
    public Camera Reset(Vector3 min, Vector3 max, float aspect = 1f) => FramedOn(min, max, aspect);

    // The ONE perspective world-units-per-pixel formula: the size of one pixel on a plane `depth`
    // in front of the eye. Pan and the anchored-zoom correction both read it.
    private static float WorldPerPixel(float depth, int viewportHeight) =>
        2f * depth * MathF.Tan(FovRadians / 2f) / MathF.Max(viewportHeight, 1);

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
        var ndc = Viewport.ScreenToNdc(pointerScreen, width, height);
        Vector3 At(float z)
        {
            var world = Vector4.Transform(new Vector4(ndc.X, ndc.Y, z, 1f), inv);
            return new Vector3(world.X, world.Y, world.Z) / world.W;
        }
        var near = At(0f);
        var far = At(1f);
        var dir = Vector3.Normalize(far - near);
        return Eye + dir * Distance; // depth heuristic: the pick ray at the current orbit distance
    }
}
