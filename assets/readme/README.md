# The README's hero frame

`hero-light.png` / `hero-dark.png` — one pair, used by the repository README and by nothing else.
GitHub swaps them with the reader's theme through a `<picture>` element, the same way Material swaps
the manual's figures.

**These are not manual figures and do not live in `docs/manual/images/`.** That whole set still shows
**Vapor Pets** on the pre-1.2 card layout, and reshooting it is a single pass that has to move the
tutorial prose with the pictures — `BACKLOG.md` holds it. Half-reshooting it would leave one document
showing two collections. A README hero is outside that document, so it can be current on its own.

## How the pair was made

The way every figure here is made — a frame of the **running app**, not a `VisualCapture` render,
whose 8×8 fixtures produce empty panels and "2 unique DNA":

```bash
dotnet build nfty.sln
src/Nfty.Desktop/bin/Debug/net10.0/Nfty.Desktop.exe
```

Then, in the app: **Open the demo CookBook** on Landing, expand `Chest` in the tree, and select the
CookBook root. Then, from the repository root:

```bash
python tools/docs-capture/pair.py hero <shots-dir>
cp <shots-dir>/hero-light.png <shots-dir>/hero-dark.png assets/readme/
```

`pair.py` captures both themes and names each file by which one it actually is — never trust capture
order, because a swallowed Ctrl+T gives you the same theme twice. `tools/docs-capture/README.md`
carries that trap and two others.

## What the frame has to show

It is the README's thesis in one picture: **31 variants, 615,600 unique DNA**. Reshoot it when the
CookBook card changes shape, and keep the demo book as the subject — it is the book a reader can
open on their own machine one click after installing.

The whole window is used, at `shot.ps1`'s standard size, uncropped. The air under the DNA-space table
is real: a two-recipe book has nothing more to put there, and the table pages against the mint bar
rather than scrolling.
