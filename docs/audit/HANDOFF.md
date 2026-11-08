# Audit handoff — 2026-11-08

Worktree: `C:/Users/Corde/.codex/worktrees/ticket-audit-fixes/nfty`. Branch: `codex/ticket-audit-fixes`. Checkpoint: `audit-app-verified-2026-11-08`. Read CLAUDE.md, AGENTS.md, REVIEW.md and 05-app-verification.md. Other applications' chat histories are separate from these files.

## Latest checkpoint — final integration

All nine project/tooling reviews and approved fixes are complete. See 06-desktop.md,
07-ticket-sample.md, 08-embedding-sample.md, 09-tooling.md and 10-final-verification.md.
Final forced Release build: zero warnings/errors. Test targets: 5,368 passes/one Linux-only skip;
470/470 output payloads exact; 431 frames compared, 423 exact and eight classified.
Both controlled callback probes pass. Independent external model-viewer validation is complete.
Fresh packages, main merge/tag/publication and a separate public-docs commit remain required.
The ignored AFTER-ACTION-draft.md enumerates every fix/simplification/XML result; finalize it
as docs/audit/AFTER-ACTION.md after publication. Do not claim pending release assets are verified.

## Latest user direction — remaining stages and release

The user has superseded the earlier stop boundary: finish Desktop, Ticket.Sample, Embedding.Sample and tooling/integration, including the new requirement that main and viewer windows open centered on the opening display (primary fallback). After final verification, commit and merge into main, tag/push and publish the newest audited release (v2.4.8 if still available). Maximum two agents at once. Current implementation is active; the prior checkpoint details below remain historical evidence, not a claim that these remaining stages are complete.

## Previous stop boundary (superseded)

Ticket, Core, Model, CLI and App both-pass audits and approved fixes are locally committed/tagged and verified. **Stop before Desktop, Ticket.Sample, Embedding.Sample and tooling/CI/integration reviews. No merge, push or publication.** The user requested at most two agents; the two final agents have finished. Old Set compatibility is waived; math, determinism, ownership and fixed geometry remain requirements.

## Final evidence

- Release solution: zero warnings/errors; 5,335 passed, zero failed, one Linux-only skip. App: 2,032 passed, 104 more than baseline. Final full run follows the last UI correction.
- Output: 470/470 byte-identical to canonical-LF checkpoint; one/four-thread comparisons 98/98 demo and 38/38 fixture files.
- Rendered frames: 431 compared, 417 exact; six temporary labels, two LF-related size labels, four dimension-label alignments, one loading state, one ten-pixel single-level control-edge difference. Every changed region inspected; controlled comparisons recorded. Share-bar frames remain exact; minimum-voxel text fits in the fixed 26px row.
- H01 actual application callback: old binary fails forced reset gap, fixed code passes live/disposed cases. Owner dispatchers and pending ownership prevent stale callbacks claiming another application. Both mesh-disposal interleavings are covered. The historical competing callback was not recorded; upstream reset gap remains a framework constraint. Tracked standalone regression: tools/verification/headless-callback-probe/README.md.
- API lint clean, 269 pages current, strict manual passed, compiled App XML duplicate summaries zero. Fixtures, archived designs and package pins untouched.

Full report/checklist: 05-app-verification.md and 05-app-implementation.md. Ignored artifacts/audit/ retains failed-before evidence, focused/full logs, hashes, screenshots and pixel inventories. Do not repeat completed audits or publish old candidate packages.

## Merge and release

Main checkout M:/Repositories/nfty is clean; origin/main is an ancestor of this branch. Two obstructing old untracked notes were preserved with SHA-256 verification at M:/Repositories/nfty-audit-notes-before-merge-20261108/docs/audit; no tracked main file changed. Recheck status/ancestry before any future authorized merge.

No GitHub push, release tag or publication occurred. Existing eight v2.4.8 candidate packages are older evidence and need rebuilding before publication. Eventual releases originate from main. tools/release/build.py deletes .release/ each time: verify the resolved workspace target and preserve needed packages first.

## Limits and deferred work

Native macOS Keychain replacement, real Linux keyring failure, native mixed-DPI movement, live cloud upload, another CPU architecture and an external model viewer were not executed. Status-policy tests and pure DPI arithmetic do not claim native execution. Minimal Desktop DPI bridges support App A19 and are not its full review.

BACKLOG.md retains GPU/performance proposals, external model-viewer validation, large-image Rise lag, walker consolidation, small audit leftovers and more pinning providers. These were not silently included in the completed App scope.
