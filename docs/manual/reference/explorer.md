# The Explorer

Where a CookBook is read and edited. A tree on the left, a panel on the right that changes with the
selection.

![The Explorer, with a CookBook selected](../images/explorer-light.png#only-light)
![The Explorer, with a CookBook selected](../images/explorer-dark.png#only-dark)

## The toolbar

| Control | Does |
|---|---|
| **Search** | Filters the tree. ++ctrl+k++ focuses it. |
| **Add ...** | Adds a child of whatever is selected: a Recipe to the book, an Ingredient to a Recipe, a Variant to an Ingredient. Its label follows the selection. |
| **Delete** | Removes the selected item. |
| **Import** | Brings in a loose file. |
| **Padlock** *(far right)* | Toggles the edit lock. |

## The edit lock

A CookBook opens **read-only**. The padlock unlocks adding, deleting and reordering; the chip in the
title bar and the message in the status bar always say which state you are in.

The lock governs *structure*. Opening the Ingredient Editor and painting is not gated by it.

## The tree

CookBook at the root, Recipes under it, Ingredients under those. The letter beside each layer is its
kind: <span class="kind dynamic">D</span>ynamic, <span class="kind static">S</span>tatic,
<span class="kind custom">C</span>ustom.

## The CookBook panel

![The CookBook panel](../images/cookbook-panel-light.png#only-light)
![The CookBook panel](../images/cookbook-panel-dark.png#only-dark)

- **Identity card** -- name, description, symbol, canvas size, colorize model, and validity status.
- **Four counts** -- recipes, layers, variants, and **UNIQUE DNA**.
- **DNA SPACE** -- the arithmetic behind that number, per Recipe, so you can see the bottleneck.
- **MINT DISTRIBUTION** -- the share each Recipe takes of the collection.
- **Reports** -- the same text reports the command line prints.
- **Cook Set** -- opens the [Cook dialog](#the-cook-dialog).

## The Recipe panel

![The Recipe panel](../images/recipe-panel-light.png#only-light)
![The Recipe panel](../images/recipe-panel-dark.png#only-dark)

- **Identity card** -- a rolled preview of this character, the per-layer variant counts multiplied
  out, and the resulting **art combinations**.
- **LAYERS** -- the stack in paint order. `1` paints first and sits furthest back. Unlocked, each row
  gains a drag grip.
- **RULES** -- this Recipe's exclusions.

## The Ingredient panel

![The Ingredient panel](../images/ingredient-panel-light.png#only-light)
![The Ingredient panel](../images/ingredient-panel-dark.png#only-dark)

- **Identity card** -- a preview, the layer's kind and coloring model, and a rule pill if this layer
  appears in one. The **pencil** opens the editor.
- **VARIANT table** -- each option with its raw weight, its share within the Recipe, and its share of
  the whole collection.
- **COLORWAYS** -- the band of colors this layer can take, with the hue and saturation ranges under
  it.
- **Export preview...** -- writes one variant as generation would render it.

## The Cook dialog

![The Cook dialog](../images/cook-dialog-light.png#only-light)
![The Cook dialog](../images/cook-dialog-dark.png#only-dark)

| Field | Means |
|---|---|
| **Count** | How many assets to make. |
| **Seed** | Any word. Pre-filled with a random one. The same book and seed always produce the same collection. |
| **Pack into a single .set** | Also write the output folder as one `.set` archive. |

## The status bar

Validity, lock state, the book's counts, and a line of guidance about whatever you last did. The
message belongs to the screen that said it and is cleared when you move to another.
