# GUI design mockups

Self-contained HTML prototypes for the cross-platform Avalonia GUI (built on `Nfty.Core`),
used to settle visual direction and interaction before writing any Avalonia code. Vaporsoft
brand (light + dark), oxblood accent `#a11f31`, neo-Japanese material restraint.

## explorer.html

The primary screen: an OpenIV-style **Explorer** — Contents tree ▸ type-aware detail ▸ Inspector,
inside a custom frameless window (own titlebar / toolbar / status bar). Selecting a node renders a
curated per-type view:

- **Cookbook** — Composition: metric band, mint distribution, per-recipe combination-space
  breakdown (factor chips `4 × 3 × 5 × 6 = 360` colored by kind + share bars), Cook footer.
- **Recipe** — a full-width portrait hero (sample roll + dice reroll + combination math + compact
  stats) above a compact layer table.
- **Ingredient** — an art hero strip (selected variant + in-recipe/overall rarity meters) above a
  sortable variant table; unlock (top-right) to edit variant weights inline.

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
