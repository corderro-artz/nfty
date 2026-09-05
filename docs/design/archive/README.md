# Design archive

**Everything under this directory is history, not specification.**

These files record what the GUI was *imagined* as before it existed. The app has since been
revamped and redesigned many times, and nearly every improvement came from building a screen,
looking at it, and changing it — not from reading a prototype. Where the app and these files
disagree, **the app is right**, by default and without needing an argument.

Nothing here is a source of truth. Nothing here needs to be matched. A difference between the
running app and a file in this folder is not a defect, is not a divergence, and does not need to be
justified, commented, or logged.

## Why keep them at all

Two reasons, both historical:

1. **They explain decisions that are still in force.** Much of the app's vocabulary was settled
   here — the oxblood accent, the mono/sans pairing, the `idchip` badge shape, the kind colors, the
   `CookBook › Recipe › Ingredient › Variant` chain. Code comments across `Nfty.App` cite these
   files by name when explaining *why* something looks the way it does. Those citations stay
   readable, and stay accurate about the past, as long as the files exist.
2. **They are the record of a design process.** Deleting them would lose the reasoning behind
   choices nobody remembers making.

## Do not edit them

Not because they are authoritative — they are not — but because they are a **dated record**. Editing
one to match today's app destroys the only thing it is still good for. If a file here is wrong about
the present, that is expected and correct; leave it wrong.

## What is in here

### `mockups/`

The seven self-contained HTML screens that preceded the Avalonia GUI, plus `gallery.html` (a
generated index over them) and `build-gallery.py` (which generates it). `mockups/README.md` is the
original description, written when these were the plan, and is preserved unedited — read it as a
statement of intent from that moment, not as a description of the app.

Note the token blocks were never identical across the seven: `explorer.html`, `help.html` and
`ingredient-editor.html` carry the full set, while `landing.html` and the three `wizard-*.html`
carry a trimmed one. That inconsistency is part of the record too.

### `mockups/explorations/`

A different kind of artifact, and worth telling apart from the seven screens above: each of these
is a set of **three variants of one component**, built to be looked at side by side so a choice
could be made. Every one of them has since been decided, and the decision is in the app —
`rail-tab-variants.html` is why the ingredient editor's rail has underline tabs, and
`reorder-control-variants.html` is why the Layers table has the grip it has.

They are spent. They document a choice, not an option.
