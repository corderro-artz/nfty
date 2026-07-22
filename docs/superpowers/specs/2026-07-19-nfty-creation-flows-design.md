# Creation flows — design spec

**Date:** 2026-07-19
**Status:** Approved (design), pending implementation
**Deliverables (mockups, currently in `.superpowers/brainstorm/54491-1784433668/content/`, to be moved to `docs/design/mockups/` on finalization):**
- `wizard-single-pane.html` — New CookBook (→ `wizard-cookbook.html` on move)
- `wizard-recipe.html` — New Recipe
- `wizard-ingredient.html` — New Ingredient
- `landing-entrypoints.html` — the expanded Landing, since **folded into `landing.html`** (it supersedes and replaces the old zero-state landing)

**Companion to:** `docs/design/mockups/{explorer,landing,help,ingredient-editor}.html` — this set copies their
token block and chrome verbatim.
**Extends:** `2026-07-16-nfty-landing-view-design.md` (which listed "the New CookBook creation flow itself" as
out of scope — this spec is that follow-up).

## Purpose

The authoring entry points: how a user creates a new **CookBook**, **Recipe**, or **Ingredient** from nothing,
and how loose files enter the app. The Explorer edits what already exists; these flows produce the thing the
Explorer then browses.

Each is a **wizard** — a focused single screen with a small set of fields, opened from the Landing (or, later,
from within an open workspace). They are mockups, not Avalonia code; they exist to settle field set, component
choice, and layout before any GUI code is written.

A hard rule shaped this whole set: **every field is grounded in `Nfty.Core`.** A wizard may only collect what
a manifest actually stores or the `Validator` actually requires. This is what determines the field lists below,
and it removed several fields an ungrounded design would have invented.

## Settled decisions

Each was chosen by the user from rendered visual options during brainstorming, and verified by reading the
model/validator source.

### 1. Single pane, no rail — the deliberate exception

Every other screen (Explorer, Ingredient Editor) uses the shared left sidebar. The wizards **do not**: each is
one centered pane on a radial-accent-wash body, no rail. The user locked this after comparing a two-step railed
variant against the single pane — the field counts are small enough (2–5 fields) that a rail is dead structure.

This is an **intentional inconsistency**, stated so it is not "fixed" later: the wizards are the one screen
family that departs from the sidebar layout.

### 2. Field sets are exactly what the manifest stores

Grounded against `Model/*.cs` and `Formats/Validator.cs`:

**New CookBook** (`CookBookManifest` → `Collection`):
- **Name** (→ derived lowercase identifier, shown under a dashed rule)
- **Symbol** — `Collection.Symbol`; see #3
- **Canvas** — `Dimensions`; W×H with an aspect-lock chain-link between them (see #5)
- **Description** — `Collection.Description`

Two fields an earlier mockup carried were **cut as phantoms**, having no home in the model:
- **Colorize model** (HSV/HSL) — lives on the *Ingredient's* `Colorization`, not the CookBook. (This also
  means the phantom `colorize HSV` chip at `2026-07-15-nfty-explorer-view-design.md:59` should be corrected.)
- **Target supply** — no field on `CookBookManifest`; the asset count is chosen at *generate* time, not stored.

Cutting these two collapsed the CookBook wizard from 7 fields to 5, which is what made the single pane viable.

**New Recipe** (`RecipeManifest`):
- **Name**
- **Selection weight** — mandatory. `Validator.cs:35`: a Recipe with no entry in the CookBook's `RecipeWeights`
  fails validation. Rendered with a live "Resulting mix" stacked bar showing this Recipe's share against its
  siblings. (Rules and the layer stack are authored later in the Explorer, not at birth — the Recipe "owns" only
  name + weight initially.)

