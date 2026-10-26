# Rise Step 9b: Union-of-Solids Stack Rule Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A layered asset rises as the union of its layers' solids: front and back are each the max over solid layers, each face is colored by the layer that owns it, so overlays no longer carve the chest's back. The demo is reshaped so the chest reads as a whole object from every angle.

**Architecture:** `StackDepth.Combine` keeps one place for the stack rule but now returns a `StackRise`: the composite art, the union depth map and `StackFaces` (front-owner colors, back-owner colors, a per-pixel seam saying which owner each wall voxel belongs to). `Riser.TryBuild` takes the faces as an optional argument. Every model textures from ONE atlas, `[front W][gutter 1][back W]`, built by `FaceAtlas` and used by the writer and the preview alike. Front faces sample the front half, back faces the back half, and walls sample the owner of their z-range, split at the seam. Callers (`SetModeler`, the Rise page's recipe and Set sources, its export) pass the faces through; the CLI `model` command rises one picture and only gains the atlas.

**Tech Stack:** .NET 10, xunit.v3, ImageSharp 3.1.11 (pinned), Avalonia 12.1.1 headless for frames, Python + PIL for the demo generator only.

**Spec:** `docs/superpowers/specs/2026-09-25-rise-design.md`, section "The stack rule — union of solids (amended 2026-10-26)", plus the dated notes in "Where depth lives", "Surface", "Geometry", "Write-through" and "Testing". The finding it answers: `.superpowers/sdd/2026-10-22-rise-step9-demo-depth/final-review.md`, I1.

## Global Constraints

- `Directory.Build.props`: `TreatWarningsAsErrors` + `GenerateDocumentationFile`. Every public member is documented, and a warning fails the build. Each task leaves `dotnet build nfty.sln` green.
- Zero new packages. `Nfty.Model` references `Nfty.Core` only; `Nfty.Core`/`Nfty.Ticket` never reference `Nfty.Model` (`AssemblyRulesTests`).
- Only `A == 255` is solid (`Opacity.IsSolid`, one test, D1). Every layer is scanned for partial alpha BEFORE combining, and the refusal names the layer (`PartialAlphaException`). Unchanged.
- Per pixel: **front = max over solid layers, back = max over solid layers**. The front owner is the TOPMOST layer among those reaching the max front, and the back owner likewise for the back; ties go to the upper layer. Where the owner is the topmost solid layer, its color is the finished art's own pixel.
- Column z-range stays `[-(0.5+back), 0.5+front]`. Mesh volume = `Σ(1 + maxFront + maxBack)`. The mesh stays closed + consistently oriented, NOT strict 2-manifold (`MeshChecks`); do not "fix" that check.
- Texture = atlas `(2W + 1) × H`: front at x∈[0,W), gutter at x=W repeating front column W−1, back at x∈[W+1, 2W+1). It is encoded with the existing `TextureEncoder` (8-bit RGBA, `SkipMetadata = true`, `InterlaceMethod = None`), sampled `NEAREST`/`CLAMP_TO_EDGE`, `MASK` 0.5. Same pixels → same bytes.
- `ModelWriter` keeps ONE glTF object model; formats differ only in `buffers[0].uri` set in the object model (D2). Still one image, one texture, one material.
- Depth never enters DNA. The demo counts **615,600** exactly. Don't re-run `build-demo.py` casually; the `.cbk` is committed ONCE, after the author's yes (step 9 Q5 two-phase handoff).
- Cooked Sets already on disk keep their old models; nothing migrates them.
- Tests are xunit.v3, named `Snake_case_sentences`, with fixtures built in memory (`Art.FromAscii`, 1×1/2×2 images). Demo tests read the EMBEDDED bytes via `DemoCookBook.WriteTo`. Use temp folders via `Directory.CreateTempSubdirectory()` and delete them in `finally` (step 9 M3).
- Use US English. Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Open Questions (controller rules; recommendation first)

- **Q1: Atlas or a second image for the back texture?** Recommend the **atlas** `[front][gutter][back]`. A second image would need a second texture, a second material and a second primitive (back faces split off), which doubles the object model's shape. The atlas keeps one image, one material and one primitive, and only the UVs change.
  - The 1-px gutter exists because `NEAREST` at exactly `u = W/(2W)` would sample the back half's first column. With the gutter, that edge lands on a copy of the front's last column. The back half's right edge is `u = 1`, which clamps.
- **Q2: Should a single picture (no stack) also use the atlas?** Recommend **yes, always**. One UV rule, one texture shape for the writer and the `Raster` preview, and no "is this a stack?" branch anywhere.
  - Cost: the back half repeats the art. Deflate's window covers the repeat whenever `4W + 1 ≤ 32 KiB`, which is every canvas nfty makes. Task 2 measures the GLB size delta on the 512-px perf fixture and records it in its report. If the delta exceeds 10%, stop and tell the controller.
- **Q3: What color do the walls take?** Recommend **two-owner walls split at a per-pixel seam**, which is the spec's "better option".
  - Each wall voxel takes the front owner's or the back owner's color by the upper-owns rule between those two layers.
  - Cost:
    - One extra quad only where an exposed wall segment crosses the seam. A single picture has none, so its quad count is identical.
    - A third, middle layer never gets its own wall color. Its voxels take whichever of the two owners covers them.
  - Why not true per-layer wall color: it would need one atlas strip per layer or per-vertex colors, which breaks the single-texture model the writers and preview share. Why not front-only walls (simplest): a back-owned strip would show the strap's color down the body's back edge. The seam costs about forty lines in `Riser`/`MeshBuilder` and keeps `ModelWriter` untouched beyond the atlas.
- **Q4: Where do layer colors come from?** Recommend **recolorizing each present layer with the asset's own rolled color**.
  - A cooked asset uses `GeneratedAsset.ColorRolls` (exact). A Set item uses its `nfty/NNNN.json` `LayerColor` (rounded 0.1°/0.001). Combined with "topmost owner = finished art's pixel", the rounding can only show on a non-topmost owner.
  - Alternative: have `Generator` hand out its colorized layer images. Rejected because `GeneratedAsset` would hold N extra images per asset across a 10k streaming cook.
  - Cost: `SetModeler` colorizes each Dynamic/Static layer a second time. Measure it with a `Perf` scope `StackDepth.Colorize`.
- **Q5: The chest's back profile.** Recommend the numbers below as a DRAFT the author retunes from frames. The tests pin acceptance rules, not numbers.
  - Lower rows mirror the front: `BODY_BACK = 9` with the overhang rows one deeper and a one-voxel bevel on each row's outermost column.
  - The chest lid curves on its back as on its front, with back rising row by row toward y=11 and rounded ends: `CHEST_BACK_DOME = {7: 5, 8: 6, 9: 7, 10: 8, 11: 9}`, and `min(CHEST_BACK_DOME[y], 4 + 2*(x-row0), 4 + 2*(row1-x))`.
  - The box lid is a slab at `BODY_BACK` with the same bevel.
  - Overlays use back 0 everywhere (ruling). Feet keep `(1,1)` and `(0,5)`.
- **Q6: What does the Rise page canvas show when a lower layer protrudes?** Recommend the canvas keeps showing the **composite** (the 2D asset as it mints), and only the 3D view shows the protruding owner. No warning is added: the spec says the artist must consider it, and the 3D tile beside the canvas already shows it.
- **Q7: Should a model record which stack rule built it?** Recommend **no**. Nothing is shipped and the branch isn't merged, so old models on disk are acceptable (spec). A `set.json` field that is only ever one value is noise.

## Review Focus

1. **A wall segment crossing the seam on both sides of the paper.** A neighbor sitting inside both ends gives two wall segments, and each may cross the seam, so one pixel edge can yield four quads. Expect a closed mesh and exact volume. Pinned in Task 2 (`A_wall_crossing_the_seam_on_both_segments_stays_closed`).
2. **A front owner that is NOT the topmost solid layer while the topmost reaches the max back.** For example, the body protrudes in front and a top strap has the deepest back. The seam must then be the strap's whole interval, and walls above it take the body's color. Pinned in Task 1 (`When_the_back_owner_is_above_the_front_owner_the_seam_is_the_back_owners_interval`).
3. **The atlas edge under `NEAREST`.** A front face ending at the art's right edge must not sample the back half. Pinned in Task 2 (`The_gutter_repeats_the_front_halfs_last_column`), and by the Raster test (`A_front_view_render_never_samples_the_back_half`).
4. **A Set item recovered with rounded colors.** The front view must still equal the Set's PNG exactly wherever the topmost layer owns the front. Pinned in Task 3 (`A_set_asset_front_owner_colors_are_the_sets_own_pixels`).
5. **Ownership of the faces' images across the export snapshot.** A frozen entry must clone the faces, and `Release` must dispose them. A reroll during export must not dispose faces the batch is reading. Pinned in Task 3 (`An_export_snapshot_owns_its_faces`).

---

### Task 1: The union stack rule and face owners

**Files:**
- Create: `src/Nfty.Model/StackFaces.cs`, `src/Nfty.Model/StackRise.cs`, `src/Nfty.Model/StackPick.cs`
- Modify: `src/Nfty.Model/StackDepth.cs` (rule + return types), `src/Nfty.Model/SetModeler.cs`, `src/Nfty.App/Rise/RecipeRiseSource.cs`, `src/Nfty.App/Rise/SetRiseSource.cs` (compile-only: read `.Depth`, dispose the `StackRise`)
- Test: `tests/Nfty.Model.Tests/StackDepthTests.cs` (existing topmost-wins tests are rewritten to the union rule; partial-alpha and size tests are kept), `tests/Nfty.Model.Tests/DemoModelsTests.cs` (compile-only: `.Depth.Front`)

**Interfaces:**
- Produces:
  - `public sealed class StackFaces : IDisposable`, which owns both images:
    - `Image<Rgba32> Front`, `Image<Rgba32> Back`
    - `int Width`, `int Height`
    - `int BackColored(int x, int y)`: how many voxels, counted from the column's back end, take the back color. 0 on a non-solid pixel.
    - `StackFaces Clone()`
    - `static StackFaces Uniform(Image<Rgba32> art)`: both faces clone the art, seam 0. This is a single picture's faces.
  - `public sealed class StackRise : IDisposable`, which owns `Art` and `Faces`:
    - `Image<Rgba32> Art`, `DepthMap Depth`, `StackFaces Faces`
    - `StackFaces DetachFaces()`: hands ownership out. A later `Dispose` then skips them.
  - `public readonly record struct StackPick(string IngredientId, string VariantId, RolledColor? Color = null)`:
    - `public static implicit operator StackPick((string IngredientId, string VariantId) t)`
    - `public static IReadOnlyList<StackPick> WithColors(IReadOnlyList<(string IngredientId, string VariantId)> present, IReadOnlyList<LayerColor> colors)`: `LayerColor.Layer` is the ingredient id (`SetWriter.ToLayerColor`). A color is set only when both `H` and `S` are non-null.
  - `StackDepth.Combine(IReadOnlyList<(Image<Rgba32> Layer, DepthMap? Depth)> bottomFirst, string source = "the stack", Image<Rgba32>? finished = null) -> StackRise`
    - When `finished` is given, `Art` is its clone and topmost owners take its pixels. A size mismatch, or a solid mask that disagrees with the layers', throws `ArgumentException` naming the first such pixel.
  - `StackDepth.ForSelection(LoadedRecipe recipe, IReadOnlyList<StackPick> presentBottomFirst, string source = "the selection", Image<Rgba32>? finished = null) -> StackRise`
    - A null `Color`, or a Custom layer, uses the stored pixels (clone). Otherwise it uses `VariantPreview.Render(ingredient, variantId, color)`.
  - `StackDepth.ForAsset(LoadedRecipe recipe, GeneratedAsset asset, string source = "the asset") -> StackRise?`
    - Colors come from `asset.ColorRolls` by `LayerId`, and `finished = asset.Image`. Returns null for an all-absent roll.

- [ ] **Step 1: Write the failing tests** in `StackDepthTests.cs` (build stacks with `Art.FromAscii`, solid colors per layer, and `Art.Depth`):

```csharp
[Fact] public void An_overlay_with_back_zero_over_a_body_with_back_nine_makes_back_nine()
// body "##" depth (2,9); strap ".#" depth (4,0) above it
// rise.Depth at (1,0): Front 4, Back 9. At (0,0): Front 2, Back 9.

[Fact] public void The_back_face_samples_the_bodys_color()
// same stack: rise.Faces.Back[1,0] == bodyColor; rise.Faces.Front[1,0] == strapColor

[Fact] public void The_front_view_equals_the_art_when_the_topmost_layer_reaches_the_max_front()
// body (2,9), strap (4,0): for every solid pixel, Faces.Front[x,y] == Art[x,y]

[Fact] public void A_lower_protruding_layer_owns_the_front()
// body (5,9) under strap (1,0): Depth.Front(1,0) == 5; Faces.Front[1,0] == bodyColor; Art[1,0] == strapColor

[Fact] public void A_tie_goes_to_the_upper_layer()
// body (3,3), strap (3,3): Faces.Front and Faces.Back at the strap pixel == strapColor

[Fact] public void An_all_paper_stack_has_faces_equal_to_the_art()
// three layers, no maps: Depth all 0, Faces.Front == Faces.Back == Art pixel for pixel, BackColored 0

[Fact] public void When_the_front_owner_is_above_the_back_owner_the_seam_is_behind_the_front_owners_back()
// body (2,9), strap (4,3): BackColored(strap pixel) == 9 - 3 == 6

[Fact] public void When_the_back_owner_is_above_the_front_owner_the_seam_is_the_back_owners_interval()
// body (5,2) under strap (1,7): back owner strap; BackColored == 7 + 1 + 1 == 9 (strap's whole interval)

[Fact] public void A_middle_layer_never_owns_a_face()
// body (2,9), band (3,4), strap (5,0): Faces.Front == strap, Faces.Back == body

[Fact] public void A_finished_picture_supplies_the_topmost_owners_colors()
// pass finished whose strap pixel differs by 1 in R from the strap layer: Faces.Front == finished pixel

[Fact] public void A_finished_picture_whose_solid_mask_disagrees_is_refused()

[Fact] public void A_selection_colorizes_each_layer_with_its_rolled_color()
// Dynamic body under Custom strap; StackPick color (200, 0.8): Faces.Back[body pixel] ==
// VariantPreview.Render(body, id, new RolledColor(200, 0.8))[x,y]

[Fact] public void An_asset_rises_with_the_colors_it_rolled()
// Generator.Generate a 2-layer book with a Dynamic lower layer whose back is deeper;
// ForAsset(...).Faces.Back at a top-layer pixel == the colorized lower layer's pixel, and Art == asset.Image byte for byte
```

Keep the existing partial-alpha, empty-stack and size tests, asserting on `.Depth` where they read depth.

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Nfty.Model.Tests --filter FullyQualifiedName~StackDepthTests`
Expected: the build fails (no `StackRise`/`StackPick`).

- [ ] **Step 3: Implement** `StackFaces`, `StackRise`, `StackPick`, and the new `StackDepth` signatures above.
  - One pass per pixel over the solid layers, top-down. Track `maxF`, `maxB` and their topmost owners. `F above B` means a higher index. Seam = `F ≥ B ? maxB − back_F : maxB + 1 + front_B`.
  - Wrap the colorizing in `Perf.Measure("StackDepth.Colorize")`.
  - Dispose every colorized layer in `finally`.
  - Update the call sites listed under Files to compile, reading `.Depth` and disposing (`using`) the `StackRise`. No behavior beyond the union depth.

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/Nfty.Model.Tests` and `dotnet build nfty.sln`
Expected: all pass, 0 warnings.

- [ ] **Step 5: Probe.** Swap `max` to topmost-wins for back only. `An_overlay_with_back_zero…` must go red. Swap the tie rule to the lower layer. `A_tie_goes_to_the_upper_layer` must go red. Restore both.

- [ ] **Step 6: Commit**
`feat(model): stack rule is a union of solids; faces colored by their owner`

---

### Task 2: Faces into geometry, atlas texture, writer and preview

**Files:**
- Create: `src/Nfty.Model/FaceAtlas.cs`
- Modify: `src/Nfty.Model/MeshBuilder.cs`, `src/Nfty.Model/Riser.cs`, `src/Nfty.Model/ModelWriter.cs`, `src/Nfty.Model/ModelBatch.cs` (`ModelInput` gains `StackFaces? Faces = null`, passed to `TryBuild` + `Write` in both loops), `src/Nfty.Model/SetModeler.cs` (passes `rise.Faces`), `src/Nfty.App/Rise/RiseSubject.cs` (`Image<Rgba32> Texture`, the atlas built once from the art, owned), `src/Nfty.App/ViewModels/RiseViewModel.cs` (`Texture => Subject.Texture`)
- Test: `tests/Nfty.Model.Tests/RiserTests.cs`, `RiserSolidityTests.cs`, `GlbWriterTests.cs`, `GltfValidator.cs`, `RasterTests.cs`, `ExportStampIsolationTests.cs` (compare the atlas's front half to the unstamped asset), plus every test passing `art` to `Raster.Render` (pass `FaceAtlas.Build(art)`)

**Interfaces:**
- Consumes: `StackFaces`, `StackRise`, `StackDepth.ForAsset` (Task 1).
- Produces:
  - `public static class FaceAtlas`:
    - `static int WidthFor(int artWidth)` returns `2*artWidth + 1`.
    - `static Image<Rgba32> Build(Image<Rgba32> art, StackFaces? faces = null)`: the caller owns the result. With null faces, both halves are the art.
  - `Riser.TryBuild(Image<Rgba32> art, DepthMap? depth, RiseOptions options, string source = "the art", StackFaces? faces = null) -> Mesh?`
    - Faces of another size throw `ArgumentException`. The seam is clamped to `[0, 1+front+back]`.
    - `Mesh.TextureWidth == FaceAtlas.WidthFor(art.Width)`.
  - `ModelWriter.Write(Mesh mesh, Image<Rgba32> art, RiseOptions options, string baseName, StackFaces? faces = null)` builds the atlas itself. It refuses art whose `WidthFor(art.Width)`/height disagree with the mesh.
  - `Raster.Render(mesh, texture, …)` is unchanged. `texture` is now the atlas.
  - `ModelInput(string BaseName, Image<Rgba32> Art, DepthMap? Depth, StackFaces? Faces = null)`.
  - `RiseSubject.Texture`: the atlas.

UV rule, the one place it lives (`MeshBuilder`, constants from `FaceAtlas`), with `A = 2W+1`:
  - Front faces: `u = x / A`, for x ∈ [0, W].
  - Back faces: `u = (W + 1 + x) / A`.
  - `v = row / H` as today.
  - Wall pixel centers: front `(x + 0.5) / A`, back `(W + 1 + x + 0.5) / A`.
  - `MeshBuilder.Wall` gains a `bool backColor` parameter.
  - In `Riser.EmitWalls`, the seam z for pixel i is `zs = -(0.5 + back[i]) + backColored[i]`. Each segment `[z0, z1]` emits `[z0, min(z1, zs)]` with the back color and `[max(z0, zs), z1]` with the front color, skipping any empty part.

- [ ] **Step 1: Write the failing tests**

```csharp
// RiserSolidityTests
[Fact] public void The_mesh_volume_is_the_sum_of_one_plus_max_front_plus_max_back()
// rise a Combine of body (2,9) + strap (4,0) over a plus shape with faces; MeshChecks.Volume == Σ(1+F+B) of rise.Depth; AssertClosed; AssertNormalsMatchWinding
[Fact] public void A_wall_crossing_the_seam_on_both_segments_stays_closed()
// neighbor inside both ends, seam inside both segments; closed + exact volume
[Fact] public void A_single_picture_has_the_same_quad_count_as_before()
// pinned count for the existing "L" + "both" pattern fixture (read the number off main before the change)

// RiserTests
[Fact] public void Front_faces_sample_the_front_half_and_back_faces_the_back_half()
// every +Z vertex u ≤ W/A; every −Z vertex u ≥ (W+1)/A
[Fact] public void A_wall_takes_the_back_color_below_the_seam_and_the_front_color_above_it()
// body (2,9) + strap (4,0) one pixel; strap pixel's outside walls: quads with z1 ≤ -0.5 sample back half, z0 ≥ -0.5 front half

// GlbWriterTests
[Fact] public void The_glb_is_byte_stable()
// same stack written twice, and written at 1 and 8 threads via SetModeler: identical bytes
[Fact] public void The_texture_is_the_face_atlas()
// decode ReadImage: width 2W+1; left half == Faces.Front; right half == Faces.Back
[Fact] public void The_gutter_repeats_the_front_halfs_last_column()

// RasterTests
[Fact] public void A_front_view_render_never_samples_the_back_half()
// art with a unique back color absent from the front; front-camera render contains no pixel of it
```

`GltfValidator.AssertValid` gains three checks:
  - every `TEXCOORD_0` value lies in `[0, 1]`;
  - the embedded PNG decodes to an odd width `2k+1`;
  - column k equals column k−1.

`The_validator_is_not_vacuous` plants a UV of 1.5 and a broken gutter.

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Nfty.Model.Tests --filter "FullyQualifiedName~Riser|FullyQualifiedName~GlbWriter|FullyQualifiedName~Raster"`
Expected: build errors or failures on the new names.

- [ ] **Step 3: Implement** `FaceAtlas`, the UV rule, the seam split, the writer atlas, `ModelInput.Faces`, `SetModeler` (`using var rise = StackDepth.ForAsset(…)`; `TryBuild(asset.Image, rise?.Depth, …, rise?.Faces)`; `Write(…, rise?.Faces)`), and `RiseSubject.Texture`. Update every test that passes art to `Raster.Render`.

- [ ] **Step 4: Measure Q2.** Run `RisePerfTests` before and after with `-l "console;verbosity=detailed"`, and record the 512-px GLB size in the task report. Stop and report if it grew by more than 10%.

- [ ] **Step 5: Run tests**

Run: `dotnet test tests/Nfty.Model.Tests`, `dotnet test tests/Nfty.Cli.Tests --filter FullyQualifiedName~Model`, and `dotnet build nfty.sln`
Expected: all pass.

- [ ] **Step 6: Probe.** Drop the gutter (atlas `2W`). `The_gutter_…` and the Raster edge test must go red. Make walls ignore the seam. `A_wall_takes_the_back_color…` must go red. Restore both.

- [ ] **Step 7: Commit**
`feat(model): atlas texture with front and back halves; walls split at the owner seam`

---

### Task 3: The Rise page shows and exports owner colors

**Files:**
- Modify: `src/Nfty.App/Rise/RiseSubject.cs` (ctor gains `StackFaces? faces = null`, owned; `Texture = FaceAtlas.Build(art, faces)`; `Faces` property), `src/Nfty.App/Rise/RecipeRiseSource.cs` (`Rolled` carries `StackFaces?` detached from `ForAsset`'s `StackRise`), `src/Nfty.App/Rise/SetRiseSource.cs` (`Resolve` builds picks with `StackPick.WithColors(present, item.Layers)` and `finished = art`, returning the depth and the detached faces), `src/Nfty.App/Rise/RiseExportSnapshot.cs` (entries carry faces: `Frozen` clones them, `Release` disposes them), `src/Nfty.App/ViewModels/RiseViewModel.cs` (`BuildMesh` passes `Subject.Faces`)
- Test: `tests/Nfty.App.Tests/RecipeRiseSourceTests.cs`, `SetRiseSourceTests.cs`, `RiseExportTests.cs`

**Interfaces:**
- Consumes: `StackDepth.ForAsset`, `StackPick.WithColors`, `StackRise.DetachFaces`, `FaceAtlas.Build`, `ModelInput.Faces` (Tasks 1–2).
- Produces: `RiseSubject(Image<Rgba32> art, DepthMap? stored, bool ownsArt = true, StackFaces? faces = null)`, `RiseSubject.Faces`, `RiseSubject.Texture`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void A_recipe_source_thickness_rail_counts_union_thickness()
// recipe body (2,9) + strap (4,0): the THICKNESS rail lists every strap pixel at 1+4+9 = 14, none at 5
[Fact] public void A_recipe_source_preview_texture_carries_the_back_owner_color()
// vm.Texture right half at a strap pixel == body color
[Fact] public void A_set_asset_front_owner_colors_are_the_sets_own_pixels()
// SetRiseSource over a cooked Set with a found book: Subject.Faces.Front == the Set PNG wherever the top layer owns the front
[Fact] public void An_export_snapshot_owns_its_faces()
// freeze, reroll (disposes the old subject), then export: GLB atlas right half still == body color; after Release, faces disposed
[Fact] public void A_recipe_export_writes_the_back_owner_color()
// export a recipe item to GLB; decode texture; right half at strap pixel == body color
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Nfty.App.Tests --filter "FullyQualifiedName~RiseSource|FullyQualifiedName~RiseExport"`
Expected: FAIL.

- [ ] **Step 3: Implement** the wiring above. Variant and picture sources pass no faces; their atlas duplicates the art.

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/Nfty.App.Tests --filter "FullyQualifiedName~Rise"`, then `dotnet build nfty.sln`
Expected: all pass.

- [ ] **Step 5: Probe.** Make `Frozen` share faces instead of cloning. `An_export_snapshot_owns_its_faces` must go red. Restore.

- [ ] **Step 6: Commit**
`feat(rise): recipe and Set sources show and export owner-colored faces`

---

### Task 4: The demo chest reads whole from every angle (two-phase)

**Files:**
- Modify: `tools/demo/draw-chest-art.py` (back profile per Q5; `over(k)` loses `back`, so overlays use back 0; `lock_padlock_depth`, `gems_depth` back 0; `check_contract` gains the back rules below), `tests/Nfty.Model.Tests/DemoModelsTests.cs`, `tests/Nfty.App.Tests/VisualCapture.cs` (adds `demo-recipe-3d-back`: `SwapPreviewCommand`, then `OrbitStep(new Vector2(12, 0))`, which is 180° at 15° per step; reuses the `demo-recipe-3d-side` `Need` case)
- Rebuild (phase 2 only): `src/Nfty.Core/Demo/ChestDemo.cbk`

**Interfaces:**
- Consumes: `StackDepth.ForSelection(...) -> StackRise` (Task 1), `SetModeler` (Task 2).

- [ ] **Step 1: Write the failing tests** in `DemoModelsTests.cs` (wrap the temp folder in `try/finally` delete):

```csharp
[Fact] public void Overlays_never_change_the_body_back()
// every body × overlay × variant: with.Depth.Back(x,y) == without.Depth.Back(x,y) at every overlay pixel
[Fact] public void The_chest_lid_curves_on_its_back()
// chestbody, center column: Back at y=7..11 is non-decreasing, takes ≥ 3 distinct values; on each lid row both end pixels' Back < the center's
[Fact] public void The_lower_body_back_mirrors_its_front()
// for y ≥ 12: overhang rows' Back > face rows' Back at the same x; each row's outermost column Back < its neighbor's
[Fact] public void A_cooked_demo_asset_is_whole_from_behind()
// cook 40 assets; for each, ForAsset(...).Depth.Back at every overlay pixel ≥ 1 wherever a body pixel is solid there, and SetModeler mesh volume == Σ(1+F+B)
```

Keep `Every_overlay_stands_proud_of_every_body_on_both_recipes` (front contract), now iterating every feet variant (step 9 M4).

- [ ] **Step 2: Run them to see them fail** against the embedded (old) `.cbk`.

Run: `dotnet test tests/Nfty.Model.Tests --filter FullyQualifiedName~DemoModelsTests`
Expected: `The_chest_lid_curves_on_its_back` and `The_lower_body_back_mirrors_its_front` FAIL. The union rule alone already passes `Overlays_never_change_the_body_back`. That is fine: it pins the rule against regressions.

- [ ] **Step 3: Edit the generator** per Q5. `check_contract` asserts the same two shape rules plus "overlay back is 0".

- [ ] **Step 4: Phase 1, iterate without committing.** Run `python tools/demo/build-demo.py` to embed into the working tree, uncommitted.
  - Run the demo tests and `DemoCookBookTests`. Expect 615,600 unchanged, with `Depth_never_touches_the_collection` green.
  - Capture `NFTY_CAPTURE=1 NFTY_CAPTURE_DIR=.superpowers/frames-step9b dotnet test tests/Nfty.App.Tests --filter "FullyQualifiedName~VisualCapture&FullyQualifiedName~Rise"`. The frames needed are `demo-recipe-3d` (front), `demo-recipe-3d-side` and `demo-recipe-3d-back`, plus `demo-recipe`, in both themes.
  - Look at them. Retune and repeat until the chest reads whole from front, side and back.

- [ ] **Step 5: STOP.** Report the frame paths to the controller. The author reviews the frames personally. Do not commit the `.cbk` until the author says yes. A "no" means go back to Step 3.

- [ ] **Step 6: Phase 2, after the author's yes.**
  - Run `python tools/demo/build-demo.py --check`. Expected: "demo is up to date" against the working-tree embed.
  - Run `dotnet test tests/Nfty.Model.Tests tests/Nfty.Core.Tests --filter "FullyQualifiedName~Demo"`. Expected: PASS.
  - Commit once: the generator, the tests, the capture case and `ChestDemo.cbk` together.
  - `feat(demo): chest back shaped; overlays carry no back under the union rule`

---

### Task 5: Docs and close-out

**Files:**
- Modify: `CLAUDE.md` — the **`Nfty.Model`** paragraph and the **Demo** paragraph
- Modify: `docs/manual/get-started/the-demo.md` (line 45), plus any Rise manual page that states topmost-wins (`grep -rn "topmost" docs/manual`)

- [ ] **Step 1: CLAUDE.md, `Nfty.Model` paragraph.**
  - Replace "solid pixel takes depth of TOPMOST layer solid there (no map = paper), clear top defers" with the union rule: front/back = max over solid layers; upper layer owns overlapping voxels; front/back face owner = topmost reaching the max, ties to the upper; topmost owner takes the finished art's pixel; `StackRise`/`StackFaces`/`StackPick`; colors are the rolled ones (`ColorRolls`, Set `LayerColor`).
  - Replace "Texture = WHOLE art re-encoded" with the `FaceAtlas` `[front W][gutter 1][back W]` rule and why the gutter exists. Keep the encoder sentence.
  - Add the wall-seam sentence (two owners, middle layers absorbed) and "Sets cooked before 2026-10-26 keep old models".
- [ ] **Step 2: CLAUDE.md, Demo paragraph.**
  - "Overlay depth contract" becomes FRONT-only: overlays state absolute `body_front + k`. Under the union rule an overlay's back is 0 and cannot carve. The body back is shaped: lower rows mirror the front, and the chest lid curves on both sides.
  - Name the new pins: `Overlays_never_change_the_body_back`, `The_chest_lid_curves_on_its_back`.
- [ ] **Step 3: Manual.** In `the-demo.md`, write that the lid curves front and back and the chest reads as one object from any side. Fix any topmost-wins sentence found by the grep.
- [ ] **Step 4: Full suite, once** (per rise test cadence).

Run: `dotnet build nfty.sln` then `dotnet test nfty.sln`
Expected: 0 warnings; every project green. Report any failure; do not mask a flake.

- [ ] **Step 5: Commit**
`docs: union stack rule and the shaped demo back in CLAUDE.md and the manual`
