# nfty — Core Engine & CLI Design

**Date:** 2026-07-10
**Status:** Approved (design phase)
**Scope of this spec:** Headless core library + CLI. The Avalonia GUI is a separate later sub-project built on top of `Nfty.Core`.

---

## 1. Overview

`nfty` is a tool for generating NFT-style asset collections from layered PNGs. It goes beyond typical layer-stacking generators by supporting two kinds of layers:

- **Static layers** — WYSIWYG full-color PNGs, composited as-is.
- **Dynamic layers** — grayscale *value-maps* that are recolored at generation time by a weighted, partly-randomized color roll. The value/lightness of every pixel is preserved exactly while hue and saturation are supplied by the rolled base color. This makes the effective combination space explode far beyond the finite set of source images.

All layers in a collection must share identical pixel dimensions (a hard requirement for correct compositing).

The domain uses a cooking metaphor: a **CookBook** is a complete generative template, a **Recipe** is one layer, an **Ingredient** is one image variant, and generating produces a **Set** (a bundle of finished assets + metadata).

### Goals
- Author and organize collections as inspectable, versioned archive files.
- Generate `N` assets — or extend an existing Set to a larger `N` — using weighted/rarity rolls.
- Support static and dynamic layers, with HSV- or HSL-based colorization.
- Produce marketplace-compatible (ERC-721/OpenSea) metadata plus tool-specific detail.
- Guarantee unique combinations, deterministic reproduction by seed, and rule-based incompatibilities.

### Non-goals (this spec)
- The GUI (tree view, live preview, rarity dashboard, vaporsoft theming) — later sub-project.
- On-chain minting, IPFS upload, or marketplace integration.
- Animated / multi-frame assets.

---

## 2. Stack & tooling

- **Language / runtime:** C# on **.NET 10 (LTS)**.
- **Imaging:** **SixLabors.ImageSharp** — fully managed (no native dependencies), so `Nfty.Core` runs unchanged on desktop, mobile, and WASM when the GUI arrives.
- **CLI:** `System.CommandLine`.
- **Archives:** `System.IO.Compression` (ZIP).
- **Tests:** xUnit.

---

## 3. Domain model (Model B)

| Item | Extension | Is a… | Owns |
|------|-----------|-------|------|
| **CookBook** | `.cbk` | Complete generative template you roll from | Canvas dimensions, ordered layer list, cross-layer incompatibility rules, collection metadata |
| **Recipe** | `.rcp` | One layer (trait-category) | `kind` (static/dynamic), z-order, measurements (weights), colorization config (if dynamic) |
| **Ingredient** | `.igt` | One image variant | Identity + a single PNG (RGBA for static; grayscale value-map for dynamic) |
| **Set** | `.set` (packed) or folder | A generated output bundle | Finished images + per-item metadata + aggregate rarity + seed |

**Key placement decisions:**
- **Measurements (weights)** live on the **Recipe**, as an `ingredientId → weight` table — matching "a recipe contains its ingredients *and* its measurements." Ingredients stay pure images with no embedded weight.
- **`kind` (static vs dynamic)** is a property of the **Recipe (layer)**, not the individual ingredient. Every ingredient in a dynamic layer is a grayscale value-map.
- **Colorization config** (HSV/HSL mode + weighted color entries) lives on the **Recipe**, because it applies to whichever ingredient was rolled for that layer.
- **Incompatibility rules** live on the **CookBook**, because they span multiple layers.

---

## 4. File formats

Each domain file is a ZIP with a fixed internal layout; the custom extension is a renamed `.zip`, so any unzip tool can inspect it. Every `manifest.json` carries a `schemaVersion` field for forward compatibility.

### 4.1 Ingredient — `.igt`
```
manifest.json   { schemaVersion, id, name, sha256 }
image.png       # static layer: full RGBA · dynamic layer: grayscale value-map (alpha preserved)
```
Dimensions are **not** stored here. The CookBook `canvas` is the single source of truth; every source layer is assumed to be authored at the final image size, and images are validated against the canvas when added or generated (§9).

### 4.2 Recipe — `.rcp`
```
manifest.json
  {
    schemaVersion, id, name,
    kind: "static" | "dynamic",
    order: <int z-index>,
    measurements: { "<ingredientId>": <weight:number>, ... },
    colorization: {                     # present only when kind == "dynamic"
      model: "hsv" | "hsl",
      quantize: { hue: <int deg>, sat: <int pct> },   # DNA precision for this layer
      entries: [
        { weight, fixed: "hex:d6249f" },              # any color spec (§4.5)
        { weight, hueRange:[h0,h1], satRange:[s0,s1] }   # degrees / percent
      ]
    }
  }
ingredients/
  <name>.igt
  ...
```

