# GUI visual-polish — running status

Record of the visual-polish pass (slice E). Read this **with**
`2026-08-01-nfty-gui-visual-audit.md`, which is the source of truth for what each slice had to
achieve. This file records where the work got to and, more usefully, what it cost to learn.

Last updated: 2026-08-05. **All 12 slices complete and merged. The wider project is complete too** —
see the note at the end for what shipped after this document's slices.

## State

- **Done and merged.** Build: 0 warnings, 0 errors. Tests: **Cli 42 / App 262 / Core 549**.
- What remains open is deliberate and listed under "Known deviations" plus the "Not done" list at
  the end of the Slice 12 section. Nothing there is an unlogged defect.
- The audit scored the app **~55%** at the start ("structurally recognisable but not close to 1:1").
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
| 12 | Final sweep | done |

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

The Explorer + detail-views audit has run. Its findings are listed below with the ones already
closed struck through; the rest are ranked and ready to pick up.

**Closed from that audit:** the lock toggle's editing wash (it was set on the Button but silently
clobbered by the rest-state rule targeting `PART_ContentPresenter` directly — measured 18 wash
pixels before, 336 after); the status bar's editing state taking the accent colour; the
theme-stuck card gradients (see the new Critical note under Known deviations); `DNA SPACE` wording;
`8 × 8` canvas formatting; `SATURATION` spelled out; the `cwmodel` chip no longer repeating the
value-map aside.

**All closed since.** The detail pane header (both pane hairlines now land at y=98, previously 98 and
74); identity chips as label+value with `colorize` added and the redundant Name chip dropped; rule
chips losing the invented WHEN/THEN/WITH and showing resolved **names** rather than raw ids; Custom
colorways getting its own swatch-and-note branch; the die-face reroll glyph; the rule-count pill
replacing a button whose command body was empty; and the New Recipe resulting-mix readout.

### Not done, and why

Both of the blocked items below were **since unblocked and shipped** (2026-08-04) — kept here only
so the reasoning is not lost:

- ~~**`target supply` chip**~~ — needed a model field. `CookBookManifest.TargetSupply` was added as
  an optional post-v1 field; the chip and the cookbar's "Target supply N of M unique DNA" now render.
- ~~**`status ● Valid` chip**~~ — needed `Validator` wired in. Done, **and** the status bar's
  unconditional "Valid" it warned about is fixed: both report the real result now.

Genuinely still open:

- **The mockup's wide left-aligned `?` popover** — Avalonia's stock ToolTip is narrower and follows
  the pointer. The affordance and the copy are faithful; the popover's shape is not.

**Superseded — the original list, kept for provenance:**

- **[High] The detail pane has no header row.** `ExplorerView.axaml` wraps `CurrentDetail` in a bare
  `Border.pane last` with no `.pane-h`. The mockup's `.pane-h.detail-h` is a 41px band with a type
  icon, the item title, an item count and a right-aligned view-tag chip
  (`COOKBOOK`/`RECIPE`/`INGREDIENT`). Measured effect: the Contents pane's content starts at y=99
  (below its 41px header) while the detail pane's starts at y=74, so paired panes are misaligned by
  25px. Needs a small shared surface (title/icon/count/tag) on each detail ViewModel.
- **[Medium] Identity chips.** Three render (`VaporPets`, `VP`, `canvas 8 × 8`) against the mockup's
  five. The Name chip is redundant with the heading right above it and is not in the mockup at all;
  `status ● Valid` is missing and needs no new data; `colorize` and `target supply` need modelling
  first. Chips also lack the mockup's muted-label / bold-value split.
- **[Medium] Recipe rules chip grammar.** Rows render synthetic `WHEN` / `THEN` / `WITH` labels; the
  mockup has no such words — each rule is two stacked `.rchip`s, the ingredient name as a tiny
  muted caption and the variant name as the bold headline, with the relationship carried by stacking
  order plus the icon's tooltip. Also check `RuleTargetRow`: it binds `IngredientId`/`VariantId`, so
  it shows raw ids, which happen to equal names only in the hand-authored fixture.
- **[Medium] Custom-kind colorways.** Routed through the same axis-row template as Dynamic/Static.
  The mockup gives Custom its own branch: a 56×56 swatch of the selected variant plus the sentence
  "Full-colour image, composited as-is. Value-map colorization does not apply to this layer."
- **[Low] Reroll uses `IconReroll`** (a circular arrow borrowed from the editor) where the mockup's
  `.dice` button is a literal die face with four pips. Needs a new `IconDiceFace` geometry.
