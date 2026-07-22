# GUI design mockups

Self-contained HTML prototypes for the cross-platform Avalonia GUI (built on `Nfty.Core`),
used to settle visual direction and interaction before writing any Avalonia code. Vaporsoft
brand (light + dark), oxblood accent `#a11f31`, neo-Japanese material restraint.

## explorer.html

The primary screen: an OpenIV-style **Explorer** — Contents tree ▸ type-aware detail, inside a
custom frameless window (own titlebar / toolbar / status bar). Each node type carries its own **icon**
beside the name (in the tree, breadcrumb, and content-pane header) — no stylized file extensions. Every
view is two-column (Contents ▸ detail); the Recipe and Ingredient details each carry a **right rail**
(≈⅓) as a full-height panel: the Recipe rail lists its **Rules**, the Ingredient rail is **Colorways** —
*how the selected layer is colorized* (a dynamic hue range, a static fixed colour, or a custom as-is
image), the one thing the main pane doesn't carry. Selecting a node renders a curated per-type view:

- **Cookbook** — an identity header (icon + name, description, symbol / canvas / **colorize-model** /
  target-supply chips; `colorize HSV` names how grayscale value-maps are recoloured collection-wide)
  above a two-column composition band: metrics (2×2, incl. **Unique DNA**) + mint distribution on the
  left, the per-recipe **DNA-space** breakdown (factor chips `4 × 3 × 5 × 6 = 360` colored by kind +
  share bars) on the right; Cook footer.
- **Recipe** — a main column (hero + full-width layer table) with a **Rules rail** (right ~⅓, a
  scrollable panel). The hero states the **unique-DNA** count as kind-colored factor chips
  (`[4] × [3] × [5] × [6] = 360`) that wrap for many-layer recipes. Rules are table-ized rows — a typed
  operator badge (`✕` never-together / `→` always-together) beside the two stacked trait chips — so the
  list scales and scrolls; empty-state when a recipe has none.
- **Ingredient** — a main column (art hero strip with selected variant image + name + in-recipe/overall
  rarity meters, updating live as you pick a variant, above a sortable variant table) beside the
  **Colorways rail** (kind-aware: dynamic hue-band + H/S ranges, static fixed colour, or custom as-is —
  value always from the grayscale map). The hero shows a compact **`⚑ N rules`** flag *only* when the layer
  participates in a rule; clicking it jumps to the Recipe's full Rules rail (the single home for the
  complete rule set). Unlock (top-right) to edit variant weights inline.

Every ingredient (layer) is one of **three kinds**, each with its own badge colour (tree marker,
kind text, factor chip): **dynamic** (blue — value-map, colour *rolled* per asset), **static** (amber
— value-map, a *single fixed* colour), and **custom** (violet — a full-colour image the user uploaded,
composited *as-is*, never colourized). Recipes deliberately mix all three (a core `Nfty.Core` feature).

**Verbiage:** the count of distinct possible assets is **unique DNA** (never "combinations" /
"unique images") — one DNA = a recipe + its rolled variants + colors, matching `Nfty.Core`'s DNA
hash. The cookbook surfaces it as the **Unique DNA** metric and the **DNA space** breakdown; keep
this term consistent across UX and code.

Everything is inline (CSS + JS + procedural `<canvas>` "pet" that demonstrates dynamic HSV
recoloring) — no external resources, theme-aware via `prefers-color-scheme` + a `data-theme` toggle.

Design spec: [`docs/superpowers/specs/2026-07-15-nfty-explorer-view-design.md`](../../superpowers/specs/2026-07-15-nfty-explorer-view-design.md).
This file **defines** the token block the other two mockups copy verbatim — every colour is a `var()`, and
a new hex value anywhere (here or in a sibling) is the signal that the style has drifted.

## landing.html

The **default view** — the single screen the app opens on before a CookBook is loaded (the VS Code
"Welcome tab" equivalent). A two-column split inside the frameless window: wordmark + tagline + the
**Create** and **Open** action groups on the left, **Recent** on the right. This absorbs the former
separate zero-state mockup — the first-run case is simply an empty Recent (illustrated beneath the
window), not a distinct screen.

- **Create** — `+ New CookBook` (accent, `⌘N`) and a dashed **New Kitchen…** (reserved), then a
  secondary row of `Recipe` and `Ingredient` — the three authoring vessels the `nfty new` CLI
  commands build.
