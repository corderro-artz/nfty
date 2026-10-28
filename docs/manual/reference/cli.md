# Command line

Everything the app does, the command line does too. Useful for scripting, for checking a project
before you cook it, and for the one job the app does not offer at all -- [extending a
Set](../how-to/more-assets.md).

```bash
dotnet run --project src/Nfty.Cli -- <command> [args]
```

The examples below write `nfty` for brevity.

## The demo

| Command | Does |
|---|---|
| `nfty demo <dir>` | Writes the built-in demo CookBook into a folder. The archive is inside the program, so this needs no network and no other files. |
| `nfty demo <dir> --force` | Replaces a copy that is already there. Without it, an existing `ChestDemo.cbk` is left exactly as it is. |

See [The demo CookBook](../get-started/the-demo.md) for what is in it.

## Reading

| Command | Does |
|---|---|
| `nfty inspect <file>` | Prints what is inside any `.cbk`, `.rcp`, `.igt`, `.set`, `.ktn` or `.tin`, plus a book's own palette. A `.cbk` also prints its collection name and symbol, target supply and recipe order. A cooked Set also works as a **folder** -- which is what `generate --out` writes when you do not pass `--pack`. |
| `nfty inspect <tin>` | A sealed export prints its header with **no passphrase**: the collection, the asset count, the sender's note and what you are permitted to do. Add `--key <source>` and it prints the full report of what is inside. |
| `nfty inspect <file> --voxel` | Also lists every variant carrying partial transparency. Costs a full scan of every image. Refused on a `.ktn` and on a cooked Set: both list paths without opening them, and a Set is the output rather than the source -- run it on the CookBook that produced it. |
| `nfty validate <file>` | Reports every problem it finds, rather than stopping at the first. Takes a `.cbk`, or a loose `.rcp` or `.igt` (checked for everything that needs no canvas). |
| `nfty stats <cbk>` | The odds the weights imply, trait by trait, plus the unique DNA space. |

`inspect` on a cooked Set reports what a run **actually produced** -- its seed, the CookBook hash it
came from, whether unique DNA was required, and the observed trait percentages. `stats` on the
CookBook reports what the weights **predict**. Both group their traits the same way, so the same
trait is in the same place in each; `stats` additionally splits its rows by Recipe, because a Set's
rarity is collection-wide by definition.

```
nfty inspect ./collection          # what this run produced
nfty stats   mybook.cbk            # what its weights imply
```

## Rendering

```bash
nfty preview aura.igt --variant glow --color hsv:200,70,90 --out one.png
nfty preview cat.rcp --seed alpha --out stack.png
```

An `.igt` renders one variant; a `.rcp` renders the whole stack rolled at a seed, with `--only` and
`--with` to include or exclude layers. Options belonging to the other form are **rejected**, not
ignored.

## Making a 3D model

```bash
nfty model sprite.png --out sprite.glb
nfty model sprite.png --out sprite.glb --front 2 --back 1
nfty model hat.igt --variant red --out hat.gltf --format gltf
nfty model chest.rcp --seed launch --out chest.glb
```

Every fully opaque pixel becomes a column of voxels: one for the paper, plus its **front** depth
toward you and its **back** depth away from you. The column's sides take the art's edge pixel color, however dark it is. A
`.png` has no stored depth, so it is flat paper unless you fill it; an `.igt` variant uses the depth
stored with it. A Dynamic or Static variant rises as its stored **gray value-map**, as a loose
picture of the same pixels would; there is no color option, because color is decided when an asset is
cooked. The Rise page does the same thing with a picture, a layer, a roll or a cooked asset: see
[Make a 3D model](../how-to/make-a-3d-model.md).

| Option | Does |
|---|---|
| `--out` | The model to write. Required. Its extension must match the format: `.glb`, or `.gltf`. |
| `--variant <id>` | Which variant of an `.igt` to rise. Required for an `.igt`, refused for a `.png`. |
| `--seed <text>` | Roll a Recipe (`.rcp`) with this seed and rise the whole stack, the same draw `nfty preview chest.rcp --seed <text>` shows. Required for an `.rcp`, refused otherwise. |
| `--front N --back M` | Fill-all: every opaque pixel gets front `N` and back `M` (0 to 255 each), replacing any stored depth. Pass both or neither. Refused for an `.rcp`, which takes the depth its layers store. |
| `--format` | `glb` (one file, the default), `gltf` (text plus a `.bin` beside it) or `gltf-embedded` (one text file). |
| `--pivot` | Which point of the model sits at the origin: `bottom-center` (the default), `center` or `min-corner`. |
| `--voxel-size` | World units per pixel, from 0.001 to 1000. Defaults to 1. The Rise page and the cook dialog keep the same range. |

A picture with a **partly transparent** pixel is refused, naming it: a voxel is either there or not,
and nfty does not round your art for you. `nfty inspect <file> --voxel` lists the variants that carry
partial transparency. Options belonging to the other form are **rejected**, not ignored.

