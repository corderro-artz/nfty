# nfty — Authoring CLI Design (`new` / `add`)

**Date:** 2026-07-21
**Status:** Approved design, ready for implementation planning.
**Scope:** The deferred authoring commands for `Nfty.Cli` that *create* and *mutate* the
`.igt` / `.rcp` / `.cbk` archives. This is the "thin follow-up" the core-engine spec
(`2026-07-10-nfty-core-engine-design.md` §7) parked: the formats, writers, and validation already
exist; this spec only designs the CLI surface over them, plus one small `Validator` refactor.

This spec does **not** cover GUI authoring. The Avalonia editor authors via the draft-based
`Nfty.Core.Editing` path (drawing value-maps); the CLI cannot draw, so CLI authoring is
**import-based** — it assembles archives from PNG files already on disk plus manifest JSON.

---

## 1. Goals & non-goals

**Goals**

- Expose the archive **write** path (`IngredientArchive.Write` / `RecipeArchive.Write` /
  `CookBookArchive.Write`) through the CLI, so a CookBook can be built and grown without a GUI.
- De-risk the write/round-trip path with a shipped consumer before the GUI depends on it.
- Make cookbook construction scriptable and reproducible (fixtures, CI, batch import).
- Preserve the existing CLI's conventions: thin `System.CommandLine` wiring, errors through
  `ErrorReport`, `using`-scoped ownership, deterministic output.

**Non-goals**

- **Drawing.** CLI authoring never creates pixels; variant images come from PNG files on disk.
- **A new manifest schema.** Authoring input *is* the existing Core manifest JSON.
- Any change to generation, output, or the read-side commands.

---

## 2. Command surface

Two command groups, mirroring the three archive levels:

```
nfty new ingredient <out.igt>  --manifest <ing.json>  --images <dir>
nfty new recipe     <out.rcp>  --manifest <rcp.json>  --ingredients <dir>
nfty new cookbook   <out.cbk>  --manifest <cbk.json>  --recipes <dir>  [--force]

nfty add variant    <igt>  --id <id> [--name <name>] --weight <w> --image <png>
nfty add ingredient <rcp>  --igt <path.igt> [--index <n>]
nfty add recipe     <cbk>  --rcp <path.rcp> --weight <w>  [--force]
```

- **`new`** creates an archive from a manifest (bulk config in JSON) plus the artifacts one level
  down.
- **`add`** mutates an existing archive by appending a single item (small enough for flags).

The asymmetry (`new` takes JSON, `add` takes flags) is deliberate: the nested/structured config —
colorization entries, incompatibility rules, recipe weights — is awkward as flags and belongs in
JSON; a single appended item is not.

`--force` (cookbook-level only) writes despite validation problems; see §4.

---

## 3. Input model & the `{id}` convention

### 3.1 Manifests are the exact Core manifest JSON

`--manifest` points at a JSON file that deserializes, through the existing `Formats.Json.Options`
(camelCase, enums-as-camelCase-strings), directly into the corresponding Core record:

- `new ingredient` → `IngredientManifest` (`id`, `name`, `kind`, `colorization?`, `variants[]`)
- `new recipe` → `RecipeManifest` (`id`, `name`, `layerOrder[]`, `rules[]`)
- `new cookbook` → `CookBookManifest` (`id`, `name`, `canvas`, `collection`, `recipeWeights`)

No authoring-specific schema is introduced. The input JSON is the same shape `inspect` would show
and the same shape stored inside the archive.

`schemaVersion` may be **omitted**; System.Text.Json fills the record's default parameter
(`Schema.Current`). This was verified against .NET 10 (`{"id":"x"}` → `SchemaVersion == 1`). An
explicitly present but unsupported `schemaVersion` is rejected with the same message
`UnsupportedSchemaVersionException` uses, so an authoring input cannot declare a version the build
cannot write.

### 3.2 Children resolve by id, one level down

Each manifest names ids; each id resolves to a file named `{id}.<ext>` in the supplied directory:

