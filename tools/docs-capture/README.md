# Regenerating the manual's screenshots

Every figure in `docs/manual/images/` is a frame of the **running desktop app**, captured in both
themes, cropped to the part the page is talking about. This folder is how they are made again when
the UI moves.

They are not `VisualCapture` frames. That harness renders the shipped controls offscreen and is the
right tool for *testing* layout, but its fixtures are 8×8 solid fills — a manual built from them
shows empty panels and quotes numbers like "2 unique DNA", which teaches a reader the wrong thing
about what the app is for. These come from a real book with real art in it — and since the demo became a shipped feature, from
**the** book: `tools/demo/build-demo.py` builds the same `ChestDemo.cbk` that is embedded in
`Nfty.Core`. The manual used to be screenshotted from a second demo (a pet collection) that existed
only in this folder, so every figure showed a CookBook no reader could open.

## The whole run

```bash
# 1. Build the demo workspace: the SHIPPED demo CookBook, plus a Kitchen, loose parts and a
#    cooked Set beside it. The book itself lives in tools/demo/ and is the one embedded in the
#    app, so the manual shows the book a reader actually has.
python tools/demo/build-demo.py --workspace M:/nfty-demo

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
| `../demo/draw-chest-art.py` | Draws the 32×32 chest sprites — bodies, bands, locks, trim, glow. Gray value-maps for the Dynamic and Static layers, full color for the Custom trim. |
| `../demo/build-demo.py` | Assembles the demo CookBook that ships inside the app, and with `--workspace` lays out a Kitchen with a packed Set beside it. |
| `shot.ps1` | Captures the nfty window to a PNG at the standard size below, inset by the 12px shadow gutter. |
| `pair.py` | Captures the current screen in **both** themes and names each file by which one it actually is. |
| `label.py` | Re-checks a whole folder of pairs the same way. Run it after a batch. |
| `click.ps1` | Clicks a list of screen points, for replaying a path to a screen after a rebuild. |
| `crop.py` | The figure list: which frame each figure comes from and what box to cut. Edit this to add one. |

## The capture size

**1416x950.** The height moved up from 864 when the window minimum became 924, which is set by the
quick-reference sheet — the largest modal. A shorter capture is a size the app will not open at.

The sheet is the one exception and needs its own taller run, because it is larger than what fits
inside a minimum window's page area:

```bash
python tools/docs-capture/pair.py help <shots-dir> 1010
```

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
