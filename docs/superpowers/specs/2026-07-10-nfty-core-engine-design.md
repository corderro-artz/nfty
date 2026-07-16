# nfty — Core Engine & CLI Design

**Date:** 2026-07-10
**Status:** Approved (design phase) — **Model A revision**
**Scope of this spec:** Headless core library + CLI. The Avalonia GUI is a separate later sub-project built on `Nfty.Core`.

> **Revision note (§4, manifest shapes):** §4.1/§4.3/§4.4 were corrected to match the **shipped v1 format**, which is the truth: archives exist and `schemaVersion: 1` is enforced, so the code did not move to the doc. Corrected: `hueQuantize`/`satQuantize` (was `quantize:{hue,sat}`), `range:{hueMin,hueMax,satMin,satMax}` (was `hueRange:[..]`/`satRange:[..]`), `canvas:{width,height}` (was `{w,h}`), and rule `targets:[...]` for both `exclude` and `require` (was `forbid`/`force`). Each shape was confirmed against a real `manifest.json` dumped from a generated archive.
>
> **Revision note:** This design was updated from "Model B" (Recipe = one layer) to **Model A** (Recipe = a whole template). A CookBook is now an *uncooked Set*: a container of Recipes, where each Recipe is a complete character/type template. Generating a CookBook produces a mixed Set, rolling a Recipe per asset by weight.

---

## 1. Overview

`nfty` generates NFT-style asset collections from layered PNGs. Beyond typical layer-stacking, it supports three kinds of layers:

- **Dynamic layers** — grayscale *value-maps* recolored at generation time by a weighted, partly-randomized color roll. Each pixel's value/lightness is preserved exactly while hue and saturation come from the rolled base color, so the effective combination space explodes beyond the finite source images.
- **Static layers** — grayscale *value-maps* colorized with **exactly one fixed color, deterministically** (no RNG roll, no per-asset variation). Value/lightness is preserved from the value-map; hue and saturation come from that single fixed color.
- **Custom layers** — WYSIWYG full-color RGBA PNGs, composited **as-is** and never colorized.

All layers share identical pixel dimensions (a hard requirement for compositing), fixed by the CookBook canvas.

The domain uses a cooking metaphor. Cooking a **CookBook** (an *uncooked Set*) produces a **Set**. A CookBook contains **Recipes**; each Recipe is a full template for one image/character *type*. A Recipe is an ordered stack of **Ingredients** (layers); each Ingredient holds weighted **variant** images.

### Goals
- Author and organize collections as inspectable, versioned archive files.
- Generate `N` assets — or extend an existing Set to a larger `N` — using weighted/rarity rolls at two levels: which Recipe (type), then which variant per layer.
- Support static and dynamic layers, with HSV- or HSL-based colorization.
- Produce marketplace-compatible (ERC-721/OpenSea) metadata plus tool-specific detail.
- Guarantee unique combinations, deterministic reproduction by seed, and rule-based incompatibilities within a Recipe.

### Non-goals (this spec)
- The GUI (tree view, live preview, rarity dashboard, vaporsoft theming) — later sub-project.
- On-chain minting, IPFS upload, or marketplace integration.
- Animated / multi-frame assets.

---

## 2. Stack & tooling

- **Language / runtime:** C# on **.NET 10 (LTS)**.
- **Imaging:** **SixLabors.ImageSharp 3.1.11** — fully managed (no native dependencies), so `Nfty.Core` runs unchanged on desktop, mobile, and WASM. (Pinned to 3.1.11: version 4.0.0 enforces a build-time license key; 3.1.11 builds royalty-free under the same Six Labors Split License with an identical API for our usage.)
- **CLI:** `System.CommandLine` 2.0.9.
- **Archives:** `System.IO.Compression` (ZIP).
- **Tests:** xUnit.

---

## 3. Domain model (Model A)

