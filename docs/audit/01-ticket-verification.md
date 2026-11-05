# Stage 1 implementation and verification

Verified 2026-11-05. Changes remain in `codex/ticket-audit-fixes`, unmerged and unpushed.

## Correct baseline

The initial review accidentally used stale local `main` (`fea71d8`). The user identified the missing seed reroll die. After fetching, `origin/main` was 115 commits ahead, at `5dece661785a75990576f4016eb796558ba75fd4`. The same isolated branch was rebased onto that revision, preserving the fixes in a backup stash and resolving compatibility changes. Main was not modified.

All readiness evidence below uses this current revision. The old build's tests, advisory warnings and output comparisons are historical only. Latest upstream already addressed the pinned ImageSharp advisories, added the seed die, and introduced package-free Ticket support for netstandard2.1.

Worktree: `C:/Users/Corde/.codex/worktrees/ticket-audit-fixes/nfty`.

## Result

| Finding | Implemented behavior |
|---|---|
| T01 | Reject `=` in ingredient IDs at validation/generation, preserving the historical DNA encoding. Variant `=` remains legal. Reject malformed UTF-16 IDs without normalizing valid Unicode. Old CookBook archives remain readable for inspection and repair. |
| T02 | Plate tickets in the Menu's authoritative layer order. Reject duplicate or unknown layers/variants; validate the recipe even for empty tickets. |
| T03 | Require mandatory fields during shared MenuJson binding, preserve nullable/optional defaults, and validate nested IDs, weights, references, colorization and geometry before building lookup caches. Retain both target frameworks and the existing package-free reader. |
| T04 | Dispose the owned stream when ZIP construction or manifest loading fails. |
| T05 | Reject nonfinite numeric color components before RGB conversion. |
| T06 | Check each depth-map coordinate before flattening it into an array index. |
| T07 | Use overflow-safe optional-weight arithmetic only when the original intermediate overflows; retain ordinary arithmetic and seeded results. |
| T08 | At rounded sampling endpoints, choose a positive interval rather than a zero-weight tail, including color entries and absent-only cases. |

XML corrections cover the HSL rendering formula, Static-layer RNG/uniqueness wording, schema ranges, complete ticket wire format, ownership/failure/concurrency contracts, floating-point endpoints and inaccurate test rationale. Shared weighted scanning and archive byte reading remove duplicated implementation. API pages were regenerated from XML, not edited by hand.

S04/S05 remain profiling-dependent proposals; no speculative caching or hashing rewrite was introduced. No new canvas ceiling was imposed: a tighter Menu-only limit would reject existing writer output.

Three Sol implementation agents handled Menu boundaries, numeric/depth APIs and DNA/docs. Peer review checked numeric and DNA changes; an independent Menu review caught two issues before completion. After updating the baseline, the new compatibility helpers, compiler shims and MenuJson reader received both correctness and documentation review. Some original T03 examples were already fixed upstream; those were not reimplemented.

## Tests and build

`dotnet build nfty.sln --no-restore`: **zero warnings, zero errors**.

`dotnet test nfty.sln --no-build --no-restore`:

| Project | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Nfty.Ticket.Tests | 235 | 0 | 0 |
| Nfty.Ticket.NetStandard.Tests | 235 | 0 | 0 |
| Nfty.Core.Tests | 1,622 | 0 | 0 |
| Nfty.Model.Tests | 500 | 0 | 0 |
| Nfty.Cli.Tests | 331 | 0 | 0 |
| Nfty.App.Tests | 1,928 | 0 | 1 |
| Total | **4,851** | **0** | **1** |

The skip is `SecretToolStoreTests.The_secret_goes_on_stdin_and_never_into_the_arguments`, a Linux-specific integration check on this Windows host. Both sample projects also build in the solution.

Regression tests were observed failing before their fixes. Latest upstream added a parser fixture with a real variant but no art cell; its fixture now contains the required cell, preserving the test's purpose and the reader's invariant. Frozen roll/hash tests and older archive fixtures pass on the relevant targets.

The unmodified latest baseline had one App test cleanup failure in `RiseExportTests.A_sealed_set_refuses_in_words_and_never_opens_the_picker`: Avalonia reported access from a different thread. Its isolated baseline rerun passed, and the candidate's full App suite passed. This pre-existing harness flake is recorded for the App review; it was not fixed as part of Ticket work.

The first capture-harness rebuild encountered a running test executable's file lock. It was rerun after the runner exited; the final build above is clean. No failed build is counted as a successful check.

## Output and rendered-frame compatibility

Original latest CLI binaries were built from a clean export of `5dece66`, independently of the candidate. [output-sample.ps1](output-sample.ps1) runs both against identical books/seeds, and [compare-output.ps1](compare-output.ps1) compares relative membership and SHA-256:

- Demo and historical fixture generation, then extension, at one and four threads.
- Atlas and loose Menus, their manifests, art and depth entries.
- Seeded Menu rendering and generated models.
- **470 of 470 files byte-identical**, with no missing or extra files. This includes images, metadata, DNA, rarity, Set indexes and model bytes.

Menu ZIP payloads are compared after extraction; raw ZIP timestamps are not treated as deterministic content. No fixture archive or package version was changed.

Both builds passed all **37 VisualCapture tests**, producing **427 frames** each. Initial comparison found 419 exact matches. Six differences were random temporary-folder names in upload-result and settings labels; repeat baseline captures changed those labels too. The remaining two were 3D views captured before queued mesh work settled.

The Rise capture helper now waits on the existing `PendingMeshBuild` observation barrier after arranging a gesture and before repainting. The identical test-only barrier was applied to the exported baseline for comparison. Across both themes and normal/minimum sizes, all **eight settled 3D frames matched byte-for-byte**. The six random-path labels are the only remaining visual differences; no production GUI code changed.

Native Windows smoke check opened the latest demo and Cook Set dialog. The seed die was present and changed the seed from `14f869dc` to `339a26c8`. The test app was closed afterward. This checks native startup/navigation and the user's specific version concern; it does not claim an exhaustive manual GUI review.

Public API generator checks: **268 pages current**, documentation lint clean. `git diff --check` passes. `tests/fixtures/`, archived design documents and dependency versions are unchanged.

## Evidence and next stage

Detailed logs and local comparison artifacts are retained under ignored `artifacts/audit/`:

- `latest-final-build.log`, `latest-final-tests.log`
- `latest-output-parity.json`
- `latest-frame-parity.json`, `latest-frame-control.json`, `latest-settled-parity.json`
- `latest-baseline-capture.log`, `latest-candidate-capture.log`
- `api-lint.log`, `api-check.log`, `baseline-cleanup-recheck.log`

Stage 1 implementation was approved for local commit and tag `audit-ticket-verified-2026-11-05`. No merge or push is authorized. Stage 2, Nfty.Core, follows separately; no broad Core audit is claimed by the targeted validation changes here.
