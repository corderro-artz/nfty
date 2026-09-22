# In-app 3D model preview — design

**Date:** 2026-09-22
**Project:** 4. See `2026-09-22-runtime-and-model-overview.md`.
**Depends on:** `Nfty.Model` tasks 2–6 (mask, rect merge, mesh, mesh builder, texture bake). **Not** on
the GLB writer.

## What this is for

The app should let you turn an asset over in your hands. A 2D preview tells you what was drawn; a
3D preview tells you what it will *be* — how thick the extrusion reads, whether the silhouette holds
up from an angle, whether `Symmetric` was the right mode. This is the first 3D surface in the
product and the thing that makes it feel native to both kinds of work.

## Three corrections to the premise, taken before designing

### 1. There is nothing to port from `mixel`

`Mixel.Web/Components/ModelPreview.razor` is eighteen lines wrapping Google's `<model-viewer>`, a
~1 MB three.js WebGL web component loaded from `lib/model-viewer.min.js`. The orbit, the zoom, the
context menu and the camera reset are all that component's. **nfty writes this from scratch.**

What IS worth taking from `mixel` is its stated pattern, which nfty already believes in: *"All
decision logic lives in a plain C# state class (no JS dependency, fully unit testable). The component
is a thin view."* That is `DepthPainterState ↔ DepthPainter.razor` there, and it is the ViewModel
discipline here.

### 2. It cannot be the first thing built

Nothing produces a mesh yet. It is the first **UI** component, after `Nfty.Model` has geometry.

### 3. It renders a `Mesh`, never a `.glb`

The preview takes `Nfty.Model.Mesh` and the baked texture directly, and re-extrudes from the art when
the options change. Parsing GLB back would be a **second definition of the format** — the exact
duplication `VariantPreview` exists to prevent — to obtain something the app can rebuild in a
millisecond.

## Decision: a software rasterizer, and why that is not a compromise

Avalonia has `OpenGlControlBase`. It is the wrong answer here, for one reason that outweighs the
rest:

**`Avalonia.Headless` has no GL context.** `VisualCapture` would photograph an empty rectangle.
`DarkModeContrastTests` would score nothing. `PixelPerfectRenderingTests` would have nothing to
read. CLAUDE.md's first GUI rule is *"Verify visuals from a rendered frame, never from the
markup"* — and a GL control is the single thing in this app that rule could not reach. It would make
the most visually complex surface in the product the only one no test can see.

A software rasterizer writing into a `WriteableBitmap`:

- **renders identically headless and on screen**, because it is pure CPU pixel arithmetic — so every
  existing visual sweep covers it for free;
- is **exact-pixel testable**, which is this repo's house style for imaging and the only style it
  has (there are no golden images anywhere);
- adds **no package**, keeping the rule the rest of this work is built on;
- and needs no GPU context, driver or platform branch.

**The workload suits it.** Extruded pixel art: a few hundred triangles, a texture measured in tens of
pixels a side, nearest-neighbour sampling, no PBR, no shadows, no transparency sorting. A 512×512
preview at 3× overdraw is about 800,000 textured pixel operations a frame — and it only has to
redraw while the pointer is moving.

This is the same judgment `NumberStamp` already made when it drew a 3×5 bitmap font rather than take
a font library: the general-purpose answer is much larger than the problem.

## Architecture

```
Nfty.Model/Render/                    (Core-side, no UI dependency, fully testable)
  Camera.cs        turntable orbit; ONE function owns view+projection
  Raster.cs        transform, cull, z-buffer, textured triangle fill
  RenderTarget.cs  a plain Rgba32 surface the rasterizer writes into

Nfty.App/
  ViewModels/ModelPreviewViewModel.cs   camera state, gestures resolved to camera moves
  Views/ModelPreviewView.axaml(.cs)     a WriteableBitmap, pointer capture, key bindings
```

**The rasterizer lives in `Nfty.Model`, not in the app.** It is arithmetic, it is testable without a
window, and a second front-end must be able to produce the identical image — the same argument that
put `VariantPreview` in Core rather than in the CLI.

## The camera

**One function owns the mapping.** `Camera.ViewProjection(size)` returns the matrix; the pointer
maths, the gizmo and any picking derive from it or its inverse. This is `IngredientEditorView`'s
`Geometry` rule restated for three dimensions, and it is there for the same reason: the 2D editor's
zoom, pan, marquee and backdrop lattice all agree because they are all one function's output.

**Turntable orbit, not a trackball.** Yaw is free; **pitch clamps to ±89°**. An extruded sprite has a
definite up, and a free trackball rolls it onto its side into an orientation that means nothing. This
is also what `<model-viewer>` does by default, so the feel will be familiar.

**No auto-rotate.** `mixel` spins the model after 1.5 s idle. In an authoring tool that is motion
under the eye of someone comparing two things, and it would make every captured frame
non-deterministic.

**Zoom is anchored on the pointer** — the difference between a magnifier and a control you fight.
Derived from the inverse of the same function, never written beside it, exactly as the 2D canvas
does.

## Gestures

| Input | Action |
|-------|--------|
| Left drag | Orbit |
| Right drag, or middle drag | Pan |
| Wheel | Zoom, anchored on the pointer |
| Double-click | Reset camera |
| `Left`/`Right`/`Up`/`Down` | Orbit by a step |
| `+`/`-` | Zoom by a step |
| `Home` | Reset camera |

