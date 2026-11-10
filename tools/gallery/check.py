"""The gallery's pull request checker (spec section 4).

Reads ONLY data: the pull request's file list, the files it changes (fetched by ``gh api``, never checked
out), GitHub's answers about the author and their ``nfty-public`` repo, and the two files the entry points
at (downloaded by ``fetch.py``). Nothing from the pull request is ever executed. Every refusal is a
``ShareRules`` sentence (``gallerylib.SENTENCES`` or the ``nfty share check`` output).

Inputs (JSON files unless noted):
  --pr-files      GET repos/{gallery}/pulls/{n}/files
  --pr            GET repos/{gallery}/pulls/{n}              (number, created_at, head.sha)
  --author        GET users/{login}                          (id, login, created_at)
  --repo          GET repos/{login}/nfty-public plus three members the workflow adds: "branches" [names],
                  "tags" [names], "manifest" (nfty-public.json parsed, or null); the file is `null` when
                  the repo does not exist
  --tree          GET repos/{login}/nfty-public/git/trees/{branch}?recursive=1 (or just its "tree" list)
  --releases      GET repos/{login}/nfty-public/releases
  --open-entries  entry objects of the gallery's OTHER open pull requests (optional; duplicates and rate only)
  --entries-dir   the gallery's entries/ on the base branch          (directory)
  --pr-dir        where the changed files were fetched, at their repo paths (directory)
  --blocked / --tags   blocked.json / tags.json of the base branch
  --fetch         fetch.py's report on the two downloads (optional)
  --set / --preview    the downloaded files
  --nfty          the nfty CLI (a command line, split like a shell would)
Writes comment.md and verdict.json to --out and exits 0 (passes) or 1 (refused).
"""
import argparse
import hashlib
import json
import os
import pathlib
import re
import shlex
import struct
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gallerylib as g  # noqa: E402

ENTRY_PATH = re.compile(r"^entries/([0-9]+)/([a-z0-9-]+)\.json$")
DAY = 86400
NEW_SHARE_LOOKBACK = 6 * 3600  # a new entry's date may not be older than this at PR time (a long upload still fits)


def _load(path):
    if path is None:
        return None
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def _read_entry(path):
    try:
        with open(path, encoding="utf-8") as f:
            return g.parse_entry(json.load(f))
    except (OSError, ValueError, g.Unreadable):
        return None


def _gone(path):
    try:
        with open(path, encoding="utf-8") as f:
            return json.load(f).get("status") == "gone"
    except (OSError, ValueError, AttributeError):
        return False


def base_entries(entries_dir):
    """Every readable entry of the base branch as (entry, is_gone)."""
    found = []
    root = pathlib.Path(entries_dir)
    if root.is_dir():
        for p in sorted(root.glob("*/*.json")):
            e = _read_entry(p)
            if e is not None:
                found.append((e, _gone(p)))
    return found


def utf16_len(s):
    return len(s.encode("utf-16-le")) // 2


def _has_link(s):
    return "://" in s or "www." in s.lower()


def _manifest_shares(doc):
    """The entries of a parsed nfty-public.json, or None when it is not one nfty wrote."""
    try:
        if not isinstance(doc, dict) or doc.get("schemaVersion") != g.SCHEMA or not isinstance(doc.get("shares"), list):
            return None
        return [g.parse_entry(s) for s in doc["shares"]]
    except g.Unreadable:
        return None


def png_size(path):
    with open(path, "rb") as f:
        head = f.read(24)
    if len(head) < 24 or head[:8] != b"\x89PNG\r\n\x1a\n" or head[12:16] != b"IHDR":
        return None
    return struct.unpack(">II", head[16:24])


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def make_cli(nfty):
    cmd = shlex.split(nfty, posix=os.name != "nt")
    cmd = [t[1:-1] if len(t) > 1 and t[0] == t[-1] and t[0] in "\"'" else t for t in cmd]  # non-posix keeps quotes

    def run(args):
        p = subprocess.run(cmd + list(args), capture_output=True, text=True, encoding="utf-8", timeout=1800)
        return p.returncode, p.stdout
    return run


class Verdict:
    def __init__(self):
        self.problems = []
        self.details = []  # (after-problem-index, line)

    def add(self, sentence, *details):
        if sentence not in self.problems:
            self.problems.append(sentence)
            self.details.append(list(details))

    @property
    def ok(self):
        return not self.problems


