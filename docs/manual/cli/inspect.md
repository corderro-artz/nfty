# Check a project

Four commands read a project without changing it: `inspect` shows what a file holds, `validate` lists
every problem, `stats` shows the odds the weights imply, and `preview` renders a layer or a roll.

## See what a file holds

```bash
nfty inspect book.cbk
```

```text
CookBook: Pets (16x16)
  Collection: Pets (PET)
  Description: A tiny test collection.
  Target supply: (none)
  Recipe: Pet [pet] (weight=1)
    Ingredient: Background [bg] (Dynamic)
      Variant: Plain [plain] (w=3)
      Variant: Stripes [stripes] (w=1)
    Ingredient: Hat [hat] (Custom)  absent 80%
      Variant: Cap [cap] (w=1)
```

The `[ids]` are what every other command's `--recipe`, `--ingredient`, `--variant` and `--id` expect.
Rules are listed with the position `remove rule --at` uses.

`inspect` reads every kind of nfty file:

| Given | Prints |
|---|---|
| `.cbk`, `.rcp`, `.igt` | The tree, ids, rules, absent chances, the book's palette. |
| `.ktn` | What the Kitchen folder holds. |
| `.mnu` | Its recipes, layers and art layout. |
| A Set (`.set` or the folder `generate` wrote) | What that run actually produced: seed, CookBook hash, uniqueness mode, models, and the observed share of every trait. |
| `.tin` | Without a key: the collection, asset count and sender's note. With `--key <source>`: the full report. |
| An upload bundle folder | Item count, pictures found, `collection.json`, every trait. |

Add `--voxel` to also list every variant with partly transparent pixels (a full scan of every image;
not available on a Kitchen or a Set, which list paths without opening them).

## Find every problem

```bash
nfty validate book.cbk
```

Prints `OK — no problems.` (exit code `0`), or **every** problem, one per line on stderr (id
collisions, rule conflicts, canvas mismatches, duplicate layer names), and exits `1`. A loose
`.rcp` or `.igt` is checked for everything that needs no canvas. `generate` runs the same check itself,
so `validate` is for seeing what is wrong, not a required step.

## See the odds

```bash
nfty stats book.cbk
```

Prints each Recipe's share, every variant's overall share (with color traits and `None` rows), each
optional layer's chance, and the **unique DNA space**: how many distinct assets the book can produce.
These are what the weights **predict**; `inspect` on a cooked Set shows what a run **produced**, laid
out the same way so you can compare.

## Render before cooking

```bash
nfty preview aura.igt --variant glow --color hsv:200,70,90 --out one.png
nfty preview cat.rcp --seed alpha --out stack.png
nfty preview cat.rcp --seed alpha --only body,eyes --out parts.png
nfty preview cat.rcp --seed alpha --with hat.igt --out with-hat.png
```

- An `.igt` renders **one variant**, exactly as generation would. Dynamic and Static layers need
  `--color`; Custom layers take none.
- A `.rcp` **rolls the whole stack** at a seed and prints what it picked. `--only` draws a subset at
  their real depths; `--with` composites a loose layer on top to see it against the Recipe it will
  join (`--with-variant` picks which variant).

## Next

[Build a project from a script](build-a-project.md) · [Cook, extend and model](cook.md)
