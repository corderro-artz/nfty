# Model implementation and verification

Approved by the user: implement Model fixes and stop for verification before CLI. Baseline: `24f2ed6`, tag `audit-core-verified-2026-11-05`. Verification was presented and accepted; the user authorized proceeding on 2026-11-06. Existing isolated worktree and branch are retained, with local checkpoint tag `audit-model-verified-2026-11-06`. No merge or push.

## Completed tasks

- [x] Renderer owner: M01/M02; M11 best-effort framing policy; D11–D15. Prove coverage and constrained zoom behavior independently.
- [x] Format owner: M03–M05/M08/M09; D07–D10; S01/S02. Preserve valid writer bytes and supported read/write round-trips.
- [x] Geometry owner: M07; D01–D06; S03/S04. Refuse nonrepresentable geometry without changing accepted transform arithmetic.
- [x] Integration owner: M06/M10; D16/D17; S05–S07. Preserve complete model pairs under failure and accurate callback/lifetime contracts.
- [x] Observe focused failing regressions, then passing nearest suites.
- [x] Independently review integrated changes for numerical correctness, ownership, rollback and compatibility.
- [x] Correct additionally discovered T-junction coverage and UV-grid rounding defects; verify independent ray, overlap, grid and camera probes.
- [x] Verify secondary rollback failure retains original backups and names their recovery directory.
- [x] Build and test the final solution in Release: zero warnings/errors; 5,148 passed, zero failed, one platform-specific skip. Model: 669 passed, 169 additional cases.
- [x] Regenerate 269 API pages and run documentation lint/check and strict manual build.
- [x] Compare 470/470 sampled files and 60 mesh/format goldens with baseline; verify direct thread and four-culture writer parity.
- [x] Inspect 431 rendered frames: 356 identical, 67 with intended 1–32-pixel preview corrections, six random-path labels and two in-flight loading states.
- [x] Record [verification evidence and limitations](03-model-verification.md), update tracker and stop for user verification before CLI.

## Policy and preservation boundary

- M11 preserves the existing maximum camera distance; framing is explicitly best effort when that limit prevents fitting.
- M07 rejects collapsed transformed geometry rather than imposing a new blanket scale range or silently clamping coordinates.
- File notifications occur after a complete model reaches the folder. Failed replacement restores the prior model; completed models before cancellation remain.
- Model formats remain nfty's supported subset; unsupported semantics are refused, not silently ignored or implemented as a generic reader.
- Ordinary mesh coordinates, vertex order, UVs, encoded model bytes, PNGs, DNA and RNG consumption must remain unchanged. Preview coverage and constrained camera behavior intentionally change for corrected cases.
- Keep optional solid-mask-composition and off-screen-DDA optimizations deferred; neither has measurement and preservation proof.
- No fixture regeneration, archived-design edits, package upgrades or production GUI layout changes.
