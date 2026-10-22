# Rise Step 9: Demo Rebuilt With Depth Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The built-in Chest Demo ships with hand-designed per-pixel depth, so a rolled chest rises into a convincing 3D object, and the author gets a re-cooked Set with models to explore.

**Architecture:** Today no authoring command writes a `.dep` (`new ingredient` passes `DepthMaps.None`), so Task 1 adds the smallest honest surface: `nfty set depth <igt> --variant <id> --map <png>`, which writes through `IngredientArchive.Write(..., depths)`, the same writer the Rise page's save uses. `tools/demo/draw-chest-art.py` then draws a depth picture beside each sprite, `build-demo.py` applies them through that command, and the committed `ChestDemo.cbk` is rebuilt **once**. Depth never enters DNA, so the 615,600 count, the frozen roll baseline and every existing demo test stay green untouched; new tests pin voxel-readiness, "overlays stand proud", non-flat models, and that stripping the depth changes no DNA and no pixel.

**Tech Stack:** .NET 10, xunit.v3, ImageSharp 3.1.11 (pinned), `System.CommandLine`, Python + PIL for the generator only (the build never needs it), Avalonia 12.1.1 headless for frames.

**Spec:** `docs/superpowers/specs/2026-09-25-rise-design.md` ("Build sequence" item 9; "Format" for `depth/{variantId}.dep`; "Testing").

## Global Constraints

- `Directory.Build.props`: `TreatWarningsAsErrors` + `GenerateDocumentationFile`; every public member documented, a warning fails the build.
- ImageSharp pinned **3.1.11**; no new packages anywhere (`Nfty.Core` gains none; `Nfty.Model` stays zero-package).
- US English everywhere product speaks (color, gray, center, canceled, behavior).
- Tests are xunit.v3, named `Snake_case_sentences`. Tests build fixtures in memory (1x1 / 2x2 / tiny ASCII art); **the demo tests are the one exception** and read the EMBEDDED bytes via `DemoCookBook`, never `tools/demo` or a file beside the test. Filesystem tests use `Directory.CreateTempSubdirectory()`.
- Demo rules from CLAUDE.md that stay binding: the `.cbk` is generated and committed; the generator is the source and the build must not need Python; **do not re-run the build casually** (zip entry timestamps change every write, so a rebuild is an unreviewable binary diff); `UniqueSpace` counts **615,600** exactly; every overlay is authored at `y >= 12`; Feet stays Custom for its arithmetic reason; `DemoCookBook.WriteTo` does not overwrite by default; no layer or variant is added or removed (that would move the count).
- Format (schema 2, floor): a variant with no `depth/{variantId}.dep` is flat paper. Writers zero depth over non-solid pixels and write no entry for an all-zero map. `partialAlpha` is a listing cache stamped by every writer; nothing here authors it.
- `Nfty.Core`/`Nfty.Ticket` never reference `Nfty.Model`. Anything that needs a mesh or a GLB lives in `Nfty.Model.Tests`, which can read the embedded demo through `Nfty.Core`.
- Depth is the stored `depth/*.dep` (front/back byte per pixel, 0..255 voxels). It is NOT `LayerDepth` (`layerOrder` position). Say "depth map" in new prose and code comments.
- Never commit cooked output. The explore Set goes under the gitignored `.superpowers/demo-cook/`.
- Commit messages end with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.

## Open Questions (controller rules; recommendation first)

