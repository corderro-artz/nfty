# Stage 8 — Nfty.Embedding.Sample

Baseline: 5b208dd. Both review passes completed; fixes authorized with the remaining audit/release work.

## Pass 1: behavior

- ES01: A valid ruled Menu could reject the fixed first draw and make the entire sample fail after writing its cooked images. It now prepares once and retries the same RNG stream up to 1,000 times. A constructed valid four-variant book forbids exactly the first seed-7 selection while retaining three legal unique outputs, reproducing the original failure deterministically.
- ES02: Output numbering and byte-count reports used current culture. Explicit invariant formatting preserves filenames and console reports across locales. The regression compares both complete reports and SHA-256 payloads from separate English/French output folders.

Reviewed generated-asset ownership, LoadedCookBook lifetime, Menu/render disposal, StackRise/face ownership, model-null handling, GLB writing and output paths. Three-asset generation remains an explicit example requirement, now stated in the manual; there is no automatic uniqueness-count or source-format change. Like a normal simple example, it does not promise whole-directory transactional output or silently recover from unreadable input.

## Pass 2: documentation and simplification

There are no public API declarations needing XML in this top-level program. All included snippets and inline ownership/math contracts were checked against current library APIs. Corrected the cross-sample rendering claim, documented the three-unique-asset prerequisite and explained bounded rule retries. Reused PreparedMenu within the explicit loop; avoided adding a duplicate generator or sample framework. Core/Model references and the transitive imaging dependency remain unchanged.

## Verification

The initial five-case sample selection had four failures and one pass, including the rejected-first-roll case and culture-dependent byte-count report. Final focused result: five passed; final CLI suite: 411 passed. See [integration verification](10-final-verification.md). Tests invoke the actual compiled sample entry point, reopen real temporary Menus and compare emitted PNG/GLB bytes. No live network, credentials, real AppData or frozen fixture regeneration is involved.
