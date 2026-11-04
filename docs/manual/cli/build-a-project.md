# Build a project from a script

Every part of a CookBook can be created and changed without the app: from manifests and PNGs, or by
editing an existing archive in place. Use this to generate projects from a pipeline, or to make bulk
changes. For a guided first run, see
[Your first collection from the command line](../get-started/first-collection-cli.md).

## How a project is assembled

Archives nest, and you build them from the inside out:

```text
PNGs + ingredient manifest  ──new ingredient──▶  layer.igt
.igt files + recipe manifest ──new recipe──────▶  type.rcp
.rcp files + book manifest   ──new cookbook────▶  book.cbk
```

Files are found **by id**: a variant `glow` is `<images>/glow.png`, a layer `aura` in `layerOrder` is
`<ingredients>/aura.igt`, a Recipe `cat` in `recipeWeights` is `<recipes>/cat.rcp`.

## The manifests

**Ingredient** (`new ingredient out.igt --manifest m.json --images <dir>`):

```json
{
  "id": "bg",
  "name": "Background",
  "kind": "dynamic",
  "colorization": {
    "model": "hsv",
    "hueQuantize": 30,
    "satQuantize": 25,
    "entries": [
      { "weight": 60, "range": { "hueMin": 15, "hueMax": 45, "satMin": 35, "satMax": 80 }, "fixed": null },
      { "weight": 40, "range": { "hueMin": 190, "hueMax": 250, "satMin": 5, "satMax": 30 }, "fixed": null }
    ]
  },
  "variants": [
    { "id": "plain", "name": "Plain", "weight": 3 },
    { "id": "stripes", "name": "Stripes", "weight": 1 }
  ],
  "schemaVersion": 2
}
```

| Field | Rules |
|---|---|
| `kind` | `dynamic`, `static` or `custom`. |
| `colorization` | `null` for `custom`. Otherwise `model` (`hsv` or `hsl`), the two quantize steps, and `entries`. |
| `entries` | Each has a `weight`, and exactly one of `range` (Dynamic) or `fixed` (a [color spec](../reference/color-specs.md), Static); the other is `null`. A Static layer has one entry. |
| `range` | Hue in degrees 0 to 360, saturation in percent 0 to 100. Half-open: the maximum itself is never rolled. |
| `variants` | Each `id` names `<id>.png`. Weights are relative; `0` shelves a variant. |

**Recipe** (`new recipe out.rcp --manifest m.json --ingredients <dir>`):

```json
{
  "id": "chest",
  "name": "Chest",
  "layerOrder": ["glow", "feet", "body", "bands", "trim", "lock"],
  "rules": [
    {
      "type": "exclude",
      "when": { "ingredientId": "body", "variantId": "stone" },
      "targets": [ { "ingredientId": "lock", "variantId": "keypad" } ]
    }
  ],
  "absentPercent": { "trim": 55, "glow": 72 },
  "schemaVersion": 2
}
```

`layerOrder` is bottom to top. `rules` is required (use `[]`); `type` is `exclude` or `require`.
`absentPercent` is optional.

**CookBook** (`new cookbook out.cbk --manifest m.json --recipes <dir>`):

```json
{
  "id": "chestdemo",
  "name": "Chest Demo",
  "canvas": { "width": 32, "height": 32 },
  "collection": { "name": "Chest Demo", "description": "Layered chests.", "symbol": "CHST" },
  "recipeWeights": { "chest": 65, "strongbox": 35 },
  "targetSupply": 1200,
  "palette": ["hex:8a5a2b", "hex:c9a227"],
  "schemaVersion": 2
}
```

`targetSupply` and `palette` are optional. `new cookbook` validates and refuses a broken book unless
you pass `--force` (it prints the problems as warnings; `generate` still refuses the book).

A kitchen needs no manifest: `nfty new kitchen Studio.ktn --name Studio`.

## Change an archive in place

Each of these validates the result before writing and replaces the file atomically.

| To | Command | Works on |
|---|---|---|
| Add a variant | `add variant x.igt --id spark --weight 30 --image spark.png [--name Spark]` | `.igt` |
| Add a layer | `add ingredient x.rcp --igt aura.igt [--index N]` (0-based; default the top) | `.rcp` |
| Add a Recipe | `add recipe book.cbk --rcp cat.rcp --weight 60` | `.cbk` |
| Add a rule | `add rule x.rcp --type exclude --when bg:night --then glow:sparks [--then …]` | `.rcp` |
| Remove a rule | `remove rule x.rcp --at 2` (1-based, as `inspect` lists) | `.rcp` |
| Remove a layer | `remove ingredient book.cbk --recipe cat --id aura` (also drops its absent chance and every rule naming it) | `.cbk` or `.rcp` |
| Remove a variant | `remove variant book.cbk --recipe cat --ingredient aura --id spark` (not a layer's last) | `.cbk` or `.igt` |
| Remove a Recipe | `remove recipe book.cbk --id fox` | `.cbk` |
| Move a layer | `move ingredient x.rcp --id aura --to 2` or `--up` / `--down` | `.rcp` |
| Move a Recipe in the listing | `move recipe book.cbk --id fox --to 1` | `.cbk` |
| Change a variant weight | `set variant book.cbk --recipe cat --ingredient aura --id spark --weight 5` (above zero) | `.cbk` or `.igt` |
| Change a Recipe weight | `set recipe book.cbk --id fox --weight 0` (`0` shelves it) | `.cbk` |
| Set an absent chance | `set chance x.rcp --id hat --absent 90` | `.rcp` |
| Change names and supply | `set cookbook book.cbk --name … --collection-name … --symbol … --description … --target-supply 1200\|none` | `.cbk` |
| Set depth | `set depth x.igt --variant stone --map stone-depth.png` | `.igt` |
| Snap soft edges | `set voxel-ready book.cbk [--dry-run]` | `.cbk` |

On a `.cbk`, commands that reach inside a Recipe or Ingredient need `--recipe` (and `--ingredient`);
on the loose part they are refused. Rules, absent chances and layer moves work on loose `.rcp` files,
so edit those and re-assemble the book, or make those changes in the app.

## Import pictures with a color profile

`new ingredient` and `add variant` convert a picture with a non-sRGB embedded profile (Display P3,
Adobe RGB) to sRGB and print one line saying so. `--keep-profile` imports the pixels as stored. A
profile nfty cannot convert is named on stderr and the picture imported as is.

## Next

[Cook, extend and model](cook.md) · [Command reference](../reference/cli.md#authoring)
