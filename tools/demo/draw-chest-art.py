"""Draws the built-in Chest Demo's art: 32x32 sprites, one pixel at a time.

Run it with an output directory; it writes `<out>/<ingredientId>/<variantId>.png`, which is the
layout `nfty new ingredient --images` expects.

    python tools/demo/draw-chest-art.py .demo/art

WHY EVERY PIXEL IS SET BY HAND. PIL's drawing primitives are close enough to hard-edged that the
old 64x64 pet art got away with them, but at 32x32 a single stray anti-aliased pixel is 0.1% of the
whole sprite and reads as dirt. Nothing here calls ImageDraw: shapes are sets of coordinates and
every pixel is a `putpixel`, so the file cannot contain a value that is not in the ramp below.

NO NOISE. An earlier cut scattered random "grain" pixels over the wood and stone; at 32px a lone
pixel one step off its neighbours reads as grit, not texture, and the rolled colors made it worse.
Every mark here is a deliberate cluster - a seam with its lit edge, a two-pixel rivet, a three-pixel
grain dash - placed by coordinate. There is no random number generator in this file.

THE RAMP IS THE WHOLE STYLE. Dynamic and Static layers are grayscale VALUE-MAPS - the byte is the
V of HSV and nfty injects the hue and saturation at generation time (see Imaging/Colorizer.cs) - so
the only thing this art controls is form. Six values, shared by every sprite in the set, is what
makes a rolled collection look like one artist drew it: a body, its bands and its lock land on the
same six steps whatever colors they roll. Shading is authored as an INDEX into that ramp and the
lighting bias adds and subtracts indices, so a highlight is always exactly one step and the set can
never drift into a seventh value.

Light is from the top-left in every sprite. The silhouette outline is DERIVED (any filled pixel with
an empty 4-neighbour) rather than drawn, so texture can never punch a hole through an edge. The TOP
edge of a body is outlined in the mid value rather than black (selective outlining): it is the edge
the light falls on, and when the art rises into a model the whole top face of the lid is that row's
color - a black top row made every risen chest look burnt on top.

THE OVERLAY CONTRACT. Bands, Lock and Trim have to sit correctly on BOTH bodies, and the two lids
are different shapes. Everything from y=12 down is identical in both silhouettes, so every overlay
is authored at y>=12 and the two lids differ only above it. Break that and a strap floats in the air
on one recipe and not the other - which no ViewModel test could see.

DEPTH. The same sprites also get a depth map each (`<out>/depth/<layer>/<variant>.png`, red = front,
green = back, in voxels; `nfty set depth` stores them). Paper is 0/0, one voxel thick. The deepest
layer wins per pixel (union), so an overlay cannot say "two voxels above my body": it states ABSOLUTE
depth, `body_front(x, y) + k`. That is only safe because of the DEPTH OVERLAY CONTRACT, the twin of
the art contract above: from y=12 down every body variant on both recipes shares ONE front profile
(`lower_front`), so an overlay stands proud of every body by construction. Variants differ in depth
only above row 12. Solid pixels are read off the just-drawn sprite, so depth can never land on a
transparent pixel, and main() asserts the contract before it saves a byte.
"""
import os
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
#
# 12-13 the lid's lower course     14-15 the lid's LIP, a pixel prouder than the lid and the body
# 16-26 the body                   27-28 the plinth, answering the lip at the foot
LOWER_ROWS = {12: (3, 28), 13: (3, 28), 14: (2, 29), 15: (2, 29), 27: (2, 29), 28: (2, 29)}
for _y in range(16, 27):
    LOWER_ROWS[_y] = (3, 28)

# A barrel lid: the arc closes in fast at the top and runs straight for its last rows, which is the
# side view of a half-cylinder seen from the front.
CHEST_LID = {7: (9, 22), 8: (6, 25), 9: (5, 26), 10: (4, 27), 11: (3, 28)}
# A slab: square shoulders, one chamfered pixel at the top corners and nothing else.
BOX_LID = {8: (4, 27), 9: (3, 28), 10: (3, 28), 11: (3, 28)}

LIP = (14, 15)
BODY_TOP, BODY_BOTTOM = 16, 26
PLINTH = (27, 28)


def rows_of(lid):
    rows = dict(lid)
    rows.update(LOWER_ROWS)
    return rows


def mask(rows):
    return {(x, y) for y, (a, b) in rows.items() for x in range(a, b + 1)}


def clamp(i):
    return max(1, min(5, i))       # never down to 0: that index belongs to the outline alone


