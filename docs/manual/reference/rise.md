# The Rise page

Where depth is painted and models are exported. For steps, see
[Make a 3D model](../how-to/make-a-3d-model.md); for what depth is, see
[Depth: paper, front and back](../understand/depth.md).

## What it can open on

| Source | Reached from | Rail lists | Saves to |
|---|---|---|---|
| Layer | Ingredient pane **Rise**, or the Ingredient Editor's footer **Rise** | The layer's variants | The layer's archive and its CookBook (**Save depth**) |
| Recipe roll | Recipe pane **Rise**, beside the reroll die | The roll's layers, bottom first | Nowhere: read-only |
| Cooked asset | Set browser **Rise** | The Set's assets | Nowhere: read-only |
| Picture | **Rise a picture...** on the opening screen | The one picture | **Save as .igt...** |
| Sheet | The same, when the picture has a grid | The sheet's cells | **Save as .igt...** |

The Editor's **Rise** is dim while the editor has unsaved edits, because Rise reads the saved picture.
A sealed Set can be risen but not exported.

## Layout

| Part | Holds |
|---|---|
| Rail header | The source's name and kind, a count, and fact chips (seed, canvas, grid origin). |
| Grid row | Loose pictures only. See [Rise a spritesheet](../how-to/rise-a-spritesheet.md). |
| Rail | One row per item, with a depth chip (`paper`, or the thickness) and, where it can be exported, a checkbox. An item that cannot rise shows `2D only`. |
| Toolstrip | Tools, brush size, undo and redo, a legend of the mouse map, and on a Recipe the reroll die. |
| Canvas | The art and its depth. A grid toggle, a grid step and the zoom sit above it. |
| 3D tile | The model, live. Enlarge it, or swap it with the canvas to fill the pane. |
| Readout | For a solid pixel under the pointer: `front 1 · back 0 · total 2`. The line is reserved, so it never moves anything. |
| Rail tabs | **DEPTH** and **EXPORT**. |
| Footer | **Back**, a one-line status of unsaved depth, and the save button. |

## Tools

| Tool | Key | Does |
|---|:--:|---|
| Brush | B | Steps depth along a stroke. |
| Line | L | A straight line at the brush size. |
| Rectangle | R | A filled box; Alt draws it from the center. |
| Same-color fill | G | Steps a region of same-colored pixels. |
| Select | M | Mark a region, then click inside it to raise the whole region. |

Every tool changes **solid pixels only**. A stroke steps each pixel it crosses once, however often it
crosses it.

## Mouse map

| Input | Effect |
|---|---|
| Left button | Front +1 |
| Right button | Back +1 |
| **Shift** held | Subtract 1 instead (floor 0) |
| **Ctrl** held | Constrain: straight stroke, 45 degrees for a line, square for a rectangle |

**Shift never constrains on this page.** The legend on the toolstrip reads `L front · R back · Shift
lowers`.

In the 3D tile, a left-button drag orbits, a right- or middle-button drag pans, and the wheel zooms
about the pointer.

## DEPTH tab

| Section | Does |
|---|---|
| **FILL ALL** | **Front** and **Back** (0 to 255 each) and a button, **Apply to N solid pixels**. Replaces every solid pixel's depth in one undo step. |
| **THICKNESS** | A histogram of how many pixels are 1, 2, 3 ... voxels thick, and the deepest. |
| **2D ONLY** | Items with partly transparent pixels, with counts. Holds **Make voxel-ready...** in its header. |

**Make voxel-ready...** opens a card (*Snap partial transparency?*) of before and after pictures, then
**Make voxel-ready** or **Cancel**. The button is dim, with the reason on its tooltip and on the
section's closing line, when the CookBook is locked, when depth is unsaved, or when a job runs. After a
snap, a line at the foot of the rail reports it and offers **Undo**. See
[Fix a 2D-only book](../how-to/fix-a-2d-only-book.md).

## EXPORT tab

| Section | Choices |
|---|---|
| **FORMAT** | GLB (one file), glTF + bin (JSON beside a binary buffer; two files per model), glTF embedded (one text file, largest). |
| **PIVOT** | Bottom center (feet on the floor), Center, Min corner. |
| **VOXEL SIZE** | World units per pixel, to two decimals. The model's dimensions are shown beside it. |
| **SHAPE** | Batches only: Single file (`.zip`) or Folder. |

A heading names the file or folder that will be written, with what is in it. A single model shows its
exact size (`~` for glTF + bin, whose size depends on the name you pick). A button, **Export model...**
or **Export N models...**, writes it. A line of words under the heading says why nothing can be
exported when that is so.

While an export runs, the page is inert: nothing changes depth, selects another item, rerolls, saves
or leaves, and the picker or progress card is the only thing to act on. An export can be canceled; what
was finished before the stop stays.

## Gates

| Action | Refused when |
|---|---|
| Painting | The item cannot rise, or an export is running. A locked CookBook still paints, as a scratch surface. |
| Save | The source is read-only (Recipe, Set), the CookBook is locked, or nothing changed. |
| Make voxel-ready | Locked, unsaved depth, or a job running. |
| Export | The Set is sealed, or nothing checked can rise. |
| Back | A save or export is running. Leaving with unsaved depth asks first. |