def evaluate(inp, cli):
    """Applies spec section 4 to one pull request. ``inp`` is a dict of the (already parsed) inputs."""
    v = Verdict()
    author = inp["author"]
    aid, login = author["id"], author["login"]
    pr_created = g.Ts(inp["pr"]["created_at"])

    # 4.1 #1 what the PR touches.
    changed, removed = [], []
    for f in inp["pr_files"]:
        m = ENTRY_PATH.match(f.get("filename", ""))
        if (not m or m.group(1) != str(aid) or f.get("previous_filename") or not g.slug_valid(m.group(2))
                or f.get("status") not in ("added", "modified", "removed")):
            v.add(g.say("EntryFilesNotAllowed"))
            return v
        (removed if f["status"] == "removed" else changed).append(m.group(2))
    if len(changed) > 1 or len(removed) > 1 or not (changed or removed) or set(changed) & set(removed):
        v.add(g.say("EntryFilesNotAllowed"))
        return v

    base = base_entries(inp["entries_dir"])
    mine = {e["slug"]: gone for e, gone in base if e["owner"]["id"] == aid}
    prev_by_slug = {e["slug"]: e for e, _ in base if e["owner"]["id"] == aid}
    if removed and removed[0] not in prev_by_slug:
        v.add(g.say("EntryFilesNotAllowed"))
        return v
    if not changed:
        return v  # a lone deletion of the author's own entry: always allowed

    slug = changed[0]
    entry = _read_entry(os.path.join(inp["pr_dir"], "entries", str(aid), slug + ".json"))
    if entry is None:
        v.add(g.say("EntryUnreadable"))
        return v
    if entry["slug"] != slug:
        v.add(g.say("EntryNameMismatch"))
        return v
    if entry["owner"]["id"] != aid:
        v.add(g.say("EntryFilesNotAllowed"))
        return v
    previous = prev_by_slug.get(slug)
    is_new = previous is None

    # 4.1 #2-#5 who is sharing.
    if pr_created.ticks < g.Ts(author["created_at"]).ticks + g.MIN_ACCOUNT_AGE_DAYS * DAY * g.TICKS:
        v.add(g.say("AccountTooYoung"))
    blocked = inp["blocked"] or {}
    if aid in [_as_int(u) for u in blocked.get("users", [])] or entry["set"]["sha256"] in blocked.get("sets", []):
        v.add(g.say("Blocked"))
    active = {s for s, gone in mine.items() if not gone}
    after = (active - set(removed)) | {slug}
    if len(after) > g.MAX_SHARES:
        v.add(g.say("AtShareLimit"))
    open_entries = []
    for raw in inp.get("open_entries") or []:
        try:
            open_entries.append(g.parse_entry(raw))
        except g.Unreadable:
            pass  # untrusted: an unreadable one is simply not counted
    if is_new:
        window = pr_created.ticks - DAY * g.TICKS
        recent = [e for e, _ in base if e["owner"]["id"] == aid and e["slug"] != slug
                  and g.Ts(e["created"]).ticks >= window]
        recent += [e for e in open_entries if e["owner"]["id"] == aid and e["slug"] != slug
                   and e["slug"] not in mine and g.Ts(e["created"]).ticks >= window]
        if recent:
            v.add(g.say("OneNewShareADay"))

    # 5.1 dates.
    if g.dates_problem(entry, previous, pr_created):
        v.add(g.say("DatesMismatch"))
    elif is_new and (entry["created"] != entry["updated"]
                     or g.Ts(entry["updated"]).ticks < pr_created.ticks - NEW_SHARE_LOOKBACK * g.TICKS):
        v.add(g.say("DatesMismatch"))

    # 4.2 the repo.
    repo = inp["repo"]
    if (entry["repo"].lower() != f"{login}/{g.REPO_NAME}".lower() or repo is None
            or str(repo.get("full_name", "")).lower() != f"{login}/{g.REPO_NAME}".lower()
            or (repo.get("owner") or {}).get("id") != aid):
        v.add(g.say("RepoNotYours"))
    else:
        manifest = _manifest_shares(repo.get("manifest"))
        pending = [e["release"] for e in (previous, prev_by_slug.get(removed[0]) if removed else None) if e]
        extras, blocker = g.scan({
            "repo": {"default_branch": repo["default_branch"], "private": repo["private"], "size": repo["size"]},
            "tree": inp["tree"], "branches": repo.get("branches", []), "releases": inp["releases"],
            "tags": repo.get("tags", []), "manifest": manifest, "pending": pending})
        if blocker:
            v.add(blocker)
        if extras:
            v.add(g.say("RepoExtras"), *[f"{x.name} ({x.kind})" for x in extras])
            for x in extras:
                if not x.removable:
                    v.add(g.say("RepoSubmodule", x.name))
        if manifest is not None:
            listed = {s["slug"]: s for s in manifest}
            keeps = set(after) - {slug}
            if (slug not in listed or not g.same_entry(entry, listed[slug]) or not keeps <= set(listed)
                    or any(r in listed for r in removed)):
                v.add(g.say("ManifestMismatch"))

    # 4.3 the files the entry points at.
    names = {"set": slug + ".set", "preview": g.PREVIEW_ASSET}
    release = [r for r in inp["releases"] if r.get("tag_name") == entry["release"]]
    assets = {a["name"]: a for a in release[0].get("assets", [])} if len(release) == 1 else {}
    for key in ("set", "preview"):
        if not g.url_allowed(entry[key]["url"], login, entry["release"], names[key]):
            v.add(g.say("LinkNotOpened"))
        elif names[key] not in assets:
            v.add(g.say("ReleaseFileMissing", names[key]))
    if names["set"] in assets and assets[names["set"]]["size"] > g.MAX_SET_BYTES:
        v.add(g.set_too_large(assets[names["set"]]["size"]))
    if entry["set"]["size"] > g.MAX_SET_BYTES:
        v.add(g.set_too_large(entry["set"]["size"]))

    fetch = inp.get("fetch") or {}
    set_path, preview_path = inp.get("set"), inp.get("preview")
    set_ok = _downloaded(v, fetch.get("set"), set_path, "set", names["set"], g.set_too_large)
    preview_ok = _downloaded(v, fetch.get("preview"), preview_path, "preview", names["preview"],
                             lambda n: g.say("PreviewBad"))

    facts = None
    if set_ok:
        if (os.path.getsize(set_path) != entry["set"]["size"] or sha256_file(set_path) != entry["set"]["sha256"]):
            v.add(g.say("HashMismatch"))
        elif os.path.getsize(set_path) > g.MAX_SET_BYTES:
            v.add(g.set_too_large(os.path.getsize(set_path)))
        else:
            facts = _facts(cli, set_path)
            for p in facts["problems"]:
                v.add(p)
            if not facts["problems"]:
                _compare(v, entry, facts)
    if preview_ok:
        canvas = (facts["width"], facts["height"]) if facts and not facts["problems"] else \
            (entry["canvas"]["width"], entry["canvas"]["height"])
        _check_preview(v, cli, preview_path, entry, canvas)

    # 4.5 words.
    name, desc = entry["name"], entry["description"]
    if not name.strip() or not 1 <= utf16_len(name) <= g.MAX_NAME:
        v.add(g.say("NameLength"))
    if utf16_len(desc) > g.MAX_DESCRIPTION:
        v.add(g.say("DescriptionLength"))
    if _has_link(name) or _has_link(desc):
        v.add(g.say("NoLinks"))
    if len(entry["tags"]) > g.MAX_TAGS:
        v.add(g.say("TooManyTags"))
    known = (inp["tags"] or {}).get("tags", []) if isinstance(inp["tags"], dict) else (inp["tags"] or [])
    for tag in entry["tags"]:
        if tag not in known:
            v.add(g.say("UnknownTag", tag))

    # 4.4a duplicates, against the index (every live entry) and the other open pull requests.
    listed = [e for e, gone in base if not gone] + open_entries
    if (dup := g.duplicate_problem(entry, listed)):
        v.add(dup)
    return v


