# Fix a 2D-only book

A book is **2D only** when some variant has a partly transparent pixel (a soft edge). A voxel is solid
or empty, so that variant cannot rise and the book cannot cook models. Nothing else is wrong: it still
cooks images, and partial transparency is legal there. This page takes a book from 2D only to
voxel-ready. For why nfty does not round for you, see
[Depth](../concepts/depth.md#why-2d-only-is-derived).

## Find the cause

- **Explorer tree:** a small flat-sheet mark trails the name of the book and of any affected
  Ingredient. Hover it for the reason.
- **Kitchen shelf:** the same mark in a card's corner.
- **Rise page:** the **2D ONLY** list on the DEPTH tab names each affected variant and counts its
  partial pixels.
- **Terminal:** `nfty inspect mybook.cbk --voxel`.

## Make it voxel-ready

1. Unlock editing in the Explorer, and save any depth you have painted.
2. Open the Rise page on any layer of the book (the Ingredient's **Rise** button).
3. On the DEPTH tab, press **Make voxel-ready…** in the **2D ONLY** header.
4. The card *Snap partial transparency?* lists every variant that would change, with before and after
   pictures and how many pixels turn solid or clear. Read it.
5. Press **Make voxel-ready**, or **Cancel** to change nothing.

The rule: a pixel at **50% alpha or more** becomes fully solid; **below 50%** it becomes fully clear.
Depth is untouched, and a pixel that turns solid rises as paper.

A picture opened with **Rise an asset…** is snapped for the session only, until you **Save as
.igt…**; leaving before that asks first.

### When the button is dim

Its tooltip, and the closing line of the 2D ONLY section, give the reason:

- the CookBook is **locked**,
- you have **unsaved depth** (save first, so the snap cannot be mistaken for your painting), or
- a save, export or snap is **already running**.

## Undo it

Right after a snap, a line at the foot of the rail offers **Undo**, which restores the original pixels
exactly. It lasts until you leave the Rise page.

## From the command line

```bash
nfty set voxel-ready mybook.cbk --dry-run    # list what would change
nfty set voxel-ready mybook.cbk
```

## Stop it happening again

Keep the padlock in the Ingredient Editor's palette strip on. Turning it off warns that saving a partly
transparent pixel makes the book 2D only. See
[Paint in color and use transparency](color-and-transparency.md#use-soft-edges-partial-transparency).
