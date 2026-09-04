# Command line

Everything the app does, the command line does too. Useful for scripting, for checking a project
before you cook it, and for the one job the app does not offer at all -- [extending a
Set](../how-to/more-assets.md).

```bash
dotnet run --project src/Nfty.Cli -- <command> [args]
```

The examples below write `nfty` for brevity.

## Reading

| Command | Does |
|---|---|
| `nfty inspect <file>` | Prints what is inside any `.cbk`, `.rcp`, `.igt`, `.set` or `.ktn`, plus a book's own palette. |
| `nfty inspect <file> --voxel` | Also lists every variant carrying partial transparency. Costs a full scan of every image. Refused on a `.ktn`. |
| `nfty validate <file>` | Reports every problem it finds, rather than stopping at the first. |
| `nfty stats <cbk>` | The odds the weights imply, trait by trait, plus the unique DNA space. |

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
| `--pack` | Also package the folder as a single `.set`. |
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
