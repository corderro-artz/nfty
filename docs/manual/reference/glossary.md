# Glossary

Every word the manual uses with a special meaning, in alphabetical order. The eight core words
(CookBook, Recipe, Ingredient, Variant, Set, Kitchen, Menu and Ticket) are explained together in
[The vocabulary](../concepts/vocabulary.md).

**2D only**
:   Marks a book, Recipe or Ingredient with at least one partly transparent pixel. It cooks images
    normally but cannot rise. Fixed with **Make voxel-ready**. See
    [Fix a 2D-only book](../app/2d-only.md).

**Absent chance**
:   The percent of assets that leave a layer out entirely, set per layer on a Recipe. `0` always
    shows it, `100` never does. See [Leave a layer out sometimes](../app/weights.md#leave-a-layer-out-sometimes).

**Base URI**
:   An export option: the address an uploaded folder lives at (`ipfs://<CID>/`), prefixed to each
    file name in the metadata. Also the contract's `baseURI`, the metadata folder's address. See
    [Upload to IPFS](../publishing/ipfs.md).

**Canvas**
:   The pixel size every drawing in a CookBook shares. Fixed when the book is created and permanent.
    See [Why nfty never resizes your art](../concepts/no-resizing.md).

**Colorization**
:   How a layer gets its color -- a hue and saturation range for Dynamic, one fixed color for Static,
    none at all for Custom.

**Color trait**
:   `"<Layer> Color"`, a published trait naming the color a Dynamic or Static layer rolled -- `Red`,
    `Pale Mint`, `Gray` -- so assets that differ only in hue differ on a marketplace. See
    [Color traits and None](set-contents.md#color-traits-and-none). `stats` predicts their odds from the CookBook.

**Colorways**
:   The band of colors a layer can take, shown on the Ingredient panel.

**Cook**
:   To generate a Set from a CookBook.

**CookBook** (`.cbk`)
:   A whole project: canvas size, collection details, and a set of Recipes with weights.

**Custom**
:   A layer kind: full-color art composited exactly as drawn, never recolored.

**Depth**
:   How far a pixel stands out toward the viewer (front) and away from them (back), in voxels. A layer's
    *depth* is also its position in the stack: depth 1 paints first and sits furthest back. See
    [Depth: paper, front and back](../concepts/depth.md).

**DNA**
:   An asset's fingerprint, hashed from its Recipe, its variant choices, and its quantized colors. No
    two assets in a Set share one.

**Dynamic**
:   A layer kind: a gray value-map whose color is rolled per asset from a range.

**Edit lock**
:   The **read-only** / **editing** chip in the title bar. A CookBook opens locked; adding, deleting,
    reordering and saving need it unlocked. See [The edit lock](../app/explorer.md#the-edit-lock).

**Export**
:   A copy of a cooked Set written for somebody else, with only the parts they should have. See
    [Export a Set](../publishing/export.md).

**Extend**
:   To grow an existing Set, keeping every asset already in it and recomputing rarity. See
    [Add assets to an existing Set](../app/cooking.md#add-assets-to-an-existing-set).

**File associations**
:   On Windows, which file types double-click into nfty or its viewer. Set per user on the Settings
    card's SYSTEM tab. See [Open files with nfty on Windows](../app/file-associations.md).

**Ingredient** (`.igt`)
:   One layer of a Recipe: its kind, its colorization, and its weighted variants.

**Kitchen** (`.ktn`)
:   Your shelf of loose parts -- Ingredients and Recipes that belong to no one CookBook, ready to
    pull into any project. Membership is discovered by scanning the folder, never recorded.

**Layer order**
:   The paint order of a Recipe's Ingredients, bottom to top. Depth 1 paints first and sits furthest
    back.

**Menu** (`.mnu`)
:   A CookBook flattened for runtime: every weight, rule, colorization and all the art, so a program
    can roll and render assets without the `.cbk`. See
    [Menus and tickets](../embedding/menus-and-tickets.md).

**Model**
:   A glTF 3D file (`.glb` or `.gltf`) made by Rise from one picture or one finished asset. A Set can
    carry one per asset in `models/`.

**None**
:   The value an optional layer is published under when an asset's roll left it out. Spelled once, so
    the metadata and the rarity table agree. A variant may not be named `None` on an optional layer.
    See [Color traits and None](set-contents.md#color-traits-and-none).

**Opacity lock**
:   The editor padlock that keeps every painted pixel fully opaque or fully erased.

**Paper**
:   The one voxel every solid pixel always has. A picture with no depth painted is paper everywhere.

**Quantize**
:   How finely a layer's color range is divided into colors nfty treats as different. Finer steps
    mean a larger DNA space.

**Quick Reference**
:   The four-tab sheet behind the **?** button (++ctrl+slash++): Basics, Editor, Rise & 3D, and
    Viewer & runtime, with a search box. Keys are drawn as key caps.

**Rarity**
:   The share of a collection carrying a given trait. A collection-wide fact, recomputed when a Set
    grows.

**Recipe** (`.rcp`)
:   One character type: an ordered stack of layers plus its rules.

**Reference layer**
:   Another layer shown behind or in front of the one you are editing, at its real depth.

**Rise**
:   The page, and the act, of turning finished pixel art into a 3D model by giving each pixel a
    thickness. See [Depth: paper, front and back](../concepts/depth.md).

**Rule**
:   An exclusion: when this variant is picked, that one cannot be.

**Sealed** (`.tin`)
:   A Set encrypted for somebody else, optionally marked view-only. See
    [What sealing does and does not do](../concepts/sealing.md).

**Seed**
:   The word that drives generation. Same book plus same seed produces identical output. See
    [Why a seed reproduces a collection](../concepts/seeds.md).

**Set** (`.set`)
:   A cooked collection: images, metadata, rarity and the seed that made it.

**Spritesheet**
:   Every asset stitched into one image in the Set's own numbering. It carries its own grid inside the
    file. See [Rise a spritesheet](../app/spritesheets.md).

**Static**
:   A layer kind: a gray value-map with one fixed color on every asset.

**Target supply**
:   An optional note on a CookBook of how many assets it is meant to produce. Advisory only; empty
    means "not decided".

**Ticket**
:   One rolled outcome, a few hundred bytes: the Recipe, one variant per layer, each colored layer's
    rolled hue and saturation, and the DNA. It names a selection, not pixels. See
    [Menus and tickets](../embedding/menus-and-tickets.md).

**Unique DNA**
:   How many distinguishable assets a CookBook can produce. A promise, not an estimate. See
    [How uniqueness is decided](../concepts/uniqueness.md).

**Upload bundle**
:   An export without `set.json`: art and marketplace metadata ready to upload, which nfty opens
    read-only to check. See
    [Check the bundle](../publishing/marketplace.md#check-the-bundle-before-you-upload-it).

**Value-map**
:   A grayscale drawing where the gray means *lightness*, to be colored at generation time.

**Variant**
:   One option inside an Ingredient: a drawing, a name and a weight.

**Viewer**
:   The pop-out window that shows a cooked asset, a recipe roll or a gallery of a book's variants in
    2D, 3D or both. See
    [The viewer](../app/viewer.md).

**Voxel**
:   A cube one pixel wide. Rise builds a model from voxel columns: one voxel thick for paper, plus the
    front and back depth you paint.

**Weight**
:   How often something is picked, relative to its siblings. Zero shelves it.