**New Ingredient** (`IngredientManifest`):
- **Name**
- **Kind** — `LayerKind { Dynamic, Static, Custom }`, a 3-way radio-card group (see #4)
- a **kind-dependent zone** that changes with the selection (see #6)

Recipe and Ingredient manifests **do not store dimensions** — a Variant is validated against the *CookBook*
canvas (`Validator.cs:182`), the single source of truth. So the wizards show a Canvas field **only** when the
item is being saved loose to the Kitchen with no parent CookBook to inherit from (see #7).

### 3. Symbol — a real constraint, not an invented one

An earlier mockup invented a 5-character `maxlength`. Removed. The real bound: a web3 symbol is **1 byte of data
up to 255 characters**, and **empty is allowed**. The `maxlength` is 255. The familiar 3–5-character ticker
convention (VPET, PUNK) is **advice, not enforcement** — it lives in a hover hint circle, not a hard limit.

### 4. Radio groups for mutually-exclusive choices

Where the model offers a closed either/or set, the component is a **radiogroup**, never a text field or a
loosely-fitting control:
- **Kind** — `LayerKind`'s three values → three radio cards.
- **ColorModel** (`Hsv` / `Hsl`, when it surfaces in the editor) → two radios, not a text input reading "HSV".
- **Save to** (see #7) → radiogroup.

General principle the user set: *for any UI component, use the feature that best fits the context and ground the
claim in the model* — don't settle for a control that merely works.

### 5. Aspect-lock chain-link

Between the Canvas W and H fields sits a chain-link glyph toggle. On (default), editing one dimension scales the
other to preserve ratio; off, they move independently. It replaces an earlier standalone "lock aspect" checkbox,
which read ambiguously.

### 6. Kind-dependent zone (Ingredient)

The zone below the Kind radios reacts to the selection, matching what `Validator.CheckKind` demands of each:
- **Dynamic** — a **Colour range**: dual-handle sliders for Hue and Saturation, one per axis of
  `ColorRange(HueMin, HueMax, SatMin, SatMax)`. `CheckKind`: Dynamic needs ≥1 weighted entry, total weight > 0.
- **Static** — a **single fixed colour** swatch + colour-spec field. `CheckKind`: exactly one `Fixed` entry.
- **Custom** — no colour controls at all; full-colour composited as-is. `CheckKind`: `Colorization` must be null.

So the wizard cannot leave colorization entirely to the editor — the kind's minimum is collected here.

**Colour-range sliders (Dynamic):**
- **Half-open ranges.** The maximum is never sampled unless both handles meet — surfaced in a hint because
  `ColorRoller.Roll` samples `Min + r*(Max-Min)`, `r ∈ [0,1)`, making the range `[Min, Max)`. A user dragging a
  handle expecting to hit exactly 360° should know it won't. This is the same rule the unique-space bucket count
  depends on, so it is not cosmetic.
- The **unselected** span is dimmed by a scrim overlay, rather than drawing a selection ring over the gradient
  (an earlier attempt drew an inset ring that washed out the very spectrum the track exists to show).
- The **Saturation track** previews the full colour space the limits permit: a spectrum that scrolls smoothly
  (GPU transform, seamless red-to-red loop), veiled left-to-right from grey to full chroma so the axis reads as
  *saturation*. It is **paused until hover** and honours `prefers-reduced-motion: reduce`. The endpoint colour of
  a static swatch would be arbitrary while hue spans wide; the cycling preview avoids picking one stand-in.

### 7. Destinations — loose files and the Kitchen

Any of the three concepts can be **created loose** or **saved into a parent**:
- **New from Landing** launches the specific wizard directly.
- **Import…** brings in an existing loose file — see #8.
- Each Recipe/Ingredient wizard has a **Save to** radiogroup: *into an existing higher-hierarchy item*
  (a Recipe into a CookBook; the weight field is live) **or** *loose in the Kitchen* (a predetermined workspace
  folder, created if absent; the weight field goes inert since nothing yet stores it, and the output path shows a
  standalone file).

**The loose-items folder is the Kitchen.** Rather than two competing containers, the "project folder for
temp/loose items" and the Kitchen workspace are the same thing — settled by the user to avoid a second concept.
The Kitchen itself (a top-level workspace with a 3-letter extension holding CookBooks and loose items, like a
VS Code workspace) is the **next task**; these flows reserve its shape.

### 8. Import is kind-agnostic

One **Import…** action covers `.cbk` / `.rcp` / `.igt`. `Formats/Archives.cs` `KindOf(path)` already resolves the
kind from the extension and rejects unknown ones, so a single action suffices — never a per-type "Import .igt".

**Canvas mismatch on import is rejected, not resampled.** If an imported item's canvas differs from the target,
the mismatch is stated and the import refused. Resampling a grayscale value-map would interpolate its values and
soften art the DNA was built on. (An upsert on id collision must also *not* silently replace — the current
`CookBookEdits.UpsertIngredient` replaces, which is wrong for an import path and is flagged for the implementation.)

### 9. Expanded Landing — Create / Open groups

The Landing (`landing.html`, prototyped as `landing-entrypoints.html`) reworks its action area into two labelled groups:
- **Create** — New CookBook (accent, `⌘N`), New Kitchen (dashed — reserved, next task), and a compact
  Recipe + Ingredient subrow.
- **Open** — Open CookBook… (`⌘O`), **Import…** (`⌘I`), and the dashed "Open a cooked .set…" (Set browser
  deferred, as in the Landing spec).

Recents mixes loose Kitchen items (a tagged `aura.igt`) with CookBooks.

## Chrome — unified across all mockups

The creation flows drove a chrome-consistency pass over all eight mockups. Settled:

- **All icon-role glyphs are SVG** from one shared icon set (window controls, zoom, help, plus/open/import/check,
  steppers, kind icons, and the rule marks). Typographic characters stay text: breadcrumb `›`/`·` separators,
  tree carets `▾`/`▸`, the `●` state/validity dot, and all `<kbd>` contents.
- **Rule marks are one atomic pair** — the `✕` exclude / `→` require / `⚑` flag SVGs in `help.html`'s legend and
  `explorer.html`'s live `mk()` rule rows must be byte-identical, because the legend documents the mark the app
  actually draws.
- **Breadcrumb** is `.crumbs` (container) / `.cseg` (segment) / `.sep` (separator) everywhere.
- **Statusbar on every screen, wizards included.** A wizard keeps its `.foot` (keyboard hints + Cancel/Create)
  **and** gains a thin statusbar below it (state span → zoom → help, help rightmost). The wizard has no zoom or
  validity state of its own, but it stays consistent with the rest of the app rather than being the one screen
  without the strip.
- **Help affordance** (`?`, `⌘/`) is the rightmost statusbar control on every screen.

### Kitchen workspace root (the persistent chip)

The Kitchen is the **highest point in the hierarchy** and does not change per selection — it changes only when
you close one Kitchen and open another. So it is **not** repeated as a breadcrumb segment. Instead a persistent
**`.kroot` chip** (kitchen icon + workspace name) sits in the titlebar's brand cluster, to the left of the
breadcrumb, with its own divider. The breadcrumb beside it is only the path *within* the workspace
(`CookBook › Recipe › Ingredient`). This is the VS Code "workspace name in title, path beside it" model.

- Present on all screens that show a path: Explorer, Ingredient Editor, and the three wizards.
- **Absent** on the Landing (nothing open, no path, no workspace root to show).
- The three wizards previously carried `Kitchen ›` *inside* the breadcrumb; that inline segment is removed in
  favour of the chip.
- The user rejected a taller two-row titlebar (Kitchen on its own line above the breadcrumb) in favour of the
  single-row chip.

### Vocabulary — lowercase counts

Domain **types** are capitalized as proper nouns in prose: CookBook, Recipe, Ingredient, Variant, Set, Kitchen.
A **count** is lowercase: `3 recipes`, `50 variants`, `4 variants`. This reserves the capital for when the type
is meant as a concept ("each Recipe rolls its layers") versus a tally ("this book has 3 recipes"). Counts inside
paths/extensions stay lowercase as always (`aura.igt`). "unique DNA", never "combinations". "Import…" stays
kind-agnostic.

## Style constraints

The Explorer's look is locked; these mockups **reuse, never redefine**:

- The complete token block, copied verbatim. **Inventing a hex value is the drift signal.** Baseline is 19 unique
  hex literals. The three wizards and the expanded Landing hold at **19**.
- `wizard-ingredient.html` is the one sanctioned exception at **27**: the 19 tokens plus **8 content-colour
  literals** — 6 spectrum stops in the hue/sat slider gradients, one grey veil (`#8c8c8c`), one sample swatch
  (`#d6249f`). These are content, not theme colour, and are exempt by this spec.
- The wizards' locked token block has **no `--r-*` radius tokens** (only the committed mockups' `:root` does), so
  the wizards inline `border-radius: 5px` (the value `--r-sm` resolves to) with a comment. This is correct and
  must not be "fixed" by adding tokens to the locked block.
- Structural conventions preserved: no `<!doctype>/<html>/<head>/<body>`; everything inline; theme-aware via
  `prefers-color-scheme` + `data-theme`; the `@media (prefers-reduced-motion: reduce)` and `:focus-visible` rules.

## Verification

1. Wrap with the charset shim (`mockups/README.md`) before viewing locally — else `›`, `⌘`, `·`, `—`, `✕` render
   as mojibake.
2. Screenshot headless Chrome directly (chrome-devtools MCP available per project memory, headless is the
   fallback) in **both** light and dark.
3. Diff each token block against `explorer.html` — must match verbatim. Confirm hex counts: 19 for the three
   wizards and the expanded Landing, 27 for the ingredient wizard.
4. Confirm the exclude SVG is byte-identical between `explorer.html` rule rows and `help.html`'s legend.
5. Exercise the wizard JS toggles: Recipe's "Save to" flips the weight field inert and the path to a loose file;
   Ingredient's kind radios swap the kind-dependent zone; Ingredient's Save-to shows the Canvas field only in the
   loose-Kitchen case.

## Open items (decisions deferred, not accidents)

1. **The Kitchen** — the top-level workspace (3-letter extension, holds CookBooks + loose items). The next main
   task. These flows only reserve its shape (the `.kroot` chip, the loose-to-Kitchen destination).
2. **Create path once a workspace is open.** The Landing has the four Create entry points; past the Landing, the
   only in-app creator is the Explorer's context `Add` button. There is no menubar. Leaning toward extending the
   Explorer's existing `⌘K` search into a command palette that also hosts the global Create/Open/Import actions,
   rather than adding a menubar to a design that has deliberately avoided one. **Undecided.**
3. **Breadcrumb root** now settled by #Chrome above (roots at the CookBook; Kitchen is the chip). No open question
   remains here beyond the Kitchen task itself.
4. **Finalization moves** (mechanical, pending): move the three wizard mockups into `docs/design/mockups/`
   (`wizard-single-pane.html` → `wizard-cookbook.html`); reconcile `landing-entrypoints.html` into `landing.html`
   as the canonical Landing; retrofit the `⌘N`/`⌘O`/`⌘I` shortcuts into `help.html`'s Keys column; correct the
   phantom `colorize HSV` chip in the Explorer spec.

## Out of scope

- Any Avalonia/C# implementation. Mockup only.
- Any `Nfty.Core` change — though two engine follow-ups are **flagged** for the implementation: the import-path
  upsert must not silently replace on id collision, and canvas-mismatch import must reject with the mismatch
  stated.
- The Kitchen workspace itself (next task, above).
- The Set browser view (still deferred).
