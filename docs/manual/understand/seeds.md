# Why a seed reproduces a collection

Cooking takes a **seed** -- any word. The same CookBook and the same seed always produce exactly the
same collection: the same assets, in the same order, with the same colors, byte for byte.

This holds across machines, across operating systems, across locales, and across CPU architectures.
It is enforced by the test suite, not hoped for.

## Why you should care

**You can throw the output away.** The Set is not the artifact -- the book plus the seed is. You can
delete a collection and cook it again identically, which makes the output folder disposable rather
than precious.

**You can hand someone a reproduction.** "Vapor Pets, seed `launch`" is a complete description of
five hundred images.

**You can explore safely.** Try a seed, look at the results, and if you do not like the roll, change
one character and get an entirely different collection out of the same art.

## What breaks it

The guarantee is *same book, same seed*. Change the book and the guarantee no longer applies -- and
the changes that break it are broader than people expect.

**Adding, removing or reordering a layer changes everything.** nfty draws one random number per layer,
walking the stack in paint order. Move a layer and you move which draw reaches it, so on any layer
with more than one variant the selection itself changes -- and every layer after it shifts too.

The result is a different collection, not the same collection rearranged. This surprises people, so
it is worth internalising before you reorder a book you have already cooked from.

**Changing weights, ranges or quantize changes the rolls too.** Anything that changes what a random
number *maps to* changes the outcome.

**Editing a variant's pixels does not.** The same variant is still chosen; it just looks different.
The identities survive.

## nfty knows when the book has changed

A Set records a fingerprint of the CookBook that produced it. When you
[extend a Set](../how-to/more-assets.md), nfty compares them and warns you if they differ. It warns
rather than refuses, because extending a changed book is sometimes exactly what you meant.

!!! note "A random seed is offered, not imposed"

    The Cook dialog arrives with a random seed already in it, so you are never blocked on inventing
    one. Replace it with something memorable whenever the collection matters.
