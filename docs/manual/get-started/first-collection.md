# Your first collection in the app

In this tutorial you create a CookBook, add one layer with two drawings, and cook fifty finished,
numbered assets into a folder. You draw two flat fills; no art skill is needed.

**Before you start:** [install nfty](install.md). It helps to have looked at
[the demo CookBook](the-demo.md) first, because it is the finished shape of what this builds toward.

**You will build:** the start of a collection called **Vapor Pets**.

!!! note "The screenshots show the demo"

    The figures here show the **Chest Demo**, a finished book, so their numbers are larger than
    yours will be. The layout of every screen is the same.

## 1. Create a CookBook

A **CookBook** is your whole project in one `.cbk` file.

1. On the opening screen, click **New CookBook**.

    ![The New CookBook dialog, still empty: name, symbol, canvas size, target supply and description](../images/new-cookbook-light.png#only-light)
    ![The New CookBook dialog, still empty: name, symbol, canvas size, target supply and description](../images/new-cookbook-dark.png#only-dark)

2. Fill in:
    - **Name**: `Vapor Pets`
    - **Symbol**: `VP`, a short ticker for the collection
    - **Canvas**: `64` by `64`. Every drawing in this project will be exactly this size.
3. Leave **Target supply** at *not decided* and **Description** empty.
4. Click **Create CookBook** and choose where to save the file.

!!! warning "The canvas size is permanent"

    You cannot change it later, and every drawing is checked against it. 64×64 is a good size to
    learn on; real collections are often 512×512 or 1000×1000.

## 2. Unlock editing

The Explorer opens with your empty book. The chip at the top right reads **read-only**, and the
status bar says editing is locked.

![The Explorer with a CookBook open. The read-only chip is at the top right, the tree on the left](../images/explorer-light.png#only-light)
![The Explorer with a CookBook open. The read-only chip is at the top right, the tree on the left](../images/explorer-dark.png#only-dark)

**Click the read-only chip.** It turns carmine and reads **editing**. The lock stops you
rearranging a book by accident; you will use it every session. See
[The edit lock](../app/explorer.md#the-edit-lock).

## 3. Add a Recipe

A **Recipe** is one *type* of character, such as a Cat or a Fox. A book can hold several, and each
asset is rolled from one of them.

1. Select **Vapor Pets** in the tree.
2. Click **Add recipe** in the toolbar and name it `Cat`.

## 4. Add a layer

An **Ingredient** is one layer of the stack.

1. Select **Cat** in the tree.
2. Click **Add ingredient**. Name it `Background` and keep its kind **Dynamic**.

Dynamic means *drawn in gray; nfty picks a color for it on every asset*. That is where a
collection's variety comes from. The other kinds are in
[Layer kinds and value-maps](../concepts/layer-kinds.md).

## 5. Draw two options

1. Select **Background** in the tree and click the **pencil** on its panel. The Ingredient Editor
   opens.

    ![The Ingredient Editor: variants on the left, the canvas in the middle, the colorize rail on the right](../images/editor-light.png#only-light)
    ![The Ingredient Editor: variants on the left, the canvas in the middle, the colorize rail on the right](../images/editor-dark.png#only-dark)

2. Click the **fill** tool (the paint bucket, ++g++).
3. Click a **mid-gray** swatch in the strip under the tools, then click the canvas. It fills.
4. Click **Add Variant** in the left pane. You get a fresh, empty canvas.
5. Fill it the same way with a **much lighter gray**.
6. Click **Save**, then **Back**.

Because this is a Dynamic layer, the gray you paint is *lightness only*. In the output these will
not be two grays; they will be a dark and a light version of whatever color each asset rolls.

## 6. Check what the book can make

Click **Vapor Pets** at the top of the tree. The panel shows the book's numbers:

![The CookBook panel: counts, the unique DNA figure, target supply and each Recipe's share of the DNA space](../images/cookbook-panel-light.png#only-light)
![The CookBook panel: counts, the unique DNA figure, target supply and each Recipe's share of the DNA space](../images/cookbook-panel-dark.png#only-dark)

**UNIQUE DNA** is how many genuinely different assets the book can produce. With one Dynamic layer
and two variants it is already in the dozens, because each asset also rolls its own color.

## 7. Cook it

1. Click **Cook Set**.

    ![The Cook dialog: count, seed, recipe, and the pack, repeats and 3D models options](../images/cook-dialog-light.png#only-light)
    ![The Cook dialog: count, seed, recipe, and the pack, repeats and 3D models options](../images/cook-dialog-dark.png#only-dark)

2. Set **Count** to `50`. If you ask for more than the book can make, nfty refuses and states the
   real maximum; lower the count to that figure.
3. Replace the random **Seed** with a word you will remember, such as `launch`.
4. Leave **Pack into a single .set**, **Allow repeats** and **Include 3D models** unticked.
5. Click **Cook** and choose an **empty** folder to write into.

![The Cook dialog reporting the finished set](../images/cook-done-light.png#only-light)
![The Cook dialog reporting the finished set](../images/cook-done-dark.png#only-dark)

## 8. Look at the result

Click the folder row to open it in your file manager. Inside:

| Path | Holds |
|---|---|
| `images/` | Your assets: `0001.png`, `0002.png` and up |
| `metadata/` | One JSON file per asset, in the format marketplaces read |
| `nfty/` | One richer JSON file per asset: its DNA, the seed, rarity and every rolled color |
| `set.json` | The collection as a whole |

Then click **Open Set** in the dialog. nfty shows the collection as a grid; point at an asset to see
its DNA and the rarity of each trait on the right.

![The Set browser: a grid of chests, with one asset's DNA and trait rarity on the right](../images/set-browser-light.png#only-light)
![The Set browser: a grid of chests, with one asset's DNA and trait rarity on the right](../images/set-browser-dark.png#only-dark)

## What you did

You made a CookBook, a Recipe and one Dynamic layer with two variants, and cooked fifty provably
distinct assets from a seed. Cooking the same book with the same seed again gives the same fifty
files, byte for byte.

## Next step

[Draw your first layer →](first-layer.md) puts a character on top of the background.
