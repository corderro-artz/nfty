# CLI implementation and verification

Approved on 2026-11-06: implement the Stage 4 findings, XML corrections and bounded simplifications in the existing isolated worktree, then stop before App review. Baseline: `95775d6`, local tag `audit-model-verified-2026-11-06`. Implementation and verification are complete; [final evidence](04-cli-verification.md) is ready for the user's verification. No merge or push; changes remain uncommitted.

## Task checklist

- [x] Delegate authoring, publishing and model-I/O implementation to `gpt-6.1-sol` owners; retain integration and final verification in the main agent.
- [x] Observe failing regressions for malformed parsing, ignored/contradictory switches, secret echo, model replacement, authoring structure, invariant confirmation, occupied generation output and extension overflow.
- [x] L01: preserve lazy-conversion parser errors across all validators and put parsing inside the process error boundary.
- [x] L02: route complete model groups through the existing transactional folder sink; preserve output casing, nested/duplicate inputs and backup recovery.
- [x] L03: refuse existing Set-owned output before rolling, including numbered model collisions and the selected packed archive; allow unrelated Kitchen files/models/archives.
- [x] L04: compare an absolute extension target before subtraction, preserving already-met-target no-ops including int.MinValue.
- [x] L05–L07: guard effective sealing/key dependencies, redact unrecognized credential sources and refuse contradictory content flags.
- [x] L08/L09: use the shared manifest structural gate and invariant confirmation formatting.
- [x] D01–D13: correct scoped public/private XML contracts, concise intent summaries and ownership/error documentation.
- [x] S01–S08: complete bounded simplifications, retaining historical authoring serialization order as described below.
- [x] Resolve all confirmed integrated-review findings: eight missing-text parser regressions observed RED, fixed and GREEN; repeat the 5,400-case parser sweep with zero uncaught failures.
- [x] Verify 470/470 normal Set/Menu/model files and seven nested authoring payloads per edited operation against baseline; verify 98/38-file direct thread parity and 76 files in each of four cultures.
- [x] Regenerate/check 269 API pages and build the manual strictly.
- [x] Verify final Release solution build at zero warnings/errors; 5,228 tests passed, zero failed, one platform-specific skip. CLI: 406 passed; Model: 674 passed.
- [x] Inspect 431 rendered frames: 421 exact matches; six temporary-path labels; two loading states; five one-level UI-border pixels across two frames, investigated with unchanged-binary repeats and historical captures.
- [x] Record [exact evidence, limitations and intentional behavior changes](04-cli-verification.md), update tracker and stop before App review.

## Preservation decisions

- The shared numeric guard catches only InvalidOperationException accompanied by the option's actual conversion errors. InvalidOperationException from unrelated implementation faults is not suppressed. Reading the pre-conversion Errors collection alone is insufficient because System.CommandLine performs conversion lazily.
- S02 retains UpsertRecipe's historical weight-key insertion order, replacing only its quadratic recipe restoration with the original Recipes list. Directly switching to SetRecipeWeight would change authoring manifest payload bytes. S03 reuses EditIdentity as proposed.
- ModelBatch.WriteFolder keeps its public signature and uses the existing rollback sink. Empty groups still create the directory; safe nested paths and last-write-wins duplicate payloads remain supported. Mesh/RNG/color/encoding algorithms are unchanged.
- The CLI receives friend access to Core solely to reuse the existing structural boundary and output layout rules. Packed archive naming moves unchanged into one internal SetLayout helper, shared by preflight and packing. No new JSON parser or public framework is added.
- Occupied generation output is rejected rather than overwritten or deleted. This changes unsafe input behavior deliberately; extension remains the explicit path for growing an existing Set.
- No dependency upgrades, fixture regeneration, archived-design edits, production App layout changes or live provider calls.
