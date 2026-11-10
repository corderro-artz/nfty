"""Shared rules of the community gallery's index repository (stdlib only).

Everything here mirrors ``src/Nfty.Core/Publish/Community`` and says so where it matters:

* ``SENTENCES`` are the ``ShareRules`` sentences this repo speaks itself (the rest come from
  ``nfty share check``). ``tools/tests/test_gallery.py`` parses ``ShareRules.cs`` and fails when
  one drifts.
* ``parse_entry`` / ``entry_json`` / ``index_json`` read and write an entry the way
  ``CommunityEntry`` + ``Json.Options`` do, byte for byte (System.Text.Json's escaping included).
* ``duplicate_problem`` / ``dates_problem`` / ``scan`` are ``CommunityDuplicates``,
  ``CommunityDates`` and ``PublicRepoScan.Scan``.
"""
import datetime
import re

# ---- limits (ShareRules constants) ------------------------------------------------------------

MAX_SHARES = 3
MAX_NAME = 64
MAX_DESCRIPTION = 500
MAX_TAGS = 5
MAX_HISTORY_KB = 5120
MIN_ACCOUNT_AGE_DAYS = 30
MAX_SET_BYTES = 209_715_200
MAX_PREVIEW_BYTES = 2_097_152
MAX_PREVIEW_SIDE = 2048
MAX_CANVAS = 512
SCHEMA = 1
REPO_NAME = "nfty-public"
MANIFEST_FILE = "nfty-public.json"
README_FILE = "README.md"
PREVIEW_ASSET = "preview.png"

# ---- sentences --------------------------------------------------------------------------------
# The C# text with every interpolation replaced by "{}" and every constant written out.
# test_sentences_match_csharp holds each one to ShareRules.cs.

SENTENCES = {
    "NameLength": "A name takes 1 to 64 characters.",
    "DescriptionLength": "A description takes up to 500 characters.",
    "NoLinks": "Names and descriptions can't contain links.",
    "TooManyTags": "A share takes up to 5 tags.",
    "UnknownTag": "'{}' isn't one of the gallery's tags.",
    "PreviewBad": "The preview picture is missing, too big or unreadable.",
    "HashMismatch": "The download doesn't match what the gallery lists, so it was not opened.",
    "AlreadyListed": "This Set is already in the gallery, shared by @{}.",
    "AlreadySharedAs": "You already share this Set as \"{}\".",
    "NothingChanged": "Nothing changed since you shared \"{}\" on {}.",
    "AtShareLimit": "You share 3 collections, the most the gallery takes. Pick one to replace.",
    "RepoNotOurs": "You already have a repository called nfty-public that nfty didn't make. "
                   "Rename or remove it on GitHub, then share again.",
    "RepoPrivate": "Your nfty-public repository is private; the gallery reads only public ones.",
    "RepoExtras": "Your nfty-public holds things the gallery doesn't allow. Remove them to share.",
    "RepoSubmodule": "'{}' is a submodule; remove it on GitHub, then share again.",
    "RepoDuplicateRelease": "Your nfty-public has two releases for \"{}\". Remove one on GitHub, then share again.",
    "RepoManifestDamaged": "Your nfty-public.json lists a release nfty didn't make, so nfty can't tell which files are yours. "
                           "Restore it on GitHub, then share again.",
    "HistoryTooLarge": "Your nfty-public's history holds about {} MB of deleted files. "
                       "Delete the repository on GitHub and share again; nfty will make a fresh one.",
    "LinkNotOpened": "This gallery entry's link isn't one nfty opens.",
    "DatesMismatch": "The share's dates don't match its history.",
    "CountMismatch": "The number of assets doesn't match the pictures and metadata in this Set.",
    "CanvasMismatch": "The canvas size doesn't match the size of the pictures in this Set.",
    "EntryFilesNotAllowed": "A share can only add, update or remove your own entries in the gallery.",
    "EntryUnreadable": "The share's entry file isn't one the gallery can read.",
    "EntryNameMismatch": "The share's name doesn't match its file name in the gallery.",
    "EntryOwnerMismatch": "The share names a different owner than the account that submitted it.",
    "AccountTooYoung": "Your GitHub account is under 30 days old; the gallery takes shares from older accounts.",
    "Blocked": "The gallery can't list this share.",
    "OneNewShareADay": "The gallery takes one new share a day. Try again tomorrow.",
    "RepoNotYours": "The share's repository isn't your nfty-public.",
    "ManifestMismatch": "Your nfty-public.json doesn't list this share as it was submitted.",
    "ReleaseFileMissing": "'{}' is missing from the share's release.",
    "EntryDetailsMismatch": "The share's details don't match its Set.",
}

