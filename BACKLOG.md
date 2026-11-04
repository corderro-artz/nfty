# Backlog

Work that is deliberately not done yet, with the reason and enough context to pick it up cold.
Nothing here is a defect in shipped behaviour; each is a decision to defer.

*Done and removed from this list: splitting `UniqueSpace`'s cap into an enumeration budget and a
reporting ceiling (0.7.2), and `Total` not being a lower bound when the walk is skipped (0.8.0 —
`SpaceCertainty` names the direction, so that case reports "at most N").*

---

## An app-wide toggle for GPU acceleration

**Status:** deferred by the author (2026-09-24) to the NEXT worktree, where they will scope it.

The 3D preview (`docs/superpowers/specs/2026-09-22-model-preview-design.md`) is being built as a
**software rasterizer**, deliberately: `Avalonia.Headless` has no GL context, so a GPU surface would
be the one screen in the app that `VisualCapture`, `DarkModeContrastTests` and
`PixelPerfectRenderingTests` cannot see. A CPU renderer draws identically on screen and under test,
needs no package, and is ample for a few hundred triangles of extruded pixel art.

The author wants the option of GPU acceleration later, **for every part of the application where it
is relevant** — not the preview alone. Recorded here so the preview is built in a way that does not
foreclose it.

**The constraint that must survive the toggle:** the software path stays the DEFAULT and stays the
path the test suite renders. A GPU path that became the only one exercised by real use would be a
second renderer with no test coverage — the exact failure this repo's visual rules exist to prevent.
Whatever the toggle switches, the headless sweeps must keep seeing a frame.

**What to decide when it is picked up:** which surfaces actually benefit (the preview certainly; the
Set browser grid and the editor canvas are candidates worth measuring, not assuming); whether the
toggle is per-surface or global; and how a GPU frame is verified at all, given the harness cannot
render one.

---

## Risen meshes: wall emission dominates, and walls barely dedup

**Status:** open, measured; the cross-quad vertex dedup itself shipped (see CLAUDE.md, `Nfty.Model`).

`ModelWriter` now compacts the mesh (`Mesh.Compact()`: bit-identical `(position, normal, uv)` vertices merged, first-seen order) before writing; `Riser.TryBuild` stays un-deduped for the per-stroke Rise preview. On the
512px Dome-ring disc (24,892 triangles) that is only 49,784 -> 48,700 vertices (-2.2%) and a
1,903,132 -> 1,868,444 byte GLB (-1.8%): wall quads (9,208 segments against 3,238 merged faces) sample
their pixel's center, so every wall quad has its own UV and shares nothing with the next pixel's. A wall
dedup would need a different UV rule (e.g. one texel per wall run), which changes the texture contract.
Doing it inside `TryBuild` cost ~34 -> ~42 ms per build, hence the move to the writer.

`EmitWalls` walks every pixel and checks four neighbors; it was NOT rewritten (no clear win measured:
the per-quad emission, not the neighbor check, is most of its ~16 ms). Revisit with a profiler if 512px+
art becomes routine.

---

## Step 7's model files were never opened in a real glTF viewer

**Status:** a process gap, not a doubt about the mesh math; needs a human or a desktop-capable agent.

Task 7's manual CLI run wrote three model files (a GLB, a `.gltf`+`.bin` pair, and an `.igt` GLB) and
checked them the way the plan's own fallback allows when "no viewer is available to the
implementer": an independent Python checker verified orientation (front-facing +Z, no mirroring, not
upside down against glTF's own conventions), enclosed volume, edge balance on the quarter-unit
lattice, wall UVs landing on the correct texel, and determinism across two runs. None of that is in
doubt — it is exactly the reasoning `MeshChecks` and `GltfValidator` already pin in the test suite.

What is still missing is the one thing analysis cannot stand in for: actual pixels on an actual
screen. Nobody has opened those three files in Windows 3D Viewer, Blender, or
gltf-viewer.donmccurdy.com to confirm by eye that nothing is mirrored or upside-down, the edge colors
read right, the pixels are crisp (no mip filtering blur), and nothing is see-through from the back.
Do this once, on the files Task 7 already produced (or freshly regenerated ones — they are
byte-identical either way), and close this entry.

---

## Rise page: the model of a 512px stroke lands a build behind

**Status:** the UI-thread half is fixed (2026-11-04); what is left is latency, not a frozen page.