- **Q1: Depth picture format for `--map`.** Recommend a PNG the same size as the variant: **R = front, G = back, B and A ignored**. It is viewable, authorable in PIL with no new format, and diffable in review. Alternative: raw `.dep` bytes (unreadable by humans).
- **Q2: Does the manual reshoot belong in step 9?** Recommend **no, it stays in BACKLOG.md**. The author deferred it as one pass tied to the manual rework; half-reshot pages would show two collections. Step 9 supplies its stated prerequisite (a depth-carrying demo) and the Rise frames for the author's gallery; Task 4 edits the BACKLOG entry to say the prerequisite is met and exactly what is left (including the Rise pages' figure list), nothing more.
- **Q3: Glow stays flat paper.** Recommend yes: sparks and runes are floating motes, one voxel thick reads right, and it keeps the depth files to the six layers that have volume. The test pins "Glow carries no depth map" so it is a decision, not an omission.
- **Q4: Back depth of the body.** Recommend a **uniform back of 6** (a chest ~8 voxels deep, 26 wide) so the side view reads as a box and not a plaque. The author can retune; it is one constant in the generator.
- **Q5: Look-iteration rule vs "plan exactly one rebuild commit".** Recommend: iterate freely with `--no-embed` into `.demo/` and with *uncommitted* embeds in the working tree; **commit the `.cbk` once, after Task 2's frame review passes**. If the author rejects the look at Task 3, the fix is a second, named rebuild commit; that is the author's call, not a defect of the plan.
- **Q6: Rise frames gallery artifact.** Recommend the controller refreshes it from `.superpowers/frames-step9/` (latest-only, replacing the old frames), as steps 7 and 8 did; the executor only produces the PNGs.
- **Q7: `build-demo.py --check`.** Recommend adding it (Task 2): builds into the scratch folder and compares every archive entry's decoded content, recursively, against the embedded `.cbk`, writing nothing. It is the sanctioned replacement for "re-run it to see if it drifted", and it keeps the committed binary honest without a binary diff.

## Review Focus

The inputs the spec implies but no happy-path task exercises, most likely to bite first:

1. **A depth map the wrong size** (or a picture that is not a PNG). Expect a named refusal and the `.igt` left byte-for-byte as it was. Pinned in Task 1.
2. **Depth painted over transparent pixels** (a generator slip). Expect the writer's existing zeroing, so the shipped demo cannot carry depth where nothing is solid. Pinned in Task 1 (command) and Task 2 (embedded demo).
3. **An overlay buried inside its body.** Topmost solid layer wins per pixel (`StackDepth.Combine`), so a band whose depth is *lower* than the body under it makes the band sink into the chest. Expect every overlay pixel strictly in front of every body variant under it, on both recipes. Pinned in Task 2.
4. **An absent layer.** Bands/trim/glow are optional at 55-72 percent; a rolled chest with none of them must still rise (body-only depth, no exception, nothing to stand proud). Pinned in Task 2.
5. **The depth map silently changing the collection.** Same seed over the demo with its depth stripped must give identical DNA and identical pixels. Pinned in Task 2.

## File Structure

| File | Responsibility |
|---|---|
| `src/Nfty.Core/Editing/DepthPicture.cs` (new) | `DepthPicture.ToMap(Image<Rgba32>)`: the one reading of the depth-picture format (R front, G back). |
| `src/Nfty.Cli/CommandFactory.Authoring.cs` (modify) | `SetDepth()` under the existing `set` group. |
| `tools/demo/draw-chest-art.py` (modify) | Depth design: writes `<out>/depth/<ingredient>/<variant>.png` beside the sprites; asserts its own invariants. |
| `tools/demo/build-demo.py` (modify) | Applies each depth picture with `nfty set depth`; adds `--check`. |
| `tools/demo/contact-sheet.py` (modify) | Optional third argument draws the depth pictures as a heat sheet for review. |
| `src/Nfty.Core/Demo/ChestDemo.cbk` (rebuilt once) | The shipped binary. |
| `tests/Nfty.Core.Tests/DepthPictureTests.cs`, `DemoDepthTests.cs` (new) | Format reader; demo is voxel-ready, depth never touches DNA. |
| `tests/Nfty.Cli.Tests/SetDepthCommandTests.cs` (new) | The command, end to end through `new recipe`. |
| `tests/Nfty.Model.Tests/DemoModelsTests.cs` (new) | Overlays stand proud; models non-flat; GLB extent. |
| `tests/Nfty.App.Tests/RisePages.cs`, `RiseFixtures.cs`, `VisualCapture.cs` (modify) | Rise page over the demo, three captured states. |
| `docs/manual/...`, `CLAUDE.md`, `BACKLOG.md` (modify) | Task 4. |

---

### Task 1: `nfty set depth`, the smallest authoring surface

**Files:**
- Create: `src/Nfty.Core/Editing/DepthPicture.cs`
- Modify: `src/Nfty.Cli/CommandFactory.Authoring.cs` (`SetGroup()` at ~line 235 adds `SetDepth()`; reuse `WriteReplacing` and the `set chance` shape)
- Test: `tests/Nfty.Core.Tests/DepthPictureTests.cs`, `tests/Nfty.Cli.Tests/SetDepthCommandTests.cs`

