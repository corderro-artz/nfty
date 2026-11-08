# Reorder the layer stack

Layers paint **bottom to top**. Depth `1` paints first and sits furthest back; the last layer paints
over everything. Change the order to put a layer in front of or behind another.

!!! warning "Reordering makes a different collection"

    Layers consume a random stream in stack order, including Dynamic color draws and skips for
    always-absent layers. A move can change which draws reach other choices. If you have already cooked and
    shipped a Set from this book, the next cook after a reorder is a different collection, not the
    same assets restacked. See [Why a seed reproduces a collection](../concepts/seeds.md).

## Move a layer

1. In the Explorer, unlock editing (click the **read-only** chip). Drag grips appear on the Recipe's
   layer table:

    ![The layer table unlocked, showing drag grips](../images/layers-unlocked-light.png#only-light)
    ![The layer table unlocked, showing drag grips](../images/layers-unlocked-dark.png#only-dark)

2. Do one of:
    - drag a row by its grip in the Recipe's **LAYERS** table,
    - drag the layer's row in the tree, or
    - select the layer and press ++alt+up++ / ++alt+down++.

All three do the same thing and save at once. The keyboard route exists so reordering never depends
on a steady hand.

## Move a Recipe in the listing

Drag a Recipe in the tree, or press ++alt+up++ / ++alt+down++ on it. This only changes the order
Recipes are listed in; the same seed cooks the same Set.

## From the command line

```bash
nfty move ingredient cat.rcp --id aura --to 2     # absolute depth; 1 is the bottom
nfty move ingredient cat.rcp --id aura --up       # one step toward the front
nfty move recipe book.cbk --id fox --to 1         # listing only
```
