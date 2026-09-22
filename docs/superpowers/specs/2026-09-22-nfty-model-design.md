# `Nfty.Model` — 3D model export — design

**Date:** 2026-09-22
**Projects:** 2 and 3 of 3. See `2026-09-22-runtime-and-model-overview.md`.

## What this is for

nfty should hand a person a **3D model** of an asset as readily as it hands them a PNG. A pixel-art
sprite extrudes into a solid along Z; that solid is a glTF 2.0 file, which Blender, Windows 3D
Viewer, VS Code and three.js all open without help. That is what *"renderable in any viewer"* means
here, and it is why glTF is the format rather than a voxel container.

`Stats/VoxelReport.cs` already exists and already answers the one question this feature cannot answer
for itself — which variants carry partial alpha, *"the one thing a voxel converter cannot resolve"*.
It was written in anticipation of exactly this. It becomes this feature's pre-flight.

## Source material: `M:\Repositories\mixel`

`mixel` (MIT, same author) converts transparency-supporting PNGs into Z-extruded glTF 2.0 via
SharpGLTF. `Mixel.Core` is 957 lines across 18 files.

**It is reference material, not a dependency and not a transplant.** No file is copied. What is taken
is the *geometry*, which is the part that is genuinely hard and genuinely correct; everything around
it is either duplicated by something nfty already owns or is defective. The audit below is the
gate — each item is a decision, not a note.

### What is taken (~300 lines, rewritten)

| From mixel | Why it is worth taking |
|-----------|------------------------|
| `GreedyRects` | Maximal-rectangle merge of the solid mask. This is what makes a 64×64 sprite a few dozen quads instead of four thousand. Correct as written; taken with tests. |
| front/back face emission, `EmitWall` / `EmitWallQuad` | The overhang logic — a wall spans only the exposed part between a pixel's depth and its neighbour's — is the non-obvious core. Taken. |
| `ApplyPivot` | Small and right. X and Y minima are already at the origin under this coordinate convention, so only Z needs shifting; verified by reading, to be pinned by a test rather than trusted. |
| `Pivot`, `ExtrudeMode` semantics | Front / Symmetric / Raised, BottomCenter / Center / MinCorner. Good vocabulary, kept. |

Nothing is taken from `GltfWriter.cs`. See "Writing the GLB" below — nfty writes the container
itself and takes no glTF library.

### What is NOT taken, and why

| Dropped | Reason |
|---------|--------|
| `PngLoader`, `RgbaImage`, `Rgba`, `TextureBaker`'s encoder | nfty has ImageSharp. Two image libraries in one product is two answers to every question about a pixel. `StbImageSharp` and `StbImageWriteSharp` are dropped entirely. |
| `ImageSize` | It refuses any dimension that is not a power of two from 8 to 1024. nfty's canvas is whatever the CookBook declares, and `Validator` already checks every variant against it. Porting this would reject books nfty itself considers valid — a second, contradictory size rule. |
| `InputExpander`, `BatchRunner` | CLI plumbing. nfty has `System.CommandLine` wiring and `ParallelWork`. |
| `SpritesheetSlicer`, `SpriteRect` | nfty has `Publish/SpriteSheet.cs`. |
| `MixelFile` | nfty writes into `SetLayout`'s own directories. |
| `GltfWriter`, and **SharpGLTF itself** | nfty writes the GLB container directly. See below. |
| `GltfFormat.Gltf` / `GltfEmbedded` | `.glb` only. **Defective — see D2**, and unnecessary: a GLB is already one self-contained file. |
| `DepthMap`, `BuildFromDepthMap` | Not defective, just not phase 1. See "Deferred" below. |

## Writing the GLB — no glTF library

`mixel` uses `SharpGLTF.Toolkit`. **nfty takes no such package.** This product's dependency list is
short on purpose — ImageSharp, System.CommandLine, Avalonia — and a glTF library is not one of the
places where a package earns its place.

A GLB is a thin binary container around a JSON document:

