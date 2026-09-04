# Color mode

The palette strip under the tools has a **Gray / Color** switch.

**Gray** is the normal mode for Dynamic and Static layers: ten shades of gray, plus the value slider
for anything in between.

**Color** paints in full color. The ten slots become a rainbow, and the right-hand panel turns into
**Paint Hue** and **Paint Saturation** — those two plus the brightness slider give you any color.

Switching to color mode brings your existing drawing with you, in gray, so you can paint over it.

## Saving color art

**Color art can only be saved as a Custom layer**, because a Custom layer is the only kind that keeps
the colors you drew. So when you save a gray layer that you painted in color, nfty asks:

- **Save as new** *(the default)* — makes a new Custom layer on top of the Recipe and leaves the
  original gray layer exactly as it was.
- **Overwrite** — converts the original.

!!! warning "Overwrite discards the color settings"

    Converting a Dynamic or Static layer to Custom throws away its hue and saturation ranges and its
    quantize steps. They cannot be recovered.

## Keeping colors you like

The **+** button on the palette strip saves the current color. Saved colors persist between sessions.
Right-click one to forget it.

A CookBook can also carry its own palette, which travels inside the file — those appear first and
cannot be deleted from the editor.
