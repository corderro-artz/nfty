"""Tests for the community gallery's index repo tooling (tools/gallery). Stdlib only.

Run with ``python -m unittest tools.tests.test_gallery -v`` from the repository root. Nothing here touches
the network, GitHub, or the real nfty CLI: the CLI is a fake that answers like ``share check --json`` and
``sheet grid``, and the API answers are recorded JSON under ``tools/tests/gallery_fixtures``.
"""
import copy
import hashlib
import io
import json
import os
from pathlib import Path
import re
import shutil
import struct
import sys
import tempfile
import unittest
import urllib.error

ROOT = Path(__file__).resolve().parents[2]
FIX = Path(__file__).resolve().parent / "gallery_fixtures"
sys.path.insert(0, str(ROOT / "tools" / "gallery"))
import build_index  # noqa: E402
import check  # noqa: E402
import fetch  # noqa: E402
import gallerylib as g  # noqa: E402
import health  # noqa: E402

RULES_CS = ROOT / "src" / "Nfty.Core" / "Publish" / "Community" / "ShareRules.cs"


def load(name):
    return json.loads((FIX / name).read_text(encoding="utf-8"))


def png(width, height):
    ihdr = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + struct.pack(">I", 13) + b"IHDR" + ihdr + b"\0\0\0\0" + b"\0" * 20


class Scenario:
    """One pull request that adds ``vapor-chests`` and would pass, built in a temp dir; tests break one thing."""

    def __init__(self, test):
        self.tmp = Path(tempfile.mkdtemp())
        test.addCleanup(shutil.rmtree, self.tmp, True)
        self.set_bytes = b"pretend this is a zip"
        self.preview_bytes = png(256, 128)
        self.set_path = self.tmp / "set.set"
        self.preview_path = self.tmp / "preview.png"
        self.set_path.write_bytes(self.set_bytes)
        self.preview_path.write_bytes(self.preview_bytes)
        self.author = load("author.json")
        self.pr = load("pr.json")
        self.pr_files = load("pr-files.json")
        self.repo = load("repo.json")
        self.tree = load("tree.json")["tree"]
        self.releases = load("releases.json")
        self.blocked = {"users": [], "sets": []}
        self.tags = {"tags": ["props", "pixel-art", "animals"]}
        self.open_entries = []
        self.base = []  # (entry, gone)
        self.fetch = None
        self.facts = {"problems": [], "assets": 600, "width": 32, "height": 32, "models": True,
                      "includesCookBook": True, "cookbookSha256": "d" * 64, "collection": "Vapor Chests",
                      "fingerprint": "c" * 64, "fileSha256": hashlib.sha256(self.set_bytes).hexdigest()}
        self.grid = "embedded: cols=8;rows=4;cell=32x32;count=29\ndetected: none\n"
        self.grid_code = 0
        self.entry = self.make_entry()
        self.manifest_others = []
        self.calls = []

    def make_entry(self, slug="vapor-chests", name="Vapor Chests", owner=(1234567, "someone"), **over):
        login = owner[1]
        base = f"https://github.com/{login}/nfty-public/releases/download/{slug}-v1/"
        e = {"schemaVersion": 1, "slug": slug, "owner": {"id": owner[0], "login": login},
             "repo": f"{login}/nfty-public", "release": f"{slug}-v1", "name": name, "collection": name,
             "description": "Chests and strongboxes.", "tags": ["props", "pixel-art"],
             "canvas": {"width": 32, "height": 32}, "assets": 600, "models": True, "includesCookBook": True,
             "cookbookSha256": "d" * 64,
             "set": {"url": base + slug + ".set", "size": len(self.set_bytes),
                     "sha256": hashlib.sha256(self.set_bytes).hexdigest()},
             "preview": {"url": base + "preview.png", "size": len(self.preview_bytes),
                         "sha256": hashlib.sha256(self.preview_bytes).hexdigest()},
             "fingerprint": "c" * 64, "nftyVersion": "2.5.0",
             "created": "2026-11-09T14:02:00+00:00", "updated": "2026-11-09T14:02:00+00:00"}
        e.update(over)
        return e

    def cli(self, args):
        self.calls.append(list(args))
        if args[:2] == ["share", "check"]:
            return (1 if self.facts["problems"] else 0), json.dumps(self.facts)
        if args[:2] == ["sheet", "grid"]:
            return self.grid_code, self.grid
        raise AssertionError(args)

    def run(self):
        aid = self.author["id"]
        pr_dir, entries_dir = self.tmp / "pr", self.tmp / "base"
        for f in self.pr_files:
            if f["status"] != "removed":
                p = pr_dir / f["filename"]
                p.parent.mkdir(parents=True, exist_ok=True)
                p.write_text(json.dumps(self.entry), encoding="utf-8")
        shutil.rmtree(entries_dir, True)
        entries_dir.mkdir()
        for e, gone in self.base:
            p = entries_dir / str(e["owner"]["id"]) / (e["slug"] + ".json")
            p.parent.mkdir(parents=True, exist_ok=True)
            p.write_text(json.dumps({**e, **({"status": "gone"} if gone else {})}), encoding="utf-8")
        repo = copy.deepcopy(self.repo)
        if repo is not None and repo.get("manifest") is None and "manifest" not in self.__dict__.get("no_manifest", ()):
            repo["manifest"] = {"schemaVersion": 1, "shares": [self.entry] + self.manifest_others}
        inp = {"pr_files": self.pr_files, "pr": self.pr, "author": self.author, "repo": repo, "tree": self.tree,
               "releases": self.releases, "open_entries": self.open_entries, "entries_dir": str(entries_dir),
               "pr_dir": str(pr_dir), "blocked": self.blocked, "tags": self.tags, "fetch": self.fetch,
               "set": str(self.set_path), "preview": str(self.preview_path)}
        self.verdict = check.evaluate(inp, self.cli)
        assert aid
        return self.verdict


