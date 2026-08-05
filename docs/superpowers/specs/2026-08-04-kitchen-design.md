# Kitchen — design

The sixth domain word, and the last unbuilt one. Reserved by
`2026-07-19-nfty-creation-flows-design.md` §7 and assumed throughout the locked mockups; this
settles its shape and records why.

## What the earlier decisions already fix

Three things were settled before this task and are not reopened here:

1. **The Kitchen *is* the loose-items folder.** §7: "Rather than two competing containers, the
   'project folder for temp/loose items' and the Kitchen workspace are the same thing — settled by
   the user to avoid a second concept." So a Kitchen is not a new place; it is a name for the place
   loose `.rcp`/`.igt` files already go.
2. **A top-level workspace with a 3-letter extension, holding CookBooks and loose items, like a VS
   Code workspace.**
3. **It is not a breadcrumb segment.** It is the persistent `.kroot` chip in the titlebar, changing
   only when you close one Kitchen and open another (`explorer.html`: *"persistent workspace root —
   the open Kitchen. Fixed for every item below it"*).

## The one real ambiguity, and how it is resolved

"A 3-letter extension" says *file*. "A folder, created if absent" and "like a VS Code workspace" say
*directory*. Both are in the spec.

VS Code settles it: a `.code-workspace` is a small **file** that gives an identity to **folders** it
points at. Applied here:

> **A Kitchen is a `.ktn` file that names the folder it sits in. The folder is the workspace; the
> file is its identity.**

Opening a `.ktn` opens its containing directory as the workspace. Loose items are written beside it.
CookBooks in the same directory are its CookBooks.

## Membership is discovered, not recorded

The manifest holds **identity only** — name, description. It does **not** list its members.

This is the decision most likely to be second-guessed, so: a recorded list goes stale the moment
anyone renames, moves or deletes a file outside the app, and then the Kitchen is lying about itself
in exactly the way this codebase keeps finding elsewhere. Scanning the directory means the filesystem
is the single source of truth, moving a file in or out Just Works, and there is no index to
reconcile. It is also what the VS Code analogy actually does.

The cost is that a very large directory is scanned on open. That is bounded by directory size, not
collection size, and a Kitchen is a working folder rather than an archive.

## Why `.ktn` is a ZIP like every other archive

Every other nfty file is "a ZIP with a `manifest.json`" (CLAUDE.md). A Kitchen holds no images, so a
plain JSON file would work — but making it an archive costs one entry and buys consistency:
`ArchiveIo` handles it, `ISchemaVersioned` and the version gate apply unchanged, and
`Archives.KindOf` gains one more case rather than one more concept. A future Kitchen-level asset
(a cover image, a palette) then has somewhere to live without another format decision.

## Shape

```
Model/KitchenManifest.cs      record KitchenManifest(Id, Name, Description, SchemaVersion)
Formats/KitchenArchive.cs     Read/Write (+ async), same shape as the other three
Formats/Kitchen.cs            Open(path) → KitchenContents: the manifest + what the folder holds
```

`KitchenContents` reports CookBooks, loose Recipes and loose Ingredients as **paths**, not loaded
graphs. Loading every archive in a folder to list it would pull every PNG in the workspace into
memory — the opposite of what a workspace listing is for. Callers open what the user picks.

## Schema

`KitchenManifest` is `schemaVersion` 1 like the rest. `Schema.Current` does **not** move: this adds a
new archive type, it does not change an existing one, so nothing already written becomes unreadable
and no existing reader needs to care.

## Deliberately out of scope

- **Nested Kitchens.** A workspace inside a workspace has no use here and doubles every path rule.
- **Moving items between Kitchens.** That is a file operation; the OS already does it, and the
  scan picks it up.
- **A Kitchen-level cook.** Cooking is a CookBook operation; a Kitchen is a place, not a collection.