**Interfaces:**
- Consumes: `DepthMap.FromBytes(int width, int height, ReadOnlySpan<byte>)` and `DepthMaps.None` (`Nfty.Core.Model`); `IngredientArchive.Read(string)` -> `LoadedIngredient` (`Manifest`, `VariantImages`, `Depths`); `IngredientArchive.Write(string path, IngredientManifest, IReadOnlyDictionary<string, Image<Rgba32>>, IReadOnlyDictionary<string, DepthMap>)`.
- Produces: `public static class DepthPicture { public static DepthMap ToMap(Image<Rgba32> picture); }` and the CLI form `nfty set depth <igt> --variant <id> --map <png>`. Task 2's `build-demo.py` calls the CLI form.

- [ ] **Step 1: Write the failing tests.**
  - `DepthPictureTests` (Core): `Red_is_front_and_green_is_back` builds a 2x2 picture with pixel (1,0) = `new Rgba32(7, 9, 200, 13)` and asserts `map.Front(1,0) == 7`, `map.Back(1,0) == 9`, other pixels 0, `map.Width == 2`; `Blue_and_alpha_are_ignored` asserts two pictures differing only in B/A give equal `ToBytes()`.
  - `SetDepthCommandTests` (Cli, same `Run` helper as `DemoCommandTests`): build a 2x2 Custom `.igt` in a temp dir with `IngredientArchive.Write` (all four pixels opaque, variant `v`), a 2x2 depth PNG with (0,0) = R 3, G 5, then:
    - `Set_depth_stores_the_map_and_reads_back` -> exit 0; `IngredientArchive.Read` gives `Depths["v"].Front(0,0) == 3`, `Back(0,0) == 5`.
    - `Set_depth_survives_new_recipe` -> after the command, `new recipe` over a folder holding that `.igt` (manifest + `--ingredients`) and a read of the `.rcp` still shows front 3 (proves the build chain carries depth).
    - `A_map_of_the_wrong_size_is_refused_and_the_igt_is_untouched` -> a 3x3 PNG throws/returns non-zero with a message containing `2x2` and `3x3`; file bytes before == after.
    - `An_unknown_variant_names_the_ingredients_variants` -> message contains `v`.
    - `Depth_over_a_clear_pixel_is_not_stored` -> variant with pixel (1,1) alpha 0 and map (1,1) = R 9; after the command `Depths["v"].Front(1,1) == 0`.
    - `An_all_zero_map_clears_the_depth_and_says_so` -> `Depths` has no `v`; stdout contains `cleared`.
    - `Set_depth_twice_with_the_same_map_stores_identical_depth_bytes` -> `Depths["v"].ToBytes()` equal across two runs.
- [ ] **Step 2: Run to verify red.** `dotnet test tests/Nfty.Core.Tests --filter FullyQualifiedName~DepthPictureTests` and `dotnet test tests/Nfty.Cli.Tests --filter FullyQualifiedName~SetDepthCommandTests`. Expected: compile errors / FAIL (type and command do not exist).
- [ ] **Step 3: Implement `DepthPicture.ToMap(Image<Rgba32> picture) -> DepthMap`** in `Nfty.Core.Editing`, fully documented; loop with `ProcessPixelRows`, row-major top first, writing `[R, G]` per pixel into a byte array, then `DepthMap.FromBytes`.
- [ ] **Step 4: Implement `SetDepth()`** beside `SetChance()` and add it in `SetGroup()`. Arguments: `igt` (Argument), `--variant` (required), `--map` (required PNG path). Flow: read the `.igt`; unknown variant -> `InvalidOperationException` listing the real variant ids (same tone as `set chance`'s `--id` message); load the picture with `Image.Load<Rgba32>`; size mismatch -> `InvalidOperationException` naming both sizes (`"--map is 3x3; variant 'v' is 2x2"`) **before** any write; `DepthPicture.ToMap`; copy `existing.Depths` into a new dictionary and set or remove (when the map is all zero after the writer's own zeroing: check by calling the same rule via writing and re-reading is unnecessary, compute `IsFlat` on a transparent-zeroed copy; simplest is to write, re-read, and print `cleared` when no entry came back) the variant's entry; `WriteReplacing(path, p => IngredientArchive.Write(p, existing.Manifest, existing.VariantImages, depths))`. Print `Variant 'v' of <path> now carries depth: N solid pixels, front up to F, back up to B` or `Depth of variant 'v' cleared in <path>` (counts read back from the re-read map, so the line states what was stored, not what was asked). Dispose the loaded picture in `finally`.
- [ ] **Step 5: Run to verify green**, same two commands. Then `dotnet build nfty.sln` (docs warnings are errors).
- [ ] **Step 6: Commit.**

