# nfty — User Guide

**How to make a collection, from a blank folder to a finished set of assets.**

nfty builds large collections of layered pixel art. You draw a handful of pieces; it stacks them in
every legal combination and colors them, so a few dozen drawings become thousands of distinct
assets — each one recorded with a fingerprint that proves it is unique.

This guide is for using the app. If you want to build or modify nfty itself, read the
[README](../README.md) instead.

---

## Contents

1. [The idea in one minute](#1-the-idea-in-one-minute)
2. [The six words](#2-the-six-words)
3. [Your first collection](#3-your-first-collection)
4. [The three kinds of layer](#4-the-three-kinds-of-layer)
5. [Drawing](#5-drawing)
6. [Color mode](#6-color-mode)
7. [Transparency and the opacity lock](#7-transparency-and-the-opacity-lock)
8. [Layer order](#8-layer-order)
9. [Weights, rules and how many you can make](#9-weights-rules-and-how-many-you-can-make)
10. [Cooking a Set](#10-cooking-a-set)
11. [The Kitchen](#11-the-kitchen)
12. [Reading the screens](#12-reading-the-screens)
13. [Doing it from the command line](#13-doing-it-from-the-command-line)
14. [When something goes wrong](#14-when-something-goes-wrong)

---

## 1. The idea in one minute

A character is made of stacked layers — a background, a body, eyes, an aura. You draw a few options
for each layer. nfty picks one option per layer, stacks them, and that is one asset. Do it a thousand
times with different picks and you have a collection.

The twist is **color**. Most layers are not drawn in color at all. You draw them in **gray**, and
nfty treats the gray as *lightness* — how bright each pixel is — then adds a color at generation
time. One gray drawing of an aura becomes a blue aura, a pink aura, a green one, all with the same
shading you drew.

That is why a small amount of art goes a very long way.

**Everything is repeatable.** Every collection is generated from a *seed* — a word you choose. The
same book and the same seed always produce exactly the same collection, on any computer. Change the
seed and you get a different one.

## 2. The six words

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

> Every one of these files is really just a `.zip` with a different name. If you ever want to look
> inside one, rename it to `.zip` and open it.

## 3. Your first collection

**1. Make a CookBook.** On the opening screen, click **New CookBook**. Give it a name, a symbol
(a short code like `VP`), and a **canvas size** — the pixel dimensions every drawing in this project
will share. 512×512 is a good starting point. You cannot change it later, and every drawing is
checked against it, so pick deliberately.

**2. Add a Recipe.** With the book open, click **Add recipe**. This is one character type. Name it
something like "Cat".

**3. Add layers.** Select the Recipe and click **Add ingredient** for each layer you want —
"Background", "Body", "Eyes". Add them in the order they should be painted: **the first one you add
sits furthest back.**

**4. Draw.** Select a layer and click the **pencil** button on its detail panel to open the
Ingredient Editor. Draw a variant, then click **Add Variant** and draw another. Each variant is one
option for that layer.

**5. Save.** The **Save** button at the bottom right writes your work back into the CookBook.

**6. Cook.** Go back to the CookBook (click its name in the tree), check the **UNIQUE DNA** number is
at least as large as the collection you want, and click **Cook Set**.

## 4. The three kinds of layer

When you create a layer you choose its kind. This is the most important decision you make about a
layer, and it decides how you draw it.

### Dynamic — a different color every time

You draw it in **gray**. nfty picks a color for it per asset, from a range you set.

Use it for anything that should vary: auras, fur, clothing, gems. This is where the multiplication
happens — one gray drawing with a wide hue range becomes dozens of visibly different assets.

You set a **hue range** (0–360°, the color wheel) and a **saturation range** (0–100%, gray to
vivid). Every asset rolls a color inside those.

### Static — one fixed color, always

You draw it in **gray**, and nfty applies a single color you choose. Same color on every asset.

Use it for anything that should be consistent but is easier to draw in gray — an outline, a shadow, a
brand color.

### Custom — exactly what you drew

Full-color art, placed as-is and never recolored.

Use it for anything whose colors matter as drawn: a logo, a detailed background, a piece of art that
is not a silhouette.

> **Why draw in gray at all?** Because a gray drawing carries the *shading* and nfty supplies the
> *color*. Draw a sphere in gray once and it can be a red sphere, a blue sphere or a gold one, all
> correctly shaded. Draw it in red and it is only ever red.

## 5. Drawing

The Ingredient Editor has three panes: your **variants** on the left, the **canvas** in the middle,
and **colorize** settings on the right.

**Tools**, along the top: pencil, eraser, flood fill, then rectangle, ellipse, triangle, line and
select, then undo/redo. **Brush size** is the number box at the end of the strip.

Every shape tool works the same way: press where you want one corner, drag, and let go. The outline
follows your cursor while you drag, so you can see what you are about to make before you commit it.
The **line** tool joins where you pressed to where you let go, at the current brush size — the
wandering in between does not count.

**Select** is two gestures in one tool, told apart by where you start the drag:

- Drag on empty canvas to **mark** a rectangle. A dashed marquee shows what is marked.
- Drag from **inside** the marquee to **move** those pixels somewhere else. What they left behind is
  cleared, and the marquee travels with them.

A single click away from the marquee drops it, as does pressing **Escape** or picking another tool.
A move is a normal edit, so **undo** puts it back.

**The value slider** is the black-to-white gradient. On a gray layer this *is* the color you paint —
it sets how bright the pixel is, and therefore how bright it will be after nfty colors it. In color
mode it becomes the brightness of the color you are painting.

**The preview** in the bottom-right corner of the canvas shows what the layer will actually look like
once colored. Its three small buttons re-roll the sampled color, enlarge it, and let it take over
the canvas.

**Reference layers**, at the bottom of the right-hand panel, are how you line art up. Switch on any
other layer in the Recipe and it composites underneath or on top of what you are drawing, depending
on its depth — so you can see the eyes sitting on the face while you draw them. Layers above yours are
dimmed by default so they cannot hide your work; **True color** shows the real composite.

**Importing** a drawing made elsewhere: **Import image…** on the left. It must match the CookBook's
canvas size exactly — nfty will not resize your art. On a gray layer a color image is converted to
its lightness, and the app tells you when that happens.

## 6. Color mode

The palette strip under the tools has a **Gray / Color** switch.

**Gray** is the normal mode for Dynamic and Static layers: ten shades of gray, plus the value slider
for anything in between.

**Color** paints in full color. The ten slots become a rainbow, and the right-hand panel turns into
**Paint Hue** and **Paint Saturation** — those two plus the brightness slider give you any color.

Switching to color mode brings your existing drawing with you, in gray, so you can paint over it.

**Color art can only be saved as a Custom layer**, because a Custom layer is the only kind that keeps
the colors you drew. So when you save a gray layer that you painted in color, nfty asks:

- **Save as new** *(the default)* — makes a new Custom layer on top of the Recipe and leaves the
  original gray layer exactly as it was.
- **Overwrite** — converts the original. **This discards its color settings** — the hue and
  saturation ranges and the quantize steps — and they cannot be recovered.

**Saving colors you like:** the **+** button on the palette strip saves the current color. Saved
colors persist between sessions. Right-click one to forget it. A CookBook can also carry its own
palette, which travels inside the file — those appear first and cannot be deleted from the editor.

## 7. Transparency and the opacity lock

The **padlock** at the right of the palette strip is on by default. While it is on, every pixel you
paint is either **fully there** or **fully erased** — nothing in between.

This is deliberate. Partly-transparent pixels have no clear answer when art is converted into a 3D
voxel model: a converter has to either drop the pixel or make it solid, and either way the result is
not what you drew.

Erasing still works normally — that is how a layer gets its shape, and layers have to show through
each other.

If you do want soft edges, click the padlock. nfty explains the trade-off once, and then the **A**
(alpha) slider beside it becomes active. Use it knowingly.

To check a finished project, run `nfty inspect yourbook.cbk --voxel` — it lists every variant carrying
partial transparency and how much.

## 8. Layer order

A Recipe's layers are painted **bottom to top**. Depth 1 paints first and sits furthest back.

You can see the order on the Recipe's detail panel, numbered. To change it: **unlock editing** with
the padlock in the toolbar, then drag a row by its grip — or select a row and press **Alt+Up** /
**Alt+Down**.

> **Reordering makes a different collection, not the same one rearranged.** nfty draws a random
> number for each layer in order, so moving a layer changes which draw reaches it. If you have already
> cooked a Set from this book, reordering means the next cook produces different assets — not the same
> assets restacked.

## 9. Weights, rules and how many you can make

**Weights** decide how often something is picked. A variant with weight 3 is picked three times as
often as one with weight 1. Weight 0 shelves a variant without deleting it. Recipes have weights too —
that is how you make one character type rarer than another.

**Rules** stop combinations that should not happen — a hat that clashes with certain hair, say. A rule
says "when this variant is picked, exclude that one". Set them on the Recipe's panel.

**UNIQUE DNA** on the CookBook panel is the number that matters before you cook. It is how many
genuinely distinct assets this book can produce, counting both the variant combinations *and* the
distinguishable colors a Dynamic layer can roll.

If you ask for more assets than that number, nfty refuses and tells you the real maximum, rather than
producing duplicates. To raise it: add variants, widen a hue range, or raise a layer's **quantize**
steps — the "≈ N colors" readout on the colorize panel shows how many distinct colors the current
settings admit.

## 10. Cooking a Set

**Cook Set** on the CookBook panel. You give it:

- **How many** assets to make (50 by default).
- **A seed** — any word. It arrives pre-filled with a random one; replace it with something you will
  remember if you want to be able to reproduce this exact collection later. The same book and the
  same seed always produce the same collection.
- Optionally, **pack** the result into a single `.set` file rather than a folder.

The result is a Set: numbered PNGs, plus two data files per asset — a standard `metadata/NNNN.json`
that marketplaces understand, and a richer `nfty/NNNN.json` with the fingerprint, the seed, the
rarity, and the exact color each layer rolled.

**Extending later:** you can grow an existing Set rather than regenerating it. nfty re-opens it, keeps
every asset it already made, adds new ones that do not collide, and recalculates rarity across the
whole collection — because rarity depends on the collection as a whole.

> If you have edited the CookBook since the Set was made, nfty warns you. The Set records which book
> made it, so it can tell.

## 11. The Kitchen

A **Kitchen** is simply a folder you work out of. Create one with **New Kitchen…** or open an existing
one with **Open Kitchen…**.

With a Kitchen open:

- The **shelf** along the bottom of the opening screen lists everything in it — CookBooks first, then
  loose Recipes and Ingredients. Click a card to open it. Scroll the shelf, or use the arrows, to page
  through by kind.
- Loose Recipes and Ingredients you create are saved into it automatically, instead of asking.
- The Ingredient Editor can borrow loose Ingredients from it as reference layers.

Membership is worked out by **looking at the folder**, not from a list. Drop a `.cbk` in with your
file manager and it appears; move one out and it is gone. Nothing to keep in sync.

## 12. Reading the screens

**The opening screen** — create or open things on the left, recently opened files on the right, and
the Kitchen shelf along the bottom.

**The Explorer** — your CookBook as a tree on the left, and a panel on the right that changes with
what you select. The letter beside each layer is its kind: **D**ynamic, **S**tatic, **C**ustom.

**Editing is locked by default.** The padlock in the toolbar unlocks adding, deleting and reordering.
The chip in the title bar and the message in the status bar always say which state you are in.

**The CookBook panel** — the collection's identity, its counts, how many unique assets it can make,
and the share each Recipe takes of the collection.

**The Recipe panel** — the layer stack in paint order, and the rules.

**The Ingredient panel** — the variants with their weights and how often each will appear, and a
**Colorways** strip showing the range of colors this layer can take.

## 13. Doing it from the command line

Everything the app does, the command line does too — useful for scripting or for checking a project
before you cook it.

```bash
nfty inspect  mybook.cbk          # what is in it
nfty inspect  mybook.cbk --voxel  # which art has partial transparency
nfty validate mybook.cbk          # is anything wrong
nfty stats    mybook.cbk          # what odds do the weights imply
nfty preview  cat.rcp --seed alpha --out preview.png
nfty generate mybook.cbk --count 500 --seed launch --out ./collection
nfty extend   mybook.cbk ./collection --to 750
```

`nfty --help`, or `nfty <command> --help`, explains any of them.

## 14. When something goes wrong

**"This image is 256×256; the canvas is 512×512."**
Every drawing in a project shares one canvas size. nfty will not resize your art, because resizing
pixel art destroys it. Redraw or re-export at the right size.

**"…allows exactly N unique assets, but M were requested."**
The book cannot make that many distinct assets. Add variants, widen a hue range, or raise a layer's
quantize steps. The message tells you the real maximum.

**"Recipe X has two ingredients named Y."**
Two layers in one Recipe share a name. A layer's name becomes the trait name in the published data,
so two of them would merge into one trait and one rarity bucket. Rename one.

**Validation lists several problems at once.**
That is deliberate — it reports everything it finds rather than stopping at the first, so you can fix
them in one pass.

**A layer is not showing up in the output.**
Check its variants' weights. A variant at weight 0 is shelved. Check the Recipe's rules too — a rule
may be excluding it more often than you intended.

**Your art looks blurry.**
It should not. nfty never smooths, resamples or anti-aliases your pixels anywhere — in the editor, the
previews, or the output. If something looks soft, it is soft in the source art, or it is partial
transparency (see [the opacity lock](#7-transparency-and-the-opacity-lock)).

---

<sub>© 2026 [Vaporsoft](https://www.vaporsoft.dev). All rights reserved.</sub>