## Cooking

```bash
nfty generate mybook.cbk --count 500 --seed launch --out ./collection
nfty generate mybook.cbk --count 500 --seed launch --out ./collection --pack
nfty extend   mybook.cbk ./collection --to 750
```

| Option | Does |
|---|---|
| `--count` | How many assets. Required. |
| `--seed` | The seed. Same book plus same seed is byte-identical output. |
| `--out` | Where to write. Required. |
| `--pack` | Also package the folder as a single `.set`, written **inside** the output folder and named after it. |
| `--recipe <id>` | Restrict to one Recipe id instead of rolling by weight. |
| `--unlimited` | Skip the uniqueness requirement. Assets may repeat; identity is the token number. Rules are still enforced. |
| `--max-rerolls` | Per-asset reroll budget before giving up. |
| `--models` | Also write a 3D model of every asset into `models/`, named like its image (`0001.glb`). See [Making a 3D model](#making-a-3d-model). Refused, with nothing written, when any variant of the book has a partly transparent pixel. An asset with nothing fully opaque gets no model and is listed (`skipped (nothing fully opaque): #0007`). |
| `--model-format` | With `--models`: `glb` (the default), `gltf` or `gltf-embedded`. |
| `--pivot`, `--voxel-size` | With `--models`: as for `nfty model`. |

The three model options are **rejected** without `--models`, not ignored. `inspect` on the finished Set prints a `Models:` line (format, pivot, voxel size, and how many assets were skipped).

`extend` takes no model options: a Set is modeled whole or not at all, so it reuses the options recorded in `set.json` and makes models for the new assets in the same format. Extending a modeled Set with a book that has partial alpha is refused.

## Publishing

A cook writes *your* copy and holds everything. `export` writes somebody else's.

```bash
nfty export ./collection --out ./dist --preset marketplace
nfty export ./collection --out ./dist --preset fullproject --book mybook.cbk
NFTY_KEY=... nfty export ./collection --out ./dist --preset sealedcritique --key env:NFTY_KEY
```

| Option | Does |
|---|---|
| `--out` | Folder to write into. Required. The export is named after the **collection**, not the folder you cooked into. |
| `--preset` | `marketplace`, `assetpack`, `fullproject` or `sealedcritique`. A starting point; every flag below overrides it. |
| `--images` / `--no-images` | The generated art (`images/`). |
| `--opensea` / `--no-opensea` | The standard ERC-721 fields (`metadata/`). |
| `--nfty` / `--no-nfty` | Per-asset DNA, seed and layer colors (`nfty/`). What a buyer wants and a public listing does not. |
| `--models` / `--no-models` | The 3D models (`models/`). `--models` is refused on a Set cooked without them. |
| `--sheet` | Also stitch every asset into one `spritesheet.png`, in the Set's own numbering, carrying its grid inside the file. |
| `--sheet-cols N` / `--sheet-rows N` | The sheet's grid. Need `--sheet`; one alone lets the other follow. |
| `--watermark` | Stamp each exported image with its set number. The cooked Set is never touched and models never carry it. |
| `--watermark-corner <corner>` | `top-left`, `top-right`, `bottom-left` or `bottom-right` (the default). Needs `--watermark`. |
| `--book <cbk>` | Ship the source CookBook alongside. A Set records only its book's hash, so the path has to be given. |
| `--folder` / `--pack` | Shape. `--pack` (one archive) is the default; a sealed export is always one file. |
| `--seal` | Encrypt it and mark it view-only. Writes a `.tin`. |
| `--note "<text>"` | A line for the recipient. On a sealed export it travels in the clear. |
| `--key <source>` | Where the passphrase comes from — see below. Omit it and nfty asks at the terminal. |

A Set cooked with `--models` ships its `models/` in every preset except `marketplace`, which leaves
them out; `--models` / `--no-models` override that. The export prints a `3D models (N)` line when it carries them.

`set.json` always ships -- it is what makes the result a Set rather than a folder of pictures, and it
carries the collection-wide rarity table, so even the leanest export still answers "what is this and
how rare is what". It also carries the seed and the source book's hash; a seed is inert without the
book, but it is there.

The command prints every part that went, by name, and warns when the export carries the source
CookBook. A `--book` that is not the book that cooked the Set is warned about on stderr and still
shipped, the rule `extend` keeps about the same comparison. Dependent options (`--sheet-cols` without
`--sheet`, `--watermark-corner` without `--watermark`) are **rejected**, not ignored.

### The grid a spritesheet carries

```bash
nfty sheet grid spritesheet.png                  # prints the embedded grid (or none) and the detected one
nfty sheet grid spritesheet.png --cols 8 --rows 4
nfty sheet grid spritesheet.png --cell 64x64
nfty sheet grid spritesheet.png --clear
```

Writing or clearing rewrites the file in place, keeping every pixel. A grid that does not divide the
picture is refused with the reason. `--cols` and `--rows` go together; `--cell` is the other way to
say the same grid; `--clear` takes none of them.

### Where a passphrase comes from

`--key` names its **source**. An unprefixed value is an error, never a guess -- the same rule a
[color spec](../understand/layer-kinds.md) follows, and for a sharper reason: `--key hunter2` looks
like it works, and treating it as the passphrase would put your secret in the process list.

| Source | Reads |
|---|---|
| `env:NAME` | An environment variable. The usual answer for CI. |
| `file:PATH` | The first line of a file. A trailing newline is dropped; spaces are kept. |
| `stdin` | One line from standard input. Leaves nothing anywhere. |
| `prompt` | Asks at the terminal, echoing nothing. The default when `--key` is omitted. |

They are not interchangeable, which is why you pick one. An environment variable is inherited by
every child process and readable from `/proc/<pid>/environ` by the same user on Linux; a key file
can be mode 600; a pipe leaves no trace at all.

```bash
nfty export ./collection --out ./dist --preset sealedcritique --key file:./key.txt
printf '%s' "$PASS" | nfty export ./collection --out ./dist --preset sealedcritique --key stdin
```

!!! warning "There is no `--passphrase`"

    An argument on a command line is visible to every process on the machine while it runs, lands in
    your shell history, and is captured verbatim by CI logs. That is why the sources above exist and
    why an unprefixed `--key` is refused rather than accepted.

See [Share a collection](../how-to/share-a-collection.md) and
[What sealing does and does not do](../understand/sealing.md).

## Authoring

Build archives without the app -- useful for generating a project from a script.

```bash
nfty new ingredient aura.igt --manifest aura.json --images ./aura-pngs
nfty new recipe     cat.rcp  --manifest cat.json  --ingredients ./igt
nfty new cookbook   book.cbk --manifest book.json --recipes ./rcp
nfty new kitchen    Studio.ktn --name Studio

nfty add variant    aura.igt --id spark --weight 30 --image spark.png
nfty add ingredient cat.rcp  --igt aura.igt --index 3
nfty add recipe     book.cbk --rcp cat.rcp --weight 60

nfty move ingredient cat.rcp --id aura --to 2

nfty set depth chestbody.igt --variant stone --map stone-depth.png
```

`nfty set depth` gives one variant its [depth map](file-formats.md#depth-maps) from a picture. In the PNG, **red is the front depth and green the back depth**, each 0-255 voxels; blue and alpha are ignored. The picture must be exactly the variant's size, since nfty resamples nothing. Depth over a clear pixel is dropped, and a map that is all zero clears the variant's depth back to flat paper. Every other variant in the `.igt` is carried across untouched.

Changing and removing, on a `.cbk` or on the loose part it holds. Each checks the result with the same validator as `nfty validate` before it writes, and replaces the file atomically.

| Command | Does |
|---|---|
| `nfty remove ingredient <cbk\|rcp> --id I [--recipe R]` | Drops a layer, its place in the stack, its absent chance and every rule naming it. `--recipe` is required on a `.cbk` and refused on a `.rcp`. |
| `nfty remove variant <cbk\|igt> --id V [--recipe R --ingredient I]` | Drops one variant. The last variant of a layer is refused. |
| `nfty remove recipe <cbk> --id R` | Drops a recipe and its roll weight. |
| `nfty move recipe <cbk> --id R --to N` | Moves a recipe within the listing; `N` is 1-based, as in `remove rule --at`. Presentation only: the same seed cooks the same Set. |
| `nfty set variant <igt\|cbk> --id V --weight W [--recipe R --ingredient I]` | Changes one variant's weight. `W` must be above zero. |
| `nfty set recipe <cbk> --id R --weight W` | Changes a recipe's roll weight. `0` shelves it. |
| `nfty set cookbook <cbk> [--name] [--collection-name] [--symbol] [--description] [--target-supply N\|none]` | Changes the book's name or collection metadata. At least one option is required. |
| `nfty set voxel-ready <cbk> [--dry-run]` | Snaps every partially transparent pixel to solid or clear at 50% alpha, listing each variant it changes. `--dry-run` lists and writes nothing. |

Files resolve by convention: a layer id `aura` looks for `aura.igt`, a variant id `glow` looks for
`glow.png`.

## Worker threads

Every command takes `--threads N` (or `max`; `0` also means all). It sets how many worker threads
heavy work uses: reading and writing archives, cooking, exporting, building models. When it is not
given, the `NFTY_THREADS` environment variable sets it; otherwise nfty uses two thirds of the
machine's logical processors. A count above the machine's runs at the machine's and says so.
The count never changes what is written: the same command gives the same bytes at any thread count.

## Getting help

`nfty --help`, or `nfty <command> --help`, explains any of them.

!!! note "Errors are messages, not stack traces"

    A failed command prints one sentence meant to be read. Add `--verbose` if you want the trace as
    well.
