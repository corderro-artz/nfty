# Completed audit handoff — 2026-11-08

Active development: `M:/Repositories/nfty`, branch main. All nine two-pass reviews and approved
fixes are complete. The consolidated AFTER-ACTION.md lists every finding, simplification, XML
correction, result and limit. REVIEW.md is the completed task list; BACKLOG.md contains deliberate
optional work, not unfinished approved fixes.

## Published releases and verification

- v2.4.8: main 6c07a38, completed suite audit and centered main/viewer startup; eight verified assets.
- v2.4.9: main c23eac4, UI 6b8617b, Pillow/tooling a0c23f2, documentation c23eac4. Eight fresh assets,
  all server digests verified. All 67 payloads match Windows/Linux and 2.4.8/2.4.9 in every shape.
- Final local Release build: zero warnings/errors; 5,375 passed, zero failed, one Linux-only skip.
  App 2,067 passes. Release CI 42530430593: all nine jobs successful.
- 453 frames compared: 403 common exact, 20 intentional Cook changes, six random path labels,
  two tiny raster differences, 22 new frames. Cook 49 focused passes include seven new cases;
  geometry/keyboard/scan-state checks and 18 footer pixel comparisons pass in both themes.
- Four manual screenshots use real 50-asset/50-model production cooks and exact source crops.
  Strict manual passes; all 269 generated API pages current, XML lint clean. Live four image hashes
  and current API record/constraint declarations verified. Site deployment 42531923431 succeeded.
- Pillow 13 tooling/Python 3.11 pin resolves all 59 recorded ranges; GitHub open dependency alerts 0.
  Demo 49 PNGs/45 depth maps unchanged; frozen fixtures and ImageSharp 3.1.11 remain untouched.

Release notes/inventories: releases/2.4.8.json and releases/2.4.9.json. Evidence is ignored under
main artifacts/audit, including 2.4.9-final-tests-corrected.log, 2.4.9-frame-parity.json,
release-2.4.9/live-site-verification.json, and the Cook/security preflight reports. Failed-before
and superseded harness attempts remain disclosed, not counted as passing results.

## Branch and checkout cleanup

The merged codex/ticket-audit-fixes branch is deleted. All 25,780 ignored evidence files were
copied to main and SHA-256 verified; inventory artifacts/audit-preservation-sha256.json. Codex's
archive tool refused folder removal: “The task’s primary checkout cannot be removed.” The retained
C:/Users/Corde/.codex/worktrees/ticket-audit-fixes/nfty folder is detached and can track a clean
snapshot of current main; all future development/commits/releases belong on the M: main checkout.
Do not bypass the primary-checkout protection. Other historical worktrees were not touched.

## Reusable documentation workflow

python tools/api-docs/build.py regenerates Ticket/Core/Model reference pages from compiler XML
and reflection; run --check and --lint separately. MkDocs builds their HTML. The live reference is
https://www.vaporsoft.dev/nfty/docs/reference/api/. The separate site repository's existing
workflow can publish immediately with:

gh workflow run deploy.yml -R corderro-artz/corderro-artz.github.io --ref main

The optional SITE_DISPATCH_TOKEN is absent, so repository notifications alone do not immediately
publish. Manual dispatch was used and actual deployment/images/API were verified for this release.
No website code, credentials, permissions or wiki settings changed.

Read CLAUDE.md and AGENTS.md. Never regenerate tests/fixtures, edit docs/design/archive or upgrade
ImageSharp beyond 3.1.11. Preserve ordinal/invariant output, caller-owned images and fixed geometry.
The release builder deletes .release each run; preserve validated archives before another RID build.
