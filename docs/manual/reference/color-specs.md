# Color specs

Everywhere you type a color (a Static layer's fixed color, a book's palette, `preview --color`), nfty
takes a **color spec**: a prefix, a colon, and a value. The prefix is **required**. A missing or
unknown prefix is an error, never a guess, because guessing between `hex:` and `hsv:` silently
produces the wrong art.

| Prefix | Value | Example |
|---|---|---|
| `hex:` | Exactly six hex digits, `rrggbb`. Case does not matter. | `hex:d6249f` |
| `rgb:` | Red, green, blue, 0 to 255 each. | `rgb:214,36,159` |
| `hsl:` | Hue 0 to 360, saturation and lightness 0 to 100. | `hsl:322,72,49` |
| `hsv:` | Hue 0 to 360, saturation and value 0 to 100. | `hsv:322,83,84` |

All four above are the same color. nfty writes colors back as `hex:` (lower case), the one form that
survives a round trip exactly.

## What a colored layer takes from it

A Dynamic or Static layer is a gray value-map: lightness comes from the drawing. From a color spec it
takes only the **hue and saturation**. That is why an 8-digit `hex:rrggbbaa` is refused: the alpha pair
would be parsed and then discarded.

## Errors

| Typed | Error |
|---|---|
| `d6249f` | missing a prefix (`hex:`/`rgb:`/`hsl:`/`hsv:`) |
| `#d6249f` | missing a prefix |
| `hue:200` | unknown color prefix |
| `hex:d6249` | must be exactly 6 hex digits |
| `hex:d6249fff` | has 8 digits (rrggbbaa); use 6-digit rrggbb |
| `rgb:10,20` | expected 3 comma-separated values |
