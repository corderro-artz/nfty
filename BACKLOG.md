# Backlog

Work that is deliberately not done yet, with the reason and enough context to pick it up cold.
Nothing here is a defect in shipped behaviour; each is a decision to defer.

*Done and removed from this list: splitting `UniqueSpace`'s cap into an enumeration budget and a
reporting ceiling (0.7.2), and `Total` not being a lower bound when the walk is skipped (0.8.0 —
`SpaceCertainty` names the direction, so that case reports "at most N").*

---

## Reshoot the manual against the Chest Demo

**Status:** deferred by the author until the manual is reworked, some time before 1.0.0.

Every figure in `docs/manual/images/` except `landing-*.png` still shows **Vapor Pets** — the
project that predated the built-in demo. The pages are not *wrong*: the tutorials build Vapor Pets
by hand, step by step, so the words and the pictures agree. But the app now ships with the Chest
Demo, so a reader's first screen and the manual's screenshots are two different collections.

**What it involves.** `tools/docs-capture/` regenerates the figures from the *running app*, and
`tools/demo/build-demo.py --workspace <dir>` already lays out a Kitchen, loose parts and a cooked
Set for exactly this purpose. The capture README carries the two PowerShell traps (`$h` silently
clobbering an `-H` parameter; a blind `Ctrl+T` that yields the same theme twice). Figures are
**pairs** — `thing-light.png#only-light` beside `thing-dark.png#only-dark`.

**Do it as one pass, not piecemeal.** Half-reshot pages would show two collections in one document,
which is worse than one consistently old one. The tutorial prose has to move with the pictures.

**Note the capture size changed.** Page frames were captured at 1416x950 against the old 1128x924
window minimum. The minimum is 1280x720 now (see `ShellViewModel`), so that standard is stale.

**The README's hero is already current and is NOT part of this pass.** `assets/readme/` holds one
fresh pair of the Chest Demo, shot with the same tools; see its README. Leave it where it is — a
figure inside the manual has to agree with the prose beside it, and that is what makes this a single
pass; the hero answers to nobody.

---

## The interactive passphrase prompt has no test

**Status:** accepted, and mostly closed by `--key`.

`Passphrase.ReadMasked` drives `Console.ReadKey` and cannot be exercised by a test runner, where
stdin is always redirected. That was half the passphrase path when the only alternative was
`--key-env`; naming the source (`env:` / `file:` / `stdin` / `prompt`) turned three of the four into
ordinary functions with inputs and outputs, covered by `PassphraseSourceTests`.

What is left untested is the genuinely interactive branch: key-by-key echo suppression, backspace,
and Escape. Testing it would mean an indirection over `Console.ReadKey` whose only consumer is the
test — worth it only if that reader grows logic worth checking. It has none today.

---

## `Nfty.Ticket` does not target `netstandard2.1`

**Status:** deferred deliberately; engine embedding is not a current goal.

`Nfty.Ticket` targets `net10.0` alone. A game engine loading it as a managed plugin would want
`netstandard2.1`, and adding that target later costs: replacing `SHA256.HashData`,
`Convert.ToHexString` and `ArgumentNullException.ThrowIfNull`; an `IsExternalInit` shim for `init`
accessors; and a hand-written reader for the Menu manifest, since `System.Text.Json` is a package
rather than in-box below `net6.0`.

**The rule that keeps it cheap:** `Nfty.Ticket` takes NO package dependency. The only thing a second
target ever has to reconcile is framework API surface.

**And the rule that keeps it correct:** whatever is replaced is replaced UNCONDITIONALLY, not behind
`#if`. A conditional would mean the assembly an engine embeds and the assembly the desktop app loads
run different code to compute a DNA — invisible in review and to any single-target test.

---

## `inspect --voxel` is refused on a `.mnu`

**Status:** deferred on scope, not on principle.

`--voxel` reports which variants carry partial alpha. It is refused on a `.ktn` (which lists paths
without opening them), on a `.tin` (sealed), and on a cooked Set (the output rather than the source
art). **A Menu is the one refused kind where the information genuinely IS present** — it carries
the source variants, uncolorized, which is exactly what the flag scans.

What is missing is the traversal, not the measurement: every `VoxelReport.Scan` overload takes a
`Loaded*` decoded from a `.cbk`, so a Menu needs its atlas decoded and cropped per cell.
`VoxelReport.CountPartial(Image<Rgba32>)` is already public and already does the per-image work.

The refusal message says so in those terms, deliberately — a "cannot" borrowed from one of the other
three arms would be false here, and a wrong reason in an error message is worse than a terse one.

---

## `extend` rebuilds a Set's DNA index by opening every asset file

**Status:** deferred; real and measured, but it only bites at large Sets.

`SetWriter.ReadExisting`/`ReadExistingAsync` enumerates `nfty/*.json` and does one
`File.ReadAllText` plus `JsonDocument.Parse` **per already-minted asset**, sequentially, on every
`extend` run — including under `--unlimited`, where the reconstructed DNA set is not used for dedup
at all.

**What is NOT the problem.** The uniqueness check itself is already O(1): `Generator.RollAll` builds
one `HashSet<string>` and calls `seen.Add` per attempt, measured flat from an empty set to one
preloaded with 500,000 DNAs. And the rerolls that pile up as a book's space fills cannot be helped
by any index — expected attempts per success tracks `N/(N-k)` (about 13 at 90% full, 30 at 99%),
because rejection sampling keeps proposing combinations already taken rather than because checking
is slow. Sampling from the remaining space instead would need an enumeration `UniqueSpace`
deliberately refuses, or a change to RNG consumption per draw, which would change every collection
ever generated.

