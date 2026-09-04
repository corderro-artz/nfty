# Why nfty never resizes your art

Import an image that is not the CookBook's canvas size and nfty refuses it. It does not offer to
scale it, and there is no setting to make it.

## Scaling pixel art destroys it

Pixel art is made of decisions about individual pixels. Every scaling algorithm either blends pixels
together -- which turns crisp edges into mush -- or duplicates them unevenly, which turns a straight
line into a staircase with one step twice as tall as the others.

Neither is your art. Both look like a mistake, and once it is in the collection it is in every asset.

So nfty refuses, and tells you the size it wanted. Re-export at the right size and the result is
exactly what you drew.

## The rule runs all the way through

Nothing in nfty resamples, blurs or anti-aliases at any point:

- **The editor** draws whole pixels. There is no soft brush.
- **Every preview and thumbnail** scales with nearest-neighbour, so a 64x64 drawing shown at 400
  pixels is a grid of crisp squares, not a blurry one.
- **The canvas** shows your art at whatever zoom you like without ever inventing a pixel.
- **The output** is composited pixel for pixel.

If something in nfty looks blurry, it is not nfty. It is either blurry in the source art or it is
partial transparency -- see [Use soft edges](../how-to/soft-edges.md).

## One canvas size per book

Every drawing in a CookBook shares one size, fixed when you create the book and checked on every
import. That is what makes compositing exact: layers line up because they cannot do anything else.

It is also why the canvas size is permanent. Changing it after the fact would mean rescaling every
variant in the project, which is the thing this whole page is about not doing.
