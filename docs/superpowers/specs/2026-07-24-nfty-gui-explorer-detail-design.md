# nfty GUI — Explorer Detail Bodies + Rails (visual-fidelity, design spec)

**Date:** 2026-07-24
**Status:** Approved (design), pending implementation planning
**Scope:** Second Explorer visual-fidelity slice (Slice B). Style the three detail bodies to the mockup
(`docs/design/mockups/explorer.html`): the CookBook body (id-chips, metric tiles, mint-distribution bar),
the Recipe body (portrait hero + layer table + rules rail), and the Ingredient body (hero + variant
table + colorways rail). Slice A (shell + tree) is merged.
**Builds on:** merged visual foundation + Explorer shell. The detail VMs (`CookBookDetailViewModel`,
`RecipeDetailViewModel`, `IngredientDetailViewModel`) already expose the data (counts, unique-DNA, share
rows, hero/thumbnail/colorway bitmaps, layers, rules, variants). The capture harness renders the real
views.

## 0. Program bar
Near-identical to the mockup; best practices; pull Avalonia 11.2 docs rather than assume; **confirm
parity from a rendered screenshot** (capture harness), never from XAML; escalate anything off. Mockup is
the pixel source of truth; every colour/size/radius verbatim; colours via `{DynamicResource}` tokens only.

## 1. Goals & non-goals
**Goals**
- CookBook body: id-chips (name · symbol · `canvas W×H`), metric tiles (recipes/layers/variants + accent
  unique-DNA), a stacked mint-distribution bar + legend, Cook button.
- Recipe body: two columns — main (portrait hero in a tile + name/seed/reroll dice + mint-share, then a
  layer `data` table) + a rules rail (`rules-panel` with exclude/require operator badges + trait chips).
- Ingredient body: two columns — main (hero + name + kind sub, then a variant `data`/`vtable`) + a
  colorways rail (`cw-panel` with the colorway swatches + Hue/Sat/Value axis rows).
- No behaviour change: Cook/Reroll/OpenIngredient/edit stay their current stubs/nav.

**Non-goals (this slice)**
- Cook / edit / add / delete real behaviour. Landing/Help/Editor/Wizards (later slices). `Nfty.Core`
  change. New palette colours.

## 2. Components

### 2.1 CookBook body (`CookBookDetailView`)
Single column (mockup `.cbk-*`):
- **Id-chips** row: `idchip` pills for name, `symbol`, and `canvas W×H` (from `Name`, `Symbol`,
  `CanvasText`).
- **Metric tiles**: `.metric` boxes (bg-alt2, `LineBrush`, `RadiusMd`, mono value 19px + uppercase label)
  for `RecipeCount` recipes / `LayerCount` layers / `VariantCount` variants, plus an **accent** metric
  (`.metric.accent`, `AccentWash`/`AccentLine`, value in `AccentTextBrush`) for `UniqueDnaText` unique
  DNA. Laid out as a wrap/uniform row.
- **Mint distribution**: a `.distbar` (height 16, `RadiusSm`, `LineBrush` border, `overflow hidden`) of
  stacked segments — one per recipe, width = `SharePercent`%, fill = the recipe's **segment colour**;
  below it a `.distlegend` (swatch + recipe name + `SharePercent`% mono). Per-recipe rows may also show
  `DnaSpaceText`.
  - **Segment colour** (small VM addition): `RecipeShareRow` gains `Color SegmentColor` (or an `IBrush`),
    computed deterministically from the recipe id — hue = stable hash of id mapped to [0,360), then
    `HSV(hue, 0.5, 0.72)` → RGB (reuse `Nfty.Core.Imaging.ColorConvert`), mirroring the mockup's
    `hsv(r.hue, .5, .72)`. Presentation-only; no behaviour.
- **Cook** button (`accent`, existing `CookCommand`, stays a stub).

### 2.2 Recipe body (`RecipeDetailView`)
Two-column `Grid` (main `*` + rules rail, e.g. `~300`):
- **Main → rhero** (`.rhero`): the `Hero` image (~92, `RadiusMd`, in a subtle tile/gradient border) beside
  name (mono), `Seed {RollSeed}`, a **reroll dice** button (`Button.dice`, existing `RerollCommand`), and
  a mint-share line. Then the **layer table** (`table.data`): columns index · ingredient name · kind
  (kind-coloured chip/text) · variant count; each row is a button/row invoking `OpenIngredientCommand`
  with `LayerRow.Id`; header row mono uppercase 10.5px; row hover `BgAlt2`.
