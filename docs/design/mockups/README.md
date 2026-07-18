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

The **default view** — what the app opens on before a cookbook is loaded (the VS Code "Welcome tab"
equivalent). A two-column split inside the same frameless window: the wordmark at front-door scale
(46px) + tagline + a **Start** action stack on the left, **Recent** on the right.

- **Start** — `+ New CookBook` (accent) and `↗ Open CookBook…`, both carrying `⌘N` / `⌘O` hints, plus
  a **dashed, muted** `↗ Open a cooked .set…`. The buttons act on **CookBooks** (`.cbk`), never Sets —
  a Set is what *comes out* of pressing Cook.
- **Recent** — rows of `name` + `N recipes · N unique DNA` + path. The metrics are the same
  **unique DNA** term the Cookbook view headlines. First run shows a dashed zero state ("Nothing here
  yet") pointing back at **New CookBook**; toggle it with the **Recents** button above the window.
- **No toolbar.** With nothing open, search / Add / Delete / Import / lock are all meaningless, so the
  toolbar is *omitted rather than greyed* — it returns when a cookbook opens. The titlebar keeps its
  shape via a muted `— nothing open —` in the breadcrumb slot, and the statusbar reads
  `No cookbook open` (no `● Valid`, no counts — nothing to validate or count).

A quiet **Learn** link — "New to nfty? *The cooking metaphor* →" — sits below the Start stack, and a
`?` anchors the far-right of the status bar; both open `help.html`'s quick-reference sheet (as does
`⌘/`), so the help view is reachable whether or not a cookbook is open.

> The dashed `.set` action is **deliberately inert**: it reserves the shape for a Set browser that
> isn't built yet. When that view lands, the action goes live and the dashed treatment — which is what
> marks it as not-yet-real — should be reconsidered. (The Learn / docs entry point that used to be
> listed here is now built — see the Learn link and status-bar `?` above.)

Design spec: [`docs/superpowers/specs/2026-07-16-nfty-landing-view-design.md`](../../superpowers/specs/2026-07-16-nfty-landing-view-design.md).
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
