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
| `gallerylib.py` | Shared: the `ShareRules` sentences this repo speaks, entry reading and writing, duplicate/date/scan rules. |

Tests: `python -m unittest tools.tests.test_gallery -v` from the repository root. They read `ShareRules.cs`
(every sentence the scripts speak must equal the C# one) and compare `build_index.py` with
`tools/tests/gallery_fixtures/index.expected.json`, which the Core test `Index_fixture_for_python_is_current`
keeps equal to `CommunityIndex.Write`'s bytes. To refresh that file after changing the index format, run
that test once with `NFTY_WRITE_FIXTURE=1`.

## Setting up the real repository

1. Create the `nfty-gallery` repository. Copy the contents of `repo/` into it (including `.github/`).
2. Copy `check.py`, `fetch.py`, `gather.sh`, `build_index.py`, `health.py` and `gallerylib.py` into the new
   repository's `scripts/`.
3. In `.github/workflows/check.yml` set `NFTY_REPO` (the nfty repository, `nfty-art/nfty` is a placeholder)
   and `NFTY_VERSION` (the release whose CLI runs `share check`; the check expects the release `v<version>`
   to carry `nfty-<version>-linux-x64-portable.tar.gz`, which `tools/release/build.py` makes). Bump
   `NFTY_VERSION` when a release changes what a legal Set is.
4. Settings, Pages: source **GitHub Actions**. Settings, Actions, General: workflow permissions
   **Read repository contents** by default (each workflow states what it needs), and allow Actions to
   create and approve pull requests off. Branch protection on `main`: none that needs a review or a status
   the Action can't give; the checks are the gate. Allow squash merging.
5. Fork pull requests from first-time contributors wait for approval by default. Leave "require approval for
   all outside collaborators" **off**, or no share merges by itself.
6. Pin the actions. `actions/checkout`, `setup-python`, `upload-artifact`, `download-artifact`,
   `upload-pages-artifact` and `deploy-pages` are referenced by major tag; replace each with the full commit
   SHA of the release you trust (`gh api repos/actions/checkout/git/ref/tags/v4 --jq .object.sha`) before the
   gallery opens, and let Dependabot keep them current.

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
