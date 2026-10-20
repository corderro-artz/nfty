# Rise step 6: software rasterizer + preview control Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to
> implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the software rasterizer (`Nfty.Model/Render/`) that turns a `Mesh` + texture into
pixels, and the Avalonia preview control (`Nfty.App`) that drives it with an orbiting camera — no
page hosts it yet (that is step 7).

**Architecture:** `Camera` (immutable, one function owning the view+projection mapping) and
`RenderTarget` (a plain pixel+depth buffer) live in `Nfty.Model/Render/` beside `Raster` (the
transform/cull/z-buffer/sample/shade pipeline) — pure CPU arithmetic, no UI dependency, so it
renders identically headless and on screen and joins every existing visual sweep for free.
`Nfty.App`'s `ModelPreviewViewModel` resolves gestures to camera moves; `ModelPreviewView` owns a
`WriteableBitmap`, pointer capture and key bindings, and repaints by calling `Raster.Render` into a
`RenderTarget` it then blits.

**Tech Stack:** .NET 10, `System.Numerics` (already used by `Nfty.Model.Mesh`), ImageSharp (arrives
transitively via `Nfty.Core`, already pinned 3.1.11), Avalonia 12.1.1 `WriteableBitmap`
(`Nfty.App` already depends on Avalonia — no new package either side).

**Spec:** `docs/superpowers/specs/2026-09-25-rise-design.md` section "The preview — software
rasterizer", which states `docs/superpowers/specs/2026-09-22-model-preview-design.md` survives
**unchanged** for the rasterizer itself (architecture, camera, gestures, rendering steps, testing) —
only its "Hosts" section is superseded (3D appears only on the Rise page, step 7; nothing wires this
control into the Ingredient pane, inspector or a `ModelDialog` in this step). Read both.

## Global Constraints

- **.NET 10**; tests **xunit.v3**, named `Snake_case_sentences`, matching what `--filter` must match.
- **Zero new NuGet packages.** `Nfty.Model` gets ImageSharp transitively through its existing
  `Nfty.Core` reference. `Nfty.App` already has Avalonia and CommunityToolkit.Mvvm.
  `Nfty.App.csproj` gains a **new** `ProjectReference` to `Nfty.Model.csproj` — the first place the
  GUI touches `Nfty.Model` — that is a project reference, not a package, and is expected.
- **`TreatWarningsAsErrors` + `GenerateDocumentationFile`** (`Directory.Build.props`): every public
  type and member needs an XML doc comment, or the build fails.
- **US English** everywhere: identifiers, comments, doc text, UI strings.
- **House testing style:** exact-pixel or exact-value assertions on tiny synthetic inputs built in
  memory; **no golden-image files**; every fix shown red first; mutation-probe non-obvious logic
  (flip a sign, invert a clamp, and confirm a test catches it) before calling a task done.