- **Rules rail** (`.recipe-rules` → `.rules-panel`, bg-alt2, `LineBrush`, `RadiusMd`): each rule a row
  with a `.rop` operator badge — **exclude** = `AccentText`/`AccentWash`/`AccentLine`, **require** =
  info(`KindDynamicBrush`) tinted — plus trait `.rchip`s (mono uppercase label + value) for the `when`
  trait and each target. Empty state: "No incompatibility rules" muted.
  - **Structured rules** (small VM change): replace `RuleRow(string Text)` with a structured row carrying
    `RuleType Type`, the `when` `(ingredient, variant)`, and the target `(ingredient, variant)` list, so
    the rail can render the operator badge + chips. `RecipeDetailViewModel` already has
    `recipe.Manifest.Rules`; map to the structured row instead of the flat string.

### 2.3 Ingredient body (`IngredientDetailView`)
Two-column `Grid` (main `*` + colorways rail, e.g. `~280`):
- **Main → vhero** (`.vhero`): `Hero` (~84, `RadiusMd`, `LineBrush`) beside name (mono 17px) + a kind sub
  line (`KindText` / `ColorwaysText`). Then the **variant table** (`table.data vtable`): thumbnail ·
  name · weight · in-recipe% · overall% (from `VariantRow`); header mono uppercase; the two sort headers
  (`SortByCommand`) styled as sortable columns; selected/hover row `AccentWash`/`BgAlt2`.
- **Colorways rail** (`.cw-panel`, bg-alt2, `LineBrush`, `RadiusMd`): the existing `Colorways` swatch
  bitmaps in a row/grid, then `.cwaxis` rows — **Hue**, **Sat**, **Value ← value-map** (mono uppercase
  axis label + value; the value row italic-muted for the "derived" value). Custom ingredient: "no
  colorize · composited as-is".
  - **Structured colorway axes** (small VM addition): expose the axis rows (label + value + is-derived)
    on `IngredientDetailViewModel` — e.g. `IReadOnlyList<ColorwayAxis>` derived from the ingredient's
    `Colorization` (or the existing `ColorwaysText` split), so the rail renders the `.cwaxis` rows
    faithfully. Presentation-only.

### 2.4 Shared styles (`Themes/Styles.axaml`)
New reusable styles mirroring the mockup: `.metric` (+`.metric.accent`), `.distbar`/`.distlegend`,
`table.data`/`.vtable` row+header (or a `DataGrid`-free `Grid`/`ItemsControl` table styled to `.data`),
`.rules-panel`/`.rop`(`.exclude`/`.require`)/`.rchip`, `.cw-panel`/`.cwaxis`, `.rhero`/`.vhero` tiles.
Reuse `idchip`, kind chips, `Border.tile/card`, `Button.dice` from the foundation.

## 3. Data flow / behaviour
Presentation only. The detail VMs are constructed by the Explorer on selection (unchanged). New members
are derived/display state (`RecipeShareRow.SegmentColor`, structured `RuleRow`, `ColorwayAxis` list).
Cook/Reroll/OpenIngredient/SortBy/Delete/Edit keep their current behaviour. Both `ThemeVariant`s correct.

## 4. Testing & acceptance
- **VM tests**: `SegmentColor` deterministic + stable per recipe id (same id ⇒ same colour); the
  structured `RuleRow` carries the right type/when/targets for an exclude and a require rule; the
  `ColorwayAxis` rows reflect a dynamic vs static vs custom ingredient.
- **Style-load** `[AvaloniaFact]`s: the new `.metric`/`.distbar`/`.data`/`.rules-panel`/`.cw-panel`
  styles resolve on a representative control (guard against a broken style).
- **Visual acceptance (required):** extend the capture harness to render each real detail view
  (`CookBookDetailView`/`RecipeDetailView`/`IngredientDetailView`) bound to fixture VMs, in both themes,
  and **view the PNGs** vs the mockup: metric tiles + distribution bar; recipe hero + layer table +
  rules rail (exclude accent / require info badges); ingredient hero + variant table + colorways rail
  with swatches + axis rows. Iterate until faithful. No golden-image tests.
- Build 0 warnings; full suite green; no raw hex outside `Tokens.axaml`.

## 5. Out of scope
- Cook/edit/add/delete/reroll real behaviour; Landing/Help/Editor/Wizards; `Nfty.Core`; mobile heads;
  live variant re-selection driving the ingredient hero swap beyond the existing `SelectVariant`.

## 6. Risks & escalation
- A faithful `table.data` may be built with a styled `Grid`/`ItemsControl` rather than `DataGrid` (avoid
  the heavier control) — confirm the cleanest Avalonia 11.2 approach for a header + hoverable rows; pull
  docs if unsure.
- Segment-colour hue derivation is a deterministic display choice mirroring the mockup; if a recipe count
  makes segments indistinct, keep it (matches mockup) and note it.
- If a structured `RuleRow`/`ColorwayAxis` needs data the manifest doesn't expose cleanly, escalate
  rather than invent.
