# Stage 2 — Core fixes and verification

Status: complete and verified on 2026-11-05. Work stopped after Core at the user's request; later project reviews remain pending.

User approval: 2026-11-05, with mathematical/logical correctness, determinism and reproducibility as the primary criteria. Reviewed baseline: `95eb1e8`. User subsequently authorized the local commit and tag `audit-core-verified-2026-11-05`. All work remains in the isolated `codex/ticket-audit-fixes` worktree; no merge, push or release publication has been made.

## Completed implementation

| Findings | Final behavior |
|---|---|
| C01, C02, C06 | Folder export stages its owned files, refuses occupied destinations, enforces destination containment, preserves additional included image/metadata paths, and checks collisions. Models retain their existing stricter recorded-file inclusion contract. |
| C03 | Upload rollback removes only positively acquired pins. Kubo checks existing pin state; Pinata requires explicit nonduplicate evidence; old third-party implementations default to unknown ownership. Unknown or pre-existing pins are retained. |
| C04 | Failed post-manifest packing restores the prior manifest and invalidates the failed cache. Packed output is staged before replacement. Rollback arms only immediately before filesystem mutation; model preflight refusals preserve every existing byte and timestamp. |
| C05 | New seals authenticate version 2 length-prefixed fields, including schema. Historical version 1 authentication bytes remain unchanged and a golden seal made with the reviewed DLL still opens. |
| C07, C08, C09 | Shared structural checks protect archive reads and direct Validator calls; undefined enums and overflowing aggregate weights produce named diagnostics. |
| C10, C11, C21 | Referenced variant deletion is refused, Kitchen async creation matches no-overwrite sync semantics, and nested async callbacks retain their archive until completion. |
| C12, C14, C15, C22 | Odds numerator/denominator association agrees, positive tiny chances retain magnitude and bound wording, collapsed saturation intervals are finite point masses, and exact-budget bucket/combination counts stay exact. |
| C13 | Streaming disposes a rendered asset when synchronous progress reporting throws before ownership transfers to the caller. |
| C16, C17, C18 | Profiler reports are invariant and explicitly mark cross-thread allocation totals unavailable; unrelated callback cancellation participates in deterministic indexed error selection. |
| C19, C20 | Legacy absence rarity survives extension without rewriting published OpenSea attributes, and wrong identity JSON kinds use the shared corruption exception. |

All 22 review findings were addressed, including the latent nested-archive lifetime issue. D01–D22 XML corrections were applied and generated API pages regenerated. S01–S07 were completed: duplicate slot maps, unused parameters/wrappers and unused reachable-layer identity removed; redundant depth copies eliminated; color-sector and opacity scans shared.

S08 structural-rule consolidation and optional footprint traversal reuse remain explicitly deferred. They would require changing or reconciling existing legality/precedence policies and are not needed for the correctness fixes.

## Additional numerical corrections

Implementation review and independent mathematical checks uncovered three related correctness issues, also addressed:

1. **Actual sampled endpoints:** the counter previously excluded a rounded upper endpoint even when the real sampler produced it. A one-ULP hue range below 30 degrees produces two DNA buckets; the old counter reported one. Counts now use actual sampled extrema without changing the sampler, RNG or DNA encoding.
2. **Legal-selection certainty:** an undercounted color layer no longer makes a total a strict lower bound when rules force that layer absent from every legal selection. Empty legal spaces and always-absent layers retain the appropriate exact result.
3. **Unsupported single-draw precision:** extreme weights can collapse cumulative intervals or imply positive branches below the 53-bit draw resolution. Subnormal draw scales can also distort even nominal equal probabilities. Reporting now conservatively returns Unknown/unavailable for these tables, while preserving archive acceptance and generation. Small joint probabilities assembled from many ordinary draws remain reportable. This gate is per draw, not a global probability floor; nominal rarity estimates remain explicitly documented as nominal.

The frozen demo did not change. Its corrected count is **8,745,000**: Chest 4,785,000 plus Strongbox 3,960,000. An independent manifest enumeration confirmed these figures. Published text, test expectation and repository guidance were corrected; no demo or frozen fixture was regenerated.

## Mathematical and regression proof

