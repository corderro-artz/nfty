# Cook a Set

Cooking rolls a CookBook into a **Set**: a folder of finished, numbered assets with their metadata.
The same dialog also adds assets to a Set you cooked before.

**Before you start:** the book must be valid. The CookBook panel's **status** chip says so; click it
to see any problems. The lock does not need to be open.

## Cook a new Set

1. Select the CookBook in the Explorer and click **Cook Set**.

    ![The Cook dialog: count, seed, recipe, packing, repeats, folder naming and 3D models](../images/cook-dialog-light.png#only-light)
    ![The Cook dialog: count, seed, recipe, packing, repeats, folder naming and 3D models](../images/cook-dialog-dark.png#only-dark)

2. Fill in the options:

    | Option | Means |
    |---|---|
    | **COUNT** | How many assets to make. |
    | **SEED** | Any word. Arrives with a random one; the die rolls another. The same book, seed and options cook the same collection. |
    | **RECIPE** | Every Recipe by its weight (the default), or one Recipe only. |
    | **Pack into a single .set** | Also write the folder as one `.set` archive, inside the folder. |
    | **Allow repeats** | Accept repeated DNA, removing the uniqueness limit. Rules and bounded retries still apply; identity becomes the token number. See [Allowing repeats](../concepts/uniqueness.md#allowing-repeats). |
    | **Name after CookBook** | Choose a parent folder and cook into a child named after the CookBook. Off by default. The name uses lowercase letters, numbers and hyphens: **Chests Demo** becomes `chests-demo`. Hover over the option to preview the folder name. |
    | **Include 3D models** | Also write a model of every asset into `models/`. See [below](#cook-with-3d-models). |

3. Click **Cook**. With **Name after CookBook** off, choose an **empty** folder. With it on, choose
   the **parent** folder; nfty creates the named child inside it. That parent can already contain
   other files.

The output folder must be empty, new, or an existing readable Set. If another file or a populated
non-Set folder occupies the named child, the cook is refused; nfty does not silently replace it or
choose a different name. For a nonempty folder such as your Desktop, enable **Name after CookBook**
to keep the new Set in its own child folder, or choose an empty folder instead. Packing writes the
archive inside the output folder, for example `chests-demo/chests-demo.set`.

A progress bar runs while the assets are rolled and written; **Cancel** stops it, and the files that
cook added are removed. Existing images and DNA are kept, although a canceled extend can already
have updated older assets' rarity fields; it does not restore every previous metadata byte.
When it finishes,
the dialog reports the result. Click the folder row to open it in your file manager, or **Open Set**
to browse it in the [Set browser](set-browser.md). What the folder holds is in
[What a Set contains](../reference/set-contents.md).

![The Cook dialog reporting the finished set](../images/cook-done-light.png#only-light)
![The Cook dialog reporting the finished set](../images/cook-done-dark.png#only-dark)

## Add assets to an existing Set

With **Name after CookBook** off, choose a folder that **already holds a Set** in step 3. With it on,
choose that Set's parent: an existing readable Set in the named child is extended. Check the folder
name in the option's tooltip before choosing the parent. nfty extends that Set:

- **COUNT** becomes the number to **add**; the dialog shows the resulting total (`→ 750 total`).
- Existing images and DNA are kept, and numbering continues. With unique DNA required, new DNA
  cannot collide with an earlier asset; **Allow repeats** permits those collisions.
- Rarity is recomputed across the whole collection, so existing items' `rarity` fields are rewritten.
- The Set's own Recipe mix is used, so the **RECIPE** choice is set aside.
- If the book has changed since the Set was cooked (its hash differs), the dialog **warns** and carries
  on. A changed book rolls differently; see [Why a seed reproduces a collection](../concepts/seeds.md).
- A Set cooked with models gets models for the new assets in the same format, whatever the box says.

The command-line equivalent is `nfty extend`; see [Cook, extend and model](../cli/cook.md#extend-a-set).

## Cook with 3D models

Tick **Include 3D models** and every asset also gets a glTF model in `models/`, named like its image
(`0001.glb`). Click the chevron beside the box (**Model options** in its tooltip) to open a row for:

| Option | Choices |
|---|---|
| **FORMAT** | GLB (one file, the default), glTF with a `.bin`, or glTF with the data embedded. |
| **PIVOT** | Bottom center (default), center, or min corner. |
| **VOXEL SIZE** | World units per pixel, 0.001 to 1000 (default 1). |

Models use the depth saved on each variant. A variant with no depth is flat paper, one voxel thick,
so paint depth first on the [Rise page](rise.md) if you want relief. An asset with nothing fully
opaque gets no model and is counted in the result.

If **Include 3D models** is dimmed, the book is **2D only**: some variant has partly transparent
pixels. The line below the options says how many; see [Fix a 2D-only book](2d-only.md).

## If the book cannot make enough

Without **Allow repeats**, a request beyond the remaining exact DNA space cannot succeed. A cook
can also reach its retry budget while unused DNA remains, particularly with uneven weights or
restrictive rules. Read the message's count or bound and the number produced. To expand the space:

1. **Widen a hue or saturation range** on a Dynamic layer (COLORIZE tab). No drawing needed.
2. **Make the quantize steps finer.** Smaller steps mean more colors count as different.
3. **Add a variant.** The only one that adds visible variety, not just count.
4. **Loosen a rule.** Every rule removes combinations.

Or tick **Allow repeats** and stop needing the figure. Weights still decide rarity.

!!! note "Colors multiply the count; variants multiply the variety"

    A book with two drawings and a huge hue range can produce a million distinct assets that all look
    like the same two drawings. Push both.

If a cook reaches its reroll budget, review weights, rules and the requested count. A larger
`--max-rerolls` on the command line gives sampling more attempts; it cannot create additional DNA
or make contradictory rules legal.
