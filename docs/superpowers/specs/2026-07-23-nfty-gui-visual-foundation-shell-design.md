# nfty GUI — Visual Foundation + Shell Chrome (design spec)

**Date:** 2026-07-23
**Status:** Approved (design), pending implementation planning
**Scope:** The first slice of the **visual-fidelity pass** — the shared Avalonia style library every
screen composes, plus restyling the existing window chrome to match the locked mockups. This slice
builds the visual *vocabulary*; per-screen layout fidelity (Explorer, Landing/Help, Editor, Wizards)
follows in its own slices, each consuming these styles.
**Builds on:** the merged behavior slices (Phase 1 shell, Phase 2a Open→Explorer, imaging bridge). The
colour token block is already ported verbatim into `Themes/Tokens.axaml`; the window shell *structure*
(custom titlebar, page host, dialog scrim, status bar) already exists in `MainWindow.axaml`.

## 0. Program bar (applies to this slice and every remaining Desktop slice)

The finished Desktop app must be **rock-solid, stable, gorgeous, and near-identical to the mockups**
(`docs/design/mockups/*.html`), functioning logically and efficiently. Best practices throughout;
**pull official docs (Avalonia 11.2 / Context7) rather than assume** any objective API/behaviour; verify
claims before relying on them; and **escalate anything that doesn't sound right** to the user instead of
guessing. The mockups are the pixel reference. Avalonia is not a CSS engine, so the acceptance bar is a
**faithful visual mirror judged side-by-side**, not literal pixel arithmetic — where a CSS effect has no
exact Avalonia equivalent, approximate it as closely as the framework allows and note the deviation.

## 1. Goals & non-goals

**Goals**
- A cohesive, reusable style library (`Themes/Styles.axaml` + `Themes/Tokens.axaml` additions) that
  mirrors the mockups' shared primitives: typography, buttons, surfaces, chips, tables, inputs.
- The window **chrome** (titlebar/brand/window-controls, status bar, outer frame) restyled to the
  mockup.
- Fix the current base-font bug (whole window is mono; the mockups use **sans for body, mono only for
  IDs/headings/wordmark/crumbs/kind labels**).
- Zero new palette colours — the locked token block is authoritative; only font/shadow/spacing
  *resources* are added.

**Non-goals (this slice)**
- Per-screen **layouts**: Explorer tree/detail/rail, Landing hero/entrypoints, Help, Ingredient Editor,
  the three wizards. Each is a later slice that composes these styles into the mockup layout.
- Any `Nfty.Core`, ViewModel, or behaviour change. This slice touches **styles and XAML chrome only**;
  no VM property, command, or service changes.
- New colours, new fonts beyond the two mockup stacks, or a golden-image test harness.

## 2. Components

### 2.1 Token additions (`Themes/Tokens.axaml`)
- `SansFontFamily` = `-apple-system, Segoe UI, Helvetica Neue, Arial, sans-serif` (the mockup
  `--font-sans`, minus the macOS-only leaders Avalonia can't resolve on Windows — the trailing
  `Segoe UI`/`Arial`/`sans-serif` still resolve correctly per-platform).
- Update `MonoFontFamily` to the mockup `--font-mono` order:
  `SF Mono, JetBrains Mono, Cascadia Code, Menlo, Consolas, monospace`.
- A shared shadow resource (a `BoxShadow`/`BoxShadows` value) for panels/cards/frame, using a **neutral
  black-alpha** colour (a shadow, not a palette hue — this is not token drift). The mockups use ~14
  subtle shadows; one or two shared shadow tokens (e.g. a soft panel shadow and a stronger window
  shadow) cover them.
- Radii (`RadiusWin`=10, `RadiusMd`=8, `RadiusSm`=5) already exist; reuse. Add an `x:Double` for any
  spacing constant used widely if it reduces repetition (optional, YAGNI).

The comment block in `Tokens.axaml` stays: the mockup is the source of truth; no hex is edited without
first updating the mockup.

### 2.2 Typography (`Themes/Styles.axaml`)
- Base `Window`/`TextBlock` `FontFamily` → **`SansFontFamily`** (replaces the current all-mono default).
- Mono applied via **style classes** only, matching the mockup's mono usages, with the mockup's font
  sizes, weights, letter-spacing, and `tabular-nums` where the mockup uses `font-variant-numeric`:
  `.wordmark`, `.mono`, `.section-h`, `.crumbs`, `.kind-txt`, `.idchip`. Keep `.muted`
  (→ `FgMutedBrush`). A small heading scale (e.g. `.h-title`, `.h-section`) captures the recurring
  header sizes.

### 2.3 Buttons & interactive
- Refine `Button.tbtn` and `Button.accent`: add real `:pointerover` and `:pressed` visual states
  (background/border shifts drawn from existing tokens — e.g. hover → `BgAlt2Brush`/`AccentLineBrush`),
  correct padding/radius, and a subtle focus adorner.
- Add `Button.ghost` (borderless, subtle-hover), `Button.icon` (borderless icon button for the window
  controls + help + inline actions — replaces `tbtn` on min/max/close), and `Button.dice` (the ~27px
  reroll square with accent-line hover).
- Interaction must read correctly: hover/press/focus states present and consistent across button
  classes.