```bash
git add src/Nfty.Core/Editing/DepthPicture.cs src/Nfty.Cli/CommandFactory.Authoring.cs tests/Nfty.Core.Tests/DepthPictureTests.cs tests/Nfty.Cli.Tests/SetDepthCommandTests.cs
git commit -m "feat(cli): set depth writes a variant's depth map from a picture"
```

---

### Task 2: The demo gains depth (the one rebuild)

**Files:**
- Modify: `tools/demo/draw-chest-art.py`, `tools/demo/build-demo.py`, `tools/demo/contact-sheet.py`
- Rebuild once: `src/Nfty.Core/Demo/ChestDemo.cbk`
- Test: `tests/Nfty.Core.Tests/DemoDepthTests.cs`, `tests/Nfty.Model.Tests/DemoModelsTests.cs`
- Modify (frame review tool): `tests/Nfty.App.Tests/RiseFixtures.cs`, `RisePages.cs`, `VisualCapture.cs`

**Interfaces:**
- Consumes: Task 1's `nfty set depth`; `DemoCookBook.WriteTo(string dir)` (returns the `.cbk` path) and `CookBookArchive.Read(path)`; `VoxelReport.Scan(LoadedRecipe)` rows with `.IsClean` / `.Partial`; `StackDepth.ForSelection(LoadedRecipe, IReadOnlyList<(string IngredientId, string VariantId)>)` -> `DepthMap` and `StackDepth.Combine`; `SetModeler(LoadedCookBook, RiseOptions)` and `.Model(GeneratedAsset, string baseName)` -> `IReadOnlyList<SetFile>?` (bare file name + bytes); test-side `GltfValidator.ReadGlb(byte[])` / `ReadFloats(JsonNode root, byte[] bin, int accessor)`.
- Produces: depth for every variant of `chestbody`, `boxbody`, `bands`, `lock`, `trim`, `feet`; none for `glow`. Task 3 and 4 use the rebuilt book.

**The depth design (a draft Claude hand-designs, tuned on frames; acceptance rules are what the tests pin, the numbers are the starting point).** Units are voxels; paper is 0/0 (one voxel thick). Topmost solid layer wins per pixel, so overlay depths are **absolute, not relative**: each overlay pixel is `body_front(x, y) + k`.

- **The depth overlay contract (twin of the `y >= 12` art contract).** At `y >= 12` every body variant on both recipes shares ONE front profile `BODY_FRONT_LOWER(x, y)`; variants may differ only above row 12. Overlays are therefore proud of every body by construction, on both recipes.
- **Body back:** a uniform `BODY_BACK = 6` on every solid body pixel (Q4).
- **Lower body (y 12..28):** front 2 across the face, 1 on the outermost column each side (a one-voxel bevel), plinth row 28 front 3 (it overhangs), the lid overhang rows 14-15 front 3.
- **Chest lid (y 7..11), domed:** the lid is a barrel, so it curves *away* going up: front 3 at row 11, 2 at rows 9-10, 1 at rows 7-8, and falls off to 0 within three pixels of the left/right ends (rounded ends). Highlight rows keep the same profile; texture differences between planked/plated/stone are *only* in the art, plated alone adds +1 front on its rivet pixels (rows < 12 only).
- **Strongbox lid (y 8..11):** a flat slab: front 3 with a one-voxel bevel on the edge columns.
- **Bands:** `body_front + 1` on every solid pixel (straps and hoops stand proud of the face), back 0.
- **Lock:** `body_front + 2` (the plate is the most proud thing on the chest), back 0; padlock shackle (its upper arc) `+3`.
- **Trim:** gilt `body_front + 1`; gems `body_front + 2` (they are set stones).
- **Feet** (rows 29-30, below the body, so no body under them): blocks front 1 back 1; skids front 0 back 5 (a runner that runs back under the chest).
- **Glow:** no depth map (Q3).

