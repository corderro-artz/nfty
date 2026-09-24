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