class CheckTests(unittest.TestCase):
    def sc(self):
        return Scenario(self)

    def assertRefused(self, v, sentence):
        self.assertIn(sentence, v.problems)

    def test_clean_pr_passes(self):
        v = self.sc().run()
        self.assertEqual([], v.problems)
        self.assertTrue(v.ok)

    def test_entry_outside_own_id_dir_fails(self):
        s = self.sc()
        s.pr_files[0]["filename"] = "entries/999/vapor-chests.json"
        self.assertEqual([g.say("EntryFilesNotAllowed")], s.run().problems)

    def test_only_entry_files_and_at_most_one_add_one_delete(self):
        for files in (
            [{"filename": "README.md", "status": "modified"}],
            [{"filename": "entries/1234567/a.json", "status": "added"}, {"filename": "entries/1234567/b.json", "status": "added"}],
            [{"filename": "entries/1234567/a.json", "status": "renamed", "previous_filename": "entries/1234567/b.json"}],
            [{"filename": "entries/1234567/../x.json", "status": "added"}],
            [{"filename": "entries/1234567/a.json", "status": "removed"}, {"filename": "entries/1234567/b.json", "status": "removed"}],
        ):
            with self.subTest(files=files):
                s = self.sc()
                s.pr_files = files
                s.base = [(s.make_entry(slug="a", fingerprint="1" * 64), False), (s.make_entry(slug="b", fingerprint="2" * 64), False)]
                self.assertEqual([g.say("EntryFilesNotAllowed")], s.run().problems)

    def test_lone_deletion_of_own_entry_passes_and_of_nothing_fails(self):
        s = self.sc()
        old = s.make_entry(slug="old", fingerprint="9" * 64)
        s.pr_files = [{"filename": "entries/1234567/old.json", "status": "removed"}]
        s.base = [(old, False)]
        self.assertEqual([], s.run().problems)
        s.base = []
        self.assertEqual([g.say("EntryFilesNotAllowed")], s.run().problems)

    def test_swap_adds_one_and_deletes_one(self):
        s = self.sc()
        others = [s.make_entry(slug=f"s{i}", fingerprint=str(i) * 64, created="2026-10-01T00:00:00+00:00",
                               updated="2026-10-01T00:00:00+00:00") for i in (1, 2, 3)]
        s.pr_files.append({"filename": "entries/1234567/s1.json", "status": "removed"})
        s.base = [(e, False) for e in others]
        s.manifest_others = others[1:]
        s.releases += [{"id": 2000 + i, "tag_name": f"s{i}-v1", "assets": [
            {"id": 3000 + i, "name": f"s{i}.set", "size": 1}, {"id": 4000 + i, "name": "preview.png", "size": 1}]}
            for i in (1, 2, 3)]
        s.releases[1:] = [r for r in s.releases[1:] if r["tag_name"] != "s3-v1"]  # one superseded release may await deletion
        self.assertEqual([], s.run().problems)
        # without the deletion the author would have a fourth entry
        s2 = self.sc()
        s2.base = [(e, False) for e in others]
        s2.manifest_others = others
        self.assertRefused(s2.run(), g.say("AtShareLimit"))

    def test_fourth_share_fails(self):
        s = self.sc()
        old = [s.make_entry(slug=f"s{i}", fingerprint=str(i) * 64, created="2026-10-01T00:00:00+00:00",
                            updated="2026-10-01T00:00:00+00:00") for i in (1, 2, 3)]
        s.base = [(e, False) for e in old]
        s.manifest_others = old
        self.assertRefused(s.run(), g.say("AtShareLimit"))

    def test_young_account_fails(self):
        s = self.sc()
        s.author["created_at"] = "2026-10-20T00:00:00Z"
        self.assertEqual([g.say("AccountTooYoung")], s.run().problems)
        s = self.sc()
        s.author["created_at"] = "2026-10-10T14:05:00Z"  # exactly 30 days before the PR
        self.assertEqual([], s.run().problems)

    def test_second_new_share_in_a_day_fails(self):
        s = self.sc()
        recent = s.make_entry(slug="today", fingerprint="7" * 64, created="2026-11-09T11:00:00+00:00",
                              updated="2026-11-09T11:00:00+00:00")
        s.base = [(recent, False)]
        s.manifest_others = [recent]
        self.assertEqual([g.say("OneNewShareADay")], s.run().problems)
        # one from an open pull request counts too
        s = self.sc()
        s.open_entries = [recent]
        self.assertRefused(s.run(), g.say("OneNewShareADay"))
        # an update to an existing entry is not a new share
        s = self.sc()
        mine = s.make_entry(fingerprint="8" * 64, created="2026-11-09T11:30:00+00:00", updated="2026-11-09T11:30:00+00:00")
        s.base = [(mine, False), (recent, False)]
        s.manifest_others = [recent]
        s.entry = s.make_entry(created="2026-11-09T11:30:00+00:00", updated="2026-11-09T14:02:00+00:00")
        s.pr_files[0]["status"] = "modified"
        self.assertEqual([], s.run().problems)

    def test_blocked_user_fails(self):
        s = self.sc()
        s.blocked = {"users": [1234567], "sets": []}
        self.assertEqual([g.say("Blocked")], s.run().problems)
        s = self.sc()
        s.blocked = {"users": [], "sets": [s.entry["set"]["sha256"]]}
        self.assertEqual([g.say("Blocked")], s.run().problems)

    def test_repo_name_must_be_nfty_public(self):
        s = self.sc()
        s.entry["repo"] = "someone/other"
        self.assertRefused(s.run(), g.say("RepoNotYours"))
        s = self.sc()
        s.entry = s.make_entry(owner=(1234567, "someone"), repo="somebody-else/nfty-public")
        self.assertRefused(s.run(), g.say("RepoNotYours"))
        s = self.sc()
        s.repo = None  # the repository does not exist
        self.assertRefused(s.run(), g.say("RepoNotYours"))
        s = self.sc()
        s.repo["owner"]["id"] = 5  # owned by somebody else
        self.assertRefused(s.run(), g.say("RepoNotYours"))

    def test_manifest_must_match_entry(self):
        s = self.sc()
        s.repo["manifest"] = {"schemaVersion": 1, "shares": [{**s.entry, "name": "Another Name"}]}
        self.assertEqual([g.say("ManifestMismatch")], s.run().problems)
        s = self.sc()
        s.repo["manifest"] = {"schemaVersion": 1, "shares": []}
        self.assertRefused(s.run(), g.say("ManifestMismatch"))

    def test_manifest_must_keep_the_others_and_not_the_deleted(self):
        s = self.sc()
        other = s.make_entry(slug="other", fingerprint="5" * 64, created="2026-10-01T00:00:00+00:00",
                             updated="2026-10-01T00:00:00+00:00")
        s.base = [(other, False)]
        self.assertRefused(s.run(), g.say("ManifestMismatch"))

    def test_repo_scan_extras_fail(self):
        def tree_extra(s): s.tree.append({"path": "notes.txt", "type": "blob", "sha": "x", "size": 10})
        def folder_extra(s): s.tree.append({"path": "docs/a.md", "type": "blob", "sha": "x", "size": 10})
        def branch_extra(s): s.repo["branches"] = ["main", "dev"]
        def release_extra(s): s.releases.append({"id": 77, "tag_name": "old-v1", "assets": []})
        def asset_extra(s): s.releases[0]["assets"].append({"id": 9, "name": "extra.zip", "size": 3})
        def tag_extra(s): s.repo["tags"] = ["vapor-chests-v1", "stray"]
        def submodule(s): s.tree.append({"path": "vendor", "type": "commit", "sha": "x"})
        for name, mutate in (("file", tree_extra), ("folder", folder_extra), ("branch", branch_extra),
                             ("release", release_extra), ("asset", asset_extra), ("tag", tag_extra),
                             ("submodule", submodule)):
            with self.subTest(name):
                s = self.sc()
                mutate(s)
                v = s.run()
                self.assertRefused(v, g.say("RepoExtras"))
                self.assertIn(g.say("RepoExtras"), v.problems)
        s = self.sc()
        submodule(s)
        self.assertRefused(s.run(), g.say("RepoSubmodule", "vendor"))

    def test_repo_scan_other_refusals(self):
        s = self.sc()
        s.repo["private"] = True
        self.assertRefused(s.run(), g.say("RepoPrivate"))
        s = self.sc()
        s.repo["size"] = 49152
        self.assertRefused(s.run(), g.say("HistoryTooLarge", "48"))
        s = self.sc()
        s.releases.append(copy.deepcopy(s.releases[0]))
        self.assertRefused(s.run(), g.say("RepoDuplicateRelease", "vapor-chests-v1"))
        s = self.sc()
        s.repo["manifest"] = "not a manifest"
        self.assertRefused(s.run(), g.say("RepoNotOurs"))

    def test_scan_allows_one_superseded_release_while_the_pr_is_open(self):
        s = self.sc()
        old = s.make_entry(fingerprint="6" * 64, release="vapor-chests-v0", created="2026-10-01T00:00:00+00:00",
                           updated="2026-10-01T00:00:00+00:00")
        old["set"]["url"] = old["set"]["url"].replace("-v1/", "-v0/")
        old["preview"]["url"] = old["preview"]["url"].replace("-v1/", "-v0/")
        s.base = [(old, False)]
        s.pr_files[0]["status"] = "modified"
        s.entry["created"] = old["created"]
        s.releases.append({"id": 1000, "tag_name": "vapor-chests-v0", "assets": [
            {"id": 1, "name": "vapor-chests.set", "size": 3}, {"id": 2, "name": "preview.png", "size": 3}]})
        s.repo["tags"] = ["vapor-chests-v1", "vapor-chests-v0"]
        self.assertEqual([], s.run().problems)
        # a second stale release is not allowed
        s.releases.append({"id": 1001, "tag_name": "vapor-chests-v-1", "assets": []})
        self.assertRefused(s.run(), g.say("RepoExtras"))

    def test_set_over_200mb_fails(self):
        s = self.sc()
        s.entry["set"]["size"] = 224_395_264
        v = s.run()
        self.assertRefused(v, "214 MB — the gallery takes up to 200 MB. Share fewer assets.")
        s = self.sc()  # the release asset itself is too big: refused before any download
        s.releases[0]["assets"][0]["size"] = 224_395_264
        self.assertRefused(s.run(), "214 MB — the gallery takes up to 200 MB. Share fewer assets.")
        s = self.sc()  # the download was aborted mid-stream
        s.fetch = {"set": {"status": "toolarge", "bytes": 209_715_201}}
        s.set_path = s.tmp / "absent.set"
        self.assertRefused(s.run(), "200.1 MB — the gallery takes up to 200 MB. Share fewer assets.")

    def test_links_in_description_fail(self):
        for field, value in (("description", "see https://example.com"), ("description", "visit WWW.example.com"),
                             ("name", "Cats www.x.io")):
            with self.subTest(value=value):
                s = self.sc()
                s.entry[field] = value
                s.facts["collection"] = s.entry["collection"]
                self.assertRefused(s.run(), g.say("NoLinks"))

    def test_words_limits(self):
        s = self.sc()
        s.entry["name"] = "x" * 65
        self.assertRefused(s.run(), g.say("NameLength"))
        s = self.sc()
        s.entry["name"] = "  "
        self.assertRefused(s.run(), g.say("NameLength"))
        s = self.sc()
        s.entry["description"] = "x" * 501
        self.assertRefused(s.run(), g.say("DescriptionLength"))
        s = self.sc()
        s.entry["tags"] = ["props", "pixel-art", "animals", "props", "props", "props"]
        self.assertRefused(s.run(), g.say("TooManyTags"))

    def test_unknown_tag_fails(self):
        s = self.sc()
        s.entry["tags"] = ["props", "memes"]
        self.assertEqual([g.say("UnknownTag", "memes")], s.run().problems)

    def test_the_repos_tag_list_is_the_one_check_enforces(self):
        tags = json.loads((ROOT / "tools" / "gallery" / "repo" / "tags.json").read_text(encoding="utf-8"))
        s = self.sc()
        s.tags = tags
        s.entry["tags"] = ["characters", "props"]
        self.assertEqual([], s.run().problems)
        s.entry["tags"] = ["props", "pixel-art"]          # not in the gallery's list
        self.assertEqual([g.say("UnknownTag", "pixel-art")], s.run().problems)

    def test_duplicate_fingerprint_by_another_owner_fails(self):
        s = self.sc()
        s.base = [(s.make_entry(slug="theirs", owner=(42, "ann"), created="2026-10-01T00:00:00+00:00",
                                updated="2026-10-01T00:00:00+00:00"), False)]
        self.assertEqual([g.say("AlreadyListed", "ann")], s.run().problems)

    def test_duplicate_file_hash_fails(self):
        s = self.sc()
        s.base = [(s.make_entry(slug="theirs", owner=(42, "ann"), fingerprint="9" * 64,
                                created="2026-10-01T00:00:00+00:00", updated="2026-10-01T00:00:00+00:00"), False)]
        self.assertEqual([g.say("AlreadyListed", "ann")], s.run().problems)

    def test_same_set_under_another_slug_of_the_same_owner_fails(self):
        s = self.sc()
        s.base = [(s.make_entry(slug="first", name="First", created="2026-10-01T00:00:00+00:00",
                                updated="2026-10-01T00:00:00+00:00"), False)]
        s.manifest_others = [s.base[0][0]]
        v = s.run()
        self.assertRefused(v, g.say("AlreadySharedAs", "First"))

    def test_unchanged_update_fails(self):
        s = self.sc()
        mine = s.make_entry(created="2026-11-09T09:00:00+00:00", updated="2026-11-09T09:00:00+00:00")
        s.base = [(mine, False)]
        s.entry = s.make_entry(created="2026-11-09T09:00:00+00:00", updated="2026-11-09T14:02:00+00:00")
        s.pr_files[0]["status"] = "modified"
        self.assertEqual([g.say("NothingChanged", "Vapor Chests", "9 Nov 2026")], s.run().problems)
        self.assertEqual('Nothing changed since you shared "Vapor Chests" on 9 Nov 2026.', s.verdict.problems[0])

    def test_duplicate_in_another_open_pr_fails(self):
        s = self.sc()
        s.open_entries = [s.make_entry(slug="theirs", owner=(42, "ann"))]
        self.assertEqual([g.say("AlreadyListed", "ann")], s.run().problems)
        s = self.sc()
        s.open_entries = [{"garbage": True}]  # untrusted and unreadable: ignored
        self.assertEqual([], s.run().problems)

    def test_a_gone_entry_is_not_listed(self):
        s = self.sc()
        s.base = [(s.make_entry(slug="theirs", owner=(42, "ann"), created="2026-10-01T00:00:00+00:00",
                                updated="2026-10-01T00:00:00+00:00"), True)]
        self.assertEqual([], s.run().problems)

    def test_created_must_be_kept_and_updated_must_advance(self):
        def update(s, created, updated):
            old = s.make_entry(fingerprint="4" * 64, created="2026-11-01T00:00:00+00:00", updated="2026-11-02T00:00:00+00:00")
            s.base = [(old, False)]
            s.entry = s.make_entry(created=created, updated=updated)
            s.pr_files[0]["status"] = "modified"
            return s.run()
        self.assertEqual([], update(self.sc(), "2026-11-01T00:00:00+00:00", "2026-11-09T14:02:00+00:00").problems)
        for created, updated in (("2026-11-05T00:00:00+00:00", "2026-11-09T14:02:00+00:00"),  # created moved
                                 ("2026-11-01T00:00:00+00:00", "2026-11-02T00:00:00+00:00"),  # updated did not advance
                                 ("2026-11-01T00:00:00+00:00", "2026-11-09T15:06:00+00:00")):  # more than an hour ahead
            with self.subTest(created=created, updated=updated):
                self.assertRefused(update(self.sc(), created, updated), g.say("DatesMismatch"))
        # an hour ahead exactly is allowed, as in CommunityDates
        self.assertEqual([], update(self.sc(), "2026-11-01T00:00:00+00:00", "2026-11-09T15:05:00+00:00").problems)

    def test_a_gone_entry_can_be_restored_with_fresh_dates(self):
        # spec 4.6: the gallery dropped the entry (gone), so sharing it again starts its history over
        def restore(created, updated):
            s = self.sc()
            s.base = [(s.make_entry(fingerprint="4" * 64, created="2026-10-01T00:00:00+00:00",
                                    updated="2026-10-02T00:00:00+00:00"), True)]
            s.entry = s.make_entry(created=created, updated=updated)
            return s.run()
        self.assertEqual([], restore("2026-11-09T14:02:00+00:00", "2026-11-09T14:02:00+00:00").problems)
        # still a new share: dates cannot be split or backdated
        self.assertRefused(restore("2026-11-09T14:00:00+00:00", "2026-11-09T14:02:00+00:00"), g.say("DatesMismatch"))
        self.assertRefused(restore("2026-01-01T00:00:00+00:00", "2026-01-01T00:00:00+00:00"), g.say("DatesMismatch"))

    def test_entry_owner_login_must_be_the_author(self):
        s = self.sc()
        s.entry["owner"]["login"] = "mallory"
        self.assertRefused(s.run(), g.say("EntryOwnerMismatch"))
        s = self.sc()
        s.entry["owner"]["login"] = "SomeOne"  # GitHub logins compare case-insensitively
        self.assertNotIn(g.say("EntryOwnerMismatch"), s.run().problems)

    def test_a_bogus_preview_grid_is_refused(self):
        for grid in ("embedded: cols=80;rows=4;cell=32x32;count=29\n",   # wider than the picture
                     "embedded: cols=8;rows=40;cell=32x32;count=29\n",   # taller than the picture
                     "embedded: cols=8;rows=4;cell=32x32;count=33\n"):   # more cells than the grid holds
            with self.subTest(grid):
                s = self.sc()
                s.grid = grid
                self.assertEqual([g.say("PreviewBad")], s.run().problems)

    def test_a_listed_release_that_is_not_slug_dash_version_blocks_the_scan(self):
        entry = self.sc().make_entry(release="other-tag")
        extras, blocker = g.scan({
            "repo": {"default_branch": "main", "private": False, "size": 10},
            "tree": [], "branches": ["main"], "tags": ["other-tag"], "manifest": [entry], "pending": [],
            "releases": [{"id": 1, "tag_name": "other-tag", "assets": [
                {"id": 2, "name": "vapor-chests.set", "size": 1}, {"id": 3, "name": "preview.png", "size": 1},
                {"id": 4, "name": "secret.bin", "size": 1}]}]})
        self.assertEqual(g.say("RepoManifestDamaged"), blocker)
        self.assertEqual([], [x for x in extras if x.kind == "asset"])

    def test_new_entry_dates_cannot_be_backdated_or_split(self):
        s = self.sc()
        s.entry = s.make_entry(created="2026-01-01T00:00:00+00:00", updated="2026-01-01T00:00:00+00:00")
        self.assertRefused(s.run(), g.say("DatesMismatch"))
        s = self.sc()
        s.entry = s.make_entry(created="2026-11-09T14:00:00+00:00", updated="2026-11-09T14:02:00+00:00")
        self.assertRefused(s.run(), g.say("DatesMismatch"))

    def test_fingerprint_must_equal_cli_output(self):
        s = self.sc()
        s.facts["fingerprint"] = "f" * 64  # what the Set really is
        self.assertEqual([g.say("EntryDetailsMismatch")], s.run().problems)

    def test_entry_claims_are_checked_against_the_cli(self):
        for key, value, sentence in (("assets", 601, "CountMismatch"), ("width", 64, "CanvasMismatch"),
                                     ("models", False, "EntryDetailsMismatch"),
                                     ("includesCookBook", False, "EntryDetailsMismatch"),
                                     ("cookbookSha256", None, "EntryDetailsMismatch")):
            with self.subTest(key=key):
                s = self.sc()
                s.facts[key] = value
                self.assertRefused(s.run(), g.say(sentence))

    def test_the_cli_problems_are_the_comment(self):
        s = self.sc()
        s.facts = {"problems": ["Sealed Sets can't be shared or come from the gallery."], "assets": 0, "width": 0,
                   "height": 0, "models": False, "includesCookBook": False, "cookbookSha256": None,
                   "collection": "", "fingerprint": "", "fileSha256": ""}
        self.assertEqual(["Sealed Sets can't be shared or come from the gallery."], s.run().problems)

    def test_the_file_must_hash_to_the_entry(self):
        s = self.sc()
        s.entry["set"]["sha256"] = "0" * 64
        v = s.run()
        self.assertRefused(v, g.say("HashMismatch"))
        self.assertNotIn(["share", "check"], [c[:2] for c in s.calls])  # never opens a file that is not the one listed

    def test_links_must_be_the_authors_release_download(self):
        for url in ("https://evil.example/someone/nfty-public/releases/download/vapor-chests-v1/vapor-chests.set",
                    "http://github.com/someone/nfty-public/releases/download/vapor-chests-v1/vapor-chests.set",
                    "https://github.com/other/nfty-public/releases/download/vapor-chests-v1/vapor-chests.set",
                    "https://github.com/someone/nfty-public/releases/download/vapor-chests-v1/vapor-chests.set?x=1",
                    "https://github.com@evil.example/someone/nfty-public/releases/download/vapor-chests-v1/vapor-chests.set",
                    "https://github.com/someone/nfty-public/releases/download/vapor-chests-v2/vapor-chests.set",
                    "https://github.com/someone/nfty-public/releases/download/vapor-chests-v1/../x.set"):
            with self.subTest(url=url):
                s = self.sc()
                s.entry["set"]["url"] = url
                self.assertRefused(s.run(), g.say("LinkNotOpened"))
        self.assertTrue(g.url_allowed("https://github.com/Someone/nfty-public/releases/download/a-v1/a.set", "someone", "a-v1", "a.set"))

    def test_the_release_must_carry_both_files(self):
        s = self.sc()
        s.releases[0]["assets"] = s.releases[0]["assets"][:1]
        self.assertRefused(s.run(), g.say("ReleaseFileMissing", "preview.png"))

    def test_preview_rules(self):
        s = self.sc()
        s.grid = "embedded: cols=8;rows=4;cell=64x64;count=29\n"
        self.assertEqual([g.say("PreviewBad")], s.run().problems)
        s = self.sc()
        s.grid = "embedded: none\ndetected: none\n"
        self.assertEqual([g.say("PreviewBad")], s.run().problems)
        s = self.sc()
        s.grid_code = 1  # does not decode
        self.assertEqual([g.say("PreviewBad")], s.run().problems)
        s = self.sc()
        s.preview_path.write_bytes(png(2049, 16))
        s.entry["preview"]["size"] = s.preview_path.stat().st_size
        s.entry["preview"]["sha256"] = hashlib.sha256(s.preview_path.read_bytes()).hexdigest()
        self.assertEqual([g.say("PreviewBad")], s.run().problems)
        s = self.sc()
        s.preview_path.write_bytes(b"not a png")
        self.assertEqual([g.say("PreviewBad")], s.run().problems)
        s = self.sc()
        s.preview_path.write_bytes(png(16, 16) + b"\0" * 2_097_153)
        self.assertEqual([g.say("PreviewBad")], s.run().problems)

    def test_entry_must_be_readable_and_named_for_its_file(self):
        s = self.sc()
        del s.entry["fingerprint"]
        self.assertEqual([g.say("EntryUnreadable")], s.run().problems)
        s = self.sc()
        s.entry["slug"] = "other-name"
        self.assertEqual([g.say("EntryNameMismatch")], s.run().problems)
        s = self.sc()
        s.entry["created"] = "yesterday"
        self.assertEqual([g.say("EntryUnreadable")], s.run().problems)

    def test_comment_has_one_bullet_per_sentence_and_no_live_markup(self):
        s = self.sc()
        s.entry["tags"] = ["@everyone <b>[x](http://e.com)</b>\nsecond line"]
        v = s.run()
        text = check.comment_md(v)
        self.assertNotIn("@everyone", text)
        self.assertNotIn("<b>", text)
        self.assertIsNone(re.search(r"(?<!\\)\[", text))
        self.assertTrue(all(line.startswith("- ") for line in text.splitlines()))
        self.assertEqual(len(v.problems), len(text.splitlines()))
        self.assertEqual("This share passes the gallery's checks.\n", check.comment_md(check.Verdict()))

    def test_main_writes_comment_and_verdict(self):
        s = self.sc()
        s.entry["tags"] = ["props", "memes"]
        t = s.tmp
        for name, obj in (("pr-files", s.pr_files), ("pr", s.pr), ("author", s.author), ("tree", s.tree),
                          ("releases", s.releases), ("blocked", s.blocked), ("tags", s.tags),
                          ("repo", {**s.repo, "manifest": {"schemaVersion": 1, "shares": [s.entry]}})):
            (t / (name + ".json")).write_text(json.dumps(obj), encoding="utf-8")
        (t / "pr" / "entries" / "1234567").mkdir(parents=True)
        (t / "pr" / "entries" / "1234567" / "vapor-chests.json").write_text(json.dumps(s.entry), encoding="utf-8")
        (t / "base").mkdir()
        fake = t / "fake_nfty.py"
        fake.write_text("import json, sys\n"
                        "a = sys.argv[1:]\n"
                        "if a[:2] == ['share', 'check']:\n"
                        "    print(json.dumps(%r)); sys.exit(0)\n"
                        "print('embedded: cols=8;rows=4;cell=32x32;count=29')\n" % s.facts, encoding="utf-8")
        out = t / "out"
        argv = ["--pr-files", t / "pr-files.json", "--pr", t / "pr.json", "--author", t / "author.json",
                "--repo", t / "repo.json", "--tree", t / "tree.json", "--releases", t / "releases.json",
                "--entries-dir", t / "base", "--pr-dir", t / "pr", "--blocked", t / "blocked.json",
                "--tags", t / "tags.json", "--set", s.set_path, "--preview", s.preview_path,
                "--nfty", '"%s" "%s"' % (sys.executable, fake), "--out", out]
        old = sys.stdout
        sys.stdout = io.StringIO()
        try:
            code = check.main([str(a) for a in argv])
        finally:
            sys.stdout = old
        self.assertEqual(1, code)
        self.assertEqual("- " + g.say("UnknownTag", "memes") + "\n", (out / "comment.md").read_text(encoding="utf-8"))
        verdict = json.loads((out / "verdict.json").read_text(encoding="utf-8"))
        self.assertEqual({"ok": False, "pr": 7, "head": s.pr["head"]["sha"], "problems": [g.say("UnknownTag", "memes")]}, verdict)


