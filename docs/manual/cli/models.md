# Models, depth and sheets

The command line can rise one picture, layer or roll into a glTF model, give variants depth from a
picture, snap soft edges, and read or set the grid a spritesheet carries. For what depth means, see
[Depth: paper, front and back](../concepts/depth.md).

## Make one model

```bash
nfty model sprite.png --out sprite.glb                       # flat paper
nfty model sprite.png --out sprite.glb --front 2 --back 1    # every solid pixel 4 voxels thick
nfty model hat.igt --variant red --out hat.gltf --format gltf
nfty model chest.rcp --seed launch --out chest.glb
```

The input picks the form:

| Input | Needs | Depth comes from |
|---|---|---|
| `.png` | Nothing else | Paper, unless `--front` and `--back` fill it |
| `.igt` | `--variant <id>` | The variant's stored depth (or `--front`/`--back`) |
| `.rcp` | `--seed <text>` | Each layer's stored depth, combined. The same roll `nfty preview x.rcp --seed` shows. |

| Option | Does |
|---|---|
| `--out` | The model to write. Required; its extension must match the format. |
| `--front N --back M` | Fill-all: every opaque pixel gets this depth (0 to 255), replacing stored depth. Both or neither; not on an `.rcp`. |
| `--format` | `glb` (default), `gltf` (writes a `.bin` beside it) or `gltf-embedded`. |
| `--pivot` | `bottom-center` (default), `center` or `min-corner`. |
| `--voxel-size` | World units per pixel, 0.001 to 1000 (default 1). |

A Dynamic or Static variant rises as its stored gray value-map; there is no color option, because
color is decided when an asset is cooked. A picture with a partly transparent pixel is refused, naming
it. To model every asset of a collection, use [`generate --models`](cook.md#write-3d-models-while-cooking).

## Give a variant depth from a picture

```bash
nfty set depth chest.igt --variant stone --map stone-depth.png
```

In the map, **red is the front depth and green the back depth**, 0 to 255 voxels each; blue and alpha
are ignored. The map must be exactly the variant's size. Depth over clear pixels is dropped, and an
all-zero map clears the variant back to flat paper. Other variants are untouched. This is how the demo
CookBook's relief is authored.

## Snap soft edges

```bash
nfty inspect book.cbk --voxel              # which variants carry partial alpha
nfty set voxel-ready book.cbk --dry-run    # what a snap would change
nfty set voxel-ready book.cbk              # snap: >= 50% alpha solid, below clear
```

## Read or set a spritesheet's grid

```bash
nfty sheet grid sheet.png                  # the embedded grid (or none) and the detected one
nfty sheet grid sheet.png --cols 8 --rows 4
nfty sheet grid sheet.png --cell 64x64
nfty sheet grid sheet.png --clear
```

Writing or clearing rewrites the PNG in place, keeping every pixel. `--cols` and `--rows` go together;
`--cell` is the other way to say the same grid. A grid that does not divide the picture is refused.
`nfty export --sheet` writes a sheet that already carries its grid. See
[Rise a spritesheet](../app/spritesheets.md).
