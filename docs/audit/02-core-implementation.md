# Core implementation and verification

Approved by the user on 2026-11-05. Primary acceptance criteria: correct mathematics and logic, deterministic/reproducible results, and stable Release behavior.

Baseline: `95eb1e8`. Work remains in the existing isolated `codex/ticket-audit-fixes` worktree. No merge or push is authorized.

Status: complete and verified on 2026-11-05. Stopped after Core for further user instructions. Final evidence: [Core verification](02-core-verification.md).

## Completed tasks

- [x] Archive/editing owner: C07–C11, C21; D13–D19; S05. Preserve Validator's report-only behavior and historical archive compatibility.
- [x] Math owner: C12–C15, C22; D01–D05, D21; S04/S06. Preserve RNG, DNA, accumulation order and unaffected generated pixels.
- [x] Publishing/output owner: C01–C06, C19/C20; D06–D12; S01–S03. Include genuine legacy seal/metadata reads and failure recovery.
- [x] Integration owner: C16–C18; D20/D22; S07, API regeneration, independent review and overall verification.
- [x] Reproduce corrected defects with meaningful regressions before implementation.
- [x] Review integrated changes for mathematical correctness, ownership, cancellation, ordering and compatibility.
- [x] Correct additional endpoint, legal-selection certainty and extreme-weight reporting defects; preserve ordinary small joint probabilities.
- [x] Verify exact rational probability/count oracle across 72 valid weighted/optional/ruled book configurations.
- [x] Build and test the full solution in Release: zero warnings/errors; 4,979 passed, zero failed, one platform-specific skip.
- [x] Regenerate API documentation and run lint/check: 269 pages current; strict manual build passed.
- [x] Compare ordinary valid output with the reviewed baseline: 470/470 files identical, excluding intentional ZIP timestamps.
- [x] Verify direct thread parity and Release output across four cultures.
- [x] Verify rendered application output: 427 frames per build, with count text, random paths and deliberate loading states accounted for.
- [x] Publish final evidence, limitations, compatibility decisions and remaining deferred work.
- [x] Update the overall tracker and stop after Core; no later project review started.

## Scope decisions

- Include C21's latent lifetime fix and C22's counting-budget correction: both protect correctness at existing API boundaries.
- New seals use an unambiguous versioned authenticated header. Legacy headers retain their historical decoding/MAC bytes; existing label ambiguity cannot be retroactively repaired. New-version files may require the new reader.
- Nonempty export destinations may be refused rather than deleting unrelated user data. This intentionally changes repeated-export failure behavior.
- Retain pre-existing/unknown-ownership pins on failure; cleanup must not infer ownership merely from a returned CID.
- Defer S08 shared rule diagnostics and optional footprint traversal consolidation. Existing legality policies differ; neither refactor is necessary for the approved correctness fixes.
- Keep broad optimization proposals and declared backlog outside this change.

## Preservation boundary

Successful valid generation must retain seed interpretation, draw order, layer order, DNA, PNG bytes, numbering and ordinary metadata payloads. Counting/probability text, malformed-input errors, failure rollback, legacy absence rarity, protected export behavior and new seal authentication change only as explicitly approved corrections. Existing frozen fixtures stay untouched.

Host verification can demonstrate the available OS/architecture, cultures and thread counts. Do not claim an executed cross-architecture comparison without another architecture actually running it.
