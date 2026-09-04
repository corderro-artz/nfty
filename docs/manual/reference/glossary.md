# Glossary

**Art combinations**
:   How many distinct *variant* combinations a Recipe can produce, with its rules applied. Colors are
    not counted here -- that is the DNA space.

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
:   A folder you work out of. Membership is discovered by scanning, never recorded.

**Layer order**
:   The paint order of a Recipe's Ingredients, bottom to top. Depth 1 paints first and sits furthest
    back.

**Opacity lock**
:   The editor padlock that keeps every painted pixel fully opaque or fully erased.

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

**Seed**
:   The word that drives generation. Same book plus same seed produces identical output.

**Set** (`.set`)
:   A cooked collection: images, metadata, rarity and the seed that made it.

**Static**
:   A layer kind: a gray value-map with one fixed color on every asset.

**Target supply**
:   An optional note on a CookBook of how many assets it is meant to produce. Advisory only.

**Unique DNA**
:   How many distinguishable assets a CookBook can produce. A promise, not an estimate.

**Value-map**
:   A grayscale drawing where the gray means *lightness*, to be colored at generation time.

**Variant**
:   One option inside an Ingredient: a drawing, a name and a weight.

**Weight**
:   How often something is picked, relative to its siblings. Zero shelves it.
