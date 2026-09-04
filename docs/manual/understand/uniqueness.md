# How uniqueness is decided

nfty guarantees that no two assets in a Set are the same. This page is what that guarantee actually
means.

## An asset's fingerprint

Every asset gets a **DNA** -- a hash computed from:

- which **Recipe** it was rolled from
- which **Variant** was picked on each layer
- for Dynamic and Static layers, the **color** that layer ended up with

Two assets with the same DNA are the same asset, so when a roll produces a DNA that already exists,
nfty throws it away and rolls again.

## Colors are counted in buckets

A hue is a continuous number, so strictly speaking every roll produces a new color and every asset
would be trivially unique. That would be a worthless guarantee -- two assets one degree of hue apart
are the same asset to anyone looking at them.

So a layer's color is **quantized** before it reaches the fingerprint. The **QUANTIZE** setting says
how coarsely: a hue quantize of 30 divides the wheel into 30-degree buckets, and two rolls in the same
bucket count as the same color.

That is what makes uniqueness mean something. It is also the dial you turn when you need more of it --
see [Raise the unique-asset count](../how-to/raise-unique-count.md).

## The count is a promise

**UNIQUE DNA** on the CookBook panel is not an estimate. It is the number of legal, distinguishable
assets the book can produce: every combination of variants that the rules allow, multiplied by the
color buckets each layer can land in.

![The CookBook panel, showing the DNA space broken down per recipe](../images/cookbook-panel-light.png#only-light)
![The CookBook panel, showing the DNA space broken down per recipe](../images/cookbook-panel-dark.png#only-dark)

The DNA SPACE list shows the arithmetic per Recipe, so you can see which layer is the bottleneck.

Ask for more assets than that number and nfty refuses before it starts, and tells you the true
maximum. It will not quietly ship duplicates.

## Two different failures

They look similar and mean opposite things:

- **"allows exactly N unique assets, but M were requested"** -- the space is real, you asked for more
  than exists. Make the space bigger.
- **a rule conflict** -- the rules exclude *every* combination, so the space is empty. Remove or
  loosen a rule.

## Very large books

Above about a million unconstrained combinations, nfty stops enumerating and reports "more than N"
instead of an exact figure. The guarantee is unchanged; only the display is approximate, and a book
that big is not going to run out.
