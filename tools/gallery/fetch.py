"""Downloads the two files a share's entry points at, so ``check.py`` can read them.

Refuses any address but ``https://github.com/<author-login>/nfty-public/releases/download/<release>/<file>``;
follows only the redirects GitHub itself serves a release download through (https, GitHub's own hosts); stops
reading the moment a file passes its size cap, so a 50 GB file costs 200 MB of traffic, not 50 GB.

  fetch.py --entry <entry.json> --login <author-login> --out <dir>

Writes ``<dir>/set.set``, ``<dir>/preview.png`` and ``<dir>/fetch.json`` (``{"set": {"status", "bytes"}, ...}``,
status one of ok, refused, missing, toolarge). A network failure is not a verdict: it exits 2.
"""
import argparse
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gallerylib as g  # noqa: E402

CHUNK = 1 << 20
TIMEOUT = 60
HOSTS = ("github.com", "githubusercontent.com")


def host_allowed(url):
    """A redirect target: https, and GitHub's own host or one of its ``*.githubusercontent.com``."""
    p = urllib.parse.urlsplit(url)
    host = (p.hostname or "").lower()
    return (p.scheme == "https" and p.username is None and p.port in (None, 443)
            and any(host == h or host.endswith("." + h) for h in HOSTS))


class _Redirects(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        if not host_allowed(newurl):
            raise urllib.error.URLError("redirect to a host the gallery does not download from")
        return super().redirect_request(req, fp, code, msg, headers, newurl)


def default_opener():
    return urllib.request.build_opener(_Redirects)


def download(url, dest, cap, opener=None):
    """Streams ``url`` to ``dest``. Returns ``("ok"|"missing"|"toolarge", bytes_seen)``; deletes a partial file."""
    opener = opener or default_opener()
    req = urllib.request.Request(url, headers={"User-Agent": "nfty-gallery-check"})
    try:
        resp = opener.open(req, timeout=TIMEOUT)
    except urllib.error.HTTPError as e:
        if e.code in (404, 410):
            return "missing", 0
        raise
    seen = 0
    try:
        declared = resp.headers.get("Content-Length")
        if declared is not None and declared.isdigit() and int(declared) > cap:
            return "toolarge", int(declared)
        with open(dest, "wb") as out:
            while True:
                chunk = resp.read(CHUNK)
                if not chunk:
                    break
                seen += len(chunk)
                if seen > cap:
                    out.close()
                    os.remove(dest)
                    return "toolarge", seen
                out.write(chunk)
        return "ok", seen
    finally:
        resp.close()


def run(entry, login, out, opener=None):
    os.makedirs(out, exist_ok=True)
    report = {}
    try:
        e = g.parse_entry(entry)
    except g.Unreadable:
        return report  # check.py names the unreadable entry
    plan = (("set", e["slug"] + ".set", "set.set", g.MAX_SET_BYTES),
            ("preview", g.PREVIEW_ASSET, "preview.png", g.MAX_PREVIEW_BYTES))
    for key, name, local, cap in plan:
        if not g.url_allowed(e[key]["url"], login, e["release"], name):
            report[key] = {"status": "refused", "bytes": 0}
            continue
        status, seen = download(e[key]["url"], os.path.join(out, local), cap, opener)
        report[key] = {"status": status, "bytes": seen}
    return report


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--entry", required=True)
    ap.add_argument("--login", required=True)
    ap.add_argument("--out", required=True)
    a = ap.parse_args(argv)
    try:
        with open(a.entry, encoding="utf-8") as f:
            entry = json.load(f)
        report = run(entry, a.login, a.out)
    except (OSError, ValueError, urllib.error.URLError) as e:
        print("fetch failed: %s" % e, file=sys.stderr)
        return 2
    with open(os.path.join(a.out, "fetch.json"), "w", encoding="utf-8") as f:
        json.dump(report, f)
    return 0


if __name__ == "__main__":
    sys.exit(main())