- **Open** — `↗ Open CookBook…` (`⌘O`), `↧ Import…` (`⌘I`, kind-agnostic — `Archives.KindOf` resolves
  `.cbk`/`.rcp`/`.igt` from the extension), and a **dashed, muted** `↗ Open a cooked .set…` reserving
  the shape for a Set browser that isn't built yet.
- **Recent** — rows of `name` + metrics + path, mixing loose Kitchen items with CookBooks. First run
  shows a dashed **"Nothing here yet"** zero state pointing back at **New CookBook**.
- **No toolbar.** With nothing open, search / Add / Delete / lock are all meaningless, so the toolbar
  is *omitted rather than greyed* — it returns when a cookbook opens. The titlebar breadcrumb reads a
  muted `— nothing open —` and the statusbar `No CookBook open`.

A quiet **Learn** link — "New to nfty? *The cooking metaphor* →" — sits below the actions, and a `?`
anchors the far-right of the status bar; both open `help.html`'s quick-reference sheet (as does `⌘/`).

Design specs: [`2026-07-16-nfty-landing-view-design.md`](../../superpowers/specs/2026-07-16-nfty-landing-view-design.md)
(the base landing) and [`2026-07-19-nfty-creation-flows-design.md`](../../superpowers/specs/2026-07-19-nfty-creation-flows-design.md)
(the expanded Create/Open entry points, folded in here).
The token block is copied **verbatim** from `explorer.html` — every colour is a `var()`, and a new hex
value anywhere is the signal that the style has drifted.

## help.html

The built-in **quick reference** — a legend, not a docs site. A modal sheet summoned over a dimmed app
window and dismissed with `Esc`, defining the vocabulary the rest of the UI rests on in one glance:

