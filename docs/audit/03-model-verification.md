# Stage 3 — Model fixes and verification

Status: complete and verified on 2026-11-05. The user accepted verification and authorized proceeding on 2026-11-06; the local checkpoint is tagged `audit-model-verified-2026-11-06`. Reviewed baseline: `24f2ed6`, local tag `audit-core-verified-2026-11-05`. All work remains in the isolated `codex/ticket-audit-fixes` worktree. Nothing has been merged or pushed.

## Completed implementation

| Findings | Final behavior |
|---|---|
| M01 | Canonical near-plane intersections and consistent edge coverage close opaque-face cracks. Shared axis lines also cover greedy-mesh T-junctions; texture sampling remains within the original triangle's half-open UV region. |
| M02 | Anchored zoom solves the target/distance constraint together. When the camera floor prevents the full requested move, bounded search limits progress without reversing zoom direction. |
| M03 | The reader validates the supported writer subset, including required structure, references, scene, material and sampler semantics. Unsupported meaning is refused instead of discarded. |
| M04 | Textures are fully decoded through the restricted image loader and verified as PNG. Malformed image failures use the documented model exception; decoded images are disposed. Original encoded texture bytes are retained. |
| M05, M08 | Buffer views respect declared lengths. GLB chunks must be aligned, ordered and complete, with only legal buffer padding. Supplied vertex bounds must match their data. |
| M06 | Folder output stages a complete model before replacing its files. Failure restores the prior pair. If restoration also fails, backups remain in a named recovery directory and both failures are reported. |
| M07 | A double-precision area check rejects transformed triangles that collapse in stored float coordinates. Accepted transform arithmetic is unchanged; representable tiny geometry remains valid. |
| M09, M10 | Direct writer and batch naming share portable basename/device rules, including superscript Windows device suffixes. Unsafe names are rejected before companion-file access. |
| M11 | The existing maximum camera distance remains unchanged. XML now states that fitting is best effort when the distance cap prevents a full fit. |

All M01–M11 findings are addressed under the approved policy. D01–D17 XML corrections cover ownership, retained lifetimes, transform limits, partial alpha, supported formats, error boundaries, camera constraints, cancellation and notification timing. Generated API pages are current.

The separate simplification pass is complete: S01 shares basename validation; S02 removes a second whole-file copy; S03 uses ordinary list snapshots; S04 removes repeated depth-map clones in tests; S05 shares filename enumeration; S06 replaces token lookup iterators with table loops; S07 shares batch preparation and skip ordering while retaining the different execution lifetimes. Optional solid-mask composition and off-screen DDA optimizations remain deferred without measurements or preservation proof.

## Numerical and failure-path verification

Implementation used delegated `gpt-6.1-sol` owners and an independent integrated review. Regressions were observed failing before fixes. Further review found greedy-edge T-junction holes and UV-grid rounding cases; these were corrected before the final build.

- An independent 35,700-case camera sweep observed no reversed zoom; worst sampled anchor drift was 0.02132 pixels. The reported defect now moves distance from 7.826837 to 7.461687 with approximately 0.0000432 pixels of drift.
- Independent coverage checks passed 20,000 targeted T-junction views, 14,780 near-floor views, 2,000 front-triangle overlap cases and 72 winding/permutation cases. A 450-view L-mask ray sweep is retained in the regression suite.
- UV checks passed 1,049,600 grid-interval and wall-center cases for sizes 1–1,024, including the adversarial 13/23 round-trip and nongrid neighbors.
- A separate ordinary-geometry oracle checked 60 hashes covering mesh arrays and all writer formats over three pivots and four scales. Original baseline and final Release hashes match exactly.
- Failure regressions exercise folder-pair restoration, cancellation, notification boundaries and secondary rollback failure. The latter verifies that original backup payloads survive and the thrown exception identifies their recovery location.
- Final Model Release tests: **669 passed**, up from the 500-test review baseline; 169 additional cases. Focused renderer, format, geometry and batch suites also passed.

