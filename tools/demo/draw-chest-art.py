"""Draws the built-in Chest Demo's art: 32x32 sprites, one pixel at a time.

Run it with an output directory; it writes `<out>/<ingredientId>/<variantId>.png`, which is the
layout `nfty new ingredient --images` expects.

    python tools/demo/draw-chest-art.py .demo/art

WHY EVERY PIXEL IS SET BY HAND. PIL's drawing primitives are close enough to hard-edged that the
old 64x64 pet art got away with them, but at 32x32 a single stray anti-aliased pixel is 0.1% of the
whole sprite and reads as dirt. Nothing here calls ImageDraw: shapes are sets of coordinates and
every pixel is a `putpixel`, so the file cannot contain a value that is not in the ramp below.

THE RAMP IS THE WHOLE STYLE. Dynamic and Static layers are grayscale VALUE-MAPS - the byte is the
V of HSV and nfty injects the hue and saturation at generation time (see Imaging/Colorizer.cs) - so
the only thing this art controls is form. Six values, shared by every sprite in the set, is what
makes a rolled collection look like one artist drew it: a body, its bands and its lock land on the
same six steps whatever colors they roll. Shading is authored as an INDEX into that ramp and the
lighting bias adds and subtracts indices, so a highlight is always exactly one step and the set can
never drift into a seventh value.

Light is from the top-left in every sprite, and the silhouette outline is DERIVED (any filled pixel
with an empty 4-neighbour) rather than drawn, so texture can never punch a hole through an edge.

THE OVERLAY CONTRACT. Bands, Lock and Trim have to sit correctly on BOTH bodies, and the two lids
are different shapes. Everything from y=12 down is identical in both silhouettes, so every overlay
is authored at y>=12 and the two lids differ only above it. Break that and a strap floats in the air
on one recipe and not the other - which no ViewModel test could see.
"""
import os
import random
import sys

from PIL import Image

W = H = 32
OUT = sys.argv[1] if len(sys.argv) > 1 else ".demo/art"

# The ramp. Index 0 is the outline; 1..5 are shadow -> highlight. Authored shading is an index into
# this list, never a byte, so "one step lighter" means the same thing everywhere.
RAMP = [26, 62, 98, 138, 182, 226]
OUTLINE, D2, D1, MID, LT, HI = range(6)

# ---------------------------------------------------------------------------- silhouettes
# y -> (x0, x1) inclusive. Rows 12..28 are shared by both lids on purpose; see the module docstring.
BODY_ROWS = {y: (3, 28) for y in range(16, 28)}
BODY_ROWS[28] = (2, 29)                       # the plinth, one pixel prouder than the body

# The lid OVERHANGS by a pixel at rows 14-15 and the plinth answers it at row 28. That overhang is
# most of what makes the silhouette read as a chest rather than a box: without it the lid and the
# body are one flat slab with a line drawn across it, which is exactly how the first cut looked.
CHEST_LID = {7: (9, 22), 8: (7, 24), 9: (6, 25), 10: (5, 26), 11: (4, 27),
             12: (3, 28), 13: (3, 28), 14: (2, 29), 15: (2, 29)}

BOX_LID = {8: (5, 26), 9: (4, 27), 10: (3, 28), 11: (3, 28),
           12: (3, 28), 13: (3, 28), 14: (2, 29), 15: (2, 29)}

SEAM_Y = 15          # the lid's bottom row: where lid meets body, in both silhouettes


def mask(rows):
    return {(x, y) for y, (a, b) in rows.items() for x in range(a, b + 1)}


# ---------------------------------------------------------------------------- authored shading
# Per-row ramp index. The lid is a form catching light near its upper third; the body's top row sits
# in the lid's own shadow, which is what stops the two reading as one flat slab.
LID_V = {7: LT, 8: HI, 9: HI, 10: LT, 11: LT, 12: MID, 13: MID, 14: MID, 15: D1}
BODY_V = {16: D2, 17: LT, 18: LT, 19: LT, 20: MID, 21: MID,
          22: MID, 23: MID, 24: D1, 25: D1, 26: D1, 27: D2, 28: D2}