def _as_int(x):
    try:
        return int(x)
    except (TypeError, ValueError):
        return None


def _downloaded(v, report, path, key, name, too_big):
    """True when the file is there to read; otherwise says why not."""
    status = (report or {}).get("status")
    if status == "refused":
        v.add(g.say("LinkNotOpened"))
    elif status == "missing":
        v.add(g.say("ReleaseFileMissing", name))
    elif status == "toolarge":
        v.add(too_big((report or {}).get("bytes", 0)))
    elif path and os.path.isfile(path):
        return True
    elif status is None and not path:
        return False  # nothing was offered to check (the earlier problems already say why)
    else:
        v.add(g.say("ReleaseFileMissing", name))
    return False


def _facts(cli, set_path):
    code, out = cli(["share", "check", set_path, "--json"])
    try:
        facts = json.loads(out)
        if code not in (0, 1) or not isinstance(facts.get("problems"), list):
            raise ValueError
    except (ValueError, AttributeError):
        raise RuntimeError("nfty share check gave no readable answer (exit %s)" % code) from None
    return facts


def _compare(v, entry, f):
    if f["assets"] != entry["assets"]:
        v.add(g.say("CountMismatch"))
    if (f["width"], f["height"]) != (entry["canvas"]["width"], entry["canvas"]["height"]):
        v.add(g.say("CanvasMismatch"))
    if (f["models"] != entry["models"] or f["includesCookBook"] != entry["includesCookBook"]
            or f.get("cookbookSha256") != entry["cookbookSha256"] or f["collection"] != entry["collection"]
            or f["fingerprint"] != entry["fingerprint"]):
        v.add(g.say("EntryDetailsMismatch"))
    if f["fileSha256"] != entry["set"]["sha256"]:
        v.add(g.say("HashMismatch"))


