# `Nfty.Ticket` and the `.mnu` Menu format — design

**Date:** 2026-09-22
**Project:** 1 of 3. See `2026-09-22-runtime-and-model-overview.md`.

## What this is for

A game ships a **Menu** (`.mnu`) — a CookBook flattened for runtime. When a drop happens, something
rolls a **Ticket** against that Menu: a recipe, one variant per layer, a colour per colorized layer,
and the DNA. The Ticket is ~100 bytes. The art is rendered from the Menu, on demand, by whoever is
displaying it.

No collection is cooked ahead of time. Storage per drop is a row, not a PNG. Supply is
`UniqueSpace.Count`, not a folder size.

## The core claim, and why it is already nearly true

The roll is pure arithmetic. Verified across the whole engine: **only `Generator.cs` and
`GeneratedSet.cs` in `Generation/` touch ImageSharp.** `Rng`, `SeedHash`, `WeightedRoller`,
`ColorRoller`, `ColorBuckets`, `Dna`, `RulesEngine`, every record in `Model/`, and
`ColorConvert`/`ColorSpec`/`RgbColor` are all image-free. `Model/` is cleaner still — its only
`using` in eleven files is `System.Linq`, and it carries no `System.Text.Json` attributes, because
`Json.Options` supplies the camelCase policy centrally.

`Generator.RollOne` is already the roll/render seam, and `GeneratorRollRenderSplitTests` already
pins it.

## Decision: the extraction is DEEP

Two shapes were considered.

**Shallow** — move only the leaf maths; `Nfty.Ticket` grows its own per-layer roll walk because
`Generator.RollOne` keeps its. **Rejected.** That is two copies of the order in which layers consume
the RNG, which is the `VariantPreview` failure this codebase already has a rule about: two copies is
how "identical" stops being true. A divergence here would not be a rendering nicety — it would mean
the server and the game disagree about what was dropped.

**Deep — chosen.** `Nfty.Ticket` owns the *whole* roll. `Generator` keeps only the render. There is
exactly one definition of every decision an asset is made of.

The refactor is provable rather than argued: the same seed over the same book must produce the same
DNA sequence and the same pixels, and a frozen fixture captured *before* any file moves is what says
so.

## Projects and targets

```
src/Nfty.Ticket/   NEW.  net10.0.  ZERO PackageReferences.
src/Nfty.Core/     +1 ProjectReference. Generator.cs rewired. Menu WRITER added.
src/Nfty.Cli/      + `menu`, + `serve`. `inspect` learns `.mnu`.
tests/Nfty.Ticket.Tests/  NEW (net10.0).
```

`Nfty.Ticket` is **optional for a consumer, mandatory in the build graph.** The desktop app carries
it because it *is* the roll; someone embedding nfty elsewhere takes `Nfty.Ticket.dll` alone and never
sees Core, ImageSharp or anything else.

### Dependency budget: zero packages

`Nfty.Ticket` references **no NuGet package at all.** Everything it needs — `SHA256`,
`BinaryPrimitives`, `System.Text.Json` — is in the `net10.0` shared framework, which is also what
`Nfty.Core`'s `Formats/Json.cs` already uses. Reading a Menu therefore costs nothing new, and the
manifest is read by the same serializer that wrote it rather than by a second, hand-written parser
that would have to be kept in agreement with it.

### Single-target, and why that is a reversal

An earlier draft multi-targeted `netstandard2.1` so a game engine could load the assembly. That is
dropped. Engine embedding is explicitly not the current goal, and carrying the second target *now*
costs real code for no present consumer: four .NET 5+ APIs would need replacing
(`SHA256.HashData`, `Convert.ToHexString`, `ArgumentNullException.ThrowIfNull`, `init`), plus a
hand-written JSON reader, plus a test proving the replacements are byte-identical to the originals —
roughly 500 lines of code and tests defending a door nobody is walking through.

**The door is not closed, and what it costs is written down** (`BACKLOG.md`). Adding the target later
is one csproj line plus those shims, written with a real consumer in hand to check them against. The
one rule that keeps it cheap: **`Nfty.Ticket` takes no package dependency**, so the only thing a
second target ever has to reconcile is framework API surface.

## What moves — namespaces UNCHANGED