def bias(x, row):
    """Left-lit: the two columns nearest the left edge gain a step, the three nearest the right
    lose one. Applied to the INDEX, so it is exactly one ramp step either way."""
    a, b = row
    if x <= a + 2:
        return 1
    if x >= b - 1:
        return -2
    if x >= b - 4:
        return -1
    return 0


def clamp(i):
    return max(1, min(5, i))       # never down to 0: that index belongs to the outline alone


# ---------------------------------------------------------------------------- canvas
class Sprite:
    """A 32x32 RGBA canvas addressed by ramp index. `ink` takes an index; `rgb` takes a color, for
    the Custom layers that are not value-maps."""

    def __init__(self):
        self.im = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        self.px = self.im.load()

    def ink(self, x, y, i, alpha=255):
        if 0 <= x < W and 0 <= y < H:
            v = RAMP[clamp(i)] if i else RAMP[0]
            self.px[x, y] = (v, v, v, alpha)

    def rgb(self, x, y, c):
        if 0 <= x < W and 0 <= y < H:
            self.px[x, y] = c if len(c) == 4 else c + (255,)

    def clear(self, x, y):
        if 0 <= x < W and 0 <= y < H:
            self.px[x, y] = (0, 0, 0, 0)

    def filled(self, x, y):
        return 0 <= x < W and 0 <= y < H and self.px[x, y][3] != 0

    def outline(self, shape):
        """Derived, not drawn: any filled pixel with an empty 4-neighbour becomes the outline. Run
        LAST, so no texture can eat an edge."""
        edge = [(x, y) for (x, y) in shape
                if not all((x + dx, y + dy) in shape for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))]
        for x, y in edge:
            self.ink(x, y, OUTLINE)

    def save(self, path):
        os.makedirs(os.path.dirname(path), exist_ok=True)
        self.im.save(path)


def body_base(lid_rows):
    """The lit, untextured chest: lid plus body plus the seam. Returns the sprite and its shape."""
    s = Sprite()
    rows = dict(lid_rows)
    rows.update(BODY_ROWS)
    shape = mask(rows)
    for y, row in rows.items():
        v = LID_V.get(y, BODY_V.get(y, MID))
        for x in range(row[0], row[1] + 1):
            s.ink(x, y, clamp(v + bias(x, row)))
    return s, shape, rows


# ---------------------------------------------------------------------------- body textures
# A texture's job at 32px is to say what the surface IS in about a dozen pixels. The first cut put a
# seam on five rows out of nine and the chest read as a shutter, so each of these gets two seams and
# a handful of marks - the form does the rest.
def v_at(y):
    return LID_V.get(y, BODY_V.get(y, MID))


def seam(s, rows, y, step=2):
    """One dark course across a row, stopping short of the outline at both ends."""
    if y not in rows:
        return
    a, b = rows[y]
    for x in range(a + 1, b):
        s.ink(x, y, clamp(v_at(y) - step))


def tex_planked(s, rows):
    """Boards: two courses, butt joints between them, and a light scatter of grain. The joints are
    what stop the courses reading as a stripe - a plank has ends."""
    rng = random.Random(0x0A11)
    for y in (11, 20, 24):
        seam(s, rows, y)
    for x, span in ((13, range(16, 20)), (20, range(21, 24)), (9, range(25, 27))):
        for y in span:
            if y in rows and rows[y][0] < x < rows[y][1]:
                s.ink(x, y, clamp(v_at(y) - 2))
    for _ in range(14):
        y = rng.choice([8, 9, 12, 13, 17, 18, 21, 22, 25, 26])
        if y not in rows:
            continue
        a, b = rows[y]
        s.ink(rng.randint(a + 2, b - 3), y, clamp(v_at(y) - 1))


def tex_plated(s, rows):
    """Riveted sheet: two panel joints down the body and three rivet courses. A rivet is two pixels -
    a highlight with a shadow at its lower right - because one reads as dirt and three as a bolt."""
    for y in (13, 18, 25):
        if y not in rows:
            continue
        a, b = rows[y]
        for x in range(a + 3, b - 2, 5):
            s.ink(x, y, HI)
            s.ink(x + 1, y + 1, D2)
    for x in (10, 21):
        for y in range(16, 27):
            if y in rows and rows[y][0] < x < rows[y][1]:
                s.ink(x, y, clamp(v_at(y) - 2))
                s.ink(x + 1, y, clamp(v_at(y) + 1))


