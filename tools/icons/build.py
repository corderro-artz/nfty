"""Generates src/Nfty.App/Themes/Icons.axaml from the SVG sources in assets/icons/.

The SVGs are the drawings; Icons.axaml is a build product in everything but the fact that it is
committed (Avalonia cannot render SVG without a library, and adding one to draw 45 static glyphs
would be a poor trade). Edit the SVG, run this, commit both — IconSourceTests fails the build if the
two ever disagree, so the generated half can never quietly become the real one.

    python tools/icons/build.py
"""
import io
import os
import re
import sys

SRC = os.path.join('assets', 'icons')
OUT = os.path.join('src', 'Nfty.App', 'Themes', 'Icons.axaml')

HEADER = '''<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <!--
    GENERATED FROM assets/icons/*.svg BY tools/icons/build.py - DO NOT HAND-EDIT.
    Edit the SVG and re-run the script. IconSourceTests asserts the two agree, so an edit made here
    instead fails the build rather than silently becoming the truth.

    Every glyph is a 24x24 stroked path (stroke-width 2, round caps and joins), which is why the SVG
    sources are the natural form to keep: they are the same drawing, viewable in any tool, and they
    scale without limit. The app previously substituted Unicode/emoji characters, which rendered as
    color emoji injecting off-palette hues (the toolbar padlock sampled #f6cd6a, the edit pencil
    #ff822d) and, where the shipped fonts lacked the glyph, as literal tofu boxes.

    Paths stay in the original 24-unit coordinate space; the `Path.ico` style in Styles.axaml maps
    that shared 24-box onto the 12/13/14/18px icon box the way an <svg viewBox> does, so every glyph
    is scaled by one factor rather than by its own bounds.
  -->
'''


def key_of(svg_text, stem):
    m = re.search(r'<title>(\w+)</title>', svg_text)
    if m:
        return m.group(1)
    return 'Icon' + ''.join(p.capitalize() for p in stem.split('-'))


def note_of(svg_text):
    """The provenance line, made safe to sit inside an XML comment.

    A `--` sequence is illegal inside an XML comment and ends it early: one of these notes carried a
    quoted `<!--` from the file it was lifted out of, which terminated the generated comment
    mid-sentence and left the remainder as stray text in the ResourceDictionary. Avalonia reported it
    as AVLN3000 "unable to find suitable setter", forty lines from the cause.
    """
    m = re.search(r'<desc>(.*?)</desc>', svg_text, re.S)
    if not m:
        return ''
    note = ' '.join(m.group(1).split())
    note = note.replace('&lt;', '<').replace('&gt;', '>').replace('&amp;', '&')
    note = note.replace('<!--', '').replace('-->', '')
    while '--' in note:
        note = note.replace('--', '-')
    return note.strip()


def path_of(svg_text, where):
    paths = re.findall(r'<path[^>]*\bd="([^"]+)"', svg_text)
    if len(paths) != 1:
        raise SystemExit(
            '%s: expected exactly one <path>, found %d. StreamGeometry is a single geometry, so a '
            'glyph must be authored as one path - join the subpaths with M commands.'
            % (where, len(paths)))
    return ' '.join(paths[0].split())


def build():
    if not os.path.isdir(SRC):
        raise SystemExit('no %s directory' % SRC)

    entries = []
    for name in sorted(os.listdir(SRC)):
        if not name.endswith('.svg'):
            continue
        full = os.path.join(SRC, name)
        text = io.open(full, encoding='utf-8').read()
        entries.append((key_of(text, name[:-4]), note_of(text), path_of(text, full)))

    if not entries:
        raise SystemExit('no .svg files in %s' % SRC)

    body = []
    for key, note, d in entries:
        if note:
            body.append('  <!-- %s -->' % note)
        body.append('  <StreamGeometry x:Key="%s">%s</StreamGeometry>' % (key, d))
        body.append('')

    out = HEADER + '\n' + '\n'.join(body) + '</ResourceDictionary>\n'
    io.open(OUT, 'w', encoding='utf-8', newline='\n').write(out)
    print('wrote %s from %d svg sources' % (OUT, len(entries)))


if __name__ == '__main__':
    sys.exit(build())