Moving a file to another assembly does not move its namespace. Every file below keeps the namespace
it has, so **`Nfty.Core`, `Nfty.Cli`, `Nfty.App` and all three test projects need zero source
edits** — not one `using` changes.

| File | Namespace kept |
|------|----------------|
| `Generation/Rng.cs` (`IRng`, `SplitMix64Rng`, `SeedHash`) | `Nfty.Core.Generation` |
| `Generation/WeightedRoller.cs` | `Nfty.Core.Generation` |
| `Generation/ColorRoller.cs` (`RolledColor`, `ColorRoller`) | `Nfty.Core.Generation` |
| `Generation/ColorBuckets.cs` | `Nfty.Core.Generation` |
| `Generation/Dna.cs` (`LayerSelection`, `Dna`) | `Nfty.Core.Generation` |
| `Generation/RulesEngine.cs` | `Nfty.Core.Generation` |
| `Imaging/ColorConvert.cs`, `ColorSpec.cs`, `RgbColor.cs` | `Nfty.Core.Imaging` |
| all eleven files in `Model/` | `Nfty.Core.Model` |

**Stays in `Nfty.Core`:** `Generator`, `GeneratedSet`, `GenerationExceptions`, `UniqueSpace`,
`SpaceText`, `SelectionOdds`, `Validator`, all of `Formats/`, the `Imaging/` renderers, `Publish/`,
`Output/`, `Stats/`, `Editing/`, `Demo/`.

`UniqueSpace` and `SelectionOdds` stay because both take `LoadedCookBook`/`PeekedCookBook`, which
live in `Formats/`. Neither is needed to roll.

## A standing rule for this assembly

**No `#if` in logic, ever, in `Nfty.Ticket`.** Should a second target framework arrive, the
replacements it needs are written unconditionally — one `SHA256.Create()` path, one hex formatter,
compiled the same for every target.

A conditional here would mean the assembly an engine embeds and the assembly the desktop app loads
run *different code to compute a DNA*, and the difference would be invisible in review and to any
single-target test. One code path cannot diverge from itself. Recorded now so the shortcut is not
available later.

## Public surface

```csharp
namespace Nfty.Ticket;

// ---- The book, image-free. Built from manifests; also what a .mnu deserializes to. ----
sealed record MenuBook(int SchemaVersion, string? SourceCookBookSha256,
                       Dimensions Canvas, string CollectionName, string CollectionSymbol,
                       IReadOnlyList<MenuRecipe> Recipes, MenuArt Art);
sealed record MenuRecipe(string Id, string Name, double Weight,
                         IReadOnlyList<string> LayerOrder,
                         IReadOnlyList<MenuLayer> Layers,
                         IReadOnlyList<IncompatibilityRule> Rules,
                         IReadOnlyDictionary<string, double> AbsentPercent);
sealed record MenuLayer(string Id, string Name, LayerKind Kind,
                        Colorization? Colorization, IReadOnlyList<Variant> Variants);

// ---- Prepared once, rolled many times. Mirrors WeightedRoller.Prepare's own split. ----
sealed class PreparedMenu { }
PreparedMenu Ticketer.Prepare(MenuBook book);

// ---- One roll. No pixels. What a server stores and sends. ----
sealed record AssetTicket(string Dna, string RecipeId, IReadOnlyList<TicketLayer> Layers);
readonly record struct TicketLayer(string IngredientId, string VariantId,
                                   double? Hue, double? Sat, ColorModel? Model, LayerKind Kind);

AssetTicket? Ticketer.TryRoll(PreparedMenu menu, IRng rng);   // null = rules rejected; caller rerolls

// ---- Wire form: one delimited string. One DB column. Re-parseable, DNA re-verifiable. ----
string      AssetTicket.ToWire();
AssetTicket AssetTicket.Parse(string wire);

// ---- What a renderer draws. Bottom-first, in layerOrder: generation's own paint order. ----
readonly record struct Draw(string ImageKey, MenuRect Source,
                            LayerKind Kind, double Hue, double Sat, ColorModel Model);
IReadOnlyList<Draw> Plating.Plate(Menu menu, AssetTicket ticket);

// ---- Reader. In-box System.Text.Json, over ~8 record shapes. ----
sealed class Menu : IDisposable { static Menu Open(Stream mnu); static Menu Open(string path); }
```