# ---------------------------------------------------------------------------- canvas
class Sprite:
    """A 32x32 RGBA canvas addressed by ramp index. `ink` takes an index; `rgb` takes a color, for
    the Custom layers that are not value-maps."""

    def __init__(self):
        self.im = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        self.px = self.im.load()

    def ink(self, x, y, i):
        if 0 <= x < W and 0 <= y < H:
            v = RAMP[clamp(i)] if i else RAMP[0]
            self.px[x, y] = (v, v, v, 255)

    def rgb(self, x, y, c):
        if 0 <= x < W and 0 <= y < H:
            self.px[x, y] = c + (255,)

    def filled(self, x, y):
        return 0 <= x < W and 0 <= y < H and self.px[x, y][3] != 0

    def outline(self, shape, top=OUTLINE):
        """Derived, not drawn: any filled pixel with an empty 4-neighbour becomes the outline. Run
        LAST, so no texture can eat an edge. A pixel whose ONLY open side is above takes `top`."""
        for (x, y) in shape:
            open_sides = [(dx, dy) for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))
                          if (x + dx, y + dy) not in shape]
            if not open_sides:
                continue
            self.ink(x, y, top if open_sides == [(0, -1)] else OUTLINE)

    def save(self, path):
        os.makedirs(os.path.dirname(path), exist_ok=True)
        self.im.save(path)


# ---------------------------------------------------------------------------- bodies (dynamic)
# The base value of each row, before texture: the lid turns away from the light as it comes down,
# the lip catches it on top, the body sits in the lip's shadow at the top and lightens to a lit
# plinth. One index per row, so a texture's seam is always "this row, two steps darker".
CHEST_LID_V = {7: LT, 8: HI, 9: HI, 10: LT, 11: LT, 12: MID, 13: MID}
BOX_LID_V = {8: HI, 9: HI, 10: LT, 11: LT, 12: MID, 13: MID}
LOWER_V = {14: LT, 15: MID, 16: D2, 27: LT, 28: MID}
for _y in range(17, 27):
    LOWER_V[_y] = MID if _y < 24 else D1


class Body:
    """One body being drawn: its rows, its per-row base value, and helpers that only ever step the
    base value up or down. Every helper stays INSIDE the outline (one pixel in from each end)."""

    def __init__(self, lid, lid_v):
        self.s = Sprite()
        self.rows = rows_of(lid)
        self.v = dict(lid_v)
        self.v.update(LOWER_V)
        self.shape = mask(self.rows)
        for y, (a, b) in self.rows.items():
            for x in range(a, b + 1):
                self.s.ink(x, y, clamp(self.v[y] + self.bias(x, y)))

    def bias(self, x, y):
        """Left-lit: the first column in from the left edge one step up, the last one in from the
        right one step down. One step, never two - two read as a stripe painted down the side."""
        a, b = self.rows[y]
        if x == a + 1:
            return 1
        if x == b - 1:
            return -1
        return 0

    def inside(self, x, y):
        return y in self.rows and self.rows[y][0] < x < self.rows[y][1]

    def at(self, x, y, step):
        """Step the pixel's BASE (row value + lighting) by `step`, if it is inside the outline."""
        if self.inside(x, y):
            self.s.ink(x, y, clamp(self.v[y] + self.bias(x, y) + step))

    def hseam(self, y, x0=None, x1=None, lit_below=True):
        """A horizontal joint: two steps dark, and the row under it one step lit - the top edge of
        the next board catching the light. That pair is what makes a line read as a JOINT."""
        a, b = self.rows[y]
        for x in range(x0 if x0 is not None else a + 1, (x1 if x1 is not None else b - 1) + 1):
            self.at(x, y, -2)
            if lit_below and y + 1 in self.rows and y + 1 not in LIP:
                self.at(x, y + 1, +1)

    def vseam(self, x, y0, y1):
        """A vertical joint: dark, with the column to its right lit."""
        for y in range(y0, y1 + 1):
            self.at(x, y, -2)
            self.at(x + 1, y, +1)

    def dash(self, x0, x1, y, step=-1):
        for x in range(x0, x1 + 1):
            self.at(x, y, step)

    def rivet(self, x, y):
        """Two pixels: a lit head and its shadow below. One pixel reads as dirt."""
        self.at(x, y, +2)
        self.at(x, y + 1, -2)

    def finish(self):
        # The lid/body seam under the lip is always the darkest course, then the outline.
        for x in range(self.rows[15][0] + 1, self.rows[15][1]):
            self.at(x, 15, -1)
        self.s.outline(self.shape, top=MID)
        return self.s


def lid_rows(b):
    return [y for y in sorted(b.rows) if y < 14]


def tex_planked(b):
    """Horizontal boards: three courses on the body, two on the lid, butt joints staggered between
    courses (a plank has ends), and one short grain dash per board - placed, not scattered."""
    top = lid_rows(b)[0]
    b.hseam(top + 3)                       # the lid's own board joint
    b.hseam(12)                            # lid course
    b.hseam(20)
    b.hseam(23)
    b.vseam(11, 17, 19)
    b.vseam(20, 21, 22)
    b.vseam(8, 24, 26)
    b.vseam(23, 24, 26)
    for x0, y in ((5, 18), (22, 18), (14, 22), (24, 22), (14, 25)):
        b.dash(x0, x0 + 2, y)
    b.vseam(17, top + 1, top + 2)
    b.dash(8, 10, top + 4)
    b.dash(20, 22, top + 4)


