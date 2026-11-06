# Staged codebase review

Current baseline: `5dece661785a75990576f4016eb796558ba75fd4` (`origin/main`, fetched 2026-11-05).

Current status: **Ticket, Core, Model and CLI accepted; App review active.** The user accepted CLI verification and approved proceeding on 2026-11-06. CLI checkpoint: local tag `audit-cli-verified-2026-11-06`. App work is review-only until findings are approved. Nothing has been merged or pushed.

The initial review used stale local `main` (`fea71d8`, 115 commits behind). The user identified the missing seed reroll control during verification. The isolated branch was rebased onto current `origin/main`; fixes were adapted and all verification repeated. Initial results are historical evidence only, not proof of compatibility with the current baseline.

## Rules

- Review one project at a time, in the order below. Review its tests alongside it.
- Pass 1: correctness, syntax/build, practices, workflows, resource ownership, determinism, and performance.
- Pass 2: XML documentation completeness/correctness and opportunities to simplify implementation.
- Record evidence, severity, proposed remedy, compatibility constraints, and verification for each finding.
- Stop after each project's report. No implementation without the user's explicit approval.
- After approval, dispatch an implementation agent. Use a cheaper model for straightforward work; reserve Astra for complex work. Every agent uses caveman ultra mode in chat.
- Deferred items in BACKLOG.md remain decisions, not newly discovered defects.

## Total progress

| Stage | Project/codebase | Scope and companion tests | Pass 1 | Pass 2 | Approval/fixes |
|---|---|---|---|---|---|
| 1 | Nfty.Ticket | Shared model, RNG, colors, DNA, Menu reader, tickets; both Ticket test projects and relevant Core integration tests | Complete | Complete | Approved; local tag audit-ticket-verified-2026-11-05 |
| 2 | Nfty.Core | Archives, validation, editing, imaging, generation, statistics, publishing; Nfty.Core.Tests | Complete | Complete | Fixed and verified; local tag audit-core-verified-2026-11-05; [evidence](02-core-verification.md) |
| 3 | Nfty.Model | Rise, meshes, model formats/readers, batch export; Nfty.Model.Tests | Complete | Complete | Fixed and verified; local tag audit-model-verified-2026-11-06; [evidence](03-model-verification.md) |
| 4 | Nfty.Cli | Command wiring, authoring, options, errors; Nfty.Cli.Tests | Complete | Complete | Accepted; local tag audit-cli-verified-2026-11-06; [evidence](04-cli-verification.md) |
| 5 | Nfty.App | Avalonia views, controls, view models, services, themes, viewer; Nfty.App.Tests | Active | Pending | Review authorized; fixes require approval |
| 6 | Nfty.Desktop | Desktop startup, windows, platform integration; relevant Nfty.App.Tests | Pending | Pending | Not authorized |
| 7 | Nfty.Ticket.Sample | Package-free Ticket embedding example and its runtime contract | Pending | Pending | Not authorized |
| 8 | Nfty.Embedding.Sample | Core/Model embedding example and its runtime contract | Pending | Pending | Not authorized |
| 9 | Tooling and integration | tools/, CI, shared build/package configuration, release and documentation generators; cross-project consistency | Pending | Pending | Not authorized |

Current solution: six production projects, two sample projects, and six test projects. `Nfty.Ticket.NetStandard.Tests` exercises the same Ticket test sources against the netstandard2.1 assembly; it is included in Stage 1. Test projects are reviewed with their production owner. Stage 9 covers executable code outside those projects; XML review applies where applicable, with equivalent documentation review for scripts.

## Completed review: Nfty.Cli

