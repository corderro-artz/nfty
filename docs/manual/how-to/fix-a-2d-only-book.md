# Fix a 2D-only book

A book is **2D only** when some variant has a partly transparent pixel (a soft edge). A voxel is
solid or empty, so such a variant cannot rise, and a book containing one cannot cook models. Nothing
else about it is wrong: it still cooks images, and partial transparency is legal there.

This page takes you from 2D only to voxel-ready. To see why nfty does not simply round the pixel
for you, read [Depth: paper, front and back](../understand/depth.md#why-2d-only-is-derived).

## See where it is

- In the Explorer tree, a small flat-sheet mark trails the name of any book, Recipe or Ingredient
  that is 2D only. Hover it for the reason.
- On the Kitchen shelf, the same mark sits in the corner of a card.
- On the Rise page, the **2D ONLY** list on the DEPTH tab names each affected variant and counts its
  partial pixels.
- From the terminal: `nfty inspect mybook.cbk --voxel`.

## Make it voxel-ready

1. Open the Rise page on any layer of the book (the Ingredient's **Rise** button).
2. On the DEPTH tab, press **Make voxel-ready...** in the **2D ONLY** header.
3. A card lists every variant that would change, with a before and an after picture and how many
   pixels turn solid and how many turn clear. Read it.
4. Press **Make voxel-ready** to apply, or **Cancel** to change nothing.

The rule is one sentence: a pixel at **50% alpha or more** becomes fully solid, **below** that it
becomes fully clear. Depth is untouched, and a pixel that turns solid rises at paper.

The snap is written into the book (or the loose Ingredient) the way a depth save is. A picture opened
with **Rise a picture...** is snapped for the session only; it has no file to write until you
**Save as .igt...**

### When the button is dim

The button is gated. Hover it, or read the closing line of the 2D ONLY section, for which reason
applies:

- The CookBook is **locked**. Unlock it in the Explorer.
- You have **unsaved depth**. Save it first, so the snap cannot be mistaken for your painting.
- A save, export or snap is **already running**.

## Undo it

Right after a snap, a line at the foot of the rail says what changed and offers **Undo**. It restores
the original pixels exactly. It lasts until you leave the Rise page; after that, the original art is
no longer in the book.

## Stop it happening again

The padlock in the Ingredient Editor's palette strip keeps painting fully opaque. Turning it off
asks you to confirm, and says plainly that saving a partly transparent pixel makes the book 2D only.
See [Use soft edges](soft-edges.md).
