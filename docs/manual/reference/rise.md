# The Rise page

Where depth is painted and models are exported. For steps, see
[Make a 3D model](../how-to/make-a-3d-model.md); for what depth is, see
[Depth: paper, front and back](../understand/depth.md).

## What it can open on

| Source | Reached from | Rail lists | Saves to |
|---|---|---|---|
| Layer | Ingredient pane **Rise**, or the Ingredient Editor's footer **Rise** | The layer's variants | The layer's archive and its CookBook (**Save depth**) |
| Recipe roll | Recipe pane **Rise**, beside the reroll die | The roll's layers, bottom first | The variant that owns the pixel: **Save depth** writes every variant you changed (see [Write-through](#write-through-on-a-recipe-roll)) |
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

## Write-through on a recipe roll

On a recipe roll the canvas shows the stack's combined depth, but depth is stored on each layer's own
variant. The page paints **through** the combined picture onto the one variant you choose. For steps,
see [Shape one layer on the whole character](../how-to/shape-one-layer-on-the-whole-character.md).

The page has two states. In **picking**, nothing is chosen and the header above the canvas reads
`click a pixel to choose its layer`. In **targeted**, one layer is chosen: the header reads, for
example, `editing Body / Planked · in ~234 of 1,200 assets`, the layer's row in the rail is marked, and
the other layers are drawn dimmed (30%) on the canvas. The 3D tile keeps showing the whole character.

| Input | In picking | In targeted |
|---|---|---|
| Left-click a pixel | Chooses the layer that owns that pixel's **front** face. Paints nothing. | Paints that layer's front depth +1, when the pixel is on the chosen layer. |
| Right-click a pixel | Chooses the layer that owns that pixel's **back** face. Paints nothing. | Paints that layer's back depth +1, when the pixel is on the chosen layer. |
| A dimmed pixel | Chooses its owner, as above. | Does nothing: a stroke that begins on another layer never begins. |
| Click a layer's row, or **Enter** on it | Chooses that layer. | Chooses that layer instead. |
| **Page Up** / **Page Down** | Steps to the next layer up or down the stack, among the layers this roll has. | The same. |
| **Escape** | Nothing. | Drops the marquee first if there is one, otherwise drops the chosen layer (back to picking). |

**Page Up**, **Page Down** and **Escape** work only while the page has focus and not while you type in
a field. **Escape** yields to an open dialog.

**Who owns a pixel.** Where layers overlap, the front face belongs to the layer that stands furthest
forward there and the back face to the one that reaches furthest back; a tie goes to the upper layer.
So a left-click and a right-click on one pixel can choose two different layers.

**The reach figure** in the header says how much of the collection the chosen variant touches: the
variant's share of assets, from the same rarity numbers the CookBook card uses, times the book's target
supply (`in ~234 of 1,200 assets`), or a percentage of assets when the book has none. A leading `~` marks an estimate; `in every asset` means exactly that. The tooltip says
how it was worked out.

Other rules of the page:

- **Painting works on a locked CookBook**, as a scratch surface. **Save depth** needs the lock opened in
  the Explorer, and the footer says so.
- **Undo and redo are one history** for the whole page, across layers. Undoing a stroke on another
  layer moves the chosen layer to it. A stroke on a variant this roll does not include is restored, and
  a line says so.
- **Reroll keeps what you painted.** Depth belongs to the variant, not the roll: a variant that is in the
  new roll shows it, one that is not keeps it until saved.
- **One save writes every changed variant**, in one write to the CookBook. If the CookBook changed
  underneath, the save is refused rather than overwriting it.
- **A cooked Set is never written.** Its Rise stays read-only.

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
| Save | The source is a cooked Set (always read-only), the CookBook is locked, or nothing changed. |
| Make voxel-ready | Locked, unsaved depth, or a job running. |
| Export | The Set is sealed, or nothing checked can rise. |
| Back | A save or export is running. Leaving with unsaved depth asks first. |
