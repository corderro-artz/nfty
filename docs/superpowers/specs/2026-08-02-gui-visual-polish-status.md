# GUI visual-polish — running status

Living handoff for `feature/gui-visual-polish`. Read this **with**
`2026-08-01-nfty-gui-visual-audit.md`, which is the source of truth for what each slice must
achieve. This file only records where the work has got to.

Last updated: 2026-08-03, after Slice 10 + the value-map import fix.

## State

- Branch: `feature/gui-visual-polish`, **20 commits ahead of `main`, not merged**. Clean tree.
- Build: 0 warnings, 0 errors. Tests: **Cli 42 / App 244 / Core 549**, all green.
- The locked mockups in `docs/design/mockups/` are **untouched** by this branch and must stay that
  way — they are the 1:1 reference. Verified with `git diff main...HEAD -- docs/design/mockups/`
  (empty). All 8 mockup HTMLs plus `gallery.html`, `README.md` and `build-gallery.py` are present.

## Start here (next agent)

1. Read `2026-08-01-nfty-gui-visual-audit.md` — it holds the exact CSS values and a per-line fix list
   for every finding. This file is only the progress ledger.
2. Read the **GUI: state and house rules** section of `CLAUDE.md`. It carries the conventions that
   cost real time to learn (verify from rendered frames, one `/template/` hop, etc.).
3. Render the current frames and look at them before changing anything, so you know the baseline:
   ```
   NFTY_CAPTURE=1 NFTY_CAPTURE_DIR=<tmp> dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~VisualCapture
   ```
   28 PNGs land in `<tmp>` (light+dark for each screen, plus `landing-*-empty` and
   `ingredient-detail-dynamic-*`).
4. Work Slice 11, then Slice 12. Commit per slice; keep the suite green and warnings at zero.

## Slices

| # | Slice | State |
|---|-------|-------|
| 1 | Icon system | done |
| 2 | Off-palette colour (Fluent blue, disabled grey slabs, scrim) | done |
| 3 | Type scale | done |
| 4 | Shell chrome | done |
| 5 | Explorer pane grammar | done |
| 6 | Clipped/colliding layout | done |
| 7 | Landing restructure | done |
| 8 | Wizard form grammar + input sizing | done |
| 9 | Detail views | done |
| 10 | Ingredient editor rebuild | done |
| 11 | **Help sheet** | **not started** |
| 12 | **Final sweep** | **not started** |

### Slice 11 — Help sheet (the audit's last open Critical)
`Views/HelpView.axaml` is **21 lines**: one bordered box holding a single run-on paragraph, so the
rendered sheet is ~3 lines of text in 80% empty panel. Confirmed still true on 2026-08-03.

The mockup (`help.html`) is a 780px three-column sheet. Its own class names, for tracing:
`.sheet` / `.sh-h` (header: `.brandtile` + `.wordmark` + `.tdiv` + `.slbl` + `.esc` chip) /
`.sh-b` (`grid-template-columns: 1.35fr 1fr .82fr`, with `.col + .col` taking a left hairline) /
`.e` (the strict `20px 1fr` glyph gutter) / `.kline` (kbd rows) / `.cs` (colour-prefix rows) /
`.sh-f` (footer with the DNA sentence and `.dnaeq` "4 × 3 × 5 × 6 = 360"). Five sections: *The five
words*, *Layer kinds*, *Rules & state*, *Keys*, *Colour*.

Two things to get right:
- The legend documents the three rule marks **verbatim**, so it must use `IconMarkExclude` /
  `IconMarkRequire` / `IconMarkFlag` from `Themes/Icons.axaml` — not the visually similar
  `IconClose` / `IconArrowRight`. Changing one without the other makes the legend stop describing
  the app; that is why those keys exist separately.
- HelpView still contains literal `→ ✕ ⚑ ●` characters (the only glyph substitutes left in any
  view). They are Slice 11's to replace.

Most of the styles needed already exist: `Border.idchip`, `TextBlock.slbl`, `Border.kbd.keys`,
`TextBlock.kind-txt.kdyn/.kstat/.kcust`, `Border.fchip`, `TextBlock.wordmark`.

### Slice 12 — Final sweep
Re-render every frame and diff against the mockups; confirm no off-palette colour; close or document
what remains.

Several items the audit parked for this slice are **already closed**, so don't redo them:
`Border.kind-*` → `Border.fchip` with wash + kind tint (Slice 9); slider ring handles (Slice 8); the
Set-browser header's chip/label grammar and its reflowing tile rows (Slice 6 + earlier); the Landing
wordmark's accent `y` and negative tracking (Slice 7); the Explorer toolbar's right-aligned lock,
flexing search pill with magnifier + ⌘K, deleted `SearchSummary` row, and leading button glyphs
(Slice 5 + this pass). What is genuinely left is the **diff pass itself** and whatever it surfaces.

