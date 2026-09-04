"""Draw the demo CookBook's art: 64x64 pixel art, no anti-aliasing anywhere.

Value-map layers (dynamic/static) are drawn in GRAY: the byte is lightness, and nfty supplies the
hue. Custom layers are drawn in full color. Every draw call writes whole pixels -- PIL's default
ellipse/polygon rasterizer is hard-edged, which is what pixel art needs.
"""
import os, sys
from PIL import Image, ImageDraw

W = H = 64
OUT = sys.argv[1]

def img():
    return Image.new("RGBA", (W, H), (0, 0, 0, 0))

def g(v):            # a gray value-map ink
    return (v, v, v, 255)

def save(im, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    im.save(path)

# ---------------------------------------------------------------- backgrounds (dynamic, gray)
def bg_plain():
    im = img(); d = ImageDraw.Draw(im)
    for y in range(H):                      # vertical ramp: dark floor, bright sky
        d.line([(0, y), (W, y)], fill=g(int(70 + 120 * (1 - y / H))))
    return im

def bg_grid():
    im = bg_plain(); d = ImageDraw.Draw(im)
    for x in range(0, W, 8): d.line([(x, 0), (x, H)], fill=g(215))
    for y in range(0, H, 8): d.line([(0, y), (W, y)], fill=g(215))
    return im

def bg_rays():
    im = img(); d = ImageDraw.Draw(im)
    for y in range(H): d.line([(0, y), (W, y)], fill=g(95))
    for i in range(0, 12):                  # fan of rays from the top-left
        d.polygon([(0, 0), (W, i * 11 - 20), (W, i * 11 - 14)], fill=g(180))
    return im

# ---------------------------------------------------------------- bodies (dynamic, gray)
def cat_body():
    im = img(); d = ImageDraw.Draw(im)
    d.ellipse([16, 30, 48, 58], fill=g(150))                       # torso
    d.polygon([(44, 46), (60, 30), (62, 36), (48, 52)], fill=g(140))  # tail
    d.ellipse([20, 12, 44, 36], fill=g(170))                       # head
    d.polygon([(21, 20), (23, 6), (32, 16)], fill=g(150))          # ear L
    d.polygon([(43, 20), (41, 6), (32, 16)], fill=g(150))          # ear R
    d.ellipse([24, 40, 32, 50], fill=g(185))                       # near paw
    d.ellipse([33, 40, 41, 50], fill=g(185))
    for y in range(30, 58):                                        # belly highlight
        d.line([(28, y), (36, y)], fill=g(200))
    return im

def cat_body_sit():
    im = img(); d = ImageDraw.Draw(im)
    d.polygon([(22, 58), (42, 58), (38, 32), (26, 32)], fill=g(150))  # seated wedge
    d.polygon([(40, 56), (58, 44), (60, 50), (44, 60)], fill=g(140))
    d.ellipse([20, 12, 44, 36], fill=g(170))
    d.polygon([(21, 20), (23, 6), (32, 16)], fill=g(150))
    d.polygon([(43, 20), (41, 6), (32, 16)], fill=g(150))
    for y in range(34, 58): d.line([(29, y), (35, y)], fill=g(200))
    return im

def fox_body():
    im = img(); d = ImageDraw.Draw(im)
    d.ellipse([14, 32, 46, 58], fill=g(145))
    d.polygon([(42, 50), (62, 34), (63, 46), (48, 58)], fill=g(160))  # big brush tail
    d.polygon([(56, 36), (63, 34), (63, 42)], fill=g(225))            # white tip
    d.ellipse([20, 14, 46, 38], fill=g(165))
    d.polygon([(22, 22), (20, 4), (34, 16)], fill=g(140))             # tall ears
    d.polygon([(44, 22), (46, 4), (32, 16)], fill=g(140))
    d.polygon([(28, 34), (38, 34), (33, 44)], fill=g(120))            # snout
    for y in range(38, 58): d.line([(28, y), (34, y)], fill=g(205))
    return im

# ---------------------------------------------------------------- eyes (static, gray)
def eyes_round():
    im = img(); d = ImageDraw.Draw(im)
    d.ellipse([25, 22, 30, 28], fill=g(60)); d.ellipse([34, 22, 39, 28], fill=g(60))
    d.point([(26, 23), (35, 23)], fill=g(240))
    return im

def eyes_sleepy():
    im = img(); d = ImageDraw.Draw(im)
    d.rectangle([25, 25, 30, 26], fill=g(60)); d.rectangle([34, 25, 39, 26], fill=g(60))
    return im

def eyes_wink():
    im = img(); d = ImageDraw.Draw(im)
    d.ellipse([25, 22, 30, 28], fill=g(60)); d.point([(26, 23)], fill=g(240))
    d.rectangle([34, 25, 39, 26], fill=g(60))
    return im

# ---------------------------------------------------------------- aura (dynamic, gray)
def aura_glow():
    im = img(); d = ImageDraw.Draw(im)
    for i, v in enumerate((70, 110, 150)):                 # concentric rings, brighter inward
        d.ellipse([6 + i * 4, 4 + i * 4, 58 - i * 4, 60 - i * 4], outline=g(v), width=2)
    return im

def aura_spark():
    im = img(); d = ImageDraw.Draw(im)
    for x, y in ((8, 10), (54, 14), (12, 50), (50, 52), (32, 3), (4, 32), (60, 34), (30, 60)):
        d.line([(x - 3, y), (x + 3, y)], fill=g(190))
        d.line([(x, y - 3), (x, y + 3)], fill=g(190))
        d.point([(x, y)], fill=g(245))
    return im

# ---------------------------------------------------------------- hats (custom, full color)
def hat_crown():
    im = img(); d = ImageDraw.Draw(im)
    d.polygon([(22, 16), (22, 6), (27, 11), (32, 4), (37, 11), (42, 6), (42, 16)],
              fill=(232, 186, 74, 255))
    d.rectangle([22, 14, 42, 17], fill=(198, 150, 44, 255))
    d.point([(27, 11), (37, 11)], fill=(214, 36, 91, 255))
    return im

def hat_cap():
    im = img(); d = ImageDraw.Draw(im)
    d.ellipse([21, 4, 43, 20], fill=(60, 96, 168, 255))
    d.rectangle([21, 14, 52, 18], fill=(44, 74, 134, 255))
    d.rectangle([30, 6, 34, 12], fill=(238, 238, 244, 255))
    return im

def hat_bare():
    return img()                                            # the "no hat" option

SPEC = {
    "bg":   {"plain": bg_plain, "grid": bg_grid, "rays": bg_rays},
    "body": {"prowl": cat_body, "sit": cat_body_sit},
    "fbody": {"stand": fox_body},
    "eyes": {"round": eyes_round, "sleepy": eyes_sleepy, "wink": eyes_wink},
    "aura": {"glow": aura_glow, "spark": aura_spark},
    "hat":  {"crown": hat_crown, "cap": hat_cap, "bare": hat_bare},
}

for layer, variants in SPEC.items():
    for vid, fn in variants.items():
        save(fn(), os.path.join(OUT, layer, vid + ".png"))
print("drew", sum(len(v) for v in SPEC.values()), "variants")