class SentenceTests(unittest.TestCase):
    @staticmethod
    def csharp_sentences():
        """Every ``ShareRules`` string literal, interpolations as ``{}`` and ``{ConstName}`` filled in."""
        src = RULES_CS.read_text(encoding="utf-8")
        consts = {m.group(1): m.group(2) for m in re.finditer(r"public const (?:int|long) (\w+) = ([\d_]+);", src)}
        consts = {k: v.replace("_", "") for k, v in consts.items()}
        found = {}
        for m in re.finditer(r"public (?:static|const) string (\w+)(?:\([^)]*\))?\s*(?:=>|=)\s*(\$?)\"", src):
            i, depth, out = m.end(), 0, []
            interpolated = bool(m.group(2))
            while True:
                ch = src[i]
                if interpolated and ch == "{" and src[i + 1] != "{":
                    depth += 1
                    expr = []
                    i += 1
                    while depth:
                        if src[i] == "{":
                            depth += 1
                        elif src[i] == "}":
                            depth -= 1
                            if not depth:
                                break
                        expr.append(src[i])
                        i += 1
                    expr = "".join(expr)
                    out.append(consts.get(expr, "{}"))
                elif ch == "\\" and src[i + 1] == '"':
                    out.append('"')
                    i += 1
                elif ch == '"':
                    break
                else:
                    out.append(ch)
                i += 1
            found[m.group(1)] = "".join(out)
        return found, consts, src

    def test_sentences_match_csharp(self):
        cs, _, _ = self.csharp_sentences()
        for name, text in g.SENTENCES.items():
            with self.subTest(name):
                self.assertIn(name, cs, "not a ShareRules member")
                self.assertEqual(cs[name], text)

    def test_set_too_large_matches_csharp(self):
        _, _, src = self.csharp_sentences()
        self.assertIn('$"{Size(bytes)} — the gallery takes up to {Size(MaxSetBytes)}."', src.replace("var head = ", "").replace("head = ", ""))
        self.assertIn('$"{head} Share fewer assets."', src)
        self.assertEqual("{} — the gallery takes up to {}. Share fewer assets.", g.SET_TOO_LARGE)

    def test_limits_match_csharp(self):
        _, consts, _ = self.csharp_sentences()
        for py, cs in (("MAX_SHARES", "MaxShares"), ("MAX_NAME", "MaxNameLength"), ("MAX_DESCRIPTION", "MaxDescriptionLength"),
                       ("MAX_TAGS", "MaxTags"), ("MAX_HISTORY_KB", "MaxHistoryKb"), ("MIN_ACCOUNT_AGE_DAYS", "MinAccountAgeDays"),
                       ("MAX_SET_BYTES", "MaxSetBytes"), ("MAX_PREVIEW_BYTES", "MaxPreviewBytes"),
                       ("MAX_PREVIEW_SIDE", "MaxPreviewSide"), ("MAX_CANVAS", "MaxCanvas")):
            with self.subTest(py):
                self.assertEqual(int(consts[cs]), getattr(g, py))

    def test_size_text_matches_dotnet(self):
        self.assertEqual("214 MB", g.size_text(224_395_264))
        self.assertEqual("200 MB", g.size_text(209_715_200))
        self.assertEqual("1.5 MB", g.size_text(1_572_864))
        self.assertEqual("1.3 MB", g.size_text(1_363_148))  # 1.29999 rounds up to 1.3
        # a size just over the limit never reads as the limit
        self.assertEqual("200.1 MB", g.size_text(209_715_201))
        self.assertTrue(g.set_too_large(209_715_201).startswith("200.1 MB — the gallery takes up to 200 MB."))


