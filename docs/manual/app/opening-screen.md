# The opening screen

What nfty shows before anything is open, and where it returns when you close a book (++ctrl+w++).
It has three columns (**CREATE**, **OPEN**, **RECENT**) and the Kitchen shelf along the bottom.

![The opening screen](../images/landing-light.png#only-light)
![The opening screen](../images/landing-dark.png#only-dark)

## CREATE

| Control | Makes |
|---|---|
| **New CookBook** (++ctrl+n++) | A new `.cbk`. Asks for a name, symbol, canvas size, an optional target supply (*not decided* by default) and a description, then opens it in the [Explorer](explorer.md). |
| **New Kitchen…** | A new `.ktn`, turning its folder into a workspace. See [Work out of a Kitchen](kitchen.md). |
| **Recipe** | A loose `.rcp` belonging to no CookBook, saved into the open Kitchen or wherever you choose. |
| **Ingredient** | A loose `.igt` belonging to no Recipe. Opens the Ingredient Editor on it. |

**The cooking metaphor**, under the wordmark, opens the Quick Reference on the vocabulary.

## OPEN

| Control | Opens |
|---|---|
| **Open CookBook…** (++ctrl+o++) | A `.cbk` in the Explorer. |
| **Import…** (++ctrl+i++) | A loose `.rcp` or `.igt` on its own, outside any CookBook (a `.cbk` or a cooked Set also opens). To bring a part or a picture *into* a book, use the Explorer's **Import**; see [Import art](import-art.md). |
| **Open a cooked .set…** | A packed `.set`, or a sealed `.tin` (which asks for its passphrase), in the [Set browser](set-browser.md). |
| **Open the demo CookBook** | The built-in Chest Demo, written into a `demo` folder beside the app. See [the demo](../get-started/the-demo.md). |
| **Open Kitchen…** | A `.ktn` workspace. |
| **Open in viewer…** | Any file the [viewer](viewer.md) reads, in its own window: an archive, a Set, a picture, a glTF model or an asset's metadata `.json`. |
| **Rise an asset…** | A `.png` on the [Rise page](rise.md), as one picture or, when it has a grid, as a [spritesheet](spritesheets.md). |

## RECENT

The ten files you opened last, with name, kind and full path. Click a row to reopen it. **Settings →
DATA → Clear recents** forgets the list without touching any file.

## The Kitchen shelf

The band along the bottom lists the contents of the open [Kitchen](kitchen.md): CookBooks first,
then loose Recipes and Ingredients. Click a card to open it; the chevrons page through by kind;
**close** leaves the Kitchen. A small flat-sheet mark in a card's corner means that part is
[2D only](2d-only.md).

![The Kitchen shelf](../images/kitchen-shelf-light.png#only-light)
![The Kitchen shelf](../images/kitchen-shelf-dark.png#only-dark)

The shelf is always there and always the same height. With no Kitchen open, or an empty one, it says
so, so opening a Kitchen never moves anything above it.