| Command | For each… | resolves to |
|---------|-----------|-------------|
| `new ingredient` | `variants[].id` | `<images>/{id}.png` |
| `new recipe` | `layerOrder[]` entry | `<ingredients>/{id}.igt` |
| `new cookbook` | `recipeWeights` key | `<recipes>/{id}.rcp` |

Symmetric at every level; the build pipeline is `PNGs → .igt → .rcp → .cbk`. Each command reuses
the existing Read/Write APIs — e.g. `new recipe` calls `IngredientArchive.Read` on each resolved
`.igt` to get a `LoadedIngredient`, then `RecipeArchive.Write(out, manifest, ingredients)`.

Only *referenced* ids are read. A referenced file that is **missing** is an error; **extra**
unreferenced files in the directory are ignored (not an error).

### 3.3 Ownership

Every `Loaded*` and `Image<Rgba32>` a command opens is disposed with `using`, exactly as the
read-side commands do. A build that fails part-way disposes what it has already loaded (the Core
Read APIs already guarantee this internally; the CLI adds nothing that leaks).

---

## 4. Validation

Generation refuses to run over an invalid CookBook; authoring should refuse to *write* one, for the
same reason. But the canvas — the single source of truth for image size — lives only on the
**CookBook**, so the amount that can be validated differs by level.

### 4.1 Level-appropriate validation

- **`new cookbook` / `add recipe`** produce a complete `LoadedCookBook`, so they run the
  authoritative `Validator.Validate`. If it reports anything, the command **refuses to write** and
  prints every problem (exit 1) — the same contract `generate` has. `--force` overrides this,
  writing anyway and printing the problems as warnings, for deliberate work-in-progress.

- **`new ingredient` / `new recipe`** (and `add variant` / `add ingredient`) have no canvas, so the
  full `Validator` cannot run. They run every check that is **canvas-independent**: kind ↔
  colorization consistency, color-spec parsing, duplicate variant/ingredient ids, grayscale for
  dynamic/static value-maps, and that all referenced PNGs load and share **one uniform size** (so
  they can match some future canvas). They cannot check a variant against the canvas (unknown until
  the CookBook), and say so in any message where it matters. These commands refuse to write on a
  reported problem; `--force` is **not** offered at these levels (there is no canvas ambiguity to
  work around — a malformed ingredient is simply wrong).

### 4.2 Validator refactor (single source of truth)

CLAUDE.md makes `Validator` *the one place* that decides what is legal; the ingredient/recipe-level
checks above must therefore be the **same** code, not a duplicate. Today the only canvas-dependent
logic is inside `CheckVariantImages`, which conflates two things: "every variant image is present,
uniform, and grayscale where required" (canvas-independent) and "every variant image matches the
canvas" (canvas-dependent).

The refactor splits that seam so the canvas-independent checks are callable without a canvas:

