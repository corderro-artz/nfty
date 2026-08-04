# GUI visual-polish — running status

Living handoff for `feature/gui-visual-polish`. Read this **with**
`2026-08-01-nfty-gui-visual-audit.md`, which is the source of truth for what each slice must
achieve. This file only records where the work has got to.

Last updated: 2026-08-04, mid Slice 12.

## State

- Branch: `feature/gui-visual-polish`, **28 commits ahead of `main`, not merged**. Clean tree.
- Build: 0 warnings, 0 errors. Tests: **Cli 42 / App 253 / Core 549**, all green.
- **Avalonia is 12.1.1** (was 11.2.3). Verified by pixel-diffing all frames against a pre-upgrade
  baseline: no ControlTheme broke, all 14 `/template/` part names still resolve. 11.3.18 was
  rejected — it loses the mono bold face, dropping 39% of the wordmark's ink to font fallback.
  Side-effects worth knowing: `Avalonia.Diagnostics` no longer exists (now
  `AvaloniaUI.DiagnosticsSupport`), the text shaper is opt-in (`Avalonia.HarfBuzz`) when the backend
  is selected by hand, and **`Nfty.App.Tests` is on xunit.v3** while Cli/Core stay on v2.
  `Grid.ColumnSpacing`/`RowSpacing` and control-level `LetterSpacing` are available now — prefer
  them over margin-per-child and TextBlock-in-Button workarounds.
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
   **32** PNGs land in `<tmp>` — light+dark for each screen, plus the pairs that exist because a
   default fixture cannot reach the state: `landing-*-empty`, `ingredient-detail-dynamic-*` (every
   other ingredient fixture is Custom, so the colorways hue band had no evidence) and
   `editor-enabled-*` (same cause: the editor gates painting on `CanPaint => !IsCustom`, so the
   `editor-paint-*` pair renders the whole toolstrip **disabled** and never showed the blip preview
   at all). If you add a state a fixture cannot reach, add a frame for it.
4. Work Slice 12, the last one. Commit per slice; keep the suite green and warnings at zero.

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
| 11 | Help sheet | done |
| 12 | **Final sweep** | **in progress — see below** |

### Slice 12 — what has been closed so far (2026-08-04)

- **Mono type on Landing and the three wizards.** landing.html and the wizard mockups declare only a
  mono font token — no sans token exists in those four files — and set it on the window container,
  so everything inherits mono. explorer/help/ingredient-editor declare both and use sans body with
  mono accents, which is what the blanket `TextBlock` rule implements. The split is real, is in the
  locked mockups, and is now reproduced via a `monoform` class. **Note this contradicts CLAUDE.md's
  claim that the mockups' token block is shared verbatim** — those four carry a trimmed block, also
  missing shadow, guide and every radius token.
- Kicker dot centring, the wizard footer collision (card 560 → 620), clipped canvas steppers,
  Symbol+Canvas sharing a row, the 54px description box, the neutral loose-ingredient recents tile.
- **New Ingredient's colour ranges** now use the editor's dual-range control instead of four bare
  stock Sliders, which fixes the audit's remaining Critical: the card overflowed and the body
  ScrollViewer silently hid Destination, Canvas and the derived identifier.
- **Kind and Destination choice cards** replacing bare RadioButtons, with equal-height stretch.
- `editor-enabled-*` frames (see the fixture note in "Start here").
- The two Avalonia-11.2 workarounds retired (`ColumnSpacing`, `LetterSpacing`).

### Slice 12 — what is still open

- The Explorer + detail-views diff pass (an audit was dispatched for it; its findings are not yet
  folded in).
- The nine `?` help badges the wizards' mockups show beside labelled fields — no `.q` badge exists
  anywhere in the app.
- New Recipe's "Resulting mix" is still a bare `ProgressBar` bound `Value=Weight, Maximum=100`, so it
  reads as an absolute percentage. The mockup's `.share` is a segmented bar plus a legend naming
  every sibling Recipe with its %. **`NewRecipeViewModel` does not surface sibling recipes/weights**,
  so the ViewModel needs that before the view can be built. The domain concept is a weight relative
  to siblings, not a percentage.
- Copy nits: New Ingredient's footer should read "create & paint" / "Create & paint →" (it opens the
  editor next); the kickers should append the destination; the derived-identifier rows omit the
  mockup's right-aligned output path.
- The remaining `ColumnSpacing`/`RowSpacing` adoption sites: `CookBookDetailView.axaml:29-42`
  (2×2 metric grid using complementary half-margins), `ExplorerView.axaml:18-49` (four toolbar
  children each `Margin="0,0,10,0"`), literal spacer columns in CookBookDetailView / LandingView /
  IngredientEditorView (each renumbers `Grid.Column`, so re-verify frames per edit), and repeated
  `Margin="8,0,0,0"` data-table columns in the detail views.