class IndexTests(unittest.TestCase):
    def test_build_index_matches_csharp_ordering(self):
        got = build_index.build(FIX / "index_entries", "2026-11-09T14:10:00Z")
        expected = (FIX / "index.expected.json").read_bytes()
        self.assertEqual(expected, got)

    def test_blocked_users_and_sets_are_left_out(self):
        got = json.loads(build_index.build(FIX / "index_entries", "2026-11-09T14:10:00Z", {"users": [42], "sets": []}))
        self.assertEqual(["bones", "vapor-chests"], [e["slug"] for e in got["entries"]])
        got = json.loads(build_index.build(FIX / "index_entries", "2026-11-09T14:10:00Z", {"users": [], "sets": ["a" * 64]}))
        self.assertEqual([], got["entries"])

    def test_numeric_owner_order_not_text_order(self):
        with tempfile.TemporaryDirectory() as d:
            for owner, login in ((9, "nine"), (10, "ten")):
                p = Path(d) / str(owner)
                p.mkdir()
                e = json.loads((FIX / "index_entries" / "42" / "cafe-cats.json").read_text(encoding="utf-8"))
                e["owner"] = {"id": owner, "login": login}
                (p / "cafe-cats.json").write_text(json.dumps(e), encoding="utf-8")
            got = json.loads(build_index.build(d, "2026-11-09T14:10:00Z"))
            self.assertEqual([9, 10], [e["owner"]["id"] for e in got["entries"]])

    def test_a_bad_entry_stops_the_build(self):
        with tempfile.TemporaryDirectory() as d:
            (Path(d) / "1").mkdir()
            (Path(d) / "1" / "x.json").write_text("{}", encoding="utf-8")
            with self.assertRaises(SystemExit):
                build_index.build(d, "2026-11-09T14:10:00Z")

    def test_timestamps_normalize_like_dotnet(self):
        self.assertEqual("2026-11-09T21:02:00.123+02:00", g.Ts("2026-11-09T21:02:00.1230000+02:00").text)
        self.assertEqual("2026-11-09T14:02:00+00:00", g.Ts("2026-11-09T14:02:00.0000000Z").text)
        self.assertEqual(g.Ts("2026-11-09T21:02:00+02:00").ticks, g.Ts("2026-11-09T19:02:00Z").ticks)
        with self.assertRaises(g.Unreadable):
            g.Ts("2026-13-09T14:02:00Z")