def _check_preview(v, cli, path, entry, canvas):
    bad = g.say("PreviewBad")
    size = os.path.getsize(path)
    dims = png_size(path)
    if size > g.MAX_PREVIEW_BYTES or dims is None or max(dims) > g.MAX_PREVIEW_SIDE:
        v.add(bad)
        return
    if size != entry["preview"]["size"] or sha256_file(path) != entry["preview"]["sha256"]:
        v.add(g.say("HashMismatch"))
        return
    code, out = cli(["sheet", "grid", path])  # decodes the picture and reads the grid it carries
    m = re.search(r"^embedded: .*?cell=(\d+)x(\d+)", out, re.M)
    if code != 0 or not m or (int(m.group(1)), int(m.group(2))) != tuple(canvas):
        v.add(bad)


_MD = re.compile(r"[\x00-\x1f\x7f]")


def clean(line):
    """Entry-derived text (a tag, a name) goes into a PR comment: no mentions, links, HTML or line breaks."""
    line = _MD.sub(" ", line).replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
    return line.replace("@", "@​").replace("[", "\\[").replace("]", "\\]").replace("`", "'")


def comment_md(v):
    if v.ok:
        return "This share passes the gallery's checks.\n"
    lines = []
    for sentence, details in zip(v.problems, v.details):
        lines.append("- " + clean(sentence))
        lines += ["  - " + clean(d) for d in details]
    return "\n".join(lines) + "\n"


def write_outputs(v, out, number, head):
    os.makedirs(out, exist_ok=True)
    with open(os.path.join(out, "comment.md"), "w", encoding="utf-8", newline="\n") as f:
        f.write(comment_md(v))
    with open(os.path.join(out, "verdict.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump({"ok": v.ok, "pr": number, "head": head, "problems": v.problems}, f)


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    for name in ("pr-files", "pr", "author", "repo", "tree", "releases", "blocked", "tags", "entries-dir",
                 "pr-dir", "nfty", "out"):
        ap.add_argument("--" + name, required=name != "out", default=None)
    for name in ("open-entries", "fetch", "set", "preview"):
        ap.add_argument("--" + name, default=None)
    a = ap.parse_args(argv)
    tree = _load(a.tree)
    if isinstance(tree, dict) and tree.get("truncated"):
        # GitHub cut the listing short: more files than it will show is not a clean repository.
        tree = {"tree": list(tree.get("tree", [])) + [{"path": "(more files than GitHub lists)", "type": "blob", "sha": "", "size": 0}]}
    inp = {
        "pr_files": _load(a.pr_files), "pr": _load(a.pr), "author": _load(a.author), "repo": _load(a.repo),
        "tree": tree["tree"] if isinstance(tree, dict) else (tree or []), "releases": _load(a.releases) or [],
        "open_entries": _load(a.open_entries) if a.open_entries and os.path.isfile(a.open_entries) else [],
        "entries_dir": a.entries_dir, "pr_dir": a.pr_dir, "blocked": _load(a.blocked), "tags": _load(a.tags),
        "fetch": _load(a.fetch) if a.fetch and os.path.isfile(a.fetch) else None,
        "set": a.set, "preview": a.preview,
    }
    try:
        v = evaluate(inp, make_cli(a.nfty))
    except RuntimeError as e:
        print(str(e), file=sys.stderr)
        return 2
    write_outputs(v, a.out or ".", inp["pr"]["number"], (inp["pr"].get("head") or {}).get("sha"))
    print(comment_md(v), end="")
    return 0 if v.ok else 1


if __name__ == "__main__":
    sys.exit(main())