# SetTooLarge is two sentences in C# (with and without the models clause); this repo never knows the
# models' share, so it speaks the second. test_sentences_match_csharp checks both pieces.
SET_TOO_LARGE = "{} — the gallery takes up to {}. Share fewer assets."


def say(name, *args):
    """A ``ShareRules`` sentence by its C# name, with the interpolations filled in."""
    return SENTENCES[name].format(*args)


def thousands(n):
    return f"{n:,}"


def size_text(n):
    """``ShareRules.Size`` for sizes of a megabyte and up ("214 MB", "1.5 MB"); rounded UP to a tenth, in integers."""
    tenths = (n * 10 + 1024 * 1024 - 1) // (1024 * 1024)
    return f"{tenths // 10} MB" if tenths % 10 == 0 else f"{tenths // 10}.{tenths % 10} MB"


def set_too_large(n):
    return SET_TOO_LARGE.format(size_text(n), size_text(MAX_SET_BYTES))


_MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"]


# ---- timestamps (DateTimeOffset, ticks of 100 ns) ---------------------------------------------

_TS = re.compile(r"^(\d{4})-(\d\d)-(\d\d)T(\d\d):(\d\d):(\d\d)(?:\.(\d{1,7}))?(Z|[+-]\d\d:\d\d)$")
TICKS = 10_000_000


class Raw(str):
    """A string System.Text.Json writes as is (a ``DateTimeOffset``), not through its escaping."""


class Unreadable(Exception):
    """An entry that cannot be read as a ``CommunityEntry``."""


class Ts:
    """A moment with the clock and offset it was written in."""

    def __init__(self, text):
        m = _TS.match(text) if isinstance(text, str) else None
        if not m:
            raise Unreadable("timestamp")
        y, mo, d, h, mi, s = (int(m.group(i)) for i in range(1, 7))
        frac = (m.group(7) or "").ljust(7, "0")
        off = m.group(8)
        try:
            datetime.date(y, mo, d)
            if h > 23 or mi > 59 or s > 59:
                raise ValueError
            sign = -1 if off.startswith("-") else 1
            off_s = 0 if off == "Z" else sign * (int(off[1:3]) * 3600 + int(off[4:6]) * 60)
            if abs(off_s) > 14 * 3600:
                raise ValueError
        except ValueError:
            raise Unreadable("timestamp") from None
        self.year, self.month, self.day = y, mo, d
        days = datetime.date(y, mo, d).toordinal()
        self.ticks = ((days * 86400 + h * 3600 + mi * 60 + s - off_s) * TICKS) + int(frac)
        trimmed = frac.rstrip("0")
        self.text = (f"{y:04d}-{mo:02d}-{d:02d}T{h:02d}:{mi:02d}:{s:02d}" + ("." + trimmed if trimmed else "")
                     + ("+00:00" if off == "Z" else off))

    def date_text(self):
        """``d MMM yyyy`` in the clock the timestamp was written in, invariant."""
        return f"{self.day} {_MONTHS[self.month - 1]} {self.year}"

    def plus(self, seconds):
        return self.ticks + seconds * TICKS


# ---- entries ----------------------------------------------------------------------------------