class TagsFileTests(unittest.TestCase):
    """tags.json is read by the app (CommunityTags.Read) and published beside index.json."""
    REPO = ROOT / "tools" / "gallery" / "repo"

    def test_the_list_obeys_the_apps_rules(self):
        data = json.loads((self.REPO / "tags.json").read_text(encoding="utf-8"))
        self.assertEqual(1, data["schemaVersion"])
        tags = data["tags"]
        self.assertLessEqual(len(tags), 32)
        self.assertEqual(len(tags), len(set(tags)))
        for t in tags:
            self.assertRegex(t, r"^[a-z0-9][a-z0-9-]{0,23}$")
        self.assertEqual(["characters", "creatures", "props", "items", "scenery", "effects", "ui", "abstract"], tags)

    def test_the_site_build_publishes_tags_beside_the_index(self):
        yml = (self.REPO / ".github" / "workflows" / "publish.yml").read_text(encoding="utf-8")
        self.assertIn("> site/index.json", yml)
        self.assertIn("cp tags.json site/tags.json", yml)
        self.assertRegex(yml, r'paths:[\s\S]*"tags\.json"')      # editing the list redeploys the site
        self.assertIn("path: site", yml)


class FetchTests(unittest.TestCase):
    class Resp:
        def __init__(self, data, length=None):
            self.buf, self.headers, self.closed = io.BytesIO(data), {}, False
            if length is not None:
                self.headers["Content-Length"] = str(length)
            self.headers = type("H", (), {"get": lambda _s, k, d=None, h=self.headers: h.get(k, d)})()

        def read(self, n):
            return self.buf.read(n)

        def close(self):
            self.closed = True

    class Opener:
        def __init__(self, resp=None, error=None):
            self.resp, self.error, self.urls = resp, error, []

        def open(self, req, timeout=None):
            self.urls.append(req.full_url)
            if self.error:
                raise self.error
            return self.resp

    def test_download_writes_the_file(self):
        with tempfile.TemporaryDirectory() as d:
            dest = os.path.join(d, "x")
            self.assertEqual(("ok", 5), fetch.download("https://github.com/x", dest, 10, self.Opener(self.Resp(b"hello"))))
            self.assertEqual(b"hello", Path(dest).read_bytes())

    def test_download_aborts_past_the_cap_without_reading_it_all(self):
        with tempfile.TemporaryDirectory() as d:
            dest = os.path.join(d, "x")
            resp = self.Resp(b"x" * (3 << 20))
            status, seen = fetch.download("https://github.com/x", dest, 2 << 20, self.Opener(resp))
            self.assertEqual("toolarge", status)
            self.assertLessEqual(seen, (2 << 20) + fetch.CHUNK)
            self.assertFalse(os.path.exists(dest))
            self.assertTrue(resp.closed)
            # a declared length over the cap is refused before a byte is read
            resp = self.Resp(b"x", length=10 << 30)
            self.assertEqual(("toolarge", 10 << 30), fetch.download("https://github.com/x", dest, 2 << 20, self.Opener(resp)))
            self.assertEqual(0, resp.buf.tell())

    def test_a_404_is_missing_and_other_errors_raise(self):
        err = lambda code: urllib.error.HTTPError("u", code, "m", {}, io.BytesIO(b""))
        with tempfile.TemporaryDirectory() as d:
            self.assertEqual(("missing", 0), fetch.download("https://github.com/x", os.path.join(d, "x"), 1, self.Opener(error=err(404))))
            with self.assertRaises(urllib.error.HTTPError):
                fetch.download("https://github.com/x", os.path.join(d, "x"), 1, self.Opener(error=err(500)))

    def test_only_the_authors_release_url_is_requested(self):
        e = Scenario(self).entry
        e["set"]["url"] = "https://evil.example/a.set"
        opener = self.Opener(self.Resp(b"abc"))
        with tempfile.TemporaryDirectory() as d:
            report = fetch.run(e, "someone", d, opener)
        self.assertEqual("refused", report["set"]["status"])
        self.assertEqual(["https://github.com/someone/nfty-public/releases/download/vapor-chests-v1/preview.png"], opener.urls)

    def test_redirects_stay_on_github(self):
        for url, ok in (("https://github.com/a", True), ("https://objects.githubusercontent.com/a", True),
                        ("https://release-assets.githubusercontent.com/a", True), ("http://github.com/a", False),
                        ("https://evilgithub.com/a", False), ("https://github.com.evil.io/a", False),
                        ("https://user@github.com/a", False), ("https://github.com:8443/a", False)):
            with self.subTest(url=url):
                self.assertEqual(ok, fetch.host_allowed(url))


