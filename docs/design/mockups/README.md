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

### Preview locally

The file has no `<!doctype>/<html>/<head>/<body>` — the publish host wraps it with a skeleton that
supplies `<meta charset="utf-8">` and a minimal CSS reset. To view it faithfully on your own
machine, wrap it first (otherwise glyphs like `×`, `›`, `⌘K`, tree carets render as mojibake):

```bash
{ printf '<!doctype html><html><head><meta charset="utf-8"><style>*{box-sizing:border-box}html,body{margin:0}</style></head><body>'; cat explorer.html; printf '</body></html>'; } > /tmp/preview.html
python3 -m http.server 8000   # then open http://localhost:8000/tmp/preview.html
```

Published (private) artifact, same URL across redeploys:
<https://claude.ai/code/artifact/04b18798-3fca-4bde-a434-1d848a8116c5>
