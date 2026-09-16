# Diagrams

`docs/diagrams/*.mmd` is the source. The `-light.svg` / `-dark.svg` beside each one are generated,
committed, and shown in the README.

```bash
python tools/diagrams/build.py
```

It serves a page, prints a URL, and writes both SVGs when a browser renders it. Mermaid needs a
browser either way; mermaid-cli would drag puppeteer and a second Chromium in to supply one.

`DiagramSourceTests` fails the build if a rendered SVG stops carrying a word the `.mmd` says, so the
"Source:" caption under the README figure stays true. Edit the `.mmd`, run the script, commit all
three.

## Two traps, both of which shipped

**Mermaid returns HTML-serialized markup.** A `<br/>` in a label comes back as a bare `<br>`, and a
`.svg` is parsed as XML — so that one unclosed tag is a fatal parse error and the whole diagram
renders as a broken-image icon. The script re-serializes through `XMLSerializer`.

**An `<img>` needs an intrinsic size.** Mermaid's default is `width="100%"` with the real size in a
`style` max-width, which gives an image element nothing to lay out against. The script writes the
viewBox's own numbers onto `width` and `height`.

A third is worth knowing even though nothing here hits it: an SVG loaded as an `<img>` renders in
secure static mode, so it cannot fetch anything — **no webfont**. The font stack names IBM Plex Sans
for readers who have it and falls back to the system sans for everyone else.

## Palette

The theme variables in `build.py` are `Tokens.axaml`'s own values, the way
`docs/manual/stylesheets/vaporsoft.css` carries them for the manual. Mermaid's stock theme is a
lavender-and-purple set that belongs to no part of this product. Mermaid also derives the swatches
you do not state, and left to itself it produced `hsl(-87, 4%, 85%)` here — a negative hue, which is
not a color — so the secondary and tertiary slots are stated rather than inferred.
