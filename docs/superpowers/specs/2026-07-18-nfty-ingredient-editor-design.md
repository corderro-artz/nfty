# Ingredient Editor — design spec

**Date:** 2026-07-18
**Status:** Approved (design), pending implementation
**Deliverables:**
- `docs/design/mockups/ingredient-editor.html` — a new self-contained HTML mockup (direction **B**)
- `Nfty.Core.Editing` — a new library namespace (the real, tested engine behind the editor)

**Companion to:** `docs/design/mockups/explorer.html`, `landing.html`, `help.html` — same locked vaporsoft look.

## Purpose

A graphical editor for **creating and editing one Ingredient** — a single layer / trait-category — by painting
the grayscale **value-maps** of its variants and configuring how they colorize at cook time. It is the authoring
counterpart to the read-only Explorer: where the Explorer *browses* a cookbook, this *makes* the layers that go
into one.

This cycle covers the **value-map mode** only (static + dynamic layers, which are grayscale value-maps that
differ only in colorization). The full-color **custom mode** is deferred (below).

Unlike the other three mockups, this feature **includes `Nfty.Core` changes** — the editor is inherently
graphical, so its document, paint operations, and export live in the library where a future Avalonia GUI can
consume them. There is **no CLI surface**: painting cannot be driven from `Nfty.Cli`, and none is added.

## Vocabulary — literal machinery, metaphorical identities

The library's established split holds and is reaffirmed here:

- **The five domain identities keep the cooking metaphor** everywhere (lib + CLI + UI): CookBook, Recipe,
  **Ingredient**, **Variant**, Set. These are the ubiquitous language and the physical format identity
  (`.cbk`/`.rcp`/`.igt`/`.set`).
- **Machinery and roles stay literal:** `Colorizer`, `Compositor`, `Rng`, `Dna`, `LayerKind`, and the new
  `ValueMap`/`Brush`/`EditHistory`. The new namespace is `Nfty.Core.Editing`, deliberately **not** "Authoring"
  or "Minting" — those web3/whole-collection terms belong only to the **cook** path (`Generation` + `Output`).

## Settled decisions

Each was chosen by the user from rendered visual options during brainstorming (direction B, with iterations).

### 1. What it edits — one whole Ingredient, single flat raster per variant

One editor session edits **one Ingredient** and all of its **Variants**. Each variant is a **single flat
grayscale + alpha raster** — there is **no paint-layer stack**, because the Ingredient *is* the layer. The only
multiplicity is the variant filmstrip. Every variant is bound to the **CookBook canvas** size (the single source
of truth for dimensions), so the editor never produces an off-canvas image.

### 2. Layout — direction B (the Explorer's own grammar)

Chosen over a canvas-dominant "studio" layout (A) and a split grayscale/preview layout (C) because it reuses the
Explorer's **tree ▸ detail ▸ rail** rhythm verbatim, so the editor reads as the same app. Three regions inside
the standard 1180px frameless window:

- **Left — Variants filmstrip:** the Ingredient's variants as cards (thumbnail + name + **editable weight** +
  rarity %), the active one accent-highlighted; `+ Add variant`, with duplicate / delete. This is the switcher
  for *which variant you are painting* — the Explorer's variant table turned vertical.
- **Center — tool strip + canvas:** the toolset over a single-layer grayscale canvas drawn on a transparency
  checker (alpha matters).
- **Right — Colorize rail:** live controls for how the layer colorizes (§5). No tabs.

### 3. Always in edit mode — no lock

The Explorer opens **read-only** and gates its limited editing behind a lock (to prevent misclicks while
browsing). The Ingredient Editor is the opposite: it exists **only to edit**, so it is **always live** — there is
no lock toggle. Every control is directly interactive: variant weights are inline inputs, the Colorize rail is
live controls (not a read-only card), tools are always armed. The titlebar breadcrumb carries an `editing
value-map` state marker instead of a lock flag.

### 4. Toolset — one grouped strip, single flat raster

Left to right: **brush · eraser │ rectangle · circle · triangle │ select-region · fill │ value ramp │ undo ·
redo** — all in the house SVG icon language.

- **Brush / eraser** — paint a grayscale value; the eraser writes **alpha**.
- **Rectangle / circle / triangle** — basic shape fills.
- **Select-region** — marquee-select a block of pixels and **move** it (still one flat raster; no layers).
- **Fill** — flood fill a value.
- **Value ramp** — the current grayscale value (0–255) + its swatch.
- **Undo / redo** — backed by the reversible-command history (§ Library).