| Item | Extension | Is a… | Owns |
|------|-----------|-------|------|
| **CookBook** | `.cbk` | An *uncooked Set* — container of Recipes | Canvas dimensions, collection metadata, per-Recipe selection weights |
| **Recipe** | `.rcp` | A complete template for one image/character *type* | Ordered layer stack (Ingredient ids), incompatibility rules |
| **Ingredient** | `.igt` | One layer / trait-category | `kind` (dynamic/static/custom), colorization (dynamic & static only), its weighted variant images |
| **Variant** | — (inside `.igt`) | One image + weight + name | A single PNG (grayscale value-map for dynamic & static; full RGBA for custom). Hidden behind the Ingredient; not its own file type. |
| **Set** | `.set` (packed) or folder | A generated (cooked) output bundle | Finished images + per-item metadata + aggregate rarity + seed |

**Key placement decisions (moved from Model B):**
- **Variant weights (measurements)** live on the **Ingredient**, as the collection of variant weights.
- **`kind` (dynamic / static / custom)** and **colorization config** live on the **Ingredient (layer)** — they apply to whichever variant is rolled for that layer. Dynamic rolls its color per asset; static uses one fixed color deterministically; custom is composited as-is with no colorization.
- **Incompatibility rules** live on the **Recipe**, spanning that recipe's layers.
- **Recipe selection weights** live on the **CookBook** — used to roll which type each asset is.
- **Canvas** stays on the **CookBook** — every variant of every ingredient of every recipe must match it, so all output images share one size.

---

## 4. File formats

Each domain file is a ZIP with a fixed internal layout; the custom extension is a renamed `.zip`, so any unzip tool can inspect it. Every `manifest.json` carries a `schemaVersion` field (starting at `1`) for forward compatibility.

### 4.1 Ingredient — `.igt` (a layer)
```
manifest.json
  {
    schemaVersion, id, name,
    kind: "dynamic" | "static" | "custom",
    colorization: {                     # present for "dynamic" and "static"; MUST be null for "custom"
      model: "hsv" | "hsl",
      hueQuantize: <int deg>,           # DNA precision for this layer
      satQuantize: <int pct>,
      entries: [                        # dynamic: ≥1 entries, non-zero total weight (fixed and/or range)
        { weight, range: null, fixed: "hex:d6249f" },   # any color spec (§4.5)
        { weight, range: { hueMin, hueMax, satMin, satMax }, fixed: null }  # degrees / percent
      ]                                 # static: EXACTLY ONE entry, and it must be `fixed` (no ranges)
    },
    variants: [ { id, name, weight }, ... ]           # the measurements are these weights
  }
```
Each entry carries **both** `range` and `fixed` keys, exactly one of them non-null. A `range` runs
ascending (`hueMin ≤ hueMax`, `satMin ≤ satMax`) and stays on its axis (hue `0..360`, sat `0..100`);
ranges do **not** wrap around, and an inverted or out-of-axis range is a validation error.
```
variants/
  <variantId>.png    # dynamic & static: grayscale value-map (alpha preserved) · custom: full RGBA
  ...
```

### 4.2 Recipe — `.rcp` (a full template / one type)
```
manifest.json
  {
    schemaVersion, id, name,
    layerOrder: [ "<ingredientId>", ... ],   # bottom → top composite order
    rules: [ <IncompatibilityRule>, ... ]     # cross-layer within this recipe
  }
ingredients/
  <ingredientId>.igt
  ...
```

### 4.3 CookBook — `.cbk` (uncooked Set / container)
```
manifest.json
  {
    schemaVersion, id, name,
    canvas: { width, height },
    collection: { name, description, symbol },
    recipeWeights: { "<recipeId>": <weight:number>, ... }   # selection weight per type
  }
recipes/
  <recipeId>.rcp
  ...
```

### 4.4 Incompatibility rule shape
Declarative, references ingredients (layers) and variants by id, scoped to a single Recipe. Both
rule types use the same `when` / `targets` shape — `targets` is always a list:
```
{ type: "exclude", when: { ingredientId, variantId }, targets: [ { ingredientId, variantId }, ... ] }
{ type: "require", when: { ingredientId, variantId }, targets: [ { ingredientId, variantId }, ... ] }
```
When `when` is selected, `exclude` rejects the roll if **any** target is also selected, and `require`
rejects it unless **every** target is selected. A rule whose `when` is not selected never fires.
Rules are checked at generation; an illegal roll is rejected and re-rolled.

