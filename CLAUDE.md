# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
dotnet build nfty.sln                       # build everything
dotnet test nfty.sln                        # run all tests
dotnet test tests/Nfty.Core.Tests           # one test project (also: tests/Nfty.Cli.Tests)
dotnet test --filter FullyQualifiedName~DnaTests           # one test class
dotnet test --filter FullyQualifiedName~DnaTests.Same_selection_same_dna   # one test method
dotnet run --project src/Nfty.Cli -- <command> [args]      # run the CLI
```

Targets **.NET 10**; tests are **xUnit**, named `Snake_case_sentences` (`Same_selection_same_dna`), which is what the `--filter` above must match. There is no lint step. ImageSharp is pinned to **3.1.11** on purpose (4.0.0 requires a build-time license key) — do not upgrade it. `sixlabors.lic` is gitignored and account-specific; never commit it.

## Big picture

`nfty` generates NFT-style asset collections by stacking layered PNGs, with a twist: each layer has one of three **kinds** (`LayerKind`). **Dynamic** and **static** layers are grayscale *value-maps* colorized at generation time — recoloring preserves each pixel's value/lightness and injects hue+saturation, so the output space is far larger than the source images; dynamic **rolls** its color per asset from a weighted colorization, while static applies a **single fixed color** deterministically (no RNG). **Custom** layers are full-color RGBA images composited **as-is**, never colorized (`Colorization` must be null).

**The domain is a cooking metaphor — learn these five terms before touching the model:**

| Term | File ext | What it is | Owns |
|------|----------|-----------|------|
| **CookBook** | `.cbk` | An *uncooked Set* — the top-level container | Canvas dimensions, collection metadata, per-Recipe selection weights |
| **Recipe** | `.rcp` | A complete template for one character/image *type* | Ordered layer stack, incompatibility rules |
| **Ingredient** | `.igt` | One layer / trait-category | `kind` (dynamic/static/custom), colorization config, its weighted variant images |
| **Variant** | *(inside `.igt`)* | One image + weight + name | A single PNG; not its own file — hidden behind the Ingredient |
| **Set** | `.set` or folder | The generated (cooked) output | Images + per-item metadata + rarity + seed |

This is **"Model A"**: a CookBook is a container of whole-template Recipes, and generating it produces a *mixed* Set by rolling a Recipe per asset. (An older "Model B" where Recipe = one layer was abandoned — ignore any stray references to it.) All images share one size, fixed by the **CookBook canvas** — the single source of truth for dimensions; every variant is validated against it.

Every archive (`.cbk`/`.rcp`/`.igt`/`.set`) is just a **ZIP with a `manifest.json`** plus nested files; the custom extension is a renamed `.zip`, so any unzip tool can inspect it. Manifests carry a `schemaVersion` for forward compatibility. `.cbk` nests `.rcp`s nests `.igt`s, so the archive layers mirror the domain layers.

### Generation pipeline (`Generation/Generator.cs`)

Per asset: **roll recipe** (by cookbook weight) → **roll each layer's variant** (by ingredient weight) → **apply the recipe's incompatibility rules** (re-roll on violation, bounded retry budget) → **colorize dynamic/static layers** (custom composites as-is) → **composite** → **hash DNA** → **dedup** (re-roll on collision) → **emit**.

- **Determinism:** a single string seed drives a **SplitMix64** RNG (`Rng.cs`), recorded in the Set. Same cookbook + same seed ⇒ byte-identical output.
- **DNA** (`Dna.cs`): SHA-256 over the recipe id + each layer's variant id, plus — for dynamic (rolled) and static (fixed) layers — the `(H,S)` **quantized** per that layer's config (custom layers contribute variant id only). Quantizing folds the explosive color space into uniqueness. Duplicate DNA ⇒ re-roll; exhausting the legal unique space before `N` is an error stating the true maximum.
- **Extend** (`extend` command / `SetWriter.ReadExisting`): re-opens an existing Set, loads its DNAs + numbering, rolls only new non-colliding assets, and **recomputes rarity across the whole collection** (rewriting existing items' `rarity` field) since rarity is collection-wide. Extend is not a second pipeline — it is the same `Generator.Generate` call with its `existingDnas` / `startNumber` parameters supplied. Keep new work behind that seam rather than forking the generator.
- `Generator.Generate` **validates the cookbook itself** and throws on any problem, so callers never need to pre-validate; the `validate` command exists for humans, not as a required pre-step.

### Project layout

- **`Nfty.Core`** — the entire engine, organized by concern so the planned Avalonia GUI can consume it directly:
  - `Model/` — immutable domain records (manifests, variants, rules, colorization).
  - `Formats/` — ZIP + manifest readers/writers (`ArchiveIo` is the shared low-level helper), plus `Validator`.
    - **Manifest ≠ what the engine consumes.** `Loaded.cs` defines `LoadedCookBook`/`LoadedRecipe`/`LoadedIngredient`, each pairing a `Model/` manifest record with its eagerly-decoded `Image<Rgba32>` variants. `Generator` and `Validator` take `Loaded*`, never bare manifests — so reading an archive is also what pulls every PNG into memory.
    - **All manifest JSON goes through `Json.Options`** (camelCase properties, enums as camelCase strings). Serializing a manifest with default options silently breaks round-trips; reuse the shared options for any new manifest field.
  - `Imaging/` — color conversion, color-spec parsing, value-map colorization, compositing (ImageSharp).
  - `Generation/` — RNG, weighted roller, color roller, DNA, rules engine, orchestrator.
  - `Output/` — dual per-item metadata (standards-pure OpenSea `metadata/NNNN.json` + rich `nfty/NNNN.json` with dna/seed/rarity/per-layer color) + set writer + extend loader.
  - `Stats/` — rarity computation.
- **`Nfty.Cli`** — thin `System.CommandLine` wiring. All command definitions live in `CommandFactory.cs`; `Program.cs` just invokes it. Commands: `inspect`, `validate`, `stats`, `preview`, `generate`, `extend`. `Nfty.Cli.Tests` only asserts the command surface parses (subcommands exist, unknown ones error) — behavior is covered in `Nfty.Core.Tests`, so put real assertions there.

**Callers own image disposal.** `Nfty.Core` returns live `Image<Rgba32>` objects; the CLI disposes them after use (see `generate`/`extend` in `CommandFactory.cs`). Follow this when adding new consumers.

### Color specs

Anywhere a user enters a color it must carry an explicit prefix — `hex:`, `rgb:`, `hsl:`, or `hsv:` (e.g. `hex:d6249f`, `hsv:322,83,84`). A missing/unknown prefix is a **validation error, never guessed** (`Imaging/ColorSpec.cs`). For dynamic/static colorization only `H`/`S` are taken from the color; value/lightness comes from the grayscale value-map.

## Conventions

- **PR-per-task, one fresh agent per major task.** Work lands on a local feature branch merged into `main` — see `docs/superpowers/` for the design spec and implementation plans.
- The **Avalonia GUI** is the next sub-project (cross-platform incl. mobile), built on `Nfty.Core`; design mockups live in `docs/design/mockups/` — `explorer.html` (the primary screen) and `landing.html` (the pre-open default view), each with a spec in `docs/superpowers/specs/`. Their style is **locked**: the token block is shared verbatim, so a new hex literal in either file means the design has drifted. Keep `Nfty.Core` free of UI/CLI dependencies so both front-ends can share it.
- When changing behavior, mirror the existing test style: seeded **distribution** tests for rollers, **exact-pixel** tests for colorization, **round-trip** tests for archives, and **golden-image** tests for compositing.
