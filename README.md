[![Vaporsoft](https://raw.githubusercontent.com/corderro-artz/corderro-artz.github.io/ddb761c320225187ead71568b1c3f460e15bbd4a/public/vaporsoft/vaporsoft-logo.svg)](https://www.vaporsoft.dev)

# nfty

**A deterministic generator and pixel editor for layered asset collections.**

Most layered generators composite fixed images from an indexed stack. nfty puts a grayscale
colorizer at the core instead: a single value-map is cast to any color by rolling a hue and a
saturation over it, with every pixel's own lightness preserved. Those layers are ordered and
flattened into one image — the shape its namesake, the web3 NFT collection, is built from — so a
handful of drawings becomes thousands of distinct assets. A pixel editor is built into the
application, so the art and the collection it belongs to are authored in one environment rather than
two. The end result is an expansive, reusable asset catalog, or a finished drop with OpenSea
metadata written beside every image.

None of it is approximate. The same book and the same seed produce byte-identical output on any
machine, in any locale, on any CPU architecture, and the program counts the exact number of distinct
assets a book admits *before* you ask it for any. The demo built into the binary admits **615,600**
of them, out of eighteen 32×32 sprites.

**Access**  
[![Manual](https://img.shields.io/website?url=https%3A%2F%2Fwww.vaporsoft.dev%2Fnfty%2F&label=User%20Manual&logo=readthedocs&logoColor=white)](https://www.vaporsoft.dev/nfty/)
[![Release](https://img.shields.io/github/v/release/corderro-artz/nfty?label=Download&logo=github&logoColor=white)](https://github.com/corderro-artz/nfty/releases/latest)

**Delivery**  
[![CI](https://img.shields.io/github/actions/workflow/status/corderro-artz/nfty/ci.yml?branch=main&label=CI&logo=githubactions&logoColor=white)](https://github.com/corderro-artz/nfty/actions/workflows/ci.yml)
[![Docs](https://img.shields.io/github/actions/workflow/status/corderro-artz/nfty/docs.yml?branch=main&label=Docs&logo=githubactions&logoColor=white)](https://github.com/corderro-artz/nfty/actions/workflows/docs.yml)
[![Tests](https://img.shields.io/badge/tests-2088%20passing-3d6b52)](#testing)
[![Warnings](https://img.shields.io/badge/warnings-0-3d6b52)](#house-rules)
[![License: MIT](https://img.shields.io/badge/License-MIT-a11f31?logo=open-source-initiative&logoColor=white)](LICENSE)

**Runtime**  
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Avalonia 12](https://img.shields.io/badge/Avalonia-12.1.1-8B44AC)](https://avaloniaui.net/)
[![Platform](https://img.shields.io/badge/platform-windows%20%7C%20linux%20%7C%20macOS-lightgrey)](https://github.com/corderro-artz/nfty)

> **Using the app rather than working on it?** Read the
> **[User Manual](https://www.vaporsoft.dev/nfty/)** instead — this file is for developers. Its
> source is in [`docs/manual/`](docs/manual/index.md).

## Table of Contents

- [Overview](#overview)
- [Requirements](#requirements)
- [Installation](#installation)
- [Quick Start](#quick-start)
- [Concepts](#concepts)
- [Architecture](#architecture)
- [Command Line](#command-line)
- [Desktop Application](#desktop-application)
- [Performance](#performance)
- [Development](#development)
- [Deployment](#deployment)
- [Troubleshooting](#troubleshooting)
- [Links](#links)
- [Contributing](#contributing)
- [License](#license)

## Overview

nfty is two front-ends over one engine. `Nfty.Core` holds the whole of it — the archive formats, the
colorizer, the roller, the rules engine, the rarity math and the text reports — and carries no UI or
CLI dependency, so the command line and the Avalonia desktop app produce identical bytes from
identical inputs. A collection is authored as a small tree of ZIP archives, generated against a
string seed, and published as images plus two parallel sets of per-asset metadata.

### Capabilities

| Capability | Details |
| --- | --- |
| Value-map colorization | One grayscale drawing rolls a hue and saturation per asset; lightness is the art's, color is the collection's |
| Three layer kinds | Dynamic (rolled color), Static (one fixed color, no RNG), Custom (full-color RGBA, composited as-is) |
| Counted output space | `UniqueSpace` reports the exact number of distinct assets a book admits, and says which direction it is bounded in |
| Deterministic runs | SplitMix64 from a string seed; identical output across locales, machines and CPU architectures |
| Optional layers | A per-recipe chance that a layer is left out entirely, added without changing any existing seed's output |
| Incompatibility rules | Exclude and require constraints between named variants, enforced by re-rolling inside a bounded retry budget |
| Built-in pixel editor | Brush, eraser, shapes, line, flood fill, select-and-move, undo/redo — over a value-map or a color raster |
| Dual metadata | Standards-pure OpenSea `metadata/NNNN.json` plus a rich `nfty/NNNN.json` with DNA, seed, rarity and per-layer color |
| Export and sealing | Four independent content axes, a plan you read before it writes, and AES-256-GCM sealing for a view-only handoff |
| Extend | Re-open a cooked Set, add to it, and recompute rarity across the whole collection |
| Open formats | Every archive is a ZIP with a `manifest.json`; the custom extension is a renamed `.zip` |

## Requirements

| Requirement | Version | Notes |
| --- | --- | --- |
| .NET SDK | 10.0 | Pinned by [`global.json`](global.json); needed to build, not to run a release |
| .NET desktop runtime | 10.0 | Only for the two framework-dependent download shapes |
| OS | Windows, macOS, Linux | The desktop head is Avalonia; the CLI is platform-agnostic |
| Python | 3.10+ | Optional — the manual, the demo generator and the icon build are Python tools |

### Dependencies

| Category | Packages |
| --- | --- |
| Imaging | `SixLabors.ImageSharp` — pinned to **3.1.11** on purpose; 4.0.0 requires a build-time license key |
| CLI | `System.CommandLine` |
| UI | `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Skia`, `Avalonia.HarfBuzz`, `CommunityToolkit.Mvvm` |
| Composition | `Microsoft.Extensions.DependencyInjection` |
| Tests | `xunit.v3`, `Avalonia.Headless.XUnit`, `coverlet.collector` |

Package versions live in [`Directory.Packages.props`](Directory.Packages.props) and never in a
`.csproj`.

## Installation

Prebuilt Windows builds are on the
[releases page](https://github.com/corderro-artz/nfty/releases/latest). Every download carries both
front-ends, the demo CookBook, and the licenses.

| Shape | Size | .NET 10 needed | For |
| --- | --- | --- | --- |
| **Portable** | 84 MB | No | Unzip anywhere and run. Start here if you are unsure. |
| **Single file** | 76 MB | No | One `.exe`. Unpacks itself on first launch, so that launch is slower. |
| **Single file, .NET** | 14 MB | Yes | One `.exe` with the runtime left out. |
| **Framework-dependent** | 14 MB | Yes | The same, as a folder. |

nfty keeps its settings in a `.nfty` folder **beside the executable**, so a copy carries its own
recent-files list and palette and writes nothing elsewhere. Move or delete the folder and nothing is
left behind.

To build from source:

```bash
git clone https://github.com/corderro-artz/nfty.git
cd nfty
dotnet build nfty.sln
```

> **Note:** Native AOT is deliberately unsupported. It links, but the resulting app cannot resolve
> its own views (`ViewLocator` builds a type name at runtime) or read a `.cbk`
> (`System.Text.Json` reflection). A release must not contain a binary that starts and then does
> nothing.

## Quick Start

A first collection, end to end, against the demo CookBook built into the program — no file to find
and no archive to download:

```bash
dotnet run --project src/Nfty.Cli -- demo .                 # writes ChestDemo.cbk here

dotnet run --project src/Nfty.Cli -- inspect  ChestDemo.cbk # what is in it
dotnet run --project src/Nfty.Cli -- validate ChestDemo.cbk # is it sound
dotnet run --project src/Nfty.Cli -- stats    ChestDemo.cbk # what odds its weights imply
dotnet run --project src/Nfty.Cli -- generate ChestDemo.cbk --count 8 --seed hello --out ./out
```

The desktop app is the same engine with a UI on top:

```bash
dotnet run --project src/Nfty.Desktop
```

Its Landing screen carries **Open the demo CookBook**, which unpacks that same book beside the app
and opens it. The demo is yours to break, and reopening it keeps whatever you did to it.

The demo is a small collection of layered chests: two Recipes, seven Ingredients covering all three
layer kinds, two optional layers, one incompatibility rule, and **615,600** distinct assets out of
eighteen 32×32 sprites — which is the argument for value-map colorization, made in a file you can
open.

## Concepts

### The Six Words

The domain is a cooking metaphor, and the words *are* the model — the file extensions follow them.

| Term | File | What it is |
| --- | --- | --- |
| **CookBook** | `.cbk` | The top-level container: canvas size, collection metadata, weighted Recipes |
| **Recipe** | `.rcp` | A complete template for one character *type* — an ordered layer stack plus its incompatibility rules |
| **Ingredient** | `.igt` | One layer, or trait-category, with its weighted variant images |
| **Variant** | — | A single image with a weight and a name, held inside an Ingredient |
| **Set** | `.set` | The generated output: images, per-item metadata, rarity, and the seed that made it |
| **Kitchen** | `.ktn` | A workspace folder of loose parts; its contents are discovered by scanning, never recorded |

Generating a CookBook rolls a Recipe per asset, so one book yields a *mixed* collection. `.cbk` nests
`.rcp` nests `.igt`, so the archive layers mirror the domain layers.

### Layer Kinds

| Kind | Source art | At generation time |
| --- | --- | --- |
| **Dynamic** | Grayscale value-map | Rolls a hue and saturation per asset from a weighted range |
| **Static** | Grayscale value-map | Applies one fixed color, deterministically, consuming no RNG |
| **Custom** | Full-color RGBA | Composited exactly as-is; never recolored |

Dynamic and static take their **value** from the grayscale map and only their hue and saturation from
the color, which is what multiplies the output space. Custom carries no colorization at all, and
`Validator` refuses one that does.

### Depth

A Recipe's layer stack is ordered bottom-to-top, and that order **is** the paint order — depth 1
paints first and sits furthest back. Because it is a list rather than a stored `z`, two layers can
never share a depth.

```bash
nfty move ingredient cat.rcp --id shades --to 3
```

…or drag a row in the desktop app. Reordering produces a **different collection**, not a re-render:
the generator consumes one roll per layer in order, so moving a layer moves which roll reaches it.

### Two Ways to Mint

Rejecting duplicates is the default, and it is a promise: no two assets in a Set are the same, and a
run that cannot fill its count from the unique space fails and states the real maximum.

It is not the only way. **Allow repeats** in the Cook dialog, or `--unlimited` on the command line,
keeps every roll — so any count is producible from any book, and identity is the token number as
ERC-721 defines it. The rules are still enforced and the weights still decide rarity: a low-weight
variant stays uncommon, a layer with a low appearance chance is rarer still, and the rarest asset in
the drop is the low-weight variant of a low-chance layer.

The two modes are not interchangeable for reproducibility. The same book and seed give a *different*
collection under each, because rejecting a duplicate spends a roll the other mode does not — which is
why `set.json` records `uniqueDna` alongside the seed.

## Architecture

```text
nfty/
├── src/
│   ├── Nfty.Core/          The engine — no UI or CLI dependency   (net10.0)
│   │   ├── Model/          Immutable domain records
│   │   ├── Formats/        ZIP + manifest IO, Validator
│   │   ├── Imaging/        Conversion, colorization, compositing, preview
│   │   ├── Generation/     RNG, rollers, DNA, rules engine, orchestrator
│   │   ├── Editing/        Layer depth, region edits, image import
│   │   ├── Output/         Set writer, dual metadata, extend loader
│   │   ├── Publish/        Export planning and AES-256-GCM sealing
│   │   └── Stats/          Rarity, and the reports both front-ends print
│   ├── Nfty.Cli/           System.CommandLine wiring              (net10.0)
│   ├── Nfty.App/           Avalonia GUI — ViewModels, Views, Themes
│   └── Nfty.Desktop/       Desktop head — window, clipboard, pickers
├── tests/                  2,088 tests across three xunit.v3 projects
│   └── fixtures/           Archives an older build wrote, and still reads
├── docs/
│   ├── manual/             The end-user manual (Material for MkDocs)
│   ├── design/archive/     A dated visual record — history, not spec
│   └── superpowers/        Design specs, newest first
├── tools/                  Demo, icons, docs capture, release build
└── nfty.sln
```

### Generation Pipeline

```text
  roll a Recipe            by cookbook weight
      │
      ▼
  roll each Variant        by ingredient weight
      │
      ├──► rules violated?      ──► re-roll
      ▼
  colorize dynamic + static     (custom composites as-is)
      │
      ▼
  composite in depth order
      │
      ▼
  hash the DNA
      │
      ├──► already generated?   ──► re-roll
      ▼
  emit
```

The **DNA** is a SHA-256 over the recipe id, each layer's variant id, and the *quantized* color of
each colorized layer. Quantizing folds a continuous color space into something countable, which is
what lets `stats` tell you how many unique assets a book can produce before you try to mint them.

### Design Principles

- **The engine is the product; the front-ends are views of it.** Anything a user reads — a report, a
  DNA-space figure, an export summary — is rendered in `Nfty.Core` so the CLI and the GUI cannot
  disagree about it.
- **Determinism is a contract, not a side effect.** Every sort that reaches an output file uses
  `StringComparer.Ordinal`; the seed hash is read little-endian rather than native-endian; anything
  that sums stays sequential, because floating-point addition is not associative.
- **Roll sequentially, render in parallel.** The Nth asset's decisions depend on every decision
  before it, so rolling can never be parallel. Rendering is a pure function of a decided roll, so it
  parallelizes freely — and `GenerateStreaming` is the sequential oracle the parallel path is tested
  against at 1, 2, 3 and 8 threads.
- **A count says which direction it is bounded in.** `Exact`, `AtLeast`, `AtMost`, `Unknown` — a
  product built on an under-count bounds the truth in neither direction, and a `bool` could not say
  so.
- **Nothing resamples.** Every image is pixel art shown at a size unrelated to its pixel count, so
  the whole stack scales with nearest neighbor and import refuses a wrong-sized picture rather than
  scaling it.
- **Callers own images.** `Nfty.Core` hands back live `Image<Rgba32>`; `LoadedCookBook` and
  `GeneratedSet` are `IDisposable` and free the whole tree.

## Command Line

```bash
dotnet run --project src/Nfty.Cli -- --help
```

| Command | What it does |
| --- | --- |
| `demo <dir>` | Write out the built-in demo CookBook — embedded, so it works on a copy with nothing beside it |
| `inspect <file>` | Print the tree of a `.cbk` / `.rcp` / `.igt` / `.set` / `.tin`, or list a `.ktn`. `--voxel` adds a partial-alpha report |
| `validate <cbk>` | Report **every** problem found, not just the first |
| `stats <cbk>` | The odds the weights imply, per Recipe and per Variant |
| `preview <file>` | Render a PNG exactly as generation would — one variant, or a whole rolled stack |
| `generate <cbk>` | Generate a Set |
| `extend <cbk> <set>` | Grow an existing Set, recomputing rarity across all of it |
| `export <set>` | Publish a cooked Set: choose what goes with it, and in what shape |
| `new ingredient\|recipe\|cookbook\|kitchen` | Build an archive from a manifest and its parts |
| `add variant\|ingredient\|recipe\|rule` | Append to an existing archive |
| `move ingredient` | Reorder a Recipe's layers |
| `remove rule` | Remove one incompatibility rule, addressed by its 1-based position |
| `set chance` | Set how often a layer is left out of an asset entirely |

Errors surface as a message, never a stack trace — add `--verbose` for the trace.

### Lining Art Up

Layered art only works if the pieces register against each other, so both front-ends can composite a
layer against the ones it will actually sit between:

```bash
nfty preview cat.rcp --seed alpha                     # the whole stack, one deterministic roll
nfty preview cat.rcp --seed alpha --only body,shades  # just those layers, at their real depths
```

### Sealing

`export --seal` is AES-256-GCM over PBKDF2-SHA256 at 600,000 iterations, and the view-only mark is
HMAC-bound to a second key derived from the same passphrase, so a third party cannot strip it. A
sealed export takes its own extension (`.tin`) rather than being a `.set` whose bytes are ciphertext,
because a recipient double-clicking it has to be told what it is before opening it.

There is deliberately **no `--passphrase` flag** anywhere — argv is visible to every process, lands in
shell history and is captured by CI logs. `--key` names a *source* instead: `env:NAME`, `file:PATH`,
`stdin` or `prompt`, and an unprefixed value is an error rather than a guess.

> **Note:** Against the person holding the passphrase, the view-only mark buys a refusal in the
> product and nothing more — the format is open source. It is a lock on a door, not a wall.

## Desktop Application

The same `Nfty.Core` engine behind an Avalonia UI: an Explorer over the open CookBook, a per-type
detail pane, a Set browser over cooked output, and an **Ingredient Editor** with a full paint stack.

- **Two paint modes.** Grayscale edits the value-map; color mode paints RGBA and saves as a Custom
  ingredient. Each mode carries its own saved palette, and they swap when the mode does.
- **The opacity lock** is on by default and keeps every painted pixel fully opaque or fully erased,
  because partial alpha does not voxelize cleanly.
- **The reference panel** composites sibling layers, or loose `.igt` files from the open Kitchen,
  under and over the art while you draw.
- **A gesture previews the pixels it will commit**, not an outline standing in for them — the shape
  tools fill, and a line commits at the brush's size in the brush's ink.
- **Pixel art scales with nearest neighbor**, everywhere. Nothing in the app or the engine blurs,
  resamples or anti-aliases an author's pixels.

## Performance

Measured inside the real app with [`Nfty.Core/Diagnostics/Perf.cs`](src/Nfty.Core/Diagnostics/Perf.cs)
— named scopes with wall time and managed allocation, free when off, enabled by `NFTY_PERF=1`. It
measures whole user-facing operations rather than microbenchmarks, which is why it is not
BenchmarkDotNet.

| Operation | Before | After | What changed |
| --- | --- | --- | --- |
| Generate 200 assets @ 512px | 3,020 ms | 662 ms | Rendering parallelized; rolling stays sequential |
| Write 200 assets @ 512px | 2,078 ms | 454 ms | Per-asset writes parallelized; rarity finished first |
| Set browser resize (8 steps) | 1,213 ms / 90 MB | 342 ms / 24 MB | Row chunking caches its last answer |

Thumbnail decode scales with the canvas the author chose — ~0.5 ms from a 64px source, **7.1 ms from
a 1000px one** — so a screenful of forty large tiles was ~280 ms of frozen UI per scroll. It decodes
off the UI thread now. A ZIP archive is not thread-safe, so archive extraction stays sequential and
only the decode runs wide, and anything that *sums* stays sequential too: a different addition order
is a different number, and rarity and the DNA-space count are both promises.

## Development

```bash
dotnet build nfty.sln                      # build everything
dotnet test nfty.sln                       # run all tests
dotnet run --project src/Nfty.Cli -- --help
dotnet run --project src/Nfty.Desktop
```

### Testing

```bash
dotnet test tests/Nfty.Core.Tests                             # one project
dotnet test --filter FullyQualifiedName~DnaTests              # one class
dotnet test --filter FullyQualifiedName~DnaTests.Same_selection_same_dna
```

Tests are named `Snake_case_sentences`, which is what `--filter` matches. Fixtures are built in
memory from tiny synthetic images; there are **no golden-image files**, and every image assertion
reads a pixel. `tests/fixtures/` is the one exception and the only place real archives are read from
disk — their value is that an older build wrote them and they still read, so they are never
regenerated to make a failing test pass.

### Visual Verification

Visual work is verified from a rendered frame, never from the markup:

```bash
NFTY_CAPTURE=1 NFTY_CAPTURE_DIR=./frames dotnet test tests/Nfty.App.Tests \
  --filter FullyQualifiedName~VisualCapture
```

…then *look at* the PNGs. Nearly every GUI defect this project has fixed was found that way and would
have been missed by reading code. Three companion sweeps catch what a frame cannot:
`DarkModeContrastTests` scores every text run against the surface it lands on in both themes,
`ThemeResourceTests` proves every `DynamicResource` the markup names resolves in both, and
`MinimumWindowFitTests` drives every page at the smallest window the app allows.

### House Rules

- **The build is the lint.** `TreatWarningsAsErrors` and `GenerateDocumentationFile` are on, so a
  warning or an undocumented public member fails the build. Zero warnings, always.
- **Generated files are committed, and the generator is the source.** `Themes/Icons.axaml` comes from
  `assets/icons/*.svg` via `python tools/icons/build.py`; the demo `.cbk` comes from
  `tools/demo/`. Edit the generator, re-run it, commit both — a test fails if the pair disagrees.
- **Token brushes only in views.** No raw hex outside `Themes/Tokens.axaml`, and a new color goes in
  **both** theme dictionaries.
- **Reserve the space, toggle the ink.** A control appearing or disappearing must not move, resize or
  reflow anything around it.
- **Never edit anything in `docs/design/archive/`.** It is a dated record; the app has moved past it
  by design, and a difference is not a defect.

## Deployment

```bash
python tools/release/build.py <version> win-x64   # writes four archives to .release/
gh release create v<version> .release/*.zip
```

- **Version**: `<Version>` in [`Directory.Build.props`](Directory.Build.props), stated once and
  carried by every assembly in the solution.
- **CI**: [`ci.yml`](.github/workflows/ci.yml) — builds and tests on Windows, audits the dependency
  graph for advisories separately, and re-runs the engine and CLI on Ubuntu, macOS (arm64) and
  Windows, because determinism is promised across architectures.
- **Manual**: [`docs.yml`](.github/workflows/docs.yml) publishes `docs/manual/` to GitHub Pages. The
  site is built there rather than committed.
- **Releases**: [GitHub Releases](https://github.com/corderro-artz/nfty/releases) — each carries both
  front-ends, the demo CookBook, the MIT license and the SIL Open Font License.

## Troubleshooting

> **Note:** ImageSharp must stay at **3.1.11**. Version 4.0.0 requires a build-time license key
> (`sixlabors.lic`), which is account-specific and is not in this repository.

> **Note:** The manual needs its own toolchain: `pip install -r docs/requirements.txt`, then
> `mkdocs serve`. `docs_dir` is `docs/manual`, not `docs/`, so the archived mockups and design specs
> stay out of the published site.

> **Note:** GUI tests render through Avalonia's headless backend with Skia, so they run on Windows in
> CI. The engine and CLI carry the determinism guarantees and are the ones re-run cross-platform.

> **Note:** Sealing needs `AesGcm`, which is a platform capability rather than a language feature.
> `Seal.IsSupported` answers for the current machine.

## Links

| Resource | Link |
| --- | --- |
| Repository | [github.com/corderro-artz/nfty](https://github.com/corderro-artz/nfty) |
| User manual | [vaporsoft.dev/nfty](https://www.vaporsoft.dev/nfty/) |
| Latest release | [GitHub releases](https://github.com/corderro-artz/nfty/releases/latest) |
| Developer briefing | [CLAUDE.md](CLAUDE.md) · [AGENTS.md](AGENTS.md) |
| Deferred work | [BACKLOG.md](BACKLOG.md) |
| Design specs | [docs/superpowers/](docs/superpowers/) |
| CI workflow | [.github/workflows/ci.yml](.github/workflows/ci.yml) |
| Docs workflow | [.github/workflows/docs.yml](.github/workflows/docs.yml) |
| Actions | [GitHub Actions](https://github.com/corderro-artz/nfty/actions) |
| Issues | [GitHub issues](https://github.com/corderro-artz/nfty/issues) |
| Pull requests | [GitHub pull requests](https://github.com/corderro-artz/nfty/pulls) |
| License | [LICENSE](LICENSE) |
| Vaporsoft | [vaporsoft.dev](https://www.vaporsoft.dev) |

## Contributing

**Read [CLAUDE.md](CLAUDE.md) first.** It is the real briefing — the domain model, the file formats,
and the invariants that are load-bearing but not obvious from the code. Several of its rules are the
kind you would otherwise only learn by breaking something. [AGENTS.md](AGENTS.md) is the short
version.

1. Create a branch from `main` for the change.
2. Write tests in the existing style — seeded distribution tests for rollers, exact-pixel tests for
   imaging, round-trip tests for archives.
3. Run `dotnet build nfty.sln` and `dotnet test nfty.sln` before opening a pull request; both must be
   clean, and a warning is a failure.
4. For any visual change, capture frames and look at them. Attach one if it helps the review.
5. Open a pull request with enough context to review the domain, the invariants and the visual
   impact.

## License

[MIT](LICENSE). The interface is set in **IBM Plex**, bundled with the app under the
[SIL Open Font License](src/Nfty.App/Assets/Fonts/OFL.txt) — that license travels with any
redistribution of the binaries, which is why every release archive carries a copy.

---

Copyright © 2026 [Corderro Artz](https://github.com/corderro-artz) / [Vaporsoft](https://www.vaporsoft.dev).
