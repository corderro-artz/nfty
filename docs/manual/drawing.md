# Drawing

The Ingredient Editor has three panes: your **variants** on the left, the **canvas** in the middle,
and **colorize** settings on the right.

## The tools

Along the top: pencil, eraser, flood fill, then rectangle, ellipse, triangle, line and select, then
undo/redo. **Brush size** is the number box at the end of the strip.

Every shape tool works the same way: press where you want one corner, drag, and let go. The outline
follows your cursor while you drag, so you can see what you are about to make before you commit it.
The **line** tool joins where you pressed to where you let go, at the current brush size — the
wandering in between does not count.

## Select and move

**Select** is two gestures in one tool, told apart by where you start the drag:

- Drag on empty canvas to **mark** a rectangle. A dashed marquee shows what is marked.
- Drag from **inside** the marquee to **move** those pixels somewhere else. What they left behind is
  cleared, and the marquee travels with them.

A single click away from the marquee drops it, as does pressing ++esc++ or picking another tool.
A move is a normal edit, so **undo** puts it back.

## The value slider

The black-to-white gradient. On a gray layer this *is* the color you paint — it sets how bright the
pixel is, and therefore how bright it will be after nfty colors it. In color mode it becomes the
brightness of the color you are painting.

## The preview

In the bottom-right corner of the canvas, showing what the layer will actually look like once
colored. Its three small buttons re-roll the sampled color, enlarge it, and let it take over the
canvas.

## Reference layers

At the bottom of the right-hand panel — this is how you line art up. Switch on any other layer in the
Recipe and it composites underneath or on top of what you are drawing, depending on its depth, so you
can see the eyes sitting on the face while you draw them.

Layers above yours are dimmed by default so they cannot hide your work; **True color** shows the real
composite.

## Importing

**Import image…** on the left. It must match the CookBook's canvas size exactly — nfty will not
resize your art. On a gray layer a color image is converted to its lightness, and the app tells you
when that happens.
