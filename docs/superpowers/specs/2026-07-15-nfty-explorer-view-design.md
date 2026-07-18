# Explorer view — design spec

**Date:** 2026-07-15 (written retroactively — the Explorer predates its siblings)
**Status:** Locked (design), pending implementation — the reference the other two views build on
**Deliverable:** `docs/design/mockups/explorer.html` — the primary-screen HTML mockup (~iteration 22)
**Companion to:** `docs/design/mockups/landing.html` and `docs/design/mockups/help.html`, which copy this
file's token block and chrome verbatim

## Purpose

The primary screen: an OpenIV-style **Explorer** for a loaded CookBook. A **Contents** tree on the left,
a **type-aware detail** pane on the right, inside a custom frameless window that carries its own titlebar,
toolbar, and status bar (the OS frame is removed in the real app).

You browse a cookbook the way you'd browse a filesystem — expand the tree, select any node, and the detail
pane **adapts to what that node is**: a cookbook lays out its identity, composition, and DNA space; a recipe
pairs a sample roll with its layer stack and rules; an ingredient opens a sortable variant table beside a
live Colorways panel. It is the first and most-iterated mockup; the Landing and Help views were designed
against it and inherit its look.

This is a **mockup**, not Avalonia code. It exists to settle visual direction and interaction before any
GUI code is written. This spec documents the locked mockup after the fact, so its three siblings form one
described set.

## Settled decisions

Each was settled over the Explorer's iteration to ~pass 22, chosen by the user from rendered visual options.

### 1. Vocabulary — it browses a CookBook, three tiers deep

The tree is **CookBook › Recipe › Ingredient**, with **Variant** appearing one level further in as table
rows (never its own tree node). This is the locked domain "Model A": a CookBook is a container of
whole-template Recipes. The Explorer is the authored **input** side — a Set (`.set`), the cooked output of
pressing **Cook**, is a separate view that does not exist yet (see Landing spec).

Node types map to the three curated detail views below. Each node carries its own **icon** — in the tree,
the breadcrumb, and the detail header — rather than a stylized file extension.

**"Unique DNA" is required verbiage** — never "combinations", never "unique images". One DNA = a recipe +
its rolled variants + colours, matching `Nfty.Core`'s DNA hash. It surfaces as the cookbook's **Unique DNA**
metric, the **DNA space** breakdown, the recipe hero's factor total, and the Cook footer's supply line.

### 2. Layout — frameless window, tree + type-aware detail, an in-detail right rail

- **Two outer columns:** Contents tree (fixed ~286px) ▸ detail pane (fluid). The window is **1180px**, the
  same as every sibling.
- **A right rail (~⅓) lives inside the detail pane**, not as a third outer column. The **Recipe** detail's
  rail is **Rules**; the **Ingredient** detail's rail is **Colorways**. Each is a full-height panel carrying
  the one thing its main column does not.

Chosen over a flat three-pane split: the rail belongs to the detail it annotates, so it appears and
disappears with the view rather than sitting empty for the cookbook view (which has no rail).

### 3. Type-aware detail — one curated view per node

Selecting a node renders exactly one of three layouts. The detail header shows the node's icon, title,
a count, and a right-aligned **view tag** (`Cookbook` / `Recipe` / `Ingredient`).

- **Cookbook** — an **identity header** (icon + name, description, chips: symbol / canvas / **colorize HSV**
  / target supply / `● Valid` status) above a two-column composition band. Left: a 2×2 **metric** statband
  (Recipes, Layers, Variants, and **Unique DNA** in accent) then **Mint distribution** (a weighted bar +
  legend, one segment per recipe by roll weight). Right: the per-recipe **DNA space** breakdown — each row a
  recipe with kind-coloured **factor chips** (`4 × 3 × 5 × 6 = 360`) and a share bar reading `N% of DNA`. A
  **Cook** footer states `Target supply N of M unique DNA` beside the accent **Cook set** button.
- **Recipe** — a main column (hero + layer table) beside the **Rules rail**. The hero pairs a **sample pet
  portrait** (rerollable, below) with the recipe's unique-DNA count as kind-coloured factor chips
  (`[4] × [3] × [5] × [6] = 360`) plus weight / mint-share / variants stats. The layer table lists
  `# · Layer · Kind · Variants · Most common`; clicking a row opens that ingredient.
