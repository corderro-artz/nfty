# Use soft edges

The **padlock** at the right of the palette strip is the opacity lock, and it is **on by default**.
While it is on, every pixel you paint is either fully there or fully erased -- nothing in between.

## Turn it off

Click the padlock. nfty explains the trade-off once, and then the **A** (alpha) slider beside it
becomes active. Paint at any opacity you like.

## Why it is locked in the first place

Partly-transparent pixels have no correct answer when art is converted into a 3D voxel model: a
converter has to either drop the pixel or make it solid, and neither is what you drew. The lock is
there so you opt into that knowingly rather than discovering it later.

The consequence is concrete. Save a partly transparent pixel and the book becomes **2D only**: it
still cooks images, but it cannot rise and **Include 3D models** is off in the Cook dialog. The
warning you get on unlocking says so. To undo it, see [Fix a 2D-only book](fix-a-2d-only-book.md).
Why nfty refuses rather than rounds is in [Depth: paper, front and back](../understand/depth.md).

Partial alpha is **legal** -- nfty will cook it, ship it and never complain. It is a decision, not an
error.

!!! note "Erasing is not the same thing"

    Erasing works normally whether the lock is on or off. That is how a layer gets its shape, and
    layers have to show through each other.

## Check a finished project

```bash
nfty inspect yourbook.cbk --voxel
```

Lists every variant carrying partial transparency, and how much of it. Worth running before you hand
a collection to anyone planning to make models out of it.