def tex_slatted(b):
    """Vertical boards running over the lid and down the body - the same five seams top to bottom,
    so the slats read as bent over the barrel rather than painted on it."""
    for x in (7, 12, 18, 23):
        b.vseam(x, lid_rows(b)[0] + 1, 13)
        b.vseam(x, 17, 26)
    b.hseam(12)
    for x, y0 in ((5, 19), (15, 21), (21, 18), (26, 22), (10, 23)):
        for y in (y0, y0 + 1):
            b.at(x, y, -1)


def tex_plated(b):
    """Riveted sheet: two panel joints down the body, rivets along the top and bottom of each
    plate, a riveted course on the lid. Rivets sit on a regular pitch - a riveter does not guess."""
    b.vseam(10, 17, 26)
    b.vseam(20, 17, 26)
    for x in (5, 8, 13, 17, 23, 26):
        b.rivet(x, 17)
        b.rivet(x, 24)
    b.hseam(12)
    for x in range(5, 28, 4):
        b.rivet(x, 10)
    b.vseam(15, lid_rows(b)[0] + 1, 9)


def tex_stone(b):
    """Ashlar: three courses with the joints offset course to course, which is the one thing that
    keeps a block pattern from reading as a grid. Each block is lit along its top by the seam."""
    b.hseam(20)
    b.hseam(23)
    for x in (9, 16, 23):
        b.vseam(x, 17, 19)
    for x in (6, 13, 19, 25):
        b.vseam(x, 21, 22)
    for x in (9, 16, 23):
        b.vseam(x, 24, 26)
    b.hseam(12)
    top = lid_rows(b)[0]
    b.hseam(top + 2)
    for x in (8, 15, 22):
        b.vseam(x, top + 3, 11)
    b.vseam(12, top + 1, top + 1)
    b.vseam(19, top + 1, top + 1)


def tex_paneled(b):
    """Frame and panel: two recessed panels in the body and one in the lid. A recess is lit on the
    edges facing away from the light and dark on the edges facing it - the inverse of a raised
    plate, which is how the eye tells in from out."""
    def recess(x0, y0, x1, y1):
        for x in range(x0, x1 + 1):
            for y in range(y0, y1 + 1):
                edge_hi = y == y1 or x == x1
                edge_lo = y == y0 or x == x0
                b.at(x, y, -2 if edge_lo else (+1 if edge_hi else -1))
    recess(5, 18, 14, 25)
    recess(17, 18, 26, 25)
    top = lid_rows(b)[0]
    recess(8, top + 2, 23, 11)
    b.hseam(12, lit_below=False)


def make_body(lid, lid_v, texture):
    b = Body(lid, lid_v)
    texture(b)
    return b.finish()


# ---------------------------------------------------------------------------- bands (dynamic)
# Every overlay below lives inside x=2..29, y>=12, which is filled in BOTH silhouettes.
def strap_v(s, x0, y0, y1, lit=LT):
    """A vertical strap five pixels wide - outline, lit, mid, shadow, outline - because a strap with
    no edge of its own is a stripe of paint."""
    for y in range(y0, y1 + 1):
        s.ink(x0, y, OUTLINE)
        s.ink(x0 + 1, y, lit)
        s.ink(x0 + 2, y, MID)
        s.ink(x0 + 3, y, D1)
        s.ink(x0 + 4, y, OUTLINE)


def strap_h(s, y0, x0, x1):
    """A horizontal hoop four pixels tall: outline, lit, shadow, outline."""
    for x in range(x0, x1 + 1):
        s.ink(x, y0, OUTLINE)
        s.ink(x, y0 + 1, LT)
        s.ink(x, y0 + 2, D1)
        s.ink(x, y0 + 3, OUTLINE)


def band_straps():
    """Two straps over the lid and down the body, each with a buckle."""
    s = Sprite()
    for x0 in (6, 21):
        strap_v(s, x0, 12, 28)
        for y in range(19, 23):                       # buckle frame, straddling the strap
            for x in range(x0 - 1, x0 + 6):
                frame = y in (19, 22) or x in (x0 - 1, x0 + 5)
                if frame:
                    s.ink(x, y, OUTLINE)
                elif x == x0 + 2:
                    s.ink(x, y, HI)                   # the prong
        s.ink(x0 - 1, 19, MID)
        s.ink(x0 + 5, 22, MID)
    return s


