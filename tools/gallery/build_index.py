"""Builds the gallery's ``index.json`` from ``entries/`` (byte-identical to ``CommunityIndex.Write``).

  build_index.py entries/ [--blocked blocked.json] [--generated 2026-11-09T14:10:00Z] > site/index.json

Entries sorted by owner id (numeric) then slug (ordinal), so a rebuild of the same entries is the same bytes.
An entry marked ``"status": "gone"`` (health.py) and anything ``blocked.json`` names is left out; the entry file
itself stays. An entry that cannot be read stops the build: a bad file in main must be seen, not published around.
"""
import argparse
import datetime
import json
import os
import pathlib
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gallerylib as g  # noqa: E402


def build(entries_dir, generated, blocked=None):
    blocked = blocked or {}
    users = {int(u) for u in blocked.get("users", [])}
    sets = set(blocked.get("sets", []))
    kept = []
    for path in sorted(pathlib.Path(entries_dir).glob("*/*.json")):
        with open(path, encoding="utf-8") as f:
            raw = json.load(f)
        try:
            entry = g.parse_entry(raw)
        except g.Unreadable:
            raise SystemExit("%s is not a gallery entry; fix or remove it." % path) from None
        if raw.get("status") == "gone" or entry["owner"]["id"] in users or entry["set"]["sha256"] in sets:
            continue
        kept.append(entry)
    return g.index_json(kept, generated)


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("entries")
    ap.add_argument("--blocked")
    ap.add_argument("--generated")
    a = ap.parse_args(argv)
    generated = a.generated or datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    blocked = None
    if a.blocked:
        with open(a.blocked, encoding="utf-8") as f:
            blocked = json.load(f)
    sys.stdout.buffer.write(build(a.entries, generated, blocked))
    return 0


if __name__ == "__main__":
    sys.exit(main())
