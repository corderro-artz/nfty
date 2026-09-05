"""Builds the installable typeface bundle the manual offers, from the app's OWN font files.

The point is that there is exactly one copy of these outlines in the repository. The manual carries
woff2 for rendering itself — a different representation, legitimately its own files — but the
download a reader installs has to be the same TTFs the application ships, or the two drift and the
site starts handing out a typeface the app no longer uses. So the zip is BUILT, at site-build time,
from ``src/Nfty.App/Assets/Fonts``.

MkDocs calls the module-level functions below by name; ``on_post_build`` runs once the site
directory exists, which is when there is somewhere to write.
"""
from __future__ import annotations

import logging
import os
import zipfile

log = logging.getLogger("mkdocs.hooks.fonts")

# Relative to the repository root, which is where mkdocs.yml sits and therefore the process's cwd.
FONT_SOURCE = os.path.join("src", "Nfty.App", "Assets", "Fonts")
BUNDLE = os.path.join("assets", "fonts", "ibm-plex-nfty.zip")


def on_post_build(config, **kwargs):  # noqa: ARG001 - MkDocs passes more than this uses
    """Zips the app's typeface files into the built site."""
    if not os.path.isdir(FONT_SOURCE):
        # Warned rather than raised: the manual is buildable on its own, and a missing bundle costs
        # one link rather than the whole site, so a docs-only checkout still works.
        log.warning("fonts: %s not found - the install bundle will be missing", FONT_SOURCE)
        return

    faces = sorted(f for f in os.listdir(FONT_SOURCE) if f.lower().endswith(".ttf"))
    if not faces:
        log.warning("fonts: no .ttf in %s - the install bundle will be missing", FONT_SOURCE)
        return

    out = os.path.join(config["site_dir"], BUNDLE)
    os.makedirs(os.path.dirname(out), exist_ok=True)

    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
        for face in faces:
            z.write(os.path.join(FONT_SOURCE, face), face)
        # The licence travels WITH the fonts. SIL OFL requires it to accompany any redistribution,
        # and handing a reader a zip of eight faces is a redistribution.
        licence = os.path.join(FONT_SOURCE, "OFL.txt")
        if os.path.exists(licence):
            z.write(licence, "OFL.txt")
        else:
            log.warning("fonts: OFL.txt missing from %s - the bundle would ship unlicensed",
                        FONT_SOURCE)

    log.info("fonts: bundled %d faces into %s", len(faces), BUNDLE)
