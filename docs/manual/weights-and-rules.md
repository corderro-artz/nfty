# Weights, rules and how many you can make

## Weights

Weights decide how often something is picked. A variant with weight 3 is picked three times as often
as one with weight 1. Weight 0 shelves a variant without deleting it.

Recipes have weights too — that is how you make one character type rarer than another.

## Rules

Rules stop combinations that should not happen — a hat that clashes with certain hair, say. A rule
says "when this variant is picked, exclude that one". Set them on the Recipe's panel.

## Unique DNA

**UNIQUE DNA** on the CookBook panel is the number that matters before you cook. It is how many
genuinely distinct assets this book can produce, counting both the variant combinations *and* the
distinguishable colors a Dynamic layer can roll.

If you ask for more assets than that number, nfty refuses and tells you the real maximum, rather than
producing duplicates.

To raise it:

- add variants
- widen a hue range
- raise a layer's **quantize** steps — the "≈ N colors" readout on the colorize panel shows how many
  distinct colors the current settings admit