- **The five words** — CookBook / Recipe / Ingredient / Variant / Set, each `icon → term → extension →
  one-line gloss`. The icon *is* the glossary bullet, so every symbol is defined exactly once. Two new
  icons join the family here: **Variant** (a single framed image — the singular counterpart to the
  Ingredient's layer stack) and **Set** (a 2×2 grid — input is a book, output is a grid).
- **Layer kinds + Rules & state** — the D/S/C kind letters in their hues, then `✕` never-together,
  `→` always-together, `⚑` layer-in-a-rule, `●` valid.
- **Keys + Colour** — the keyboard chords (`⌘/` opens this sheet), then the four colour-spec prefixes.
- **Unique DNA** spans the footer with the factor equation `4 × 3 × 5 × 6 = 360` pinned right.

Every glyph sits in one strict 20px gutter so all terms align; hairlines divide the columns, no boxes.
**Summoned from** the `?` at the far-right of the status bar, the **Learn** link on the landing view, or
`⌘/` anywhere — all open the one sheet. This **resolves** the landing view's reserved "Learn / docs"
entry point (below): that link is one of the three summon points.

Design spec: [`docs/superpowers/specs/2026-07-17-nfty-help-view-design.md`](../../superpowers/specs/2026-07-17-nfty-help-view-design.md).
Token block copied **verbatim** from `explorer.html`.

## ingredient-editor.html

The authoring counterpart to the Explorer: a graphical editor for **creating and editing one Ingredient** — painting
the grayscale **value-maps** of its Variants and configuring how they colorize at cook time. It reuses the Explorer's
own tree ▸ detail ▸ rail rhythm (direction B) as a three-pane window: a left **Variants** filmstrip (thumbnail +
name + editable weight + rarity, `+ Add variant`), a center tool strip (brush · eraser │ rectangle · circle ·
triangle │ select-region · fill │ value ramp │ undo · redo) over a grayscale canvas on a transparency checker, and a
right **Colorize** rail of **live** controls — a segmented **Static | Dynamic** toggle that swaps a dual hue-range
and saturation-range slider (Dynamic) for a single hue + saturation pair (Static), a locked **Value ← from
grayscale** read-out, and **Quantize step** steppers with the derived colour count. Unlike the Explorer, this screen
is **always in edit mode** — there is no lock toggle anywhere; the titlebar breadcrumb instead carries an `editing
value-map` state marker. A corner **preview blip** sits pinned on the canvas, showing the real colorized output of
the current grayscale variant with an integrated overlay strip (reroll / enlarge / fill-pane).

Design spec: [`docs/superpowers/specs/2026-07-18-nfty-ingredient-editor-design.md`](../../superpowers/specs/2026-07-18-nfty-ingredient-editor-design.md).
The token block is copied **verbatim** from `explorer.html`; every colour is a `var()`, and a new hex value anywhere
is the signal that the style has drifted.

## Creation flows — the wizards

The authoring entry points: creating a CookBook, Recipe, or Ingredient from nothing. Each is a
**single centered pane** (no sidebar — the one deliberate exception to the shared-rail layout, because
the field sets are 2–5 items). **Every field is grounded in `Nfty.Core`** — a wizard collects only what
a manifest stores or the `Validator` requires. Full rationale in the spec:
[`docs/superpowers/specs/2026-07-19-nfty-creation-flows-design.md`](../../superpowers/specs/2026-07-19-nfty-creation-flows-design.md).

- **`wizard-cookbook.html`** — New CookBook. Name, Symbol (1–255 bytes, empty allowed; 3–5 ticker is
  hover advice), Canvas W×H with an aspect-lock chain-link, Description. Two phantom fields cut: colorize
  model (lives on the Ingredient) and target supply (chosen at generate time, stored nowhere).
- **`wizard-recipe.html`** — New Recipe. Name + **mandatory** selection weight (a Recipe absent from
  `RecipeWeights` fails validation) with a live "Resulting mix" bar.
- **`wizard-ingredient.html`** — New Ingredient. Name, a 3-way **Kind** radio-card group, and a
  kind-dependent zone matching `Validator.CheckKind`: Dynamic → dual-handle Hue/Saturation
  `ColorRange` sliders (half-open, animated saturation preview); Static → one fixed-colour swatch;
  Custom → none.
The **Create** / **Open** entry points that launch these wizards (New CookBook / New Kitchen / Recipe /
Ingredient · Open CookBook / Import… / cooked `.set`) live on the Landing itself — see the
`landing.html` section above. (They were prototyped in a separate `landing-entrypoints.html`, now folded
into `landing.html`.)

All screens share the unified chrome: SVG icons, a persistent **`.kroot` Kitchen workspace chip** in the
titlebar (VS-Code title:path model — the workspace root, absent only on the Landing), a statusbar on
every screen, and lowercase counts (`3 recipes`) with type-names capitalized in prose.

## gallery.html — the tabbed review page

`gallery.html` stacks every mockup above behind a left-rail tab switcher, each in its own sandboxed
iframe at the app's **minimum window** (1180×760), theme-synced. Open it to see the whole design set at
once — the fast way to review a freshly-drafted mockup against its siblings. Keys `1`…`8` jump, `↑`/`↓`
step.

Regenerate after adding or editing a mockup:

```bash
python3 docs/design/mockups/build-gallery.py   # rewrites gallery.html
```

To add a mockup: drop the file in this directory and add one row to `SCREENS` in `build-gallery.py`.
(The generator base64-embeds each mockup so its inline `<script>` survives intact, and injects a
theme/layout normalizer that works across both token architectures — see the script's header.)

### Preview locally

Neither file has a `<!doctype>/<html>/<head>/<body>` — the publish host wraps them with a skeleton
that supplies `<meta charset="utf-8">` and a minimal CSS reset. To view one faithfully on your own
machine, wrap it first (otherwise glyphs like `×`, `›`, `⌘K`, `↗`, tree carets render as mojibake):

```bash
F=explorer.html   # or landing.html, help.html
{ printf '<!doctype html><html><head><meta charset="utf-8"><style>*{box-sizing:border-box}html,body{margin:0}</style></head><body>'; cat $F; printf '</body></html>'; } > /tmp/preview.html
python3 -m http.server 8000   # then open http://localhost:8000/tmp/preview.html
```

Published (private) artifacts — same URL across redeploys (pass the URL as `url` to the Artifact tool
from a later session, or a new one gets minted):

| Mockup | Artifact |
|--------|----------|
| `explorer.html` | <https://claude.ai/code/artifact/04b18798-3fca-4bde-a434-1d848a8116c5> |
| `landing.html`  | <https://claude.ai/code/artifact/6bfa007e-b4a6-48e4-8ec8-90f1d193e35f> |
| `help.html`     | <https://claude.ai/code/artifact/4b0a9a36-c264-4a28-94cb-99e06fa3d0d5> |
| `gallery.html` (all 8, tabbed) | <https://claude.ai/code/artifact/c8f1c7bb-d238-49a5-bd25-c8173e5c8c14> |
