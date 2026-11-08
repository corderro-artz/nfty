# Cook, extend and model

`generate` cooks a new Set from a CookBook; `extend` grows an existing one. Both can write a 3D model
of every asset.

## Generate a Set

```bash
nfty generate book.cbk --count 500 --seed launch --out ./collection
nfty generate book.cbk --count 500 --seed launch --out ./collection --pack
nfty generate book.cbk --count 50 --seed test --out ./cats --recipe cat
nfty generate book.cbk --count 100000 --seed big --out ./big --unlimited
```

| Option | Does |
|---|---|
| `--count` | How many assets. Required. |
| `--out` | The folder to write. Required. |
| `--seed` | The seed (default `nfty`). Same book and seed give byte-identical output. |
| `--pack` | Also write the folder as one `.set`, inside the folder and named after it. |
| `--recipe <id>` | Roll only this Recipe instead of choosing one per asset by weight. |
| `--unlimited` | Allow repeated DNA, removing the uniqueness limit. Identity becomes the token number. Validation, rules and bounded retries still apply. |
| `--max-rerolls` | Per-asset budget for finding a legal, unique roll (default 10,000). |

The book is validated first. Generation samples within a per-asset retry budget; failure reports
the known count or bound, and can occur before all legal DNA has been found. A failed fresh cook
removes the files it added. What the folder holds is in
[What a Set contains](../reference/set-contents.md). Check the run afterward with `nfty inspect
./collection`.

## Write 3D models while cooking

```bash
nfty generate book.cbk --count 500 --seed launch --out ./collection --models
nfty generate book.cbk --count 500 --seed launch --out ./collection --models \
    --model-format gltf --pivot center --voxel-size 0.5
```

| Option | Does |
|---|---|
| `--models` | Write `models/`, one model per asset named like its image (`0001.glb`). |
| `--model-format` | `glb` (default), `gltf` (with a `.bin` beside each), or `gltf-embedded`. |
| `--pivot` | `bottom-center` (default), `center` or `min-corner`. |
| `--voxel-size` | World units per pixel, 0.001 to 1000 (default 1). |

The three model options are refused without `--models`. A book with any partly transparent pixel is
refused before anything is written (see [Fix a 2D-only book](../app/2d-only.md)). An asset with
nothing fully opaque gets no model and is listed (`skipped (nothing fully opaque): #0007`). The
options are recorded in `set.json`.

## Extend a Set

```bash
nfty extend book.cbk ./collection --to 750
nfty extend book.cbk ./collection --to 750 --seed second-drop --pack
```

`extend` keeps existing images and DNA and continues the numbering. With uniqueness enforced it
rejects collisions with earlier DNA; `--unlimited` permits repeats.

| Option | Does |
|---|---|
| `--to` | The total the Set should reach. Required. |
| `--seed` | The seed for the new rolls (default `nfty-extend`). |
| `--unlimited`, `--max-rerolls` | As for `generate`. |
| `--pack` | Repackage the extended Set as a `.set`. |

Things to know:

- **Rarity is recomputed** across the whole collection, so existing items' `rarity` fields are
  rewritten. Their images and DNA never change. Canceling removes newly added files but does not
  restore rarity fields already rewritten on older items.
- **A changed book is warned about, not refused.** If the book's hash differs from the one in
  `set.json`, `extend` prints a warning on stderr and carries on. A book that gained a layer or was
  reordered rolls differently; see [Why a seed reproduces a collection](../concepts/seeds.md).
- **Models follow the Set.** `extend` takes no model options: a Set cooked with models gets models for
  the new assets in the recorded format; a Set without stays without.
- **Exports cannot be extended.** An export whose files were renamed for a marketplace is refused;
  extend the source Set, then export again.
- The app does the same thing when you cook into a folder that already holds a Set; see
  [Add assets to an existing Set](../app/cooking.md#add-assets-to-an-existing-set).

## Next

[Models, depth and sheets](models.md) · [Export a Set](../publishing/export.md)
