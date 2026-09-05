"""Draws the application icon from the same mark the titlebar wears.

    python tools/icons/make-app-icon.py

The titlebar draws its mark live, as a rotated glyph on a washed, outlined tile; Windows needs the
same thing as a file. Rather than exporting a screenshot, this reproduces the recipe from the theme's
own values, so the two can only differ if somebody changes one and not the other -- and the values
are named here, in one place, rather than sampled off a rendered pixel.

Writes src/Nfty.Desktop/nfty.ico with every size Windows actually asks for: 16 and 32 in the taskbar
and Explorer's small views, 48 and 64 in medium, 128 and 256 for large tiles and the Alt-Tab card.
A single 256 scaled down by the shell looks muddy at 16, which is the size a user sees most.
"""
import io
import math
import os

from PIL import Image, ImageDraw, ImageFont

OUT = os.path.join('src', 'Nfty.Desktop', 'nfty.ico')
FONT = os.path.join('src', 'Nfty.App', 'Assets', 'Fonts', 'IBMPlexMono-Bold.ttf')

# Straight from Themes/Tokens.axaml's DARK dictionary. The icon sits on a taskbar, not on the app's
# own ground, and the dark tile reads on both light and dark Windows themes where the light one
# disappears against a pale taskbar.
PANEL = (15, 17, 24, 255)        # #0f1118 - the tile
ACCENT = (161, 31, 49, 255)      # #a11f31 - the edge and the glyph
WASH = (38, 14, 20, 255)         # the accent wash over the panel, flattened
SIZES = [16, 32, 48, 64, 128, 256]


def draw(size):
    """One square, drawn at 4x and downsampled so the arcs and the rotated glyph stay clean."""
    ss = 4
    n = size * ss
    img = Image.new('RGBA', (n, n), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    # The tile: the app's own RadiusSm proportion (5 of 24), not a fixed pixel radius, so the corner
    # keeps its shape at every size.
    radius = int(n * 5 / 24)
    inset = max(1, int(n * 0.03))
    border = max(1, int(n * 0.045))

    # SMALL SIZES ARE TUNED, not merely scaled. At 16 the outline eats most of the tile and an
    # accent glyph on the dim wash has almost no contrast left to spend, so the mark dissolves into a
    # dark square - and 16 is the size in the taskbar, which is where this is seen most. Below 32 the
    # tile fills with the accent and the letter is knocked out of it instead: the same mark, with the
    # figure and ground swapped so there is real contrast at the size that has none to spare.
    small = size <= 24
    if small:
        d.rounded_rectangle([inset, inset, n - inset - 1, n - inset - 1],
                            radius=radius, fill=ACCENT)
    else:
        d.rounded_rectangle([inset, inset, n - inset - 1, n - inset - 1],
                            radius=radius, fill=WASH, outline=ACCENT, width=border)

    # The glyph, rotated on its own transparent layer so the rotation resamples the letter rather
    # than the tile under it.
    glyph = Image.new('RGBA', (n, n), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glyph)
    try:
        font = ImageFont.truetype(FONT, int(n * (0.70 if small else 0.62)))
    except OSError:
        raise SystemExit('missing %s - the app fonts must be present' % FONT)

    box = gd.textbbox((0, 0), 'n', font=font)
    gd.text(((n - (box[2] - box[0])) / 2 - box[0],
             (n - (box[3] - box[1])) / 2 - box[1]), 'n', font=font,
            fill=PANEL if small else ACCENT)

    glyph = glyph.rotate(45, resample=Image.BICUBIC, center=(n / 2, n / 2))
    img = Image.alpha_composite(img, glyph)

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
