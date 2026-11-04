# How uniqueness is decided

By default nfty guarantees that no two assets in a Set are the same. This page explains what "the
same" means, how the **unique DNA** figure is counted, and how to switch the guarantee off.

## An asset's fingerprint

Every asset gets a **DNA**: a SHA-256 hash of

- the **Recipe** it was rolled from,
- the **Variant** picked on each layer that is present, and
- for Dynamic and Static layers, the **color** that layer ended up with, quantized (below).

A layer left out by its absent chance contributes nothing, so two assets that both skip it agree
about it. Paint order and depth never enter the hash. When a roll produces a DNA the Set already
has, nfty throws the roll away and rolls again.

## Colors are counted in buckets

A hue is a continuous number, so strictly every roll would be a new color and every asset trivially
unique. That would be a worthless guarantee: two assets one degree of hue apart look identical.

So each colored layer's color is **quantized** before it reaches the DNA. The layer's **QUANTIZE**
setting says how coarsely: a hue step of 30 divides the wheel into 30-degree buckets, and two rolls
in one bucket count as the same color. Coarser steps mean fewer, more distinct colors; finer steps
mean more.

## The count is a promise

**UNIQUE DNA** on the CookBook panel (and the last line of `nfty stats`) is the number of legal,
distinguishable assets the book can produce: every variant combination the rules allow, times the
color buckets each colored layer can land in, with each optional layer adding its "absent" case.

![The CookBook panel, showing the DNA space broken down per recipe](../images/cookbook-panel-light.png#only-light)
![The CookBook panel, showing the DNA space broken down per recipe](../images/cookbook-panel-dark.png#only-dark)

The **DNA SPACE** list shows the figure per Recipe, so you can see which one is the bottleneck. Ask
for more assets than the total and nfty refuses before it starts and states the true maximum. It
never ships duplicates you did not ask for.

## When the figure is a bound, not a count

Nearly every book counts exactly. A few can only be bounded, and the wording says which direction:

| Wording | Means |
|---|---|
| `7,155,000` | Exact. |
| `more than N` | A floor: a color set was too large to enumerate, so N under-counts. |
| `at most N` | A ceiling: a ruled Recipe has too many combinations to walk, so N is the count before rules removed any. |
| `cannot be counted` | Both happened (the figure bounds the truth in neither direction), or the book has problems; run `nfty validate`. |

Large figures print in full when they fit, then as a named magnitude (`2.46 billion`), then with an
exponent (`2.18e21`). The exact digits are on the figure's tooltip.

## Two different failures

They look alike and mean opposite things:

- **"...allows exactly N unique DNA, but M were requested."** The space is real and smaller than
  what you asked for. Make it bigger: see [Cooking](../app/cooking.md#if-the-book-cannot-make-enough).
- **A rule conflict.** The rules exclude *every* combination, so the space is empty. Remove or
  loosen a rule.

## Allowing repeats

The guarantee has a cost: a small book cannot mint a large collection. Many collections are minted
the other way. Tick **Allow repeats** in the Cook dialog (or pass `--unlimited`) and every roll is
kept, so any count is producible from any book.

Nothing else changes. Rules are still enforced, and **the weights still decide how rare each asset
is**: a low-weight variant stays uncommon, and a low-weight variant on a rarely present layer is
rarer still. You give up only the promise that two assets are never identical. Identity becomes the
asset's **number**, as ERC-721 defines it.

`set.json` records which mode made a Set (`"uniqueDna": true` or `false`). The same book and seed
give *different* collections in the two modes, because rejecting a duplicate consumes a roll the
other mode never spends. The seed and this field together reproduce a run.

## See also

- [Rarity and weights](rarity.md)
- [Why a seed reproduces a collection](seeds.md)
