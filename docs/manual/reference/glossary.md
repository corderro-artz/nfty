# Glossary

Every word the manual uses with a special meaning, in alphabetical order. The first six —
CookBook, Recipe, Ingredient, Variant, Set and Kitchen — are explained as a story in
[The six words](../understand/six-words.md).

**2D only**
:   Marks a book, Recipe or Ingredient with at least one partly transparent pixel. It cooks images
    normally but cannot rise. Fixed with **Make voxel-ready**. See
    [Fix a 2D-only book](../how-to/fix-a-2d-only-book.md).

**Absent chance**
:   The percent of assets that leave a layer out entirely, set per layer on a Recipe. `0` always
    shows it, `100` never does. See [Leave a layer out sometimes](../how-to/leave-a-layer-out.md).

**Canvas**
:   The pixel size every drawing in a CookBook shares. Fixed when the book is created and permanent.
    See [Why nfty never resizes your art](../understand/no-resizing.md).

**Colorization**
:   How a layer gets its color -- a hue and saturation range for Dynamic, one fixed color for Static,
    none at all for Custom.

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
    [Depth: paper, front and back](../understand/depth.md).

**DNA**
:   An asset's fingerprint, hashed from its Recipe, its variant choices, and its quantized colors. No
    two assets in a Set share one.

**Dynamic**
:   A layer kind: a gray value-map whose color is rolled per asset from a range.

**Extend**
:   To grow an existing Set, keeping every asset already in it and recomputing rarity. See
    [Make more assets later](../how-to/more-assets.md).

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
    [Roll assets at runtime](../how-to/roll-assets-at-runtime.md).

**Model**
:   A glTF 3D file (`.glb` or `.gltf`) made by Rise from one picture or one finished asset. A Set can
    carry one per asset in `models/`.

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
    thickness. See [Depth: paper, front and back](../understand/depth.md).

**Rule**
:   An exclusion: when this variant is picked, that one cannot be.

**Sealed** (`.tin`)
:   A Set encrypted for somebody else, optionally marked view-only. See
    [What sealing does and does not do](../understand/sealing.md).

**Seed**
:   The word that drives generation. Same book plus same seed produces identical output. See
    [Why a seed reproduces a collection](../understand/seeds.md).

**Set** (`.set`)
:   A cooked collection: images, metadata, rarity and the seed that made it.

**Spritesheet**
:   Every asset stitched into one image in the Set's own numbering. It carries its own grid inside the
    file. See [Rise a spritesheet](../how-to/rise-a-spritesheet.md).

**Static**
:   A layer kind: a gray value-map with one fixed color on every asset.

**Target supply**
:   An optional note on a CookBook of how many assets it is meant to produce. Advisory only; empty
    means "not decided".

**Ticket**
:   One rolled outcome, a few hundred bytes: the Recipe, one variant per layer, each colored layer's
    rolled hue and saturation, and the DNA. It names a selection, not pixels. See
    [Embed nfty in your program](../how-to/embed-nfty.md).

**Unique DNA**
:   How many distinguishable assets a CookBook can produce. A promise, not an estimate. See
    [How uniqueness is counted](../understand/uniqueness.md).

**Value-map**
:   A grayscale drawing where the gray means *lightness*, to be colored at generation time.

**Variant**
:   One option inside an Ingredient: a drawing, a name and a weight.

**Viewer**
:   The pop-out window that shows a cooked asset, a recipe roll or a gallery of a book's variants in
    2D, 3D or both. See
    [View an asset in its own window](../how-to/view-an-asset-in-its-own-window.md).

**Voxel**
:   A cube one pixel wide. Rise builds a model from voxel columns: one voxel thick for paper, plus the
    front and back depth you paint.

**Weight**
:   How often something is picked, relative to its siblings. Zero shelves it.
