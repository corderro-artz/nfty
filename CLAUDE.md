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

Every archive (`.cbk`/`.rcp`/`.igt`/`.set`) is just a **ZIP with a `manifest.json`** plus nested files; the custom extension is a renamed `.zip`, so any unzip tool can inspect it. `.cbk` nests `.rcp`s nests `.igt`s, so the archive layers mirror the domain layers. Every manifest implements `ISchemaVersioned` and is checked against `Schema.Current` in `ArchiveIo.ReadManifest` — the one place all manifests are read — so an unknown version raises `UnsupportedSchemaVersionException` rather than silently misparsing. Bump `Schema.Current` (`Model/Schema.cs`) when the format changes. Extensions map to types via `Archives.KindOf`, which rejects an unknown extension rather than guessing.

### Generation pipeline (`Generation/Generator.cs`)

Per asset: **roll recipe** (by cookbook weight) → **roll each layer's variant** (by ingredient weight) → **apply the recipe's incompatibility rules** (re-roll on violation, bounded retry budget) → **colorize dynamic/static layers** (custom composites as-is) → **composite** → **hash DNA** → **dedup** (re-roll on collision) → **emit**.

- **Determinism:** a single string seed drives a **SplitMix64** RNG (`Rng.cs`), recorded in the Set. Same cookbook + same seed ⇒ byte-identical output — including across machine locales, which is why every sort that reaches an output file uses `StringComparer.Ordinal` (a default `OrderBy` sorts by current culture and silently breaks this).
- **DNA** (`Dna.cs`): SHA-256 over the recipe id + each layer's variant id, plus — for dynamic (rolled) and static (fixed) layers — the `(H,S)` **quantized** per that layer's config (custom layers contribute variant id only). Quantizing folds the explosive color space into uniqueness. Duplicate DNA ⇒ re-roll. Two distinct failures, never conflated: `RuleConflictException` when a recipe's rules exclude *every* combination (the space is empty), and `UniqueSpaceExhaustedException` when the space is real but smaller than `N`. The latter states the true maximum, counted by `UniqueSpace.Count` (`Generation/UniqueSpace.cs`) — legal variant combinations honouring rules, times each dynamic layer's quantized `(H,S)` buckets; static/custom contribute one bucket each. It enumerates only when the unconstrained product fits under a cap (default 1M) and otherwise saturates, reporting `IsExact = false` (i.e. "more than N" — then the reroll budget ran out, not the space). **The count is a promise:** exactly `Total` unique DNA must be generable, so bucket counting must track `ColorRoller.Roll` exactly — it samples `Min + r*(Max-Min)` with `r ∈ [0,1)`, making a range **half-open** `[Min, Max)`; only a degenerate `Min == Max` reaches its endpoint. Off-by-one here makes `Generate` throw the self-contradicting "allows exactly N, but N were requested".
- **Extend** (`extend` command / `SetWriter.ReadExisting`): re-opens an existing Set, loads its DNAs + numbering, rolls only new non-colliding assets, and **recomputes rarity across the whole collection** (rewriting existing items' `rarity` field) since rarity is collection-wide. Extend is not a second pipeline — it is the same `Generator.Generate` call with its `existingDnas` / `startNumber` parameters supplied. Keep new work behind that seam rather than forking the generator.
- `Generator.Generate` runs `Validator.Validate` itself and throws if it reports **any** problem, so callers need not pre-validate; the `validate` command exists for humans, not as a required pre-step. `Validator` is the single place that decides what a legal cookbook is — it must **report** problems, never throw, since `validate` (and a GUI's "what's wrong?") exists precisely to explain a broken book. Check id uniqueness before any `ToDictionary` that would throw on a duplicate.

### Project layout

- **`Nfty.Core`** — the entire engine, organized by concern so the planned Avalonia GUI can consume it directly:
  - `Model/` — immutable domain records (manifests, variants, rules, colorization).
  - `Formats/` — ZIP + manifest readers/writers (`ArchiveIo` is the shared low-level helper), plus `Validator`.
    - **Manifest ≠ what the engine consumes.** `Loaded.cs` defines `LoadedCookBook`/`LoadedRecipe`/`LoadedIngredient`, each pairing a `Model/` manifest record with its eagerly-decoded `Image<Rgba32>` variants. `Generator` and `Validator` take `Loaded*`, never bare manifests — so reading an archive is also what pulls every PNG into memory.
    - **All manifest JSON goes through `Json.Options`** (camelCase properties, enums as camelCase strings). Serializing a manifest with default options silently breaks round-trips; reuse the shared options for any new manifest field.
  - `Imaging/` — color conversion, color-spec parsing, value-map colorization, compositing (ImageSharp).
  - `Generation/` — RNG, weighted roller, color roller, DNA, rules engine, orchestrator.
  - `Output/` — dual per-item metadata (standards-pure OpenSea `metadata/NNNN.json` + rich `nfty/NNNN.json` with dna/seed/rarity/per-layer color) + set writer + extend loader. `set.json` records `cookbookSha256` — the hash of the source `.cbk`, carried on `LoadedCookBook.SourceSha256` (populated by `CookBookArchive.Read`, **null for an in-memory book that never came from a file**) → `GeneratedSet` → `SetManifest`. It is what ties a Set back to the exact archive that produced it, so keep it threaded through.
  - `Stats/` — rarity computation.
- **`Nfty.Cli`** — thin `System.CommandLine` wiring. All command definitions live in `CommandFactory.cs`; `Program.cs` just invokes it. Commands: `inspect` (any of `.cbk`/`.rcp`/`.igt`), `validate`, `stats`, `preview`, `generate`, `extend`. Authoring commands (`new`, `add`) are a deferred follow-up — the formats and validation already support them.

**Errors surface through `ErrorReport.Format`, not raw traces.** `Program.cs` sets `EnableDefaultExceptionHandler = false` — without it `System.CommandLine` catches first and prints `Unhandled exception:` plus a trace, and `Program`'s own handler never runs. Commands themselves catch nothing: they throw, and `Program` prints the message (plus the trace only under the recursive `--verbose`) and returns 1. Engine exceptions are expected to carry a message worth showing a user verbatim — that is why `ErrorReport` prints `ex.Message` rather than the type name. `Nfty.Cli.Tests` covers `ErrorReport` and that the command surface parses; engine behavior is covered in `Nfty.Core.Tests`, so put real assertions there.

**Callers own image disposal.** `Nfty.Core` returns live `Image<Rgba32>` objects. This covers **both directions**: reading an archive eagerly decodes every variant PNG, so `LoadedCookBook`/`LoadedRecipe`/`LoadedIngredient` are `IDisposable` too and each disposes what it owns down to the variant images — `using var book = CookBookArchive.Read(path)` frees the whole tree, and a GUI that opens and closes cookbooks must do so. On the output side, `GeneratedAsset` and `GeneratedSet` are `IDisposable` (disposing the set disposes every asset image), so `using var set = Generator.Generate(...)` is the default — see `generate`/`extend` in `CommandFactory.cs`. `Generate` materialises the whole collection in memory; a consumer that can't afford that (a GUI grid over 10k assets) should use **`Generator.GenerateStreaming`**, which yields one asset at a time and hands ownership of each to the caller — an abandoned enumeration leaves the last asset undisposed.

### Sync/async pairs

Every I/O entry point has an async twin; `Generate` also takes an optional `IProgress<GenerationProgress>` and `CancellationToken` (`SetWriter.WriteAsync` reports `WriteProgress`). The split is deliberate and worth preserving:

- **Genuinely async** (real awaits on stream/JSON/PNG-codec work): `CookBookArchive`/`RecipeArchive`/`IngredientArchive` `ReadAsync`/`WriteAsync`, `SetWriter.WriteAsync`/`ReadExistingAsync`. Note `ZipArchive`'s own directory parsing has no async API — only the per-entry I/O awaits.
- **CPU-bound, offloaded**: `Generator.GenerateAsync` is a `Task.Run` over the sync core. Rolling/colorizing/compositing has nothing to await; it exists to keep a UI thread free. **Don't add fake `async` to the generation loop** — put new work behind the progress/cancellation parameters instead.

### Color specs

Anywhere a user enters a color it must carry an explicit prefix — `hex:`, `rgb:`, `hsl:`, or `hsv:` (e.g. `hex:d6249f`, `hsv:322,83,84`). A missing/unknown prefix is a **validation error, never guessed** (`Imaging/ColorSpec.cs`). For dynamic/static colorization only `H`/`S` are taken from the color; value/lightness comes from the grayscale value-map.

## Conventions

- **PR-per-task, one fresh agent per major task.** Work lands on a local feature branch merged into `main` — see `docs/superpowers/` for the design spec and implementation plans. **The spec (`docs/superpowers/specs/`) wins over the plan docs (`docs/superpowers/plans/`)**, which are stale — they predate the Custom kind and describe a two-kind `LayerKind`. Where the shipped archives contradict even the spec, the archives win: `schemaVersion: 1` is out in the world, so the doc gets corrected, not the format.
- The **Avalonia GUI** is the next sub-project (cross-platform incl. mobile), built on `Nfty.Core`; design mockups live in `docs/design/mockups/` — `explorer.html` (the primary screen) and `landing.html` (the pre-open default view), each with a spec in `docs/superpowers/specs/`. Their style is **locked**: the token block is shared verbatim, so a new hex literal in either file means the design has drifted. Keep `Nfty.Core` free of UI/CLI dependencies so both front-ends can share it.
- When changing behavior, mirror the existing test style: seeded **distribution** tests for rollers, **exact-pixel** tests for colorization *and* compositing, and **round-trip** tests for archives. There are **no golden-image files** — every image assertion reads a pixel off a tiny synthetic image, so keep new imaging tests self-contained rather than introducing a golden-file harness.
- **Tests build their fixtures in memory** — each test file constructs `Loaded*` graphs directly (see `GeneratorTests.Ing`/`Recipe`/`OneRecipeBook`) from 1×1 or 2×2 solid-fill `Image<Rgba32>`s, with private per-file builders shaped for what that file tests (weights in `RarityCalculatorTests`, kinds in `KindSemanticsTests`). Tests that touch the filesystem use `Directory.CreateTempSubdirectory()`.
- **`tests/fixtures/` is the one exception**, and the only place real archives are read from disk (`FixtureArchiveTests`). `VaporPets.cbk` + its `cat.rcp`/`aura.igt` parts exercise all three layer kinds and an exclude rule in one 8×8 archive. **Their value is that an older build wrote them and they still read** — so don't regenerate them to make a failing fixture test pass; that launders a format change instead of catching it. A deliberate format change means bumping `Schema.Current` and adding a new fixture beside this one. The images are synthetic placeholders, to be joined (not replaced) by real art. See `tests/fixtures/README.md`.