- [x] Inventory 12 production C# files (3,885 lines), project wiring and 28 companion test source files (5,726 lines).
- [x] Split read-only review ownership across main/model commands, authoring/sheets and publishing/error boundaries.
- [x] Establish Release baseline at `95775d6`: solution build has zero warnings/errors; CLI suite has 331 passed, no failures/skips.
- [x] Pass 1: inspect every command path and relevant tests; reproduce confirmed failures without editing production code or tests.
- [x] Independently confirm model-pair recovery, existing-Set generation/uniqueness and extension-overflow findings against saved executable probes.
- [x] Pass 2: independently inspect XML completeness/correctness and bounded simplification opportunities.
- [x] Consolidate nine findings (seven P2, two P3), 13 grouped XML items and eight bounded simplifications into the [Stage 4 report](04-cli.md).
- [x] Update tracker and stop for user review; no CLI implementation without approval.
- [x] After approval: delegate and implement L01–L09, D01–D13 and S01–S08; preserve historical authoring weight-key order while removing quadratic restoration.
- [x] Resolve additional missing-text parser cases discovered by independent review; 5,400-case sweep now has zero uncaught exceptions.
- [x] Verify Release solution: zero warnings/errors; 5,228 passed, zero failed, one platform-specific skip. CLI: 406 passed (75 additional); Model: 674 passed (five additional).
- [x] Verify 470/470 sampled files, seven nested authoring payloads per operation, 98/38-file thread parity and 76 files in each of four cultures.
- [x] Inspect 431 captured frames: 421 exact matches; variable temporary labels/loading and five one-level border pixels classified, with unchanged-binary repeats and historical comparisons recorded.
- [x] Regenerate/check 269 API pages, build the manual strictly and confirm protected files unchanged.
- [x] Publish [final evidence and limits](04-cli-verification.md), complete the [implementation checklist](04-cli-implementation.md) and stop before App review.

Stage 4 result: all nine findings, 13 grouped XML items and eight bounded simplifications addressed. Model pairs and existing Sets are protected; overflow and parser failures are corrected; secret/sealing boundaries are explicit. Ordinary sampled output and authoring payloads remain byte-identical. Finite Windows x64 verification, culture/thread proof, frame differences and recovery limits are documented. The user accepted CLI verification and approved the next review on 2026-11-06. CLI is locally committed/tagged in the isolated worktree. App review may proceed; no App fixes, merge or push are authorized.

## Completed review: Nfty.Model

- [x] Inventory 29 production files: 12 geometry/mesh files, nine model format files, four rendering files and four options/batch/integration files.
- [x] Assign independent review scopes; retain math, determinism and ownership as primary acceptance criteria.
- [x] Pass 1: inspect every implementation and relevant tests/callers; reproduce actionable defects.
- [x] Establish the current Release Model test baseline: 500 passed, no failures or skips.
- [x] Pass 2: separately review XML contracts and simplification opportunities in every scope.
- [x] Consolidate confirmed findings, deferred proposals, compatibility constraints and evidence into the [Stage 3 report](03-model.md): 11 findings, 17 XML items, seven bounded simplification proposals and two optional measurement candidates.
- [x] Stop for the user's review; no Model fixes before approval.

- [x] After approval: delegate implementation of M01–M11, D01–D17 and S01–S07; retain M11's distance cap with an accurate best-effort contract.
- [x] Independently verify integrated math and error boundaries; correct related T-junction/UV rounding cases and prove backup retention after secondary rollback failure.
- [x] Verify final Release solution: 5,148 passed, zero failed, one platform-specific skip; build at zero warnings/errors. Model: 669 passed, 169 additional cases.
- [x] Verify 470/470 sampled files, 60 mesh/format goldens, direct thread parity and four-culture writer parity.
- [x] Inspect all changed regions across 431 captured frames: 356 exact matches; 67 expected preview corrections; six random-path labels; two in-flight loading states.
- [x] Regenerate/check 269 API pages, build the manual strictly and confirm protected files unchanged.
- [x] Publish the [final verification report](03-model-verification.md), complete the [implementation checklist](03-model-implementation.md) and stop before CLI.

Stage 3 result: all 11 findings, 17 XML items and seven bounded simplifications addressed. Narrow-aspect framing retains its existing distance cap with an accurate contract; two unmeasured optimizations remain deferred. Ordinary exported output is byte-identical in sampled comparisons; preview coverage intentionally improves. Verification ran on Windows x64; another CPU architecture and an external model viewer were not exercised.

The implementation and verification phases are complete. The user accepted verification and approved proceeding on 2026-11-06. Model is locally committed/tagged before the CLI review; no merge or push.

## Completed review: Nfty.Core