- Regression failures were observed before fixes. Additional failures exposed real Kubo empty-node response handling and preflight rollback/cache preservation; both were corrected before final verification.
- A separate exact rational oracle exhaustively checks 72 valid two-recipe configurations with weighted variants, optional layers, Require/Exclude predicates and shelved recipes. It independently checks probability normalization and distinct-selection counts without calling production rule/probability helpers for expected results.
- Focused regressions cover rounded endpoints, exact-budget unions, forced absence, impossible selections, finite shares, tiny positive display values, extreme/subnormal tables, image ownership, callback failures, malformed inputs and failure recovery.
- The final Core Release suite contains **1,750 tests**, up from the 1,622-test review baseline: 128 additional cases, all passing.

Final integration gates:

| Gate | Result |
|---|---|
| Release solution build | Zero warnings and errors |
| Ticket tests | 235 passed |
| Ticket netstandard tests | 235 passed |
| Core tests | 1,750 passed |
| Model tests | 500 passed |
| CLI tests | 331 passed |
| App tests | 1,928 passed; one Linux-only secret-tool test skipped on Windows |
| Full solution total | **4,979 passed, zero failed, one platform-specific skip** |
| API generator lint/check | Clean; 269 pages current |
| Strict MkDocs build | Passed |
| Git whitespace/scope checks | Clean; protected fixtures, archived designs, dependencies and Ticket source unchanged |

## Output preservation

Compared final Release CLI output against an independently built export of reviewed commit `95eb1e8`:

- **470 of 470 sampled files byte-identical**, no missing or extra files. Samples cover demo and frozen-fixture generation/extension, PNGs, metadata, DNA, rarity, indexes, both Menu modes and seeded renders, and model generation.
- Menu archive entry payloads were extracted before comparison; ZIP timestamps are intentionally variable and excluded from byte-identity claims.
- Direct 1-thread versus 4-thread comparisons matched all 98 demo Set files and all 38 fixture Set files.
- Final Release library output matched across `en-US`, `de-DE`, `sv-SE` and `tr-TR`: 74 files per culture. The ordinary generation sampler, draw order, DNA implementation and rendering arithmetic were unchanged.

Rendered Avalonia verification produced 427 baseline and 427 final frames. **411 matched exactly**. Eight differ only in intentionally corrected count text, six contain random temporary-path labels, and two intentionally capture thumbnails while decoding is still in flight. The loading test explicitly allows a partial decode state; those two timing-dependent frames are not claimed identical. Steady-state art frames match. No production GUI markup, theme or view-model implementation changed.

## Compatibility and verification limits

- New version 2 seals require a compatible reader. Valid legacy version 1 seals remain readable; their historical ambiguous header labels cannot acquire stronger authentication retroactively.
- Pinning services do not provide cross-client reference counting or atomic ownership of shared CIDs. Conflicting mutations against one account/node must be serialized. HTTP stubs verify provider behavior and rollback; no live pins were changed.
- Verification ran on the available Windows x64 host. Cross-culture/thread tests and both Ticket target assemblies ran; no second CPU architecture or live provider deployment was tested.
- Existing manual screenshot assets were not regenerated. Published count text and generated API pages are current; manual-image refresh belongs with the tooling/documentation capture stage.
- Frozen fixtures, archived designs, package versions and Nfty.Ticket source were unchanged.

## Evidence

Ignored local artifacts under `artifacts/audit/`:

- `core-release-final-build.log`, `core-release-final-tests.log`
- `core-api-final.log`; API lint/check also run after generation
- `core-manual-build.log`
- `core-final-output-parity.json`, `core-demo-thread-parity.json`, `core-fixture-thread-parity.json`
- `core-release-cultures.log`, `core-release-cultures/Program.cs`
- `core-final-frame-parity.json`, `core-frame-review.png`, `core-baseline-frames/`, `core-final-frames/`
- `core-execution-red.log`, `core-execution-green.log`, `core-oracle.log`, `core-math-final-green.log`
- `core-subnormal-red.log`, `core-invalid-weight-red.log`, `core-publish-preflight-green.log`

The [original report](02-core.md) preserves the review findings; the [implementation checklist](02-core-implementation.md) records approval and scope decisions.