**The shape of the fix, if it is ever wanted:** an append-only `nfty/_index.jsonl` sidecar written as
each asset is written, preferred by `ReadExisting` when present AND consistent (line count against
the `nfty/*.json` count), falling back to today's per-file walk otherwise. Determinism is untouched:
it changes only what `ReadExisting` reads to populate `existingDnas`/`startNumber`, which are
consumed identically downstream, so no sort order and no RNG draw count moves.

**The staleness check is the load-bearing part, not the index.** A user who hand-edits a Set folder
leaves an index that either claims a DNA is taken when it is not (harmless — one needless reroll) or
fails to report one that is (breaks the uniqueness guarantee `extend` exists to preserve). Same
posture `CookBookManifest.RecipeOrder` already keeps: never trust a possibly-stale listing, verify
or fall back.

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

## A cross-quad `(position, normal, uv)` vertex dedup for risen meshes

**Status:** deferred; measured (`RisePerfTests`), not added, per the step-6 task brief's instruction
to add this entry rather than the pass itself if the numbers argued for one.

`Mesh`'s vertex rule is deliberate: a vertex is shared only within its own quad (`VertexCount` is
always exactly twice `TriangleCount`), never merged across quads, because two quads meeting at an
edge usually disagree on normal or UV. `RisePerfTests` measured a 512px disc (24,892 triangles,
49,784 vertices, a 1,900,004-byte GLB): vertex data (position + normal + UV, 32 bytes/vertex) is
1,593,088 bytes — **83.85% of the file**. Indices add another 298,704 bytes (15.72%), so vertex +
index data together are 99.57% of the GLB; the PNG texture and JSON scaffolding are the remaining
0.43% (~8.2 KB).

That 83.85% is a hard ceiling on what any dedup could ever remove, not an estimate of what it would
actually save — index count is unaffected by merging (the same number of triangles still needs three
vertex references each), and only vertices sharing the *exact* same position, normal AND UV can
merge at all. In practice that limits merges to touching quads of the *same* orientation (e.g. two
adjoining front-face rectangles from `GreedyRects`, or two adjoining back-face ones); a front face
meeting a wall, or two walls meeting at a corner, have different normals and can never merge under
this key. A separate, uncommitted timing split of `Riser.TryBuild`'s internals on the same 512px
fixture (scan ~10.3 ms, `GreedyRects.Merge` ~10.0 ms, face emission ~4.1 ms, wall emission ~16.0 ms)
found 9,208 wall segments against only 3,238 merged front/back rectangles — this fixture's Dome
depth pattern (8 rings) turns most of the disc into wall, which is exactly the geometry a
same-orientation dedup helps least. So the real, achievable saving is well below the 83.85% ceiling
and depends heavily on how "steppy" a given book's art is; it is not knowable without building the
merge and measuring it on real (not synthetic-ring) demo art.

**If picked up:** key on `(position, normal, uv)` exactly (no epsilon — the same float bit patterns
already flow through one deterministic pipeline), dedup per mesh after `EmitWalls`, and keep the
"`VertexCount == 2 * TriangleCount`" invariant as documentation of the *un-deduped* builder output
only, moving the real invariant to a new test asserting no two indices reference bit-identical
vertices where it would be safe to merge them. Any change here must stay byte-identical for
identical input (`Every_format_is_byte_identical_when_written_twice` and its GLB sibling), so the
merge's own ordering needs to be as deterministic as `GreedyRects`' discovery order is today.

**Also open, from the same measurement:** `Riser.TryBuild` at 512px is dominated by wall emission
(~16.0 ms), not by `GreedyRects.Merge` (~10.0 ms) — wall emission alone costs more than the merge
step, and roughly as much as scanning plus merging combined. `EmitWalls` walks every pixel and
checks all four neighbors regardless of how many walls it finds, so its cost tracks image area more
than solid-pixel count; if 512px+ art becomes routine, that loop is the one worth revisiting first,
before a vertex dedup.

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

## M5 deferred: `Mesh`/`MeshBuilder` copies vertex data three times

**Status:** deferred; harmless at today's scale, worth revisiting before step 8.

`MeshBuilder` accumulates vertex data in `List<float>` fields as it walks a picture's faces and
walls. `MeshBuilder.ToMesh` calls `ToArray()` on each of them — copy one — and `Mesh`'s own
constructor clones its array parameters again — copy two — so building one mesh moves the same bytes
three times before anything is written to disk. Harmless at the sizes this branch ships (a 512px
disc's GLB is 1.9 MB; the copies are microseconds against `EmitWalls`' own cost, see the vertex-dedup
entry above), but step 8 builds a model for every asset of a whole Set, in parallel — worth an
internal no-copy constructor, or an ownership-transfer overload that takes `MeshBuilder`'s arrays
directly, before that multiplies the copy count by collection size.

---

## M6 deferred: fractional CLI options parse culture-sensitively

**Status:** pre-existing precedent, not introduced by this branch; deferred as one family, not a
piecemeal per-option fix.

`--voxel-size` parses via System.CommandLine's built-in `float` converter, which reads the current
culture — under `de-DE`, `--voxel-size 0.5` parses as `5` (the culture's decimal separator is a
comma, so `.` is read as a thousands separator instead). The same is true of every other fractional
CLI option nfty has (`--weight`, `--absent`) and any future one, since none of them supplies an
invariant-culture parser to override the default. This is not new to `model` — it is the same trap
`ColorSpec`'s explicit-prefix rule exists to avoid for color input, just not yet closed for numeric
options — and worth fixing once, for the whole family (a shared invariant-culture argument parser
each option opts into), rather than patching `--voxel-size` alone and leaving the next fractional
option to rediscover it.
