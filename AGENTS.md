# AGENTS.md

**Read [CLAUDE.md](CLAUDE.md) before changing anything.** It is the project's real briefing — the
domain model, the file formats, and the invariants that are load-bearing but not obvious from the
code. This file exists because `AGENTS.md` is what most coding agents look for, and for a long time
it contained only communication-style preferences: any agent that read it and stopped there worked
this repository without knowing a single one of the rules below.

Everything here is a summary. Where this file and `CLAUDE.md` disagree, `CLAUDE.md` wins.

## Build and test

```bash
dotnet build nfty.sln     # must stay at 0 warnings — TreatWarningsAsErrors is on
dotnet test nfty.sln      # all three projects
```

## The things that will bite you

- **Do not upgrade `SixLabors.ImageSharp` past 3.1.11.** 4.0.0 needs a build-time license key that is
  account-specific and gitignored, so upgrading makes the repo unbuildable for everyone else.
- **Do not regenerate `tests/fixtures/`.** Those archives were written by an older build and the
  point of them is that they still read. Regenerating one to make a failing test pass launders a
  format change instead of catching it.
- **Do not edit anything in `docs/design/mockups/`.** They are the locked 1:1 visual reference; the
  app moves to match them, never the reverse. A deliberate divergence is allowed but must be
  commented at the point of divergence with the reason.
- **Determinism is a product guarantee.** Same cookbook + same seed ⇒ byte-identical output, across
  locales and CPU architectures. Every sort that can reach an output file uses
  `StringComparer.Ordinal`; every number that reaches a file or a report is formatted with
  `CultureInfo.InvariantCulture`. A default `OrderBy` on strings is a bug here.
- **Callers own image disposal.** `Nfty.Core` hands back live `Image<Rgba32>` objects in both
  directions. `LoadedCookBook`/`LoadedRecipe`/`LoadedIngredient` and `GeneratedSet`/`GeneratedAsset`
  are all `IDisposable`; `using` them is the default, and a reader that throws part-way must dispose
  what it already decoded.
- **All manifest JSON goes through `Json.Options`.** Serializing with default options silently
  breaks round-trips.
- **Verify GUI changes from a rendered frame, not from the markup.** Nearly every visual defect this
  project has had was found by looking at a PNG and would have been missed by reading XAML:
  `NFTY_CAPTURE=1 NFTY_CAPTURE_DIR=<dir> dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~VisualCapture`.
- **Colors live in `Themes/Tokens.axaml` only**, and a new one goes in *both* theme dictionaries.
  Avalonia hex is `#AARRGGBB`; the mockups' CSS is `#RRGGBBAA`, so the alpha moves to the front.
- **Tests must never touch the real `%APPDATA%`** — inject a temp directory into `RecentsService`.
- **Layer depth is `layerOrder` read a second way, never a stored field.** `LayerDepth` projects it:
  1-based, dense, bottom-first — depth 1 paints first and sits furthest back. Do not add an integer
  `z` to a manifest; a list makes "two layers at one depth" unrepresentable rather than merely
  invalid, and `Validator`'s existing bijection rules already *are* the depth invariant.
- **Reordering a recipe produces a different collection, not a re-render.** `Generator.RollOne` walks
  the layers in `layerOrder` taking one roll each, so moving a layer moves which RNG draw reaches it:
  same seed + reordered book ⇒ different pixels *and* different identities. Depth must never enter
  the DNA hash — that would invalidate every Set ever minted.
- **In the GUI, geometry is fixed: reserve the space, toggle the ink.** A control appearing must not
  move or resize anything. `Opacity`/`IsHitTestVisible`, never `IsVisible`, on anything occupying
  layout. Prove it by pixel-diffing two captured frames.
- **`DragDrop.DoDragDropAsync` does not work under `Avalonia.Headless`** (no platform drag source; a
  call never returns). Use pointer capture, and always ship a keyboard path alongside any drag.

## The domain, in one table

The model is a cooking metaphor and the words are load-bearing.

| Term | File | What it is |
|------|------|-----------|
| **CookBook** | `.cbk` | An uncooked Set: canvas, collection metadata, weighted Recipes |
| **Recipe** | `.rcp` | A complete template for one character type: an ordered layer stack plus rules |
| **Ingredient** | `.igt` | One layer / trait-category, with its weighted variant images |
| **Variant** | — | One image + weight + name, inside an Ingredient |
| **Set** | `.set` | The generated output: images, per-item metadata, rarity, seed |
| **Kitchen** | `.ktn` | The workspace folder; membership is discovered by scanning, never recorded |

Every archive is a ZIP with a `manifest.json`, so any unzip tool can open one — keep it that way.