The mesh is built off the UI thread (`RiseViewModel.MeshBuild.cs`, latest wins, one running + one
waiting), and the EXPORT tab's exact size is debounced 250 ms and counted off the UI thread. Measured
in `RisePagePerfTests` at 512px, UI-thread time per drag move (`Rise.PreviewStroke`) went from 28.2 ms
to 0.21 ms in Debug (6.5 ms to 0.69 ms Release), and per release (`Rise.ApplyStroke`) from 48 ms to
26 ms Debug (12.6 ms to 3.3 ms Release). The build itself (`Riser.TryBuild`) is unchanged, ~30 ms
Debug / ~7-12 ms Release, so during a 512px drag the 3D tile shows the model of a move one build old,
and on release the exact model lands one build later (always: the last request's build is never
dropped). Levers still open: cross-quad vertex dedup and an incremental rebuild of only the columns a
stroke touched (both in `Nfty.Model`).

**What is left of the 26 ms Debug release** is the DEPTH tab's own bindings after an edit
(`OnDepthEdited`): the thickness histogram is a whole-surface scan (~15 ms at 512px Debug, now taken
once per pass, was twice) and its rows rebuild the list control. Measure in Release before touching it;
an incremental histogram kept by `DepthSurface` would remove the scan.

**Rise, a Set's book arrival that throws part-way:** `SetRiseSource.Arrive` publishes `ExportReady` in a
`finally`, so an export is never left waiting, but an exception from a listener or a reseed mid-loop leaves
some loaded subjects reseeded with the book's depth and some not, and the next export snapshots that mixed
state. Reachable only after an exception has already escaped the dispatcher; noted, not fixed.

## The audit-fixes pass: performance ideas measured as not worth it yet

**Status:** deferred; each wants a measurement before it wants code. All five are throughput ideas, none a defect.

**Sprite-sheet decode window (T-G2).** `SpriteSheet` builds one asset at a time so the sheet is the only large thing in memory. A bounded look-ahead (decode asset N+1..N+k while N draws) would overlap the PNG decode with the draw. Deferred because the draw must stay sequential (every asset paints into one image) and decode is the minority of the time at demo sizes. Pick up when a 1000px, several-thousand-asset sheet is reported slow: measure decode vs draw with a `Perf` scope first.

**Book-wide decode (T-G5).** `CookBookArchive.Read` decodes every variant PNG up front, which `ArchivePeek` exists to avoid for listings. Reads that need only some variants (a Rise page on one recipe) still pay for all. Deferred: lazy decoding changes `Loaded*` ownership and disposal (`IDisposable` down to every image). Pick up with a measurement of open time on a book with hundreds of variants at 512px.

**Parallel `Validator` pixel checks (T-G7).** `Validator` checks each variant image against the canvas, and for partial alpha, one image at a time. The per-image work is independent and would fit `ParallelWork` (results into slots, report order unchanged). Deferred: `Validator` must report in a stable order and the checks are cheap next to decode. Pick up if `validate` on a large book shows up in `Perf`.

**Pipelined `ModelBatch.Export` (T-G10 / R12).** The batch builds a window of 2x degree, then writes in ordinal order on the calling thread; build and write do not overlap across windows. A producer/consumer pipeline would hide the writes. Deferred: output must stay byte-identical at any degree (`ModelBatchParallelTests`) and the windowed form is easy to reason about. Pick up when exporting a few thousand models is measured write-bound.

**CookBook card and Explorer validation/counting cost (T-U6 / T-U7).** `CookBookDetailViewModel` validates and counts `UniqueSpace` on construction, and `ExplorerViewModel` re-validates after edits. At demo size these are invisible; at hundreds of recipes they may not be. First step: add `Perf` scopes `Card.Validate`, `Card.UniqueSpace` and `Explorer.Validate` and read them on a large book. Only then choose between caching, a debounce, or moving them off the UI thread.

## A deep review, and possibly a merge, of the `UniqueSpace` and `SelectionOdds` walks (S-C14 / author ruling Q1)

**Status:** deferred by author ruling (2026-10-27): leave as is, review deeply later.

**What.** `UniqueSpace.Walk(depth, BigInteger)` and `SelectionOdds.Walk(depth, double)` walk the same tree. Both use the same selection dictionary, the same absent-as-omission rule, the same `RulesEngine.IsLegal` leaf and the same `DefaultEnumerationBudget` gate. `SelectionOdds`' own remark says "the walk is UniqueSpace's".

**Why not now.** Two walkers are one copy too many only once a third appears.