Canvas **modifier keys** (ctrl-drag for a straight line, shift to lock a shape's aspect) are **deferred** — the
tools ship first, the modifiers layer on later.

### 5. Colorize rail — live controls, not a card

The rail is where static-vs-dynamic and the color ranges are set, **in real time**, with the preview updating
as you go. It is a control surface, not an information panel:

- **Mode — a segmented toggle: `Static` | `Dynamic`.** Switching it swaps the controls below and changes the
  Ingredient's `LayerKind`.
- **Dynamic** (colour *rolled* per asset): a **hue-range** dual-handle slider over a hue track + numeric
  min/max, a **saturation-range** dual-handle slider + numeric min/max, and a compact **quantize step** control
  (degrees-per-hue-bucket / percent-per-sat-bucket). Quantize is **granularity, not a count**: it sets how coarsely
  a rolled `(H,S)` is snapped when computing **DNA**, which changes the size of the unique-DNA space and the minimum
  colour gap between two assets — it does **not** change the appearance of any single asset (pixels always use the
  exact continuous rolled colour). `Value` is a locked read-out: **`← from grayscale`**.
- **Static** (one *fixed* colour, deterministic): a **single** hue slider + saturation slider (or a swatch that
  opens a picker) yielding one colour; `Value` again **`← from grayscale`**.

Only **H** and **S** are ever editable — value/lightness always comes from the grayscale map, matching
`Imaging.Colorizer`. Colours are entered/shown per the existing color-spec rules.

### 6. Preview blip — always-visible, on the canvas

A small live-**colorized** preview sits pinned in a corner of the canvas, showing what the **cook** produces from
the current grayscale variant + a rolled colour. Controls are an integrated overlay strip (not a padded card):
**⟳ reroll** the sampled colour, **⤢ enlarge** (a bigger floating preview), **⛶ fill pane** (preview temporarily
covers the whole canvas; tap again to return). This keeps the grayscale→colorized relationship — the point of
the tool — in view while painting.

### 7. New-ingredient sizing — inherit the canvas

The canvas size is **not** a free choice. Created in context (from a recipe or the cookbook), a new Ingredient
**inherits the open CookBook's canvas** — shown, not chosen — because the canvas is the single source of truth
and every layer must share it for compositing. The **only** case that *selects* a size is a standalone new
Ingredient with no cookbook open, which picks the canvas of an existing cookbook. This enforces the user's
requirement that ingredient size derive from existing cookbook sizes.

### 8. Entry points

The editor opens from two places. **Neither affordance exists yet** — the editor screen is new, so nothing
references it today; adding these is downstream wiring (below), not part of this cycle's editor mockup:

- **From the Explorer** — a context-aware **`+ New ingredient`** action (the existing Add path) opens the editor
  on a **new** draft inheriting the open cookbook's canvas (§7); an **edit / pencil** affordance on a selected
  Ingredient **re-opens** that ingredient for editing.
- **From the Landing view** — a **`+ New ingredient`** entry alongside the existing New / Open CookBook actions,
  for starting an ingredient with **no cookbook open** (the standalone-size case, §7).

Both open the same editor.

## Library — `Nfty.Core.Editing`

A new namespace, sibling to `Generation/` and `Imaging/`, framework-agnostic and CLI-free. It depends
**downward only** — on `Model`, `Imaging`, `Formats`, and `Generation`'s `Rng`/`ColorRoller` — and nothing
depends back on it, so it stays an isolated, testable leaf.

| Type | Role |
|------|------|
| `ValueMap` | Mutable single-layer raster backed by a **raw value+alpha byte buffer** (grayscale by construction), bound to a `Dimensions`. Materializes an `Image<Rgba32>` only at export/preview. |
| `Brush` | Brush settings — size, hardness, value (0–255). |
| `IEditCommand` | One **reversible** edit: `Apply` / `Undo` over the affected pixel region only (memory-light). |
| `BrushStroke`, `EraseStroke`, `FloodFill`, `DrawShape`, `MoveSelection` | `IEditCommand` implementations (one per tool). |
| `ShapeKind` | `Rectangle` \| `Ellipse` \| `Triangle`. |
| `Selection` | Marquee bounds + lifted pixels, for select-region + move. |
| `EditHistory` | Undo/redo stack of `IEditCommand`. |
| `VariantDraft` | Editable variant — id, name, weight, `ValueMap`. |
| `IngredientDraft` | Editable ingredient — name, `LayerKind`, `Colorization`, canvas `Dimensions`, `List<VariantDraft>`. |
| `IngredientDraftExporter` | Turns a draft into `(IngredientManifest, {variantId → Image<Rgba32>})` for the existing `Formats.IngredientArchive.Write`. |
| `ColorizedPreview` | `VariantDraft` + `Colorization` → preview `Image<Rgba32>`, reusing `Imaging.Colorizer` and (for a dynamic roll) `Generation.Rng`/`ColorRoller`. The preview uses the **real cook path**, so it is truthful. |

**Two settled engineering choices** (chosen by the user):

- **Undo = reversible commands** (region-based), not full-raster snapshots — cheap even at 1000×1000, precise,
  unit-testable per op.
- **`ValueMap` = raw value+alpha buffer**, not a wrapped `Image<Rgba32>` — grayscale is guaranteed by the data
  shape, enforcement is free, and Core stays decoupled from ImageSharp's mutable-image surface. One explicit
  conversion point, at the `.igt` boundary.

**Custom disposal contract** holds: `ColorizedPreview` and `IngredientDraftExporter` return live
`Image<Rgba32>` objects the caller must dispose, consistent with the rest of `Nfty.Core`.

## Save / integration seam

`IngredientDraftExporter` produces the manifest + images; **where they go** has two targets:

1. **Into the open CookBook** — splice the exported ingredient into its `LoadedRecipe` (updating the recipe's
   `LayerOrder` if new), then persist the whole book via `Formats.CookBookArchive.Write`. Because `Model` records
   are immutable, the splice is a small set of `with`-expression updates; a thin `Editing` helper
   (`CookBookEdits`) encapsulates it so the GUI doesn't hand-roll record surgery.
2. **As a standalone `.igt`** — `Formats.IngredientArchive.Write` directly, for a loose ingredient to import
   later (the Explorer already has an **Import .igt** path).

Grayscale and canvas size are guaranteed by construction, so a saved ingredient always passes the existing
`Formats.Validator`. That validator now **enforces** grayscale for dynamic/static variants (implemented this
cycle): any variant whose pixels aren't `R==G==B` is reported. Because `Generator.Generate` runs the validator
and throws on any problem, this is a **compatibility-affecting tightening** — a pre-existing `.cbk` whose
dynamic/static value-maps were not strictly grayscale will now fail generation instead of silently colorizing off
the red channel alone. That is intended (such an archive was already relying on undefined behaviour), but it is a
narrowing worth recording. Custom variants are exempt (full-colour by design).

## Style constraints

The Explorer's look is locked. This mockup **reuses, never redefines**:

- **The complete token block** — `:root`, the `prefers-color-scheme: dark` block, and both
  `:root[data-theme]` overrides — copied **verbatim** from `explorer.html`. **Inventing a colour is the drift
  signal.**
- Existing component idioms: `.titlebar` / `.brandtile` / `.wordmark` / `.crumbs` / `.wc`, the pane / `.pane-h`
  grammar, `.tbtn`, `.statusbar` / `.zoomctl`, `.pitch` / `.ghost` / `.frame` / `.window` / `.note`, and the
  variant-row and kind idioms.
- The `@media (prefers-reduced-motion: reduce)` block and the `:focus-visible` outline rule.

New CSS is limited to: the tool strip + tool buttons, the grayscale **canvas** on its transparency checker, the
**Variants filmstrip**, the **Colorize** live controls (segmented mode toggle, range dual-sliders, value read-out,
quantize steppers), and the **preview blip** with its overlay controls.

Structural conventions from `explorer.html`, all preserved: no `<!doctype>/<html>/<head>/<body>`; everything
inline, no external resources; theme-aware via `prefers-color-scheme` + a `data-theme` toggle; the theme toggle
is a `.ghost` button in `.pitch`, outside the `.window`; 1180px window width.

## Verification

1. Wrap with the charset shim from `mockups/README.md` before viewing locally (else `›`, `⌘`, `✕`, `⟳`, the tool
   glyphs render as mojibake).
2. Screenshot headless Chrome (chrome-devtools MCP unavailable; drive `google-chrome --headless`) in **both**
   light and dark.
3. Confirm the screen reads as an **editor**: variant weights are inputs, the Colorize rail shows live controls
   (mode toggle, draggable range handles), and switching **Static ⇄ Dynamic** visibly swaps the controls.
4. Confirm the preview blip's three controls (reroll / enlarge / fill-pane) and that there is **no lock** anywhere.
5. Diff the token block against `explorer.html` — it must match verbatim.
6. **Library:** exact-pixel tests for each paint op and for grayscale-by-construction on tiny `ValueMap`s;
   reversible-command round-trips (apply → undo restores pixels); a round-trip test for `IngredientDraftExporter`
   → `.igt` → read-back; and an assertion that `ColorizedPreview` matches the `Generation` cook path for a fixed
   seed.

## Deferred / downstream

- **Custom full-colour mode (2b).** Present only as an inert/reserved affordance in the mockup; not built. When
  it lands it produces a `Custom` ingredient (full-colour RGBA, `Colorization` null, composited as-is) and the
  canvas drops its grayscale constraint.
- **Canvas modifier keys** — ctrl-drag straight line, shift-lock aspect (§4).
- **Entry-point wiring.** The `explorer.html` and `landing.html` mockups don't reference this editor today;
  adding the `+ New ingredient` affordances (Explorer toolbar/ingredient hero, Landing action stack) to them is a
  follow-up once this editor mockup is locked — and touches their locked style, so it's its own small task.
- **Avalonia implementation** of the editor UI on top of `Nfty.Core.Editing`.
- The separate **literalization epic** (a literal-core / stylized-surface split) is explicitly *not* pursued —
  the metaphor stays on the five identities.

## Out of scope

- Any `Nfty.Cli` change — the editor is graphical; no command is added.
- The Set browser view (a different deferred feature).
- Any change to the five domain identities' names or the archive formats.
