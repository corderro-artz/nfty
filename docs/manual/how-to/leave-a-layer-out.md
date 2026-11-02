# Leave a layer out sometimes

Give a layer an **absent chance**: the share of assets that skip it entirely. A hat that only one
asset in ten wears, a glow that usually is not there, a trim that makes a character a chase item.

## Why not just add an empty variant

A transparent variant looks the same, but it costs you. On a Dynamic layer it still rolls a color,
so two assets that both show nothing can have *different* DNA, and the unique DNA figure inflates for no
visible reason. An absent layer rolls no color and contributes nothing to the DNA: two assets that
both leave it out agree about it.

The chance lives on the **Recipe**, not on the layer, because one layer (`.igt`) can sit in several
Recipes. The same hat can be common on one character and rare on another.

## In the app

1. Unlock editing (click the **read-only** chip in the title bar).
2. Select a Recipe in the tree.
3. In the **LAYERS** header, click **OPTIONAL LAYERS**. An **ABSENT** column appears.
4. Type a **percent** into the layer's row and leave the field (Tab or Enter). `0` means the layer
   always appears. `100` means it never does: the layer is shelved, not deleted, the same meaning a
   Recipe weight of `0` carries.

Turning **OPTIONAL LAYERS** off asks first, then clears every chance in the Recipe.

On the layer's own panel, an **absent 72%** flag appears. The shares in its variant table already
account for it, so they are the odds of getting each variant, not the share among its siblings: a
variant that is 1 in 20 of the times the layer shows, on a layer shown a tenth of the time, reads as
well under 1%.

## From the command line

```bash
nfty set chance cat.rcp --id hat --absent 90
nfty set chance cat.rcp --id hat --absent 0      # always present again
nfty stats book.cbk                              # prints each chance, and what it does to the odds
```

`--id` is the layer's id, not its display name; `nfty inspect cat.rcp` lists them.

## What it does to the numbers

- **The seed still reproduces.** The absent outcome is drawn ahead of the layer's variants and never
  mixed into their weights. A book that uses no absent chance rolls byte-identically to how it did
  before the feature existed.
- **Unique DNA changes.** An optional layer adds one shape (absent) beside its present shapes, so the
  count is a sum rather than a plain product. The CookBook panel and `nfty stats` show the new
  figure.
- **Rules still apply.** A rule fires only when its trigger layer is present with the named variant. An
  absent layer cannot trigger a rule, and an *Exclude* target that is absent is never a violation. A
  *Require* target that is absent **is**: the roll is rejected and rerolled.

## See also

- [Make one trait rarer](rarer-trait.md)
- [How uniqueness is decided](../understand/uniqueness.md)
- [Command line](../reference/cli.md)
