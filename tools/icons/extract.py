"""One-shot: lifts the existing StreamGeometry data out of Icons.axaml into SVG source files.

Run once, to seed assets/icons/ from what the app already draws. After that the SVGs are the source
and ``build.py`` generates Icons.axaml from them, never the other way round.
"""
import io
import os
import re

SRC = os.path.join('src', 'Nfty.App', 'Themes', 'Icons.axaml')
DEST = os.path.join('assets', 'icons')

TEMPLATE = '''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" width="24" height="24"
     fill="none" stroke="currentColor" stroke-width="2"
     stroke-linecap="round" stroke-linejoin="round">
  <title>{name}</title>
  <desc>{note}</desc>
  <path d="{d}"/>
</svg>
'''


def kebab(key):
    """IconChevronLeft -> chevron-left."""
    body = key[4:] if key.startswith('Icon') else key
    return re.sub(r'(?<!^)(?=[A-Z])', '-', body).lower()


def main():
    text = io.open(SRC, encoding='utf-8').read()
    os.makedirs(DEST, exist_ok=True)

    found = 0
    for m in re.finditer(
            r'(?:<!--(.*?)-->\s*)?<StreamGeometry x:Key="(\w+)">(.*?)</StreamGeometry>',
            text, re.S):
        note = ' '.join((m.group(1) or '').split())
        key, d = m.group(2), m.group(3).strip()
        out = os.path.join(DEST, kebab(key) + '.svg')
        io.open(out, 'w', encoding='utf-8', newline='\n').write(
            TEMPLATE.format(name=key, note=note.replace('&', '&amp;').replace('<', '&lt;'), d=d))
        found += 1

    print('wrote %d svg files to %s' % (found, DEST))


if __name__ == '__main__':
    main()
