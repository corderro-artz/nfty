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
(`src/Nfty.Ticket/Model/Schema.cs`) and adding a *new* fixture alongside this one, so the old
version stays covered by whatever compatibility path you write for it.

**Rise, 2026-09-25 is the one deliberate exception to that rule**, not a precedent for the next
one. The author decided to drop v1 outright — `Schema.Oldest` moved from 1 to 2, no v1 archives
kept anywhere, see CLAUDE.md — so the three files below were *replaced* with v2 ones, in place,
rather than a fourth fixture being added beside v1 versions nothing can read any more. From v2
on, the ordinary rule is back in force: the next format change adds a fixture beside these, it
does not rewrite them.

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

`aura`'s **`glow`** variant also carries a depth map (`depth/glow.dep`, `.igt`/schema-2 only): every
solid pixel in the top half is front 2 / back 1, the bottom half is untouched (flat, 0/0) — added
deliberately when the fixtures were regenerated at v2, so `depth/{variantId}.dep` has one real
archive covering it. `spark` carries no depth map at all (nothing painted there → nothing written,
same rule a real `.igt` write follows). See `IngredientArchive.Write`/`DepthMap` for the byte layout.

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

Originally written by `CookBookArchive.Write` / `RecipeArchive.Write` / `IngredientArchive.Write`
at schemaVersion 1, from a throwaway program, on 2026-07-16.

**Regenerated at schemaVersion 2 on 2026-09-25**, by the author's own decision to drop v1 (Rise
step 4 — see CLAUDE.md): a one-shot temporary test read the v1 files with the still-v1-reading
build, added the `glow` depth map described above, and wrote all three back to the same paths
through the current (v2) writers, before `Schema.Oldest` moved to 2 and the temporary test was
deleted. Nothing else about the structure changed. The structure above is the complete
description — `FixtureArchiveTests` asserts every part of it, so the tests double as the spec
for what these files contain.
