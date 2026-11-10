# tools/gallery

The template and tooling for the `nfty-gallery` index repository (spec: `docs/superpowers/specs/2026-11-09-community-gallery-design.md`,
sections 3 to 5). It lives here so it is versioned with the code it mirrors; the real repository is a copy.

## Layout

| Path | What |
|---|---|
| `repo/` | The repository's files: workflows, issue template, `blocked.json`, `tags.json`, README. |
| `check.py` | The pull request checker (section 4). Reads data, never runs PR content. Writes `comment.md` + `verdict.json`. |
| `fetch.py` | Downloads the entry's Set and preview, only from `https://github.com/<author>/nfty-public/releases/download/<release>/…`, aborting past the size cap. |
| `gather.sh` | check.yml's data gathering: the PR's files, the author, their `nfty-public`, other open PRs' entries. |
| `build_index.py` | `entries/` to `index.json`, byte-identical to `CommunityIndex.Write`. |
| `health.py` | The nightly HEAD check and the 3-misses `gone` marker. |
| `nfty_version.py` | Picks the nfty release whose CLI checks a share: the newest published one at or above `NFTY_MIN_VERSION`, or the `NFTY_VERSION` repository variable when set. |
| `gallerylib.py` | Shared: the `ShareRules` sentences this repo speaks, entry reading and writing, duplicate/date/scan rules. |

Tests: `python -m unittest tools.tests.test_gallery -v` from the repository root. They read `ShareRules.cs`
(every sentence the scripts speak must equal the C# one) and compare `build_index.py` with
`tools/tests/gallery_fixtures/index.expected.json`, which the Core test `Index_fixture_for_python_is_current`
keeps equal to `CommunityIndex.Write`'s bytes. To refresh that file after changing the index format, run
that test once with `NFTY_WRITE_FIXTURE=1`.

## Setting up the real repository

1. Create the `nfty-gallery` repository. Copy the contents of `repo/` into it (including `.github/`).
2. Copy `check.py`, `fetch.py`, `gather.sh`, `build_index.py`, `health.py`, `nfty_version.py` and `gallerylib.py` into the new
   repository's `scripts/`.
3. Nothing to set for the nfty version. `check.yml` resolves it on every run: the newest published nfty
   release (`NFTY_REPO`, `corderro-artz/nfty`) at or above `NFTY_MIN_VERSION` that carries
   `nfty-<version>-linux-x64-portable.tar.gz` (`tools/release/build.py` makes it). A new nfty release reaches
   the gallery on its next check. To hold the gallery on one release (a release that changes what a legal Set
   is before the gallery is ready for it), set the repository variable `NFTY_VERSION`; clear it to follow
   releases again. Raise `NFTY_MIN_VERSION` only when an older CLI must never check a share again.
4. Settings, Pages: source **GitHub Actions**. Settings, Actions, General: workflow permissions
   **Read repository contents** by default (each workflow states what it needs), and allow Actions to
   create and approve pull requests off. Branch protection on `main`: none that needs a review or a status
   the Action can't give; the checks are the gate. Allow squash merging.
5. Fork pull requests from first-time contributors wait for approval by default. Leave "require approval for
   all outside collaborators" **off**, or no share merges by itself.
6. The actions are pinned to full commit SHAs (the trailing comment names the tag). Refresh them with Dependabot
   for `github-actions` in the real repository, or by hand: `gh api repos/actions/<name>/commits/<tag> --jq .sha`.

## How the workflows stay safe

* `check.yml` is `on: pull_request` with `permissions: contents: read` and no secrets. It checks out the
  **base** commit, reads the pull request's files through the API as bytes, and never executes any of them.
* `merge.yml` is `on: workflow_run` and holds the write token. It reads only `verdict.json` and
  `comment.md`, validates the pull request number is an integer, and requires that the verdict is for the
  commit that was checked and is still the pull request's head. Because `check.yml` runs the workflow file a
  pull request carries, `merge.yml` also re-checks, itself, that the pull request changes nothing but
  `entries/<id>/<slug>.json`; otherwise it closes it with a fixed sentence and ignores the artifact.
* Every address the Action downloads from is rebuilt from the author's login and the entry's release; the
  entry's own URL must equal it exactly. Redirects are followed only within `github.com` and
  `*.githubusercontent.com`, and a download stops the moment it passes its cap.
* Account age, rates and duplicates are decided from GitHub's answers and the entries already merged, never
  from claims in the entry.

## Live values

The shipped gallery is `vaporsoft-dev/nfty-gallery` (Pages: `https://vaporsoft-dev.github.io/nfty-gallery/`), signed in
through the vaporsoft-dev OAuth app; `CommunityEndpoints.Default` in
`src/Nfty.Core/Publish/Community/GitHubModels.cs` holds both, pinned by `Default_endpoints_are_filled`.