### 4.5 Color spec syntax
Everywhere a color is entered by the user (`preview --color`, `fixed` entries in a colorization config), it uses a single **prefixed** form:

| Prefix | Form | Example |
|--------|------|---------|
| `hex:` | `rrggbb` (or `rrggbbaa`) | `hex:d6249f` |
| `rgb:` | `r,g,b` (0–255) | `rgb:214,36,159` |
| `hsl:` | `h,s,l` (deg, %, %) | `hsl:322,72,49` |
| `hsv:` | `h,s,v` (deg, %, %) | `hsv:322,83,84` |

A missing or unknown prefix is a validation error (never guessed). For dynamic colorization only `H`/`S` are used from a `fixed` color; value/lightness comes from the grayscale value-map.

---

## 5. Generation engine

Cooking a CookBook to `N` assets. Per asset:

```
roll recipe (by cookbook weight) → roll each layer's variant → apply recipe rules
  → colorize dynamic layers → composite → hash DNA → dedup → emit
```

### 5.1 Two-level weighted roll
- **Recipe roll:** pick one Recipe via seeded weighted RNG over the cookbook's `recipeWeights`. (A single-recipe generation mode skips this and fixes the recipe.)
- **Variant roll:** within the chosen recipe, for each Ingredient-layer, pick one variant via seeded weighted RNG over that ingredient's variant weights.

Zero total weight at either level is a validation error.

### 5.2 Incompatibility rules (per recipe)
After a full variant roll, evaluate the recipe's rules over the selected `{ingredientId → variantId}` map. On violation, re-roll; if a layer has no legal variant, emit a clear error. A bounded retry budget guards against unsatisfiable rule sets.

### 5.3 Colorization (dynamic & static)
Both dynamic and static layers colorize a grayscale value-map; they differ only in where `(H, S)` comes from.

**Dynamic** — per asset:
1. **Roll base color:** pick one weighted `entry`; sample `H ∈ hueRange`, `S ∈ satRange` uniformly (a `fixed` entry is a degenerate range).
2. **Recolor per pixel:** grayscale `g = R/255` becomes **V** (`hsv`) or **L** (`hsl`); the rolled `(H, S)` supply hue and saturation.
3. **Preserve alpha** from the value-map.

**Static** — identical recolor step, but `(H, S)` comes from the layer's **single fixed color**, resolved **deterministically and without consuming any RNG**. This keeps seeds reproducible regardless of how many static layers a recipe has, and produces zero per-asset variation for the layer.

**Custom** — no colorization at all; the full-color image is composited as-is.

Documented edges: `g = 0` → black for any hue; in HSL, `g = 1` → white; in HSV, `V = 1` stays colored. Intended, matching standard color-space behavior.

### 5.4 DNA & deduplication
DNA = SHA-256 over: the **recipe id**, then (sorted by ingredient id) each layer's selected **variant id**, plus the resolved `(H, S)` **quantized** per that layer's `colorization.hueQuantize` / `satQuantize` for **dynamic and static** layers (custom layers contribute variant id only). Quantizing color into the DNA means the explosive dynamic color space still contributes to uniqueness; a static layer's fixed color is constant, so it does not add cross-asset uniqueness but is recorded for correctness. Duplicate DNA ⇒ re-roll. If the legal unique space is exhausted before `N`, emit an error stating how many unique assets were possible.

### 5.5 Determinism
A single string seed drives a SplitMix64 RNG, recorded in the Set manifest. Same cookbook + seed ⇒ identical output.

### 5.6 Extend
`extend` re-opens an existing Set with its cookbook, loads recorded DNAs and item numbering, then rolls only new, non-colliding assets up to the new `N`, preserving existing items' images, traits, DNA, and numbering exactly. Because rarity is derived from the whole collection, `extend` **recomputes** every item's `rarity` and the `set.json` count/distribution/aggregate rarity over the full collection (existing on-disk items + new additions), rewriting existing items' `rarity` field only.

