# Stage 5 — approved App implementation

Started 2026-11-07. The user approved the App findings, documentation corrections and bounded simplifications. Latest direction: continue orchestration/reporting and **do not merge or publish until the work is done**. Release preparation is on hold while approved App fixes proceed. Old Set compatibility is not required; correctness, deterministic output, image ownership and fixed layout remain requirements.

Baseline: `0bcea68` (includes the cross-platform canonical-LF correction). Fresh Release build: zero warnings/errors; full suite 5,231 passed, zero failures, one platform skip. The broad 470-file sample matches the accepted checkpoint with exactly 264 raw matches, 201 canonical-LF-only changes and five validated cache-length updates. H01 was unresolved at this baseline; the demonstrated application callback is now corrected with controlled before/after proof. See the final verification report; per-test isolation is preserved.

## Ownership

| Slice | Production ownership | Required behavior findings | Pass 2 ownership |
|---|---|---|---|
| Authoring | IngredientEditor partials, IngredientDetail, RecipeDetail, Explorer, BookSettings, RuleDialog, RangeAxis, CanvasAdvice, TableSort, ColorSave; corresponding views; Models/Imaging | A01–A03, A06–A11 | Authoring portions D01/D04–D10/D19; S01–S04 |
| Workflows | Shell, cook/export/upload, settings, Menu, Landing/Kitchen, wizards/help/common dialogs; corresponding views; DialogService if needed | A04–A05, A12–A13 | Workflow portions D01–D03/D11/D19; S07–S09 |
| Rise/viewer | Rise/Viewer, related view models/views including PixelCanvasHost/PixelLattice; ViewerWindowPlacement and minimal Desktop placement callers | A14–A17, A19 | D04 Rise portion, D12–D14/D20; S10 |
| Shared follow-up/integration | SettingsJson, SecretStore, shared controls/converters/composition/services, integration tests/docs | A18, A20–A21 | Remaining D01/D05/D15–D18; S05–S06/S11–S12 |

Agents must not overwrite another slice, change shared test fakes without coordination, alter engine algorithms, regenerate fixtures, upgrade packages, merge, push or publish. New regression files are preferred for isolated ownership. Agree shared write/busy lifetimes between Explorer and Shell before crossing their boundaries.

## Behavior checklist

Final status, 2026-11-08: A01-A21, D01-D20 and S01-S12 are implemented and accepted by the integration gates below. Final Release build has zero warnings/errors; 5,335 tests pass with zero failures and one Linux-only skip. App has 2,032 passing cases, 104 more than its baseline. The two review passes remain distinct; implementation and final verification are complete. User inspection is next.

Native macOS credential execution remains untested; injected status-policy regressions and source review cover its correction. The dimensions readout retains precision and fits its fixed row. H01's actual application callback fails before the correction and passes after it; the upstream reset gap remains a framework constraint, not an exception suppressed by this implementation.

- [x] A01 — Address authored rule identity after filtering/sorting
- [x] A02 — Track dirty state after Undo/Redo
- [x] A03 — Keep edits made during Save unsaved
- [x] A04 — Keep running dialogs until cancellation/work unwinds
- [x] A05 — Protect chrome/modal ownership and active borrowed graphs
- [x] A06 — Export the selected variant
- [x] A07 — Reconcile references and invalidate retained composites
- [x] A08 — Own and release loose-save prospective/current images
- [x] A09 — Dispose replaced/import-failure subtrees correctly
- [x] A10 — Notify Ingredient command availability on lock changes
- [x] A11 — Guard overlapping structural commits and close lifetime
- [x] A12 — Write models-checkbox changes through to export options
- [x] A13 — Reject stale endpoint-check results
- [x] A14 — Own live viewer image snapshots across picker awaits
- [x] A15 — Refresh live model controls and layout fallback
- [x] A16 — Apply inspector zoom scale/pan coherently
- [x] A17 — Match canonical voxel bounds and display precision
- [x] A18 — Canonicalize case-insensitive settings-key updates
- [x] A19 — Account for each screen DPI during placement
- [x] A20 — Report failed secret-tool deletion without clearing the hint
- [x] A21 — Update macOS credentials without delete-before-add loss

