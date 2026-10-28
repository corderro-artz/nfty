# Raise the unique-asset count

**UNIQUE DNA** on the CookBook panel is how many genuinely distinct assets the book can produce. Ask
for more than that and nfty refuses, and tells you the real maximum, rather than shipping duplicates
you did not ask for.

Four ways to raise it, cheapest first -- and a fifth option at the end, which is to stop needing it.

## 1. Widen a hue range

On a Dynamic layer, open the editor and drag the **HUE RANGE** handles apart:

![The colorize rail, with hue range, saturation range and quantize](../images/colorize-rail-light.png#only-light)
![The colorize rail, with hue range, saturation range and quantize](../images/colorize-rail-dark.png#only-dark)

A wider range means more distinguishable colors, and the count is multiplied by them. This costs no
drawing at all.

## 2. Make the quantize steps finer

**QUANTIZE** decides how finely nfty divides that range into colors it treats as *different*. The
approximate-colors readout beside it shows how many the current settings admit.

Smaller quantize numbers mean finer steps, and more distinct colors.

## 3. Add a variant

One more option on a layer multiplies the whole combination count. This is the expensive one -- it
means drawing -- but it is also the one that makes the collection look bigger rather than merely
count bigger.

## 4. Loosen a rule

Every [exclusion rule](exclude-combinations.md) removes combinations. If a book is short by a little,
a rule you added early and no longer need may be the reason.

!!! note "Widening colors and adding art are not the same win"

    Color range multiplies the *count*. Variants multiply the *visible variety*. A book with two
    drawings and a huge hue range can produce a million distinct assets that all look like the same
    two drawings. Push both.

## Or: allow repeats instead

None of the above is compulsory. If what you want is a large collection from a small book -- which is
how a great many collections are minted -- tick **Allow repeats** in the Cook dialog and the limit
stops applying: every roll is kept, and any count is producible.

You keep the rarity. The weights still decide how common each variant is, and a layer with a low
appearance chance is still rare. What you give up is the guarantee that no two assets are identical.

See [How uniqueness is decided](../understand/uniqueness.md) for what that trade actually costs.