def tex_stone(s, rows):
    """Offset courses. The joints alternate per course, which is the one thing that keeps a brick
    pattern from reading as a grid."""
    rng = random.Random(0x5709)
    for y, off in ((12, 0), (19, 3), (24, 0)):
        seam(s, rows, y)
        if y not in rows:
            continue
        a, b = rows[y]
        for x in range(a + 3 + off, b - 1, 6):
            for dy in (1, 2, 3):
                if y + dy in rows and rows[y + dy][0] < x < rows[y + dy][1] and y + dy != SEAM_Y:
                    s.ink(x, y + dy, clamp(v_at(y + dy) - 2))
    for _ in range(16):
        y = rng.choice([9, 10, 13, 17, 21, 22, 26])
        if y not in rows:
            continue
        a, b = rows[y]
        s.ink(rng.randint(a + 2, b - 3), y, clamp(v_at(y) + rng.choice((-1, 1))))


def make_body(lid_rows, texture):
    s, shape, rows = body_base(lid_rows)
    texture(s, rows)
    for x in range(rows[SEAM_Y][0] + 1, rows[SEAM_Y][1]):   # the lid/body seam, always darkest
        s.ink(x, SEAM_Y, D2)
    s.outline(shape)
    return s


# ---------------------------------------------------------------------------- bands (dynamic)
# Every overlay below lives inside x=3..28, y>=12, which is filled in BOTH silhouettes.
def raised(s, x, y, i):
    """One pixel of a raised fitting: same six values as everything else, so a band tinted close to
    its body still separates by form."""
    s.ink(x, y, i)


def band_straps():
    """Two straps with buckle plates. Five pixels wide - outline, highlight, mid, shadow, outline -
    because a strap with no edge of its own is a stripe of paint on the lid."""
    s = Sprite()
    for x0 in (6, 20):
        for y in range(12, 28):
            s.ink(x0, y, OUTLINE)
            s.ink(x0 + 1, y, LT if y < 16 else MID)
            s.ink(x0 + 2, y, MID if y < 16 else D1)
            s.ink(x0 + 3, y, D1)
            s.ink(x0 + 4, y, OUTLINE)
        for y in range(18, 22):                       # buckle plate, straddling the strap
            for x in range(x0 - 1, x0 + 6):
                inner = x0 <= x <= x0 + 4 and 18 < y < 21
                s.ink(x, y, LT if inner else OUTLINE)
        s.ink(x0 + 1, 19, HI)
        s.ink(x0 + 2, 20, D2)
        s.ink(x0 + 3, 20, D2)
    return s


def band_corners():
    """Corner brackets on the body, caps on the lid. Two pixels thick with one lit arm each, so a
    bracket that rolls close to its body still has a light edge and a dark one."""
    s = Sprite()
    for cx, cy, sx, sy in ((3, 16, 1, 1), (28, 16, -1, 1), (3, 27, 1, -1), (28, 27, -1, -1)):
        for i in range(7):
            s.ink(cx + sx * i, cy, MID)
            s.ink(cx + sx * i, cy + sy, D1)
            s.ink(cx, cy + sy * i, MID)
            s.ink(cx + sx, cy + sy * i, D1)
        s.ink(cx, cy, HI)
        s.ink(cx + sx * 6, cy + sy, OUTLINE)
        s.ink(cx + sx, cy + sy * 6, OUTLINE)
    for cx, sx in ((3, 1), (28, -1)):                 # lid caps
        for i in range(6):
            s.ink(cx + sx * i, 12, MID)
            s.ink(cx + sx * i, 13, D1)
        s.ink(cx, 12, HI)
    return s


def band_hoops():
    """Two hoops with rivets: one across the lid, one low on the body."""
    s = Sprite()
    for y0 in (12, 22):
        for x in range(3, 29):
            s.ink(x, y0, OUTLINE)
            s.ink(x, y0 + 1, LT)
            s.ink(x, y0 + 2, MID)
            s.ink(x, y0 + 3, OUTLINE)
        for x in range(5, 28, 6):
            s.ink(x, y0 + 1, HI)
            s.ink(x, y0 + 2, D2)
    return s