- **Ingredient** — a main column (art hero + variant table) beside the **Colorways rail**. The hero shows
  the selected variant's image, its name, kind, `variant N of M`, and **in-recipe / overall rarity meters**
  that update live as you pick a variant or edit a weight. A compact **`⚑ N rules`** flag appears *only* when
  the layer participates in a rule, and jumps to the Recipe's Rules rail. The variant table is **sortable**
  by Variant / Kind / Weight / In-recipe, with a preview swatch and an Overall-rarity column.

### 4. Three layer kinds, each with its own colour

Every ingredient is one of **dynamic** (blue, `--info` — value-map, colour *rolled* per asset), **static**
(amber, `--warning` — value-map, a *single fixed* colour), or **custom** (violet, `--custom` — a full-colour
image composited *as-is*, never colourized). The kind colour is consistent across its tree marker (the
`D`/`S`/`C` letter), its `kind-txt` label in tables, and its factor chip. Recipes deliberately mix all three
— a core `Nfty.Core` feature.

A boxed `.kbadge` treatment of the kind indicator exists in the stylesheet but is **held for later**; the
shipped marker is the plain `D`/`S`/`C` letter.

### 5. Rules — table-ized operator rows, one home in the Recipe rail

Rules render as rows: a typed **operator badge** — `✕` **never-together** (exclude, accent) or `→`
**always-together** (require, blue) — beside the two stacked trait chips it relates. The list scales and
scrolls; a recipe with none shows the empty state *"No incompatibilities — every combination is allowed."*

The **Recipe rail is the single home** for a recipe's complete rule set. The Ingredient view never restates
rules — it shows only the `⚑ N rules` flag that jumps there, so the full set lives in exactly one place.

### 6. Colorways rail — kind-aware, value always from the value-map

The Ingredient rail shows *how the selected layer is coloured*, switching on its kind:

- **dynamic** — `HSV · rolled`: a hue-band gradient, the Hue span + the cookbook's Sat range, and *"colour
  rolled per asset."*
- **static** — `HSV · fixed`: a single-hue value gradient, the fixed Hue + Sat, and *"one fixed colour, no
  roll."*
- **custom** — `no colorize`: the as-is image and *"composited as-is."*

In every kind, **Value reads `← value-map`** — lightness always comes from the grayscale source, never the
colour spec. This is the one fact the main pane cannot show, which is why the rail exists.

### 7. Lock / edit — the authoring seam

The Explorer opens **read-only**. A **lock toggle** at the right of the toolbar flips it to **editing**, a
state mirrored in the titlebar's `lockflag` and the status bar's state flag so it reads the same from any
edge. Editing turns variant weights into inline inputs (rarity meters recompute live), reveals per-row
delete, and enables the authoring toolbar buttons. Locked, those buttons are disabled rather than hidden —
the shape of the authoring surface stays visible.

### 8. Chrome — titlebar, toolbar, status bar

- **Titlebar:** brandtile + `nft<b>y</b>` wordmark, a `›`-separated **breadcrumb** (cookbook ▸ recipe ▸
  ingredient, each segment clickable and keyboard-navigable), the `lockflag`, and window controls
  (minimize / maximize / close).
- **Toolbar:** a **search** field (`Find recipe, ingredient, variant…` with a `⌘K` hint), a context-aware
  **Add** button (`Add recipe` / `Add ingredient` / `Add variant` by selection), **Delete**, **Import .igt**,
  and the **lock toggle** pushed right (`margin-left:auto`).
- **Status bar:** `● Valid`, the read-only/editing state, and the recipe / ingredient / variant totals, with
  the **zoom control** (`− %  +`, 50–300%, also `Ctrl ±` / `Ctrl 0`) right-aligned via `margin-left:auto`.
  The status bar's right cluster and this zoom control are what the Landing and Help views retain and reuse.

### 9. The procedural pet canvas

Both heroes and the variant/preview swatches draw a small procedural `<canvas>` **pet**, tinted by an HSV
roll, to *demonstrate dynamic recolouring* without shipping art. The recipe hero's **reroll** dice samples a
new body hue (weighted) with a spin; canvases redraw on theme flip and zoom so the demonstration stays
crisp. It stands in for `Nfty.Core`'s value-map colorization, which the real app performs on actual PNGs.