**Risk.** Every DNA figure on every surface (CLI `stats`, the CookBook card, the cook failure message, the Set rail's exact odds) depends on one of them. The `SpaceCertainty` direction (`Exact`/`AtLeast`/`AtMost`/`Unknown`) and the budget-exhaustion behavior must survive exactly. Float summation order in `SelectionOdds` must not change: the order of additions is the number.

**Shape if done.** A generic `RuleWalk.Sum<T>` over `INumber<T>`, allocation-free.

**Pinned by.** `UniqueSpace*`, `SelectionOddsTests` (prediction against a real cook), `DemoCookBookTests` (the demo's exact count) and `GeneratorTests`' failure-message tests.

**Pick up when.** A third walker is written, or a bug is fixed in one walker that the other also has.

## Small leftovers from the audit-fixes pass

**Status:** each is a minor a review noted and the author did not rule on. None is a defect in shipped behavior. One paragraph each: what, why deferred, how to pick up.

**The watermark-stamp test's "lowest-named file" assertion is vacuous.** `CorruptSetException` names the lowest-positioned unreadable asset, but the test corrupts only one file, so it cannot fail if the choice becomes nondeterministic. Pick up by corrupting two files and asserting the lower one is named at several degrees of parallelism, then probe by choosing the higher.

**A test that diffs the global TEMP directory could flake.** A test asserting a temp folder was removed compares listings of the shared TEMP, so another process or test creating a folder there fails it. Pick up by giving the code under test an injectable temp root and diffing that instead.

**ImageSharp's own parallelism can oversubscribe inside `ParallelWork`.** A decode or encode run inside a parallel body starts ImageSharp's internal parallel work, so `--threads N` is a floor on CPU use, not a ceiling. Pick up by passing `Configuration.MaxDegreeOfParallelism = 1` for images processed inside a parallel body (`insideParallelBody`), measured first: single-image calls outside one benefit from the default.

**`IngredientArchive.ReadAsync` may decode on the caller's thread.** ZIP entries are read sequentially (as `ZipArchive` requires) and the decode is parallel, but part of the decode can still land on the calling thread, which on the UI thread is the freeze the async twin exists to prevent. `WriteAsync` was fixed to run off the caller (34ed37b); the read side was not. Pick up with a test asserting no decode runs on the calling thread, the shape `WriteAsync`'s test has.

**The Rise page has no 1px lattice-phase assertion.** The Ingredient editor's `CanvasGridTests` measures the backdrop lattice's PHASE against the art's corner; the Rise canvas shares the idea but not the test. Pick up by copying that measurement onto `RiseView`'s backdrop.

**A `.tin` opened at startup does not set `PendingOpen`.** Opening a sealed Set through the startup path (a file argument) shows the passphrase prompt without exposing the observable handle `LandingViewModel.PendingOpen` gives a fire-and-forget open, so a test cannot await it. Pick up by setting `PendingOpen` on that path, as Landing's own open does.

**`RiseExport` sealed-set test: a possible flake, unreproduced.** One review saw it fail once and could not repeat it. Suspect an unawaited continuation of the fire-and-forget open (see the previous entry). Pick up by running it in a loop (200 times) with `--blame`; if it reproduces, it will name the missing handle.

**The `rise-set-batch-export` capture is nondeterministic.** Two of 156 frames differ between identical runs (a pre-existing capture race, not a regression). Pick up by pumping until the batch card's progress text settles before capturing, as `Shoot` does for thumbnails.

**The sheet-grid read-only fallback has no CLI test.** `SheetGrid.WriteInto`/`RemoveFrom` keep the grid for the session when the file is read-only; `sheet grid` has no test driving that through the command. Pick up by marking a temp PNG read-only and asserting the command reports it without throwing.

**Two `ToDictionary` calls and one color conversion are not on the shared helpers.** `ExplorerViewModel` (around line 366) and `IngredientDetailViewModel` (around line 383) build dictionaries with `ToDictionary`, which throws on a duplicate id where the repo's rule is to check first or use the helper; `BookSettings` builds `Color.FromRgb` where everything else goes through `ToAvalonia`. Deferred because a validated book cannot reach the duplicate case from these two. Pick up as one small cleanup.

**`Problems.Join` joins with LF.** The report text is invariant across machines, which is why it is LF, but a Windows clipboard paste into some editors shows one line. Pick up only if someone reports it; CRLF on Windows would make the same report differ across machines.

**The PIVOT layout test covers the variant source only.** The Rise rail's PIVOT-above-the-fold test measures the layout for a single variant; the Recipe and Set sources have different rail heights (different note lines). Pick up by running the same measurement for those two sources at the smallest window.

**The Kitchen has no creation wizard.** `New Kitchen` is a save-file picker, so there is no `wizard-kitchen` frame to capture although the audit list (V-M3) named one. If a wizard is ever built, add the frame and a `DarkModeContrastTests` screen with it.

**The `set-browser-loading` capture shows a mixed state.** A Set browser's first four tiles decode even a 25-megapixel picture inside the first layout pass on this machine, so the frame shows four decoded blue squares beside two loading diamonds, not six diamonds. Making it fully deterministic needs a way to hold `ThumbnailGate` closed from a test (the gate is built inside `SetBrowserViewModel`). Pick up by letting the view model take a gate.

**Pop-out viewer: no single-instance hand-off.** Opening a file from the shell would start a second app process. Deferred because it needs a named pipe or mutex plus argument forwarding, and it is only useful once a file association exists. Pick up together with the association: the second process sends its path to the first and exits.

**Pop-out viewer: no drag-and-drop onto a window.** `Avalonia.Headless` registers no platform drag source and no test can drive a drop, so a drop handler would ship unverified. Pick up when a headless-testable seam exists (a handler taking the dropped paths, exercised directly) and wire both the viewer and the shell window to it.

**Pop-out viewer: no `nfty view <file>` command.** The viewer lives in `Nfty.App`; the CLI does not reference it and must not start a GUI. Pick up as a small launcher in `Nfty.Desktop` that accepts a path argument and opens the viewer at startup.

**Pop-out viewer: a generic glTF reader.** `GltfReader` reads only the subset nfty's own `ModelWriter` writes (one mesh, one texture, the fixed accessors), and refuses anything else by name. A model from elsewhere needs sparse accessors, other component types, multiple nodes, materials and cameras. Deferred: nothing in the product produces such a file and the zero-new-packages rule rules out a library. Pick up only if people ask to inspect foreign models.

**Pop-out viewer: near-plane quads are skipped, not clipped.** The 3D pane's software rasterizer drops a quad that crosses the camera's near plane instead of clipping it, so zooming the camera into a model can make faces vanish. Reason: clipping adds polygon splitting for a case the zoom limits mostly prevent. Pick up by clipping quads against the near plane before projection and asserting no hole at the closest zoom.

**Pop-out viewer: Edges draws quad edges, not a per-voxel grid.** The greedy mesh merges coplanar voxels into big quads, so the outline shows those quads, not every voxel. A per-voxel grid needs the voxel lattice drawn over the faces (or an unmerged mesh for the pane only). Pick up by building the lattice lines from the depth map in the viewer, never changing the exported mesh.

**Pop-out viewer: the Ingredient Editor's viewer has no model.** The editor holds one layer's value-map, not a risen stack, so its viewer shows the picture only. Pick up by letting the editor hand the viewer a `Riser` result for the current variant with its stored depth, following the Rise page's source.

**Pop-out viewer: a recipe tree row is a fixed-seed 2D snapshot.** The row's viewer rolls the recipe once with a fixed seed and shows a picture, with no reroll and no model. Pick up by routing the row through the Rise page's recipe source (reroll, model) or giving the viewer its own reroll control.

**ImageSharp 3.2.0 (six advisories unreachable, suppressed).** 3.1.11 stays pinned: 3.2.0 and 4.x need a license key for Release builds. The six advisories (TIFF fax/CCITT/BigTIFF, HistogramEqualization, ICC CLUT) are made unreachable by `Imaging/ImageFormats` (PNG + JPEG decoders only, ICC dropped) and suppressed in `Directory.Build.props` with `NuGetAuditSuppress`. Pick up by moving to 3.2.0 if a license path appears, then deleting the six suppression lines; `ImageFormats` can stay as the one decode seam.

## Planned: more pinning services (Filebase, NFT.Storage)

**Status:** planned, author's decision (2026-11-03).

The upload ships two providers: local IPFS (Kubo HTTP API, the default) and Pinata (user's own JWT,
OS credential store). Filebase and NFT.Storage are the next two. Each is one more `IPinningService`
in `Nfty.Core/Publish/Ipfs/` (status + folder add that returns the root CID of the folder's
contents, so `ipfs://CID/1.png` resolves), one more Settings PUBLISHING choice with its key kept in
the same credential store, and one more CLI provider flag beside `--pinata`. Checked against a real
account before shipping, like Pinata was: the same Set must give the same CIDs on every provider.
Filebase speaks an S3-compatible API (bucket + access key/secret); NFT.Storage's current API must be
checked first, since its upload service has changed over time.