## Documentation and simplification checklist

- [x] D01 — complete all assigned parts from the review.
- [x] D02 — complete all assigned parts from the review.
- [x] D03 — complete all assigned parts from the review.
- [x] D04 — complete all assigned parts from the review.
- [x] D05 — complete all assigned parts from the review.
- [x] D06 — complete all assigned parts from the review.
- [x] D07 — complete all assigned parts from the review.
- [x] D08 — complete all assigned parts from the review.
- [x] D09 — complete all assigned parts from the review.
- [x] D10 — complete all assigned parts from the review.
- [x] D11 — complete all assigned parts from the review.
- [x] D12 — complete all assigned parts from the review.
- [x] D13 — complete all assigned parts from the review.
- [x] D14 — complete all assigned parts from the review.
- [x] D15 — complete all assigned parts from the review.
- [x] D16 — complete all assigned parts from the review.
- [x] D17 — complete all assigned parts from the review.
- [x] D18 — complete all assigned parts from the review.
- [x] D19 — complete all assigned parts from the review.
- [x] D20 — complete all assigned parts from the review.
- [x] S01 — implement with preservation proof, or record a justified deferral.
- [x] S02 — implement with preservation proof, or record a justified deferral.
- [x] S03 — implement with preservation proof, or record a justified deferral.
- [x] S04 — implement with preservation proof, or record a justified deferral.
- [x] S05 — implement with preservation proof, or record a justified deferral.
- [x] S06 — implement with preservation proof, or record a justified deferral.
- [x] S07 — implement with preservation proof, or record a justified deferral.
- [x] S08 — implement with preservation proof, or record a justified deferral.
- [x] S09 — implement with preservation proof, or record a justified deferral.
- [x] S10 — implement with preservation proof, or record a justified deferral.
- [x] S11 — implement with preservation proof, or record a justified deferral.
- [x] S12 — implement with preservation proof, or record a justified deferral.

## Integration gates

- [x] Independently review each slice against findings and preservation contracts.
- [x] Verify controlled async, busy, dirty-state and ownership regressions; preserve allocation baselines and actual bound-control behavior.
- [x] Verify full Release solution at zero warnings/errors; investigate every failure without suppressions or hidden retries.
- [x] Verify canonical generated output, locale/thread boundaries and actual model/PNG payloads.
- [x] Capture/render affected views in both themes and required window sizes; inspect and classify differences and fixed-layout comparisons.
- [x] Complete documentation/API/manual checks where affected; preserve protected files.
- [x] Update report/tracker/handoff with evidence, remaining limitations and deferred work; stop for the user's verification.

## Decisions and progress

- Canonical LF is a justified output correction: JSON meaning, PNGs, models and DNA match, while Windows/Linux textual payloads become identical. The user explicitly waived old-format compatibility.
- H01 will not be hidden by retries, weakened test isolation or reflection into framework globals. A controlled probe reproduces the exact pre-test failure; a green repeat alone is not a fix.
- Final release packages must be rebuilt after App work. Existing Windows/Linux packages belong to earlier candidates and must not be published as the final implementation.

### Final integration evidence

- All behavior and documentation groups were reviewed, including four additional authoring/workflow integration corrections and the small-voxel label fix.
- S05 retains row identity caches/order/fallbacks; S06 retains pixel-identical share-bar frames and passes actual-view width checks; S11 retains deferred candidate ordering/duplicates. No unmeasured speed claim is made.
- Session-owned write leases cover graph derivation through adoption/refresh, with final close rechecks after awaits.
- H01 callbacks use captured owner dispatchers; unpublished images/books are disposed without draining obsolete queues. Mesh completion covers both disposal interleavings and awaits snapshot release.
- Generated output: 470/470 exact; thread parity: 98/98 and 38/38. Frames: 431, 417 exact, all 14 differences classified. API lint/check, strict manual and protected-file checks pass.
- Complete evidence, command results and native-platform limits: [final verification](05-app-verification.md). Local tag: `audit-app-verified-2026-11-08`. No merge, push or publication; stop before Desktop.