### 10. Icons — the three-node family

The tree/breadcrumb/header icons are a matched family: 24×24, `stroke="currentColor"`, a `--panel` body
fill, and exactly one `--accent` highlight. The Explorer defines marks for the three node types that appear
in a cookbook tree — **CookBook, Recipe, Ingredient**. (The **Variant** and **Set** marks were added later,
in `help.html`, when the glossary needed all five domain words.)

## Style constraints — this file is the lock

The Explorer's look is **the** lock; the other two mockups reuse it, never redefine it. This file is where
the shared vocabulary is **defined**:

- **The complete token block** — `:root`, the `prefers-color-scheme: dark` block, and both
  `:root[data-theme="light"]` / `[data-theme="dark"]` overrides. Oxblood `--accent: #a11f31`; the
  `--bg` / `--bg-alt` / `--panel` / `--tile` ramp; the kind hues `--info` / `--warning` / `--custom`;
  `--font-mono`; the `--r-win`…`--r-xs` radii. `landing.html` and `help.html` copy this verbatim.
- **The component idioms** other views draw on: `.stage` → `.pitch` → `.frame` → `.window` → `.note`
  scaffold, `.titlebar` / `.brand` / `.brandtile` / `.wordmark` / `.tdiv` / `.crumbs` / `.wc`,
  `.exp-toolbar` / `.search` / `.tbtn`, `.statusbar` / `.zoomctl`, `.ghost`, plus the kind, table, rules,
  and colorways idioms unique to this screen.
- **The `@media (prefers-reduced-motion: reduce)` block and the `:focus-visible` outline rule.**

**Inventing a colour is the drift signal.** Every colour is a `var()`; a new hex literal anywhere — here or
in a sibling — means a token was missed and the system has drifted.

Structural conventions, all preserved and inherited by the siblings: no
`<!doctype>/<html>/<head>/<body>` (the publish host wraps it); everything inline, no external resources
(including the procedural `<canvas>`); theme-aware via `prefers-color-scheme` plus a `data-theme` toggle; the
theme toggle is a `.ghost` button in `.pitch`, **outside** the `.window` — demo scaffold, not app chrome;
1180px window width.

## Verification

1. Wrap with the charset shim documented in `mockups/README.md` before viewing locally — without it `›`,
   `⌘K`, `✕`, `→`, `⚑`, `●`, `×`, `↗`, and the tree carets render as mojibake.
2. Screenshot headless Chrome (chrome-devtools MCP is unavailable; drive `google-chrome --headless`
   directly) in **both** light and dark.
3. Confirm all three detail views render — cookbook, recipe, ingredient — and that selecting across the tree
   and breadcrumb switches between them.
4. Toggle the lock and confirm the read-only → editing flip is reflected in **all three** places (titlebar
   lockflag, toolbar toggle, status-bar state) and that weights become editable with rarity updating live.
5. Confirm reroll samples a new portrait, the variant table sorts by each column, and the Colorways rail
   switches shape across a dynamic, a static, and a custom layer.
6. This file **is** the token-block reference: the sibling verification steps diff *against* it, so any edit
   here must be intentional and propagated to `landing.html` and `help.html`.

## Deferred / downstream

- **Authoring is affordance-only.** Add (recipe/ingredient/variant), Delete, and Import .igt render and
  enable under the lock, but the actual create/import dialogs are not built — matching `Nfty.Core`'s
  deferred `new` / `add` commands. Editing weights and deleting variants mutate the in-memory mockup model
  only.
- **Cook is inert.** The **Cook set** button reserves the generation entry point; wiring it to
  `Nfty.Core`'s generator and to the resulting **Set browser view** (the deferred view named in the Landing
  spec) is downstream work.
- **Search (`⌘K`)** is a visual affordance, not wired.
- The reserved `.kbadge` kind treatment is held for a later pass, above.

## Out of scope

- Any Avalonia/C# implementation. Mockup only.
- Any `Nfty.Core` change. The procedural pet stands in for real value-map colorization; nothing here touches
  the engine.
- The Set browser view (deferred).
- The Landing and Help views (their own specs).