### `Nfty.Ticket` never touches a pixel

`Plating.Plate` returns *instructions*. A renderer — `Nfty.Core`'s `Colorizer` + `Compositor`, or a
fragment shader, or something not yet written — turns them into an image. The colorize rule is one
line and is stated in the spec so a future renderer has something to be checked against:

> result RGB = HSV→RGB(hue, sat, source.R / 255), alpha = source.A

which is exactly what `Colorizer.Apply` computes, via a 256-entry lookup table it builds because the
source's red channel is the only per-pixel input.

### Why the wire form is a string and not JSON

A Ticket is written once per drop and read once per display. It goes in a database column and across
a network by the million. A delimited string is smaller, is trivially indexed, and can be re-parsed
and re-hashed to verify that the DNA it claims is the DNA its contents produce.

```
v1|<recipeId>|<ingId>:<varId>[@<hue>,<sat>,<model>]|...|<dna>
```

Absent layers are **omitted**, never marked — the same decision `Dna.Compute` already makes, for the
same reason: a present layer contributes a part an absent one does not, so omission is unambiguous
and needs no sentinel that no variant id could ever collide with.

`Parse` recomputes the DNA and throws when it disagrees with the one on the wire. A ticket that has
been tampered with in transit fails to parse rather than rendering something nobody rolled.

## The `.mnu` format

A ZIP with a `manifest.json` — the house rule every nfty archive follows, so any unzip tool can
inspect one.

```
manifest.json         menuSchemaVersion, sourceCookBookSha256, canvas, collection,
                      recipes[], ingredients[], art{}
atlas.png             art.mode == "atlas"
art/<recipe>/<ing>/<variant>.png     art.mode == "loose"
```

**Both art modes ship; atlas is the default.** They are the same manifest with a different `art`
block, and a reader handles both because a Menu built for one consumer should not have to be rebuilt
for another.

- **Atlas** — every variant stitched into one PNG, rects in the manifest. One texture to load.
- **Loose** — one PNG per variant. Trivially inspectable, and a consumer that wants to pack them its
  own way can.

### Rules the atlas keeps

- **The cell key is `recipeId/ingredientId/variantId`.** A `.cbk` nests a separate `.igt` inside each
  `.rcp`, so `bg` in two recipes can be two different layers with different variants. A key scoped
  any shallower would collide silently.
- **Identical art is packed once**, deduped on the SHA-256 of the encoded PNG bytes. Two recipes
  sharing a layer cost one cell and one entry.
- **The pixel ceiling is `SpriteSheet.cs`'s, and it is honoured for its reasons** — 16,384 a side and
  separately on area, because a sheet is one contiguous raster and a refusal naming the numbers beats
  an `OutOfMemoryException` half a minute in. Over the ceiling, the writer refuses and says to use
  `--art loose`.
- **Cells are packed in ordinal key order** into a grid of the canvas size, exactly as `SpriteSheet`
  places assets. Deterministic layout: the same book produces the same atlas.

### Schema

`menuSchemaVersion` is the Menu's own, gated the way `ArchiveIo.ReadManifest` gates a manifest —
`Oldest..Current`, refusing anything newer, because a future field this build cannot see could change
what the file means.

**`Schema.Current` for `.cbk` is NOT bumped.** Nothing about a cookbook changes. A Menu is a
different file with a different version line.

`Archives`/`ArchiveKind` gain a seventh kind, `Menu` for `.mnu`. Additive; `TryKindOf` already exists
so no caller grows a second copy of the extension table.

## Rewiring `Generator`

`Generator.RollOne`, `PlanLayers`, `LayerPlan` and the private `RolledAsset` are **replaced** by
`Ticketer`. The generator's loop becomes:

```
prepared = Ticketer.Prepare(MenuBook.From(book))      // manifests only; no image is read
ticket   = Ticketer.TryRoll(prepared, rng)            // null → reroll
if (!seen.Add(ticket.Dna)) continue                   // dedup before rendering, as today
asset    = Render(canvas, ticket, book, number)       // ids → images, then colorize + composite
```

### The invariant this must not break

