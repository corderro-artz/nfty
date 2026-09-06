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
| `nfty inspect <file>` | Prints what is inside any `.cbk`, `.rcp`, `.igt`, `.set` or `.ktn`, plus a book's own palette. A cooked Set also works as a **folder** -- which is what `generate --out` writes when you do not pass `--pack`. |
| `nfty inspect <file> --voxel` | Also lists every variant carrying partial transparency. Costs a full scan of every image. Refused on a `.ktn` and on a cooked Set: both list paths without opening them, and a Set is the output rather than the source -- run it on the CookBook that produced it. |
| `nfty validate <file>` | Reports every problem it finds, rather than stopping at the first. |
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
```

Files resolve by convention: a layer id `aura` looks for `aura.igt`, a variant id `glow` looks for
`glow.png`.

## Getting help

`nfty --help`, or `nfty <command> --help`, explains any of them.

!!! note "Errors are messages, not stack traces"

    A failed command prints one sentence meant to be read. Add `--verbose` if you want the trace as
    well.