# ---------------------------------------------------------------------------- locks (static)
# A Static layer takes ONE fixed color for the whole collection, so a lock is the one thing on every
# chest that is the same metal - and it has to stay legible over a body that rolled any hue at all.
# That is what the full outline is for.
def plate(s, x0, y0, x1, y1, chamfer=True):
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            if chamfer and x in (x0, x1) and y in (y0, y1):
                continue                              # clipped corners: a plate, not a domino
            edge = x in (x0, x1) or y in (y0, y1)
            s.ink(x, y, OUTLINE if edge else (LT if y - y0 < (y1 - y0) / 2 else MID))
    s.ink(x0 + 1, y0 + 1, HI)


def lock_keyhole():
    """An escutcheon: the plainest of the four, and the commonest."""
    s = Sprite()
    plate(s, 12, 14, 19, 22)
    for x, y in ((15, 16), (16, 16), (15, 17), (16, 17)):
        s.ink(x, y, OUTLINE)
    for y in (18, 19):
        s.ink(15, y, OUTLINE)
        s.ink(16, y, OUTLINE)
    return s


def lock_padlock():
    """A shackle over the seam and a small body under it - narrower than the plates, so it reads as
    a thing HANGING on the chest rather than a thing set into it."""
    s = Sprite()
    for y in range(12, 17):
        s.ink(14, y, MID)
        s.ink(17, y, MID)
    for x in range(14, 18):
        s.ink(x, 12, LT)
    s.ink(14, 13, HI)
    plate(s, 13, 16, 18, 23)
    for x, y in ((15, 18), (16, 18), (15, 19), (16, 19), (15, 20), (16, 20)):
        s.ink(x, y, OUTLINE)
    return s


def lock_keypad():
    """Six buttons in a 3x2. The alternating lit/unlit pixels are what make it read as a KEYPAD at
    this size - six identical dots read as rivets, which the bands already own."""
    s = Sprite()
    plate(s, 12, 14, 19, 22)
    for row, y in enumerate((16, 19)):
        for col, x in enumerate((14, 16)):
            s.ink(x, y, HI if (row + col) % 2 == 0 else D2)
            s.ink(x + 1, y, HI if (row + col) % 2 == 0 else D2)
            s.ink(x, y + 1, D2)
            s.ink(x + 1, y + 1, D2)
    for y in (16, 19):
        s.ink(18, y, HI)
        s.ink(18, y + 1, D2)
    return s


def lock_latch():
    """A clasp: a small plate on the lid and a hook dropping onto the body, with daylight either
    side of the hook. The smallest of the four on purpose - four identically sized plates would make
    the Lock layer read as one variant wearing four faces."""
    s = Sprite()
    plate(s, 13, 12, 18, 16, chamfer=False)
    s.ink(15, 14, D2)
    s.ink(16, 14, D2)
    for y in range(16, 21):
        s.ink(15, y, OUTLINE)
        s.ink(16, y, LT if y < 19 else MID)
        s.ink(17, y, OUTLINE)
    for x in range(14, 19):
        s.ink(x, 21, OUTLINE)
    s.ink(16, 20, D2)
    return s


# ---------------------------------------------------------------------------- trim (custom)
# Full color, composited exactly as drawn - a Custom layer is never colorized, which is why these
# are the only sprites in the set that state a color at all. Both keep clear of x=11..20, where the
# lock plate lands: trim half-covered by a lock reads as a bug rather than as a lock over trim.
GOLD = ((150, 108, 36), (214, 168, 68), (246, 214, 120))
GEMS = (((150, 32, 52), (206, 54, 72), (244, 128, 140)),
        ((34, 74, 150), (72, 132, 214), (146, 196, 248)))