---

## 6. Set (bundle) output

Default output is a **folder**; `--pack` zips it into a `.set`.

```
myset/
├─ set.json            # collection name, N, seed, cookbook sha256, generator version,
│                      # recipe distribution, aggregate rarity table
├─ images/0001.png ...
├─ metadata/0001.json  # (a) standards-pure ERC-721 / OpenSea
└─ nfty/0001.json      # (b) rich nfty extras (same stem number as its OpenSea sibling)
```

**Two files per item — decision.** The item metadata is split into two files rather than one mixed file or a namespaced sub-object, so the OpenSea file is unambiguously standards-pure (nothing non-standard for a drag-and-drop web3 storefront to trip over). The rich nfty file is keyed by the same `NNNN` stem, so tools pair them by number. `metadata/` is what you point a marketplace at; `nfty/` is the tool's own detail. (`extend` reads back `nfty/NNNN.json` for DNA + numbering, and the OpenSea `attributes` for full-collection rarity.)

### 6.1a Per-item OpenSea metadata (`metadata/NNNN.json`)
Standard ERC-721 / OpenSea only — `name`, `description`, `image`, `attributes`. The recipe (type) is exposed as a `Type` attribute; each layer's rolled variant name is an attribute:
```json
{
  "name": "VaporPets #1",
  "description": "...",
  "image": "images/0001.png",
  "attributes": [
    { "trait_type": "Type", "value": "Cat" },
    { "trait_type": "Background", "value": "Sunset" }
  ]
}
```

### 6.1b Per-item nfty metadata (`nfty/NNNN.json`)
The tool-specific extras, including a per-layer resolved color for **every** kind (dynamic → rolled `(H,S)`; static → its fixed `(H,S)`; custom → nulls, composited as-is), each tagged with its `kind`:
```json
{
  "setNumber": 1,
  "recipe": "cat",
  "dna": "<sha256 hex>",
  "seed": "<run seed>",
  "rarity": [ { "trait_type": "Background", "value": "Sunset", "rarityPct": 12.4 }, ... ],
  "layers": [
    { "layer": "aura", "kind": "dynamic", "model": "hsv", "h": 187, "s": 0.72 },
    { "layer": "skin", "kind": "static",  "model": "hsv", "h": 30,  "s": 0.5 },
    { "layer": "base", "kind": "custom",  "model": null,  "h": null, "s": null }
  ]
}
```

### 6.2 `set.json`
Collection-level: `name`, `count`, `seed`, `cookbookSha256`, `generatorVersion`, a per-recipe distribution table (counts + percent), and an aggregate trait rarity table.

---

## 7. CLI surface (`System.CommandLine`)

| Command | Purpose |
|---------|---------|
| `nfty inspect <file>` | Print the tree of a `.cbk`/`.rcp`/`.igt` (cookbook → recipes → ingredients → variants) |
| `nfty validate <cbk>` | Schema + dimension-consistency + weight/rule checks |
| `nfty stats <cbk>` | Rarity breakdown: per-recipe odds and overall per-trait odds |
| `nfty preview <igt> --variant <id> --color <spec> [--model hsv] [--out png]` | Render one variant recolored |
| `nfty generate <cbk> --count N --seed S --out <dir> [--pack] [--recipe <id>]` | Cook a Set (optionally restricted to one recipe/type) |
| `nfty extend <cbk> <set-dir> --to N [--seed S]` | Grow an existing Set to a new count |

Authoring commands (`new`, `add`) that scaffold/append archives are a deferred thin follow-up (formats + validation already support them).

---

## 8. Architecture

`.NET 10` solution:

