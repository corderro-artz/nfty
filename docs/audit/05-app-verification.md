# Stage 5 — App verification

Date: 2026-11-08. Baseline: `0bcea68`. Scope: approved A01–A21, D01–D20 and S01–S12, plus defects found while integrating those fixes. Work remains isolated on `codex/ticket-audit-fixes`; no merge, push or publication.

The final code checkpoint passes the gates below and is ready for user review and a fast-forward merge. The user directed a stop before Desktop; no merge or publication is performed. Local checkpoint tag: `audit-app-verified-2026-11-08`.

## Implemented behavior

All 21 reviewed behavior findings have implementation source and focused regression coverage. Rule operations retain authored identity; editor history and save revisions preserve unsaved edits; structural writers share one session lease; selected previews and live viewer saves own snapshots across picker awaits; imported/replaced images have explicit adoption and orphan disposal. Running modal operations remain alive through cancellation and cleanup, chrome respects borrowed document lifetimes, and checkbox/endpoint updates reach their actual options and current connection state.

Live model availability, layout fallback, anchored inspector zoom, canonical voxel bounds and small-voxel dimensions are corrected. Viewer placement converts each screen's DIP dimensions to physical pixels without changing saved positions. Settings updates remove case aliases; failed Linux key deletion retains the hint and reports a sanitized error; macOS replacement updates before adding an absent item.

Independent integration review also found and corrected:

- Recipe-local ID collisions could redirect deletion after confirmation. Resolution now includes domain kind and owning recipe; written archives are reopened to verify the intended removal and untouched image pixels.
- A second writer could start after a close await. Window/document close rechecks current writers, modal state and relevant navigation before disposing a borrowed graph.
- Cook/Menu Close availability used mismatched command/operation lifetimes. It now follows the accepted operation's CTS, with explicit availability notifications and direct-execution guards.
- Upload cancellation had lost pin-retention guidance. The message now states that pre-existing content stays pinned, consistent with Core's ownership-limited best-effort cleanup of newly acquired pins.
- Supported voxel sizes below 0.01 could display zero dimensions. The label retains five decimal places, including quarter-voxel steps; exported mesh arithmetic is unchanged. The readout wraps into two short lines inside the existing 26px row when needed, with a full-value tooltip. Both-theme checks verify every number fits without moving the row.

## H01: application callback correction

An actual gallery thumbnail callback was forced into the framework's dispatcher reset-to-compositor gap. The retained old App binary fails with the exact recorded `Dispatcher.VerifyAccess` / `DefaultRenderLoop.Add` / `EnsureIsolatedApplication` stack; the next application's delegate never enters. Both live-owner and disposed-owner cases reproduce it. The corrected App passes the same two controlled interleavings.

Workers retain their originating dispatcher before leaving the UI thread. Pending thumbnail/book ownership lets Dispose free results even if an obsolete UI queue never drains. Mesh completion covers disposal before worker completion and disposal after a result is queued; completion still waits until worker-owned snapshots are freed.

The tracked standalone regression is [headless-callback-probe](../../tools/verification/headless-callback-probe/README.md). It uses public framework setup hooks in a separate process, preserving per-test isolation. No dependency upgrade, private reflection, retry or exception suppression was introduced. The exact callback behind the historical intermittent failure was not recorded; the demonstrated application mechanism is corrected. Avalonia's upstream reset gap remains a framework constraint for future background code.

## XML and simplification

D01–D20 were reviewed against the implemented contracts: ownership, cancellation, coordinate units, source/save responsibilities, schema floor, page counts, authored/display identity, legal counts and export-size precision. Duplicate summaries on partial types and misattached documentation are consolidated. Compiled App XML has no members with duplicate summary elements. The remaining syntax-scan omissions belong to private/internal helper members, not undocumented public surface; no filler comments were added.

S01–S12 retain the approved boundaries: lazy legacy colorway samples, cached stable hero ordering, one authored rule scan, local prospective adoption, standard Chunk row grouping with existing identity caches, shared measured-width bindings, operation-owned cancellation, export-plan reuse, one stable mix projection, readonly viewer preview, shared deferred book candidates and concise current contracts. No unmeasured performance improvement is claimed. Broader engine/traversal refactors remain deferred.

## Verification

