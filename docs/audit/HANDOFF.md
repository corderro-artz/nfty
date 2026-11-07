# Audit handoff — 2026-11-07

Open this exact worktree: `C:/Users/Corde/.codex/worktrees/ticket-audit-fixes/nfty`.
Branch: `codex/ticket-audit-fixes`. Read `CLAUDE.md`, `AGENTS.md`, `docs/audit/REVIEW.md` and `docs/audit/05-app.md` first. Another application's chat history is not stored in these files.

## Current state

- Ticket, Core, Model and CLI fixes are accepted and locally committed. Latest commit: `cf67cce`, local tag `audit-cli-verified-2026-11-06`.
- The user authorized publishing those completed fixes as a GitHub release, then implementing the approved App findings. The user now requires releases to originate from main, not a temporary/development branch. Merge readiness was questioned; do not interpret that question as an unconditional instruction to merge before remaining gates pass.
- Release version chosen: `2.4.8`; `Directory.Build.props` is updated but uncommitted. GitHub currently has no v2.4.8 release. No release push or release tag has been performed.
- All four Windows x64 packages successfully built in `.release/`. Log: `artifacts/audit/release-2.4.8/win-build.log`. Release notes draft: `artifacts/audit/release-2.4.8/notes.md`.
- Linux packages, packaged-binary verification, release commit/tag, push and GitHub publication remain unfinished. The packaging script DELETES `.release/` at each run: preserve the Windows ZIPs elsewhere before building Linux. Verify resolved deletion targets stay inside this workspace.
- The Windows build completed after the chat interruption. There is no packaging job intentionally left running.
- App two-pass review is complete: 21 behavior findings, 20 grouped documentation corrections, 12 bounded simplifications. Implementation is approved after the release; no App fixes have started. Detailed source references, tests and preservation requirements are in `05-app.md`.
- Remaining tracked changes are audit status/review documents and the release version. `05-app.md` and this handoff may be untracked; inspect git status rather than discarding them.

## Verification already available

- Completed implementation gate: Release solution build zero warnings/errors; 5,228 tests passed, one platform skip. CLI final evidence: `docs/audit/04-cli-verification.md`.
- 470 sampled files byte-identical; thread and four-culture parity, model/authoring payload checks and rendered-frame comparisons documented in the completed stage reports.
- App review baseline: fresh Release solution build zero warnings/errors. First App run: 1,927 pass, one headless cleanup failure, one skip; failed class repeat 27/27 pass; full repeat 1,928 pass, zero failures, one skip. H01 remains an unresolved intermittent harness issue; do not call it fixed merely because a repeat passed.
- Package publishing changed build outputs/version, so re-establish the appropriate build/test and packaged smoke gates before publication. No blanket guarantee of exhaustive functional equivalence: documented bug-boundary/numerical corrections intentionally differ.

## Merge/release readiness

The completed fixes are strongly verified, but the current snapshot is not certified exhaustively non-breaking. New version-2 sealed exports require an updated reader; legacy version-1 seals remain readable by the new build. Corrected numerical counts and model previews intentionally differ. App findings are existing baseline defects, not implemented changes in this checkpoint. Remaining gates include packaged-binary smoke/output checks, Linux packaging/verification and a final clean release verification after the version bump. H01 remains unresolved. Merging main would not rewrite the existing published v2.4.7 downloads; publication is a separate action. All remaining audit stages and App findings stay tracked in REVIEW.md/05-app.md.

## Working agreement

Use concise chat; normal English in persisted docs. Two separate review passes per project. No fixtures regeneration, archived design edits or ImageSharp upgrade beyond 3.1.11. Preserve deterministic RNG/DNA/output rules and image ownership. Delegate approved fixes using an economical model when suitable. Report verification limits accurately. Never push secrets or ignored artifacts other than deliberately selected release assets.

## Next steps

1. Reconcile current git/build/package state; do not repeat completed audits.
2. Preserve Windows packages, build the four Linux x64 shapes using `tools/release/build.py`, and verify packaged front-ends/demo/output. WSL Ubuntu is available.
3. Complete verification and settle compatibility/release readiness. The user requires a release tag on main: merge the verified checkpoint into main only when authorized and ready, then tag/push/publish all eight assets and verify GitHub. Do not publish a release from the audit branch.
4. Only after publication, dispatch approved App implementation A01–A21/D01–D20/S01–S12 with explicit file ownership. Fix correctness/ownership first; preserve sampled output and rendered geometry. Investigate H01 without hiding failures.
5. Update tracker, verify and stop for the user's inspection before proceeding to the Desktop review.
