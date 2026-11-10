"""Nightly: HEAD every listed Set and preview; mark an entry ``"status": "gone"`` after 3 misses in a row.

  health.py entries/ health.json

``health.json`` is ``{"<ownerId>/<slug>": {"misses": n}}``. A 404 or 410 is a miss; any other failure (a network
error, GitHub having a bad night) is neither a miss nor a recovery, so an outage cannot delist anyone. The entry file
stays, with the one extra member, so the sharer can restore it by sharing a fresh Release.
"""
import json
import os
import pathlib
import sys
import urllib.error
import urllib.request

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fetch  # noqa: E402
import gallerylib as g  # noqa: E402

MISSES_TO_GONE = 3


def head(url, opener=None):
    """``"ok"``, ``"gone"`` (404/410) or ``"unknown"`` (anything else); never an address outside GitHub."""
    if not fetch.host_allowed(url):
        return "unknown"
    opener = opener or fetch.default_opener()
    req = urllib.request.Request(url, method="HEAD", headers={"User-Agent": "nfty-gallery-health"})
    try:
        with opener.open(req, timeout=fetch.TIMEOUT):
            return "ok"
    except urllib.error.HTTPError as e:
        return "gone" if e.code in (404, 410) else "unknown"
    except (urllib.error.URLError, OSError):
        return "unknown"


def run(entries_dir, health_path, check=head):
    """Updates the counters and entry files; returns the ids newly marked gone."""
    try:
        with open(health_path, encoding="utf-8") as f:
            counters = json.load(f)
    except (OSError, ValueError):
        counters = {}
    newly = []
    for path in sorted(pathlib.Path(entries_dir).glob("*/*.json")):
        try:
            raw = json.loads(path.read_text(encoding="utf-8"))
            entry = g.parse_entry(raw)
        except (OSError, ValueError, g.Unreadable):
            continue
        if raw.get("status") == "gone":
            continue
        key = g.entry_id(entry)
        results = {check(entry["set"]["url"]), check(entry["preview"]["url"])}
        if "gone" in results:
            misses = counters.get(key, {}).get("misses", 0) + 1
        elif results == {"ok"}:
            misses = 0
        else:
            misses = counters.get(key, {}).get("misses", 0)
        if misses >= MISSES_TO_GONE:
            path.write_bytes(g.entry_json(entry, status="gone"))
            counters.pop(key, None)
            newly.append(key)
        elif misses:
            counters[key] = {"misses": misses}
        else:
            counters.pop(key, None)
    with open(health_path, "w", encoding="utf-8", newline="\n") as f:
        f.write(g.stj_dumps(dict(sorted(counters.items()))) + "\n")
    return newly


def main(argv=None):
    argv = sys.argv[1:] if argv is None else argv
    if len(argv) != 2:
        print(__doc__, file=sys.stderr)
        return 2
    for key in run(argv[0], argv[1]):
        print("gone:", key)
    return 0


if __name__ == "__main__":
    sys.exit(main())
