# The vocabulary

nfty names its parts after a kitchen. Learning these eight words is most of learning the product:
every screen, command and file is one of them.

## The authoring chain

| Word | What it is | File |
|---|---|---|
| **CookBook** | Your whole project: the canvas size, the collection's name and symbol, and its Recipes with their weights. | `.cbk` |
| **Recipe** | One *type* of character: its ordered stack of layers (bottom to top, the paint order) and its rules. A book can hold several. | `.rcp` |
| **Ingredient** | One layer, such as "Background" or "Hat": its kind, its colorization, and its options. | `.igt` |
| **Variant** | One option inside a layer: a single drawing, with a name and a weight. Never a file of its own. | *inside an `.igt`* |

A CookBook holds Recipes, a Recipe stacks Ingredients in order, and an Ingredient holds Variants.
The archives nest the same way: a `.cbk` contains `.rcp` files, which contain `.igt` files, which
contain PNGs.

## What you get out

| Word | What it is | File |
|---|---|---|
| **Set** | A cooked collection: the images, two kinds of metadata per asset, the rarity table and the seed that made it. | folder or `.set` |
| **Sealed export** | A Set encrypted under a passphrase and marked view-only, for sending to a reviewer. | `.tin` |

**Cook** is the verb: cooking a CookBook with a seed produces a Set. Every asset in it is rolled by
picking a Recipe (by Recipe weight), then one Variant per layer (by Variant weight), then a color for
each colored layer.

## Outside the chain

| Word | What it is | File |
|---|---|---|
| **Kitchen** | Your shelf of loose parts: Ingredients and Recipes that belong to no one CookBook, kept in a folder so any project can pull them in. Membership is found by scanning the folder, never recorded. | `.ktn` |
| **Menu** | A CookBook flattened for runtime: every weight, rule, colorization and all the art, so a game or server can roll and render assets with no `.cbk`. | `.mnu` |
| **Ticket** | One rolled outcome, a few hundred bytes: the Recipe, one Variant per layer, each colored layer's rolled hue and saturation, and the DNA. It names a selection, not pixels. | *no file* |

A Kitchen is where parts wait between projects. A Menu and a Ticket are what you hand to your own
program; see [Embedding](../embedding/index.md).

## Two more words you will see everywhere

- **DNA**: an asset's fingerprint, a SHA-256 hash of its Recipe, its Variants and its quantized
  colors. No two assets in a Set share one (unless you allow repeats). See
  [How uniqueness is decided](uniqueness.md).
- **Seed**: the word that drives generation. Same book plus same seed gives byte-identical output.
  See [Why a seed reproduces a collection](seeds.md).

The [glossary](../reference/glossary.md) lists every other term.

!!! tip "Every archive is a zip file"

    Each nfty file is a `.zip` with a different extension and a `manifest.json` inside. Copy one,
    rename it to `.zip`, and any unzip tool shows exactly what it holds. See
    [File formats](../reference/file-formats.md).