def band_corners():
    """Corner brackets on the body, caps on the lid course. Two pixels thick with one lit arm each,
    so a bracket that rolls close to its body still has a light edge and a dark one."""
    s = Sprite()
    for cx, cy, sx, sy in ((3, 16, 1, 1), (28, 16, -1, 1), (3, 26, 1, -1), (28, 26, -1, -1)):
        for i in range(6):
            s.ink(cx + sx * i, cy, LT)
            s.ink(cx + sx * i, cy + sy, D1)
            s.ink(cx, cy + sy * i, LT)
            s.ink(cx + sx, cy + sy * i, D1)
        s.ink(cx, cy, HI)
        s.ink(cx + sx * 6, cy, OUTLINE)
        s.ink(cx + sx * 6, cy + sy, OUTLINE)
        s.ink(cx, cy + sy * 6, OUTLINE)
        s.ink(cx + sx, cy + sy * 6, OUTLINE)
        s.ink(cx + sx * 3, cy, HI)                  # a rivet on each arm
        s.ink(cx, cy + sy * 3, HI)
    for cx, sx in ((3, 1), (28, -1)):               # lid caps
        for i in range(5):
            s.ink(cx + sx * i, 12, LT)
            s.ink(cx + sx * i, 13, D1)
        s.ink(cx, 12, HI)
        s.ink(cx + sx * 5, 12, OUTLINE)
        s.ink(cx + sx * 5, 13, OUTLINE)
    return s


def band_hoops():
    """Two hoops with rivets: one around the lid course, one low on the body."""
    s = Sprite()
    for y0 in (12, 21):
        strap_h(s, y0, 3, 28)
        for x in range(5, 28, 5):
            s.ink(x, y0 + 1, HI)
            s.ink(x, y0 + 2, MID)
    return s


def band_frame():
    """Iron edging down both sides of the chest and along the plinth - the band a chest that gets
    dragged wears. Riveted where a smith would put them: top, middle, foot."""
    s = Sprite()
    for x0, lit, mid in ((3, LT, MID), (25, MID, D1)):
        for y in range(12, 28):
            s.ink(x0, y, OUTLINE)
            s.ink(x0 + 1, y, lit)
            s.ink(x0 + 2, y, mid)
            s.ink(x0 + 3, y, OUTLINE)
        for y in (13, 19, 24):
            s.ink(x0 + 1, y, HI)
            s.ink(x0 + 2, y + 1, D2)
    for x in range(7, 25):                            # along the plinth
        s.ink(x, 26, OUTLINE)
        s.ink(x, 27, LT)
    return s


def band_studs():
    """Rows of domed studs: across the lid course and twice around the body, clear of the middle,
    where the lock goes. A stud is a 2x2 - lit, mid, mid, shadow - with an outline under it."""
    s = Sprite()

    def stud(x, y):
        s.ink(x, y, HI)
        s.ink(x + 1, y, MID)
        s.ink(x, y + 1, MID)
        s.ink(x + 1, y + 1, D1)
        s.ink(x, y + 2, OUTLINE)
        s.ink(x + 1, y + 2, OUTLINE)
    for x in (4, 8, 22, 26):
        stud(x, 12)
        stud(x, 17)
        stud(x, 23)
    for x in (12, 18):
        stud(x, 12)
    return s


