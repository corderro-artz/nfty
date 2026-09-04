# The six words

nfty uses a cooking metaphor. Learning these six words *is* learning the app.

| Word | What it means | Saved as |
|------|---------------|----------|
| **CookBook** | Your whole project. Holds the canvas size, the collection's name, and its Recipes. | `.cbk` |
| **Recipe** | One *type* of character -- its full stack of layers, in order, plus its rules. A book can have several. | `.rcp` |
| **Ingredient** | One layer: "Background", "Eyes", "Aura". Holds that layer's kind, its coloring, and all its options. | `.igt` |
| **Variant** | One option inside a layer -- a single drawing, with a name and a weight. | *(inside an Ingredient)* |
| **Set** | The finished output: the images, their data, the rarity, and the seed that made them. | `.set` |
| **Kitchen** | Your shelf of loose parts -- Ingredients and Recipes that belong to no one CookBook. | `.ktn` |

So: a **CookBook** holds **Recipes**, a Recipe holds **Ingredients** in order, an Ingredient holds
**Variants**, and cooking the book produces a **Set**. A **Kitchen** is not part of that chain at
all -- it is the shelf the chain draws from: loose parts that belong to no one book, ready to be
pulled into any of them.

## Why a metaphor at all

Because the alternative words are worse. "Project", "template", "layer", "asset" and "collection" are
each used by three other tools to mean three other things, and the relationships between them would
have to be memorised separately. The kitchen ones carry their own structure: nobody needs to be told
that a recipe lives in a cookbook, or that ingredients are what a recipe is made of.

The one place it strains is **Variant**, which is not a cooking word. It is an option inside an
Ingredient, and it never gets a file of its own.

!!! tip "They are all just zip files"

    Every one of these is really a `.zip` with a different extension. Rename one and open it with any
    unzip tool to see exactly what it holds. There is no hidden database and no proprietary blob --
    see [File formats](../reference/file-formats.md).
