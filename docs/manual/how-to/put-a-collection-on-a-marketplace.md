# Put a collection on a marketplace

A cooked Set cannot go straight to OpenSea, and that is deliberate: it records files by the paths
inside the Set (`images/0001.png`), and a marketplace needs **addresses** it can fetch
(`ipfs://<CID>/images/1.png`), file names the contract can compute (`1`, not `0001.json`), and a
collection file for the contract itself. The **Marketplace** tab of the export card, and the matching
`nfty export` options, write all of that into the *export*. Your cooked Set is never changed.

Open the Set, press **Export…**, choose the **Marketplace** preset, and open the **MARKETPLACE** tab.

![The Export card's Marketplace tab, filled in as a seller would](../images/export-marketplace-light.png#only-light)
![The Export card's Marketplace tab, filled in as a seller would](../images/export-marketplace-dark.png#only-dark)

```bash
nfty export ./collection --out ./dist --preset marketplace \
    --base-uri ipfs://<CID>/ \
    --token-names token-id --start-id 1 \
    --external-url https://example.com/chest/ \
    --external-link https://example.com/ \
    --royalty 5 --fee-recipient 0x1234567890abcdef1234567890abcdef12345678
```

## Make the image links absolute

Upload the exported `images/` folder, get its address, and give it as the **base URI**:
`ipfs://<CID>/`. Every `image` becomes `ipfs://<CID>/images/1.png`, and a model's `animation_url`
(below) uses the same prefix. The address must be absolute (`ipfs`, `https`, `http` or `ar`) and must
**end with a slash**; nfty refuses anything else and says which rule it broke, rather than guessing
what you meant.

Leave it empty and the links stay relative. The card and the terminal both warn you, because a
marketplace cannot resolve `images/0001.png`:

> Image links are relative; set a base URI before uploading.

## Name the files the way your contract counts

A contract builds a token's address as `baseURI + tokenId`. Choose **Token id** (`--token-names
token-id`) and the files are named by that id alone: `metadata/1` with **no extension**, `images/1.png`,
`models/1.glb`. The `#n` in each item's `name` and every link follow. **First token id** (`--start-id`)
is `1` (the default) or `0`, as your contract counts: the id is the asset's number minus one plus
that start.

The default, **Padded**, is exactly what nfty has always written (`0001.json`, `0001.png`), byte for
byte. `nfty/` keeps the Set's own names and numbering whichever you pick; it is nfty's record of the
Set, cross-referenced by set number, and not what a marketplace reads.

An export that keeps `set.json` records how it named its files (a `files` block with the naming and the
first id), so nfty reopens it as a full Set whatever you chose, and exporting it again renames the files
for the new request. You cannot **extend** such an export: it would mix names. Extend the source Set,
then export again.

Models cooked as `.gltf` plus a separate `.bin` are renamed as a pair: nfty rewrites the one link inside
the `.gltf` that names its `.bin`, and nothing else in the file changes.

## Describe the collection for the contract

Tick **collection.json** (on in the Marketplace preset) and nfty writes OpenSea's `contractURI` file:

```json
{
  "name": "Chest Demo",
  "description": "A demo collection that ships with nfty: layered chests to open, edit and cook. Nothing here is precious - break it and rebuild it.",
  "image": "ipfs://<CID>/images/1.png",
  "external_link": "https://example.com/",
  "seller_fee_basis_points": 500,
  "fee_recipient": "0x1234567890abcdef1234567890abcdef12345678",
  "symbol": "CHST"
}
```

The name, description and symbol come from the Set. **External link** is your site; **Royalty** is a
percentage from 0 to 100 with at most two decimals (it becomes whole basis points, 2.5% is `250`) and
needs a **Fee recipient**, `0x` and 40 hexadecimal digits. The `image` is the first token's image
when a base URI is set and the art is included, unless you give one. A field left empty is left out
of the file.

## Link each item back to you

Give **External URL** an address and each item gets an `external_url` of that address followed by its
token id. When the Set was cooked with 3D models in `glb`, the base URI is set and models are being
exported, each item also gets an `animation_url` pointing at its `models/<id>.glb`; models in other
formats are not linked, and the card says so.

## Keep the seed to yourself

`set.json` records the seed and the hash of the book. The Marketplace preset turns **set.json** off so
a public drop does not publish them. An export without it cannot be sealed, and it is not a Set: it
records no seed, rarity or DNA. Every other preset keeps it.

## Check the bundle before you upload it

nfty still opens a bundle without `set.json`, read only, so you can look at exactly what you are about
to upload. Start nfty on the folder (`nfty <folder>`) or open a packed `.set` with **Open a cooked .set**, and it
opens in the viewer's gallery marked **upload bundle, not a Set**, with one tile per `metadata/`
item. A tile shows the picture, the name and every trait; the rail shows what `collection.json` says
(name, symbol, royalty, payee, links). Pictures are found inside the bundle: a relative link is a path
in it, and an absolute `ipfs://` or `https://` link is matched by its trailing `images/<file>` onto
the bundle's own `images/` folder. Nothing is fetched from the network. A picture the bundle does not
carry shows the missing mark. From the terminal, `nfty inspect <folder>` prints the same check: item
count, pictures found, `collection.json` and every trait with its values.

A folder counts as a bundle only when it has no `set.json`, has a `metadata/` folder, and the first
file in it is an item with a `name`, an `image` and an `attributes` list. Any other folder is left alone.

## What a finished item looks like

This is `metadata/1` exported from the demo CookBook with the options above:

```json
{
  "name": "Chest Demo #1",
  "description": "A demo collection that ships with nfty: layered chests to open, edit and cook. Nothing here is precious - break it and rebuild it.",
  "image": "ipfs://<CID>/images/1.png",
  "external_url": "https://example.com/chest/1",
  "attributes": [
    { "trait_type": "Type", "value": "Chest" },
    { "trait_type": "Feet", "value": "Skids" },
    { "trait_type": "Body", "value": "Plated" },
    { "trait_type": "Body Color", "value": "Orange" },
    { "trait_type": "Bands", "value": "Straps" },
    { "trait_type": "Bands Color", "value": "Gray" },
    { "trait_type": "Lock", "value": "Latch" },
    { "trait_type": "Lock Color", "value": "Yellow" },
    { "trait_type": "Glow", "value": "None" },
    { "trait_type": "Trim", "value": "None" }
  ]
}
```

The `... Color` traits and `None` are in the cooked Set itself; see
[Color traits and None](../reference/set-contents.md#color-traits-and-none).

## Before you upload

The panel at the bottom of the card, and the terminal, name every file going and every warning:
relative links, metadata whose images are not in the export (the links would dangle), a collection
with no image. An export with nothing in it is refused.

nfty does not write `display_type` or numeric traits: its traits are categories (a layer and the
variant it rolled), not numbers, so there is nothing to say "level 5 of 10" about.

See also: [Share a collection with somebody](share-a-collection.md) for the other presets, and the
[command line reference](../reference/cli.md#publishing).