- **[Low] The hero's `.hflag` rule-count pill is missing** — the mockup shows `⚑ N rules` only when
  the ingredient is in a rule. The app instead shows an always-visible "Jump to rules" button whose
  command is an empty stub.
- **[Low, no action] The titlebar Kitchen chip is hardcoded "Kitchen".** Kitchens are not modelled
  yet (`OpenKitchen` is a `_notify.Report` stub), so this is a defensible placeholder.

Set browser was reviewed and found clean in both themes. Note it has no 1:1 mockup section to diff
against — `explorer.html`'s tree never descends past Ingredient — so it was judged for internal
consistency with the app's own `idchip`/`data-row` vocabulary.

- The nine `?` help badges the wizards' mockups show beside labelled fields — **done**, eight exist
  (the count was 8, not 9).
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

## A gradient in a Style Setter does not follow the theme

Worth its own heading because it shipped a Critical and is invisible three ways over.

A `LinearGradientBrush`/`RadialGradientBrush` built **inside a Style `Setter`** with
`DynamicResource` GradientStops does **not** re-resolve when the theme flips. The brush is
constructed once and shared, and its stops keep whatever values they had when the Style was first
applied. `Border.cbk-id` therefore painted the *light* gradient in dark mode — near-white text on
cream, functionally illegible — while every solid-brush token on the same card was correct.

It is invisible in light mode, invisible in the markup, and invisible to any test that checks one
theme. **Define the whole brush per theme dictionary instead** (`CardGradientBrush`,
`ModalGlowBrush` in `Tokens.axaml`), which is the mechanism every other brush here already uses.
`ThemedGradientTests` pins it: both keys must exist in both dictionaries and differ between them.

Note `Border.modal-body` had the identical construction. Its gradient is a near-theme-invariant
accent wash over a correctly-themed panel, so the symptom was *invisible rather than absent* — a
good reminder that "it looks fine" is not evidence the construction is sound.

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

---

## After the slices (2026-08-04/05)

The visual pass was the last GUI *slice*, but not the last work. Recorded here because this file is
where someone will look:

- **Avalonia 11.2.3 → 12.1.1**, verified by pixel-diffing every frame rather than by the suite passing.
- **Honesty fixes** — several controls asserted things the app had not checked or done: a hardcoded
  "Valid" that never ran `Validator`, a "Delete variant" that reported *not wired* while the editor
  had a real delete, a working Set browser rendered as "coming soon", and a New Recipe *Kitchen*
  destination that was accepted and then silently ignored.
- **Cooking into an existing Set overwrote it.** The GUI never passed `existingDnas`/`startNumber`,
  so `SetWriter` renumbered from 1 over the previous assets. Data loss, not a missing feature.
- **Schema made evolvable** (`Oldest..Current`, was exact equality), plus an optional `TargetSupply`.
- **The Kitchen** — the sixth domain word. `docs/superpowers/specs/2026-08-04-kitchen-design.md`.
- **`stats`, `inspect` and `preview` reachable from the app**, rendered by Core so the output is
  byte-identical to the commands' rather than merely similar.

- **The editor's tools finished** (2026-09-04). Rectangle/circle/triangle had no rubber band — you
  dragged blind and found out on release — and Select marked nothing and moved nothing. Both now
  draw on a hit-test-invisible overlay above the canvas; Select marks on a drag from outside and
  moves on a drag from inside; **Line** was added (a two-point `BrushStroke`, no new Core command),
  which is a deliberate divergence from `ingredient-editor.html`. Its 36px cost overran the
  toolstrip and pushed the brush-size field off the pane — caught in a frame, now guarded by
  `ToolstripLayoutTests`.
- **`INotYetWired` deleted** (2026-09-04). Nothing had called `Report` for some time, so the shell's
  `"Not wired yet: {a}"` handler could never fire: an interface, an implementation, a DI
  registration, a constructor parameter on eleven ViewModels and a fake threaded through fifty test
  files, all inert. `IStatusService` is the only status channel now. What survives is the *lesson*,
  in comments at each former call site: gated is not unbuilt, and a working feature must never
  announce itself as missing.

**The lesson this project kept re-teaching**, six separate times: a state no capture fixture reaches
renders nothing and therefore looks fine. The editor's enabled toolstrip, the dynamic colorways band,
two of three detail-header variants, the rule pill, the resulting-mix panel, and the Reports/Export
buttons were each invisible-but-passing until a fixture was given what it needed to reach them. When
a fixture cannot reach a state, add a frame or a test — "it looks right" is not evidence it works.