def _int(v):
    if isinstance(v, bool) or not isinstance(v, int):
        raise Unreadable("int")
    return v


def _str(v):
    if not isinstance(v, str):
        raise Unreadable("string")
    return v


def _bool(v):
    if not isinstance(v, bool):
        raise Unreadable("bool")
    return v


def _obj(v):
    if not isinstance(v, dict):
        raise Unreadable("object")
    return v


def _file(v):
    v = _obj(v)
    return {"url": _str(v.get("url")), "size": _int(v.get("size")), "sha256": _str(v.get("sha256"))}


def parse_entry(obj):
    """Reads one entry object into canonical form (record order, timestamps normalized) or raises ``Unreadable``.

    Unknown members (``status``, a future field) are ignored, as ``System.Text.Json`` ignores them.
    """
    try:
        obj = _obj(obj)
        tags = obj.get("tags")
        if not isinstance(tags, list):
            raise Unreadable("tags")
        book = obj.get("cookbookSha256")
        owner, canvas = _obj(obj.get("owner")), _obj(obj.get("canvas"))
        version = _int(obj.get("schemaVersion"))
        if version != SCHEMA:
            raise Unreadable("schemaVersion")
        return {
            "schemaVersion": version,
            "slug": _str(obj.get("slug")),
            "owner": {"id": _int(owner.get("id")), "login": _str(owner.get("login"))},
            "repo": _str(obj.get("repo")),
            "release": _str(obj.get("release")),
            "name": _str(obj.get("name")),
            "collection": _str(obj.get("collection")),
            "description": _str(obj.get("description")),
            "tags": [_str(t) for t in tags],
            "canvas": {"width": _int(canvas.get("width")), "height": _int(canvas.get("height"))},
            "assets": _int(obj.get("assets")),
            "models": _bool(obj.get("models")),
            "includesCookBook": _bool(obj.get("includesCookBook")),
            "cookbookSha256": None if book is None else _str(book),
            "set": _file(obj.get("set")),
            "preview": _file(obj.get("preview")),
            "fingerprint": _str(obj.get("fingerprint")),
            "nftyVersion": _str(obj.get("nftyVersion")),
            "created": Raw(Ts(obj.get("created")).text),
            "updated": Raw(Ts(obj.get("updated")).text),
        }
    except (Unreadable, AttributeError):
        raise Unreadable("entry") from None


def entry_id(e):
    return f"{e['owner']['id']}/{e['slug']}"


def same_share(a, b):
    """``CommunityEntry.SameShare``: every shared field equal, ``updated`` ignored, instants compared as instants."""
    keys = [k for k in a if k not in ("updated", "created")]
    return (all(a[k] == b[k] for k in keys) and Ts(a["created"]).ticks == Ts(b["created"]).ticks)


def same_entry(a, b):
    """Every field equal, ``updated`` included (what ``nfty-public.json`` must show for the submitted entry)."""
    return same_share(a, b) and Ts(a["updated"]).ticks == Ts(b["updated"]).ticks


# ---- System.Text.Json, indented, Json.Options -------------------------------------------------

_SHORT = {"\b": "\\b", "\t": "\\t", "\n": "\\n", "\f": "\\f", "\r": "\\r", "\\": "\\\\"}


def _stj_string(s):
    out = ['"']
    for ch in s:
        if ch in _SHORT:
            out.append(_SHORT[ch])
        elif " " <= ch <= "~" and ch not in "\"&'+<>`":
            out.append(ch)
        else:
            u = ch.encode("utf-16-le")
            for i in range(0, len(u), 2):
                out.append("\\u%04X" % int.from_bytes(u[i:i + 2], "little"))
    out.append('"')
    return "".join(out)