```
12-byte header    magic 'glTF' | version 2 | total length          (uint32, little-endian)
chunk 0           length | 'JSON' | the glTF JSON, padded with 0x20 to a 4-byte boundary
chunk 1           length | 'BIN\0' | vertex data + the PNG, padded with 0x00
```

The JSON for what nfty emits is **one fixed shape** — one scene, one node, one mesh, one primitive,
one material, one texture, one sampler, one image, four accessors over five bufferViews. It is not a
general glTF exporter and does not need to be. `System.Text.Json` writes it (in-box, and already how
every manifest in this repo is written) and `BinaryPrimitives` frames it.

**Writing it ourselves settles two things a library left open**, which is most of the argument:

- **Determinism, by construction.** nfty freezes fixtures. A third-party writer that stamps a
  generator version, a timestamp or non-deterministic buffer padding would make the same asset
  produce different bytes on two runs, and no freeze test would be possible. Owning the writer means
  the same mesh is the same file, always — which is the same reason `SeedHash` reads little-endian
  and every sort that reaches a file is ordinal.
- **Vertex sharing is a decision we make**, rather than a behaviour of someone else's mesh builder
  that has to be measured and hoped for.

It also removes SharpGLTF 1.0.6's own API traps — the `WithSampler` parameter order and the obsolete
`GetChannel(string)` overload both cost `mixel` a debugging session, and neither exists if the
sampler is four integers in a JSON object.

**The spec-compliance points that must be got right**, each pinned by a test:

- `POSITION`'s accessor **must** carry `min` and `max`; the glTF 2.0 spec requires it and a viewer is
  entitled to reject a file without them.
- Every accessor's data must start on a boundary of its component size, and every chunk must be
  padded to four bytes — JSON with spaces, BIN with zeros.
- Indices are `UNSIGNED_INT` (5125) unconditionally. Universally supported, and choosing per-mesh
  between short and int is an optimisation with a correctness cliff for no meaningful gain here.
- The PNG goes in a bufferView with **no** `target`, referenced by `images[0].bufferView` with
  `mimeType: "image/png"`.
- The sampler is `NEAREST`/`NEAREST` (9728) with `CLAMP_TO_EDGE` (33071) on both axes, so pixel art
  stays crisp — the same rule the app keeps with `BitmapInterpolationMode="None"`, and the whole
  reason this product resamples nothing anywhere.
- `alphaMode: "MASK"` with `alphaCutoff: 0.5`. The silhouette is already cut at the alpha threshold,
  so `BLEND` would only invite a viewer to sort transparent fragments that do not exist.

## Defect audit — findings that must be fixed on the way in

Each of these is present in `mixel` today. None may be reproduced.

### D1 — Two different answers to "is this pixel there?" *(correctness)*

`SilhouetteMask.Build` treats a pixel as solid at `A == 255`. `Extruder.Build`'s per-pixel branch
builds its mask with `A > 0` instead, with a comment explaining it is done to avoid
`EmptySilhouetteException`. Same library, same question, two thresholds — so the *same sprite
extrudes to a different silhouette* depending on whether a depth map was supplied.

**nfty's rule:** one function answers it, both paths call it, and the threshold is a stated option
defaulting to fully-opaque (`A == 255`). `VoxelReport` runs first and reports what will be dropped.
Partial alpha is **legal** in nfty — the editor's opacity lock is a default an author may turn off —
so this warns and proceeds; it never refuses.

### D2 — `GltfEmbedded` string-replaces JSON and fails silently *(correctness, silent)*

`GltfWriter.WriteToMemory` inlines the satellite `.bin` by running

```csharp
json = json.Replace($"\"uri\":\"{key}\"", $"\"uri\":\"{dataUri}\"");
```

over the serialized glTF. That depends on SharpGLTF emitting exactly `"uri":"buffer.bin"` with no
space after the colon. If the writer ever indents or spaces its output — a serializer setting, and
version-dependent — the replace matches nothing, no exception is raised, and the result is a `.gltf`
that references a `.bin` file which was never written. A broken model that reports success.