- **`Nfty.Core`** (class library) — the entire engine. Internal boundaries:
  - `Model/` — immutable domain records (CookBook, Recipe, Ingredient, Variant, rules, colorization).
  - `Formats/` — ZIP + manifest readers/writers, schema-versioned, round-trippable; validator.
  - `Imaging/` — color conversion, color-spec parsing, value-map colorization, compositing (ImageSharp).
  - `Generation/` — deterministic RNG, weighted roller, color roller, DNA, rules engine, generation orchestrator.
  - `Output/` — ERC-721 metadata + set writer + extend loader.
  - `Stats/` — rarity computation.
- **`Nfty.Cli`** (console) — thin `System.CommandLine` wiring over `Nfty.Core`.
- **`Nfty.Core.Tests`**, **`Nfty.Cli.Tests`** (xUnit).

Each unit has one purpose and a well-defined interface so the future GUI can consume `Nfty.Core` directly and each piece is testable in isolation.

---

## 9. Validation & error handling

- **Canvas is the single source of truth for size.** Every variant image (all ingredients, all recipes) is validated against the cookbook `canvas`; mismatches are rejected with a specific message.
- **Zero total weight** — at the recipe-selection level, or within any ingredient's variants → validation error.
- **Empty ingredient** (no variants) → validation error.
- **Unsatisfiable rules** / no legal variant for a layer → clear conflict error.
- **Exhausted unique space** before `N` → error stating the true maximum.
- **Color specs** must carry a known prefix (§4.5); missing/unknown prefix is rejected, never guessed.
- **Manifest schema version** unsupported → explicit error, never a silent misparse.

---

## 10. Testing strategy

- Seeded **distribution** tests for the weighted roller at both levels (observed frequencies match weights).
- **Exact-pixel** tests for HSV/HSL colorization, including edge grays (0, 1).
- **DNA stability** (same selection ⇒ same hash, recipe-aware) and **dedup** (no duplicate DNA).
- **Rules satisfaction** (no output violates a recipe's rules; unsatisfiable sets error).
- **Format round-trip** (write → read → equal) for all three archive types, including multi-variant ingredients and multi-recipe cookbooks.
- **Dimension/weight validation** rejection paths.
- **Two-level generation**: recipe mix proportions, per-layer variant rolls, single-recipe mode.
- **Extend** preserves existing items, numbering, and uniqueness.
- **Golden-image** tests for compositing and colorization.

---

## 11. Delivery workflow

- **One fresh agent per major task**, **one traceable PR per push** — clean, reviewable history.
- Repository is git-initialized. **No remote yet** — one is created later. Until then, each major task lands on its own **local feature branch** merged into `master` with `--no-ff`; when the GitHub remote is added, those branches are pushed as PRs with no rework.
- Major-task decomposition (solution scaffold → model → imaging → formats → generation → set output → stats → CLI) is realized in the implementation plan.

---

## 12. Resolved decisions
- **Domain model: Model A** — CookBook = uncooked Set (container of Recipes); Recipe = whole template (one type); Ingredient = a layer holding weighted variants; Variant = one image+weight inside an Ingredient; Set = cooked output.
- **CookBook generation: weighted mix** — roll a Recipe per asset by cookbook weight; a single-recipe mode is also supported.
- Packed-bundle extension: **`.set`**.
- Target framework: **.NET 10**; imaging **ImageSharp 3.1.11**; CLI **System.CommandLine 2.0.9**.
- Container: **ZIP + manifest.json**, schema-versioned.
- Color roll: **weighted entries, each fixed or range**; color input uses **prefixed spec syntax**.
- Layer kinds: **dynamic / static / custom** — dynamic rolls color per asset; static uses one fixed color deterministically (no RNG); custom is full-color, composited as-is.
- Metadata: **two files per item** — a standards-pure ERC-721/OpenSea `metadata/NNNN.json` (recipe exposed as a `Type` attribute) plus a rich `nfty/NNNN.json` (dna, seed, rarity, per-layer resolved color + kind).
- Generation guarantees: **DNA dedup (recipe-aware), extend, deterministic seed, per-recipe incompatibility rules**.
- Dimensions: **canvas only** (single source of truth); no per-layer/variant dimensions.
