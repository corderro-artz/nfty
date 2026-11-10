# Command reference

Every command and option of the nfty command line, as `nfty <command> --help` defines them. For
task-oriented guides see [The command line](../cli/index.md). This page writes the program as `nfty`;
see [Running it](../cli/index.md#running-it).

```text
nfty [command] [options]
```

## Global options

Every command accepts these.

| Option | Does |
|---|---|
| `--threads <n\|max>` | Worker threads for heavy work. `max` (or `0`) uses every logical processor. Default: two thirds of them; `NFTY_THREADS` sets it when this is absent. A count above the machine's runs at the machine's and says so. The count never changes what is written. |
| `--verbose` | On error, print the full stack trace as well as the message. |
| `-h`, `-?`, `--help` | Show help for the command. |
| `--version` | Print the version (top level only). |

A failed command prints one sentence on stderr and exits `1`.

## Commands at a glance

| Command | Does |
|---|---|
| [`demo`](#demo) | Write the built-in demo CookBook. |
| [`inspect`](#inspect) | Print what any nfty file or Set holds. |
| [`validate`](#validate) | Report every problem in a CookBook, Recipe or Ingredient. |
| [`stats`](#stats) | The odds a CookBook's weights imply, and its unique DNA space. |
| [`preview`](#preview) | Render one variant, or a rolled Recipe, as a PNG. |
| [`model`](#model) | Rise an asset into a 3D model. |
| [`generate`](#generate) | Cook a new Set. |
| [`extend`](#extend) | Grow an existing Set. |
| [`menu`](#menu) | Write a `.mnu` Menu from a CookBook. |
| [`serve`](#serve) | Roll or render one asset from a Menu. |
| [`export`](#export) | Publish a cooked Set: presets, marketplace metadata, upload, seal. |
| [`ipfs status`](#ipfs-status) | Check the IPFS service an upload would use. |
| [`share check`](#share-check) | Check a Set against the community gallery's rules. |
| [`sheet grid`](#sheet-grid) | Read or set the grid a spritesheet carries. |
| [`new`](#new) | Create an `.igt`, `.rcp`, `.cbk` or `.ktn`. |
| [`add`](#add) | Add a variant, layer, Recipe or rule to an archive. |
| [`move`](#move) | Move a layer in its stack, or a Recipe in its listing. |
| [`remove`](#remove) | Remove a rule, layer, variant or Recipe. |
| [`set`](#set) | Change weights, chances, depth, book metadata; snap soft edges. |

Options that belong to another form of a command, or that depend on an option you did not give, are
**refused**, not ignored.

## Reading

### demo

```text
nfty demo [<directory>] [--force]
```

Writes `ChestDemo.cbk` into the folder (default `.`, created if missing). The archive is inside the
program, so this needs nothing else.

| Option | Does |
|---|---|
| `--force` | Overwrite an existing `ChestDemo.cbk`. Without it an existing copy is left untouched. |

### inspect

```text
nfty inspect <file> [--voxel] [--key <source>]
```

`<file>`: a `.cbk`, `.rcp`, `.igt`, `.ktn`, `.mnu`, `.set` or `.tin`, a cooked Set folder, or an upload
bundle folder. Prints the tree with every part's `[id]`, rules with their positions, absent chances and
the book's palette; for a Set, what the run produced (seed, CookBook hash, uniqueness mode, models,
observed trait shares); for a `.tin`, its header, and with a key, its contents.

| Option | Does |
|---|---|
| `--voxel` | Also list every variant with partly transparent pixels. Scans every image. Refused on a Kitchen or a Set. |
| `--key <source>` | Passphrase for a `.tin`. See [sources](#where-a-secret-comes-from). |

### validate

```text
nfty validate <file>
```

`<file>`: a `.cbk`, `.rcp` or `.igt`. Prints `OK — no problems.` and exits `0`, or prints every problem
on stderr and exits `1`. A loose `.rcp` or `.igt` is checked for everything that needs no canvas.

### stats

```text
nfty stats <cookbook>
```

Each Recipe's share, every variant's overall share by Recipe (with `<Layer> Color` and `None` rows),
each optional layer's chance, and the unique DNA space, all predicted from the weights.

### preview

```text
nfty preview <file.igt> --variant <id> [--color <spec>] [--model hsv|hsl] [--out <png>]
nfty preview <file.rcp> [--seed <text>] [--only a,b] [--with <igt>] [--with-variant <id>] [--out <png>]
```

| Option | Form | Does |
|---|---|---|
| `--out <png>` | both | Output path. Default `preview.png`. |
| `--variant <id>` | `.igt` | The variant to render. Required. |
| `--color <spec>` | `.igt` | The [color spec](color-specs.md) for a Dynamic or Static layer. Required for those; omit for Custom. |
| `--model hsv\|hsl` | `.igt` | Preview as if the layer used the other color model. |
| `--seed <text>` | `.rcp` | The roll. Default `nfty`. |
| `--only <ids>` | `.rcp` | Draw only these layers, at their real depths. The whole Recipe is still rolled. |
| `--with <igt>` | `.rcp` | Composite a loose layer on top. Must match the canvas. |
| `--with-variant <id>` | `.rcp` | Which variant of `--with`. Default: its highest-weight one. |

### model

```text
nfty model <input> --out <file> [options]
```

`<input>`: a `.png`, an `.igt` with `--variant`, or a `.rcp` with `--seed`. Every fully opaque pixel
becomes a column of voxels. A partly transparent pixel is refused by name.

| Option | Does |
|---|---|
| `--out <file>` | The model to write. Required. `.glb` for `glb`, `.gltf` for the others. |
| `--variant <id>` | The `.igt` variant to rise (its stored depth is used). Refused for a `.png`. |
| `--seed <text>` | Roll the `.rcp` and rise the whole stack. Required for, and only for, an `.rcp`. |
| `--front <n>` / `--back <n>` | Fill-all depth, 0 to 255, replacing stored depth. Both or neither; not on an `.rcp`. |
| `--format` | `glb` (default), `gltf` (plus a `.bin`), `gltf-embedded`. |
| `--pivot` | `bottom-center` (default), `center`, `min-corner`. |
| `--voxel-size` | World units per pixel, 0.001 to 1000. Default 1. |

## Cooking

### generate

```text
nfty generate <cookbook> --count <n> --out <dir> [options]
```

| Option | Does |
|---|---|
| `--count <n>` | How many assets. Required. |
| `--out <dir>` | Folder to write the Set into. Required. |
| `--seed <text>` | Default `nfty`. Same book and seed give byte-identical output. |
| `--pack` | Also write the folder as one `.set`, inside it. |
| `--recipe <id>` | Roll only this Recipe. |
| `--unlimited` | Allow repeats: no unique-DNA requirement; identity is the token number. Rules still apply. |
| `--max-rerolls <n>` | Per-asset reroll budget. Default 10000. |
| `--models` | Also write `models/`. Refused, with nothing written, for a book with partial alpha. |
| `--model-format` | With `--models`: `glb` (default), `gltf`, `gltf-embedded`. |
| `--pivot`, `--voxel-size` | With `--models`: as for [`model`](#model). |

### extend

```text
nfty extend <cookbook> <set-dir> --to <n> [options]
```

Rolls only the new assets needed to reach `--to`, keeps every existing one, and recomputes rarity
across the whole Set. Warns on stderr when the book's hash differs from the Set's record. Models follow
the Set's recorded `models` block; there are no model options. Refused on an export with renamed files.

| Option | Does |
|---|---|
| `--to <n>` | Target total. Required. |
| `--seed <text>` | Seed for the new rolls. Default `nfty-extend`. |
| `--unlimited` | As for `generate`. |
| `--pack` | Repackage the Set as a `.set`. |
| `--max-rerolls <n>` | As for `generate`. |

## Menus

### menu

```text
nfty menu <cookbook> --out <file.mnu> [--art atlas|loose]
```

| Option | Does |
|---|---|
| `--out` | The Menu to write. Required. |
| `--art` | `atlas` (default): every distinct image in one PNG. `loose`: one PNG per variant. |

### serve

```text
nfty serve <menu> (--seed <text> | --ticket "<wire>") --out <png> [--max-rerolls <n>]
```

| Option | Does |
|---|---|
| `--seed <text>` | Roll a fresh asset, the same roll `generate` makes first for that seed. |
| `--ticket "<wire>"` | Render an already-rolled ticket. Its DNA is checked first. Quote it: it contains `\|`. |
| `--out <png>` | Output PNG. Required. |
| `--max-rerolls <n>` | With `--seed` only. Default 10000. |

Exactly one of `--seed` and `--ticket`. Prints the DNA and Recipe.

## Publishing

### export

```text
nfty export <set> --out <dir> [options]
```

`<set>`: the folder `generate` wrote, or a `.set`. The export is named after the collection.

**Contents and shape**

| Option | Does |
|---|---|
| `-o`, `--out <dir>` | Folder to write into. Required. |
| `--preset <name>` | `marketplace`, `assetpack`, `fullproject` or `sealedcritique`. Every flag below overrides it. |
| `--images` / `--no-images` | `images/`. |
| `--opensea` / `--no-opensea` | `metadata/`, the standard ERC-721 fields. |
| `--nfty` / `--no-nfty` | `nfty/`: per-asset DNA, seed and colors. |
| `--models` / `--no-models` | `models/`. `--models` is refused on a Set without them. |
| `--set-manifest` / `--no-set-manifest` | `set.json`. Without it the export is an upload bundle and cannot be sealed. |
| `--book <cbk>` | Ship this CookBook alongside. Warned about if its hash does not match the Set. |
| `--folder` / `--pack` | Write a folder, or one archive (the default). |
| `--sheet` | Also write `spritesheet.png`, carrying its grid. |
| `--sheet-cols <n>` / `--sheet-rows <n>` | The sheet's grid. Need `--sheet`. |
| `--watermark` | Stamp each exported image with its set number. Never the Set, never models. |
| `--watermark-corner` | `top-left`, `top-right`, `bottom-left`, `bottom-right` (default). Needs `--watermark`. |

**Sealing**

| Option | Does |
|---|---|
| `--seal` | Encrypt and mark view-only. Writes a `.tin`. |
| `--key <source>` | Where the passphrase comes from ([sources](#where-a-secret-comes-from)). Omit to be asked. |
| `--note "<text>"` | A line for the recipient, readable without the passphrase. |

**Marketplace**

| Option | Does |
|---|---|
| `--art-only` | Only `images/` (and `models/`): step one of a by-hand upload. |
| `--image-base-uri <uri>` | Each `image` becomes this plus the file name. Absolute (`ipfs`, `https`, `http`, `ar`), ending in `/`. |
| `--model-base-uri <uri>` | Each glb model's `animation_url`. Same rules. |
| `--token-names padded\|token-id` | `padded` (`0001.json`, default) or `token-id` (`metadata/1`, `images/1.png`). `marketplace` starts at `token-id`. |
| `--start-id 0\|1` | First token id. Default 1. |
| `--external-url <uri>` | Each item's `external_url`: this plus its token id. |
| `--collection` / `--no-collection` | `collection.json` (on for `marketplace`). |
| `--external-link <uri>` | `collection.json`'s `external_link`. |
| `--royalty <percent>` | 0 to 100, two decimals at most. Needs `--fee-recipient`. |
| `--fee-recipient <0x…>` | `0x` and 40 hex digits. |
| `--collection-image <uri>` | `collection.json`'s `image`. Default: the first token's image. |

The `collection.json` options need `--collection`. Base URIs need metadata or `collection.json` to
write into.

**Upload**

| Option | Does |
|---|---|
| `--upload` | Pin `images/` (and `models/`), write metadata with those addresses, pin `metadata/` with `collection.json`, then write the export to `--out`. Prints the CIDs, `baseURI` and `contractURI`. Refused beside `--seal`, `--art-only` and the base URIs. |
| `--api <url>` | Local node RPC address. Default `http://127.0.0.1:5001`. |
| `--pinata` | Upload to Pinata instead of the local node. |
| `--pinata-key <source>` | Where the Pinata JWT comes from ([sources](#where-a-secret-comes-from)). Omit to be asked. |

### ipfs status

```text
nfty ipfs status [--api <url>] [--pinata --pinata-key <source>]
```

Prints the provider and a one-line state (`connected · kubo 0.29.0`, `not reachable`, `key refused`),
and exits `1` when the service cannot be reached.

### share check

```text
nfty share check <file.set> [--json]
```

Runs the check the app's **Share…** card and the gallery's Action run: canvas, asset count, size,
archive safety, sealed or not, and the preview. Prints each problem on its own line and exits `1`, or
prints `ok: <assets> assets · <w> x <h>` and exits `0`. `--json` prints the facts (problems, assets,
canvas, fingerprint, file hash) instead, with the same exit code. Limits are in
[the community gallery](../app/community.md#limits).

### sheet grid

```text
nfty sheet grid <png> [--cols <n> --rows <n> | --cell <WxH> | --clear]
```

With no options, prints the embedded grid (or none) and the one detected from the pixels. `--cols`
with `--rows`, or `--cell` (`64x64`), writes a grid into the file keeping every pixel; `--clear` removes
it.

### Where a secret comes from

`--key` (a passphrase) and `--pinata-key` (a Pinata JWT) never take the secret itself. They name its
source:

| Source | Reads |
|---|---|
| `env:NAME` | An environment variable. Usual for CI. Inherited by child processes; readable from `/proc` by the same user on Linux. |
| `file:PATH` | The first line of a file (trailing newline dropped). The file can be mode 600. |
| `stdin` | One line from standard input. Leaves nothing behind. |
| `prompt` | Asks at the terminal without echo. The default when the option is omitted. |

An unprefixed value is an error: `--key hunter2` is refused rather than putting your secret in the
process list, shell history and CI logs. There is deliberately no `--passphrase`.

```bash
printf '%s' "$PASS" | nfty export ./set --out ./dist --preset sealedcritique --key stdin
```

## Authoring

Every authoring command validates its result and replaces the file atomically. Files resolve by id:
variant `glow` is `glow.png`, layer `aura` is `aura.igt`, Recipe `cat` is `cat.rcp`. See
[Build a project from a script](../cli/build-a-project.md) for the manifest formats.

### new

| Command | Options |
|---|---|
| `nfty new ingredient <out.igt>` | `--manifest <json>` and `--images <dir>` (required); `--keep-profile` keeps a picture's non-sRGB color profile instead of converting. |
| `nfty new recipe <out.rcp>` | `--manifest <json>` and `--ingredients <dir>` (required). |
| `nfty new cookbook <out.cbk>` | `--manifest <json>` and `--recipes <dir>` (required); `--force` writes despite validation problems. |
| `nfty new kitchen <out.ktn>` | `--name <text>` (default: the file name). Its folder becomes the workspace. |

### add

| Command | Options |
|---|---|
| `nfty add variant <igt>` | `--id`, `--weight` (0 or more) and `--image <png>` (required); `--name`; `--keep-profile`. |
| `nfty add ingredient <rcp>` | `--igt <file>` (required); `--index <n>`, the 0-based position (default: the top). |
| `nfty add recipe <cbk>` | `--rcp <file>` and `--weight` (required); `--force`. |
| `nfty add rule <rcp>` | `--type exclude\|require`, `--when layer:variant`, `--then layer:variant` (repeat for more targets). *Exclude*: none of the targets with the trigger. *Require*: all of them. |

### move

| Command | Options |
|---|---|
| `nfty move ingredient <rcp>` | `--id` and one of `--to <depth>` (1 is the bottom; past the top clamps), `--up` (toward the front), `--down`. |
| `nfty move recipe <cbk>` | `--id` and `--to <position>` (1-based). Listing only; the same seed cooks the same Set. |

### remove

| Command | Options |
|---|---|
| `nfty remove rule <rcp>` | `--at <n>`, 1-based as `inspect` lists. Later rules shift down. |
| `nfty remove ingredient <cbk\|rcp>` | `--id`; `--recipe` required on a `.cbk`, refused on a `.rcp`. Also drops its absent chance and every rule naming it. |
| `nfty remove variant <cbk\|igt>` | `--id`; `--recipe` and `--ingredient` on a `.cbk`. A layer's last variant is refused. |
| `nfty remove recipe <cbk>` | `--id`. Also drops its weight. |

### set

| Command | Options |
|---|---|
| `nfty set variant <igt\|cbk>` | `--id`, `--weight` (above zero); `--recipe` and `--ingredient` on a `.cbk`. |
| `nfty set recipe <cbk>` | `--id`, `--weight`. `0` shelves the Recipe. |
| `nfty set chance <rcp>` | `--id`, `--absent <percent>`. `0` always present, `100` never. |
| `nfty set cookbook <cbk>` | At least one of `--name`, `--collection-name`, `--symbol`, `--description`, `--target-supply <n\|none>`. |
| `nfty set depth <igt>` | `--variant`, `--map <png>`: red front, green back, 0 to 255; same size as the variant; all zero clears. |
| `nfty set voxel-ready <cbk>` | Snap partial alpha: 50% or more becomes solid, less becomes clear. `--dry-run` lists and writes nothing. |

## Environment variables

| Variable | Does |
|---|---|
| `NFTY_THREADS` | Thread count (`n` or `max`) when `--threads` is absent. |
| `NFTY_PERF` | `1` enables the built-in timing scopes for embedding code. Never changes output. |