### 4.3 CookBook — `.cbk`
```
manifest.json
  {
    schemaVersion, id, name,
    canvas: { w, h },
    layerOrder: [ "<recipeId>", ... ],      # bottom → top composite order
    rules: [ <IncompatibilityRule>, ... ],
    collection: { name, description, symbol }
  }
recipes/
  <name>.rcp
  ...
```

### 4.4 Incompatibility rule shape
Declarative, references layers (recipes) and ingredients by id:
```
{ type: "exclude", when: {layer, ingredient}, forbid: [ {layer, ingredient}, ... ] }
{ type: "require", when: {layer, ingredient}, force:  {layer, ingredient} }
```
Rules are symmetric-checked at generation; an illegal roll is rejected and re-rolled.

### 4.5 Color spec syntax
Everywhere a color is entered by the user (the `preview --color` flag, `fixed` entries in a colorization config), it uses a single **prefixed** form so the input space is always unambiguous:

| Prefix | Form | Example |
|--------|------|---------|
| `hex:` | `rrggbb` (or `rgba` 8-digit) | `hex:d6249f` |
| `rgb:` | `r,g,b` (0–255) | `rgb:214,36,159` |
| `hsl:` | `h,s,l` (deg, %, %) | `hsl:322,72,49` |
| `hsv:` | `h,s,v` (deg, %, %) | `hsv:322,83,84` |

A missing or unknown prefix is a validation error (never guessed). For dynamic colorization only `H`/`S` are used from a `fixed` color; the value/lightness comes from the grayscale value-map.

---

## 5. Generation engine

Per-asset pipeline:

```
roll layers → apply rules → colorize dynamic layers → composite → hash DNA → dedup → emit
```

### 5.1 Weighted roll
For each layer, pick one ingredient via a **seeded** weighted RNG over the recipe's measurements. Zero total weight is a validation error.

### 5.2 Incompatibility rules
After a full roll, evaluate cookbook rules. On violation, re-roll the offending layer(s). If a layer has no legal ingredient given current selections, emit a clear error naming the conflict. A bounded retry budget guards against unsatisfiable rule sets.

### 5.3 Dynamic colorization
For a dynamic layer's rolled value-map:
1. **Roll base color:** pick one weighted `entry`; sample `H ∈ hueRange`, `S ∈ satRange` uniformly (a `fixed` entry is a degenerate range).
2. **Recolor per pixel:** grayscale `g ∈ [0..1]` (pixel luminance) becomes **V** (`model: hsv`) or **L** (`model: hsl`); the rolled `(H, S)` supply hue and saturation. Convert `(H,S,V)`/`(H,S,L)` → RGB.
3. **Preserve alpha** from the value-map (retains shape/transparency).

Documented edges: `g = 0` → black for any hue; in HSL, `g = 1` → white; in HSV, `V = 1` stays colored. These are intended and match standard color-space behavior.

### 5.4 DNA & deduplication
DNA = SHA-256 over the ordered selection:
- each layer's selected **ingredient id**, and
- for dynamic layers, the rolled `(H, S)` **quantized** per that layer's `colorization.quantize` precision.

Quantizing color into the DNA means the explosive color space still contributes to uniqueness. A duplicate DNA triggers a re-roll. If the legal unique space is exhausted before reaching `N`, emit an error stating how many unique assets were actually possible.

### 5.5 Determinism
A single RNG seed governs the whole run and is recorded in the Set manifest. Same cookbook + same seed ⇒ byte-identical output.

### 5.6 Extend
`extend` re-opens an existing Set, loads its recorded DNAs, seed lineage, and item numbering, then rolls only **new, non-colliding** assets until the new `N` is reached. Existing items and their numbers are preserved exactly.

---

## 6. Set (bundle) output

Default output is a **folder** (convenient for upload); `--pack` zips it into a `.set` archive.

```
myset/
├─ set.json            # collection name, N, seed, cookbook sha256, generator version, aggregate rarity table
├─ images/
│  ├─ 0001.png
│  └─ ...
└─ metadata/
   ├─ 0001.json
   └─ ...
```

### 6.1 Per-item metadata (`metadata/NNNN.json`)
ERC-721/OpenSea standard fields plus tool-specific extras:
```json
{
  "name": "VaporPets #1",
  "description": "...",
  "image": "images/0001.png",
  "attributes": [ { "trait_type": "Background", "value": "Sunset" }, ... ],

  "setNumber": 1,
  "dna": "<sha256 hex>",
  "seed": "<run seed>",
  "rarity": [ { "trait_type": "Background", "value": "Sunset", "rarityPct": 12.4 }, ... ],
  "colorRolls": [ { "layer": "Aura", "model": "hsv", "h": 187, "s": 72 }, ... ]
}
```

