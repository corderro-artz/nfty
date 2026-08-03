# GUI visual-polish — running status

Living handoff for `feature/gui-visual-polish`. Read this **with**
`2026-08-01-nfty-gui-visual-audit.md`, which is the source of truth for what each slice must
achieve. This file only records where the work has got to.

Last updated: 2026-08-02, after Slice 10.

## State

- Branch: `feature/gui-visual-polish`, **18 commits ahead of `main`, not merged**.
- Build: 0 warnings, 0 errors. Tests: **Cli 42 / App 244 / Core 549**, all green.
- The locked mockups in `docs/design/mockups/` are **untouched** by this branch and must stay that
  way — they are the 1:1 reference. Verified with `git diff main...HEAD -- docs/design/mockups/`.

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

### Slice 11 — Help sheet
`Views/HelpView.axaml` is still a placeholder paragraph. The mockup (`help.html`) is a 780px
three-column reference with a header, glyph gutters, key/colour columns and a DNA footer band.
Note: help.html's legend documents the three rule marks **verbatim**, so it must use
`IconMarkExclude` / `IconMarkRequire` / `IconMarkFlag` from `Themes/Icons.axaml` — not
lookalike chrome icons. HelpView still contains literal `→ ✕ ⚑ ●` glyph characters; they are
Slice 11's to replace.

### Slice 12 — Final sweep
Re-render all frames and diff against the mockups; confirm no off-palette colour; close or
document what remains.

## Known deviations (documented in code, deliberately open)

1. **Stepper column** — the mockups' `.stepr` is a 20px column of 9px chevrons; ours stays
   Fluent's wide side-by-side pair. Fixed inside Fluent's `ButtonSpinner` template at a priority a
   `Style` setter does not beat. Verified against rendered frames; setters that did nothing were
   removed rather than left in. See `Themes/Controls.axaml`.
2. **Slider track** — mockup is a 6px rounded bar; ours stays Fluent's ~2px line, same cause. The
   **ring handle** (the visible half) is done.
3. **Dual-range control** — Avalonia has none. Two sliders are laid over a gradient band with
   transparent tracks so only their ring handles show. Close to the mockup; not a real dual control.
4. **`ValueMap.FromImage` keeps the RED channel**, not luminance (`src/Nfty.Core/Editing/ValueMap.cs`).
   A colour import therefore darkens saturated hues. The GUI now **warns accurately** at import;
   the Core conversion was deliberately left alone (Core change, would alter existing art).
   **Open question for the user.**
5. Icons traced from multi-path SVGs drop fill-only accent details (cookbook bookmark tab, recipe
   corner dot, marquee dash) — `StreamGeometry` is single-stroke. Noted per icon in `Icons.axaml`.

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