def trim_gems():
    """A cut stone on each shoulder of the lid, plus gold studs along the overhang. Three pixels
    across is the smallest a gem can be and still show a facet."""
    s = Sprite()
    for x0, (dark, base, light) in zip((5, 22), GEMS):
        for dx in range(3):
            s.rgb(x0 + dx, 12, GOLD[0])
            s.rgb(x0 + dx, 16, GOLD[0])
        for dy in range(3):
            s.rgb(x0 - 1, 13 + dy, GOLD[0])
            s.rgb(x0 + 3, 13 + dy, GOLD[0])
        s.rgb(x0, 13, light)
        s.rgb(x0 + 1, 13, base)
        s.rgb(x0 + 2, 13, base)
        s.rgb(x0, 14, base)
        s.rgb(x0 + 1, 14, base)
        s.rgb(x0 + 2, 14, dark)
        s.rgb(x0, 15, base)
        s.rgb(x0 + 1, 15, dark)
        s.rgb(x0 + 2, 15, dark)
    for x in range(4, 28, 3):
        s.rgb(x, 26, GOLD[1])
        s.rgb(x, 27, GOLD[0])
    return s


def trim_gilt():
    """Gold along the lid's overhang and the plinth, with corner pips. It reads as applied metal
    rather than paint because the highlight run is shorter than the base run at both ends."""
    s = Sprite()
    for x in range(2, 30):
        s.rgb(x, 14, GOLD[0])
        s.rgb(x, 15, GOLD[1])
    for x in range(4, 28):
        s.rgb(x, 15, GOLD[2] if x % 2 == 0 else GOLD[1])
    for x in range(2, 30):
        s.rgb(x, 27, GOLD[0])
        s.rgb(x, 28, GOLD[1])
    for x, y in ((3, 12), (4, 12), (27, 12), (28, 12), (3, 13), (28, 13)):
        s.rgb(x, y, GOLD[1])
    return s


# ---------------------------------------------------------------------------- glow (dynamic)
# Drawn OUTSIDE the silhouette, which is why it needs no alignment contract with the two lids. Fully
# opaque: partial alpha is legal here but it is the one thing a voxel converter cannot resolve, and a
# demo should not teach a habit it also warns about.
def star(s, x, y, arm=2):
    s.ink(x, y, HI)
    for d in range(1, arm + 1):
        i = LT if d == 1 else MID
        s.ink(x + d, y, i)
        s.ink(x - d, y, i)
        s.ink(x, y + d, i)
        s.ink(x, y - d, i)


def glow_sparks():
    s = Sprite()
    for x, y in ((5, 4), (26, 4), (2, 20), (29, 22), (16, 1)):
        star(s, x, y)
    for x, y in ((10, 2), (22, 6), (1, 12), (30, 12), (12, 30), (20, 30), (6, 29), (25, 29)):
        s.ink(x, y, LT)
    return s


def glow_runes():
    """Four marks, one per corner, each a small glyph rather than a blob - at this size a soft glow
    is just a smudge, and a rune is something the eye can name."""
    s = Sprite()
    for cx, cy in ((4, 4), (27, 4), (4, 27), (27, 27)):
        for d in range(-1, 2):
            s.ink(cx + d, cy, MID)
            s.ink(cx, cy + d, MID)
        s.ink(cx, cy, HI)
        s.ink(cx - 2, cy - 2, D1)
        s.ink(cx + 2, cy + 2, D1)
    for x, y in ((16, 1), (1, 16), (30, 16), (16, 30)):
        s.ink(x, y, LT)
        s.ink(x - 1, y, D1)
        s.ink(x + 1, y, D1)
    return s


# ---------------------------------------------------------------------------- the set
SPEC = {
    "chestbody": {"planked": lambda: make_body(CHEST_LID, tex_planked),
                  "plated": lambda: make_body(CHEST_LID, tex_plated),
                  "stone": lambda: make_body(CHEST_LID, tex_stone)},
    "boxbody": {"planked": lambda: make_body(BOX_LID, tex_planked),
                "plated": lambda: make_body(BOX_LID, tex_plated)},
    "bands": {"straps": band_straps, "corners": band_corners, "hoops": band_hoops},
    "lock": {"keyhole": lock_keyhole, "padlock": lock_padlock,
             "keypad": lock_keypad, "latch": lock_latch},
    "trim": {"gems": trim_gems, "gilt": trim_gilt},
    "glow": {"sparks": glow_sparks, "runes": glow_runes},
}


def main():
    n = 0
    for layer, variants in SPEC.items():
        for vid, fn in variants.items():
            fn().save(os.path.join(OUT, layer, vid + ".png"))
            n += 1
    print("drew %d variants at %dx%d into %s" % (n, W, H, OUT))


if __name__ == "__main__":
    main()