**Every drag captures the pointer** (`e.Pointer.Capture`), and `PointerCaptureLost` aborts —
the rule the 2D canvas already keeps, because a drag that wanders off the control otherwise stops
reporting and never releases.

**A drag always ships with a keyboard path.** That is stated in CLAUDE.md about layer reordering and
it is a general rule here: pointer-only is an incomplete feature, not a scoped-down one. The keys
above are the path.

**The view takes focus on attach** (`Focusable`, `IsTabStop=False`), or the key bindings are silently
inert — the defect the editor page already hit, where every chord the screen owned was raised on the
Window instead.

## Rendering

1. Transform vertices by view-projection; divide by w; map to the target.
2. **Back-face cull** by winding. An extrusion is a closed manifold, so exactly half the triangles
   are invisible at any angle — this is free and it halves the fill.
3. **Z-buffer**, a `float[]` the size of the target. A painter's algorithm cannot resolve a closed
   solid against itself without sorting per-triangle, and sorting is both slower and wrong at
   intersections.
4. **Nearest-neighbour texture sampling.** Stated, not defaulted: nothing in nfty resamples, blurs or
   anti-aliases, and a filtered 8×8 texture blown up to 300 px is the one thing that would make this
   product's art look like someone else's.
5. **Flat per-face shading, quantized to a few discrete levels.** Not Gouraud, not smooth. A smooth
   gradient puts continuous tone on art with none in it — the objection `NumberStamp` already
   records about anti-aliased grey ink. Face normals are already per-face in the mesh, so flat is
   also the cheap answer.
6. **No anti-aliasing on the silhouette edges** in phase 1. It would be the only soft edge in the
   app. If the jaggies read badly at small sizes, the answer is supersampling the whole target and
   downsampling with a box filter — which is still whole-pixel arithmetic — not an edge filter.

The background is a **token**, defined in both theme dictionaries, and the control honours the app
theme like everything else.

## Hosts

The control takes a `Mesh` and a texture and knows nothing about where it came from. That is what
makes it reusable across three places that are otherwise unrelated:

| Host | What it shows |
|------|---------------|
| Ingredient pane | one variant, extruded |
| Set browser inspector | the selected asset, extruded |
| `ModelDialog` | a live preview of the options being set |

## A defect in the defaults, found while designing this

`ExtrudeOptions` defaults to `Depth = 1`, `VoxelSize = 1f` — inherited from `mixel`, where it is also
the default. On a 64×64 sprite that is a **64 × 64 × 1 slab: a sheet of paper.** The first thing a
user will ever see in this preview is a flat plane, and the natural conclusion is that the feature is
broken rather than that the default is thin.

`mixel` gets away with it because its UI puts a depth slider in front of you immediately. nfty should
not inherit the number without inheriting that.

**Two candidate fixes, both cheap; pick one when the dialog is designed:**
- default `Depth` to a fraction of the art's smaller side (say ⅛, so 64 px art extrudes 8 deep), or
- keep `Depth = 1` in `ExtrudeOptions` — it is the honest unit — and have the **dialog** open at a
  proportional value, showing the ratio.

The second is better: a library default of "one voxel" is correct and predictable, and choosing a
flattering starting point is a presentation decision. Recorded here so it is decided rather than
inherited.

## What the preview will honestly reveal

Worth saying plainly because it will look like a bug and is not: a Z-extruded sprite **is** flat-
backed. From behind it is the mirror image; from the side it is a slab of uniform thickness. That is
what extrusion means, and seeing it is precisely what should send an author to `Symmetric`, to
`Raised`, or eventually to per-layer depth. The preview is doing its job when it shows this.

## Testing

Everything below is exact-pixel or exact-value, built in memory, no golden files.

- **Projection.** A unit cube at a known camera lands its eight corners at known target coordinates.
- **Back-face culling halves the work.** A closed cube draws exactly half its triangles from any
  angle; assert the drawn-triangle count, not the pixels.
- **The z-buffer resolves the solid.** Render a cube whose front and back faces carry different
  texels; assert every visible pixel came from the front face.
- **Nearest-neighbour sampling.** An 8×8 checker texture rendered to a 256 px face produces exactly
  two colours and no third.
- **Flat shading is flat.** Every pixel of one face is one colour.
- **The camera clamps.** Orbiting past ±89° pitch stops rather than flipping.
- **Zoom is pointer-anchored.** The canvas point under the pointer is unchanged by a zoom step, to
  within a pixel.
- **The control joins the existing sweeps for free**, which is the whole argument for the software
  path: `VisualCapture` photographs it, `DarkModeContrastTests` walks it, `PixelPerfectRenderingTests`
  reads its effective interpolation mode.
- **A gesture test, not a command test.** `TileClickGestureTests` and `ChanceFieldGestureTests` are
  the pattern: drive the real control with real pointer and key events and assert the camera moved —
  a test that calls `Camera.Orbit` directly proves nothing about whether the drag reaches it.

## Out of scope

Lighting models, shadows, materials beyond the baked texture, animation, multiple meshes in one
scene, scene export, and reading `.glb` back.
