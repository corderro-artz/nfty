# Rise a spritesheet

A spritesheet is one picture holding several equal cells. The Rise page can cut it into cells so you
can give each its own depth and export them all together. For a picture that is just one picture,
see [Make a 3D model](make-a-3d-model.md).

## Open it

On the opening screen, press **Rise a picture...** and choose the `.png`. nfty decides the grid in
this order, and the rail header says which it used:

| Header says | Meaning |
|---|---|
| `from the file` | The PNG carries an `nfty-grid` record (sheets nfty writes do), so it knows. |
| `detected` | No record. nfty found equal cells separated by transparent gutters. |
| `entered` | You set the grid yourself this session. |

A picture with no grid opens as a single picture, and the rail says so.

**Opening a sheet can rewrite the file.** When nfty detects a grid (the `detected` row), it records it in the PNG
straight away, without asking, so the next open reads `from the file`. Work on a copy if the original must
stay byte-for-byte. If the file is read-only, it is left alone and the grid holds for that session only: the
next open detects it again.

## Correct the grid

When a loose picture is open, the **grid row** sits at the head of the rail:

| Choice | You give |
|---|---|
| **Auto** | Nothing. nfty reads the gutters again. |
| **Columns x rows** | How many columns and rows. |
| **Cell size** | A cell's width and height in pixels. |

Press **Apply**. A problem with the numbers appears on the line beneath and keeps Apply dim: a cell
size that does not divide the picture, or more cells than it holds, is refused with the numbers
named. Auto with no gutters says so.

Automatic detection is a guess: a sprite with an internal gap wider than a cell reads as two cells
until you enter the grid yourself. Entering one column by one row turns a sheet back into a single
picture.

If cells carry depth you have not saved, applying a different grid asks first, because the picture is
cut again and that depth cannot follow. Applying the grid already in force changes nothing.

## The grid is written into the PNG

Applying a grid **records it in the file**, as an `nfty-grid` text record. Reopening the file later,
or handing it to someone, needs no explanation. Other programs ignore the record and show the same
picture. Entering a single cell removes the record.

- The file is replaced by writing a temporary file beside it and swapping, so a failure cannot leave
  half a PNG.
- If the file is **read-only**, nfty keeps the grid for the session and says so; nothing is written.

This rewrites the file you opened, not a copy. Work on a copy of a sheet you cannot afford to touch.

## Keep the depth

Depth on a loose sheet lasts for the session until you press **Save as .igt...**. That writes one
Ingredient: a Custom layer with one variant per cell (`cell-1`, `cell-2` ...), each with its depth
and weight 1. The cells reopen with their depth from that `.igt`.

## Export

The EXPORT tab works as for any batch: tick the cells, choose format and pivot, and export a zip or a
folder. Cells that cannot rise are left out and named afterward.
