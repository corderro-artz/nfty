# Import art from another program

Draw in Aseprite, Photoshop, Procreate — anything — and bring the PNG in.

## Import one variant

In the Ingredient Editor, click **Import image…** on the left. It replaces the selected variant's
art.

To bring in several, add a variant for each and import into them one at a time.

## The one rule

**The image must be exactly the CookBook's canvas size.** A 256×256 file will not go into a 512×512
book.

nfty will not resize it for you, and that is deliberate: scaling pixel art destroys it. See
[Why nfty never resizes your art](../understand/no-resizing.md).

Export at the right size instead.

## Color art on a gray layer

If you import a full-color image into a Dynamic or Static layer, nfty converts it to its **lightness**
— because those layers are value-maps, not pictures. The app tells you when this happens.

That is usually what you want for shading. If you meant to keep the colors, the layer needs to be
**Custom** — see [Paint in full color](paint-in-color.md).