# ---------------------------------------------------------------------------- locks (static)
# A Static layer takes ONE fixed color for the whole collection, so a lock is the one thing on every
# chest that is the same metal - and it has to stay legible over a body that rolled any hue at all.
# That is what the full outline is for.
def plate(s, x0, y0, x1, y1, chamfer=True):
    """A raised plate: outlined, lit on its top half, a highlight in its top-left corner."""
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            if chamfer and x in (x0, x1) and y in (y0, y1):
                continue                              # clipped corners: a plate, not a domino
            edge = x in (x0, x1) or y in (y0, y1)
            if edge:
                s.ink(x, y, OUTLINE)
            elif x == x1 - 1 or y == y1 - 1:
                s.ink(x, y, D1)                       # the plate's own shadow edge
            else:
                s.ink(x, y, LT if y - y0 <= (y1 - y0) // 2 else MID)
    s.ink(x0 + 1, y0 + 1, HI)


def keyhole(s, cx, y):
    """A keyhole: a 2x2 hole over a 2x2 slot, one pixel narrower at the waist."""
    for x, yy in ((cx, y), (cx + 1, y), (cx, y + 1), (cx + 1, y + 1), (cx, y + 2), (cx + 1, y + 2), (cx, y + 3), (cx + 1, y + 3)):
        s.ink(x, yy, OUTLINE)


def lock_keyhole():
    """An escutcheon: the plainest of the six, and the commonest."""
    s = Sprite()
    plate(s, 12, 14, 19, 22)
    keyhole(s, 15, 16)
    return s


def lock_padlock():
    """A shackle over the lip and a body under it - narrower than the plates, so it reads as a thing
    HANGING on the chest rather than a thing set into it."""
    s = Sprite()
    for x in range(14, 18):
        s.ink(x, 12, OUTLINE)
    for y in (13, 14, 15):
        s.ink(13, y, OUTLINE)
        s.ink(14, y, HI if y == 13 else LT)
        s.ink(17, y, MID)
        s.ink(18, y, OUTLINE)
    s.ink(15, 13, LT)
    s.ink(16, 13, LT)
    s.ink(15, 14, OUTLINE)
    s.ink(16, 14, OUTLINE)
    plate(s, 12, 16, 19, 24)
    keyhole(s, 15, 18)
    return s


def lock_keypad():
    """Four buttons in a 2x2 over a card slot. The alternating lit/unlit buttons are what make it
    read as a KEYPAD at this size - identical dots read as rivets, which the bands already own."""
    s = Sprite()
    plate(s, 12, 14, 19, 22)
    for row, y in enumerate((16, 18)):
        for col, x in enumerate((14, 16)):
            s.ink(x, y, HI if (row + col) % 2 == 0 else D2)
            s.ink(x, y + 1, D2)
    for x in range(14, 18):
        s.ink(x, 20, OUTLINE)
    return s


def lock_latch():
    """A clasp: a small plate on the lid course and a hook dropping onto the body. The smallest of
    the six on purpose - six identically sized plates would make the Lock layer read as one variant
    wearing six faces."""
    s = Sprite()
    plate(s, 13, 12, 18, 16, chamfer=False)
    s.ink(15, 14, D2)
    s.ink(16, 14, D2)
    for y in range(17, 21):
        s.ink(14, y, OUTLINE)
        s.ink(15, y, LT)
        s.ink(16, y, MID)
        s.ink(17, y, OUTLINE)
    for x in range(14, 18):
        s.ink(x, 21, OUTLINE)
    s.ink(15, 20, D1)
    s.ink(16, 20, D2)
    return s


def lock_dial():
    """A combination dial: a round face, a ring of ticks and a lit knob. Round, which none of the
    others are - the one silhouette the Lock layer did not have."""
    s = Sprite()
    rows = {14: (14, 17), 15: (13, 18), 16: (12, 19), 17: (12, 19), 18: (12, 19), 19: (12, 19),
            20: (13, 18), 21: (14, 17)}
    shape = {(x, y) for y, (a, b) in rows.items() for x in range(a, b + 1)}
    for x, y in shape:
        s.ink(x, y, LT if (x + y) < 33 else MID)
    for x, y in ((15, 15), (16, 15), (13, 17), (18, 18), (15, 20), (16, 20)):
        s.ink(x, y, D2)                                # the ticks
    for x, y in ((15, 17), (16, 17), (15, 18), (16, 18)):
        s.ink(x, y, MID)                               # the knob
    s.ink(15, 17, HI)
    s.ink(16, 18, D1)
    s.outline(shape)
    s.ink(14, 22, OUTLINE)                             # a short stem under the dial
    s.ink(15, 22, D1)
    s.ink(16, 22, D1)
    s.ink(17, 22, OUTLINE)
    return s


def lock_crest():
    """A shield-shaped hasp with a keyhole: square at the shoulders, pointed at the foot."""
    s = Sprite()
    rows = {14: (12, 19), 15: (12, 19), 16: (12, 19), 17: (12, 19), 18: (12, 19), 19: (12, 19),
            20: (13, 18), 21: (13, 18), 22: (14, 17), 23: (15, 16)}
    shape = {(x, y) for y, (a, b) in rows.items() for x in range(a, b + 1)}
    for x, y in shape:
        s.ink(x, y, LT if x < 16 else MID)            # the shield's two halves, lit and shaded
    for y in range(15, 23):
        s.ink(15, y, HI if y < 19 else LT)            # the ridge down the middle
    keyhole(s, 15, 16)
    s.outline(shape)
    return s


# ---------------------------------------------------------------------------- trim (custom)
# Full color, composited exactly as drawn - a Custom layer is never colorized, which is why these
# are the only sprites in the set that state a color at all. Shades are three steps each, dark to
# light, and every highlight run is shorter than the run beneath it, so the metal reads as metal.
GOLD = ((150, 108, 36), (214, 168, 68), (246, 214, 120))
PEARL = ((150, 140, 128), (214, 206, 194), (250, 248, 240))
GEMS = (((150, 32, 52), (206, 54, 72), (244, 128, 140)),
        ((34, 74, 150), (72, 132, 214), (146, 196, 248)))


def trim_gems():
    """A cut stone on each shoulder of the lid course in a gold setting. Three pixels across is the
    smallest a gem can be and still show a facet."""
    s = Sprite()
    for x0, (dark, base, light) in zip((5, 24), GEMS):
        for dx in range(-1, 4):
            s.rgb(x0 + dx, 12, GOLD[1] if 0 <= dx < 3 else GOLD[0])
            s.rgb(x0 + dx, 16, GOLD[0])
        for dy in range(1, 4):
            s.rgb(x0 - 1, 12 + dy, GOLD[1])
            s.rgb(x0 + 3, 12 + dy, GOLD[0])
        s.rgb(x0, 13, light)
        s.rgb(x0 + 1, 13, base)
        s.rgb(x0 + 2, 13, base)
        s.rgb(x0, 14, base)
        s.rgb(x0 + 1, 14, base)
        s.rgb(x0 + 2, 14, dark)
        s.rgb(x0, 15, base)
        s.rgb(x0 + 1, 15, dark)
        s.rgb(x0 + 2, 15, dark)
    return s


def trim_gilt():
    """Gold along the lip and the plinth, with corner pips on the lid course."""
    s = Sprite()
    for x in range(2, 30):
        s.rgb(x, 14, GOLD[2] if 4 <= x <= 27 else GOLD[1])
        s.rgb(x, 15, GOLD[0])
        s.rgb(x, 27, GOLD[1] if 4 <= x <= 27 else GOLD[0])
        s.rgb(x, 28, GOLD[0])
    for x, y in ((3, 12), (4, 12), (27, 12), (28, 12), (3, 13), (28, 13)):
        s.rgb(x, y, GOLD[1])
    return s


def trim_pearls():
    """A string of pearls beaded along the lip, with a drop at each end. A pearl is two by two - lit,
    body, body, shade - and the string between pearls is one dark pixel, so the row reads as beads
    on a thread rather than as a dashed line."""
    s = Sprite()
    for x0 in range(3, 28, 3):
        s.rgb(x0, 14, PEARL[2])
        s.rgb(x0 + 1, 14, PEARL[1])
        s.rgb(x0, 15, PEARL[1])
        s.rgb(x0 + 1, 15, PEARL[0])
        if x0 + 2 <= 28:
            s.rgb(x0 + 2, 14, PEARL[0])
    for x in (4, 26):                                 # the drops
        s.rgb(x, 16, PEARL[0])
        s.rgb(x, 17, PEARL[2])
        s.rgb(x + 1, 17, PEARL[1])
        s.rgb(x, 18, PEARL[1])
        s.rgb(x + 1, 18, PEARL[0])
    return s


def trim_filigree():
    """Gold scrollwork in the four body corners: an L with a curl at each end. Kept clear of the
    middle, where the lock lands - trim half-covered by a lock reads as a bug."""
    s = Sprite()
    curl = [(0, 0), (1, 0), (2, 0), (3, 0), (0, 1), (0, 2), (0, 3), (3, 1), (2, 2), (1, 3)]
    for cx, cy, sx, sy in ((5, 18, 1, 1), (26, 18, -1, 1), (5, 25, 1, -1), (26, 25, -1, -1)):
        for dx, dy in curl:
            lit = (dx == 0 and sx > 0) or (dy == 0 and sy > 0)
            s.rgb(cx + sx * dx, cy + sy * dy, GOLD[2] if lit else GOLD[1])
        s.rgb(cx + sx * 2, cy + sy * 1, GOLD[0])
        s.rgb(cx + sx * 1, cy + sy * 2, GOLD[0])
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
    for x, y in ((5, 4), (26, 4), (16, 2)):
        star(s, x, y)
    for x, y in ((1, 19), (30, 21)):
        star(s, x, y, arm=1)
    for x, y in ((10, 3), (22, 5), (1, 11), (30, 12)):
        s.ink(x, y, LT)
    return s


def glow_runes():
    """Four marks, one per corner, each a small glyph rather than a blob - at this size a soft glow
    is just a smudge, and a rune is something the eye can name."""
    s = Sprite()
    for cx, cy in ((4, 4), (27, 4)):
        for d in range(-1, 2):
            s.ink(cx + d, cy, MID)
            s.ink(cx, cy + d, MID)
        s.ink(cx, cy, HI)
        s.ink(cx - 2, cy - 2, D1)
        s.ink(cx + 2, cy + 2, D1)
    for x, y in ((16, 2), (1, 16), (30, 16)):
        s.ink(x, y, LT)
        s.ink(x, y - 1, D1)
        s.ink(x, y + 1, D1)
    return s


# ---------------------------------------------------------------------------- feet (custom)
# Rows 29-30, BELOW the plinth at 28, so these clear the y >= 12 overlay contract by a mile and can
# never collide with either lid - the two silhouettes differ only above row 12.
#
# CUSTOM, not dynamic, and the reason is arithmetic rather than taste: a dynamic layer multiplies
# the book's DNA space by its variants AND its color buckets, so a dynamic Feet layer would have been
# the most expensive way to add the least visible variety. Custom multiplies by variants alone, and
# it gives the demo a second Custom example beside the trim.
# Aged pewter, deliberately NEUTRAL-WARM rather than blue-gray: a hair of brown lets one pair of feet
# belong under both timber and metal bodies.
IRONW = ((76, 71, 66), (121, 114, 105), (168, 159, 147))


def _foot(s, x0, x1, ramp=IRONW):
    """One foot: a lit top row under the plinth and a darker row it stands on."""
    for x in range(x0, x1 + 1):
        s.rgb(x, 29, ramp[1])
        s.rgb(x, 30, ramp[0])
    s.rgb(x0, 29, ramp[2])
    s.rgb(x1, 29, ramp[0])


def feet_blocks():
    """Two square blocks under the corners - the plain answer, and the one that reads at 32px."""
    s = Sprite()
    _foot(s, 4, 8)
    _foot(s, 23, 27)
    return s


def feet_skids():
    """One continuous rail, like a crate on runners. Reads as a different OBJECT rather than as the
    blocks with more pixels, which is the whole test for a second variant at this size."""
    s = Sprite()
    _foot(s, 4, 27)
    return s


def feet_claws():
    """Gilt ball feet, a pair of round pads under each end: the parlor chest to the blocks' cellar."""
    s = Sprite()
    for x0 in (4, 24):
        s.rgb(x0, 29, GOLD[2])
        s.rgb(x0 + 1, 29, GOLD[1])
        s.rgb(x0 + 2, 29, GOLD[1])
        s.rgb(x0 + 3, 29, GOLD[0])
        s.rgb(x0 + 1, 30, GOLD[0])
        s.rgb(x0 + 2, 30, GOLD[0])
    return s


# ---------------------------------------------------------------------------- the set
TEXTURES = {"planked": tex_planked, "plated": tex_plated, "slatted": tex_slatted,
            "paneled": tex_paneled, "stone": tex_stone}

SPEC = {
    "chestbody": {v: (lambda t=t: make_body(CHEST_LID, CHEST_LID_V, t))
                  for v, t in TEXTURES.items()},
    "boxbody": {v: (lambda t=t: make_body(BOX_LID, BOX_LID_V, t))
                for v, t in TEXTURES.items() if v != "stone"},
    "bands": {"straps": band_straps, "corners": band_corners, "hoops": band_hoops,
              "frame": band_frame, "studs": band_studs},
    "lock": {"keyhole": lock_keyhole, "padlock": lock_padlock, "keypad": lock_keypad,
             "latch": lock_latch, "dial": lock_dial, "crest": lock_crest},
    "trim": {"gems": trim_gems, "gilt": trim_gilt, "pearls": trim_pearls,
             "filigree": trim_filigree},
    "glow": {"sparks": glow_sparks, "runes": glow_runes},
    "feet": {"blocks": feet_blocks, "skids": feet_skids, "claws": feet_claws},
}

# ---------------------------------------------------------------------------- depth (voxels)
# One voxel of paper sits at z in [-0.5, 0.5]; a front of F reaches 0.5+F toward the viewer and a
# back of B reaches 0.5+B away. Every body is built from (front, back) pairs with back = front + 1,
# so each row is CENTERED on one plane and the body reads as a box, not a facade with a deep back.
#
#   lid course 12-13     6 / 7    (the lid is a voxel fuller than the body all round)
#   lip 14-15            7 / 8    (and the lip a voxel fuller than the lid: the overhang)
#   body 16-26           5 / 6    (12 voxels deep - a chest, not a slab)
#   plinth 27-28         6 / 7    (answers the lip at the foot)
#
# The outermost column of every row is a voxel shy front AND back - a bevel on every corner edge.
LOWER_DEPTH = {12: (6, 7), 13: (6, 7), 14: (7, 8), 15: (7, 8), 27: (6, 7), 28: (6, 7)}
for _y in range(16, 27):
    LOWER_DEPTH[_y] = (5, 6)

# The barrel: a half-cylinder of radius ~7 seen from the side, read per row. It closes in front AND
# back alike, so the dome is the same curve from either side; the straight rows at 11-13 are where
# the arc meets the lid course.
CHEST_DEPTH = {7: (2, 3), 8: (4, 5), 9: (5, 6), 10: (5, 6), 11: (6, 7)}
# The slab: flat front and back at the lid course's depth, with the top row chamfered a voxel in.
BOX_DEPTH = {8: (5, 6), 9: (6, 7), 10: (6, 7), 11: (6, 7)}


def bevelled(table, rows, x, y):
    row = rows.get(y)
    if row is None or not row[0] <= x <= row[1]:
        return (0, 0)
    f, b = table[y]
    return (f - 1, b - 1) if x in row else (f, b)


def lower_front(x, y):
    """THE shared front profile at y>=12."""
    return bevelled(LOWER_DEPTH, LOWER_ROWS, x, y)[0]


def lower_back(x, y):
    """THE shared back profile at y>=12."""
    return bevelled(LOWER_DEPTH, LOWER_ROWS, x, y)[1]


def body_depth_at(x, y, lid):
    if y >= 12:
        return bevelled(LOWER_DEPTH, LOWER_ROWS, x, y)
    if lid == "chest":
        return bevelled(CHEST_DEPTH, CHEST_LID, x, y)
    return bevelled(BOX_DEPTH, BOX_LID, x, y)


def body_front(x, y, lid):
    return body_depth_at(x, y, lid)[0]


def depth_picture(sprite, fn):
    """(W,H) RGB picture: R front, G back. `fn(x, y, rgba) -> (front, back)` is asked ONLY for solid
    pixels, so no depth can land on a transparent one."""
    im = Image.new("RGB", (W, H), (0, 0, 0))
    out = im.load()
    for y in range(H):
        for x in range(W):
            rgba = sprite.px[x, y]
            if rgba[3] == 0:
                continue
            f, b = fn(x, y, rgba)
            assert 0 <= f <= 255 and 0 <= b <= 255, (x, y, f, b)
            out[x, y] = (f, b, 0)
    return im


def body_depth(lid):
    return lambda sp: depth_picture(sp, lambda x, y, c: body_depth_at(x, y, lid))


def over(k):
    """An overlay `k` voxels proud of the body beneath it (absolute: body_front + k). Its back is 0:
    the union rule keeps the body's back behind it."""
    return lambda sp: depth_picture(sp, lambda x, y, c: (lower_front(x, y) + k, 0))


def lock_padlock_depth(sp):
    # The shackle (above the plate, y < 16) is the most proud thing on the chest.
    return depth_picture(sp, lambda x, y, c: (lower_front(x, y) + (3 if y < 16 else 2), 0))


def lock_dial_depth(sp):
    # The knob stands a voxel off the dial's face.
    return depth_picture(sp, lambda x, y, c: (lower_front(x, y) + (3 if 15 <= x <= 16 and 17 <= y <= 18 else 2), 0))


def gems_depth(sp):
    # Set stones stand a voxel above the gold that holds them.
    return depth_picture(sp, lambda x, y, c: (lower_front(x, y) + (1 if c[:3] in GOLD else 2), 0))


def feet_depth(front, back):
    return lambda sp: depth_picture(sp, lambda x, y, c: (front, back))


# Glow has no entry on purpose: it stays flat paper, and a test pins that.
DEPTH = {
    "chestbody": {v: body_depth("chest") for v in SPEC["chestbody"]},
    "boxbody": {v: body_depth("box") for v in SPEC["boxbody"]},
    "bands": {v: over(1) for v in SPEC["bands"]},
    "lock": {"keyhole": over(2), "latch": over(2), "padlock": lock_padlock_depth,
             "keypad": over(2), "dial": lock_dial_depth, "crest": over(2)},
    "trim": {"gilt": over(1), "gems": gems_depth, "pearls": over(1), "filigree": over(1)},
    # Feet stand under the plinth a voxel in from it all round; the skids run its full depth.
    "feet": {"blocks": feet_depth(4, 5), "skids": feet_depth(5, 6), "claws": feet_depth(3, 4)},
}


def check_contract(pictures):
    """The python twin of the C# proud test: fail at generation, not after a rebuild."""
    for layer in ("chestbody", "boxbody"):
        for vid, (sp, pic) in pictures[layer].items():
            for y in range(12, H):
                for x in range(W):
                    if sp.px[x, y][3]:
                        assert pic.getpixel((x, y))[1] == lower_back(x, y), \
                            "body %s/%s breaks the shared back at (%d,%d)" % (layer, vid, x, y)
                        assert pic.getpixel((x, y))[0] == lower_front(x, y), \
                            "body %s/%s breaks the shared profile at (%d,%d)" % (layer, vid, x, y)
    for layer in ("bands", "lock", "trim"):
        for vid, (sp, pic) in pictures[layer].items():
            for y in range(H):
                for x in range(W):
                    if sp.px[x, y][3]:
                        assert y >= 12, "%s/%s is drawn above row 12 at (%d,%d)" % (layer, vid, x, y)
                        assert lower_front(x, y) > 0, "%s/%s hangs off the body at (%d,%d)" % (layer, vid, x, y)
                        assert pic.getpixel((x, y))[1] == 0, "%s/%s has a back at (%d,%d)" % (layer, vid, x, y)
                        for lid in ("chest", "box"):
                            assert pic.getpixel((x, y))[0] > body_front(x, y, lid), \
                                "%s/%s is not proud of the %s body at (%d,%d)" % (layer, vid, lid, x, y)


def main():
    n = 0
    pictures = {}
    for layer, variants in SPEC.items():
        for vid, fn in variants.items():
            sprite = fn()
            sprite.save(os.path.join(OUT, layer, vid + ".png"))
            n += 1
            if layer in DEPTH:
                pictures.setdefault(layer, {})[vid] = (sprite, DEPTH[layer][vid](sprite))
    check_contract(pictures)
    for layer, variants in pictures.items():
        for vid, (_, pic) in variants.items():
            path = os.path.join(OUT, "depth", layer, vid + ".png")
            os.makedirs(os.path.dirname(path), exist_ok=True)
            pic.save(path)
    print("drew %d variants at %dx%d into %s" % (n, W, H, OUT))


if __name__ == "__main__":
    main()