These are finite independent checks and regression coverage, not a proof for every possible floating-point input. Mesh validation adds one linear triangle pass; no unmeasured performance improvement is claimed.

## Integration gates

| Gate | Result |
|---|---|
| Release solution build | Zero warnings and errors |
| Ticket tests | 235 passed |
| Ticket netstandard tests | 235 passed |
| Core tests | 1,750 passed |
| Model tests | 669 passed |
| CLI tests | 331 passed |
| App tests | 1,928 passed; one Linux-only secret-tool test skipped on Windows |
| Full solution total | **5,148 passed, zero failed, one platform-specific skip** |
| API generation, lint and check | Clean; 269 pages current |
| Strict MkDocs build | Passed |
| Protected files and whitespace | Clean; fixtures, archived designs, dependencies, Core, Ticket and App production code unchanged |

## Output and rendered verification

Final Release CLI output matches the reviewed baseline sample: **470/470 files byte-identical**, with no missing, extra or changed files. Samples cover demo and frozen-fixture generation/extension, PNGs, metadata, DNA, rarity, indexes, both Menu modes and seeded rendering, and model generation. Menu ZIP entry payloads are compared after extraction; variable ZIP timestamps are excluded.

Direct 1-thread versus 4-thread output matches all 98 demo Set files and 38 fixture Set files. Four additional writer-file goldens match, and GLB read/write preserves the sampled bytes. Writer output also matches across `en-US`, `tr-TR`, `ar-SA` and `de-DE`.

Rendered Avalonia verification compares **431 baseline and 431 final frames**. Of these, **356 match exactly**. All 67 changed 3D preview frames were inspected: each changes only 1–32 pixels, consistent with the coverage/UV corrections; sheet previews visibly lose their erroneous internal diagonal. Six other frames contain random temporary-path labels. Two loading-state frames differ by four pixels each while thumbnail decoding is in flight. Preview pixels intentionally change for corrected cases; they are not claimed byte-identical. Layout and non-preview behavior are unchanged in these captures. No production App code, markup or theme changed.

## Limits and stop point

- Verification ran on Windows x64. Cross-culture and thread comparisons ran; another CPU architecture and an external model viewer were not exercised.
- The reader intentionally rejects malformed or unsupported semantics it previously ignored. This is a narrow supported-subset reader, not a general glTF importer.
- Nonrepresentable public-API scales and unsafe filenames now fail explicitly. Ordinary accepted model bytes, mesh ordering, UVs, DNA and RNG behavior remain unchanged in the sampled comparisons.
- Folder rollback is recovery logic, not a filesystem transaction across process termination or power loss. Secondary restoration failure preserves backups for manual recovery.
- Frozen fixtures, archived designs, dependency versions, Core, Ticket and App production code remain unchanged.
- The requested verification stop was observed. The user subsequently approved proceeding; Model is committed and locally tagged before the CLI review begins. No merge or push has been performed.

## Evidence

Ignored local artifacts under `artifacts/audit/` retain the executable probes, captures and logs:

- `model-release-build.log`, `model-release-tests.log`
- `model-api-generate.log`, `model-manual-build.log`
- `model-output-parity.json`, `model-demo-thread-parity.json`, `model-fixture-thread-parity.json`
- `model-formats-probe/final-results.txt`, `model-geometry-release-goldens.txt`
- `model-frame-parity.json`, `model-frame-review.png`, `model-all-renderer-changes.png`, `model-final-frames/`
- `model-render-red.log`, `model-render-green.log`, `model-render-tjunction-red.log`, `model-render-tjunction-green.log`, `model-render-tjunction-sweep.log`, `model-render-uv-grid-red.log`
- `model-format-red.log`, `model-format-final-red.log`, `model-format-final-green.log`
- `model-batch-red.log`, `model-batch-green.log`, `model-batch-recovery-green.log`
- `model-render-fix-probe/` and the temporary `nfty-model-geometry-fix-baseline/` oracle directory

The [review report](03-model.md) preserves the original findings. The [implementation checklist](03-model-implementation.md) records approved scope and completion; the [overall tracker](REVIEW.md) retains all completed stages.
