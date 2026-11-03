"""Cut the captured 1392x768 frames into the figures the manual actually uses.

Both themes are cropped with the SAME box -- the two frames are the same layout, so a figure that
lines up in one lines up in the other, and Material swaps them on #only-light / #only-dark.
"""
import os, sys
from PIL import Image

SRC, DST = sys.argv[1], sys.argv[2]

# name -> (source frame, crop box or None for the whole frame)
FIGURES = {
    "landing":            ("landing", None),
    "kitchen-shelf":      ("landing-kitchen", (2, 600, 1390, 730)),
    "explorer":           ("explorer-cookbook", None),
    "cookbook-panel":     ("explorer-cookbook", (344, 156, 1392, 734)),
    "recipe-panel":       ("explorer-recipe", None),
    "layer-stack":        ("explorer-recipe", (360, 370, 1046, 732)),
    "rules":              ("explorer-recipe", (1060, 176, 1374, 350)),
    "ingredient-panel":   ("explorer-ingredient", None),
    "colorways":          ("explorer-ingredient", (1036, 176, 1374, 520)),
    "variant-weights":    ("explorer-ingredient", (360, 436, 1020, 668)),
    "layers-unlocked":    ("recipe-unlocked", (360, 370, 1046, 732)),
    "editor":             ("editor", None),
    "toolstrip":          ("editor", (318, 46, 1030, 100)),
    "palette-strip":      ("editor-color", (318, 100, 1030, 182)),
    "colorize-rail":      ("editor", (1034, 46, 1392, 676)),
    "editor-color":       ("editor-color", None),
    "reference-layers":   ("editor-references", (1034, 46, 1392, 676)),
    "cook-dialog":        ("cook-dialog", (438, 130, 954, 650)),
    "cook-done":          ("cook-done", (438, 130, 954, 650)),
    "set-browser":        ("set-browser", None),
    "inspector":          ("inspector", (181, 60, 1213, 719)),
    "rarity":             ("set-browser", (1012, 124, 1372, 714)),
    "book-settings":      ("book-settings", (169, 96, 1225, 682)),
    "settings":           ("settings", (438, 271, 954, 507)),
    "export":             ("export", (277, 85, 1117, 693)),
    "new-cookbook":       ("wizard-cookbook", (169, 104, 1225, 674)),
    # The Quick Reference has four tabs and a search box; one figure each for the ones the manual
    # leans on. All three are the same 1248x608 card.
    "help-sheet":         ("help-sheet", (73, 86, 1321, 694)),
    "help-editor":        ("help-editor", (73, 86, 1321, 694)),
    "help-search":        ("help-search", (73, 86, 1321, 694)),
    "rise-page":          ("rise-page", None),
    "rise-recipe":        ("rise-recipe", None),
    "rise-picture":       ("rise-picture", None),
    "rise-3d-tile":       ("rise-set", (856, 468, 1000, 612)),
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
