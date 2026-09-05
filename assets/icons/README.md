# Icon sources

The 45 glyphs the app draws, as SVG. **These are the drawings.**
`src/Nfty.App/Themes/Icons.axaml` is generated from them:

```bash
python tools/icons/build.py
```

Edit an SVG, run that, commit both. `IconSourceTests` fails the build if the two disagree, so the
generated half can never quietly become the source — which is the failure mode of every "source of
truth" that nothing checks.

## The rules a glyph follows

- **One 24×24 box, always.** `Path.ico` maps that shared box onto the 12/13/14/18px icon sizes the
  way an `<svg viewBox>` does, so every glyph is scaled by one factor rather than by its own bounds.
  A drawing in a different box comes out the wrong size with nothing to say why.
- **One `<path>`.** A `StreamGeometry` is a single geometry; join subpaths with `M` rather than
  adding a second element. The build script refuses anything else.
- **Stroked, never filled** — `stroke-width="2"`, round caps and joins. The app supplies the ink from
  the theme, which is why one set serves both light and dark: there is no second set to keep in step.
- **It has to read at 12px.** Most of these are drawn at 12 or 14. A glyph that only works at 40 is
  not finished.
- **It has to differ from its neighbours.** Four of these were redrawn because they did not:
  `select` was a rounded square beside the `rect` tool's rounded rectangle; `import` was an
  arrow-into-a-tray beside `download`'s arrow-into-a-tray; `kitchen` was a box with a header bar and
  two hanging tabs, which is a calendar; and `fill` was a bare diamond that never read as a bucket
  until it was given its handle.

`tools/icons/extract.py` is the one-shot that seeded this directory out of the old hand-written
`Icons.axaml`. It has done its job and is kept only as the record of where these came from.