### 2.4 Surfaces & chips
- `Border.panel` / `Border.tile` / `Border.card`: backgrounds (`PanelBrush`/`TileBrush`), `LineBrush`
  borders, appropriate radius, and the shared shadow where the mockup shows elevation.
- `Border.frame`: the outer window frame border + `RadiusWin` + window shadow.
- `Border.idchip`: mono pill (bordered, `tabular-nums` value). Kind chips as
  `.kind-dynamic` / `.kind-static` / `.kind-custom` using the existing `KindDynamicBrush` /
  `KindStaticBrush` / `KindCustomBrush` (coloured marker + mono label).

### 2.5 Inputs
- Base restyle of `TextBox`, `Slider`, `NumericUpDown`, `RadioButton`, `CheckBox` to the mockup (panel
  background, `LineStrongBrush` borders, accent focus/thumb/checkmark) — reused by the editor and
  wizards. **Mechanism to confirm against Avalonia 11.2 docs during planning:** restyling built-in
  templated controls faithfully typically uses **`ControlTheme`s** (`BasedOn` the Fluent theme's control
  theme) rather than plain class `Style`s for template-level changes; simple property tweaks use
  `Style`. The plan will pull the current Avalonia styling/ControlTheme docs (Context7) and pick the
  correct mechanism per control rather than assume — no guessing on the templating API.

### 2.6 Shell chrome (`MainWindow.axaml`)
- Titlebar: height to the mockup (~46px), brand = the rotated-accent-square tile + mono wordmark;
  window controls (min/maximize/close) become borderless `Button.icon`s with proper hover; keep the
  existing commands/bindings unchanged (structure and VM untouched — restyle only).
- Status bar: restyle to the mockup (heights, spacing, the zoom/help controls as `icon` buttons).
- Outer `frame`: apply `Border.frame` around the page host so the window reads as the mockup's framed
  surface. The `ContentControl` page host and the dialog scrim keep their current structure; the scrim's
  look is refined to the mockup's modal treatment (overlay + centred dialog).
- No `ShellViewModel` change; no command/keybinding change.

## 3. Data flow / behaviour
None changes. This slice is presentation-only: it edits `Themes/Tokens.axaml`, `Themes/Styles.axaml`
(and, if `ControlTheme`s are used, a small resources file they live in), and `MainWindow.axaml`. Every
existing binding, command, and VM stays exactly as-is. The three theme-aware behaviours already present
(light/dark via `ThemeDictionaries`, the theme toggle, zoom) must continue to work: all new styles pull
colours from `DynamicResource` token keys so both `ThemeVariant`s remain correct.

## 4. Testing & acceptance
- **Build 0 warnings**; `dotnet build src/Nfty.Desktop` and the full solution build clean.
- The existing headless **ViewLocator smoke** (`SmokeTests`) stays green — every page/dialog VM still
  resolves and constructs with the new styles applied (a broken style/resource reference surfaces here
  as a load failure).
- One light **`[AvaloniaFact]`** asserting the merged style/token dictionaries load and a representative
  styled control resolves its class (e.g. a `Button` with class `accent` gets the accent background, or
  a `Border.panel` resolves `PanelBrush`) — enough to catch a broken `Styles.axaml`/`Tokens.axaml`
  without a golden-image harness. **No pixel/golden-image tests** (per CLAUDE.md).
- **Manual acceptance:** run `dotnet run --project src/Nfty.Desktop` and compare the chrome + a screen
  that already exercises the primitives (the Explorer, even at its pre-fidelity layout) **side-by-side
  with the mockup** in **both light and dark**, at a couple of zoom levels. The bar is a faithful visual
  mirror; log any CSS effect that Avalonia can only approximate.
- Determinism/behaviour regression: the full existing suite (`dotnet test nfty.sln`) stays green — this
  slice must not change any test outcome, since it changes no behaviour.

## 5. Open items / deferred (reserved)
- Per-screen layout fidelity slices: **Explorer**, **Landing + Help**, **Ingredient Editor**,
  **three Wizards** — each consumes this style library.
- Any custom-drawn flourish the mockup has that a style can't express (e.g. the colorways hue-band
  gradient, the brand tile's exact geometry) is handled in the screen slice that owns it, or as a small
  reusable control if it recurs.

## 6. Out of scope
- `Nfty.Core`, ViewModels, services, commands, keybindings — unchanged.
- New palette colours; new fonts beyond the two mockup stacks; a golden-image test harness.
- Per-screen layouts (this slice is the shared vocabulary + window chrome only).

## 7. Risks & escalation
- **Avalonia ≠ CSS.** Some mockup effects (conic/gradient fills, exact shadow falloff, certain hover
  transitions) have no 1:1 Avalonia equivalent. Approximate faithfully; where a gap is visible enough to
  matter, **escalate to the user** with the options rather than silently diverging.
- **ControlTheme vs Style** for input restyling is an objective API question — resolved by pulling
  Avalonia 11.2 docs during planning, not by assumption.
- **Font availability.** The mono/sans first-choice families may be absent on a given OS; the stacks
  fall back to `Consolas`/`Segoe UI`/generic, which is acceptable and matches the mockup's own fallback
  intent.
