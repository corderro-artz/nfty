# Your first collection from the command line

In this tutorial you build a small CookBook entirely from a terminal: three layers, one Recipe, an
optional layer and a rule. Then you check it, preview it, cook twenty assets, grow the Set to thirty
and export it. Everything here also runs unattended in a script or CI job.

**Before you start:**

- [Install nfty](install.md) and make `nfty` run the command line (see the tip on that page).
- Have five small PNGs, all **16×16** pixels, in this layout. Any drawing program works; flat
  shapes are fine.

```text
pets/
  art/
    bg/      plain.png  stripes.png      gray fills: they will be colored per asset
    body/    round.png  big.png          gray shapes on a transparent ground
    hat/     cap.png                     a full-color hat on a transparent ground
  man/                                   you will write the manifests here
```

Every file name is the **id** nfty uses for that variant, so `plain.png` becomes variant `plain`.

## 1. Describe each layer

Each layer is an Ingredient built from a JSON manifest plus a folder of PNGs. Create these three
files in `pets/man/`.

`bg.json`: a **Dynamic** layer whose color is rolled per asset from any hue, at medium saturation.

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
      { "weight": 1, "range": { "hueMin": 0, "hueMax": 360, "satMin": 30, "satMax": 80 }, "fixed": null }
    ]
  },
  "variants": [
    { "id": "plain", "name": "Plain", "weight": 3 },
    { "id": "stripes", "name": "Stripes", "weight": 1 }
  ],
  "schemaVersion": 2
}
```

`body.json`: a **Static** layer, one fixed color on every asset.

```json
{
  "id": "body",
  "name": "Body",
  "kind": "static",
  "colorization": {
    "model": "hsv",
    "hueQuantize": 30,
    "satQuantize": 20,
    "entries": [ { "weight": 1, "range": null, "fixed": "hex:d6249f" } ]
  },
  "variants": [
    { "id": "round", "name": "Round", "weight": 1 },
    { "id": "big", "name": "Big", "weight": 1 }
  ],
  "schemaVersion": 2
}
```

`hat.json`: a **Custom** layer, composited exactly as drawn. Custom layers must have
`"colorization": null`.

```json
{
  "id": "hat",
  "name": "Hat",
  "kind": "custom",
  "colorization": null,
  "variants": [ { "id": "cap", "name": "Cap", "weight": 1 } ],
  "schemaVersion": 2
}
```

Every colorization entry states both `range` and `fixed`, with one of them `null`. Hue is in degrees
(0 to 360), saturation in percent (0 to 100). The quantize steps decide how finely colors count as
different; see [How uniqueness is decided](../concepts/uniqueness.md).

## 2. Build the layers

From the `pets` folder:

```bash
mkdir igt rcp
nfty new ingredient igt/bg.igt   --manifest man/bg.json   --images art/bg
nfty new ingredient igt/body.igt --manifest man/body.json --images art/body
nfty new ingredient igt/hat.igt  --manifest man/hat.json  --images art/hat
```

```text
Wrote igt/bg.igt (2 variants)
Wrote igt/body.igt (2 variants)
Wrote igt/hat.igt (1 variants)
```

## 3. Stack them into a Recipe

`man/pet.json` lists the layers bottom to top. `absentPercent` makes the hat appear on only one
asset in five:

```json
{
  "id": "pet",
  "name": "Pet",
  "layerOrder": ["bg", "body", "hat"],
  "rules": [],
  "absentPercent": { "hat": 80 },
  "schemaVersion": 2
}
```

```bash
nfty new recipe rcp/pet.rcp --manifest man/pet.json --ingredients igt
nfty add rule rcp/pet.rcp --type exclude --when bg:stripes --then body:big
```

```text
Wrote rcp/pet.rcp (3 ingredients)
Added rule 1 to rcp/pet.rcp: bg:stripes never with body:big
```

The rule says a striped background never appears with the big body. `--when` and `--then` take
`layer:variant` ids.

## 4. Assemble the CookBook

`man/book.json` fixes the canvas, names the collection and weights each Recipe:

```json
{
  "id": "pets",
  "name": "Pets",
  "canvas": { "width": 16, "height": 16 },
  "collection": { "name": "Pets", "description": "A tiny test collection.", "symbol": "PET" },
  "recipeWeights": { "pet": 1 },
  "schemaVersion": 2
}
```

```bash
nfty new cookbook pets.cbk --manifest man/book.json --recipes rcp
```

`new cookbook` validates the result and refuses to write a broken book unless you pass `--force`.

## 5. Check it

```bash
nfty validate pets.cbk     # every problem, or "OK — no problems."
nfty inspect  pets.cbk     # the tree, with the [id] of every part
nfty stats    pets.cbk     # the odds the weights imply, and the unique DNA space
```

`stats` ends with the unique DNA figure, the most distinct assets this book can cook:

```text
Optional layers:
  Pet          Hat            absent  80.00%  present  20.00%
Unique DNA space: 216
```

## 6. Preview before cooking

```bash
nfty preview rcp/pet.rcp --seed alpha --out pet.png
nfty preview igt/bg.igt --variant stripes --color hsv:200,70,90 --out stripes.png
```

The first rolls the whole stack at a seed; the second renders one variant in a color you name.
Colors always carry a prefix (`hex:`, `rgb:`, `hsl:`, `hsv:`); see
[Color specs](../reference/color-specs.md).

## 7. Cook

```bash
nfty generate pets.cbk --count 20 --seed launch --out out/launch
nfty inspect out/launch
```

`inspect` on a Set reports what the run actually produced: its seed, the CookBook hash, and the
observed share of every trait.

## 8. Grow it

```bash
nfty extend pets.cbk out/launch --to 30
```

```text
Extended by 10 → 30 total.
```

The first twenty assets are untouched; ten new, non-colliding ones are added, and rarity is
recomputed across all thirty.

## 9. Export it

```bash
nfty export out/launch --out dist --preset assetpack
```

```text
Pets → dist/Pets.set
  images/  30 files
  metadata/  30 files
  nfty/  30 files
  set.json
```

The warning about relative image links is expected: an asset pack is not uploaded anywhere. For a
marketplace, see [Publishing](../publishing/index.md).

## What you did

You built three layers of three different kinds, a Recipe with an optional layer and a rule, and a
CookBook, all from text files; then validated, previewed, cooked, extended and exported it.

## Next steps

- [Build a project from a script](../cli/build-a-project.md): every authoring command, and how to
  edit a book in place.
- [Cook, extend and model](../cli/cook.md): the cooking options.
- [Command reference](../reference/cli.md): every command and option.
