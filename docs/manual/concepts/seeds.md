# Why a seed reproduces a collection

Cooking takes a **seed**: any word. The same CookBook, seed and generation options produce exactly the
same collection: the same assets, in the same order, with the same colors, byte for byte. This holds
across machines, operating systems, locales, CPU architectures and thread counts, and the test suite
enforces it.

## What that buys you

- **The output is disposable.** The book plus the seed is the artifact. Delete a Set and cook it
  again identically.
- **A collection fits in a sentence.** "Vapor Pets, seed `launch`, 500 assets" describes five hundred
  images completely.
- **Exploring is safe.** Don't like a roll? Change the seed to try another seeded run from the same
  art. A small space can still produce matching selections or pixels.

## What must stay the same

The guarantee requires the same book, seed and options, including count, Recipe restriction and
whether repeats are allowed (`uniqueDna` in `set.json`; see
[How uniqueness is decided](uniqueness.md#allowing-repeats)). Model and export options also affect
the files written. Thread count does not.

The table below describes the roll's selections and identities, rather than promising that every
output byte survives an edit. Metadata, the recorded book hash, pixels and models can change.

| You change | The collection |
|---|---|
| A variant's pixels | **Same identities.** The same variants are chosen; they just look different. |
| Depth (Rise) | **Same identities.** Depth never enters the roll or the DNA. |
| Book name, symbol, description, target supply | **Same identities.** None is read by the roll; published metadata or the book hash can change. |
| Recipe listing order | **Same identities.** Recipes are rolled in id order; the recorded book hash can change. |
| A weight, a range, a quantize step, an absent chance | **Different.** What a random number maps to has changed. |
| Adding, removing or **reordering** a layer | **Different**, broadly. See below. |

## Why layer order affects rolls

nfty consumes random draws while walking the stack in paint order. A present Dynamic layer also
draws its color; a layer that is always absent can consume no draw. Moving a layer changes which
draws can reach which choices, so the resulting selections and DNA can change. Treat a reordered
book as a different collection rather than assuming it is the old one restacked.

## nfty notices when the book changed

A Set records a SHA-256 of the CookBook that produced it (`cookbookSha256` in `set.json`). When you
[extend](../cli/cook.md#extend-a-set) a Set, nfty compares that hash with the book you give it and
**warns** if they differ. It warns rather than refuses, because extending an edited book is
sometimes exactly what you meant.

!!! note "A random seed is offered, not imposed"

    The Cook dialog arrives with a random seed filled in, so you are never blocked on inventing one.
    Replace it with something memorable whenever the collection matters.