**nfty's rule:** `.glb` only. It is a single self-contained file and it is the flavour that "just
worked", so the problem does not arise. Writing the container ourselves makes it moot twice over —
there is no satellite `.bin` to inline, because a GLB's binary chunk *is* the buffer.

The lesson generalises past this one method and is why it is listed even though the code is not being
ported: **a transformation that can silently match nothing must not be the mechanism.** If `.gltf`
text output is ever wanted, it is built from the same JSON object model that writes the GLB chunk,
never by string replacement over serialized output.

### D3 — `WithDoubleSide(true)` on a closed solid *(quality)*

An extrusion is a closed manifold: every face has a correct outward normal and no back face is ever
visible. Double-sided disables backface culling for no benefit and doubles fragment work wherever
there is overdraw. It is presumably there so a degenerate zero-depth case does not vanish.

**nfty's rule:** single-sided by default, with a test that renders/inspects the emitted material. If
a degenerate depth is ever allowed, it is that case that opts into double-sided, not every model.

### D4 — The material and mesh are named `"mixel"` *(correctness of output)*

Baked into every exported file. Renamed, obviously — but worth listing, because it is the kind of
thing a port carries silently into a shipped artifact.

### D5 — A full-transparent image throws and kills the batch *(behaviour)*

`EmptySilhouetteException` is right for a single conversion and wrong for a Set. A rolled asset can
legitimately be fully transparent — every layer of a recipe may be optional.

**nfty's rule:** the same one `SpriteSheet` already keeps about a missing asset. **One asset that
cannot be extruded is skipped and named in the result; it does not fail the Set.** A hole is
information; a failed batch after nine minutes is not.

### D6 — `Extruder` round-trips through PNG bytes *(efficiency)*

`ExtrudeOptions.PngBytes` forces `PngLoader.Load` on every call. At cook time nfty already holds an
`Image<Rgba32>`; encoding it to PNG and decoding it again per asset is pure waste on the hot path.

**nfty's rule:** the API takes `Image<Rgba32>`. There is no byte-array entry point.

### D7 — `DepthMap.MaxLevel` lazily caches into a nullable field *(latent, under parallelism)*

`_maxLevel ??= …` is a benign race today because mixel is single-threaded there. nfty runs asset work
through `ParallelWork`. Computed eagerly instead.

### Measure, do not assume

Owning the writer settles determinism and vertex sharing by construction. Two questions remain
genuinely open and are answered with `Diagnostics/Perf.cs`, which exists for exactly this:

- **Vertex dedup is now a choice.** Every quad emits four fresh vertices and nothing shares. Phase 1
  ships it that way — it is correct, and a duplicated vertex is a size cost rather than a defect.
  Measure a real 64×64 asset's GLB before adding a dedup pass; a hash-keyed merge is easy to add and
  easy to get subtly wrong (normals and UVs differ per face, so vertices that share a *position* must
  not be merged).
- **`GreedyRects` cost.** The row-growth check rescans the strip per row. Fine for one sprite; a
  1000px canvas over 10,000 assets is a different question. Model export is opt-in and runs through
  `ParallelWork` for this reason.

---

## Project 2 — `Nfty.Model`

```
src/Nfty.Model/   NEW. net10.0.
                  PackageReference: SixLabors.ImageSharp  (already in Directory.Packages.props)
                  ProjectReference: Nfty.Core
tests/Nfty.Model.Tests/   NEW.
```

**`Directory.Packages.props` gains nothing.** The only package `Nfty.Model` references is the
ImageSharp this product already pins, and it is referenced for the type it already uses everywhere —
`Image<Rgba32>`. The whole feature adds zero new third-party code to the graph.

**`Nfty.Core` does NOT reference `Nfty.Model`.** The dependency runs one way only, so a consumer that
wants generation alone never pulls a glTF library into its graph. This is what makes the feature
optional in the sense that matters.

### Surface