class HealthTests(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.tmp, True)
        shutil.copytree(FIX / "index_entries", self.tmp / "entries")
        self.health = self.tmp / "health.json"

    def run_health(self, gone_urls):
        return health.run(self.tmp / "entries", self.health, lambda url: "gone" if url in gone_urls else "ok")

    def test_three_misses_in_a_row_mark_the_entry_gone(self):
        url = "https://github.com/someone/nfty-public/releases/download/bones-v1/bones.set"
        for night in (1, 2):
            self.assertEqual([], self.run_health({url}))
            self.assertEqual(night, json.loads(self.health.read_text())["1234567/bones"]["misses"])
        self.assertEqual(["1234567/bones"], self.run_health({url}))
        entry = json.loads((self.tmp / "entries/1234567/bones.json").read_text(encoding="utf-8"))
        self.assertEqual("gone", entry["status"])
        self.assertNotIn("1234567/bones", json.loads(self.health.read_text()))
        index = json.loads(build_index.build(self.tmp / "entries", "2026-11-09T14:10:00Z"))
        self.assertEqual(["vapor-chests"], [e["slug"] for e in index["entries"] if e["owner"]["id"] == 1234567])

    def test_a_hit_resets_the_count_and_an_outage_changes_nothing(self):
        url = "https://github.com/someone/nfty-public/releases/download/bones-v1/bones.set"
        self.run_health({url})
        self.run_health({url})
        health.run(self.tmp / "entries", self.health, lambda u: "unknown")
        self.assertEqual(2, json.loads(self.health.read_text())["1234567/bones"]["misses"])
        self.run_health(set())
        self.assertNotIn("1234567/bones", json.loads(self.health.read_text()))

    def test_either_file_missing_counts_and_a_gone_entry_is_left_alone(self):
        preview = "https://github.com/someone/nfty-public/releases/download/bones-v1/preview.png"
        self.run_health({preview})
        self.assertEqual(1, json.loads(self.health.read_text())["1234567/bones"]["misses"])
        self.assertNotIn("42/old-gone", json.loads(self.health.read_text()))

    def test_head_refuses_other_hosts(self):
        self.assertEqual("unknown", health.head("https://evil.example/x"))


if __name__ == "__main__":
    unittest.main()