def stj_dumps(value, _depth=0):
    """Indented (2 spaces), LF, no trailing newline: what ``JsonSerializer`` writes with ``Json.Options``."""
    pad, inner = "  " * _depth, "  " * (_depth + 1)
    if value is None:
        return "null"
    if value is True:
        return "true"
    if value is False:
        return "false"
    if isinstance(value, int):
        return str(value)
    if isinstance(value, Raw):
        return '"' + value + '"'
    if isinstance(value, str):
        return _stj_string(value)
    if isinstance(value, list):
        if not value:
            return "[]"
        return "[\n" + ",\n".join(inner + stj_dumps(v, _depth + 1) for v in value) + "\n" + pad + "]"
    if isinstance(value, dict):
        if not value:
            return "{}"
        return "{\n" + ",\n".join(inner + _stj_string(k) + ": " + stj_dumps(v, _depth + 1)
                                  for k, v in value.items()) + "\n" + pad + "}"
    raise TypeError(type(value))


def entry_json(entry, **extra):
    """An entry file's bytes; ``extra`` members (``status``) follow the record's own."""
    return stj_dumps({**entry, **extra}).encode("utf-8")


def sort_key(e):
    return (e["owner"]["id"], e["slug"].encode("utf-16-be"))


def index_json(entries, generated):
    """``CommunityIndex.Write``: entries sorted by owner id then slug (ordinal)."""
    doc = {"schemaVersion": SCHEMA, "generated": Raw(Ts(generated).text),
           "entries": sorted(entries, key=sort_key)}
    return stj_dumps(doc).encode("utf-8")


# ---- slug -------------------------------------------------------------------------------------

_SLUG = re.compile(r"^[a-z0-9](?:[a-z0-9-]{0,38}[a-z0-9])?\Z")


def slug_valid(slug):
    return bool(_SLUG.match(slug))


_SAFE = re.compile(r"[A-Za-z0-9._-]+\Z")


def url_allowed(url, login, release, name):
    """True only for ``https://github.com/<login>/nfty-public/releases/download/<release>/<name>``, exactly.

    No other host, port, user info, query, fragment, encoded or extra path segment: this is the one place that
    decides which address the Action downloads from.
    """
    prefix = "https://github.com/"
    if not isinstance(url, str) or not url.startswith(prefix):
        return False
    parts = url[len(prefix):].split("/")
    return (len(parts) == 6 and parts[0].lower() == login.lower() and parts[1] == REPO_NAME
            and parts[2:4] == ["releases", "download"] and parts[4] == release and parts[5] == name
            and all(_SAFE.match(p) for p in (parts[0], parts[4], parts[5])))


# ---- duplicates and dates (CommunityDuplicates, CommunityDates) --------------------------------

def duplicate_problem(candidate, listed):
    own_other = own_same = None
    for e in listed:
        same_set = e["fingerprint"] == candidate["fingerprint"]
        if e["owner"]["id"] != candidate["owner"]["id"]:
            if same_set or e["set"]["sha256"] == candidate["set"]["sha256"]:
                return say("AlreadyListed", e["owner"]["login"])
        elif same_set:
            if e["slug"] == candidate["slug"]:
                own_same = say("NothingChanged", e["name"], Ts(e["updated"]).date_text())
            elif own_other is None:
                own_other = say("AlreadySharedAs", e["name"])
    return own_same or own_other


def dates_problem(candidate, previous, pr_created):
    """``CommunityDates.Problem``; ``pr_created`` is a ``Ts``."""
    created, updated = Ts(candidate["created"]), Ts(candidate["updated"])
    if previous is not None:
        if created.ticks != Ts(previous["created"]).ticks or updated.ticks <= Ts(previous["updated"]).ticks:
            return say("DatesMismatch")
    if updated.ticks > pr_created.plus(3600):
        return say("DatesMismatch")
    return None


# ---- the 3.3 scan (PublicRepoScan.Scan) ---------------------------------------------------------

_VERSION_SUFFIX = re.compile(r"-v\d+$")


class Extra:
    def __init__(self, kind, name, size=None, ident=None, removable=True):
        self.kind, self.name, self.size, self.id, self.removable = kind, name, size, ident, removable

    def __repr__(self):
        return f"Extra({self.kind},{self.name})"


