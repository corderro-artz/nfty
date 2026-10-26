# Rise — pixel art to 3D, in nfty — design

**Date:** 2026-09-25
**Status:** written from the author interview of 2026-09-24/25; awaiting the author's review.
**Supersedes:** `2026-09-22-nfty-model-design.md` (projects 2 and 3) and
`2026-09-22-model-preview-design.md` wherever they conflict. Both stay as written — `docs/superpowers/`
is a dated record — and this document says, section by section, what survives from them.
**Ledger:** every decision below was taken in the interview and is recorded in
`.superpowers/sdd/2026-09-25-voxel-designer/decisions.md`.

## What this is for

nfty should hand a person a **3D model** of an asset as readily as it hands them a PNG, and let them
**shape** that model rather than accept a flat slab. The feature and its page are called **Rise**
("Rise this asset", "the Rise page"). A sprite rises off the paper: every solid pixel becomes a column
of voxels that can be pushed toward the viewer, pulled away from them, or both.

Mold was rejected (spoiled food), Proof was rejected (reads as preview/check), and Bake and Plate are
already names inside the code.

## The depth model

This is the part that differs most from `mixel`, and **mixel is not the reference for it**.

- Every solid pixel carries **two independent counts**: `front ≥ 0` (toward the viewer) and
  `back ≥ 0` (away from them).
- **Thickness = 1 + front + back.** The 1 is the paper — the center plane — and it can never be
  removed. A pixel at `front 0 · back 0` is one voxel thick.
- Each side caps at **255** (one byte).
- A pixel can extrude **both ways at once**. `mixel` cannot; nfty must.
- A new source opens **flat**: every pixel at `0 · 0`, the paper. (This replaces the "open at ⅛ of
  the smaller side" decision of 2026-09-24 — paper is the honest start, and the fill-all action below
  is one click from any other.)
- **Transparent pixels have no depth.** A click on one does nothing: there is no color to extrude.
- **Colors are the picture's exact pixel colors.** A column's sides take its pixel's color. Rise
  paints no color.
