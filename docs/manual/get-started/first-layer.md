# Draw your first layer

In this tutorial you add a creature on top of the background from the previous page, and use every
drawing tool in the Ingredient Editor on the way.

**Before you start:** finish [Your first collection in the app](first-collection.md). Open
**Vapor Pets** in the Explorer with editing **unlocked**.

## 1. Add a second layer

1. Select **Cat** in the tree and click **Add ingredient**.
2. Name it `Body` and keep the kind **Dynamic**.

Layers paint **bottom to top in the order you add them**, so Body lands on top of Background. The
Recipe's panel shows the stack in paint order:

![The layer table, numbered in paint order with each layer's kind](../images/layer-stack-light.png#only-light)
![The layer table, numbered in paint order with each layer's kind](../images/layer-stack-dark.png#only-dark)

Select **Body** and click the **pencil** to open the editor.

## 2. Show the background while you draw

Reference layers show the rest of the character behind (or over) the one you are editing, so you
can place your drawing without guessing.

1. On the right-hand rail, open the **REFERENCES** tab.
2. Under **IN THIS RECIPE**, tick **Background**.

![The reference layer list, with layers marked UNDER and OVER](../images/reference-layers-light.png#only-light)
![The reference layer list, with layers marked UNDER and OVER](../images/reference-layers-dark.png#only-dark)

Background now composites behind the canvas, tagged **UNDER**. A layer above this one would be
tagged **OVER** and drawn dimmed so it cannot hide your work.

## 3. Draw a body

The tools run along the top of the canvas:

![The editor toolstrip](../images/toolstrip-light.png#only-light)
![The editor toolstrip](../images/toolstrip-dark.png#only-dark)

1. Pick the **circle** tool (++c++) and a **mid-gray**. Press near the middle of the canvas, drag
   out a rounded body, and release. The shape is drawn live while you drag, exactly as it will be
   committed.
2. Pick the **triangle** tool (++t++) and drag two small ears on top.
3. Pick the **brush** (++b++), set the brush size to `1`, choose a **dark gray**, and dot two eyes.

If a shape lands wrong, press ++ctrl+z++.

## 4. Shade it

Pick a **lighter gray** and draw a smaller circle inside the body, toward the top.

That patch is not a highlight in a fixed color; it is a *brighter value*. When nfty colors the layer
it keeps every pixel's brightness and injects a hue, so your shading survives into every color the
layer rolls. See [Layer kinds and value-maps](../concepts/layer-kinds.md).

## 5. Watch it in color

The small **preview** tile in the bottom-right corner of the canvas shows this layer exactly as
generation renders it, in a color sampled from the layer's range. Its three buttons re-roll the
color, enlarge the tile, and swap it with the canvas. Click the first a few times: same drawing,
different creature each time.

## 6. Add a second option

Click **Add Variant** and draw a different pose, such as a sitting version or bigger ears. It only
needs to be *different*: variety in the output comes from having options to roll between.

Click **Save**, then **Back**.

## 7. Cook again

Select **Vapor Pets**, click **Cook Set**, and use the **same seed** as before, into a new empty
folder.

You get a different collection, not the old one with a creature added. Adding a layer changes how
random numbers are handed out, so every roll changes. See
[Why a seed reproduces a collection](../concepts/seeds.md).

## What you did

You used the shape tools, the brush, the value swatches, reference layers and the live preview. The
Chest Demo is six layers built exactly this way.

## Next steps

- [Your first collection from the command line](first-collection-cli.md): the same workflow, scripted.
- [The Ingredient Editor](../app/ingredient-editor.md): every tool and control in detail.
- [Weights and rarity](../app/weights.md): make one option rarer than another.