- Extract the canvas-independent portion of `CheckVariantImages` into a helper that takes no
  `Dimensions` (presence, uniform size across an ingredient's variants, grayscale-for-non-custom).
- Keep the canvas match as a separate step that `CheckVariantImages` still performs for the
  cookbook path.
- Expose a public entry point for level-appropriate validation, e.g.
  `Validator.ValidateIngredient(LoadedIngredient)` and `Validator.ValidateRecipe(LoadedRecipe)`,
  returning the same `IReadOnlyList<string>` shape as `Validate`, running exactly the subset that
  is meaningful without a canvas. `Validate(LoadedCookBook)` stays the authoritative whole-book
  check and its behavior is unchanged.

The split is behavior-preserving for the existing cookbook path; the audit's `Validator` tests must
stay green, and new tests cover the extracted per-level entry points.

---

## 5. `add` semantics

Read-modify-write over the existing Read/Write APIs, mutating the immutable manifest records with
`with`. Each `add` re-runs the level-appropriate validation from §4 on the *result* before writing,
so a mutation cannot quietly produce a broken archive.

- **`add variant <igt>`** — append `Variant(id, name ?? id, weight)` to the manifest and copy
  `--image` in as that variant's PNG. Error if the id already exists (`add` ≠ replace).
- **`add ingredient <rcp>`** — `IngredientArchive.Read` the `--igt`, append it to the recipe's
  ingredients, and insert its id into `layerOrder` at `--index` (0-based; default = end). Error if
  the id is already a layer.
- **`add recipe <cbk>`** — `RecipeArchive.Read` the `--rcp`, add it, and set
  `recipeWeights[id] = weight`. Error if the recipe id already exists.

The mutation logic lives in the CLI as thin record edits. It does **not** use
`Nfty.Core.Editing` (that namespace is the GUI's draft path) and adds no new Core API. If cross-
front-end mutation helpers are ever wanted, they can be lifted into Core later; YAGNI for now.

---

## 6. Errors & determinism

- All failures flow through the existing `ErrorReport`: a missing `<dir>/{id}.png`, an unparseable
  manifest, a duplicate id, a size mismatch — each a clean message, with the trace behind
  `--verbose`.
- Output is deterministic: `IngredientArchive.Write` emits variants in manifest order; no RNG is
  involved anywhere in authoring. The same inputs produce byte-identical archives.
- Manifest JSON that fails to deserialize (malformed, wrong types) surfaces as a named error rather
  than a raw `JsonException` leaking through.

---

## 7. Code layout

- **`Nfty.Cli/CommandFactory.Authoring.cs`** — a `partial class CommandFactory` holding the
  authoring command builders, exposing `NewGroup()` and `AddGroup()` (each a `System.CommandLine`
  command group), wired into `CommandFactory.Build()`. `CommandFactory` is already ~336 lines;
  six more commands belong in their own partial rather than swelling the file.
- **`Nfty.Cli/ManifestFile.cs`** (small) — `Read<T>(string path)` centralizing manifest
  deserialization through `Json.Options`, with a friendly error on malformed JSON.
- **`Nfty.Core/Formats/Validator.cs`** — the §4.2 refactor: extract the canvas-independent image
  checks and add `ValidateIngredient` / `ValidateRecipe` entry points.
- No other Core files change.

---

## 8. Testing

Mirrors the existing test style — CLI tests build real archives in `Directory.CreateTempSubdirectory()`
and round-trip them; Core tests build fixtures in memory.

- **`new` round-trips:** each `new` builds an archive from a manifest (written in-memory to a temp
  JSON) + PNGs, then `IngredientArchive.Read` / `RecipeArchive.Read` / `CookBookArchive.Read`
  asserts the result. A full **`new ingredient → new recipe → new cookbook → generate`** pipeline
  proves end-to-end parity with a hand-built book.
- **`add`:** each `add` asserts the new item appears, ordering/weight is correct
  (`--index` places the layer where asked), and the archive still reads.
- **Validation:** a bad manifest (non-grayscale dynamic variant, duplicate id, non-uniform image
  sizes) is rejected before writing; `add` of a duplicate id errors; `--force` writes an invalid
  cookbook and the level commands without `--force` do not.
- **`Validator` refactor:** new Core tests for `ValidateIngredient` / `ValidateRecipe` covering the
  canvas-independent checks; the existing `ValidatorTests` stay green unchanged (behavior-preserving
  split).
- **Errors:** a missing `{id}.png`, a missing referenced child archive, and malformed manifest JSON
  each produce a named `ErrorReport` message, asserted via the non-throwing invocation config the
  existing CLI tests already use.

---

## 9. Open items deferred (YAGNI)

- A one-shot whole-cookbook build (single spec → `.cbk`) — bottom-up per-level commands are the
  first cut; a convenience wrapper can come later if the multi-step flow proves annoying.
- Cross-front-end mutation helpers in Core — only if the GUI ever needs the same splice logic.
- Explicit per-variant image paths in the manifest (vs the `{id}.png` convention) — revisit only if
  the filename-equals-id coupling becomes a real friction.