### Slice 11 — Help sheet (done)
`Views/HelpView.axaml` went from 21 lines (one run-on paragraph in a box) to the mockup's 780px
three-column card: `.sh-h` header (brandtile + accent-`y` wordmark + divider + label + Esc chip),
`.sh-b` body at `1.35* / * / 0.82*` with left hairlines, five labelled sections, one strict 20px
glyph gutter down every entry, and the `.sh-f` footer band with the DNA sentence and its
`4 × 3 × 5 × 6 = 360` chip. Verified from rendered frames in both themes and pixel-measured against
the mockup's DOM: sheet 780 wide, columns 331/245/200 (mockup 331.3/245.4/201.3), header 54px of
content, footer 60px. Every other frame is byte-identical to the pre-slice capture.

Notes for whoever touches it next:
- The three rule marks read from `IconMarkExclude` / `IconMarkRequire` / `IconMarkFlag`, never the
  identical-looking `IconClose` / `IconArrowRight`. `HelpSheetTests` pins this by **resource
  reference identity** — the geometry strings are the same, so no screenshot and no string
  comparison could tell a wrongly-wired legend from a correct one.
- `IconTypeVariant` and `IconTypeSet` are new, traced from help.html:295/299. Both were `<rect>`
  only, so the rounded rects are written out by hand; per deviation 5 the accent sun and the
  accent-filled fourth square are dropped (the square is still *stroked*, since omitting it would
  break the 2x2 silhouette).
- New size class `Path.ico.ti-sh` (16px) for the sheet's type marks; all other new grammar is the
  `sh-`-prefixed block at the end of `Styles.axaml`, scoped that way because the sheet reuses several
  mockup class NAMES (`.slbl`, `.t`, `.s`, `.k`) at its own sizes.
- `Grid.ColumnSpacing` does not exist in Avalonia 11.2.3 (it landed in 11.3), so every "gap" in the
  sheet is a left margin on the content column. The compiler catches this; the API docs do not.
- Avalonia's `ArrangeCore` positions `Stretch` like `Center` once an explicit `Height` is set, so
  the 16px gutter cell needs `VerticalAlignment="Top"` or the glyph floats down beside the subtitle.
  This was visible only in the frame.

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
Plus a non-ASCII scan of the views for leftover glyph substitutes. **Run after Slice 11 and clean:**
what remains across `Views/`, `Themes/` and `Nfty.Desktop` is `—` `›` `×` `⌘` `·` `…` plus one `●`
that appears only in a HelpView comment explaining why the mockup's dot is drawn as an `Ellipse`.
No icon stand-ins left.

## Known deviations (documented in code, deliberately open)

1. **Stepper column** — the mockups' `.stepr` is a 20px column of 9px chevrons; ours stays
   Fluent's wide side-by-side pair. Fixed inside Fluent's `ButtonSpinner` template at a priority a
   `Style` setter does not beat. Verified against rendered frames; setters that did nothing were
   removed rather than left in. See `Themes/Controls.axaml`.
2. **Slider track** — mockup is a 6px rounded bar; ours stays Fluent's ~2px line, same cause. The
   **ring handle** (the visible half) is done. **Do not try to compress the range row's height.**
   Fluent lays a horizontal Slider out at ~40px against the mockup's ~20px `.rng`; both levers were
   tried and reverted — an explicit `Height` on the Slider, and a fixed `Height` on the containing
   `Panel`. Each clips the ring handles to their top arc, because the template keeps positioning the
   thumb for the taller row it wants. The natural height is the only one that draws a whole handle,
   so New Ingredient absorbs the cost with a taller card and tighter body (`.modal.tall`,
   `.modal-body.tight`, both scoped to that view). `WizardFitsTests` fails if that budget is blown
   again.
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
   corner dot, marquee dash, and now the Variant sun and the Set's accent-filled square) —
   `StreamGeometry` is single-stroke. Noted per icon in `Icons.axaml`.
6. **The Help sheet's `Esc` is a Button, the mockup's is a static span.** Escape (a `KeyBinding`) is
   otherwise the sheet's only way out and the scrim behind it does not dismiss, so the chip stays
   clickable; it is styled to the mockup's `.esc` exactly, with Fluent's hover/pressed neutralised.
7. **The Help sheet scrolls below a ~474px page area** (i.e. at MainWindow's `MinHeight="580"`),
   which the mockup does not — at 480px tall it would otherwise lose its footer band by six pixels.
   Inert at every larger size; verified by rendering at 874x474.

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
- **Patching source with PowerShell.** `Get-Content -Raw` defaults to the ANSI codepage on
  PowerShell 5.1, so reading a UTF-8 file and writing it back double-decodes every non-ASCII
  character — `—` became `â€"`, `⌘` became `âŒ˜`. Silent: it builds, it tests green, and it only
  shows up as garbage text in a rendered frame. **Use the Edit/Write tools for source edits.** Fast
  check if you suspect it: grep the tree for `â€`.
- **A `Classes` attribute that does nothing.** `<UserControl Classes="foo">` in a view's own XAML
  root does NOT match `Style Selector="UserControl.foo TextBlock"` — the style was a silent no-op
  and cost a full build/capture cycle. Anchor the class on the view's outermost **child** instead,
  which is the pattern used throughout `Styles.axaml` (`Button.landing TextBlock.lbl`). Related: a
  blanket type rule that sets a property defeats inheritance for it, so setting `FontFamily` on an
  ancestor cannot reach text that `Style Selector="TextBlock"` already sets.

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