```csharp
namespace Nfty.Model;

enum ExtrudeMode { Front, Symmetric, Raised }
enum Pivot { BottomCenter, Center, MinCorner }

sealed record ExtrudeOptions
{
    int    Depth      { get; init; } = 1;      // in voxels
    float  VoxelSize  { get; init; } = 1f;     // world units per pixel
    Pivot  Pivot      { get; init; } = Pivot.BottomCenter;
    ExtrudeMode Mode  { get; init; } = ExtrudeMode.Front;
    byte   SolidAlpha { get; init; } = 255;    // D1: ONE threshold, stated
}

// The only entry point. Takes a live image; never bytes. (D6)
byte[] Extruder.ExtrudeGlb(Image<Rgba32> art, ExtrudeOptions options);

// Null when the art has no solid pixel at all, rather than throwing. (D5)
byte[]? Extruder.TryExtrudeGlb(Image<Rgba32> art, ExtrudeOptions options);
```

Every member `init` with a default, so a new axis is additive at every existing call site — the rule
`ExportOptions` already follows.

### Testing

Mirrors the house style: exact-pixel/exact-value assertions on tiny synthetic inputs, built in
memory, no golden files.

- **Geometry, from ASCII fixtures.** A 3×3 plus-shape, an L, a single pixel, a full square, a shape
  with a hole, a checkerboard (the worst case for greedy merging). Assert triangle count, bounding
  box, and that the mesh is closed — every edge shared by exactly two triangles. A closed-manifold
  assertion is the one check that catches a whole class of wall-emission bug at once.
- **`GreedyRects` directly.** A full rectangle merges to one rect; a checkerboard merges to none;
  rects tile the mask exactly once with no overlap and no gap.
- **Pivot.** Each of the three lands the mesh's bounding box where its name says.
- **Alpha threshold (D1).** A sprite with an anti-aliased edge produces the *same* silhouette
  whichever path builds the mask.
- **Empty art (D5).** `TryExtrudeGlb` returns null; `ExtrudeGlb` throws with a message naming the
  problem.
- **Material (D3, D4).** Single-sided; named for nfty; NEAREST/NEAREST sampler and CLAMP_TO_EDGE, so
  pixel art stays crisp — the same rule the app's `BitmapInterpolationMode="None"` keeps.
- **Determinism.** The same art and the same options produce **byte-identical** GLB bytes, twice in
  one process and across two. This is assertable only because nfty owns the writer, and it is what
  lets a model fixture be frozen at all.
- **`GlbStructureTests` — a validator, not a round-trip.** With no glTF library there is nothing to
  re-open the file with, so the test parses the container itself and asserts what the spec requires:
  magic, version and total length agree; both chunks are present, correctly typed and padded to four
  bytes; every `bufferView` lies inside the BIN chunk; every accessor lies inside its bufferView and
  starts on a component-size boundary; `POSITION` carries `min` and `max` and they match the actual
  vertex extents; every index is less than the vertex count; and `images[0]` points at bytes whose
  first eight are the PNG signature.

  This is the piece of the plan most likely to be under-built. A test that merely checks the file
  starts with `glTF` proves nothing — the assertion list above is the deliverable, and it is what
  replaces the safety a library would have provided. It is also worth opening one emitted file in a
  real viewer once, by hand, and recording in the test file that this was done and when.

### CLI

```bash
nfty model <input> --out <x.glb> [--depth N] [--voxel-size F]
                                 [--mode front|symmetric|raised]
                                 [--pivot bottom-center|center|min-corner]
```

`<input>` is a `.png`, or an `.igt` with `--variant` (extruding one variant's art the way `preview`
renders one), dispatched by `Archives.KindOf` — and options belonging to the other form are
**rejected, never ignored**, as `preview` already does.

---

## Project 3 — models in a Set, and the UI

Depends on project 2.

### A Set may carry models

`SetLayout` gains a fourth directory, `models`, holding `NNNN.glb` — **the same four-pad numbering as
`metadata/`**, because `SetReader` orders by the parsed number and not by filename, and a second
padding convention inside one Set is exactly the bug that ordering rule exists to prevent.

