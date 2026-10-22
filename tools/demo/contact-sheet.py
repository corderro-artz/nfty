"""A look at the demo art without building anything: every variant, scaled, on a checker.

    python tools/demo/draw-chest-art.py .demo/art
    python tools/demo/contact-sheet.py .demo/art .demo/sheet.png
    python tools/demo/contact-sheet.py .demo/art .demo/sheet.png .demo/art/depth   # + depth-sheet.png

Value-maps are shown as drawn (gray) - the point of the sheet is FORM, and a preview that guessed at
a hue would be showing a color no roll has to produce. Nearest-neighbour scaling only; the sheet
exists to be looked at closely, and a filtered one hides exactly the stray pixels it is for.

With a third argument (the depth directory the art script writes) it also writes `depth-sheet.png`
beside the sheet: each variant's FRONT as a gray ramp (0 dark -> brightest, scaled to the largest
front in the set) with the BACK added as a blue ramp, over the same layout. Review only - nothing
ships from it.
"""
import os
import sys

from PIL import Image

ART = sys.argv[1] if len(sys.argv) > 1 else ".demo/art"
DEST = sys.argv[2] if len(sys.argv) > 2 else ".demo/sheet.png"
DEPTH = sys.argv[3] if len(sys.argv) > 3 else None
SCALE, PAD, LABEL = 6, 8, 12


def checker(w, h):
    im = Image.new("RGBA", (w, h), (44, 44, 52, 255))
    px = im.load()
    for y in range(h):
        for x in range(w):
            if ((x // 8) + (y // 8)) % 2 == 0:
                px[x, y] = (58, 58, 68, 255)
    return im


def ramp_picture(path, top_front, top_back):
    """A depth picture as a ramp: R -> gray by front, plus blue by back; clear where depth is 0/0."""
    src = Image.open(path).convert("RGB")
    out = Image.new("RGBA", src.size, (0, 0, 0, 0))
    a, b = src.load(), out.load()
    for y in range(src.size[1]):
        for x in range(src.size[0]):
            f, k, _ = a[x, y]
            g = 40 + int(215 * f / top_front) if top_front else 40
            b[x, y] = (g, g, min(255, g + int(150 * k / top_back)) if top_back else g, 255)
    return out


def depth_sheet(grid):
    maxima = [0, 0]
    for layer, files in grid:
        for f in files:
            p = os.path.join(DEPTH, layer, f)
            if os.path.exists(p):
                for i, band in enumerate(Image.open(p).convert("RGB").split()[:2]):
                    maxima[i] = max(maxima[i], band.getextrema()[1])
    cols = max(len(v) for _, v in grid)
    cell = 32 * SCALE + PAD
    sheet = checker(cols * cell + PAD, len(grid) * (cell + LABEL) + PAD)
    for r, (layer, files) in enumerate(grid):
        for c, f in enumerate(files):
            p = os.path.join(DEPTH, layer, f)
            if not os.path.exists(p):
                continue                              # glow: flat, so nothing to draw
            im = ramp_picture(p, maxima[0], maxima[1]).resize((32 * SCALE, 32 * SCALE), Image.NEAREST)
            sheet.alpha_composite(im, (PAD + c * cell, PAD + r * (cell + LABEL)))
    dest = os.path.join(os.path.dirname(os.path.abspath(DEST)), "depth-sheet.png")
    sheet.save(dest)
    print("wrote", dest, "- front max %d, back max %d" % tuple(maxima))


def main():
    layers = sorted(d for d in os.listdir(ART) if os.path.isdir(os.path.join(ART, d)) and d != "depth")
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
    if DEPTH:
        depth_sheet(grid)
    print("wrote", DEST, "-", sum(len(v) for _, v in grid), "variants,",
          " / ".join("%s:%s" % (l, ",".join(os.path.splitext(f)[0] for f in v)) for l, v in grid))


if __name__ == "__main__":
    main()
