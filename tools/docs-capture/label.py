"""Name each capture pair by what it actually IS, not by the order it was taken.

Ctrl+T is sent blind, and a keystroke that misses (focus lost for a moment) silently swaps the pair
-- the "-dark" file then holds the light theme and nothing downstream notices. Read the titlebar's
own pixel instead and let the image decide its name.
"""
import sys, os, glob
from PIL import Image

d = sys.argv[1]
pairs = sorted({f.rsplit('-', 1)[0] for f in glob.glob(os.path.join(d, '*-dark.png')) +
                glob.glob(os.path.join(d, '*-light.png'))})

def lum(path):
    im = Image.open(path).convert('RGB')
    # A titlebar pixel clear of the wordmark and the window buttons: chrome, not content.
    r, g, b = im.getpixel((im.width // 2, 12))
    return 0.2126 * r + 0.7152 * g + 0.0722 * b

for base in pairs:
    a, b = base + '-dark.png', base + '-light.png'
    if not (os.path.exists(a) and os.path.exists(b)):
        print('  skip (unpaired):', os.path.basename(base)); continue
    if lum(a) > lum(b):                      # the "dark" file is the brighter one -> swapped
        tmp = base + '-swap.tmp'
        os.replace(a, tmp); os.replace(b, a); os.replace(tmp, b)
        print('  SWAPPED:', os.path.basename(base))
    else:
        print('  ok:', os.path.basename(base), f'{lum(a):.0f} / {lum(b):.0f}')
