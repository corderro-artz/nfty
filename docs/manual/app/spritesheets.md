# Rise a spritesheet

A spritesheet is one picture holding several equal cells. The Rise page can cut it into cells so you
can give each its own depth and export them together. For a single picture, see
[Rise: make 3D models](rise.md).

## Open the sheet

On the opening screen, press **Rise an asset…** and choose the `.png`. nfty decides the grid in this
order, and the rail header says which it used:

| Header says | Meaning |
|---|---|
| `from the file` | The PNG carries an `nfty-grid` record (sheets nfty exports do). |
| `detected` | No record; nfty found equal cells separated by transparent gutters. |
| `entered` | You set the grid yourself this session. |

A picture with no grid opens as a single picture, and the rail says so.

!!! warning "Opening or applying a grid rewrites the file"

    When nfty detects or you apply a grid, it records it inside the PNG straight away, so the next open
    reads `from the file`. Pixels are untouched, and the write goes to a temporary file that replaces
    the original, so a failure never leaves half a PNG. Work on a copy if the original must stay byte
    for byte. A read-only file is left alone and keeps the grid for the session only.

## Correct the grid

For a loose picture, the **grid row** sits at the head of the rail:

| Choice | You give |
|---|---|
| **Auto** | Nothing. nfty reads the gutters again. |
| **Columns x rows** | How many columns and rows. |
| **Cell size** | A cell's width and height in pixels. |

Press **Apply**. A size that does not divide the picture, or more cells than it holds, is refused on
the line beneath, with the numbers. Detection is a guess: a sprite with an internal gap wider than a
cell reads as two cells until you enter the grid. One column by one row turns a sheet back into a
single picture and removes the record.

If cells carry unsaved depth, applying a different grid asks first, because that depth cannot follow
a new cut.

## Keep the depth

Depth on a loose sheet lasts for the session until you press **Save as .igt…**. That writes one Custom
Ingredient with one variant per cell (`cell-1`, `cell-2` …), each with its depth and weight 1.

## Export

The EXPORT tab works as for any batch: tick the cells, choose format and pivot, and export a `.zip` or
a folder. Cells that cannot rise are left out and named afterward.

## From the command line

```bash
nfty sheet grid sheet.png                  # the embedded grid (or none) and the detected one
nfty sheet grid sheet.png --cols 8 --rows 4
nfty sheet grid sheet.png --cell 64x64
nfty sheet grid sheet.png --clear
```

The record's format is in [File formats](../reference/file-formats.md#a-spritesheets-grid).