| Gate | Result |
|---|---|
| Release solution build | Zero warnings and errors |
| Full Release solution tests | 5,335 passed, zero failed, one Linux-only platform skip; App 2,032 passed |
| Controlled H01 callback probes | Live and disposed-owner cases pass; old binary fails the same interleaving |
| Focused authoring/workflow integration | 120 passed, zero failures |
| Focused H01/lifetime integration | 80 passed, zero failures |
| Generated payload preservation | 470/470 byte-identical against the canonical-LF checkpoint |
| Thread parity | Demo 98/98 and fixture 38/38 files match at one versus four threads |
| Rendered GUI frames | 431 captured: 417 exact; all 14 changed frames inspected and classified below |
| API documentation | Lint clean; 269 generated pages up to date |
| Manual | Strict build passed |
| Protected files/dependencies | Frozen fixtures, archived designs and package pins unchanged |

The focused selections overlap and are not added to the full-suite count. Output comparison includes generated/extended Sets, metadata, extracted Menu payloads, seeded Menu renders and GLB models. ZIP timestamps are packaging metadata; extracted payloads are compared. Engine/library/CLI sources are unchanged from `0bcea68`.

Detailed logs, assembly hashes, pixel inventories and initial failing regression evidence remain under ignored `artifacts/audit/`. Test setup/analyzer errors were corrected, the invalid nested-headless diagnostic was replaced with the standalone probe, and the initially overstrict bar assertions were corrected to distinguish exact bound widths from pixel-snapped layout. These earlier failures remain recorded; they are not counted as successful runs.

## Rendered-frame classification

All 431 baseline names are present, with no extra or missing frames. The 14 changed frames contain:

- Six temporary-path label changes (upload results and settings data).
- Two marketplace size labels, 4 KB to 3 KB, from canonical LF. A controlled run of the old App with the current Core reproduces both final frames exactly; the old Core reproduces the earlier labels. This isolates the accepted newline correction from App changes.
- Four Rise export dimension-label alignments resulting from the explicit two-line leading. The values are unchanged and the numeric row retains its existing 26px bounds. Refreshed minimum/0.123/maximum captures show the full values in both themes.
- One intentionally in-flight Set-thumbnail loading frame. Settled browser captures remain exact.
- One sheet-grid frame with ten RGB pixels differing by at most one channel level at control edges. An unchanged-binary focused capture reproduces the final frame exactly. This is a small reproducible raster difference, not silently classified as random noise; no displaced element or changed value was observed. Its pixel coordinates and before/after values are recorded in app-final-grid-pixels.json.

Ingredient share bars and Recipe mix wizard frames remain pixel-identical after S06. Four actual-view geometry cases also verify exact bound percentages, pixel-snapped widths, both themes and live weight changes. New Rise/viewer captures were opened and inspected in addition to the full baseline inventory. The small-voxel clipping regression failed twice before the layout fix; the related 38-case selection passes afterward. The initial full suite passed before that last markup change; the complete suite was then repeated and passed after it.

Final evidence includes app-final-build.log, app-final-tests.log, app-final-output-parity.json, app-final-frame-parity.json, app-final-size-label-control.json, app-final-grid-unchanged-control.json, app-final-api-check.log and app-final-manual.log. The controlled raster comparison initially expected baseline equality; its failed expectation was investigated and replaced by the accurate finding that the unchanged final binary reproduces the final frame. No product test was suppressed or weakened.

## Limits and next boundary

Verification is finite and primarily Windows x64. Native macOS Keychain replacement, real Linux keyring failure, native mixed-DPI monitor movement, live cloud upload, another CPU architecture and an external model viewer were not exercised. Managed status-policy tests and pure DPI calculations do not claim native platform execution. Earlier cross-platform canonical output evidence remains applicable to unchanged engine sources.

Earlier release packages predate the App changes and must not be published as this checkpoint. Rebuilding assets and publishing are separate from code merge readiness. Two old untracked audit notes in the main checkout were copied to `M:/Repositories/nfty-audit-notes-before-merge-20261108/docs/audit`, SHA-256 verified, then removed from their obstructing untracked locations; tracked main files were unchanged.

Stop after the verified local commit/tag. Desktop, Ticket.Sample, Embedding.Sample and tooling/CI/integration reviews have not started. The two Desktop DPI caller edits support App A19 and do not constitute a Desktop audit.
