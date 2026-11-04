# Why a seed reproduces a collection

Cooking takes a **seed**: any word. The same CookBook and the same seed always produce exactly the
same collection: the same assets, in the same order, with the same colors, byte for byte. This holds
across machines, operating systems, locales, CPU architectures and thread counts, and the test suite
enforces it.

## What that buys you

- **The output is disposable.** The book plus the seed is the artifact. Delete a Set and cook it
  again identically.
- **A collection fits in a sentence.** "Vapor Pets, seed `launch`, 500 assets" describes five hundred
  images completely.
- **Exploring is safe.** Don't like a roll? Change one character of the seed and get an entirely
  different collection from the same art.

## What must stay the same

The guarantee is *same book, same seed, same mode*. "Mode" is whether repeats are allowed, recorded
as `uniqueDna` in `set.json` (see [How uniqueness is decided](uniqueness.md#allowing-repeats)).

| You change | The collection |
|---|---|
| A variant's pixels | **Same identities.** The same variants are chosen; they just look different. |
| Depth (Rise) | **Same identities.** Depth never enters the roll or the DNA. |
| Book name, symbol, description, target supply | **Same.** None of them is read by the roll. |
| Recipe listing order | **Same.** Recipes are rolled in id order whatever the listing shows. |
| A weight, a range, a quantize step, an absent chance | **Different.** What a random number maps to has changed. |
| Adding, removing or **reordering** a layer | **Different**, broadly. See below. |

## Why reordering changes everything

nfty draws one random number per layer, walking the stack in paint order. Move a layer and you move
*which draw reaches it*, so on any layer with more than one variant the selection itself changes,
and every layer after it shifts too. The result is a different collection, not the same one
restacked. Think twice before reordering a book you have already cooked and shipped.

## nfty notices when the book changed

A Set records a SHA-256 of the CookBook that produced it (`cookbookSha256` in `set.json`). When you
[extend](../cli/cook.md#extend-a-set) a Set, nfty compares that hash with the book you give it and
**warns** if they differ. It warns rather than refuses, because extending an edited book is
sometimes exactly what you meant.

!!! note "A random seed is offered, not imposed"

    The Cook dialog arrives with a random seed filled in, so you are never blocked on inventing one.
    Replace it with something memorable whenever the collection matters.