- **Only fully opaque pixels (`A == 255`) are voxels.** There is one threshold, it is not a control,
  and one function answers "is this pixel solid?" for every caller (mixel's defect D1).

### What depth is NOT

- **Not modes.** Front / Symmetric / Raised are gone from every surface, the CLI included. Their job
  is done by **fill-all: front N, back M**, which sets every solid pixel of the source at once.
- **Not a size rule.** mixel's power-of-two `ImageSize` stays dropped; nfty's canvas is arbitrary.
- **Not in the DNA.** A variant's id already names its depth map, so `Dna.Compute` is unchanged.

## Where depth lives

- **Per VARIANT, in the `.igt`.** A variant with no stored map is paper. An absent map is the
  natural encoding of "nothing raised yet", not a compatibility default.
- **The finished image is what is voxelized, always as ONE solid.** Layers are never extruded as
  separate slabs — stacked slabs z-fight wherever two layers cover the same pixel.
- **Combining a stack.** The solid mask is the composited image's alpha (`A == 255`). A book with
  no partial alpha anywhere (see "2D only") has every layer pixel at 0 or 255, so the composite is
  solid exactly where some layer is. ~~Each solid pixel takes its depth from the **topmost layer whose
  own pixel is solid there** (walking `layerOrder` top-down).~~ **Amended 2026-10-26: union rule,
  per-face owner color — author ruling.** The topmost-wins depth rule is replaced by the union rule
  in "The stack rule" below.

### The stack rule — union of solids (amended 2026-10-26: union rule, per-face owner color — author ruling)

The step 9 final review (I1) found the rule above carving the demo chest: a strap authored with back
0 took BOTH depths from the topmost layer, so every overlay pixel cut a groove nine voxels deep into
the chest's back. The author ruled a new rule:

- **Each layer keeps its own map, unchanged.** Per variant, `depth/{variantId}.dep`, painted on the
  Rise page (left +1 front, right +1 back, Shift −1). Nothing about storage or painting changes.
- **Geometry is the union of every solid layer's column.** Every column contains the paper, so the
  union of intervals is itself one interval: per pixel, **front = max over the layers solid there,
  back = max over the layers solid there**. Where layers overlap, the upper layer owns the voxels.
  Nothing subtracts; no layer can carve another.
- **Front face color = the topmost layer among those reaching the max front. Back face color = the
  topmost layer among those reaching the max back.** Ties go to the upper layer, so an all-paper stack
  shows exactly its 2D art from the front, as before. Usually the front owner IS the topmost solid
  layer and the front view equals the art; when a lower layer protrudes further, **its** color shows
  from the front, and the artist must consider that. Engraving is done on a layer's own map.
- **Where the owner is the topmost solid layer, its color is the composite's own pixel** — exact by
  construction, whatever path recovered the layer colors.
- **Layer colors are the colors the asset rolled.** A cooked asset recolorizes each present layer
  with its own rolled `(H,S)` (`GeneratedAsset.ColorRolls`), exactly as generation did; an asset read
  back from a Set uses its recorded `LayerColor` (rounded to 0.1° / 0.001 — only non-topmost owners
  can show the difference). A selection with no colors (a recipe's stored layers, a test) uses the
  stored pixels, the Rise page's existing rule for a Dynamic/Static variant.
- **Walls** take one of the two owner colors per z-range: each pixel carries a **seam** — the part of
  the column the back owner owns by the upper-owns rule (if the front owner sits above the back owner,
  everything behind the front owner's own back; else the back owner's whole interval). Wall voxels
  below the seam take the back color, above it the front color. A wall segment crossing the seam is
  split in two quads. A third, middle layer never gets its own wall color: its voxels take whichever
  of the two owners covers them. That is the cost of a two-color texture, stated, not hidden.
- **Texture = one atlas, front and back side by side.** `[front W][gutter 1][back W]`, height H; the
  gutter repeats the front half's last column so `NEAREST` sampling exactly on the front half's right
  edge cannot land in the back half. Every model uses the atlas, a single picture included (its back
  half repeats the art), so there is ONE UV rule and ONE texture shape for the writers and the
  preview. Encoded exactly as before (8-bit RGBA, `SkipMetadata`, no interlace), `NEAREST` /
  `CLAMP_TO_EDGE`: same pixels, same bytes.
- **Partial alpha is unchanged:** every layer is scanned before combining and the refusal names the
  layer.
- **Who uses it:** `StackDepth.ForAsset` and `SetModeler` (Set `models/`), the Rise page's recipe and
  asset sources (the combined read-only view and its THICKNESS rail, which now counts union
  thickness), and the Rise page's export of them. The CLI `model` command rises one picture or one
  variant, never a stack, so it changes only by the atlas.
- **Cooked Sets already on disk keep their old models** (topmost-wins geometry, single-image
  texture). Acceptable: nothing is shipped and the branch is not merged. No `set.json` field records
  which rule built a model.
- **A cooked Set** carries no depth. Rise recovers it by locating the source book by hash
  (`CookBookLocator`, the same search the export dialog and the combined-chance figure use). Not
  found ⇒ every asset is paper, and the page says so in one sentence.
- **A loose PNG or a sheet cell** keeps its depth for the session and in anything exported from it,
  and the page offers **Save as .igt** — the only way loose art keeps its depth.

## "2D only" — derived, never set

Partial alpha is legal in nfty (the editor's opacity lock is a default an author may turn off), and it
is the one thing a voxel model cannot represent. Half the assets of a book with transparency cooking
into malformed models is **not acceptable**, so:

- **"2D only" is DERIVED from the pixels.** Any pixel with `0 < A < 255` in any variant of a book
  makes the **whole book** 2D-only. No click sets it, no click clears it.
- **Writers recompute and store it on every save**, at every level that is listed without decoding
  (`.igt`, `.rcp`, `.cbk`, `.mnu`), so `ArchivePeek` and the Kitchen shelf read it for free. Anything
  that has already decoded the pixels **recomputes rather than trusting the stored value** — the flag
  is a cache for listings, never an input to a render or a model.
- **A 2D-only book:** voxelizing the book, the Set or a cook is off, hidden-and-explained. **Individual
  items with no partial alpha of their own remain risable** — a 2D-only book does not stop the author
  looking at a chest lid in 3D.
- **An item with partial alpha of its own is refused by name**, in the GUI and the CLI. It is not
  silently thresholded: that would be the "half the models are malformed" outcome at the scale of one.
- **The editor warns immediately** when the opacity lock is turned off, saying that saving a
  see-through pixel makes the book 2D-only — at the toggle, before any pixel is painted.
- **The rail marks it.** The book and each item carrying partial alpha get a dedicated glyph (new SVG
  in `assets/icons/`, see "Icons").
- **The fix path:** a listing of the offending variants, plus one click, **Make voxel-ready** — snaps
  every partial alpha at 50% (`A ≥ 128 → 255`, else `0`), shown as a preview first, undoable, and gated
  by the edit lock like every structural write.
- `VoxelReport` stays as the CLI's listing (`inspect --voxel`) and becomes that listing's source.
  Its old "warn and proceed" stance is superseded: Rise refuses rather than proceeds.

## Format

### Schema 2, and the compatibility purge

The author holds no archives worth keeping except the demo, which is rebuilt anyway, and asked that
compatibility code not be carried: **no backward compatibility.**

- `Schema.Current` **and** `Schema.Oldest` become **2**. The `Oldest..Current` gate stays — it is the
  mechanism that lets a later schema read this one — but nothing reads 1. A v1 archive is refused by
  the existing `UnsupportedSchemaVersionException`, which already names the version.
- The v1 fixtures under `tests/fixtures/` are **replaced** by v2 fixtures written by the build that
  introduces schema 2 — a replacement the author authorized, not the laundering CLAUDE.md warns
  against. `FixtureFreezeTests` then freezes the new ones.
- The app-state migrations go too: `RecentsService`'s legacy-file import and the palette store's
  flat-array reading. Both exist only to carry state across from builds nobody is running.
- CLAUDE.md's format paragraph (the `Oldest..Current` contract, "every field added after v1 is
  optional") is rewritten carefully, not deleted: the contract still holds from v2 onward.
- **Order:** the purge happens only **after** the current state is verified stable (done:
  2026-09-25, suite green on main) and each removal is test-guarded — the app-state migrations first,
  then the schema bump together with the new fields below, since v2 fixtures cannot exist before the
  v2 writer does.

### Depth maps

- Stored in the `.igt` as `depth/{variantId}.dep`, beside `variants/{variantId}.png`.
- **Raw bytes, not an image**: `width × height × 2`, row-major, top row first; for each pixel the
  `front` byte then the `back` byte. The canvas gives the dimensions, so there is no header. The ZIP's
  own deflate compresses the zeros.
- Not a PNG on purpose: `Nfty.Ticket` must read it with no imaging library, a depth map viewed as a
  picture would mislead anyone who opened it, and raw bytes are deterministic by construction.
- A map whose length is not `2 × canvas area` is a `Validator` problem naming the variant.
- A map entry for a pixel that is not solid is ignored when meshing, and **zeroed by the writer**, so
  a stored map never carries depth for a transparent pixel.
- An all-zero map is **not written** — absent and all-zero mean the same thing, and one of them must
  be the only spelling.

### The 2D-only flag

`bool PartialAlpha` — named for what it measures; the UI calls it "2D only".

| Manifest | Meaning |
|----------|---------|
| `IngredientManifest.PartialAlpha` | some variant of this ingredient has a partial-alpha pixel |
| `RecipeManifest.PartialAlpha` | some ingredient in this recipe does |
| `CookBookManifest.PartialAlpha` | some recipe in this book does — the book is 2D-only |
| `MenuBook.PartialAlpha` | copied from the book |

Required (non-nullable) in schema 2; always written by the writer; recomputed from pixels by anything
that decoded them.

### Menu

`.mnu` carries depth beside its art as `depth/{cellKey}.dep` (the same `MenuBook.CellKey` the art
cells use), same raw layout, sized to the **cell**. `MenuArchive` hands back the bytes — it decodes
nothing, as today — so a runtime host can build the same solid nfty builds.

### Set

- `SetLayout` gains a fourth directory, **`models/`**, `NNNN.<ext>` with the four-pad numbering of
  `metadata/` (the ordering rule `SetReader` keeps). `SetLayout.Directories` is an include list, so
  adding it is what makes `Pack` carry it.
- `set.json` gains a `models` block: format, pivot, voxel size. Absent when a cook made no models.
- **The number stamp never reaches a model.** Models are built from the unstamped asset; the test is
  an export with the debug mark armed whose model texture equals the unstamped asset's.

### Spritesheets

- nfty writes its grid into every sheet PNG it produces as a `tEXt` chunk, keyword `nfty-grid`,
  value e.g. `cols=8;rows=4;cell=64x64;count=29` (~60–80 bytes).
- Opening a sheet: read `nfty-grid` if present; otherwise **auto-detect** — equal-sized cells
  separated by fully transparent rows and columns; otherwise the author enters columns × rows or a
  cell size. Auto-detect is the default and manual is always available.
- **A foreign sheet gets the grid written in, always and silently**, once one is known: the file is
  rewritten whole with the same pixels and the chunk added (reported with its byte size in the status
  line). A read-only file keeps the grid for the session only.

## `Nfty.Model` — the engine

```
src/Nfty.Model/         NEW. net10.0. ProjectReference: Nfty.Core. No new package.
tests/Nfty.Model.Tests/ NEW.
```

`Nfty.Core` does not reference `Nfty.Model`. **Zero new third-party packages**, as before.

### Surviving from the 2026-09-22 model spec

Unchanged and binding: taking mixel's geometry *ideas* but copying no file; writing the container
ourselves with `System.Text.Json` + `BinaryPrimitives` (no SharpGLTF); **every** spec-compliance point
(`POSITION` min/max, 4-byte chunk padding, component-aligned accessors, `UNSIGNED_INT` indices, PNG in
a target-less bufferView, `NEAREST`/`CLAMP_TO_EDGE` sampler, `alphaMode: MASK`); defects **D1** (one
solid test), **D3** (single-sided), **D4** (named for nfty), **D5** (an item that cannot be risen is
skipped and named, never fails a batch), **D6** (takes `Image<Rgba32>`, never bytes), **D7** (nothing
cached lazily); vertex dedup and `GreedyRects` cost measured with `Perf` before optimizing; the
`GlbStructureTests` validator; byte-identical output for identical input.

### Changed from it

- **Two-sided per-pixel depth replaces `Depth` + `ExtrudeMode`.** No modes anywhere.
- **Three output formats**, all serialized from **one JSON object model** — never by string
  replacement over serialized text (D2's lesson, now the rule for the formats D2 was about):
  - **GLB** — one binary file, the default;
  - **glTF + .bin** — text JSON with a sibling binary, `uri` set in the object model;
  - **glTF embedded** — text JSON, the buffer as a `data:` URI set in the object model.
- Deferred-in-that-spec items that are now **in**: depth maps; `.gltf` output.
- Still out: per-layer slabs ("Layered" mode) — superseded by the one-solid rule above, not deferred.

### Surface

```csharp
namespace Nfty.Model;

enum ModelFormat { Glb, GltfWithBin, GltfEmbedded }
enum Pivot { BottomCenter, Center, MinCorner }

sealed record DepthMap            // width × height, front and back per pixel, 0..255 each
sealed record ModelFile(string FileName, byte[] Bytes)
sealed class Mesh                 // positions, normals, UVs, indices; what the writers AND the preview consume
sealed record RiseOptions
{
    float       VoxelSize { get; init; } = 1f;
    Pivot       Pivot     { get; init; } = Pivot.BottomCenter;
    ModelFormat Format    { get; init; } = ModelFormat.Glb;
}

// Geometry, shared by the writer and the preview. Null when nothing is solid (D5).
Mesh? Riser.TryBuild(Image<Rgba32> art, DepthMap? depth, RiseOptions options);

// Files: one entry for GLB and embedded, two (.gltf + .bin) for GltfWithBin.
IReadOnlyList<ModelFile> ModelWriter.Write(Mesh mesh, Image<Rgba32> texture, RiseOptions options, string baseName);

// The stack rule, in one place: composite + topmost-solid-layer depth.
(Image<Rgba32> Art, DepthMap Depth) StackDepth.Combine(IReadOnlyList<(Image<Rgba32> Layer, DepthMap? Depth)> bottomFirst);

// Amended 2026-10-26: union rule, per-face owner color — author ruling. Combine now returns a
// StackRise (Art, union Depth, StackFaces: front-owner colors, back-owner colors, per-pixel seam);
// Riser.TryBuild takes the faces as an optional argument and ModelWriter.Write writes the
// FaceAtlas built from (art, faces). See the plan 2026-10-26-rise-step9b-union-depth.md for names.
```

Refusing partial alpha is `Riser`'s first check, with a message naming the source.

### Geometry

A column's extent along Z, in voxel units with the paper centered on `z = 0`:
`[-(0.5 + back), 0.5 + front]`. +Z faces the viewer (glTF's default camera looks down −Z).

- **Front faces:** greedy-merge (maximal rectangles) solid pixels of **equal `front`**, emitted at
  their `0.5 + front`. **Back faces** likewise on `back`. A flat sprite is still a few dozen quads.
- **Walls:** between a column and each 4-neighbour (or the outside), the exposed part is the column's
  interval **minus** the neighbour's interval — which, with two-sided depth, can be **two** segments
  (one toward the viewer, one away). mixel's single-segment overhang logic is generalized to that.
- Walls sample their pixel's color (all four UVs at the pixel center); faces sample the art directly.
  *Amended 2026-10-26: union rule, per-face owner color — author ruling.* Front faces sample the
  atlas's front half, back faces its back half; a wall samples the front or back owner's pixel center
  by the seam, split where it crosses it ("The stack rule" above).
- **The mesh is a closed manifold** — every edge shared by exactly two triangles — for any depth map.
  That one assertion catches whole classes of wall bug and is the anchor test.

## The preview — software rasterizer

**Surviving from `2026-09-22-model-preview-design.md`, unchanged:** the software rasterizer and the
reasons for it (headless-renderable, exact-pixel testable, no package, GPU seam kept beside it —
see `BACKLOG.md`), `Camera.ViewProjection` as the one mapping, turntable orbit with pitch clamped to
±89°, no auto-rotate, pointer-anchored zoom, the gesture/keyboard table, pointer capture with
`PointerCaptureLost` as abort, focus on attach, back-face culling, z-buffer, nearest sampling, flat
quantized shading, no silhouette anti-aliasing, a token background, rendering a `Mesh` and never
parsing a model file back, and every test in its Testing section.

**Superseded:** the `ModelDialog` host and the Ingredient-pane / inspector inline previews — **3D
appears only on the Rise page** (panes get a button that opens it); the depth-default discussion
(Rise opens flat); the "flat-backed" caveat (two-sided depth is the answer to it).

**Live on every click:** a depth change rebuilds the mesh and repaints. Budgeted like the editor's
companion tile (canvas never skipped, 3D repaint capped at 30/s, exact on gesture end).

## The Rise page

Its own page, mirroring the Ingredient Editor's three panes so it reads as part of the app, built
only from app tokens and the brand. Layout, controls and wording are verified against rendered frames
at the minimum window (1280×720) before they are called done.

```
┌ left rail 262 ─────┬ canvas + toolstrip ──────────────────┬ right rail 300 ───────┐
│ SOURCE             │ [brush][line][rect][fill][select] … │  DEPTH | EXPORT        │
│  items, checkboxes │                                      │                        │
│  for batch         │        canvas (pixel art, labels)    │  (tab content)         │
│ (sheet grid row)   │                         ┌─3D tile─┐  │                        │
│                    │                         └─────────┘  │                        │
│                    │ front 1 · back 1 · total 3           │  [ Export ]            │
└────────────────────┴──────────────────────────────────────┴────────────────────────┘
```

### Center: canvas

- The pixel art at the editor's own zoom/pan/lattice rules (`Geometry` as the one mapping).
- **Controls on this page** (and only here):

  | Input | Action |
  |-------|--------|
  | Left-click | front +1 |
  | Right-click | back +1 |
  | Shift + left-click | front −1 (floors at 0) |
  | Shift + right-click | back −1 (floors at 0) |
  | Ctrl while drawing | constrain (45° lines, square shapes) — Shift is taken here |

  *Left/right swapped by the author 2026-10-20 (was right = front, left = back): the primary button
  raises toward the viewer, as in most tools.*

- **Tools** — brush (size 1 is mixel's click), line, rect, same-color fill, select — reused from the
  editor. Each changes every pixel it touches by ±1 **once per gesture**, on the side the button
  picks. Select marks a region; a click inside the marked region applies its ±1 to every solid pixel
  in it, once.
- **A recipe source carries the reroll die** in the toolstrip, so a stack can be re-rolled without
  leaving the page (the die is `Random.Shared`-faced as on the Recipe pane; the roll itself is
  `Ticketer.TryRoll`, as everywhere).
- **Cell labels:** each cell shows its **total** thickness when the cell is large enough to read
  (drawn by one overlay control's `Render`, not a `TextBlock` per cell); smaller, they are omitted.
- **Status line, reserved:** `front 1 · back 1 · total 3` for the pixel under the pointer; blank
  ink, same height, when the pointer is elsewhere.
- **The 3D tile** sits in the corner and **swaps** with the canvas — the editor's fill-pane pattern,
  including its rule that the tile always stays so the way back is never lost.

### Left rail: source

What is being risen, and its siblings: a variant's ingredient's variants; a recipe's layers; a Set's
assets; a sheet's cells. **Checkboxes** select items for batch export. A sheet adds a reserved grid
row (auto / columns × rows / cell size). Partial-alpha items carry the 2D-only glyph and are not
risable; the book row carries it when the book is 2D-only.

### Right rail: tabs

- **DEPTH** — fill-all (front N, back M, Apply), the partial-alpha listing and **Make voxel-ready**
  when relevant, Save depth (edit-lock gated).
- **EXPORT** — format (GLB / glTF + bin / glTF embedded), pivot, voxel size, and, for a batch,
  **folder or zip** chosen here. Export writes through the same `ModelWriter` the cook uses.

### Page rules

- **Its own undo history**, separate from any other page; leaving with unsaved depth asks first.
- **Saving depth is structure** → gated by the edit lock, written through `CookBookPersistence` like
  every structural edit. Make voxel-ready likewise.
- **Recipe and asset sources show combined depth read-only** until write-through (below).
- **A cooked Set is read-only.** A **sealed** Set can be viewed in Rise; exporting from it is refused,
  by the same seal flag that gates Save image.
- **Performance is measured, never asserted as wall-clock**: `Perf` scopes on real 64px and 512px art
  for a depth click, a mesh rebuild and a 3D repaint.

## Entry points and integration

| Where | What |
|-------|------|
| **Ingredient pane / editor** | a **Rise** button — the variant, with its depth editable |
| **Recipe pane** | the lone centered die under the rolled image becomes a clean **action row**: Reroll with **Rise** adjacent — the rolled stack, depth read-only |
| **Set browser** | Rise the selected asset, or check several for a batch |
| **Landing** | one entry in OPEN, *Rise a picture…*, for a loose PNG or a sheet — the only path loose art has |
| **Cook dialog** | **Include 3D models**, with the full options (format, pivot, voxel size) in a collapsed section; hidden-and-explained for a 2D-only book |
| **Export dialog** | `models/` as a content axis when the Set carries them; the IMAGES tab notes the debug mark never reaches `models/` |
| **CLI** | see below |

### CLI

```bash
nfty model <input> --out <path> [--variant ID] [--front N --back M]
                   [--format glb|gltf|gltf-embedded]
                   [--pivot bottom-center|center|min-corner] [--voxel-size F]
nfty generate … --models [--model-format …] [--pivot …] [--voxel-size F]
```

- `<input>` is a `.png` or an `.igt` with `--variant`, dispatched by `Archives.KindOf`; options that
  belong to the other form are **rejected, never ignored**.
- An `.igt` variant uses its **stored** depth map; `--front`/`--back` is an optional fill-all applied
  on top. A `.png` has no stored map, so it is paper unless filled.
- `generate --models` is the Cook dialog's checkbox, and refuses a 2D-only book by name.

## Icons

Two new glyphs, both drawn in `assets/icons/*.svg` and generated into `Icons.axaml`:

- **rise** — a solid with a visible depth face. At real risk of reading like `kitchen`'s archive box
  beside it: render the set at 12/14/18px and look before accepting it.
- **2D only** — a flat sheet, distinct from `rise` at a glance.

## Write-through — option C, last

Painting depth on a combined view (a recipe or an open book's rolled stack) writes through to the
variant that owns the pixel:

- Clicking a pixel **selects its owning layer** (the topmost solid one, the combining rule).
  *Amended 2026-10-26: union rule, per-face owner color — author ruling:* the owner of a front
  stroke is the pixel's front owner, of a back stroke its back owner.
- Strokes then edit **only that variant's** depth; the other layers draw **dimmed and not clickable**.
- The 3D tile keeps showing the **whole character**, live.
- A reserved header names the target and its reach: `editing Lock / Gold · in 212 of 1,200 assets`.
- Never from a cooked Set (read-only; its book is not open).
- Reuses `StackPreview`'s split-around-the-edited-layer render.

## Build sequence (author-approved)

Each step is its own plan, branch and merge; nothing starts until the one before it is merged and green.

1. ~~Plan 1 review + merge~~ — done 2026-09-25.
2. ~~Stability + dead-code sweep~~ — done 2026-09-25.
3. **Compatibility purge (app state):** remove the recents legacy import and the palette flat-array
   read, each test-guarded.
4. **Format:** schema 2 (`Current = Oldest = 2`), depth maps in `.igt` and `.mnu`, `PartialAlpha` at
   every level, v2 fixtures replacing v1, CLAUDE.md format paragraph rewritten.
5. **`Nfty.Model` engine:** solid test, depth map, geometry, stack combine, the three writers,
   `nfty model`. The 2026-09-22 model plan is rewritten, not reused.
6. **Software rasterizer + preview control.**
7. **The Rise page.**
8. **Entry points; Set / cook / export / sheet integration**, including `generate --models`, the
   `nfty-grid` chunk and Make voxel-ready.
9. **Demo rebuilt with depth** (drafted by Claude in `tools/demo/build-demo.py`), re-cooked for the
   author to explore.
10. **Write-through (option C).**

## Testing

House style throughout: exact values on tiny synthetic inputs built in memory, no golden files,
every fix shown red first and mutation-probed.

- **Format:** depth map round-trip; wrong-length map is a named `Validator` problem; all-zero map is
  not written; transparent-pixel depth is zeroed; `PartialAlpha` written at every level and
  recomputed from pixels on read; a v1 archive is refused naming version 1; fixtures frozen.
- **2D only:** one partial-alpha pixel in one variant flips the book; Make voxel-ready snaps at 50%
  exactly (127 → 0, 128 → 255), is undoable, and is edit-lock gated; `generate --models` refuses a
  2D-only book; a single partial-alpha item is refused by name.
- **Geometry:** closed manifold for every fixture shape (plus, L, single pixel, square, hole,
  checkerboard) × depth patterns (paper, front only, back only, both, neighbors differing on both
  sides so walls split in two); triangle counts; bounding boxes; pivot placement.
- **Stack combine:** topmost solid layer wins; a transparent top pixel defers to the layer below.
  *Amended 2026-10-26: union rule, per-face owner color — author ruling:* front and back are each the
  max over solid layers; an overlay with back 0 over a body with back 9 makes back 9; the back face
  samples the back owner's color; a lower protruding layer owns the front; volume is
  `Σ(1 + maxFront + maxBack)`; the GLB is byte-stable.
- **Writers:** all three formats from one object model; `GlbStructureTests`-style validation for each
  (the `.gltf` variants parse the JSON and check the same accessor/bufferView invariants); the
  embedded form's data URI decodes to exactly the `.bin` bytes; byte-identical output twice.
- **Stamp:** a model exported with the debug mark armed matches the unstamped asset.
- **Sheet:** `nfty-grid` written by `SpriteSheet` and read back; auto-detect on gutters; foreign-sheet
  rewrite keeps every pixel identical.
- **Rise page:** gesture tests driving real pointer events (left-click adds front, Shift+right
  subtracts back, one ±1 per gesture however long the stroke); rendered frames at the minimum
  window in both themes; joins `DarkModeContrastTests`' `Screens()` in its riskiest state (a 2D-only
  book with the listing showing); the corner-tile swap keeps its way back.
- **Preview:** everything in the 2026-09-22 preview spec's Testing section.

## Out of scope

Lighting models, shadows, materials beyond the baked texture, animation, per-layer slabs, reading a
model file back, and GPU rendering (the seam is kept; the toggle is `BACKLOG.md`'s).
