# File formats

Every nfty file is a **ZIP archive with a `manifest.json` inside**. The custom extension is a renamed
`.zip`, so any unzip tool can open one and read exactly what it holds. There is no database, and
nothing is hidden.

| Extension | Holds | Contains |
|---|---|---|
| `.cbk` | CookBook | `manifest.json` + `recipes/*.rcp` |
| `.rcp` | Recipe | `manifest.json` + `ingredients/*.igt` |
| `.igt` | Ingredient | `manifest.json` + one PNG per variant |
| `.set` | A cooked Set | The whole output folder, packed |
| `.ktn` | Kitchen | `manifest.json` only -- an identity, nothing else |

The nesting mirrors the domain: a CookBook archive literally contains Recipe archives, which contain
Ingredient archives, which contain your PNGs.

## Looking inside one

```bash
cp mybook.cbk mybook.zip
unzip -l mybook.zip
```

## A Kitchen records nothing

A `.ktn` holds only its own name. Which files belong to a Kitchen is worked out by **scanning the
folder it sits in**, every time. Nothing is registered, so nothing can fall out of sync -- move a
file in or out with your file manager and the app agrees immediately.

## Versioning

Every manifest carries a `schemaVersion`. nfty reads its own version and older ones, and **refuses
anything newer** -- a field a build cannot see could change what the file means, so it stops rather
than guessing.

!!! note "Editing a manifest by hand"

    It works, and nothing stops you, but the app validates what it reads and will tell you if you
    have produced something it cannot use. Prefer the [authoring commands](cli.md#authoring) if you
    are generating projects programmatically.
