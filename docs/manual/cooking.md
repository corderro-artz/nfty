# Cooking a Set

**Cook Set** on the CookBook panel. You give it:

- **How many** assets to make (50 by default).
- **A seed** — any word. It arrives pre-filled with a random one; replace it with something you will
  remember if you want to be able to reproduce this exact collection later. The same book and the
  same seed always produce the same collection.
- Optionally, **pack** the result into a single `.set` file rather than a folder.

The result is a Set: numbered PNGs, plus two data files per asset — a standard `metadata/NNNN.json`
that marketplaces understand, and a richer `nfty/NNNN.json` with the fingerprint, the seed, the
rarity, and the exact color each layer rolled.

## Extending later

You can grow an existing Set rather than regenerating it. nfty re-opens it, keeps every asset it
already made, adds new ones that do not collide, and recalculates rarity across the whole
collection — because rarity depends on the collection as a whole.

!!! note "It knows if the book has changed"

    If you have edited the CookBook since the Set was made, nfty warns you. The Set records which
    book made it, so it can tell.