`SetLayout.Directories` is an **include list**, so adding `models` to it is what makes `SetWriter.Pack`
carry them; nothing else needs to know. `SetReader` must tolerate its absence — every Set written
before this exists has none.

`set.json` gains an optional `models` block recording the format and the extrude options used.
**Additive and optional, so `Schema.Current` is NOT bumped** — the rule `TargetSupply` already
established: `System.Text.Json` ignores unknown properties, so an older build still reads the file,
whereas a bump would make it refuse one.

Cooking with models is **opt-in** on `GenerateOptions`, runs through `ParallelWork` (one asset's model
is a pure function of that asset — it qualifies), and reports progress as its own phase.

### The number stamp must never reach a model

**This is the trap worth naming loudly.** `NumberStamp` draws the asset's set number into a corner.
It is export-only and destructive, so `SetExporter` renders a copy and stamps that. A model is
extruded from the asset's *art*, and the stamp is not part of the asset — it is a debug mark added on
the way out of the door.

**Rule: model extrusion always reads the unstamped asset.** At cook time this is true by
construction, because stamping does not happen at cook time. The place it can go wrong is an export
with the debug mark armed, and that is where the test goes: export with stamping on, then assert the
model's baked texture is byte-identical to the unstamped asset's cropped region. The stamp is white
over a one-pixel black halo, so a stamped texture is trivially distinguishable from a clean one.

### Single-asset model download

The Set browser's inspector already has a **Save image** action. It gains **Save model…** beside it,
which is the same gesture for the same asset in the other dimension.

It is gated by the **same seal flag** as `SaveImage` and the inspector's own Save. Saving one asset's
model *is* an export; a seal that closed the Export button while leaving this working would be a
label rather than a rule — which is the argument already recorded for the other two.

### UI checklist — tracked, NOT built until the engine lands

| Where | What |
|-------|------|
| Set browser inspector, beside **Save image** | `Save model…` |
| new modal `ModelDialog` (its own menu, per the ask) | depth; voxel size; mode tray (Front / Symmetric / Raised); pivot picker; a live triangle/size estimate; a `VoxelReport` line naming how many pixels will be dropped to partial alpha |
| Cook dialog | `Include 3D models` checkbox + the same options, collapsed; a warning that it multiplies cook time, with the measured figure |
| Export dialog, **CONTENTS** tab | `models/` as a fifth content axis beside images and the two metadata sets |
| Export dialog, **IMAGES** tab | a note that the debug mark applies to `images/` only and never to `models/` |
| CookBook / Ingredient pane | `Export preview…` gains a model option, for one variant |
| `assets/icons/` + `Icons.axaml` | one new glyph (a cube/solid). It must differ from its **neighbours**, and this one is at real risk — a plain cube sits beside `kitchen`'s archive box. Draw it as a solid with a visible depth face, not an outline box. |
| status bar | model-written message |
| edit lock | model export is a READ → **not** gated. The seal flag **is**. |

The forms, fields, wording and layout get designed against a rendered frame once the engine works —
this list exists so nothing is discovered late, not so it can be built now.

## Deferred, with the reason

**Per-layer depth ("Layered" mode).** Extruding the *composited* asset gives one flat slab. Extruding
each layer at its own Z gives a chest whose bands actually stand off its lid.

The mapping is already there and is almost eerie: `Editing/LayerDepth.cs` defines depth as *"the
1-based position, dense, bottom-first — depth 1 paints first and sits furthest back"*. The 2D paint
order **is** a Z order. A layer at depth *d* becomes the slab at *d*.

It is deferred because flat extrusion is the parity target, it is what `mixel` does, and it is the
thing that must work first. Recorded here so the phase-1 API does not foreclose it: `ExtrudeOptions`
is `init`-only with defaults, and the entry point takes an image, so a layered entry point is an
addition rather than a change.

**`.gltf` output.** See D2. `.glb` first; text glTF only with a real JSON writer behind it.

**Depth maps.** `mixel`'s per-pixel relief. Useful, not parity, and it is the feature that would most
benefit from being designed alongside Layered rather than before it.