- **D7 — no field in the `Nfty.Model` assembly may be written after construction**
  (`AssemblyRulesTests.No_field_in_the_assembly_can_be_written_after_construction`, which already
  covers every type via reflection — it needs no new test, but it WILL fail the build if this is
  missed). This is the single easiest way to burn a fix round on this plan, so it is called out
  everywhere it applies below: **`Camera` must be immutable.** Every "mutator" (`Orbit`, `Pan`,
  `Zoom`, `ZoomAnchored`, `Reset`) returns a **new** `Camera`, exactly like `Mesh`'s own documented
  rule ("every array is copied in and exposed read-only... so one mesh may be read from many
  threads"). `RenderTarget`'s `Color`/`Depth` arrays are fine as get-only auto-properties assigned
  once in its constructor — their **contents** mutate in place (`Clear`, per-pixel writes during
  render), but the **fields holding the array references** are never reassigned, which is what the
  rule actually checks (`FieldInfo.IsInitOnly`). `Raster` is a `static` class with no fields at all.
- **D6 — image parameters are `Image<Rgba32>`, never raw bytes** (`Riser.TryBuild`'s own rule,
  restated for `Raster.Render`'s texture parameter).
- **`Nfty.Core`/`Nfty.Ticket` never reference `Nfty.Model`** (existing
  `AssemblyRulesTests.Neither_the_engine_nor_the_roll_references_nfty_model`) — nothing in this plan
  touches those two assemblies, so this is automatic, just don't be tempted to "helpfully" move
  anything there.
- **`Perf.Measure` named scopes** on the one operation worth watching before step 7's page depends on
  it: `Raster.Render` wraps itself in `using var _ = Perf.Measure("Raster.Render");` — the exact
  pattern `Riser.TryBuild`, `ModelWriter.Write` and `StackDepth.Combine` already use
  (`src/Nfty.Model/Riser.cs:63`, `ModelWriter.cs:57`, `StackDepth.cs:51`). **Measured, never asserted
  as wall-clock** — CLAUDE.md's rule; a `RasterPerfTests` prints `Perf.Report()`, it does not assert a
  duration.
- **Pixel-art rendering rule:** every view root hosting an `<Image>` sets
  `RenderOptions.BitmapInterpolationMode="None"` (`PixelPerfectRenderingTests` derives the view list
  from markup and reads the **effective** mode off live controls) — `ModelPreviewView`'s `<Image>`
  hosting the `WriteableBitmap` needs this explicitly; the 3D content is rasterized pixel-exact and
  must not be resampled when Avalonia scales the control to its layout size.
- **Token brushes only in `Views`/`Themes/Tokens.axaml`, never a raw hex in a view** — the 3D
  viewport's background is a **new token**, defined in **both** theme dictionaries (light + dark),
  per the spec's "a token background, ... honours the app theme like everything else."

## Task 1: `Camera` and `RenderTarget`

**Files:**
- Create: `src/Nfty.Model/Render/Camera.cs`
- Create: `src/Nfty.Model/Render/RenderTarget.cs`
- Test: `tests/Nfty.Model.Tests/CameraTests.cs`
- Test: `tests/Nfty.Model.Tests/RenderTargetTests.cs`

**Interfaces:**
- Consumes: `System.Numerics.{Vector2,Vector3,Vector4,Matrix4x4}` only (already used by `Mesh`).
- Produces (for Task 2's `Raster` and Task 3's `ModelPreviewViewModel`):
  - `Camera(Vector3 target, float distance, float yaw, float pitch)` — constructor, clamps
    `distance` to `[Camera.MinDistance, Camera.MaxDistance]` and `pitch` to
    `±Camera.PitchLimitRadians` on the way in (so every `Camera` in existence is always valid; no
    caller can construct an out-of-range one and no getter needs to re-clamp).
  - `Camera.Target`, `Camera.Distance`, `Camera.Yaw`, `Camera.Pitch` — `float`/`Vector3` get-only
    properties.
  - `Camera.Eye` — `Vector3` get-only, computed on every access (expression-bodied, no backing
    field — cheap trig, not the lazy-cache pattern D7 forbids).
  - `Camera Orbit(float deltaYawRadians, float deltaPitchRadians)` — new camera, `Yaw` unclamped
    (free), `Pitch` clamped.
  - `Camera Zoom(float factor)` — new camera, `Distance` scaled by `factor` and clamped. `factor < 1`
    zooms in (closer), `factor > 1` zooms out.
  - `Camera Pan(Vector2 screenDeltaPixels, int viewportHeight)` — new camera, `Target` shifted in the
    camera's own screen-aligned right/up plane so a `screenDeltaPixels` drag feels 1:1 at the current
    `Distance`.
  - `Camera ZoomAnchored(float factor, Vector2 pointerScreen, int width, int height)` — new camera;
    the world point under `pointerScreen` stays under `pointerScreen` after the zoom, to within a
    pixel (the spec's own acceptance test). See worked algorithm below.
  - `Matrix4x4 ViewProjection(int width, int height)` — **the one function** that owns view and
    projection together (`Matrix4x4.CreateLookAt(Eye, Target, Vector3.UnitY) *
    Matrix4x4.CreatePerspectiveFieldOfView(...)`); every other mapping (picking, zoom-anchor) derives
    from this or its inverse, never restates the matrices beside it.
  - `static Camera FramedOn(Vector3 min, Vector3 max)` — a camera that frames a bounding box: `Target`
    = box center, `Distance` = fit the box's bounding-sphere radius into the vertical FOV with 15%
    margin, `Yaw` = 30°, `Pitch` = 20° (a three-quarter starting view, not a flat silhouette).
  - `Camera Reset(Vector3 min, Vector3 max)` — instance-method convenience for `FramedOn(min, max)`
    (so a caller holding a `Camera` doesn't need the static import); returns a new camera, current
    orientation discarded (matches "Home"/"double-click" resetting fully, not just re-framing).
  - `const float PitchLimitDegrees = 89f`, `const float FovDegrees = 40f` — public, so
    `ModelPreviewViewModel`'s keyboard-step math and any future dialog agree with the camera's own
    numbers instead of restating them.
- Produces (for Task 2's `Raster`):
  - `RenderTarget(int width, int height)` — allocates `Color` (`Rgba32[]`, length `width*height`,
    row-major) and `Depth` (`float[]`, same length).
  - `RenderTarget.Width`, `RenderTarget.Height` — `int` get-only.
  - `RenderTarget.Color` — `Rgba32[]` get-only (array reference fixed at construction; contents
    mutate freely — see the D7 note in Global Constraints).
  - `RenderTarget.Depth` — `float[]` get-only, same shape.
  - `void Clear(Rgba32 background)` — fills `Color` with `background` and `Depth` with
    `float.PositiveInfinity` (nothing drawn is "closer than infinity", so the first triangle at any
    pixel always passes the depth test).

- [ ] **Step 1: Write the failing `Camera` tests**

```csharp
using System.Numerics;

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
```

- [ ] **Step 2: Run the tests, confirm they fail with "Camera does not exist"**

Run: `dotnet test tests/Nfty.Model.Tests --filter FullyQualifiedName~CameraTests`
Expected: build error (type not found) or all FAIL.

- [ ] **Step 3: Implement `Camera`**

```csharp
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
    /// corrective <see cref="Pan"/> that cancels the screen-space error the zoom introduced for the
    /// off-center anchor point.</summary>
    public Camera ZoomAnchored(float factor, Vector2 pointerScreen, int width, int height)
    {
        var anchor = Unproject(pointerScreen, width, height);
        var zoomed = Zoom(factor);

        var vpAfter = zoomed.ViewProjection(width, height);
        var clip = Vector4.Transform(new Vector4(anchor, 1f), vpAfter);
        if (MathF.Abs(clip.W) < 1e-6f)
            return zoomed; // anchor landed behind the eye; nothing sane to correct toward
        var ndc = new Vector2(clip.X / clip.W, clip.Y / clip.W);
        var screenAfter = new Vector2(
            (ndc.X * 0.5f + 0.5f) * width,
            (1f - (ndc.Y * 0.5f + 0.5f)) * height);

        var error = pointerScreen - screenAfter;
        return zoomed.Pan(-error, height);
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
```

**If the zoom-anchor test does not land within a pixel:** the depth heuristic in `Unproject` (using
`Distance` as the ray depth) is exact only when the anchor is near the view's forward axis, and
approximate toward the screen edges. If the single-pass correction above is not tight enough at the
test's off-center pointer, either (a) run the zoom→project→correct sequence twice (the residual error
shrinks geometrically, so a second `Pan` correction converges fast), or (b) compute `Pan`'s
`worldPerPixel` using the anchor's own forward-axis depth (`Vector3.Dot(anchor - zoomed.Eye,
Vector3.Normalize(zoomed.Target - zoomed.Eye))`) instead of `zoomed.Distance` — that removes the
off-axis discrepancy entirely. Pick whichever passes; leave a one-line comment on why.

- [ ] **Step 4: Run the `Camera` tests, confirm they pass**

Run: `dotnet test tests/Nfty.Model.Tests --filter FullyQualifiedName~CameraTests`
Expected: PASS, all 8.

- [ ] **Step 5: Write the failing `RenderTarget` tests**

```csharp
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Tests;

public class RenderTargetTests
{
    [Fact]
    public void A_new_target_allocates_one_color_and_depth_slot_per_pixel()
    {
        var target = new RenderTarget(4, 3);
        Assert.Equal(4, target.Width);
        Assert.Equal(3, target.Height);
        Assert.Equal(12, target.Color.Length);
        Assert.Equal(12, target.Depth.Length);
    }

    [Fact]
    public void Clear_fills_every_pixel_with_the_background_and_resets_depth_to_infinity()
    {
        var target = new RenderTarget(2, 2);
        var background = new Rgba32(10, 20, 30, 255);

        target.Color[0] = new Rgba32(255, 255, 255, 255);
        target.Depth[0] = 0.1f;

        target.Clear(background);

        Assert.All(target.Color, c => Assert.Equal(background, c));
        Assert.All(target.Depth, d => Assert.Equal(float.PositiveInfinity, d));
    }

    [Fact]
    public void Zero_sized_dimensions_are_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RenderTarget(0, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RenderTarget(4, 0));
    }
}
```

- [ ] **Step 6: Run, confirm fail; implement `RenderTarget`; run, confirm pass**

```csharp
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Render;

/// <summary>A plain pixel-and-depth surface <see cref="Raster"/> writes into: one background-filled
/// color per pixel and a z-buffer initialized to "nothing here yet". The array references are fixed
/// at construction (D7); their contents mutate freely as <see cref="Raster"/> draws.</summary>
public sealed class RenderTarget
{
    /// <summary>Pixels across.</summary>
    public int Width { get; }

    /// <summary>Pixels down.</summary>
    public int Height { get; }

    /// <summary>One color per pixel, row-major, length <see cref="Width"/> × <see cref="Height"/>.</summary>
    public Rgba32[] Color { get; }

    /// <summary>One depth value per pixel, same shape as <see cref="Color"/>. Smaller is closer;
    /// <see cref="float.PositiveInfinity"/> means nothing has drawn there yet.</summary>
    public float[] Depth { get; }

    public RenderTarget(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        Color = new Rgba32[width * height];
        Depth = new float[width * height];
    }

    /// <summary>Resets every pixel to <paramref name="background"/> and every depth to infinity.</summary>
    public void Clear(Rgba32 background)
    {
        Array.Fill(Color, background);
        Array.Fill(Depth, float.PositiveInfinity);
    }
}
```

Run: `dotnet test tests/Nfty.Model.Tests --filter "FullyQualifiedName~CameraTests|FullyQualifiedName~RenderTargetTests"`
Expected: PASS, all.

- [ ] **Step 7: Commit**

```bash
git add src/Nfty.Model/Render/Camera.cs src/Nfty.Model/Render/RenderTarget.cs tests/Nfty.Model.Tests/CameraTests.cs tests/Nfty.Model.Tests/RenderTargetTests.cs
git commit -m "feat(model): immutable turntable Camera and a RenderTarget for the preview rasterizer"
```

---

## Task 2: `Raster`

**Files:**
- Create: `src/Nfty.Model/Render/Raster.cs`
- Test: `tests/Nfty.Model.Tests/RasterTests.cs`
- Test: `tests/Nfty.Model.Tests/RasterPerfTests.cs`

**Interfaces:**
- Consumes: `Camera`/`RenderTarget` from Task 1; `Mesh` (existing, `src/Nfty.Model/Mesh.cs`) —
  `Positions`/`Normals`/`TexCoords`/`Indices`/`TextureWidth`/`TextureHeight`; `Image<Rgba32>` texture
  (never raw bytes, D6); `Nfty.Core.Diagnostics.Perf` (existing, used exactly as
  `src/Nfty.Model/Riser.cs:63` uses it).
- Produces (for Task 4's `ModelPreviewView`):
  - `static class Raster` with `static void Render(Mesh mesh, Image<Rgba32> texture, Matrix4x4
    viewProjection, RenderTarget target)` — does **not** clear `target` first (the caller decides the
    background and calls `target.Clear(background)` itself, once per frame, before rendering — Raster
    only draws triangles into whatever state the target is already in, which is what lets a future
    caller composite more than one mesh into one target without this method knowing anything about
    "the frame" as a concept).

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Numerics;
using Nfty.Model.Render;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Tests;

public class RasterTests
{
    [Fact]
    public void Back_face_culling_draws_from_exactly_half_a_cubes_triangles()
    {
        var frontColor = new Rgba32(255, 0, 0, 255);
        var backColor = new Rgba32(0, 255, 0, 255);
        var mesh = UnitCube();
        using var texture = TwoColorTexture(front: frontColor, back: backColor);
        var camera = new Camera(Vector3.Zero, distance: 4f, yaw: 0.6f, pitch: 0.4f);
        var target = new RenderTarget(64, 64);
        target.Clear(new Rgba32(0, 0, 0, 0));

        Raster.Render(mesh, texture, camera.ViewProjection(64, 64), target);

        // A generic camera angle on a closed cube shows exactly three of its six faces — this camera
        // was placed in the +X+Y+Z octant on purpose so the three UnitCube marks "front" (+X, +Y, +Z)
        // are exactly the three that are visible. Shading darkens whatever shows, so this checks
        // absence of the back texel rather than an exact lit color: backColor is never sampled at all
        // when culling is correct, regardless of what brightness multiplies it by.
        var colors = target.Color.Where(c => c.A > 0).Distinct().ToList();
        Assert.NotEmpty(colors);
        Assert.DoesNotContain(backColor, colors);
    }

    [Fact]
    public void The_z_buffer_resolves_a_solid_every_visible_pixel_comes_from_the_nearer_face()
    {
        // Two quads facing the camera, same footprint, one behind the other. Both share one face
        // normal, so shading (whatever it turns out to be) scales both identically — which lets this
        // assert on channel identity (red-only vs blue-only) rather than replicate Raster's private
        // brightness formula to predict an exact lit value.
        var mesh = TwoStackedQuads();
        using var texture = HalfSplitTexture(near: new Rgba32(200, 0, 0, 255), far: new Rgba32(0, 0, 200, 255));
        var camera = new Camera(Vector3.Zero, distance: 6f, yaw: 0f, pitch: 0f);
        var target = new RenderTarget(32, 32);
        target.Clear(new Rgba32(0, 0, 0, 0));

        Raster.Render(mesh, texture, camera.ViewProjection(32, 32), target);

        var covered = target.Color.Where(c => c.A > 0).ToList();
        Assert.NotEmpty(covered);
        Assert.All(covered, c =>
        {
            Assert.True(c.R > 0, "the near (red) face should be what's visible");
            Assert.Equal(0, c.B);
        });
    }

    [Fact]
    public void Nearest_neighbour_sampling_of_an_eight_by_eight_checker_produces_exactly_two_colours()
    {
        var mesh = SingleQuadFacingCamera(size: 2f);
        using var texture = Checker(8, new Rgba32(255, 255, 255, 255), new Rgba32(0, 0, 0, 255));
        var camera = new Camera(Vector3.Zero, distance: 4f, yaw: 0f, pitch: 0f);
        var target = new RenderTarget(256, 256);
        target.Clear(new Rgba32(50, 50, 50, 255));

        Raster.Render(mesh, texture, camera.ViewProjection(256, 256), target);

        var onFace = target.Color.Where(c => c != new Rgba32(50, 50, 50, 255)).Distinct().ToList();
        Assert.Equal(2, onFace.Count);
        // Black is invariant under the brightness multiply (0 × anything is 0), so it is the one
        // exact literal this test can still pin regardless of shading. The checker's white texel
        // becomes some shaded grey instead — asserted as "equal channels, brighter than black" rather
        // than a literal (255,255,255,255) that shading would make false.
        Assert.Contains(onFace, c => c.R == 0 && c.G == 0 && c.B == 0);
        Assert.Contains(onFace, c => c.R == c.G && c.G == c.B && c.R > 0);
    }

    [Fact]
    public void Flat_shading_makes_every_pixel_of_one_face_the_same_colour()
    {
        var mesh = SingleQuadFacingCamera(size: 2f);
        using var texture = SolidTexture(new Rgba32(128, 64, 200, 255));
        var camera = new Camera(Vector3.Zero, distance: 4f, yaw: 0.3f, pitch: 0.2f);
        var target = new RenderTarget(128, 128);
        target.Clear(new Rgba32(0, 0, 0, 0));

        Raster.Render(mesh, texture, camera.ViewProjection(128, 128), target);

        var faceColors = target.Color.Where(c => c.A > 0).Distinct().ToList();
        Assert.Single(faceColors);
    }

    // ---- fixtures ----
    // Hand-built the same way tests/Nfty.Model.Tests/MeshTests.cs already hand-builds small meshes:
    // literal position/normal/uv/index arrays straight into the Mesh constructor, no Riser involved
    // — far simpler than rising one from pixel art for a fixture this small.

    /// <summary>A 2x2x2 cube centered at the origin. The +X/+Y/+Z faces sample <c>TwoColorTexture</c>'s
    /// "front" texel; -X/-Y/-Z sample its "back" texel — so whichever three faces a given camera
    /// angle exposes (any generic angle on a cube shows exactly half its faces) prove culling by
    /// which color shows up, never by counting hidden triangles this method has no reason to expose.</summary>
    private static Mesh UnitCube()
    {
        var positions = new List<float>();
        var normals = new List<float>();
        var uvs = new List<float>();
        var indices = new List<uint>();

        void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, bool front)
        {
            uint baseIndex = (uint)(positions.Count / 3);
            foreach (var p in new[] { a, b, c, d })
                positions.AddRange([p.X, p.Y, p.Z]);
            for (int i = 0; i < 4; i++)
                normals.AddRange([normal.X, normal.Y, normal.Z]);
            float u = front ? 0.75f : 0.25f; // TwoColorTexture is 2 wide: u<0.5 -> texel 0 (back), else texel 1 (front)
            for (int i = 0; i < 4; i++)
                uvs.AddRange([u, 0.5f]);
            indices.AddRange([baseIndex, baseIndex + 1, baseIndex + 2, baseIndex, baseIndex + 2, baseIndex + 3]);
        }

        // Each quad's four corners are CCW seen from outside that face.
        AddQuad(new Vector3(1, -1, 1), new Vector3(1, -1, -1), new Vector3(1, 1, -1), new Vector3(1, 1, 1), new Vector3(1, 0, 0), front: true);      // +X
        AddQuad(new Vector3(-1, -1, -1), new Vector3(-1, -1, 1), new Vector3(-1, 1, 1), new Vector3(-1, 1, -1), new Vector3(-1, 0, 0), front: false); // -X
        AddQuad(new Vector3(-1, 1, -1), new Vector3(-1, 1, 1), new Vector3(1, 1, 1), new Vector3(1, 1, -1), new Vector3(0, 1, 0), front: true);       // +Y
        AddQuad(new Vector3(-1, -1, 1), new Vector3(-1, -1, -1), new Vector3(1, -1, -1), new Vector3(1, -1, 1), new Vector3(0, -1, 0), front: false);  // -Y
        AddQuad(new Vector3(-1, -1, 1), new Vector3(1, -1, 1), new Vector3(1, 1, 1), new Vector3(-1, 1, 1), new Vector3(0, 0, 1), front: true);       // +Z
        AddQuad(new Vector3(1, -1, -1), new Vector3(-1, -1, -1), new Vector3(-1, 1, -1), new Vector3(1, 1, -1), new Vector3(0, 0, -1), front: false);  // -Z

        return new Mesh(positions.ToArray(), normals.ToArray(), uvs.ToArray(), indices.ToArray(),
            textureWidth: 2, textureHeight: 1);
    }

    /// <summary>Two quads with the same footprint and the same +Z-facing normal (so both pass
    /// back-face culling identically — a planar quad's on-screen winding does not change when it
    /// moves along the camera's own look axis), one at z=1 (near) and one at z=-1 (far).</summary>
    private static Mesh TwoStackedQuads()
    {
        float[] positions =
        [
            -1, -1, 1,   1, -1, 1,   1, 1, 1,   -1, 1, 1,      // near, z=1
            -1, -1, -1,  1, -1, -1,  1, 1, -1,  -1, 1, -1,     // far, z=-1
        ];
        float[] normals =
        [
            0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1,
            0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1,
        ];
        float[] uvs =
        [
            0, 0.25f, 1, 0.25f, 1, 0.25f, 0, 0.25f,   // near quad samples HalfSplitTexture's near rows
            0, 0.75f, 1, 0.75f, 1, 0.75f, 0, 0.75f,   // far quad samples its far rows
        ];
        uint[] indices = [0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7];
        return new Mesh(positions, normals, uvs, indices, textureWidth: 4, textureHeight: 4);
    }

    /// <summary>One quad at z=0, normal +Z, spanning [-size/2, size/2] in x and y.</summary>
    private static Mesh SingleQuadFacingCamera(float size)
    {
        float h = size / 2f;
        float[] positions = [-h, -h, 0, h, -h, 0, h, h, 0, -h, h, 0];
        float[] normals = [0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1];
        float[] uvs = [0, 1, 1, 1, 1, 0, 0, 0];
        uint[] indices = [0, 1, 2, 0, 2, 3];
        return new Mesh(positions, normals, uvs, indices, textureWidth: 8, textureHeight: 8);
    }

    private static Image<Rgba32> SolidTexture(Rgba32 color)
    {
        var img = new Image<Rgba32>(4, 4);
        img.Mutate(ctx => ctx.BackgroundColor(color));
        return img;
    }

    private static Image<Rgba32> TwoColorTexture(Rgba32 front, Rgba32 back)
    {
        var img = new Image<Rgba32>(2, 1);
        img[0, 0] = back;
        img[1, 0] = front;
        return img;
    }

    private static Image<Rgba32> HalfSplitTexture(Rgba32 near, Rgba32 far)
    {
        var img = new Image<Rgba32>(4, 4);
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                img[x, y] = y < 2 ? near : far;
        return img;
    }

    private static Image<Rgba32> Checker(int size, Rgba32 a, Rgba32 b)
    {
        var img = new Image<Rgba32>(size, size);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                img[x, y] = ((x + y) % 2 == 0) ? a : b;
        return img;
    }
}
```

- [ ] **Step 2: Run, confirm every test fails** (`Raster` does not exist yet, or fixtures throw)

- [ ] **Step 3: Implement `Raster`**

```csharp
using System.Numerics;
using Nfty.Core.Diagnostics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Render;

/// <summary>
/// The software rasterizer: transforms a <see cref="Mesh"/> by a view-projection matrix, culls
/// back faces, resolves visibility with a z-buffer, samples the texture with nearest-neighbour
/// filtering, and shades with flat, quantized lighting — the whole rendering pipeline the spec lists
/// (see <c>docs/superpowers/specs/2026-09-22-model-preview-design.md</c>, "Rendering"). Pure CPU
/// pixel arithmetic: it renders identically headless and on screen, which is the entire reason this
/// exists instead of a GPU control (`Avalonia.Headless` has no GL context).
/// </summary>
public static class Raster
{
    private const int ShadeLevels = 4;
    private const float Ambient = 0.35f;
    private static readonly Vector3 LightDirection = Vector3.Normalize(new Vector3(0.4f, 1f, 0.6f));

    /// <summary>Draws every triangle of <paramref name="mesh"/>, sampling <paramref name="texture"/>
    /// nearest-neighbour, into <paramref name="target"/>. Does not clear <paramref name="target"/>
    /// first — the caller owns the background and calls <see cref="RenderTarget.Clear"/> itself.</summary>
    public static void Render(Mesh mesh, Image<Rgba32> texture, Matrix4x4 viewProjection, RenderTarget target)
    {
        using var _ = Perf.Measure("Raster.Render");

        var indices = mesh.Indices;
        var positions = mesh.Positions;
        var normals = mesh.Normals;
        var uvs = mesh.TexCoords;

        for (int t = 0; t < indices.Count; t += 3)
        {
            uint i0 = indices[t], i1 = indices[t + 1], i2 = indices[t + 2];
            var (clip0, screen0) = ToScreen(positions, i0, viewProjection, target.Width, target.Height);
            var (clip1, screen1) = ToScreen(positions, i1, viewProjection, target.Width, target.Height);
            var (clip2, screen2) = ToScreen(positions, i2, viewProjection, target.Width, target.Height);

            // Screen space is Y-down; the mesh's indices are CCW seen from outside in world space
            // (Mesh.cs's own documented rule). This signed area's sign is the empirical link between
            // those two facts — fixed once by the back-face-culling test above and never touched
            // again. Positive area here means the triangle winds clockwise on screen, which is what a
            // CCW-in-world, front-facing triangle becomes once Y flips for screen space and the
            // camera looks toward -forward; that is the front-facing case we keep.
            float signedArea = (screen1.X - screen0.X) * (screen2.Y - screen0.Y)
                              - (screen2.X - screen0.X) * (screen1.Y - screen0.Y);
            if (signedArea <= 0f)
                continue; // back-facing or degenerate

            // Face normal: all three vertices of one risen quad's triangle share one normal already
            // (Mesh.cs: a vertex is shared only within its own quad), so any vertex's normal is the
            // face normal — no cross-product needed, and flat shading falls out for free.
            var faceNormal = new Vector3(normals[(int)i0 * 3], normals[(int)i0 * 3 + 1], normals[(int)i0 * 3 + 2]);
            float lit = Math.Clamp(Vector3.Dot(faceNormal, LightDirection), 0f, 1f);
            int level = Math.Clamp((int)(lit * ShadeLevels), 0, ShadeLevels - 1);
            float brightness = Ambient + (1f - Ambient) * (level / (float)(ShadeLevels - 1));

            RasterizeTriangle(
                (screen0, clip0.W, uvs[(int)i0 * 2], uvs[(int)i0 * 2 + 1]),
                (screen1, clip1.W, uvs[(int)i1 * 2], uvs[(int)i1 * 2 + 1]),
                (screen2, clip2.W, uvs[(int)i2 * 2], uvs[(int)i2 * 2 + 1]),
                brightness, texture, target);
        }
    }

    private static (Vector4 Clip, Vector2 Screen) ToScreen(
        IReadOnlyList<float> positions, uint index, Matrix4x4 viewProjection, int width, int height)
    {
        var world = new Vector3(positions[(int)index * 3], positions[(int)index * 3 + 1], positions[(int)index * 3 + 2]);
        var clip = Vector4.Transform(new Vector4(world, 1f), viewProjection);
        var ndc = new Vector2(clip.X / clip.W, clip.Y / clip.W);
        var screen = new Vector2((ndc.X * 0.5f + 0.5f) * width, (1f - (ndc.Y * 0.5f + 0.5f)) * height);
        return (clip, screen);
    }

    private static void RasterizeTriangle(
        (Vector2 Screen, float W, float U, float V) v0,
        (Vector2 Screen, float W, float U, float V) v1,
        (Vector2 Screen, float W, float U, float V) v2,
        float brightness, Image<Rgba32> texture, RenderTarget target)
    {
        int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(v0.Screen.X, MathF.Min(v1.Screen.X, v2.Screen.X))));
        int maxX = Math.Min(target.Width - 1, (int)MathF.Ceiling(MathF.Max(v0.Screen.X, MathF.Max(v1.Screen.X, v2.Screen.X))));
        int minY = Math.Max(0, (int)MathF.Floor(MathF.Min(v0.Screen.Y, MathF.Min(v1.Screen.Y, v2.Screen.Y))));
        int maxY = Math.Min(target.Height - 1, (int)MathF.Ceiling(MathF.Max(v0.Screen.Y, MathF.Max(v1.Screen.Y, v2.Screen.Y))));

        float area = EdgeFunction(v0.Screen, v1.Screen, v2.Screen);
        if (MathF.Abs(area) < 1e-8f) return;

        // Perspective-correct interpolation: barycentric weights on 1/w, not on screen-space
        // position directly, or texturing would warp on any triangle not exactly parallel to the
        // screen.
        float invW0 = 1f / v0.W, invW1 = 1f / v1.W, invW2 = 1f / v2.W;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float w0 = EdgeFunction(v1.Screen, v2.Screen, p) / area;
                float w1 = EdgeFunction(v2.Screen, v0.Screen, p) / area;
                float w2 = EdgeFunction(v0.Screen, v1.Screen, p) / area;
                if (w0 < 0f || w1 < 0f || w2 < 0f) continue;

                float invW = w0 * invW0 + w1 * invW1 + w2 * invW2;
                float depth = 1f / invW; // linear-in-view-space depth proxy: smaller is nearer

                int pixel = y * target.Width + x;
                if (depth >= target.Depth[pixel]) continue;

                float u = (w0 * v0.U * invW0 + w1 * v1.U * invW1 + w2 * v2.U * invW2) / invW;
                float v = (w0 * v0.V * invW0 + w1 * v1.V * invW1 + w2 * v2.V * invW2) / invW;
                int tx = Math.Clamp((int)(u * texture.Width), 0, texture.Width - 1);
                int ty = Math.Clamp((int)(v * texture.Height), 0, texture.Height - 1);
                var texel = texture[tx, ty];
                if (texel.A == 0) continue; // transparent texel: nothing drawn, depth untouched

                target.Depth[pixel] = depth;
                target.Color[pixel] = new Rgba32(
                    (byte)(texel.R * brightness),
                    (byte)(texel.G * brightness),
                    (byte)(texel.B * brightness),
                    texel.A);
            }
        }
    }

    private static float EdgeFunction(Vector2 a, Vector2 b, Vector2 c) =>
        (c.X - a.X) * (b.Y - a.Y) - (c.Y - a.Y) * (b.X - a.X);
}
```

**A note on the winding-sign comment above:** write the comment to match whatever sign the tests
actually require — derive it empirically in this red/green cycle (try `signedArea <= 0f` first; if
the cube test shows the wrong faces, the condition is backwards, flip it) and then make the comment
describe the convention that turned out true, not a guess. This is the one place in the task where
"run it and see" is the correct way to pin an exact value, same as `StackDepth`'s I1 fix pinned its
condition by a revert-and-confirm-red probe.

- [ ] **Step 4: Run the `Raster` tests, confirm they pass**

Run: `dotnet test tests/Nfty.Model.Tests --filter FullyQualifiedName~RasterTests`
Expected: PASS, all 4.

- [ ] **Step 5: Mutation-probe the back-face cull and the z-buffer**

Flip `signedArea <= 0f` to `>= 0f` — confirm `Back_face_culling_draws_from_exactly_half_a_cubes_triangles`
goes red (the wrong faces draw). Flip `depth >= target.Depth[pixel]` to `<=` — confirm
`The_z_buffer_resolves_a_solid_every_visible_pixel_comes_from_the_nearer_face` goes red (far face
wins). Revert both before continuing.

- [ ] **Step 6: Write and run the perf test** (measured, not asserted — see Global Constraints)

```csharp
using System.Globalization;
using System.Numerics;
using Nfty.Core.Diagnostics;
using Nfty.Core.Model;
using Nfty.Model.Render;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Tests;

/// <summary>MEASURED, NEVER ASSERTED AS WALL-CLOCK — same discipline as <see cref="RisePerfTests"/>.
/// Establishes a baseline before step 7's Rise page starts calling this every pointer move during a
/// camera drag. Run with
/// <c>dotnet test tests/Nfty.Model.Tests --filter FullyQualifiedName~RasterPerfTests -l "console;verbosity=detailed"</c>.</summary>
public class RasterPerfTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(64)]
    [InlineData(512)]
    public void Rendering_a_real_risen_mesh_is_measured_at_preview_resolution(int textureSize)
    {
        using var art = Disc(textureSize);
        var depth = Dome(art);
        var mesh = Riser.TryBuild(art, depth, new RiseOptions(), $"{textureSize}px disc")!;
        var camera = Camera.FramedOn(mesh.Min, mesh.Max);
        var target = new RenderTarget(512, 512);

        Perf.Enable();
        try
        {
            Perf.Reset();
            target.Clear(new Rgba32(0, 0, 0, 0));
            Raster.Render(mesh, art, camera.ViewProjection(512, 512), target);

            Assert.Contains("Raster.Render", Perf.Snapshot().Select(e => e.Name));
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{textureSize}px source, 512px target: {mesh.TriangleCount:N0} triangles"));
            output.WriteLine(Perf.Report());
        }
        finally
        {
            Perf.Disable();
        }
    }

    private static Image<Rgba32> Disc(int size)
    {
        var img = new Image<Rgba32>(size, size);
        float c = (size - 1) / 2f, r = (size / 2f) - 1;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = MathF.Sqrt(((x - c) * (x - c)) + ((y - c) * (y - c)));
                img[x, y] = d <= r
                    ? new Rgba32((byte)(x * 255 / size), (byte)(y * 255 / size), 128, 255)
                    : new Rgba32(0, 0, 0, 0);
            }
        return img;
    }

    private static DepthMap Dome(Image<Rgba32> art)
    {
        float c = (art.Width - 1) / 2f, r = art.Width / 2f;
        return Art.Depth(art, (x, y) =>
        {
            float d = MathF.Sqrt(((x - c) * (x - c)) + ((y - c) * (y - c)));
            return (Math.Clamp(8 - (int)(d * 8 / r), 0, 8), 1);
        });
    }
}
```

Run: `dotnet test tests/Nfty.Model.Tests --filter FullyQualifiedName~RasterPerfTests -l "console;verbosity=detailed"`
Expected: PASS; note the printed numbers in the task report (no threshold to meet, just a baseline).

- [ ] **Step 7: Commit**

```bash
git add src/Nfty.Model/Render/Raster.cs tests/Nfty.Model.Tests/RasterTests.cs tests/Nfty.Model.Tests/RasterPerfTests.cs
git commit -m "feat(model): Raster — transform, back-face cull, z-buffer, nearest sample, flat shade"
```

---

## Task 3: `ModelPreviewViewModel`

**Files:**
- Create: `src/Nfty.App/ViewModels/ModelPreviewViewModel.cs`
- Modify: `src/Nfty.App/Nfty.App.csproj` — add `<ProjectReference Include="..\Nfty.Model\Nfty.Model.csproj" />`
- Test: `tests/Nfty.App.Tests/ModelPreviewViewModelTests.cs`

**Interfaces:**
- Consumes: `Camera` (Task 1); existing `Nfty.App.ViewModels.ViewModelBase`/`ObservableObject`
  pattern (see `src/Nfty.App/ViewModels/IngredientEditorViewModel.cs` for house conventions —
  `[ObservableProperty]` fields, `[RelayCommand]` methods from CommunityToolkit.Mvvm, already used
  throughout `Nfty.App`).
- Produces (for Task 4's `ModelPreviewView`):
  - `ModelPreviewViewModel(Vector3 boundsMin, Vector3 boundsMax)` — constructs with
    `Camera.FramedOn(boundsMin, boundsMax)` as the starting camera.
  - `Camera CameraState` — `[ObservableProperty]`-backed, so the view's paint trigger can bind or
    subscribe to `PropertyChanged` on it.
  - `void Orbit(Vector2 pointerDeltaPixels)` — turns a raw pointer-drag delta into
    `CameraState.Orbit(...)`; **orbit speed**: `deltaYaw = -pointerDeltaPixels.X * OrbitRadiansPerPixel`,
    `deltaPitch = -pointerDeltaPixels.Y * OrbitRadiansPerPixel`, where `OrbitRadiansPerPixel = MathF.PI
    / 400f` (a full horizontal drag across a ~400px control roughly spins it half a turn — a starting
    feel, not a measured constant; note it as tunable once step 7 has a real page to drag on).
  - `void Pan(Vector2 pointerDeltaPixels, int viewportHeight)` — `CameraState =
    CameraState.Pan(pointerDeltaPixels, viewportHeight)`.
  - `void Zoom(float wheelDelta, Vector2 pointerScreen, int width, int height)` — one wheel notch
    (`wheelDelta` the raw `PointerWheelEventArgs.Delta.Y`, positive = away from user = zoom in per
    Avalonia's convention) maps to a `factor` via `MathF.Pow(0.9f, MathF.Sign(wheelDelta))` (each
    notch is a flat 10% step regardless of the OS's delta magnitude — matches how mouse wheels differ
    in reported magnitude across platforms), then `CameraState = CameraState.ZoomAnchored(factor,
    pointerScreen, width, height)`.
  - `void OrbitStep(Vector2 direction)` — the `Left/Right/Up/Down` keyboard path: one fixed step
    (`KeyOrbitStepRadians = 15f * MathF.PI / 180f`) in the given axis (`direction` is one of
    `(±1,0)`/`(0,±1)`, `Left`/`Right` change yaw, `Up`/`Down` change pitch — the keyboard mirrors the
    drag axes).
  - `void ZoomStep(float sign)` — the `+`/`-` keyboard path: `CameraState = CameraState.Zoom(sign > 0
    ? 0.9f : 1f / 0.9f)`.
  - `void Reset()` — the `Home`/double-click path: `CameraState = Camera.FramedOn(_boundsMin,
    _boundsMax)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Numerics;
using Nfty.App.ViewModels;
using Nfty.Model.Render;

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
```

- [ ] **Step 2: Run, confirm fail**

- [ ] **Step 3: Add the project reference, then implement `ModelPreviewViewModel`**

Edit `src/Nfty.App/Nfty.App.csproj`, inside the existing `<ItemGroup>` that has the `Nfty.Core`
`ProjectReference`:

```xml
<ProjectReference Include="..\Nfty.Model\Nfty.Model.csproj" />
```

```csharp
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

    private readonly Vector3 _boundsMin;
    private readonly Vector3 _boundsMax;

    [ObservableProperty]
    private Camera _cameraState;

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

    /// <summary>One wheel event, anchored on the pointer.</summary>
    public void Zoom(float wheelDelta, Vector2 pointerScreen, int width, int height)
    {
        if (wheelDelta == 0f) return;
        float factor = MathF.Pow(ZoomStepFactor, MathF.Sign(wheelDelta));
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

    /// <summary>Home or double-click: back to the original framed camera, discarding orientation.</summary>
    public void Reset() => CameraState = Camera.FramedOn(_boundsMin, _boundsMax);
}
```

- [ ] **Step 4: Run the tests, confirm they pass**

Run: `dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~ModelPreviewViewModelTests`
Expected: PASS, all 7.

- [ ] **Step 5: Commit**

```bash
git add src/Nfty.App/Nfty.App.csproj src/Nfty.App/ViewModels/ModelPreviewViewModel.cs tests/Nfty.App.Tests/ModelPreviewViewModelTests.cs
git commit -m "feat(app): ModelPreviewViewModel — gesture-to-camera-move resolution, no view yet"
```

---

## Task 4: `ModelPreviewView`

**Files:**
- Create: `src/Nfty.App/Views/ModelPreviewView.axaml`
- Create: `src/Nfty.App/Views/ModelPreviewView.axaml.cs`
- Modify: `src/Nfty.App/Themes/Tokens.axaml` — new `Model3DBackgroundBrush` token, light + dark.
- Test: `tests/Nfty.App.Tests/ModelPreviewGestureTests.cs`

**Interfaces:**
- Consumes: `ModelPreviewViewModel` (Task 3); `Raster`/`RenderTarget`/`Camera` (Tasks 1-2); `Mesh`
  (existing); the app's existing `WriteableBitmap` construction pattern
  (`src/Nfty.App/Services/IImageBridge.cs` — `new WriteableBitmap(new PixelSize(w,h), new
  Vector(96,96), PixelFormat.Rgba8888, AlphaFormat.Unpremul)`, `wb.Lock()`, unsafe byte copy); the
  existing real-control gesture-test harness pattern in `tests/Nfty.App.Tests/TileClickGestureTests.cs`
  and `tests/Nfty.App.Tests/ChanceFieldGestureTests.cs` (read both before writing step 1 — they show
  how this repo drives a real Avalonia control with real pointer/key events under the headless test
  runner, including how focus and pointer capture are established in-test).
- Produces: a reusable control taking a `Mesh` and an `Image<Rgba32>` texture via two new properties
  (`Mesh? Mesh`, `Image<Rgba32>? Texture`, both settable — no host wires this control into a real page
  in this step; that is step 7). Nothing outside this task's own test references it yet.

- [ ] **Step 1: Write the failing gesture tests**

Follows `tests/Nfty.App.Tests/CanvasZoomTests.cs`'s exact harness — `[AvaloniaFact]`, a real `Window`,
`window.MouseDown`/`MouseMove`/`MouseUp`/`MouseWheel`/`KeyPressQwerty` (all `Avalonia.Headless`
extension methods on `Window`, already used throughout `tests/Nfty.App.Tests/`), and
`Dispatcher.UIThread.RunJobs()` to pump layout/input between steps — rather than
`TileClickGestureTests.cs`'s bubbled-`RoutedEventArgs` shortcut, because these gestures need real
pointer *coordinates* and *drag deltas*, which a bare `RoutedEventArgs` cannot carry.

```csharp
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.ViewModels;
using Nfty.App.Views;
using Nfty.Model;
using Nfty.Model.Render;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using Image = Avalonia.Controls.Image;

namespace Nfty.App.Tests;

/// <summary>Drives the real control with real pointer and key events — the pattern
/// <c>CanvasZoomTests</c> establishes: a test that calls <c>ModelPreviewViewModel.Orbit</c> directly
/// proves nothing about whether the drag reaches it.</summary>
public class ModelPreviewGestureTests
{
    private static Mesh TinyQuad()
    {
        float[] positions = [-1, -1, 0, 1, -1, 0, 1, 1, 0, -1, 1, 0];
        float[] normals = [0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1];
        float[] uvs = [0, 1, 1, 1, 1, 0, 0, 0];
        uint[] indices = [0, 1, 2, 0, 2, 3];
        return new Mesh(positions, normals, uvs, indices, textureWidth: 2, textureHeight: 2);
    }

    private static Image<Rgba32> TinyTexture()
    {
        var img = new Image<Rgba32>(2, 2);
        img.Mutate(ctx => ctx.BackgroundColor(new Rgba32(200, 100, 50, 255)));
        return img;
    }

    private static (Window Window, ModelPreviewViewModel Vm, ModelPreviewView View) Render()
    {
        var vm = new ModelPreviewViewModel(new Vector3(-1, -1, -1), new Vector3(1, 1, 1));
        var view = new ModelPreviewView { DataContext = vm, Mesh = TinyQuad(), Texture = TinyTexture() };
        var window = new Window { Content = view, Width = 400, Height = 400 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, view);
    }

    [AvaloniaFact]
    public void A_left_drag_orbits_the_camera()
    {
        var (window, vm, _) = Render();
        try
        {
            float startYaw = vm.CameraState.Yaw;
            window.MouseDown(new Point(200, 200), MouseButton.Left);
            window.MouseMove(new Point(260, 200));
            window.MouseUp(new Point(260, 200), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.NotEqual(startYaw, vm.CameraState.Yaw);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_right_drag_pans_the_camera_target()
    {
        var (window, vm, _) = Render();
        try
        {
            var startTarget = vm.CameraState.Target;
            window.MouseDown(new Point(200, 200), MouseButton.Right);
            window.MouseMove(new Point(230, 180));
            window.MouseUp(new Point(230, 180), MouseButton.Right);
            Dispatcher.UIThread.RunJobs();

            Assert.NotEqual(startTarget, vm.CameraState.Target);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void The_wheel_zooms_anchored_on_the_pointer()
    {
        var (window, vm, _) = Render();
        try
        {
            float startDistance = vm.CameraState.Distance;
            window.MouseWheel(new Point(260, 150), new Vector(0, 1));
            Dispatcher.UIThread.RunJobs();

            Assert.True(vm.CameraState.Distance < startDistance);
        }
        finally { window.Close(); }
    }

    /// <summary>Raises the routed event directly rather than simulating two real clicks within a
    /// timing threshold — the same shortcut <c>TileClickGestureTests</c> takes for
    /// <c>Button.ClickEvent</c>, and for the same reason: the code-behind handler
    /// (<c>DoubleTapped += (_, _) => vm?.Reset();</c>) only needs a <c>RoutedEventArgs</c> carrying
    /// the right <see cref="RoutedEvent"/>, not real input timing.</summary>
    [AvaloniaFact]
    public void Double_click_resets_the_camera()
    {
        var (window, vm, view) = Render();
        try
        {
            var original = vm.CameraState;
            window.MouseDown(new Point(200, 200), MouseButton.Left);
            window.MouseMove(new Point(300, 260));
            window.MouseUp(new Point(300, 260), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(original.Yaw, vm.CameraState.Yaw);

            view.RaiseEvent(new RoutedEventArgs(Gestures.DoubleTappedEvent) { Source = view });
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(original.Target, vm.CameraState.Target);
            Assert.Equal(original.Distance, vm.CameraState.Distance, 4);
            Assert.Equal(original.Yaw, vm.CameraState.Yaw, 4);
            Assert.Equal(original.Pitch, vm.CameraState.Pitch, 4);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Arrow_keys_orbit_when_the_view_has_focus()
    {
        var (window, vm, view) = Render();
        try
        {
            view.Focus();
            Dispatcher.UIThread.RunJobs();
            float startYaw = vm.CameraState.Yaw;

            window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.NotEqual(startYaw, vm.CameraState.Yaw);
        }
        finally { window.Close(); }
    }

    /// <summary>The rule every other gesture in this app keeps: <c>PointerCaptureLost</c> is treated
    /// as abort, so a drag that wanders off the control stops reporting rather than leaving a
    /// half-applied camera move.</summary>
    /// <remarks>
    /// <b>Implementer: verify the exact constructor Avalonia 12.1.1 exposes for
    /// <see cref="PointerCaptureLostEventArgs"/> (and, if it needs one, <c>Pointer</c>) before relying
    /// on the call below — check it against the installed package (go-to-definition, or the source
    /// under the NuGet cache) rather than trust this literally; the point of the test is the
    /// assertions, not this one construction call. If constructing the event directly proves awkward,
    /// an equally valid alternative is capturing the pointer to a second, unrelated control mid-drag
    /// (`e.Pointer.Capture(otherControl)`), which revokes the view's capture and raises
    /// `PointerCaptureLost` on it exactly as a real capture-steal would.</b>
    /// </remarks>
    [AvaloniaFact]
    public void Losing_pointer_capture_mid_drag_aborts_it_without_a_partial_move()
    {
        var (window, vm, view) = Render();
        try
        {
            window.MouseDown(new Point(200, 200), MouseButton.Left);
            window.MouseMove(new Point(220, 200));
            Dispatcher.UIThread.RunJobs();
            float yawAfterFirstMove = vm.CameraState.Yaw;

            var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
            view.RaiseEvent(new PointerCaptureLostEventArgs(view, pointer)); // verify this constructor — see remarks above
            Dispatcher.UIThread.RunJobs();

            window.MouseMove(new Point(280, 200));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(yawAfterFirstMove, vm.CameraState.Yaw);
        }
        finally { window.Close(); }
    }

    /// <summary>Otherwise every key binding above is raised on the Window instead and silently does
    /// nothing — the exact defect CLAUDE.md records the editor page hitting once already.</summary>
    [AvaloniaFact]
    public void The_view_takes_focus_on_attach()
    {
        var (window, _, view) = Render();
        try
        {
            Assert.True(view.IsFocused);
        }
        finally { window.Close(); }
    }

    /// <summary>Exact-pixel per house style: render the same Mesh/Camera through <c>Raster</c>
    /// directly into a <c>RenderTarget</c>, and assert the control's <c>WriteableBitmap</c>'s pixel
    /// bytes match — proving the view's paint path is the same pipeline <c>Raster</c> owns, not a
    /// second one.</summary>
    [AvaloniaFact]
    public void The_rendered_bitmap_reflects_the_current_mesh_and_camera()
    {
        var (window, vm, view) = Render();
        try
        {
            var surface = view.GetVisualDescendants().OfType<Image>().Single();
            var bitmap = (Avalonia.Media.Imaging.WriteableBitmap)surface.Source!;
            int width = (int)view.Bounds.Width, height = (int)view.Bounds.Height;

            var expected = new RenderTarget(width, height);
            expected.Clear(new Rgba32(0, 0, 0, 0));
            Raster.Render(view.Mesh!, view.Texture!, vm.CameraState.ViewProjection(width, height), expected);

            using var fb = bitmap.Lock();
            unsafe
            {
                var actual = new ReadOnlySpan<byte>((void*)fb.Address, expected.Color.Length * 4);
                var expectedBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes<Rgba32>(expected.Color);
                Assert.True(actual.SequenceEqual(expectedBytes));
            }
        }
        finally { window.Close(); }
    }
}
```

**On `Losing_pointer_capture_mid_drag_aborts_it_without_a_partial_move`'s construction line:** it is
written defensively (deliberately awkward) because this plan cannot verify Avalonia 12.1.1's exact
`PointerCaptureLostEventArgs` constructor signature from the spec alone — replace that one line with
whatever actually compiles against the installed package; the surrounding arrange/act/assert shape is
the part that must survive unchanged. Do not skip this test or
`The_view_takes_focus_on_attach` — CLAUDE.md records both as real defects this app already shipped
once, each caught only by a test in exactly this shape.

- [ ] **Step 2: Run, confirm every test fails**

- [ ] **Step 3: Add the background token**

In `src/Nfty.App/Themes/Tokens.axaml`, find the existing light-theme `ResourceDictionary` and the
dark-theme one (the file already has two, one per `ThemeVariant` — follow the exact structure already
there for an existing background/surface brush) and add, in each:

```xml
<SolidColorBrush x:Key="Model3DBackgroundBrush" Color="{StaticResource <pick the nearest existing panel/surface color token in that theme>}" />
```

Pick a color close to the app's neutral panel background in each theme dictionary, so the 3D
viewport reads as part of the page rather than a hole cut into it — check the rendered result later
in step 7 when a real page hosts this control; nothing here is final until then.

- [ ] **Step 4: Implement `ModelPreviewView`**

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="Nfty.App.Views.ModelPreviewView"
             Focusable="True" IsTabStop="True"
             Background="{DynamicResource Model3DBackgroundBrush}">
  <Image x:Name="PART_Surface"
         Stretch="Uniform"
         RenderOptions.BitmapInterpolationMode="None" />
</UserControl>
```

```csharp
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Nfty.App.ViewModels;
using Nfty.Model;
using Nfty.Model.Render;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
// No alias needed: Avalonia.Controls.Image (non-generic, used below for the control) and
// SixLabors.ImageSharp.Image<Rgba32> (generic, used for Texture) never collide — arity tells them
// apart. An alias here would be a footgun, not a convenience; don't add one.

namespace Nfty.App.Views;

/// <summary>The 3D preview surface: a <see cref="WriteableBitmap"/> painted by <see cref="Raster"/>,
/// driven by pointer and keyboard gestures resolved through <see cref="ModelPreviewViewModel"/>. This
/// control knows nothing about where its <see cref="Mesh"/>/<see cref="Texture"/> come from — no page
/// hosts it yet (that is step 7).</summary>
public sealed partial class ModelPreviewView : UserControl
{
    public static readonly StyledProperty<Mesh?> MeshProperty =
        AvaloniaProperty.Register<ModelPreviewView, Mesh?>(nameof(Mesh));

    public static readonly StyledProperty<Image<Rgba32>?> TextureProperty =
        AvaloniaProperty.Register<ModelPreviewView, Image<Rgba32>?>(nameof(Texture));

    public Mesh? Mesh
    {
        get => GetValue(MeshProperty);
        set => SetValue(MeshProperty, value);
    }

    public Image<Rgba32>? Texture
    {
        get => GetValue(TextureProperty);
        set => SetValue(TextureProperty, value);
    }

    private readonly Image _surface;
    private Point? _dragOrigin;
    private bool _dragIsPan;

    public ModelPreviewView()
    {
        AvaloniaXamlLoader.Load(this); // the InitializeComponent() boilerplate every view in this app uses (see e.g. BusyView.axaml.cs)
        _surface = this.FindControl<Image>("PART_Surface")!;

        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += OnPointerCaptureLost;
        PointerWheelChanged += OnPointerWheelChanged;
        DoubleTapped += (_, _) => (DataContext as ModelPreviewViewModel)?.Reset();
        KeyDown += OnKeyDown;

        this.GetObservable(MeshProperty).Subscribe(_ => Repaint());
        this.GetObservable(TextureProperty).Subscribe(_ => Repaint());
        this.GetObservable(DataContextProperty).Subscribe(dc =>
        {
            if (dc is ModelPreviewViewModel vm)
                vm.PropertyChanged += (_, _) => Repaint();
            Repaint();
        });
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Focus();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed && !point.Properties.IsRightButtonPressed
            && !point.Properties.IsMiddleButtonPressed)
            return;

        _dragOrigin = point.Position;
        _dragIsPan = point.Properties.IsRightButtonPressed || point.Properties.IsMiddleButtonPressed;
        e.Pointer.Capture(this);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragOrigin is not { } origin || DataContext is not ModelPreviewViewModel vm) return;
        var current = e.GetCurrentPoint(this).Position;
        var delta = new Vector2((float)(current.X - origin.X), (float)(current.Y - origin.Y));
        _dragOrigin = current;

        if (_dragIsPan)
            vm.Pan(delta, (int)Math.Max(Bounds.Height, 1));
        else
            vm.Orbit(delta);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragOrigin = null;
        e.Pointer.Capture(null);
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => _dragOrigin = null;

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not ModelPreviewViewModel vm) return;
        var point = e.GetCurrentPoint(this).Position;
        vm.Zoom((float)e.Delta.Y, new Vector2((float)point.X, (float)point.Y),
            (int)Math.Max(Bounds.Width, 1), (int)Math.Max(Bounds.Height, 1));
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ModelPreviewViewModel vm) return;
        switch (e.Key)
        {
            case Key.Left: vm.OrbitStep(new Vector2(-1, 0)); e.Handled = true; break;
            case Key.Right: vm.OrbitStep(new Vector2(1, 0)); e.Handled = true; break;
            case Key.Up: vm.OrbitStep(new Vector2(0, -1)); e.Handled = true; break;
            case Key.Down: vm.OrbitStep(new Vector2(0, 1)); e.Handled = true; break;
            case Key.OemPlus or Key.Add: vm.ZoomStep(1f); e.Handled = true; break;
            case Key.OemMinus or Key.Subtract: vm.ZoomStep(-1f); e.Handled = true; break;
            case Key.Home: vm.Reset(); e.Handled = true; break;
        }
    }

    private void Repaint()
    {
        if (Mesh is not { } mesh || Texture is not { } texture || DataContext is not ModelPreviewViewModel vm)
            return;

        int width = (int)Math.Max(Bounds.Width, 1);
        int height = (int)Math.Max(Bounds.Height, 1);
        if (width <= 0 || height <= 0) return;

        var target = new RenderTarget(width, height);
        target.Clear(new Rgba32(0, 0, 0, 0)); // the WriteableBitmap composites over the control's own themed Background
        Raster.Render(mesh, texture, vm.CameraState.ViewProjection(width, height), target);

        var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96),
            PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using (var fb = bitmap.Lock())
        {
            unsafe
            {
                var span = new Span<byte>((void*)fb.Address, width * height * 4);
                System.Runtime.InteropServices.MemoryMarshal.AsBytes<Rgba32>(target.Color).CopyTo(span);
            }
        }
        _surface.Source = bitmap;
    }
}
```

**Note on `Repaint`'s trigger:** this redraws on every `CameraState` change, every `Mesh`/`Texture`
change, and every drag pointer-move (since `Orbit`/`Pan`/`Zoom` each reassign `CameraState`, which
raises the `PropertyChanged` this view subscribes to) — no throttling in this step. The spec's "3D
repaint capped at 30/s" budget belongs to the Rise page's depth-editing loop (step 7); this control in
isolation repaints once per gesture delta, which is the correct behavior for a drag that must track
the pointer exactly. Revisit only if `RasterPerfTests`' numbers (Task 2) say a real page's drag feels
slow — don't add a budget nothing has shown is needed yet (YAGNI).

- [ ] **Step 5: Run the gesture tests, confirm they pass**

Run: `dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~ModelPreviewGestureTests`
Expected: PASS, all 8.

- [ ] **Step 6: Run the full suite**

Run: `dotnet test nfty.sln`
Expected: PASS, no regressions, 0 build warnings (`TreatWarningsAsErrors`).

- [ ] **Step 7: Commit**

```bash
git add src/Nfty.App/Views/ModelPreviewView.axaml src/Nfty.App/Views/ModelPreviewView.axaml.cs src/Nfty.App/Themes/Tokens.axaml tests/Nfty.App.Tests/ModelPreviewGestureTests.cs
git commit -m "feat(app): ModelPreviewView — WriteableBitmap surface, orbit/pan/zoom gestures, keyboard path"
```

---

## Notes for the controller (not a task)

- **Nothing hosts this control yet.** `VisualCapture`, `DarkModeContrastTests` and
  `PixelPerfectRenderingTests` walk real pages/screens; `ModelPreviewView` joins those sweeps for free
  once step 7's Rise page embeds it, not before. Don't add it to any screen list in this plan — that
  would be scope step 7 owns.
- **Projection is a ruling, not a spec answer.** Neither `2026-09-25-rise-design.md` nor
  `2026-09-22-model-preview-design.md` states perspective vs. orthographic. Ruling: perspective,
  40° vertical FOV — the spec explicitly aligns the camera's *feel* with `<model-viewer>`'s default
  ("this is also what `<model-viewer>` does by default, so the feel will be familiar"), and
  `<model-viewer>` is a perspective camera. Cost if wrong: NOT a one-line swap, as this note first
  claimed — the final review found `Pan` and `ZoomAnchored` restate the perspective world-per-pixel
  formula, and `Zoom` (moving the eye) would stop magnifying under an orthographic matrix. Switching
  means reworking those three as well.
- **Final review: dispatch on Opus 5.5**, per the user's explicit instruction this segment — every
  step's final review, and especially this one, must not be the place an issue slips through before
  merge.
