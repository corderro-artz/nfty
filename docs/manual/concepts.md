# The six words

nfty uses a cooking metaphor. Learning these six words *is* learning the app.

| Word | What it means | Saved as |
|------|---------------|----------|
| **CookBook** | Your whole project. Holds the canvas size, the collection's name, and its Recipes. | `.cbk` |
| **Recipe** | One *type* of character — its full stack of layers, in order. A book can have several. | `.rcp` |
| **Ingredient** | One layer: "Background", "Eyes", "Aura". Holds all the options for that layer. | `.igt` |
| **Variant** | One option inside a layer — a single drawing, with a name and a weight. | *(inside an Ingredient)* |
| **Set** | The finished output: the images, their data, and the seed that made them. | `.set` |
| **Kitchen** | A folder you work out of. Anything you save there shows up in the app. | `.ktn` |

So: a **CookBook** holds **Recipes**, a Recipe holds **Ingredients** in order, an Ingredient holds
**Variants**, and cooking it produces a **Set**.

!!! tip "They are all just zip files"

    Every one of these files is really a `.zip` with a different name. If you ever want to look
    inside one, rename it to `.zip` and open it.