- [ ] **Step 1: Write the failing tests (they read the embedded bytes, so they fail against the old demo).**
  - `DemoDepthTests` (Core, same `Read()` helper as `DemoCookBookTests`):
    - `Every_demo_variant_is_voxel_ready`: for each recipe `VoxelReport.Scan(recipe)` has no row with `Partial > 0`, and `book.Manifest.PartialAlpha` is false after the read (the listing cache is recomputed by the reader). Fails today? It passes today (the art is already opaque); it is a regression pin for the new art and the brief's requirement. Say so in the test's comment.
    - `The_demo_carries_depth_on_every_layer_that_has_volume`: for `chestbody`, `boxbody`, `bands`, `lock`, `trim`, `feet`, every variant has `Depths[id]` with `IsFlat == false`; for `glow`, `Depths` is empty.
    - `Depth_exists_only_where_something_is_solid`: for every variant, every pixel with a nonzero front or back is `Opacity.IsSolid` in that variant's image.
    - `Depth_never_touches_the_collection`: load the book; build a twin whose every `LoadedIngredient` has `Depths = DepthMaps.None` (new `LoadedIngredient` with the same `Manifest` and `VariantImages` references); `Generator.Generate` both with `new GenerateOptions(Count: 60, Seed: "depth-is-not-dna")`; assert the DNA lists are equal in order and the pixel bytes of every asset (`Image` -> raw bytes) are equal. Disposal: the twin must not dispose the shared images twice (dispose only the original).
    - The existing `The_demo_dna_space_is_counted_exactly` (615,600), `TicketFreezeTests` and `MenuFidelityTests` run **unmodified**; they are the proof the count and the roll baseline survived (Step 9 below runs them).
  - `DemoModelsTests` (Model.Tests; `using Nfty.Core.Demo`):
    - `Every_overlay_stands_proud_of_every_body_on_both_recipes`: for recipe in `chest`/`strongbox`, for each body variant (the recipe's body ingredient) x each of `bands`/`lock`/`trim` variant: `StackDepth.ForSelection` with (glow absent) selection `{body, overlay}` plus `feet` blocks; compare against the selection `{body}` + feet alone. At every pixel where the overlay variant is solid, assert `with.Front(x,y) > without.Front(x,y)` (strictly proud). This is the cross product, ~3x(3+4+2)+2x(3+4+2) combinations, still milliseconds.
    - `A_body_alone_still_rises`: for every body variant, selection `{body}` (nothing optional present) builds `Riser.TryBuild(art, depth, new RiseOptions(), "x")` non-null (absent-layer case).
    - `A_cooked_demo_asset_is_not_flat`: cook 40 assets (seed `"explore"`, `EnforceUniqueDna: false` is NOT used; use the default), `new SetModeler(book, new RiseOptions { Format = ModelFormat.Glb })`; for each asset read the GLB with `GltfValidator.ReadGlb`, read the `POSITION` accessor min/max from the JSON, assert the Z extent is `>= 9` voxels (body back 6 + front 2 + paper 1) for every non-skipped asset, and that at least one asset's Z extent is strictly greater than its body-only floor (some overlay rose proud). Assert `Skipped` is empty (every demo asset has a solid pixel).
    - `The_demo_cooks_with_models_through_the_cli_seam`: skip; the Cli's `generate --models` is covered by step 8; this is exercised by hand in Task 3.
- [ ] **Step 2: Run to verify red.** `dotnet test tests/Nfty.Core.Tests --filter FullyQualifiedName~DemoDepthTests` and `dotnet test tests/Nfty.Model.Tests --filter FullyQualifiedName~DemoModelsTests`. Expected: the voxel-ready test PASSES (pin), the depth-presence, proud and extent tests FAIL, `Depth_never_touches_the_collection` passes vacuously today (both sides depth-free); it earns its keep once Step 7 lands, and the plan probes that in Step 9.
- [ ] **Step 3: Implement the depth section in `draw-chest-art.py`.** Add `DEPTH = {layer: {variant: lambda: ...}}` mirroring `SPEC`, each returning an `(W, H)` RGB picture (R front, G back) built from the SAME masks the art uses: derive solid pixels from the just-drawn sprite (`px[x, y][3] != 0`) so depth can never land on a transparent pixel. Share one `body_front(x, y, lid)` function that returns the profile above; overlays call it and add their `k`. `main()` writes `<out>/depth/<layer>/<variant>.png` for every layer except `glow`. The script **asserts before it saves**: depth only on solid pixels; every value `0..255`; for each overlay pixel `value > body_front(x, y, lid)` for both lids across all y >= 12 (the python twin of the C# proud test, so a bad edit fails at generation, not after a rebuild).
- [ ] **Step 4: Extend `contact-sheet.py`** with an optional third argument: the depth directory; it draws each variant's front as a ramp (0 dark -> brightest) with the back drawn as a blue-channel ramp, nearest-neighbour, same layout, to `.demo/depth-sheet.png`. Review only; nothing ships from it.
- [ ] **Step 5: Extend `build-demo.py`.** After each `new ingredient`, for every `<art>/depth/<id>/<variant>.png` run `run(["set", "depth", igt, "--variant", variant, "--map", png])` (sorted, so order is stable). Add `--check`: build into the work folder, then compare to `EMBEDDED` by unzipping both **recursively** (a `.cbk` nests `.rcp` nests `.igt`) and comparing every entry's name and bytes, ignoring zip timestamps; print the first differing entry and exit 1, or `demo is up to date` and exit 0; `--check` never writes `EMBEDDED` and conflicts with `--workspace`/embedding (argparse error). Update the module docstring: the book now carries depth (one paragraph, same voice), and the rebuild rule points at `--check`.
- [ ] **Step 6: Iterate the look without touching the shipped bytes.** `python tools/demo/build-demo.py --no-embed`, then `python tools/demo/draw-chest-art.py .demo/art` and `python tools/demo/contact-sheet.py .demo/art .demo/sheet.png .demo/art/depth`; open the depth sheet and look. For each of the two recipes run `nfty inspect .demo/ChestDemo.cbk --voxel` (expect no partial alpha) and `nfty model` on a body: `dotnet run --project src/Nfty.Cli -- model .demo/igt/chestbody.igt --variant planked --out .demo/planked.glb`. Adjust the numbers; all edits are in the generator.
- [ ] **Step 7: The rebuild.** Exactly once, when the sheet looks right: `python tools/demo/build-demo.py` (embeds into `src/Nfty.Core/Demo/ChestDemo.cbk`), then `python tools/demo/build-demo.py --check` (expect `demo is up to date`). Do NOT run the embedding build again; if something must change, use `--no-embed` iteration and re-embed once more before the commit (the working tree may be rebuilt freely; only the committed binary counts).
- [ ] **Step 8: Frame-review tool (test code only).** In `RiseFixtures.cs` add `internal static VariantRiseSource DemoVariant(CookBookSession s, string recipeId, string ingredientId, string variantId, Func<bool> isEditing)` (the existing `Variant` helper hard-codes `chest`/`body`); in `RisePages.cs` add `DemoVariantVm()` (chest recipe, `chestbody`, `planked`), `DemoRecipeVm()` (`RecipeRiseSource` over the demo's chest recipe, roll 1) and `DemoSetVm()` (60 assets cooked with `Generator.Generate` from `GenerateOptions(60, "explore")`, written with `SetWriter.Write(set, dir, pack: false)` beside the book, opened as a `SetRiseSource`; return the `(Vm, Owner)` pair like `SetVm`). All three open the book via `DemoCookBook.WriteTo(tempDir)` -> `CookBookArchive.Read` -> `CookBookSession.Open`, mirroring `RiseFixtures.OnDisk`. In `VisualCapture.Capture_rise_page` add states `demo-variant`, `demo-recipe`, `demo-set` and in `VerifyRiseState` one `Need` per state that reads the page: `demo-variant` -> `vm.Surface` has a nonzero front or back somewhere solid and `labels.LastLabelCount > 0`; `demo-recipe` -> the rail lists the recipe's rolled layers and `model.RepaintCount > 0`; `demo-set` -> `vm.Source` is the Set source and the 3D tile painted. Frames are named `rise-demo-*-{light,dark}[-min].png` by the existing loop.
- [ ] **Step 9: Verify, then look.**
  1. `dotnet test tests/Nfty.Core.Tests --filter "FullyQualifiedName~DemoCookBookTests|FullyQualifiedName~DemoDepthTests|FullyQualifiedName~TicketFreezeTests|FullyQualifiedName~MenuFidelityTests"` and `dotnet test tests/Nfty.Model.Tests --filter FullyQualifiedName~DemoModelsTests` -> all PASS (the 615,600 assertion and the frozen roll baseline untouched and green).
  2. **Probe `Depth_never_touches_the_collection`:** in the test, temporarily nudge one pixel of one variant image in the stripped twin only; the test must go red. Revert.
  3. **Probe the proud test:** on a scratch build (`python tools/demo/build-demo.py --work .demo-probe`, which embeds into the working tree) set the lock's `k` to 0 in the generator; `Every_overlay_stands_proud_of_every_body_on_both_recipes` must go red. Restore `k`, rebuild once more (working tree only), `--check` green. Nothing is committed until Step 10.
  4. Capture: `NFTY_CAPTURE=1 NFTY_CAPTURE_DIR=.superpowers/frames-step9 dotnet test tests/Nfty.App.Tests --filter "FullyQualifiedName~VisualCapture.Capture_rise_page"`; LOOK at `rise-demo-variant`, `rise-demo-recipe`, `rise-demo-set` in both themes at 1180x720 and `-min`: the lid reads domed, the bands and lock stand off the face, nothing floats, the side view is a box. Text must fit and the 3D tile must not be blank. Fix the generator and return to Step 6 if not.
  5. Full pass: `dotnet test nfty.sln`. Expected: green.
- [ ] **Step 10: Commit once (generator, tests, review tool and the rebuilt binary together).**

```bash
git add tools/demo src/Nfty.Core/Demo/ChestDemo.cbk tests/Nfty.Core.Tests/DemoDepthTests.cs tests/Nfty.Model.Tests/DemoModelsTests.cs tests/Nfty.App.Tests
git commit -m "feat(demo): the Chest Demo carries hand-designed depth"
```

---

### Task 3: Re-cook for the author, final frames

**Files:** none tracked (outputs are gitignored: `.superpowers/demo-cook/`, `.superpowers/frames-step9/`). If the frames expose a capture-only defect, fix in `tests/Nfty.App.Tests` and commit `test(app): ...` separately.

**Interfaces:** Consumes Task 2's embedded book and `nfty demo`, `nfty generate --models`, `nfty inspect`.

- [ ] **Step 1: Write the book out from the embedded bytes** (so the Set's `cookbookSha256` is the shipped archive's): `dotnet run --project src/Nfty.Cli -- demo .superpowers/demo-cook --force`.
- [ ] **Step 2: Cook with models.** `dotnet run --project src/Nfty.Cli -- generate .superpowers/demo-cook/ChestDemo.cbk --count 200 --seed explore --out .superpowers/demo-cook/ChestDemo-explore --models --model-format glb --pack`. Expected: prints `models: ...` with no skipped numbers; `.superpowers/demo-cook/ChestDemo-explore/models/` holds 200 `.glb`.
- [ ] **Step 3: Check the Set.** `nfty inspect .superpowers/demo-cook/ChestDemo-explore` shows a models block; spot-check two `.glb` sizes differ from a flat-paper cook (re-cook 5 assets from the pre-step-9 commit is NOT needed; assert only that sizes are > the 2D-flat size by comparing against `nfty model <png>` of the same asset image).
- [ ] **Step 4: Re-capture frames** per Task 2 Step 9.4, replacing `.superpowers/frames-step9/` entirely (latest-only). Both themes, normal and `-min`. Report the folder and the three state names for Q6.
- [ ] **Step 5: Tell the author how to explore** in the final report: open `.superpowers/demo-cook/ChestDemo-explore` in the Set browser, point at an asset, press **Rise**; or open the demo book, select the Chest recipe, **Rise**; or the `chestbody` Ingredient's **Rise** to repaint the dome.

---

### Task 4: Docs, CLAUDE.md, BACKLOG ruling

**Files:**
- Modify: `docs/manual/get-started/the-demo.md`, `docs/manual/reference/cli.md` (`set depth` row beside `set chance`; the `set` section), `docs/manual/how-to/make-a-3d-model.md` (one sentence in the intro: the demo carries depth, open it to try this), `docs/manual/reference/file-formats.md` (one line under Depth maps: how the demo writes them), `CLAUDE.md`, `BACKLOG.md`, `tools/demo/draw-chest-art.py` module docstring (the depth overlay contract).
- Test: `dotnet test tests/Nfty.Cli.Tests` (any CLI-surface test enumerating commands) and a `mkdocs` build if the repo has one wired (`python -m mkdocs build --strict`; skip with a note if mkdocs is not installed).

- [ ] **Step 1: the-demo.md.** Add what the depth is, in the manual's voice: chests rise as a domed lid, bands and lock standing proud of the face, a box eight voxels deep, Glow flat. No new figure (Q2). State no number the tests do not pin.
- [ ] **Step 2: cli.md.** Document `nfty set depth <igt> --variant <id> --map <png>`: the PNG's R/G meaning (Q1), size must match, depth over clear pixels is dropped, an all-zero map clears.
- [ ] **Step 3: CLAUDE.md demo paragraph.** Add, in the file's existing compressed register: depth shipped (step 9); **overlay depth contract** (body shares one front profile at `y >= 12`; overlays are absolute `body_front + k`, because topmost-solid-wins, so they stand proud of every body by construction; pinned by `DemoModelsTests.Every_overlay_stands_proud_of_every_body_on_both_recipes`); Glow flat on purpose; depth never in DNA (the strip-the-depth test); `build-demo.py --check` compares entry content, the sanctioned way to ask "has it drifted" without a rebuild; `nfty set depth` is how depth enters an `.igt` from the CLI. Update the "Rise" Nfty.Cli commands line to name `set depth`. Remove nothing else; do not touch the count (615,600).
- [ ] **Step 4: BACKLOG.md ruling (Q2).** In "Reshoot the manual against the Chest Demo", replace the Rise-pages paragraph's "they need a demo that carries depth (step 9's)" with the status: the prerequisite is **met** (step 9), the reshoot **stays deferred and stays one pass**; list the Rise figures to add in that pass (Rise page both tabs, Cook dialog model options, voxel-ready card, grid row), and note `.superpowers/frames-step9` held the step-9 frames of the demo for reference only. Keep every other paragraph.
- [ ] **Step 5: Verify.** `dotnet build nfty.sln` and `dotnet test tests/Nfty.Cli.Tests tests/Nfty.Core.Tests tests/Nfty.Model.Tests`. Expected: green. Then the once-per-step full suite: `dotnet test nfty.sln` (house cadence: targeted per task, full at step end and before merge).
- [ ] **Step 6: Commit.**

```bash
git add docs CLAUDE.md BACKLOG.md tools/demo/draw-chest-art.py
git commit -m "docs: the demo carries depth; set depth documented; reshoot stays in the backlog"
```

---

## Self-Review

- **Spec coverage:** "Demo rebuilt with depth, drafted in build-demo.py" -> Task 2 (generator drives the CLI's own authoring command, Task 1 supplies it); "re-cooked for the author to explore" -> Task 3; spec's voxel-ready/format/stack-combine rules -> Task 2 tests; manual reshoot ruling -> Q2 + Task 4. Frames (Rise page over the demo book, 3D preview of a demo asset, both themes) -> Task 2 Step 8-9, Task 3 Step 4.
- **Count:** 615,600 untouched (no layer or variant added; depth is not DNA); proven by the unmodified `The_demo_dna_space_is_counted_exactly`, the frozen baseline and the strip-depth test.
- **Rebuild discipline:** one committed rebuild (Task 2 Step 10); `--check` replaces casual re-runs; iteration is `--no-embed` or uncommitted (Q5).
- **Types:** `DepthPicture.ToMap` and `set depth` names are the same in Tasks 1 and 2; `RiseFixtures.DemoVariant` / `RisePages.Demo*Vm` are defined in Task 2 Step 8 and used only there and in Task 3.
- **Risk:** the depth numbers are a draft by design; the acceptance rules (proud, non-flat, deepness floor 9, opaque-only) are what the tests pin so the author can retune freely.
