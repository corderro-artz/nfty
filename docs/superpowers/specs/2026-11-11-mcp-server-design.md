# nfty MCP server — design

2026-11-11. Status: proposed.

## Purpose

A local Model Context Protocol server, `nfty-mcp`, that lets an agent run the jobs an author would
otherwise script around the CLI: inspect and check a book, roll and preview, cook and extend a Set,
write a Menu, tune numbers toward a target, plan and run an export, and publish. It calls the same
library code as the CLI and the app, so every result is byte-identical to the CLI's.

The server **runs actions on art that already exists. It never makes art**: no painting, depth
sculpting, image import or layer authoring. Structure (which layers a recipe stacks, their order,
which recipes exist) is the author's design act, and the CLI already scripts it, so the server
leaves it out too.

## Shape

- **Local only.** It talks over stdio and opens no port, so it needs no auth. One process belongs
  to one client.
- **Sandboxed to roots.** The `--root <dir>` arguments (repeatable; default: the working directory),
  plus any roots the client declares, are the only folders a tool may read or write. Every path is
  checked after resolving links, and anything outside the roots is refused by name.
- **Never overwrites art or a Set.** Output paths must be new, using the CLI's own collision rules,
  moved into Core. The one in-place writer is `tune_book`, which writes through a temp file and
  then replaces the book, after checking that the file hasn't changed since it was read.
- **stdout belongs to the protocol.** `Console.Out` is pointed at stderr before the host starts, so
  no library line can corrupt a JSON-RPC frame. Secret sources `stdin` and `prompt` are refused,
  because stdin *is* the protocol.
- **Errors in house style.** Every exception reaches the client as `ErrorReport.Format(ex)`
  (product sentences, never a stack trace), marked as a tool error rather than a protocol error,
  so the agent can read it and recover.
- **Long work** (cook, extend, export, upload, write_menu) reports MCP progress and honors
  cancellation. While one writer holds a destination path, a second writer to that path is
  refused, the same one-in-flight rule as the app's reorder flag.
- **Threads.** `--threads n|max` and `NFTY_THREADS` use Core's `Parallelism.TryParse`. Output is
  identical at any count.

## Tools (16)

| Tool | Does | CLI twin | Hints |
|---|---|---|---|
| `inspect` | Report on any archive, Set folder or bundle (optional voxel scan) | `inspect` | read-only |
| `validate` | Problems list for a `.cbk`/`.rcp`/`.igt` | `validate` | read-only |
| `stats` | Collection report: DNA space, recipe and trait odds | `stats` | read-only |
| `set_item` | One cooked asset: metadata, rarity, exact odds when the book is found by hash, image | (app rail) | read-only |
| `preview` | Roll a recipe at a seed, or render one variant; returns the image | `preview` | read-only, writes nothing unless `out` is given |
| `roll` | Roll from a Menu (seed → ticket) or render a ticket; returns ticket and image | `serve` | read-only unless `out` is given |
| `rise_model` | One glTF/GLB model from a variant or a recipe roll | `model` | writes new file |
| `cook` | Generate a Set (optional models) | `generate` | writes new folder |
| `extend_set` | Grow a Set to a target count | `extend` | writes into the Set |
| `write_menu` | `.cbk` → `.mnu` | `menu` | writes new file |
| `tune_book` | Atomic batch of numeric edits (weights, absent chance, target supply, rules), validated, with an optional dry run that returns DNA space before and after | `set …`, `add/remove rule` | destructive (rewrites the book) |
| `export_plan` | The file list and warnings an export would write, without writing anything | `export` (plan) | read-only |
| `export_set` | Write an export (presets, marketplace values, sheet, watermark, seal with a named key source) | `export` | writes new folder or file |
| `share_check` | Community gallery rules check | `share check` | read-only |
| `ipfs_status` | Is the pinning service reachable | `ipfs status` | read-only, open-world |
| `upload_set` | Two-step IPFS drop, then the final export | `export --upload` | open-world |

## Left out, and why

- **Drawing, depth painting, image import, variant/ingredient/recipe creation, layer add/remove/
  move, recipe move.** These make the art or the structure, which is the author's job and not
  something to automate. Reordering layers also changes the collection.
- **`new kitchen`, `sheet grid`, `set voxel-ready`, `demo`.** These are a single flag or a single
  file, and an agent gains nothing over the CLI.
- **`share` (GitHub publish).** It needs the app's GitHub device sign-in and account state. The
  `share_check` tool is the part worth automating.
- **Settings, file associations, viewer, Rise page.** These belong to the app UI.
- **Resources and prompts.** None in v1, to keep it small.

## Distribution

The server is a .NET tool package `Nfty.Mcp` (command `nfty-mcp`), runnable with `dnx Nfty.Mcp`.
It also carries an MCP Registry `server.json`, and its version follows `Directory.Build.props`.
Pushing it to NuGet and the Registry is a manual release step that the author runs.

## Dependency rule

`ModelContextProtocol` (stable 1.x; pin the exact version when it is added) is referenced ONLY by
`Nfty.Mcp`. `Nfty.Core`, `Nfty.Ticket` and `Nfty.Model` stay at zero new packages, and
`AssemblyRulesTests` pins that none of them references `Nfty.Mcp`.