def scan(snapshot):
    """Applies the 3.3 table.

    ``snapshot`` keys: ``repo`` {default_branch, private, size}, ``tree`` [{path,type,sha,size}], ``branches`` [name],
    ``releases`` [{id, tag_name, assets:[{id,name,size}]}], ``tags`` [name], ``manifest`` (parsed shares list or None),
    ``pending`` [tags of superseded releases whose replacing PR is open].
    Returns ``(extras, blocker_sentence_or_None)``.
    """
    extras = []
    folders = {}
    for item in snapshot["tree"]:
        path, typ = item["path"], item["type"]
        if typ == "blob" and path in (README_FILE, MANIFEST_FILE):
            continue
        top = path.split("/", 1)[0]
        size, removable = folders.get(top, (None, True))
        if typ == "blob" and item.get("size") is not None:
            size = (size or 0) + item["size"]
        folders[top] = (size, removable and typ in ("blob", "tree"))
    for name in sorted(folders, key=_ordinal):
        size, removable = folders[name]
        extras.append(Extra("file", name, size, None, removable))

    default = snapshot["repo"]["default_branch"]
    for branch in sorted(set(snapshot["branches"]), key=_ordinal):
        if branch != default:
            extras.append(Extra("branch", branch))

    shares = snapshot["manifest"] or []
    listed = []
    for s in shares:
        if s["release"] not in listed:
            listed.append(s["release"])
    listed = listed[:MAX_SHARES]
    allowed = set(listed) | set(snapshot.get("pending", []))
    # A share whose release tag is not <slug>-v<N> is a manifest nfty did not write; its files can't be told apart.
    damaged = {x["release"] for x in shares if not re.fullmatch(re.escape(x["slug"]) + r"-v[0-9]+", x["release"])}

    releases = snapshot["releases"]
    counts = {}
    for r in releases:
        if r["tag_name"] in allowed:
            counts[r["tag_name"]] = counts.get(r["tag_name"], 0) + 1
    duplicated = {t for t, n in counts.items() if n > 1}
    kept, asset_extras, release_extras = set(), [], []
    for r in releases:
        tag = r["tag_name"]
        if tag in duplicated:
            kept.add(tag)
            continue
        if tag not in allowed or tag in kept:
            assets = r.get("assets", [])
            total = sum(a["size"] for a in assets) if assets else None
            release_extras.append(Extra("release", tag, total, r["id"]))
            continue
        kept.add(tag)
        if tag in damaged:
            continue
        owned = next((s["slug"] for s in shares if s["release"] == tag), None)
        slug = owned if owned is not None else _VERSION_SUFFIX.sub("", tag)
        for a in r.get("assets", []):
            if a["name"] not in (slug + ".set", PREVIEW_ASSET):
                asset_extras.append(Extra("asset", a["name"], a["size"], a["id"]))
    extras += sorted(release_extras, key=lambda e: (_ordinal(e.name), e.id))
    extras += sorted(asset_extras, key=lambda e: (_ordinal(e.name), e.id))
    for tag in sorted(set(snapshot["tags"]), key=_ordinal):
        if tag not in kept:
            extras.append(Extra("tag", tag))

    repo = snapshot["repo"]
    if snapshot["manifest"] is None:
        blocker = say("RepoNotOurs")
    elif repo["private"]:
        blocker = say("RepoPrivate")
    elif repo["size"] > MAX_HISTORY_KB:
        blocker = say("HistoryTooLarge", thousands((repo["size"] + 512) // 1024))
    elif duplicated:
        blocker = say("RepoDuplicateRelease", next(r["tag_name"] for r in releases if r["tag_name"] in duplicated))
    elif damaged:
        blocker = say("RepoManifestDamaged")
    else:
        blocker = None
    return extras, blocker


def _ordinal(s):
    return s.encode("utf-16-be")