A sweep worth running, since it caught real things before:
```
grep -rnE '"#[0-9a-fA-F]{3,8}"' src/Nfty.App/Views src/Nfty.App/Themes/Styles.axaml src/Nfty.Desktop   # raw hex outside Tokens
grep -rnE 'Classes="ico[^"]*"[^/]*(Width|Height)=' src/Nfty.App/Views/*.axaml                          # inline icon sizes (breaks viewBox scaling)
```
Plus a non-ASCII scan of the views for leftover glyph substitutes — after Slice 11 the only
non-ASCII left should be genuine typography (`·` `—` `…` `›` `⌘` `×`), never an icon stand-in.

## Known deviations (documented in code, deliberately open)

1. **Stepper column** — the mockups' `.stepr` is a 20px column of 9px chevrons; ours stays
   Fluent's wide side-by-side pair. Fixed inside Fluent's `ButtonSpinner` template at a priority a
   `Style` setter does not beat. Verified against rendered frames; setters that did nothing were
   removed rather than left in. See `Themes/Controls.axaml`.
2. **Slider track** — mockup is a 6px rounded bar; ours stays Fluent's ~2px line, same cause. The
   **ring handle** (the visible half) is done.
3. **Dual-range control** — Avalonia has none. Two sliders are laid over a gradient band with
   transparent tracks so only their ring handles show. Close to the mockup; not a real dual control.
4. ~~`ValueMap.FromImage` keeps the RED channel~~ — **RESOLVED 2026-08-02.** `FromImage` still reads
   R, which is exact and lossless for its real job (round-tripping this layer's own grayscale PNG).
   The colour-import path in the editor now desaturates with ImageSharp `Grayscale()` (BT.709)
   *before* calling it, so foreign art collapses by luminance rather than by "how red is it" — pure
   green used to import as pure black. Fixed at the caller, so Core's contract and its test are
   untouched. Note the earlier claim that changing this would alter existing art was wrong: existing
   `.igt`s already store converted grayscale, and for grayscale input R equals luminance.
5. Icons traced from multi-path SVGs drop fill-only accent details (cookbook bookmark tab, recipe
   corner dot, marquee dash) — `StreamGeometry` is single-stroke. Noted per icon in `Icons.axaml`.

## Failure modes this branch actually hit

Recorded because each one shipped, or nearly shipped, a defect. They recur.

- **A test that cannot fail.** An assertion aimed at a pre-mutation snapshot passed no matter what
  the code did. Every non-trivial guard added here was **mutation-probed** — break the fix, watch the
  test go red, restore. Do that; it is cheap and it caught several.
- **A screenshot of something that is not the app.** See the `ShellChromeView` note below. If a
  harness builds its own copy of a control, the frames stop being evidence.
- **A setter that looks effective and is not.** Fluent fixes some geometry at a priority a `Style`
  cannot beat, so the XAML reads correctly and nothing changes. Check the frame, then delete the
  dead setter rather than leaving it to mislead.
- **Verifying at the wrong size.** Judging the Explorer at 900px made correctly-sized columns look
  broken. Capture at the size the app ships at.
- **A fixture that never reaches the code.** Every ingredient fixture was Custom, so the whole
  dynamic path — including the colorways hue band — rendered nothing and looked fine.
- **Trusting a subagent's report.** One reported a commit as "already landed"; it was true, but only
  verifying made that knowable. Two others were killed mid-task by session limits and left partial
  work. Check the tree, don't take the summary.
- **Asserting blast radius without measuring it.** The `ValueMap` note above was first written as
  "would alter existing art"; grepping the call sites showed that was false and changed the decision.

## Conventions this work established (do not regress)

- **Verify from a rendered frame, never from the markup.** `NFTY_CAPTURE=1
  NFTY_CAPTURE_DIR=<dir> dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~VisualCapture`
  then *look at* the PNGs. Frames render at MainWindow's own **1180x720** — the mockups' pane track
  alone needs 1014px, so a smaller capture makes correct layouts look clipped.
- **The harness must render the shipped control**, never a replica. The shell chrome was moved out
  of `Nfty.Desktop` into `Nfty.App/Views/ShellChromeView.axaml` precisely because a hand-written
  mirror in `VisualCapture` had been drifting from the real titlebar undetected.
- **Cover the paths a fixture cannot reach.** Frames exist for populated *and* empty Landing, and
  for a *dynamic* ingredient (every other fixture is Custom, so the hue band had no evidence).
- Token brushes only in Views; new colour → a token in **both** dictionaries. Avalonia hex is
  `#AARRGGBB` (mockup CSS is `#RRGGBBAA`).
- Icons scale by `size/24` about the top-left, reproducing the SVG viewBox. Never set `Width`/
  `Height` on a `Path.ico` without a matching transform — use the `xs`/`sm`/`ti`/`lg` classes.
- Avalonia style ordering is last-one-wins at equal specificity: a narrower opt-out must be
  declared **after** the blanket rule.
- A selector may cross **one** `/template/` boundary. Reaching a nested control's internals needs
  its own `ControlTheme`.
- Neutralising a Fluent state means **setting it explicitly** (e.g. to `Transparent`). Simply not
  painting hands the state back to Fluent's stock styling.
