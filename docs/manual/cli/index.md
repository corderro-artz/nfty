# The command line

The command line runs the same engine as the app, with no screen: for scripts, CI, servers, and for
checking a project before you cook it. A collection cooked from either is byte-for-byte the same.
Every command and option is listed in the [command reference](../reference/cli.md); this section is
organized by task.

| Task | Page |
|---|---|
| Look inside any nfty file, find problems, see the odds | [Check a project](inspect.md) |
| Create and change CookBooks, Recipes and Ingredients from files | [Build a project from a script](build-a-project.md) |
| Generate a Set, grow it, write models | [Cook, extend and model](cook.md) |
| Rise pictures, set depth, snap soft edges, work with sheet grids | [Models, depth and sheets](models.md) |
| Export, upload and seal | [Publishing](../publishing/index.md) |
| Write a Menu and roll from it | [Menus and tickets](../embedding/menus-and-tickets.md) |

For a guided first run, see [Your first collection from the command line](../get-started/first-collection-cli.md).

## Running it

| You have | Run |
|---|---|
| A Windows download | `cli\Nfty.Cli.exe <command>` |
| A Linux download | `./cli/Nfty.Cli <command>` |
| A source checkout | `dotnet run --project src/Nfty.Cli -- <command>` |

This manual writes `nfty` for any of these; see the alias tip in [Install nfty](../get-started/install.md#check-that-it-works).

```bash
nfty --help                 # every command
nfty generate --help        # one command's options
nfty --version
```

## Conventions every command follows

**Ids, not names.** Options such as `--recipe`, `--variant` and `--id` take the part's **id**
(`[stone]`), not its display name (`Stone`). `nfty inspect` prints both side by side.

**Options that do not apply are refused, not ignored.** `--variant` on a `.rcp`, `--royalty` without
`collection.json`, `--sheet-cols` without `--sheet`: each is an error naming the problem, so a typo
never silently does nothing.

**Colors carry a prefix.** `hex:d6249f`, `rgb:214,36,159`, `hsl:322,72,49`, `hsv:322,83,84`. A missing
or unknown prefix is an error, never a guess. See [Color specs](../reference/color-specs.md).

**Secrets are named by source, never typed.** There is no `--passphrase`. `--key` and `--pinata-key`
take `env:NAME`, `file:PATH`, `stdin` or `prompt`. See
[Where a secret comes from](../reference/cli.md#where-a-secret-comes-from).

**Edits are checked and atomic.** Every command that changes an archive validates the result first and
replaces the file in one step, so a failure never leaves half a file.

**Errors are sentences.** A failed command prints one line meant to be read and exits with code `1`.
Add `--verbose` for the stack trace as well.

**Thread count changes speed, never output.** Every command takes `--threads N` (or `max`); the
`NFTY_THREADS` environment variable sets it when the option is absent. The default is two thirds of
the logical processors.

## In a script or CI job

```bash
set -e
nfty validate book.cbk                       # exit 1 lists every problem
nfty generate book.cbk --count 1000 --seed "$RELEASE" --out out --pack
nfty inspect out                              # what the run produced
NFTY_KEY="$SECRET" nfty export out --out dist --preset sealedcritique --key env:NFTY_KEY
```

`nfty ipfs status` exits `1` when the upload service cannot be reached, so a pipeline can check before
it uploads.
