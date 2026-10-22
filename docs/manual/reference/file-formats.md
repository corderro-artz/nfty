# File formats

Every nfty file is a **ZIP archive with a `manifest.json` inside**. The custom extension is a renamed
`.zip`, so any unzip tool can open one and read exactly what it holds. There is no database, and
nothing is hidden.

| Extension | Holds | Contains |
|---|---|---|
| `.cbk` | CookBook | `manifest.json` + `recipes/*.rcp` |
| `.rcp` | Recipe | `manifest.json` + `ingredients/*.igt` |
| `.igt` | Ingredient | `manifest.json` + one PNG per variant + an optional `depth/{variantId}.dep` per variant |
| `.set` | A cooked Set | `set.json` + `images/`, `metadata/`, `nfty/`, and `models/` when cooked with them |
| `.ktn` | Kitchen | `manifest.json` only -- an identity, nothing else |
| `.mnu` | Menu | `manifest.json` + art (one atlas PNG, or one PNG per cell) + an optional `depth/{cellKey}.dep` per cell |
| `.tin` | A sealed export | `seal.json` in the clear + an encrypted `.set` |

The nesting mirrors the domain: a CookBook archive literally contains Recipe archives, which contain
Ingredient archives, which contain your PNGs.

## The one you cannot read with an unzip tool

A `.tin` is still a ZIP, and `seal.json` inside it is plain text -- deliberately, so a recipient can
be told what they are holding. `payload.bin` beside it is a whole `.set` encrypted under a
passphrase, and no tool opens that without the passphrase. It has its own extension rather than
being a `.set` full of ciphertext, because a file whose name says what it is beats one that
announces itself by failing to parse. See
[What sealing does and does not do](../understand/sealing.md).

## Depth maps

A variant that carries relief for the Rise feature stores it as `depth/{variantId}.dep` beside its
PNG (an `.igt`'s own variant id inside the archive), or `depth/{cellKey}.dep` beside a Menu's art (a
`.mnu`'s cell key). The file is raw bytes, not an image: **width × height × 2 bytes**, row-major with
the top row first and, within a row, left to right -- pixel `(x, y)` lives at byte offset
`(y * width + x) * 2`, **front depth then back depth**, each one byte (0-255).

A variant with no `depth/` entry at all is flat paper -- the same as every pixel in the map reading
zero. An all-zero map is therefore never written; a `depth/` entry, when present, always carries at
least one non-zero byte. An entry whose length disagrees with its variant's (or cell's) width and
height is refused on read, naming the variant or cell, rather than silently misread.

From the command line, `nfty set depth` writes a variant's map from a picture (red front, green back); the built-in demo's relief is authored that way.

## A spritesheet's grid

A spritesheet that nfty writes, or that you set the grid of on the Rise page, carries its grid **inside
the PNG**, as a `tEXt` chunk with the keyword `nfty-grid`. The text is semicolon-separated `key=value`
pairs in a fixed order:

```
cols=8;rows=4;cell=64x64;count=29
```

`count` is how many cells, in reading order, hold a picture; the rest are empty. Numbers are positive
integers, written invariant. A reader ignores keys it does not know, but a missing or malformed number,
or a count past `cols * rows`, makes the whole record count as absent. Other programs skip an
ancillary chunk they do not know, so the sheet is an ordinary picture to them. See
[Rise a spritesheet](../how-to/rise-a-spritesheet.md).

## Looking inside one

```bash
cp mybook.cbk mybook.zip
unzip -l mybook.zip
```

## A Kitchen records nothing

A `.ktn` holds only its own name. Which files belong to a Kitchen is worked out by **scanning the
folder it sits in**, every time. Nothing is registered, so nothing can fall out of sync -- move a
file in or out with your file manager and the app agrees immediately.

## The `partialAlpha` flag

Every manifest -- Ingredient, Recipe and CookBook alike -- carries a `partialAlpha` flag. It is a
**listing cache, not authored data**: true when any pixel underneath is neither fully opaque nor
fully transparent (a soft edge), false otherwise. Every writer recomputes it from the actual pixels
before writing, and every reader recomputes it again from what it just decoded -- so a hand-edited
`partialAlpha` in a manifest is silently overwritten rather than trusted. The one deliberate
exception is `ArchivePeek`, which reads manifests only and never decodes a pixel; it hands back
whatever value is stored, on the working assumption that the last write stamped it correctly.

## Versioning

Every manifest carries a `schemaVersion`, checked against a range this build accepts -- **refusing
anything outside that range**, older or newer: a field a build cannot see could change what the file
means, so it stops rather than guessing. The range mechanism remains, but today it reads exactly one
version: 2. Version 1 is no longer supported.

!!! note "Editing a manifest by hand"

    It works, and nothing stops you, but the app validates what it reads and will tell you if you
    have produced something it cannot use. Prefer the [authoring commands](cli.md#authoring) if you
    are generating projects programmatically.
