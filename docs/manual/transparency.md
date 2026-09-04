# Transparency and the opacity lock

The **padlock** at the right of the palette strip is on by default. While it is on, every pixel you
paint is either **fully there** or **fully erased** — nothing in between.

This is deliberate. Partly-transparent pixels have no clear answer when art is converted into a 3D
voxel model: a converter has to either drop the pixel or make it solid, and either way the result is
not what you drew.

Erasing still works normally — that is how a layer gets its shape, and layers have to show through
each other.

If you do want soft edges, click the padlock. nfty explains the trade-off once, and then the **A**
(alpha) slider beside it becomes active. Use it knowingly.

!!! tip "Checking a finished project"

    ```bash
    nfty inspect yourbook.cbk --voxel
    ```

    Lists every variant carrying partial transparency, and how much.
