"""Draws the application icon from the same mark the titlebar wears.

    python tools/icons/make-app-icon.py

The titlebar draws its mark live (src/Nfty.App/Views/BrandMarkView.axaml); Windows needs the same
thing as a file. Rather than exporting a screenshot, this reproduces the recipe from the theme's own
values, so the two can only differ if somebody changes one and not the other -- and the values are
named here, in one place, rather than sampled off a rendered pixel.

THE MARK IS THE LAYER STACK, which is what the shipped brand icon carries
(corderro-artz.github.io/public/nfty). It replaced a lowercase `n` turned 45 degrees: a letter
rotated into a diamond spelled the name and said nothing about the product, while offset diamonds
are what an asset IS here -- a stack of layers with the top one live.

THE TILE, NOT THE CARD. The 256px brand icon is a card: near-black ground, hairline inset, the
oxblood rail, the symbol, a divider and the NFTY wordmark. None of that survives the sizes this file
exists for -- the wordmark is unreadable below 64 and a near-black ground disappears into a dark
taskbar, which is the size and the place a user sees this most. So the icon carries the card's
SYMBOL on the app's own washed and outlined tile, which is also exactly what the titlebar shows, and
the card stays the 256px web form.

Writes src/Nfty.Desktop/nfty.ico with every size Windows actually asks for: 16 and 32 in the taskbar
and Explorer's small views, 48 and 64 in medium, 128 and 256 for large tiles and the Alt-Tab card.
A single 256 scaled down by the shell looks muddy at 16, which is the size a user sees most.
"""
import os

from PIL import Image, ImageDraw

OUT = os.path.join('src', 'Nfty.Desktop', 'nfty.ico')

# Straight from Themes/Tokens.axaml's DARK dictionary. The icon sits on a taskbar, not on the app's
# own ground, and the dark tile reads on both light and dark Windows themes where the light one
# disappears against a pale taskbar.
PANEL = (15, 17, 24, 255)        # #0f1118 - the tile
ACCENT = (161, 31, 49, 255)      # #a11f31 - the edge and the live layer
WASH = (38, 14, 20, 255)         # the accent wash over the panel, flattened
INK = (242, 237, 230)            # #f2ede6 - FgBrush (dark), the layers under the live one
SIZES = [16, 32, 48, 64, 128, 256]


def diamond(cx, cy, w, h):
    """The four points of one layer, flat side to side, as the card draws them."""
    return [(cx - w / 2, cy), (cx, cy - h / 2), (cx + w / 2, cy), (cx, cy + h / 2)]


def draw(size):
    """One square, drawn at 4x and downsampled so the arcs and the diagonals stay clean."""
    ss = 4
    n = size * ss
    img = Image.new('RGBA', (n, n), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    # The tile: the app's own RadiusSm proportion (5 of 24), not a fixed pixel radius, so the corner
    # keeps its shape at every size.
    radius = int(n * 5 / 24)
    inset = max(1, int(n * 0.03))
    border = max(1, int(n * 0.045))

    # SMALL SIZES ARE TUNED, not merely scaled, and the tuning is now about HOW MANY LAYERS rather
    # than about the letter's contrast. At 16 the outline eats most of the tile and three rows of
    # anything is a smear, so the tile fills with the accent and ONE layer is knocked out of it -
    # figure and ground swapped, for real contrast at the size that has none to spare. Below 64 the
    # stack reads from two, which is the cut the 64px web favicon already makes.
    small = size <= 24
    if small:
        d.rounded_rectangle([inset, inset, n - inset - 1, n - inset - 1],
                            radius=radius, fill=ACCENT)
    else:
        d.rounded_rectangle([inset, inset, n - inset - 1, n - inset - 1],
                            radius=radius, fill=WASH, outline=ACCENT, width=border)

    # The layers on their own transparent sheet: the lower ones are drawn at less than full alpha,
    # and ImageDraw REPLACES pixels rather than blending into them - drawn straight onto the tile a
    # translucent stroke would punch a hole in it instead of dimming over it.
    layers = Image.new('RGBA', (n, n), (0, 0, 0, 0))
    ld = ImageDraw.Draw(layers)

    if small:
        # One layer, knocked out of the accent tile in the panel color. Larger than a layer's share
        # of the stack above, because it is not sharing: at 16px a diamond drawn to the stack's own
        # proportions is ten pixels by four and reads as a speck on a red chip rather than as a mark.
        ld.polygon(diamond(n / 2, n / 2, n * 0.70, n * 0.32), fill=PANEL)
    else:
        # The card's own proportions, scaled up to fill a tile that has no rail or wordmark taking
        # width from it: a layer is 0.55 wide by 0.19 tall, and they sit 0.257 apart, which leaves a
        # gap wider than the stroke. The 256px card shipped these overlapping at first, and touching
        # strokes read as a lattice rather than as a stack.
        w, h, gap = n * 0.55, n * 0.19, n * 0.257
        stroke = max(1, int(n * 0.034))
        rows = [-1, 0, 1] if size >= 64 else [-0.545, 0.545]
        alphas = [255, 217, 153] if size >= 64 else [255, 191]
        for i, (row, alpha) in enumerate(zip(rows, alphas)):
            pts = diamond(n / 2, n / 2 + row * gap, w, h)
            if i == 0:
                ld.polygon(pts, fill=ACCENT)          # the live layer
            else:
                ld.polygon(pts, outline=INK + (alpha,), width=stroke)

    img = Image.alpha_composite(img, layers)
    return img.resize((size, size), Image.LANCZOS)


def main():
    frames = [draw(s) for s in SIZES]
    os.makedirs(os.path.dirname(OUT), exist_ok=True)

    # append_images, NOT sizes= alone. Given only sizes=, Pillow takes the one image it was called on
    # and downsamples it for every entry - which silently threw away the per-size drawing above and
    # shipped a 16 that was just a shrunken 256, the exact thing the small-size tuning exists to
    # avoid. Passing the frames explicitly is what makes each size the one that was drawn for it.
    frames[-1].save(OUT, format='ICO',
                    sizes=[(s, s) for s in SIZES],
                    append_images=frames[:-1])
    print('wrote %s  (%d sizes: %s)' % (OUT, len(SIZES), ', '.join(str(s) for s in SIZES)))


if __name__ == '__main__':
    main()
