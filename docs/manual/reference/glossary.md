# Glossary

**2D only**
:   Marks a book, Recipe or Ingredient with at least one partly transparent pixel. It cooks images
    normally but cannot rise. Fixed with **Make voxel-ready**.

**Canvas**
:   The pixel size every drawing in a CookBook shares. Fixed when the book is created and permanent.

**CookBook** (`.cbk`)
:   A whole project: canvas size, collection details, and a set of Recipes with weights.

**Colorization**
:   How a layer gets its color -- a hue and saturation range for Dynamic, one fixed color for Static,
    none at all for Custom.

**Colorways**
:   The band of colors a layer can take, shown on the Ingredient panel.

**Cook**
:   To generate a Set from a CookBook.

**Custom**
:   A layer kind: full-color art composited exactly as drawn, never recolored.

**DNA**
:   An asset's fingerprint, hashed from its Recipe, its variant choices, and its quantized colors. No
    two assets in a Set share one.

**Dynamic**
:   A layer kind: a gray value-map whose color is rolled per asset from a range.

**Extend**
:   To grow an existing Set, keeping every asset already in it and recomputing rarity.

**Ingredient** (`.igt`)
:   One layer of a Recipe: its kind, its colorization, and its weighted variants.

**Kitchen** (`.ktn`)
:   Your shelf of loose parts -- Ingredients and Recipes that belong to no one CookBook, ready to
    pull into any project. Membership is discovered by scanning the folder, never recorded.

**Layer order**
:   The paint order of a Recipe's Ingredients, bottom to top. Depth 1 paints first and sits furthest
    back.

**Absent chance**
:   The percent of assets that leave a layer out entirely, set per layer on a Recipe. `0` always
    shows it, `100` never does. See [Leave a layer out sometimes](../how-to/leave-a-layer-out.md).

**Opacity lock**
:   The editor padlock that keeps every painted pixel fully opaque or fully erased.

**Paper**
:   The one voxel every solid pixel always has. A picture with no depth painted is paper everywhere.

**Menu** (`.mnu`)
:   A CookBook flattened for runtime: every weight, rule, colorization and all the art, so a program
    can roll and render assets without the `.cbk`. See
    [Roll assets at runtime](../how-to/roll-assets-at-runtime.md).

**Quantize**
:   How finely a layer's color range is divided into colors nfty treats as different. Finer steps
    mean a larger DNA space.

**Rarity**
:   The share of a collection carrying a given trait. A collection-wide fact, recomputed when a Set
    grows.

**Recipe** (`.rcp`)
:   One character type: an ordered stack of layers plus its rules.

**Reference layer**
:   Another layer shown behind or in front of the one you are editing, at its real depth.

**Rule**
:   An exclusion: when this variant is picked, that one cannot be.

**Rise**
:   The page, and the act, of turning finished pixel art into a 3D model by giving each pixel a
    thickness. See [Depth: paper, front and back](../understand/depth.md).

**Sealed** (`.tin`)
:   A Set encrypted for somebody else, optionally marked view-only. See
    [What sealing does and does not do](../understand/sealing.md).

**Seed**
:   The word that drives generation. Same book plus same seed produces identical output.

**Set** (`.set`)
:   A cooked collection: images, metadata, rarity and the seed that made it.

**Spritesheet**
:   Every asset stitched into one image in the Set's own numbering. It carries its own grid inside the
    file. See [Rise a spritesheet](../how-to/rise-a-spritesheet.md).

**Static**
:   A layer kind: a gray value-map with one fixed color on every asset.

**Target supply**
:   An optional note on a CookBook of how many assets it is meant to produce. Advisory only; empty means "not decided".

**Ticket**
:   One rolled outcome, a few hundred bytes: the Recipe, one variant per layer, each colored layer's
    rolled hue and saturation, and the DNA. It names a selection, not pixels. See
    [Embed nfty in your program](../how-to/embed-nfty.md).

**Unique DNA**
:   How many distinguishable assets a CookBook can produce. A promise, not an estimate.

**Value-map**
:   A grayscale drawing where the gray means *lightness*, to be colored at generation time.

**Variant**
:   One option inside an Ingredient: a drawing, a name and a weight.

**Weight**
:   How often something is picked, relative to its siblings. Zero shelves it.
