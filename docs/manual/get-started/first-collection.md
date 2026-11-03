# Your first collection

By the end of this page you will have a folder of finished, numbered assets on your disk. It takes
about twenty minutes, and you will draw two simple shapes — no art skill needed.

If you have not looked at [the demo CookBook](the-demo.md) yet, do that first: it is one click and it
shows you the finished shape of what this page builds up to.

We will build the beginning of a collection called **Vapor Pets**.

!!! note "The screenshots are of the demo"

    The figures on this page show the **Chest Demo**, a finished book, so their numbers are larger
    than yours will be. The layout of every screen is the same.

## 1. Make a CookBook

A **CookBook** is your whole project in one file.

On the opening screen, click **New CookBook**. Fill it in like this:

![The New CookBook dialog, still empty: name, symbol, canvas size, target supply and description](../images/new-cookbook-light.png#only-light)
![The New CookBook dialog, still empty: name, symbol, canvas size, target supply and description](../images/new-cookbook-dark.png#only-dark)

- **Name** — `Vapor Pets`
- **Symbol** — `VP`, a short code for the collection
- **Canvas** — `64` by `64`. Every drawing in this project will be exactly this size.

Leave **Target supply** at *not decided* and **Description** empty. Click **Create CookBook** and
choose where to save it.

!!! warning "The canvas size is permanent"

    You cannot change it later, and every drawing is checked against it. 64×64 is a good size to
    learn on; a real collection is often 512×512 or 1000×1000.

## 2. Unlock editing

The Explorer opens with your empty book. Look at the top right: the chip says **read-only**, and the
status bar says editing is locked.

![The Explorer with a CookBook open. The read-only chip is at the top right, the tree on the left](../images/explorer-light.png#only-light)
![The Explorer with a CookBook open. The read-only chip is at the top right, the tree on the left](../images/explorer-dark.png#only-dark)

**Click the read-only chip itself.** It turns carmine and reads **editing**.

This lock exists so you cannot rearrange a book by accident. You will use it every session.

## 3. Add a Recipe

A **Recipe** is one *type* of character. A book can hold several — a Cat and a Fox, say — and each
asset is rolled from one of them.

With **Vapor Pets** selected in the tree on the left, click **Add recipe**. Name it `Cat`.

## 4. Add a layer

An **Ingredient** is one layer — one row of the stack.

Select **Cat** in the tree, then click **Add ingredient**. Name it `Background` and leave its kind as
**Dynamic**.

Dynamic means *you draw it in gray and nfty picks a color for it on every asset*. That is where a
collection's variety comes from. The other two kinds are covered in
[The three kinds of layer](../understand/layer-kinds.md).

## 5. Draw two options for it

Select **Background** in the tree, then click the **pencil** button on its panel. The Ingredient
Editor opens.

![The Ingredient Editor: variants on the left, the canvas in the middle, the colorize rail on the right](../images/editor-light.png#only-light)
![The Ingredient Editor: variants on the left, the canvas in the middle, the colorize rail on the right](../images/editor-dark.png#only-dark)

Draw the first option:

1. Click the **flood fill** tool — the third icon, a paint bucket.
2. Click a **mid-gray** swatch in the strip under the tools.
3. Click anywhere on the canvas. The whole canvas fills.

Now the second option:

4. Click **+ Add Variant** on the left. You get a fresh empty canvas.
5. Fill it the same way, but pick a **much lighter gray** this time.

Two variants, two brightnesses. Because this is a Dynamic layer, the brightness you paint is
*lightness only* — nfty supplies the hue later, so these will not be two grays in the output, they
will be a dark and a light version of whatever color each asset rolls.

Click **Save** at the bottom right, then **Back**.

## 6. Check what it can make

Click **Vapor Pets** at the top of the tree. The panel shows the book's numbers:

![The CookBook panel: counts, the unique DNA figure, target supply and each Recipe's share of the DNA space](../images/cookbook-panel-light.png#only-light)
![The CookBook panel: counts, the unique DNA figure, target supply and each Recipe's share of the DNA space](../images/cookbook-panel-dark.png#only-dark)

The number that matters before cooking is **UNIQUE DNA** — how many genuinely different assets this
book can produce. With one Dynamic layer and two variants it is already in the hundreds, because each
asset also rolls its own color.

## 7. Cook it

Click **Cook Set**.

![The Cook dialog: count, seed, recipe, and the pack, repeats and 3D models options](../images/cook-dialog-light.png#only-light)
![The Cook dialog: count, seed, recipe, and the pack, repeats and 3D models options](../images/cook-dialog-dark.png#only-dark)

- **Count** — how many assets to make. `50` is a good first run.
- **Seed** — any word. It arrives pre-filled with a random one; type something you will remember,
  like `launch`. The same book and the same seed always produce the same collection.
- Leave **Pack into a single .set** unchecked so you get a plain folder you can look through, and
  leave **Include 3D models** unchecked for now.

Click **Cook**, then choose an empty folder to write into.

![The Cook dialog reporting the finished set: 50 assets, 50 models](../images/cook-done-light.png#only-light)
![The Cook dialog reporting the finished set: 50 assets, 50 models](../images/cook-done-dark.png#only-dark)

Click the folder row to open it in your file manager. Inside:

- `images/` — your assets, numbered `0001.png` upward
- `metadata/` — one JSON file per asset, in the format marketplaces expect
- `nfty/` — a richer JSON file per asset: its fingerprint, its seed, its rarity, the exact color each
  layer rolled
- `set.json` — the collection as a whole

## 8. Look at the Set

Click **Open Set** instead and nfty shows the collection as a grid. Click an asset to see its
fingerprint and the rarity of each of its traits on the right.

![The Set browser: a grid of fifty chests, with one asset's DNA and trait rarity on the right](../images/set-browser-light.png#only-light)
![The Set browser: a grid of fifty chests, with one asset's DNA and trait rarity on the right](../images/set-browser-dark.png#only-dark)

## You have a collection

Fifty assets, each provably distinct, from two drawings.

They are two flat rectangles, so it is not much to look at yet — that is the next page's job. But
everything after this is the same steps with more layers in them.

---

**Next:** [Draw your first layer →](first-layer.md)
