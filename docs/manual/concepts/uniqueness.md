# How uniqueness is decided

By default nfty rejects repeated DNA within a Set. This page explains what DNA records, how the
**unique DNA** figure is counted, and how to allow repeats.

## An asset's fingerprint

Every asset gets a **DNA**: a SHA-256 hash of

- the **Recipe** it was rolled from,
- the **Variant** picked on each layer that is present, and
- for Dynamic and Static layers, the **color** that layer ended up with, quantized (below).

A layer left out by its absent chance contributes nothing, so two assets that both skip it agree
about it. Paint order and depth never enter the hash. When a roll produces a DNA the Set already
has, nfty throws the roll away and rolls again.

This is a guarantee about fingerprints, not visually distinct pixels. Two different variants can
contain identical drawings, and a layer hidden by another can still contribute to DNA.

## Colors are counted in buckets

A hue can take many finely spaced values. Treating each as a separate identity would give little
control over visible variety: two assets one degree of hue apart can look alike.

So each colored layer's color is **quantized** before it reaches the DNA. The layer's **QUANTIZE**
setting says how coarsely: a hue step of 30 divides the wheel into 30-degree buckets, and two rolls
in one bucket count as the same color. Coarser steps mean fewer, more distinct colors; finer steps
mean more.

## The count is a promise

**UNIQUE DNA** on the CookBook panel (and the last line of `nfty stats`) counts legal DNA values:
every variant combination the rules allow, times the
color buckets each colored layer can land in, with each optional layer adding its "absent" case.

![The CookBook panel, showing the DNA space broken down per recipe](../images/cookbook-panel-light.png#only-light)
![The CookBook panel, showing the DNA space broken down per recipe](../images/cookbook-panel-dark.png#only-dark)

The **DNA SPACE** list shows the figure per Recipe. An exact count describes the legal DNA space;
the generator still samples it with a bounded retry budget. Asking for more than the remaining
space cannot succeed, and a heavily weighted book can also reach its retry limit before finding
every available DNA. A failed fresh cook leaves no completed Set, and unique mode never accepts a
duplicate to fill the request.

## When the figure is a bound, not a count

Some books count exactly; others can only be bounded or reported as unknown. The wording says which:

| Wording | Means |
|---|---|
| `8,745,000` | Exact. |
| `more than N` | A floor: a color set was too large to enumerate, so N under-counts. |
| `at most N` | A ceiling: a ruled Recipe has too many combinations to walk, so N is the count before rules removed any. |
| `cannot be counted` | Both happened (the figure bounds the truth in neither direction), or the book has problems; run `nfty validate`. |

Large figures print in full when they fit, then as a named magnitude (`2.46 billion`), then with an
exponent (`2.18e21`). The exact digits are on the figure's tooltip.

## Reading a failed cook

The message distinguishes what is known:

- **"...allows exactly N unique DNA, but M were requested."** N is the book's exact DNA count.
  Compare the request and any existing assets with that space. If enough unused DNA should remain,
  sampling may have reached its retry limit instead. See
  [Cooking](../app/cooking.md#if-the-book-cannot-make-enough).
- **A rule conflict.** The rules exclude *every* combination, so the space is empty. Remove or
  loosen a rule.
- **A reroll-budget failure with a bound or unknown space.** The attempts did not find another
  acceptable roll; that alone does not prove the space is exhausted. Consider weights, rules and
  the request before raising the budget.

## Allowing repeats

The guarantee has a cost: a small book cannot mint a large collection. Many collections are minted
the other way. Tick **Allow repeats** in the Cook dialog (or pass `--unlimited`) to accept repeated
DNA and remove the uniqueness limit. Validation, rules and the rule-retry budget still apply.

Nothing else changes. Rules are still enforced, and **the weights still decide how rare each asset
is**: a low-weight variant stays uncommon, and a low-weight variant on a rarely present layer is
rarer still. You give up the promise that two assets have different DNA. Identity becomes the
asset's **number**, as ERC-721 defines it.

`set.json` records which mode made a Set (`"uniqueDna": true` or `false`). The same book and seed
can give different collections in the two modes, because rejecting a duplicate consumes a roll the
other mode keeps. Reproducing a run requires the same book, seed and generation options, including
this mode.

## See also

- [Rarity and weights](rarity.md)
- [Why a seed reproduces a collection](seeds.md)
