"""A look at the demo art without building anything: every variant, scaled, on a checker.

    python tools/demo/draw-chest-art.py .demo/art
    python tools/demo/contact-sheet.py .demo/art .demo/sheet.png

Value-maps are shown as drawn (gray) - the point of the sheet is FORM, and a preview that guessed at
a hue would be showing a color no roll has to produce. Nearest-neighbour scaling only; the sheet
exists to be looked at closely, and a filtered one hides exactly the stray pixels it is for.
"""
import os
import sys

from PIL import Image

ART = sys.argv[1] if len(sys.argv) > 1 else ".demo/art"
DEST = sys.argv[2] if len(sys.argv) > 2 else ".demo/sheet.png"
SCALE, PAD, LABEL = 6, 8, 12


def checker(w, h):
    im = Image.new("RGBA", (w, h), (44, 44, 52, 255))
    px = im.load()
    for y in range(h):
        for x in range(w):
            if ((x // 8) + (y // 8)) % 2 == 0:
                px[x, y] = (58, 58, 68, 255)
    return im


def main():
    layers = sorted(d for d in os.listdir(ART) if os.path.isdir(os.path.join(ART, d)))
    grid = [(d, sorted(f for f in os.listdir(os.path.join(ART, d)) if f.endswith(".png")))
            for d in layers]
    cols = max(len(v) for _, v in grid)
    cell = 32 * SCALE + PAD
    sheet = checker(cols * cell + PAD, len(grid) * (cell + LABEL) + PAD)

    for r, (layer, files) in enumerate(grid):
        for c, f in enumerate(files):
            im = Image.open(os.path.join(ART, layer, f)).convert("RGBA")
            im = im.resize((32 * SCALE, 32 * SCALE), Image.NEAREST)
            sheet.alpha_composite(im, (PAD + c * cell, PAD + r * (cell + LABEL)))
    sheet.save(DEST)
    print("wrote", DEST, "-", sum(len(v) for _, v in grid), "variants,",
          " / ".join("%s:%s" % (l, ",".join(os.path.splitext(f)[0] for f in v)) for l, v in grid))


if __name__ == "__main__":
    main()
