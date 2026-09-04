"""Cut the captured 1392x840 frames into the figures the manual actually uses.

Both themes are cropped with the SAME box -- the two frames are the same layout, so a figure that
lines up in one lines up in the other, and Material swaps them on #only-light / #only-dark.
"""
import os, sys
from PIL import Image

SRC, DST = sys.argv[1], sys.argv[2]

# name -> (source frame, crop box or None for the whole frame)
FIGURES = {
    "landing":            ("landing", None),
    "kitchen-shelf":      ("landing-kitchen", (16, 754, 1376, 882)),
    "explorer":           ("explorer-cookbook", None),
    "cookbook-panel":     ("explorer-cookbook", (352, 160, 1392, 700)),
    "recipe-panel":       ("explorer-recipe", None),
    "layer-stack":        ("explorer-recipe", (360, 350, 1000, 710)),
    "rules":              ("explorer-recipe", (1014, 175, 1378, 320)),
    "ingredient-panel":   ("explorer-ingredient", None),
    "colorways":          ("explorer-ingredient", (1030, 175, 1382, 430)),
    "variant-weights":    ("explorer-ingredient", (360, 375, 1020, 550)),
    "layers-unlocked":    ("recipe-unlocked", (360, 390, 1000, 710)),
    "editor":             ("editor", None),
    "toolstrip":          ("editor", (318, 44, 1028, 104)),
    "palette-strip":      ("editor", (318, 102, 1028, 146)),
    "colorize-rail":      ("editor", (1034, 100, 1388, 620)),
    "editor-color":       ("editor-color", None),
    "reference-layers":   ("editor-references", (1036, 145, 1386, 795)),
    "cook-dialog":        ("cook-dialog", (470, 318, 930, 612)),
    "cook-done":          ("cook-done", (470, 372, 930, 545)),
    "set-browser":        ("set-browser", None),
    "inspector":          ("inspector", (156, 90, 1236, 830)),
    "rarity":             ("set-browser", (1006, 108, 1384, 672)),
    # The quick-reference sheet outgrew the 864px capture when it gained Kitchen and a second
    # KEYS group, so ITS frame is shot at `pair.py help <dir> 1010` and cropped from that.
    "help-sheet":         ("help", (202, 92, 1184, 898)),   # from the 1010-tall run
    "new-cookbook":       ("wizard-cookbook", (326, 116, 1076, 796)),
}

for name, (frame, box) in FIGURES.items():
    for theme in ("light", "dark"):
        src = os.path.join(SRC, f"{frame}-{theme}.png")
        if not os.path.exists(src):
            print("  MISSING", src); continue
        im = Image.open(src)
        if box:
            im = im.crop(box)
        im.save(os.path.join(DST, f"{name}-{theme}.png"))
    print(f"  {name:20} <- {frame}{'' if box is None else ' cropped'}")
