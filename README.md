# nfty

Generates NFT-style asset collections by stacking layered PNGs — with a twist that makes the output
space far larger than the source art.

Most layered generators composite fixed images. Here a layer can be a **grayscale value-map** that is
colorized at generation time: recolouring preserves each pixel's value and lightness and injects a
hue and saturation, so one hand-drawn variant becomes a whole family of them. A layer is one of three
kinds — **dynamic** rolls its colour per asset, **static** applies one fixed colour deterministically,
and **custom** is full-colour art composited as-is.

Same cookbook and same seed produce byte-identical output, on any machine, in any locale, on any CPU
architecture. That is a guarantee the test suite enforces rather than a hope.

## Getting started

```bash
dotnet build nfty.sln
dotnet run --project src/Nfty.Cli -- --help
```

A first collection, end to end:

```bash
dotnet run --project src/Nfty.Cli -- inspect  tests/fixtures/VaporPets.cbk
dotnet run --project src/Nfty.Cli -- validate tests/fixtures/VaporPets.cbk
dotnet run --project src/Nfty.Cli -- stats    tests/fixtures/VaporPets.cbk
dotnet run --project src/Nfty.Cli -- generate tests/fixtures/VaporPets.cbk --count 8 --seed hello --out ./out
```

The desktop app is the same engine with a UI on top:

```bash
dotnet run --project src/Nfty.Desktop
```

## The six words

The domain is a cooking metaphor, and the words are the model — the file extensions follow them.

| Term | File | What it is |
|------|------|-----------|
| **CookBook** | `.cbk` | The top-level container: canvas size, collection metadata, weighted Recipes |
| **Recipe** | `.rcp` | A complete template for one character *type* — an ordered layer stack plus incompatibility rules |
| **Ingredient** | `.igt` | One layer, or trait-category, with its weighted variant images |
| **Variant** | — | A single image with a weight and a name, held inside an Ingredient |
| **Set** | `.set` | The generated output: images, per-item metadata, rarity and the seed that made it |
| **Kitchen** | `.ktn` | A workspace folder; what is in it is discovered by scanning, never recorded |

Generating a CookBook rolls a Recipe per asset, so one book yields a mixed collection.

A Recipe's layer stack is ordered bottom-to-top, and that order **is** the paint order — depth 1 is
drawn first and sits furthest back. Because it is a list, two layers can never share a depth. Reorder
it with `nfty move ingredient <rcp> --id <id> --to <depth>`, or by dragging a row in the desktop app.

## Lining art up

Layered art only works if the pieces register against each other, so both front-ends can composite a
layer against the ones it will actually sit between:

```bash
nfty preview cat.rcp --seed alpha                     # the whole stack, one deterministic roll
nfty preview cat.rcp --seed alpha --only body,shades  # just those layers, at their real depths
```

In the desktop app, the Ingredient Editor's reference panel does the same live while you draw: switch
on any sibling layer from the Recipe, or any loose `.igt` from the open Kitchen, and it composites
under or over the art depending on its depth. Layers above are ghosted by default so they cannot hide
what you are painting.

Every archive is a ZIP with a `manifest.json` inside — the custom extension is a renamed `.zip`, so
any unzip tool can open one and look.

## How a run works

Per asset: roll a Recipe by weight → roll each layer's Variant by weight → apply the Recipe's
incompatibility rules, re-rolling on a violation → colorize the dynamic and static layers →
composite → hash the **DNA** → reject duplicates → emit.

The DNA is a SHA-256 over the recipe id, each layer's variant id, and the *quantized* colour of each
colorized layer. Quantizing folds a continuous colour space into something that can be counted, which
is what lets `stats` tell you how many unique assets a book can produce before you try to mint them.

## Output

A generated Set carries two metadata files per asset: a standards-pure OpenSea
`metadata/NNNN.json`, and a richer `nfty/NNNN.json` with the DNA, seed, rarity and per-layer colour.
`extend` re-opens a Set and adds to it, recomputing rarity across the whole collection.

## Layout

- `src/Nfty.Core` — the engine. No UI or CLI dependencies, so both front-ends share it.
- `src/Nfty.Cli` — the command line.
- `src/Nfty.App` / `src/Nfty.Desktop` — the Avalonia GUI and its desktop head.
- `tests/` — 1030 tests. `tests/fixtures/` holds archives written by an older build, kept so that
  format changes cannot pass unnoticed.
- `docs/design/mockups/` — the locked visual reference the GUI is built to match.

## Contributing

Read [CLAUDE.md](CLAUDE.md) first — it is the real briefing, and several of its rules are the kind
you would otherwise only learn by breaking something. [AGENTS.md](AGENTS.md) is the short version.

`dotnet build` must stay at zero warnings; warnings are errors and missing XML documentation is a
warning.