### 6.2 `set.json`
Collection-level: `name`, `count`, `seed`, `cookbookSha256`, `generatorVersion`, and an aggregate rarity table (per trait_type/value occurrence counts and percentages).

---

## 7. CLI surface (`System.CommandLine`)

| Command | Purpose |
|---------|---------|
| `nfty new cookbook\|recipe\|ingredient <name>` | Scaffold a new archive with a valid empty manifest |
| `nfty add ingredient <png> --to <recipe.rcp> --weight <w>` | Add an image variant (validates dimensions) |
| `nfty inspect <file>` | Print the manifest / tree of any `.cbk`/`.rcp`/`.igt` |
| `nfty preview <igt\|rcp> --color <spec> [--out png]` | Render a preview with a chosen color (for dynamic layers); `<spec>` uses the prefixed color syntax (§4.5) |
| `nfty stats <cookbook.cbk>` | Rarity breakdown and percentage chances per trait |
| `nfty validate <file>` | Schema + dimension-consistency check |
| `nfty generate <cookbook.cbk> --count N --seed S --out <dir> [--pack]` | Generate a Set |
| `nfty extend <set> --to N` | Grow an existing Set to a new count |

---

## 8. Architecture

`.NET 10` solution:

- **`Nfty.Core`** (class library) — the entire engine, no console concerns. Internal boundaries:
  - `Model/` — immutable domain records (CookBook, Recipe, Ingredient, rules, colorization).
  - `Formats/` — ZIP + manifest readers/writers, schema-versioned, round-trippable.
  - `Imaging/` — value-map handling, HSV/HSL colorization, compositing (ImageSharp).
  - `Generation/` — weighted roller, rules engine, DNA, dedup, extend, orchestrator.
  - `Stats/` — rarity computation.
- **`Nfty.Cli`** (console) — thin `System.CommandLine` wiring over `Nfty.Core`.
- **`Nfty.Core.Tests`**, **`Nfty.Cli.Tests`** (xUnit).

Each unit has one purpose and communicates through well-defined interfaces so the future GUI can consume `Nfty.Core` directly and each piece is testable in isolation.

---

## 9. Validation & error handling

- **Canvas is the single source of truth for size.** Dimensions are not stored per layer/ingredient; every source image is expected to already be the final image size. On `add` and `generate`, each ingredient PNG is validated against the cookbook `canvas`, and mismatches are rejected with a specific message.
- **Color specs** must carry a known prefix (§4.5); a missing/unknown prefix is rejected, never guessed.
- **Zero total weight** in a layer → validation error.
- **Unsatisfiable rules** / no legal ingredient for a layer → clear conflict error.
- **Exhausted unique space** before `N` → error stating the true maximum.
- **Manifest schema version** unsupported → explicit "unsupported version" error, never a silent misparse.

---

## 10. Testing strategy

- Seeded **distribution** tests for the weighted roller (observed frequencies match weights within tolerance).
- **Exact-pixel** tests for HSV and HSL colorization against hand-computed values, including edge grays (0, 1).
- **DNA stability** (same selection ⇒ same hash) and **dedup** (no duplicate DNA in output).
- **Rules satisfaction** (no output violates any rule; unsatisfiable sets error).
- **Format round-trip** (write → read → equal) for all three archive types.
- **Dimension validation** rejection paths.
- **Extend** preserves existing items, numbering, and uniqueness.
- **Golden-image** tests for compositing and colorization output.

---

## 11. Delivery workflow

- **One fresh agent per major task**, **one traceable PR per push** — clean, reviewable history.
- Repository is git-initialized (this spec is the first commit). **No remote exists yet** — one is created later. Until then, each major task lands on its own **local feature branch** merged into `master`, so the history is already PR-shaped; when the GitHub remote is added, those branches are pushed as PRs with no rework.
- Major-task decomposition (solution scaffold → formats → imaging/colorization → generation/rules/dedup → set output/metadata → CLI → stats/validate) is finalized in the implementation plan.

---

## 12. Open items resolved
- Packed-bundle extension: **`.set`**.
- Target framework: **.NET 10 (latest stable/LTS)**.
- Domain model: **Model B** (CookBook = template, Recipe = layer, Ingredient = variant).
- Color roll: **weighted entries, each fixed or range**.
- Container: **ZIP + manifest.json**.
- Metadata: **ERC-721/OpenSea + extras**.
- Generation guarantees: **DNA dedup, extend, deterministic seed, incompatibility rules** (all in scope).
- Dimensions: **canvas only** (single source of truth); no per-layer/ingredient dimensions.
- Color input: **prefixed spec syntax** (`hex:`/`rgb:`/`hsl:`/`hsv:`), prefix required.
- Remote: **none yet**; local feature branches now, pushed as PRs when a remote is created.
