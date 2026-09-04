# Test fixtures — real archives on disk

Every other test in this repo builds its `Loaded*` graph in memory, so nothing else would
notice if a change quietly altered what lands on disk. These files are the exception: real
`.cbk`/`.rcp`/`.igt` archives, read back by `FixtureArchiveTests`.

## Don't regenerate them casually

**Their value is that they were written by an older build and still read.** Rewriting them from
the current build defeats half the point — it would launder a format change into the fixture
instead of failing the test that should have caught it.

If a fixture test fails, the question is *"what did I change about the format?"*, not *"how do I
refresh the file?"*. A deliberate format change means bumping `Schema.Current`
(`src/Nfty.Core/Model/Schema.cs`) and adding a *new* fixture alongside this one, so the old
version stays covered by whatever compatibility path you write for it.

## What's here

| File | What it is |
|------|-----------|
| `VaporPets.cbk` | A cookbook: 8×8 canvas, one recipe (`cat`, weight 100), nesting `cat.rcp` |
| `cat.rcp` | The recipe standalone: layer order `bg` → `skin` → `aura`, plus one rule |
| `aura.igt` | The dynamic ingredient standalone |

`VaporPets.cbk` deliberately exercises **all three layer kinds at once**, which no other single
archive does:

- **`bg`** (custom) — full-color, composited as-is. Variants `sunset` (w 70), `grid` (w 30).
- **`skin`** (static) — value-map, one fixed color `hsv:322,83,84`, no RNG. Variant `smooth`.
- **`aura`** (dynamic) — value-map, color rolled per asset from two weighted entries (a
  170–200° hue range at w 70, and fixed `hex:d6249f` at w 30). Variants `glow` (w 60),
  `spark` (w 40).

One incompatibility rule: **`bg=grid` excludes `aura=spark`**.

## These are placeholders, not art

The images are synthetic: solid fills for the custom layer, vertical grayscale gradients for the
value-maps, all 8×8 so the archives stay tiny. They exist to prove the *format* round-trips and
that generation, rules, colorization and determinism work against a real file — not to look like
anything.

**Replacing them with real art is the intent.** When real assets exist, add them as further
fixtures rather than overwriting these (see above). A richer fixture would be worth having for:
a multi-recipe cookbook (this one has a single recipe, so the recipe roll is untested from
disk), a `require` rule (only `exclude` is covered here), and an HSL colorization (this one is
HSV throughout).

## Provenance

Written by `CookBookArchive.Write` / `RecipeArchive.Write` / `IngredientArchive.Write` at
schemaVersion 1, from a throwaway program, on 2026-07-16. The structure above is the complete
description — `FixtureArchiveTests` asserts every part of it, so the tests double as the spec
for what these files contain.
