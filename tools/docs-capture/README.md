# Regenerating the manual's screenshots

Every figure in `docs/manual/images/` is a frame of the **running desktop app**, captured in both
themes, cropped to the part the page is talking about. This folder is how they are made again when
the UI moves.

They are not `VisualCapture` frames. That harness renders the shipped controls offscreen and is the
right tool for *testing* layout, but its fixtures are 8×8 solid fills — a manual built from them
shows empty panels and quotes numbers like "2 unique DNA", which teaches a reader the wrong thing
about what the app is for. These come from a real book with real art in it.

## The whole run

```bash
# 1. Draw the demo art and assemble a real CookBook, Kitchen and cooked Set.
python tools/docs-capture/draw-demo-art.py    .demo/art
python tools/docs-capture/build-demo-book.py  .demo/art  M:/nfty-demo

# 2. Launch the app and drive it to each screen by hand, capturing as you go.
dotnet run --project src/Nfty.Desktop
python tools/docs-capture/pair.py <name> <shots-dir>

# 3. Cut the frames into the figures the pages use.
python tools/docs-capture/crop.py <shots-dir> docs/manual/images
```

`M:/nfty-demo` is only what the current screenshots happen to show in their path labels and recent
lists. Any short, neutral folder works — but keep it short: it appears in the Landing screenshots.

## The pieces

| File | Does |
|---|---|
| `draw-demo-art.py` | Draws the 64×64 pixel art: three backgrounds, two cat bodies, a fox, three eyes, two auras, three hats. Gray value-maps for the Dynamic and Static layers, full color for the Custom hats. |
| `build-demo-book.py` | Writes the manifests, calls the CLI's authoring commands, and lays out a Kitchen with a packed Set beside it. |
| `shot.ps1` | Captures the nfty window to a PNG at exactly 1416×864, inset by the 12px shadow gutter. |
| `pair.py` | Captures the current screen in **both** themes and names each file by which one it actually is. |
| `label.py` | Re-checks a whole folder of pairs the same way. Run it after a batch. |
| `click.ps1` | Clicks a list of screen points, for replaying a path to a screen after a rebuild. |
| `crop.py` | The figure list: which frame each figure comes from and what box to cut. Edit this to add one. |

## Three traps, all of which cost real time

**`$h` is not `$H`.** PowerShell variables are case-insensitive, so assigning the window handle to
`$h` silently overwrote the `-Height` parameter and `MoveWindow` was called with a handle as its
height. `shot.ps1` uses `$hwnd`, and places the window in a **verify loop** — a single `MoveWindow`
issued while the window is still settling is accepted and then quietly undone.

**A blind Ctrl+T gives you the same theme twice.** The toggle is fire-and-forget; if the window loses
focus for a moment the keystroke is swallowed and you get a "pair" that is two dark frames named
dark and light. `pair.py` reads a titlebar pixel back and retries, and `label.py` re-checks a whole
folder. Never trust capture order.

**A modal swallows Ctrl+T entirely.** For a dialog, capture one theme, close it, toggle, and reopen —
`pair.py` cannot help there.

## Adding a figure

Add a row to `FIGURES` in `crop.py`: the frame it comes from, and the crop box in the frame's own
coordinates (or `None` for the whole window). Both themes are cut with the same box, because the two
frames are the same layout.

Then reference it from a page as a pair, and Material swaps them with the reader's theme:

```markdown
![What it shows](../images/thing-light.png#only-light)
![What it shows](../images/thing-dark.png#only-dark)
```

Do not set `display` on those images in `vaporsoft.css`. Material hides the off-theme half with a
selector of the same specificity, and this stylesheet loads after Material's — so a `display: block`
there wins a tie it should never have entered, and both images render, one under the other.
