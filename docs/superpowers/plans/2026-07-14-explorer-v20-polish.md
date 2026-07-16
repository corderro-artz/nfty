# Explorer mockup — v20 visual-polish pass (MCP-driven)

**Date:** 2026-07-14
**Owner:** a fresh agent (start cold — this doc is the full brief)
**Target file:** `docs/design/mockups/explorer.html`
**Input state:** iteration **19** (Colorways panel, hero rule-flag, dedup, cookbook `colorize` chip — all landed)
**Output:** iteration **20** — the polished result, redeployed to the existing artifact URL.

## Goal

v19 restructured the Ingredient view (Colorways replaces the old Inspector rules/variant
sections, rules collapse to a hero flag + the Recipe rail, cookbook surfaces the colour model).
The *content* is right. This pass is **look-and-feel only**: tighten spacing, balance, and
dark-mode so every view reads as one polished system. **Do not add features or change
information architecture.** If something seems to need a structural change, stop and ask.

## Environment / preview

- **Screenshot with headless Chrome, not an MCP.** `node` (v22) exists but `npx` does **not**, so
  the `chrome-devtools`/`playwright` MCP servers cannot spawn — don't rely on them. Use
  `/usr/bin/google-chrome --headless … --screenshot` (with the click-injection + `data-theme`
  technique below). This is documented in the `browser-screenshots` auto-memory.
- The file has **no `<!doctype>/<html>/<head>/<body>`** — the publish host wraps it. To preview
  faithfully, wrap first (otherwise `×`, `›`, `⚑`, `←`, carets render as mojibake):
  ```bash
  cd docs/design/mockups
  { printf '<!doctype html><html><head><meta charset="utf-8"><style>*{box-sizing:border-box}html,body{margin:0}</style></head><body>'; cat explorer.html; printf '</body></html>'; } > /tmp/preview.html
  ```
- To reach a specific view in a static screenshot, append a click script before `</body>`:
  `<script>addEventListener("load",()=>setTimeout(()=>document.querySelector("[data-key='KEY']")?.click(),40))</script>`
  and pass `--virtual-time-budget=1500`. Keys: `cbk`, `r:Aurora`, `i:Aurora/Body` (dynamic),
  `i:Aurora/Eyes` (static), `i:Aurora/Accessory` (custom).

## Review matrix (capture before touching CSS, and again after)

For **each** of the 5 states above × **light and dark** (toggle `data-theme` via the header
"Theme" button or `document.documentElement.setAttribute('data-theme',…)`), at widths **1280,
1024, and ~820** (the narrow reflow point):

1. Screenshot.
2. Note every spacing/alignment/contrast issue.
3. Fix in `explorer.html`.
4. Re-screenshot the same cell and confirm.

## Known nits to start from (found during v19 verification)

- **Ingredient hero is right-heavy with empty space.** The rarity meters (`.rarity`, max-width
  340px) only fill the left; the right half of `.vhero` is blank. Either let the hero content
  breathe across the width, or reduce hero height so the void isn't conspicuous. Keep the
  `⚑ N rules` flag where it is (top-right of the name row) — that placement tested well.
- **Colorways panel vertical rhythm.** Check the gap between the hue band, `.cwranges`, and the
  `.cwnote`; make static/custom blocks feel the same height/weight as dynamic so switching
  layers doesn't jump.
- **Dark-mode audit of new elements:** `.hueband` border, `.cwmodel` chip border, `.hflag`
  wash/hover, the cookbook `colorize` chip, and the static `.cwswatch` border — verify contrast
  and that nothing glows or disappears on `#07080b`.
- **`Value ← value-map` glyph:** confirm the `←` renders (not tofu) in both themes and at zoom.
- **Static swatch fidelity (optional):** a flat swatch is fine, but a subtle value gradient
  (light→dark of the same H/S) would better convey "value from the map." Only if it stays simple.
- **Narrow width (~820):** confirm `.panes` horizontal scroll is intentional and the Colorways
  pane doesn't crush; check the recipe `.recipe-grid` and cookbook `.cbk-cols` reflow.
- **Cross-view consistency:** hero paddings, panel radii, and `.sub-h` label treatment should
  match between Recipe and Ingredient.

## Guardrails

- Preserve determinism/interactivity: variant select, sort, reroll, lock/edit, zoom, theme
  toggle, and the hero-flag → recipe navigation must all still work after CSS changes.
- Keep it **self-contained** (inline CSS/JS/canvas only — CSP blocks external hosts).
- Keep both `prefers-color-scheme` and the `data-theme` overrides in sync (edit both blocks).
- Match existing token usage (`--line`, `--bg-alt2`, `--r-md`, etc.) — no hardcoded colors.

## Acceptance criteria

- All 5 states × 2 themes × 3 widths screenshot cleanly: no overflow, no orphaned whitespace
  that reads as a bug, consistent rhythm, legible dark mode.
- No feature/IA changes vs v19; diff is CSS + minor markup only.
- Interactivity smoke-tested via injected click scripts / a real browser (click a variant, sort a
  column, toggle theme, click the hero flag → lands on the recipe rules rail).

## Redeploy (produces iteration 20)

Update the **existing** artifact in place (do not mint a new URL):
`https://claude.ai/code/artifact/04b18798-3fca-4bde-a434-1d848a8116c5`
Use the Artifact tool with `url:` set to that URL, `file_path` = `docs/design/mockups/explorer.html`,
a stable `favicon`, title `nfty — Explorer`, and `label` like `v20-polish`.
