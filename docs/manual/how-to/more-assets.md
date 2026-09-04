# Add more assets to a finished set

You shipped 500 and want 750. You do not regenerate -- you **extend**, which keeps every asset you
already made.

```bash
nfty extend mybook.cbk ./collection --to 750
```

nfty re-opens the Set, reads the assets and numbering it already has, rolls only new ones that do not
collide with them, and continues the numbering.

## Rarity is recalculated

Rarity is a property of the *whole* collection -- "3% of assets have this hat" changes the moment the
collection grows. So extend rewrites the `rarity` field on the existing items too. Their images and
their identities do not change.

## It knows if the book has changed

A Set records which CookBook produced it. If you have edited the book since, extend **warns you and
continues** -- it does not refuse, because sometimes that is exactly what you meant.

Take the warning seriously though: a book that has gained a layer or been reordered rolls
differently, so the new assets will not be siblings of the old ones in the way you might expect. See
[Why a seed reproduces a collection](../understand/seeds.md).

!!! note "Extend is not a second generator"

    It is the same generation run with the existing fingerprints and the starting number handed to
    it. Anything true of cooking is true of extending.
