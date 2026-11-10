"""Choose the nfty release whose CLI checks a share.

check.yml lists nfty's published releases (tag names, newest first) and asks this script which one to use:
the PINNED version when the gallery's NFTY_VERSION repository variable is set, otherwise the highest release
that is at least MIN (the first release with `share check`). A new nfty release therefore reaches the gallery
on its next check with nothing to edit. Prints the bare version (no "v"); exits 1 with a reason when none fits.

Stdlib only; reads data, runs nothing.
"""

import argparse
import re
import sys

_VERSION = re.compile(r"^v?(\d+)\.(\d+)\.(\d+)$")


def parse(text):
    """'v2.6.0' or '2.6.0' -> (2, 6, 0); anything else (pre-releases included) -> None."""
    m = _VERSION.match(text.strip())
    return tuple(int(p) for p in m.groups()) if m else None


def choose(tags, minimum, pinned=""):
    """The version to use, as 'X.Y.Z', or raise ValueError naming why none fits."""
    floor = parse(minimum)
    if floor is None:
        raise ValueError(f"NFTY_MIN_VERSION '{minimum}' is not X.Y.Z")
    if pinned and pinned.strip():
        want = parse(pinned)
        if want is None:
            raise ValueError(f"NFTY_VERSION '{pinned}' is not X.Y.Z")
        if want < floor:
            raise ValueError(f"NFTY_VERSION {pinned} is older than {minimum}, the first release with share check")
        return "%d.%d.%d" % want
    found = [v for v in (parse(t) for t in tags) if v is not None and v >= floor]
    if not found:
        raise ValueError(f"no published nfty release is {minimum} or newer")
    return "%d.%d.%d" % max(found)


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--tags", required=True, help="file with one release tag per line")
    ap.add_argument("--min", required=True)
    ap.add_argument("--pinned", default="")
    a = ap.parse_args(argv)
    with open(a.tags, encoding="utf-8") as f:
        tags = [line for line in f.read().splitlines() if line.strip()]
    try:
        print(choose(tags, a.min, a.pinned))
    except ValueError as e:
        print(f"::error::{e}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