- [x] Inventory 117 production C# files across archives, editing, imaging, generation, statistics, publishing, output, and shared helpers.
- [x] Read repository contracts and distinguish deliberate backlog decisions.
- [x] Run the current Core test baseline: 1,622 passed, no failures or skips.
- [x] Pass 1: inspect production code and related tests; reproduce boundary failures in ignored local probe projects.
- [x] Pass 2: finish XML contract review and bounded simplification proposals.
- [x] Consolidate findings, compatibility constraints, and regression checks into the [Stage 2 report](02-core.md): 22 findings, 22 XML corrections, eight bounded simplification proposals and one optional traversal candidate.
- [x] Stop for the user's review and fix approval.
- [x] After approval: delegate implementation, verify output and behavior, then report.
- [x] Fix all C01–C22 findings, apply D01–D22 XML corrections and complete S01–S07; explicitly defer S08 and optional traversal consolidation.
- [x] Correct additional sampled-endpoint counts, legal-selection certainty and unsupported weight-precision reporting.
- [x] Verify Release solution: 4,979 tests passed, zero failures, one platform-specific skip; build at zero warnings/errors.
- [x] Verify 470/470 baseline sample files, thread parity, four-culture parity and 427 rendered frames with expected differences classified.
- [x] Regenerate/check 269 API pages, build the manual strictly, and confirm protected files unchanged.
- [x] Publish the [final Core verification report](02-core-verification.md), update all completed tasks and stop for instructions.

The review-only phase ended when the user approved implementation. Fixes and regression tests are complete; probes and logs remain in ignored `artifacts/audit/` or temporary directories. No live pinning requests were made.

Stage 2 result: all 22 review findings addressed, plus three related numerical corrections. Core now has 1,750 passing tests (128 additional cases); the full Release solution has 4,979 passing tests and one platform-specific skip. Ordinary generation output remains byte-identical in sampled comparisons. Demo counts intentionally correct to 8,745,000; new seals use versioned unambiguous authentication and retain legacy readability. Broader rule/traversal refactors remain deferred. Cross-architecture and live-provider verification were not performed; see the final report for exact limits.

## Completed review: Nfty.Ticket

- [x] Inventory source, tests, dependencies, and repository rules.
- [x] Read all production source files and all six test files.
- [x] Pass 1: trace callers, inspect boundary cases, reproduce actionable defects.
- [x] Establish build/test results; distinguish skipped execution from passing tests.
- [x] Pass 2: inspect XML contracts and simplification opportunities separately.
- [x] Publish the stage report with source references and evidence: [Stage 1](01-ticket.md).
- [x] User reviews findings and approves a specific fix scope.
- [x] Dispatch approved implementation work; verify regressions and compatibility.
- [x] Record implementation results and deferred proposals before starting the next project: [verification report](01-ticket-verification.md).

Only review artifacts may be changed before approval. No production code, committed tests, fixtures, archived designs, or dependencies are to be changed.

Stage 1 result: eight reproduced correctness defects; eight documentation items; five simplification/performance proposals. Implementation approved on 2026-11-04, with an isolated worktree and no merge or push until standards and output/function compatibility are verified.

Worktree: `C:/Users/Corde/.codex/worktrees/ticket-audit-fixes/nfty`, branch `codex/ticket-audit-fixes`. Three `gpt-6.1-sol` implementation agents own Menu boundaries, numeric/depth APIs, and DNA validation/documentation respectively. S04/S05 remain measurement-dependent proposals, not automatic optimization work.

The initial old-baseline verification is retained in ignored `artifacts/audit/` for traceability. Current-baseline evidence uses the `latest-` prefix. Samples cover generation/extension at 1 and 4 threads, both Menu modes, seeded Menu rendering, and model generation. Current upstream already mitigated/suppressed the pinned ImageSharp advisories; the latest full build has zero warnings and errors.

Latest-source review also includes the four added Ticket files (`Compat/Guard`, `Compat/Hashing`, `Compat/NetStandardShims`, `Menu/MenuJson`). Existing upstream parser fixes supersede the original report's root/null-element examples and obsolete System.Text.Json details. The remaining structural/required-member validation is implemented in the shared package-free parser plus typed validation, not a second JSON engine. Public XML changes regenerate the committed API pages, as the current briefing requires.

Final: zero-warning build; 4,851 tests passed, one platform-specific skip; 470 sampled files byte-identical; rendered output verified with random-path labels accounted for and settled 3D captures identical. Native seed reroll checked. User approved the local commit and tag `audit-ticket-verified-2026-11-05`; no merge or push. S04/S05 deferred pending measurements; one pre-existing App cleanup flake recorded for Stage 5.
