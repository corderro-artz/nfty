# Publishing

A cook writes **your** copy of a collection, holding everything. **Export** writes somebody else's
copy, and who that somebody is decides what goes in it. Your cooked Set is never changed by an export.

## Which page you need

| You want to | Page |
|---|---|
| Hand a collection to a buyer, a collaborator, or keep a backup | [Export a Set](export.md) |
| List it on OpenSea or mint it from a contract | [Prepare marketplace metadata](marketplace.md), then [upload to IPFS](ipfs.md) |
| Upload the art and metadata to IPFS and get your contract's `baseURI` | [Upload to IPFS](ipfs.md) |
| Send it for review without handing over the files | [Send a sealed copy for critique](sealed-critique.md) |
| Let a game or server roll assets at runtime instead | [Menus and tickets](../embedding/menus-and-tickets.md) |

## The four presets

| Preset | For | Art | Marketplace metadata | nfty metadata | `set.json` | CookBook | Sealed |
|---|---|:--:|:--:|:--:|:--:|:--:|:--:|
| **Marketplace** | A public listing or contract drop | ✓ | ✓ (+ `collection.json`) | – | – | – | – |
| **Asset pack** | A buyer of the collection | ✓ | ✓ | ✓ | ✓ | – | – |
| **Full project** | A collaborator, or your backup | ✓ | ✓ | ✓ | ✓ | ✓ | – |
| **Sealed critique** | A reviewer | ✓ | ✓ | ✓ | ✓ | – | ✓ |

3D models, when the Set has them, are included in every preset. A preset is a starting point: every
box stays yours to change, and the card stops naming a preset once the contents differ from it.

## A typical marketplace drop

1. [Cook](../app/cooking.md) the collection, with models if you want a 3D viewer on the listing.
2. Open the Set, press **Export…**, choose **Marketplace**, and fill in the
   [MARKETPLACE tab](marketplace.md): token-id file names, royalty, links.
3. On the **UPLOAD** tab press **Upload**: nfty pins the art, writes the metadata with the real
   `ipfs://` addresses, pins that, and keeps a byte-identical copy. See [Upload to IPFS](ipfs.md).
4. Copy the **Base URI** and **Contract URI** into your contract. nfty does not deploy contracts or
   mint.