`Generator` consumes the RNG in a fixed order: one draw for the recipe, then per layer in
`layerOrder` — one draw for the variant (or the absent outcome, which competes in the *same* draw),
then for a Dynamic layer one draw to pick the colour entry and two more to sample hue and saturation.
A Static layer consumes **none**. `Ticketer.TryRoll` must consume exactly that, in exactly that
order, or every Set ever generated stops reproducing.

This is not asserted in prose. It is frozen in a fixture (below) captured before the first file
moves.

## Testing

### 1. `TicketFreezeTests` — captured FIRST, before any file is touched

The demo CookBook's first 200 DNAs at a fixed seed, and the SHA-256 of asset `0001`'s PNG bytes,
recorded into `tests/fixtures/`. Asserted after the extraction.

This is the guard that the refactor changed **no output that has ever been produced**, and it only
works if it is captured before the change. It is step one of the plan for that reason.

It mirrors `FixtureFreezeTests`, and it carries that file's warning: do not regenerate it to make it
pass. A deliberate change to roll order would mean a new baseline beside this one, not an edit to it.

### 2. `TicketRollEquivalenceTests`

`Generator.GenerateStreaming` and `Ticketer.TryRoll` produce the same DNA sequence, over the demo
book and `tests/fixtures/VaporPets.cbk`, across several seeds — including books with rules, with
optional layers, with Static layers and with Custom layers, since each consumes a different number of
draws.

Also asserts the **draw count** is unchanged, via a counting `IRng`. That is the thing
`GeneratorRollRenderSplitTests` protects and the thing a resequenced phase would break.

### 3. `MenuFidelityTests`

Cook a Set with `Generator`. Separately: write a `.mnu`, open it, roll the same seed through
`Ticketer`, `Plate` it, and render the draws with Core's own `Colorizer` + `Compositor` from the
Menu's art. Assert **pixel-identical** to the cooked asset.

Run for **both** `--art atlas` and `--art loose`. This is what proves the Menu carries enough, and
carries it correctly — the atlas rects, the colour, the paint order and the absent layers all at
once.

### 4. `Nfty.Ticket.Tests`

Wire round-trip; a tampered wire refused; the reader on a malformed archive, on a newer
`menuSchemaVersion`, on a missing atlas cell; `Plate` order equals `layerOrder`; and dedupe actually
dedupes.

### 5. `nfty serve` is the format's own proof

`nfty serve <game.mnu> --seed <s> --out asset.png` renders without a CookBook anywhere. If that
produces the same PNG `generate` does, the Menu is complete. The CLI test asserts the command parses;
the pixel assertion lives in `MenuFidelityTests`, per the repo's split.

## CLI

```bash
nfty menu  <book.cbk> --out <game.mnu> [--art atlas|loose]
nfty serve <game.mnu> (--seed <s> | --ticket <wire>) --out <asset.png>
nfty inspect <game.mnu>
```

`serve` takes **either** a seed or a ticket and rejects both together — the same rule `preview`
already keeps about its `.igt` and `.rcp` forms: an option belonging to the other form is rejected,
never ignored.

## UI — tracked, NOT built in this project

| Where | What |
|-------|------|
| CookBook detail card, action band beside **Cook** | `Export Menu…` button |
| new modal `MenuDialog` | output path; `ART: (•) Atlas ( ) Loose` segmented tray; atlas size line with the refusal warning; a manifest line stating exactly what will be written — the `SetExporter.Plan` rule, that the sentence a user reads is generated by the code that then does the writing |
| Explorer status bar | menu-written message |
| `assets/icons/` + `Icons.axaml` | one new glyph. It must differ from its **neighbours** — `download`, `import` and `kitchen` are all box-shaped, and that is the rule a glyph is judged by here |
| edit lock | writing a Menu is a READ of the book → **not** gated |

A Menu is built from a **CookBook**, not from a Set, so it does not belong anywhere on the Export
dialog.

## Out of scope, stated

Commit–reveal fairness and server economy; a database schema; any engine integration; moving
`SelectionOdds`/`UniqueSpace` into `Nfty.Ticket`; NuGet publication.

## The convergence, recorded so it is not foreclosed

A Menu carries colorization per layer. If it also carried a **depth** per layer, a runtime drop could
be extruded into 3D by project 2's mesh builder. Nothing in this design prevents that: `MenuLayer` is
a record with an optional-by-default evolution rule, and `menuSchemaVersion` exists to add a field
without orphaning a file. Do not build it now.
