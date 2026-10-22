# Cook with 3D models

Write a model of every asset into the Set while you cook, so the collection ships with `models/`
beside `images/`.

## In the app

In the **Cook** dialog, tick **Include 3D models**. It is off until you tick it. **Model options**
then opens a row for the format, the pivot and the voxel size; leave it closed to take the defaults
(GLB, bottom center, 1). The choices last for the session.

The models use the depth saved on each variant. A variant you never gave depth is flat paper, so a
book with no depth cooks thin models. Paint depth first on the Rise page
([Make a 3D model](make-a-3d-model.md)) if you want relief.

When the cook finishes, the result line adds the count (`500 models`) and, if any, how many assets
were skipped because nothing in them is fully opaque.

### When the box is dimmed

If **Include 3D models** is disabled, the book is **2D only**: some variants have partly transparent
pixels, and a voxel is solid or empty. The line beside the box says how many and sends you to
[Fix a 2D-only book](fix-a-2d-only-book.md).

## From the terminal

```bash
nfty generate mybook.cbk --count 500 --seed launch --out ./collection --models
nfty generate mybook.cbk --count 500 --seed launch --out ./collection --models --model-format gltf --pivot center --voxel-size 0.5
```

The model options are refused without `--models`, and a 2D-only book is refused before anything is
written. The options used are recorded in `set.json` (see
[What a Set contains](../reference/set-contents.md#models)), and `nfty inspect` on the finished Set
prints them on a `Models:` line.

## Add more assets later

```bash
nfty extend mybook.cbk ./collection --to 750
```

`extend` takes no model options. A Set is modeled whole or not at all, so it reads the format, pivot
and voxel size recorded in `set.json` and makes models for the new assets the same way. A Set cooked
without models stays without. Extending a modeled Set with a book that has since gained partial
transparency is refused. In the app the rule is the same: extending a modeled Set always makes
models, whatever the box says.

## Share them

[Exporting](share-a-collection.md#3d-models) carries `models/` unless you leave it out. A model's
texture is the asset as cooked, so the debug number mark never appears on one.
