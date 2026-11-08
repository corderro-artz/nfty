# nfty audit — after-action report

**2026-11-08.** The reviewed source changes are implemented and merged into main. This report records
what changed, what was simplified, the result of each change, and the limits of the evidence.
**Audited release 2.4.8 is published from main.** A separately requested Cook-dialog cleanup and
developer-tool security update are in progress for 2.4.9; their remaining gates are listed below.

The audit covered Nfty.Ticket, Nfty.Core, Nfty.Model, Nfty.Cli, Nfty.App, Nfty.Desktop, both embedding
samples, developer tools, CI and shared build configuration. Each scope received a correctness and
ownership pass, then a separate documentation and bounded-simplification pass. A finding's proposed
remedy in an early review is not necessarily its final implementation; the tables below use the
completed implementation and verification reports.

Finding and D/S identifiers restart within each project. For example, Ticket T01 and tooling T01
are different items. Rows are not an additive count of unique defects: some integration work crosses
projects, and Desktop W07/tooling T09 describe the same coordinated dependency-instruction correction.
Focused test selections overlap full-suite totals and are never added to them.

## Published 2.4.8 integration and publication record

| Item | Result |
|---|---|
| Correct review baseline | `5dece661785a75990576f4016eb796558ba75fd4`, the then-current `origin/main`; the original stale-local-main review was rebased and all readiness checks repeated. |
| Final source build | Forced, non-incremental Release solution build passed with zero warnings/errors; `artifacts/audit/final-audit-build.log`. |
| Final solution test gates | **5,368 passed, zero outstanding failures, one Linux-only skip** across the final six project results. The first full run's wording failure and the subsequent CLI raw-ZIP timestamp test correction are disclosed below; the fresh complete CLI rerun passed 411/411. |
| Final 431-frame capture/comparison | **431/431 names present, 423 exact, eight changed**: six temporary-path labels, one in-flight thumbnail loading frame and one ten-pixel single-level control-edge difference. The coordinator visually inspected all eight changed regions; no layout change. `artifacts/audit/final-audit-frame-parity.json`. |
| Final output preservation | 470/470 files byte-identical to the canonical-LF checkpoint; no missing, extra or changed files. `artifacts/audit/final-audit-output-parity.json`. |
| Main code commit and merge | `af84cf6d7abff15a64f7262d39e83987a445c3a3`, fast-forward merged into `main`; local checkpoint tag `audit-suite-verified-2026-11-08`. |
| Separate main documentation work | README committed as `6c07a38`; 21 manual pages verified locally. This consolidated report and manual corrections are being finalized for the next main commit. |
| Published audit tag | `v2.4.8` points to main commit `6c07a38be1b5a8c8bbd228f5a0968c35f2036081`. |
| Eight rebuilt assets | All four Windows ZIPs and four Linux tarballs rebuilt from main, with zero publish warnings/errors. All eight extracted CLIs and native startup checks pass; [asset inventory](releases/2.4.8.json). |
| Windows/Linux packaged output | All 67 payloads match exactly across both operating systems and all four package shapes. Embedded demo bytes and executable modes were checked. |
| GitHub publication | Main/tag pushed; [2.4.8 published](https://github.com/corderro-artz/nfty/releases/tag/v2.4.8) on 2026-11-08. All eight server SHA-256 digests and byte counts match local archives. |
| Hosted CI | [Run 42526520361](https://github.com/corderro-artz/nfty/actions/runs/42526520361): all nine jobs successful on the actual `6c07a38` main commit, including Windows/Linux GUI, macOS/Core/CLI/Model and both samples. Platform-specific skips remain visible in the logs. |
| Native placement | Linux private 1920×1080 display: all four main windows 1280×720 at (320,180); portable 2D viewer 1024×576 at (448,252). Both are exactly centered. Windows portable demo/Cook/seed reroll were visually exercised; the other three shapes reached a responding native window. |

The older stage reports' instructions to stop, not merge or not publish describe checkpoints at
that time. The user later authorized finishing all stages, committing/merging to main and publishing
the fully verified audited release. This report does not treat that authorization as evidence that
publication has happened.

The final .NET project results are Ticket **235**, Ticket.NetStandard **235**, Core **1,753**,
Model **674**, CLI **411**, and App **2,060**, with one App Linux-only secret-tool integration skip
on the Windows host. These sum to 5,368 passing cases; focused selections and standalone probes
are excluded. Both standalone callback modes—live owner and disposed owner—also passed.

The first final full-solution run rejected newly added manual wording that said “unique assets”
instead of the project's precise “unique DNA” terminology. That text was corrected. The next CLI
run then exposed a pre-existing verification flaw: the culture test compared raw ZIP bytes and
crossed a DOS timestamp boundary. The coordinator replaced that assertion with recursive nested
archive name/payload hashing and rebuilt/reran the complete CLI suite, which passed 411/411. No
product behavior was changed for this timestamp-only difference. A subsequent complete solution rerun passed all 5,368 cases with the same one platform skip
(`final-audit-tests-corrected.log`). The earlier red logs are retained rather than labeled passes.


## Current follow-up status — 2.4.9

The original nine staged audits and their approved implementations are complete. The user then
requested a separate cleanup of the Cook dialog's options/footer and a new main-branch release.

- **Complete:** 2.4.8 main merge, commit/tag/push/publication, eight-asset digest verification and
  all nine hosted CI jobs. The table above records the actual published checkpoint.
- **Complete locally:** README corrections; 21 manual-page corrections; strict manual build;
  63 pages and 457 local file targets checked with zero missing targets. Publication follows
  the separate main documentation commit. Existing Diataxis navigation remains.
- **Complete locally:** Pillow tooling pin updated from 12.1.1 to 13.0.0 and the Python tooling
  minimum to 3.11. All 59 Dependabot alert ranges include the old pin and exclude the new one.
  Isolated verification preserves all 49 demo PNGs and 45 depth maps; 15 manifest differences
  are only the already accepted canonical LF change. Tool regressions pass (18 plus one Windows
  symlink-permission skip). Pillow is not a shipped .NET application dependency. Final hosted
  checks and alert refresh follow the next push. ImageSharp remains pinned at 3.1.11.
- **Complete locally:** compact Cook options/footer, truthful DNA/retry hints, fixed geometry
  across model/form/running/done states, both themes and keyboard verification; 49 focused passes.
- **Complete locally:** four manual screenshots replaced from actual 50-asset/50-model production
  renders, with exact source-pixel verification and updated crop mappings.
- **Complete locally:** final Release build has zero warnings/errors; corrected full solution
  passes **5,375 tests with zero failures and one Linux-only skip** (App 2,067). Captured 453
  frames: 403 common frames exact, 20 intentional Cook changes, six temporary path labels,
  two tiny raster variations (two/four pixels; maximum channel differences one/three), and
  22 new Cook-state frames. No baseline frame is missing.
- **Pending:** eight fresh 2.4.9 packages/output parity; main tag/push/release; live documentation,
  screenshots, badges and dependency-alert refresh.
- **Complete:** preserved and SHA-256 verified all 25,780 ignored evidence files on main, then
  deleted the merged `codex/ticket-audit-fixes` branch. Codex refuses to remove this chat’s primary
  checkout folder; that folder remains detached and is not the active development branch.

The first 2.4.9 full run found the new test harness’s unbounded worker gate via the repository
wait-policy guard. A bounded wait corrected it; a transient wrong callback-argument edit was
rejected by the compiler and corrected. The nearest ten cases and the subsequent complete
solution rerun pass. No production behavior changed for this harness correction.

## Public documentation corrections

| Area | Change and result |
|---|---|
| Links and badges | Four missing local README targets and one live 404 repaired. Badges now distinguish the build policy, SDK, actual Windows/Linux downloads and dynamic release/CI state. |
| Installation and architecture | Correct 1280×720 logical startup size, ordinary .NET Runtime 10, Linux dependencies, state/XDG locations, six test projects, and Ticket's two target frameworks. Stale package-size figures removed. |
| DNA and reproducibility | Distinguish fingerprint uniqueness from different pictures, exact counts from bounds/unknown, and complete input/options equality from edits preserving only identities. Describe actual stack-ordered random draws. |
| Generation and extension | Explain bounded retries, repeat-mode rule validation and uniqueness-conditional collision prevention. Document cancellation's existing rarity-rewrite limit. |
| Models, formats and demo | Explain layered front/back ownership and the atlas, actual ZIP JSON entry names, named color buckets, five shared Ingredients/two Body Ingredients and flat Glow. Preserve the tested 8,745,000-DNA count. |
| Publishing and OS integration | Describe acquired-pin-only best-effort rollback and URI handoff, preserve other Windows defaults, and remove an unverified provider recommendation. |
| Organization and verification | Existing Diataxis categories retained; no wiki enabled. Strict manual build passes, 457 targets resolve, and all eight font files plus OFL match source bytes. |

## 1. Nfty.Ticket

### Correctness and ownership

| ID | Change made | Result |
|---|---|---|
| T01 | Reject ambiguous `=` in ingredient IDs and malformed UTF-16 at validation/generation boundaries; retain valid Unicode and variant `=`. | Distinct accepted selections cannot exploit that delimiter collision. Historical DNA encoding is unchanged; old books remain readable for inspection/repair. |
| T02 | Plate ticket selections in the Menu's authoritative layer order and validate recipe/layer/variant identity, including empty tickets. | Reordered wire fields no longer reverse paint order under the same DNA; duplicates and unknown references fail clearly. Optional omissions remain supported. |
| T03 | Extend the current package-free MenuJson binder's required-field and nested structural validation before lookup/index construction. | Malformed IDs, null elements, weights, references, kind/color combinations and cell geometry fail at the archive boundary instead of crashing later. Both target frameworks remain package-free. Earlier upstream parser fixes were retained, not reimplemented. |
| T04 | Include ZIP-constructor failure in owned-stream cleanup. | Failed ZIP/manifest opens release their streams immediately; successful archive disposal retains its ownership contract. |
| T05 | Reject nonfinite numeric color components before conversion. | NaN, infinities and overflowing exponents no longer silently become ordinary colors. Valid finite wrapping/clamping is preserved. |
| T06 | Check depth-map X and Y independently before flattening. | Invalid coordinates cannot alias a different valid pixel; valid access and immutable updates are preserved. |
| T07 | Fall back to overflow-safe optional-weight arithmetic only when the original intermediate overflows. | Representable extreme optional weights remain usable without changing ordinary floating-point association, draw counts or seeded sequences. Truly unrepresentable totals still fail. |
| T08 | Share positive-interval scanning and correct the rounded upper endpoint in weighted and color-entry selection. | Subnormal/rounded draws cannot choose a trailing zero-weight outcome. Absent-only behavior and ordinary one-draw sequences remain intact. |

### Simplifications

| ID | Status and implementation | Result |
|---|---|---|
| S01 | Done: shared weighted scans and total validation. | The endpoint fix has one implementation; ordinary and optional rolling retain their preservation tests. |
| S02 | Done: direct archive ownership/exception cleanup and shared ZIP-entry byte reading. | Fewer duplicate stream-copy paths; lazy art access and depth-length policies remain explicit. No global atlas cache was introduced. |
| S03 | Done: XML reorganized around current contracts with concise load-bearing remarks. | Renderer, schema, wire, sampling and ownership instructions match the shipped API. |
| S04 | Deferred: prepared fixed-color/entry-total caching. | No performance benefit was measured; reparsing/summing was not changed merely to reduce apparent work. |
| S05 | Deferred: removing the intermediate DNA selection list. | No second hashing implementation or unmeasured allocation optimization was added. |

### XML groups

| ID | Correction and result |
|---|---|
| D01 | Plating documents both HSV and HSL rendering, so third-party renderers are not taught HSV-only math. |
| D02 | Static color consumes no color RNG; variant selection still rolls and can add uniqueness. |
| D03 | Schema contracts state `Oldest..Current` and refusal below the floor as well as above the ceiling. |
| D04 | Ticket wire grammar includes the mandatory kind field and Custom empty-field form. |
| D05 | Menu validation documentation matches the current package-free reader. Obsolete System.Text.Json/TryGetInt32 discussion from the stale baseline is not presented as a current fix. |
| D06 | Sampling comments distinguish ideal half-open intervals from reachable floating-point endpoints and the real weighted fallback. |
| D07 | Parameters, returns, validation preconditions, coordinate exceptions, archive failure ownership and nonconcurrent ZIP-read limits are stated. |
| D08 | Rules-placement test rationale no longer claims Exclude can never expose premature rule checking. |

The verified Stage 1 full solution passed 4,851 tests with one Linux-only skip, at zero build
warnings/errors. Ticket passed 235 cases on each of net10.0 and netstandard2.1. The 470-file output
sample was byte-identical; extracted Menu payloads, rather than ZIP timestamps, were compared.
Settled 3D capture controls matched. This is historical stage evidence, not the final suite total.

## 2. Nfty.Core

### Correctness, mathematics and ownership

| ID | Change made | Result |
|---|---|---|
| C01 | Stage owned export contents and refuse occupied destinations rather than overlaying a narrower plan. | A Marketplace export cannot retain a previous FullProject's excluded source book, seed manifest or rich metadata. Unrelated author files are not deleted. |
| C02 | Enforce safe export leaves and normalized destination containment. | A collection named `..` cannot escape the chosen output root; collection display names are not rewritten. |
| C03 | Track positive pin acquisition and roll back only acquired pins. | Failed/canceled upload preserves pre-existing or unknown-ownership content. Kubo state and Pinata duplicate evidence feed the policy; older providers default to unknown. |
| C04 | Restore the prior manifest/invalidate failed indexes after post-manifest packing failure; stage packed replacement and arm rollback only at mutation. | Failed extension cannot leave a larger manifest with rolled-back assets. Preflight refusals retain existing bytes/timestamps. The documented older-item rarity-regrade limitation remains. |
| C05 | New seals authenticate versioned length-prefixed header fields, including schema; preserve legacy v1 authentication bytes. | Distinct collection/count/note headers no longer collide in new seals. Historical seals remain readable, but their old ambiguous labels cannot be retroactively strengthened. |
| C06 | Rename only canonical per-item primary paths; retain additional included image/metadata paths and detect destination collisions. | Additional numbered files cannot overwrite canonical exported PNGs. Models keep their stricter recorded-file inclusion policy. |
| C07 | Reuse shared nested-structure validation for readers and direct Validator calls. | Malformed archives fail contextually; malformed in-memory graphs produce validation problems instead of null/key exceptions. |
| C08 | Reject undefined layer-kind, color-model and rule-type values at the legality gate. | Unknown numeric enums cannot silently select alternate semantics or disable constraints; defined representations remain supported. |
| C09 | Validate finite aggregate recipe, variant and color weights. | Individually finite weights whose sum overflows fail before rolling with a relevant diagnostic. Ordinary accumulation and RNG behavior are preserved. |
| C10 | Refuse deletion of variants referenced by rule triggers or targets. | Editing cannot persist an invalid recipe by removing a still-referenced variant; rules are not silently cascaded away. |
| C11 | Match asynchronous Kitchen creation to no-overwrite sync semantics and check cancellation before destructive file acquisition. | Existing Kitchen bytes survive attempted recreation and pre-cancellation. |
| C12 | Reconcile selection-odds numerator/denominator arithmetic and final bounded-error handling. | A guaranteed legal selection is reported as probability one rather than Unknown because of a tiny arithmetic mismatch. |
| C13 | Dispose a rendered streaming asset when progress reporting throws before yield. | Ownership transfers only on successful yield; a synchronous callback failure no longer leaks an image the caller never received. |
| C14 | Preserve positive magnitude and bound wording below ordinary percentage display precision. | Reachable tiny probabilities do not misleadingly print as exact zero. Exact zero remains distinct. |
| C15 | Recognize collapsed saturation intervals in sampled coordinates as point masses. | Narrow valid ranges produce finite normalized color shares consistent with the sampler, instead of NaNs. |
| C16 | Mark allocation totals unavailable across thread changes instead of subtracting unrelated thread counters. | Async scopes retain timing without reporting impossible negative/incorrect scope-owned allocations. |
| C17 | Format profiler columns invariantly. | A stored measurement report does not change decimal syntax with the current locale. |
| C18 | Treat unrelated callback OperationCanceledException as an indexed failure unless the operation's token is actually canceled. | Sequential and parallel helpers choose the same deterministic failure rather than changing exception shape/order. Genuine cancellation is preserved. |
| C19 | Include genuine legacy absence outcomes during existing-item tally and rarity regrade. | Extension preserves absence rarity without changing historical published OpenSea attributes, DNA or numbering. |
| C20 | Check JSON identity value kinds before conversion and use CorruptSetException. | Wrong-kind set numbers receive contextual corruption errors through sync/async/index-fallback paths. |
| C21 | Await nested asynchronous archive callbacks before disposing their ZIP. | Delayed callbacks retain a valid archive lifetime. This was a latent API defect, not a demonstrated normal-input failure in then-current synchronous callbacks. |
| C22 | Claim a strict counting lower bound only after finding a distinct bucket beyond the enumeration budget. | Totals exactly equal to the budget stay exact, including fixed/ranged unions. |

Additional mathematical corrections made during implementation:

| Item | Action and result |
|---|---|
| Actual sampled endpoints | Count actual sampled extrema, including rounded upper endpoints, without changing the sampler. A one-ULP hue range with two reachable DNA buckets no longer reports one. |
| Legal-selection certainty | Do not turn an undercounted color layer into a strict total lower bound when rules force it absent. Empty spaces and always-absent layers retain appropriate exactness. |
| Single-draw precision | Report Unknown/unavailable for cumulative tables whose positive branches cannot be represented by the 53-bit draw or whose subnormal scale distorts nominal probabilities. Archive acceptance/generation are preserved. Many ordinary draws may still form a reportable tiny joint probability. |
| Demo count | Independent enumeration established 8,745,000 total: Chest 4,785,000 plus Strongbox 3,960,000. Text/expectations were corrected; the embedded demo and frozen fixtures were not regenerated. |

### Simplifications

| ID | Status and implementation | Result |
|---|---|---|
| S01 | Done: build/share one slot map per SetWriter operation. | Index and skipped-slot work share the same mapping without changing files/progress ordering. |
| S02 | Done: remove unused StampAsync set-directory parameter. | Smaller internal contract; stamping still uses the authoritative set number and naming fallback. |
| S03 | Done: remove unused export-naming wrappers. | One naming implementation remains. |
| S04 | Done: remove unused reachable-layer identity and return its variant list directly. | Counting keeps filtering, order and deduplication without an unnecessary wrapper. |
| S05 | Done: scan and return the existing depth buffer rather than constructing/scanning/cloning a DepthMap. | Two whole-buffer copies are removed; flat-map and masking behavior is preserved. |
| S06 | Done: share color-sector calculation for names and point masses. | Wrapping/boundary semantics have one implementation. |
| S07 | Done: share partial-alpha existential scans. | Short-circuiting and borrowed image ownership remain intact. |
| S08 | Deferred: common rule diagnostics across authoring/Validator. | Their empty/duplicate-target and cross-rule policies differ; changing acceptance was not disguised as refactoring. |
| Unnumbered traversal candidate | Deferred: optional footprint traversal reuse. | No unnecessary ownership/policy reconciliation or broad traversal rewrite. |

### XML groups

| ID | Correction and result |
|---|---|
| D01 | Unique-space arithmetic describes per-legal-selection bucket products for optional layers. |
| D02 | RecipeSpace combination counts disclose unconstrained fallback and certainty. |
| D03 | Retry exhaustion does not claim the mathematical unique space is proven exhausted. |
| D04 | Streaming generation states cancellation exceptions. |
| D05 | GeneratedSet documents rule-reroll failure with uniqueness disabled. |
| D06 | AbsentLayers are display names, not IDs. |
| D07 | Reproduction contracts include compatible uniqueness/options as well as book/seed. |
| D08 | Upload cancellation distinguishes acquired, pre-existing and unknown pins. |
| D09 | Stamping prefers ExportEntry.SetNumber before filename fallback. |
| D10 | Seal comments describe current framing, authenticated encoding/version and legacy limits. |
| D11 | An export without set.json can reopen as a read-only marketplace bundle. |
| D12 | SetLayout no longer promises absolute paths when given relative inputs. |
| D13 | Loaded-to-peeked projections preserve supplied order; ordinal order depends on the producer. |
| D14 | Loaded Menu projection discloses the art scan needed for partial alpha. |
| D15 | Archive schema errors include versions below the supported floor. |
| D16 | DepthSurface Overwrite invalidates the whole canvas even for identical bytes. |
| D17 | Loaded depth coverage can equal variant-image count. |
| D18 | Rule authoring and Validator checks have distinct responsibilities. |
| D19 | RemoveIngredient returns a new graph; the caller retains ownership of the removed subtree. |
| D20 | ImageFormats distinguishes metadata skipping, retained/lazy ICC bytes, NoIcc and metadata-preserving overloads; it does not claim a newly demonstrated advisory exploit. |
| D21 | Color-name/counting documentation distinguishes ideal intervals from floating-point sampling. |
| D22 | Profiler XML states allocation-thread restrictions and invariant reports. |

Core's completed stage passed 1,750 tests, 128 above its review baseline; the whole solution then
passed 4,979 with one platform skip and zero build warnings/errors. A separate exact rational oracle
checked 72 weighted/optional/ruled configurations. The 470-file sample stayed exact, 98 demo/38
fixture files matched across one/four threads, and 74 files per culture matched across four locales.
Of 427 rendered frames, 411 were exact; eight corrected count labels, six temporary paths and two
deliberate loading-state captures explain the rest. No live pins were mutated.

## 3. Nfty.Model

### Correctness and failure recovery

| ID | Change made | Result |
|---|---|---|
| M01 | Canonicalize near-plane intersections and consistent edge coverage; also close greedy-edge T-junction and UV-grid rounding cases found during integration. | Opaque preview faces no longer contain erroneous cracks/internal diagonals. Texture samples stay inside the original triangle's half-open UV region; exported model bytes are not changed by the raster correction. |
| M02 | Solve anchored target/distance constraints together and bound progress when the camera floor limits movement. | Zoom-in does not reverse outward to satisfy a new floor. Anchors remain stable within the demonstrated constrained cases. |
| M03 | Validate required fixed-subset glTF structure, references and scene/material/sampler semantics. | Unsupported meaning is explicitly refused instead of silently dropped. This remains a narrow reader, not a generic importer. |
| M04 | Fully decode and verify PNG textures through the restricted loader, dispose temporary images and retain encoded bytes. | JPEGs mislabeled PNG and truncated images fail inside the documented model-error boundary. |
| M05 | Bound views against positive declared buffer lengths while permitting valid padding. | Extra physical storage no longer authorizes an out-of-declaration view. |
| M06 | Stage each model's complete file group, restore the prior pair after failure and preserve named backups if restoration also fails. | Failed glTF/BIN replacement does not silently leave a mixed pair. Callbacks run after a safe commit; already completed models remain after cancellation. |
| M07 | Reject triangles that collapse after accepted transforms are stored as float coordinates, using a double-precision area check. | Nonrepresentable public-API scales fail explicitly; representable tiny geometry and ordinary transform arithmetic are preserved. |
| M08 | Check GLB chunk order, alignment, complete framing and legal padding; verify supplied bounds against vertices. | Incomplete trailing headers and inconsistent data are refused; valid writer round-trips remain intact. |
| M09 | Share portable safe-basename validation for direct writer entry points. | The writer cannot emit companion names its own reader refuses; failure occurs before companion access/encoding. |
| M10 | Include superscript COM/LPT device suffixes in portable batch naming. | Reserved Windows names are made safe without opening devices during tests. |
| M11 | Keep the existing maximum camera distance and qualify the framing guarantee. | Extremely narrow-aspect framing is explicitly best effort under the cap. This item was resolved by an accurate contract, not a new camera-range policy. |

### Simplifications

| ID | Status and implementation | Result |
|---|---|---|
| S01 | Done: one basename validator. | Writer entry points cannot drift in safe-name rules. |
| S02 | Done: path reads avoid a second whole-file array copy. | Span ownership/error framing is retained. |
| S03 | Done: ordinary List.ToArray snapshots replace specialized CollectionsMarshal spans. | Mesh arrays and complete encoded files match their preservation oracle. |
| S04 | Done: cache unchanged depth snapshots/use point access in tests. | Tests stop cloning whole depth maps per pixel without weakening assertions. |
| S05 | Done: reuse model filename enumeration. | Format/name order has one source. |
| S06 | Done: use direct token-table loops. | Removes unnecessary iterator machinery without adding a registry or framework. |
| S07 | Done: share batch name/order preparation and skip-report ordering. | Distinct sequential/windowed execution lifetimes are deliberately retained. |
| Unnumbered candidates | Deferred: solid-mask composition and off-screen DDA optimization. | Neither had measurement and preservation proof; no speedup is claimed. |

### XML groups

| ID | Correction and result |
|---|---|
| D01 | Finished StackRise art is not necessarily the actual front face owner. |
| D02 | Callers own/dispose StackRise returned by ForAsset. |
| D03 | Forwarded empty-selection, size and finished-mask mismatch exceptions are documented. |
| D04 | Whole-book partial-alpha refusal does not claim every generated asset would select that variant. |
| D05 | Combine's clear-pixel guarantee is distinguished from Uniform's supplied-alpha clone. |
| D06 | Public scale contracts match representability rejection. |
| D07 | Unsupported/malformed model errors are distinguished from propagated I/O/permission failures. |
| D08 | UnsupportedModelException includes malformed JSON, truncation and structure. |
| D09 | Writers may compact numbering; Format is consumed while the supplied mesh already carries scale/pivot. |
| D10 | Payload arrays remain mutable/retained references; read records do not return an owned decoded image. |
| D11 | New render targets start at zero; callers must Clear before drawing rather than assume infinite depth. |
| D12 | Camera anchors are current-distance ray heuristics, with target/floor constraints, not surface picks. |
| D13 | WithSubject can reduce distance to the new maximum as well as raise it to a floor. |
| D14 | FramedOn's best-effort limit under MaxDistance is explicit. |
| D15 | Camera validity assumes finite inputs; documentation no longer claims constructor rejection it does not perform. |
| D16 | Batch overloads preserve bytes/order but have different callback/input-release lifetimes. |
| D17 | SetModeler retains borrowed recipe/art/depth; the book must remain live and stable during use. |

The stage passed 669 Model tests, 169 additional cases, and 5,148 solution tests with one skip.
Independent checks included 35,700 camera cases, 20,000 T-junction views, 14,780 near-floor views,
2,000 overlap cases, 72 winding/permutation cases and 1,049,600 UV interval/center cases. These are
finite checks, not universal floating-point proofs. Sixty ordinary mesh/format hashes and the
470-file sample matched. Of 431 captures, 356 were exact; 67 corrected 3D previews changed 1–32
pixels each, six had temporary paths and two showed in-flight loading. No external model viewer was
used at that stage; the final independent viewer check below subsequently closes that gap. A second
CPU architecture remains untested.

## 4. Nfty.Cli

### Correctness and workflow protection

| ID | Change made | Result |
|---|---|---|
| L01 | Keep lazy-conversion diagnostics through validators and place parsing inside the process error boundary. Eight additional missing-string cases were fixed during review. | Malformed arguments fail with parser diagnostics instead of uncaught process exceptions. A 5,400-case direct-parser sweep had zero uncaught exceptions. |
| L02 | Commit complete single-model file groups through the existing rollback sink. | A failed companion replacement restores prior files; secondary failure retains named recovery backups. Output extension casing and ordinary model bytes are preserved. |
| L03 | Refuse occupied Set-owned output before generation, including selected packed/model targets. | New generation cannot mix stale assets or publish old duplicate DNA as newly unique. Unrelated Kitchen files/models/archives remain usable; extension is the growth operation. |
| L04 | Compare absolute target with existing count before subtraction. | Already-met targets, including int.MinValue, stay no-ops instead of wrapping into enormous positive work. |
| L05 | Require effective sealing when an explicit key source is supplied, after presets/overrides. | A supplied key cannot be silently ignored by an unsealed export; explicit and preset sealing remain authenticated. |
| L06 | Omit potentially secret unrecognized source text from errors, including verbose output. | Errors explain valid source syntax without echoing the supplied marker. |
| L07 | Refuse both orders of contradictory image/OpenSea/nfty switches. | Opposite flags cannot silently select an arbitrary winner. |
| L08 | Use Core's structural manifest gate before nested authoring enumeration/image decode. | Malformed authoring JSON fails with manifest/structure context rather than downstream exceptions. |
| L09 | Format added-recipe weight confirmation invariantly. | Successful confirmation text is stable across locales. |

### Simplifications

| ID | Status and implementation | Result |
|---|---|---|
| S01 | Done: use LayerDepth.DepthOf instead of allocating a list for each preview layer. | Depth semantics are shared; the underlying lookup is still linear and no stronger complexity claim is made. |
| S02 | Done with preservation refinement: retain historical UpsertRecipe weight-key insertion order, restore the original Recipes list directly and remove quadratic searches. | Seven nested authoring payloads remain byte-identical; directly switching to SetRecipeWeight would have changed manifest key order and was not done. |
| S03 | Done: reuse EditIdentity. | Identity transformation/ownership rules are not restated in the CLI. |
| S04 | Done: share a lazy export plan across compatible checks. | Repeated archive planning is removed without changing refusal/warning timing or owner lifetime. No measured speedup is claimed. |
| S05 | Done: read the first secret-file line rather than loading ignored trailing content. | BOM, line-ending, whitespace and empty-first-line behavior is preserved. |
| S06 | Done: make HttpClient ownership explicit per action. | Success/failure release the client; no long-lived-process exhaustion incident is claimed. |
| S07 | Done: resolve command input form once per validator; remove a one-use wrapper. | Existing archive mapping and form-specific errors remain authoritative. |
| S08 | Done: consolidate repeated replacement explanations in WriteReplacing's contract. | Current intent is concise without discarding caller-specific ownership rationale. |

### XML groups

| ID | Correction and result |
|---|---|
| D01 | Modeling documents its seeded Recipe form as well as other inputs. |
| D02 | CommandFactory describes its actual partial surface and process error boundary. |
| D03 | ResolveThreads distinguishes requested/effective parallelism and null-parse failure. |
| D04 | Authoring group docs describe Kitchen's input requirements, current subcommands and factory returns. |
| D05 | ID syntax states delimiter restrictions without incorrectly banning uppercase/Unicode. |
| D06 | Parity editing docs do not claim every operation delegates to a Core edit helper. |
| D07 | Loading/edit callbacks state owned images, partial-load cleanup and borrowed graphs. |
| D08 | Numeric/rule formatting helpers state inputs, returns and parse-fallback behavior. |
| D09 | Credential-source docs cover passphrases and Pinata credentials, first-line/whitespace behavior and realistic process/OS visibility limits. |
| D10 | Prompt delegates/output and Enter/Backspace/Escape behavior distinguish hidden secret characters from visible prompts/newlines. |
| D11 | Clear seal-header claims remain unverified until authenticated opening. |
| D12 | Guards distinguish null absence from supplied empty strings and document exceptions. |
| D13 | Private command/authoring/sheet/publishing helpers receive concise intent and ownership/error contracts, including complete model commit behavior. |

Final CLI-stage evidence was 406 CLI tests and 674 Model tests, contributing to a 5,228-test solution
pass with one skip. The 470-file sample, seven nested authoring payloads per operation, 98/38-file
thread comparisons and 76 files per culture matched. Of 431 captures, 421 were exact; six path labels,
two loading states and five one-level border pixels across two frames were investigated explicitly.
Output preflight is not an interprocess lock; recoverable rollback is not a power-loss transaction.

## 5. Cross-platform canonical JSON correction

This was an additional release-gate correction between the CLI and App checkpoints, not an item
silently folded into a byte-preservation claim. Actual extracted Windows/Linux CLIs produced equal
images, models and DNA, but indented JSON used CRLF on Windows and LF on Linux. Cache metadata sizes
therefore differed as well.

Shared `Json.Options` now emits canonical LF. Three focused tests first failed for exact nested
metadata, sync archive manifests and async archive manifests, then passed. No RNG, color, mesh or
identity arithmetic changed. Against the previous 470-file sample there were exactly 264 raw
matches, 201 LF-only JSON changes and five validated cache-length updates. The updated Windows/Linux
CLIs then matched all 67 tested payloads byte-for-byte. Later 470/470 preservation claims use this
canonical-LF checkpoint, not a claim that original Windows whitespace was untouched.

The versioned release assets built during this investigation were earlier candidates. They must
not be substituted for the final rebuilt packages.

## Independent exported-model viewer verification

The final audit subsequently opened three freshly exported models in Don McCurdy's public glTF
Viewer, running three.js r176, independently of nfty's rasterizer: an orientation-marker GLB, the
same art as a glTF/BIN pair, and a stored-depth Ingredient GLB read from the unchanged aura fixture.
Front/back views were inspected for orientation, opaque faces, crisp texture edges and stepped
thickness. The back is naturally mirrored when viewed from behind. GLB and glTF/BIN front captures
were byte-identical in RGBA pixels at the same camera.

Khronos glTF-Validator reported **zero errors and zero warnings for all three**. Each had one
informational IMAGE_NPOT_DIMENSIONS note for its intentional 11×5 or 17×8 face atlas. That information
note is not a geometry/transparency warning and does not require padding or resampling source art.
The retained evidence is `artifacts/audit/external-viewer/VERIFICATION.md`, its four screenshots and
three validation JSON files. Early browser/input/result-inspection mistakes are identified there
and excluded from proof.

This closes the specific external-viewer backlog check, not general foreign-glTF import support or
universal third-party compatibility. No engine change or fixture regeneration was needed.

## 6. Nfty.App

### Authored data, workflow and lifetime corrections

| ID | Change made | Result |
|---|---|---|
| A01 | Resolve displayed rule rows against authored identity, then revalidate identity after dialogs. | Editing/deleting a sorted or filtered row mutates the intended authored rule, not another rule at that display index. |
| A02 | Track dirty state across successful Undo/Redo after Save. | Restored pixels differing from disk remain saveable and trigger leave protection. No-op history actions remain no-ops. |
| A03 | Associate save snapshots with mutation revisions. | Edits made during an asynchronous save remain dirty instead of being falsely marked saved by completion of the older snapshot. |
| A04 | Route Escape/scrim dismissal through busy-card cancellation and cleanup. | Running Cook/export/upload/Menu cards retain their UI and lifetime until unwind; dismissal cannot bypass close protection. |
| A05 | Guard chrome navigation/modal launch and use non-orphaning dialog stack/answer ownership. | Help/Settings/document close cannot dispose a running job's borrowed book or abandon an older dialog answer. |
| A06 | Snapshot the selected variant ID before the picker and use VariantPreview. | Preview export writes the chosen variant even if selection changes while the picker is open. |
| A07 | Reconcile reference rows by stable identity, retain enabled/placement choices and invalidate rebuilt stacks. | Repeated Save does not duplicate reference rows or make enabled reference ink disappear. |
| A08 | Make prospective/current loose-save image ownership and adoption explicit, with failure cleanup. | Repeated loose/in-book saves and failed writes do not leave the latest owned images alive after close. Shared session images are not disposed accidentally. |
| A09 | Retain incoming import ownership until persistence/adoption succeeds; dispose replaced orphaned subtrees and failed incoming graphs. | Same-ID Recipe/Ingredient replacement no longer leaks images; unrelated surviving references remain valid. |
| A10 | Notify the existing Ingredient pane's delete command when lock state changes. | Its real Button enables/disables on unlock/relock without pane recreation or lost selection; execution still checks the authoritative lock. |
| A11 | Use a session-owned structural-write lease before graph derivation and through adoption/refresh. | Overlapping rule/chance/reorder edits cannot write stale whole-book snapshots or submit the same chance twice; close respects the write lifetime. |
| A12 | Write models-checkbox changes through to export options. | The displayed checkmark agrees with Parts, exported GLB content and upload choices. Unmodeled Sets keep their disabled/unticked appearance. |
| A13 | Invalidate/cancel checks or verify endpoint identity before accepting connection results. | An old endpoint's late success cannot label a newly typed endpoint Connected. |
| A14 | Own a cloned live-viewer snapshot across the save picker await. | Source refresh/disposal cannot invalidate the image being saved; unchanged PNG/enlargement/seal behavior is retained. |
| A15 | Notify all live-model properties/commands and apply model-loss layout fallback. | Model arrival enables rendered controls; model disappearance reveals valid 2D art rather than leaving it hidden in a stale 3D layout. Ordinary model updates retain camera identity. |
| A16 | Apply inspector scale and pan coherently after computing from the prior transform. | Wheel zoom keeps its valid anchor instead of clamping pan against old Fit limits. |
| A17 | Bind canonical voxel bounds and three-decimal editing; retain sufficiently precise dimension labels. | The real field accepts 0.001/0.123 correctly. Sub-0.01 and quarter-voxel dimensions do not print zero or clip; numbers fit two lines inside the existing 26px row with a full-value tooltip. Export arithmetic is unchanged. |
| A18 | Remove case-insensitive aliases before writing a canonical settings key. | Theme/canvas/provider/API updates do not revert on reload because an older alias wins; unknown settings keys remain. |
| A19 | Carry each screen's scaling into saved-window overlap/clamp calculations. | DIP sizes and pixel working areas are no longer mixed. Managed tests cover 100%, 200%, mixed DPI and monitor removal. Desktop later changes default startup to centering while retaining explicit placement compatibility. |
| A20 | Distinguish secret-tool absence/no-op from sanitized deletion failure. | Failed key deletion retains the UI hint instead of implying that the stored credential is gone. Real Linux keyring failure was not executed. |
| A21 | Update an existing macOS credential, adding only when absent, instead of deleting before replacement. | A failed replacement policy does not intentionally remove the previous credential. Managed native-status policy is tested; native macOS execution remains unverified. |

Additional integration corrections:

| Item | Action and result |
|---|---|
| Recipe-local deletion identity | Resolve domain kind and owning recipe as well as ID after confirmation. Reopened archives prove only the intended item was removed and unrelated image pixels survived. |
| Close-after-await race | Recheck current writers, modal state and navigation after leave awaits before disposing borrowed graphs. |
| Cook/Menu command lifetime | Make Close availability follow the accepted operation's CTS, notify availability and guard direct execution. |
| Upload cancellation guidance | Restore the explanation that pre-existing content stays pinned; match Core's ownership-limited cleanup policy. |
| H01 callback ownership | Capture each worker's originating dispatcher and explicitly own pending thumbnails/books/results. Dispose unpublished results even when an obsolete UI queue never drains. Cover mesh disposal before completion and after queueing, waiting for snapshot release. |

H01 received a separate-process controlled proof using the actual gallery thumbnail callback forced
into Avalonia's dispatcher-reset/compositor gap. The old binary fails both live-owner and
disposed-owner interleavings; the fixed binary passes. The historical competing callback itself was
not recorded. The demonstrated application mechanism is corrected; the upstream framework gap is
not claimed fixed. Per-test isolation was retained without retries, private-framework reflection,
dependency upgrades or exception suppression. The probe is now also a CI gate.

### Simplifications

| ID | Status and implementation | Result |
|---|---|---|
| S01 | Done: lazy legacy Ingredient colorway samples instead of unused eager rendering. | Public compatibility is retained; shipped swatches/selected thumbnails do not pay for unused samples. |
| S02 | Done: cache immutable stable HeroBars ordering. | Exact shares, tie order, paging and selection identity are unchanged. |
| S03 | Done: one authored-rule scan. | Fixes A01 without repeatedly filtering/sorting the displayed list. |
| S04 | Done: local prospective owners and try/finally adoption. | Save/import cleanup is explicit; no dangerous generic dispose-old-book framework was introduced. |
| S05 | Done: typed Chunk projections replace three row-copy loops. | Existing row identity caches, fallback four columns, Rise's three columns, order and short final rows remain. No speed/allocation improvement is claimed. |
| S06 | Done: shared measured-width bindings replace duplicated percent-track multipliers. | Actual-view geometry passes and ingredient share-bar/Recipe mix captures remain pixel-identical. |
| S07 | Done: each operation captures/disposes its own CTS at its boundary. | Cancellation remains a request; continuations are not handed a prematurely disposed source. |
| S08 | Done: reuse Refresh's export plan for sheet checks. | Error precedence, WrittenOptions, Parts, SizeText and naming remain consistent; speed benefit is unmeasured. |
| S09 | Done: one stable NewRecipe mix projection per change. | Sibling order, ties, five-row cap and floating-point addition order remain. |
| S10 | Done: readonly viewer preview and removal of duplicate DepthNote notification. | Camera identity and effective notification/layout behavior remain. |
| S11 | Done: share deferred current-book-plus-recents candidate projection locally. | Candidate priority, duplicates and hash-only acceptance remain; no locator cache was introduced. |
| S12 | Done: concise current contracts replace stale history essays at reviewed sites. | Ownership, coordinate, determinism and cancellation explanations remain; audit/briefing retain history. |

### XML groups

| ID | Correction and result |
|---|---|
| D01 | Picker/registration/wizard/editor docs describe shipped persistence, not old phase/stub tasks. |
| D02 | NewCookBook docs use the actual v2 floor and optional omission contract. |
| D03 | Export, cancellation and wizard summaries attach to the correct declarations. |
| D04 | Partial IngredientEditor/Rise types have one coherent ownership/behavior summary. |
| D05 | LooseWorkspace, FactorChip, gesture and PendingReorder documentation is neither duplicated nor misattached. |
| D06 | Detail/Explorer/editor comments distinguish borrowed graphs from owned bitmaps and describe Domain tuples/caches correctly. |
| D07 | VariantImagery describes fixed-color behavior, sampled colorways and returned bitmap ownership. |
| D08 | Illustrative chips are distinguished from legal DNA counts; shortened figures/tooltips are described honestly. |
| D09 | Authored rule identity is separate from display sorting/filtering; sort callbacks/casing/reset claims match the UI. |
| D10 | Save/import adoption and exceptions match the implemented lifetimes without overstating whole-operation atomicity. |
| D11 | Current settings/export page counts, pending opens, bundle recents and seal/reveal behavior replace obsolete/archived-design claims. |
| D12 | Rise source exclusivity, texture mutation, chips, 2D-only state and model snapshot/builder lifetimes match code. |
| D13 | glTF-plus-BIN size estimates disclose chosen-stem-dependent JSON length. |
| D14 | TwoDPane distinguishes logical units from device pixels and retains the tested 1:1 fit floor. |
| D15 | StripPanel states actual cost/tie/oversized behavior; CanvasMapping qualifies letterboxed fit pan. |
| D16 | ThumbnailGate limits are per instance/operation, not one global application budget. |
| D17 | Registration/persistence/session/workspace methods state parameters, returns and ownership. |
| D18 | Key-cap width/spoken helpers state parameters/returns and the current-style estimate boundary. |
| D19 | Help, builder, RangeAxis and RuleDialog contracts state valid ranges, ownership and validation responsibility. |
| D20 | Preview/gesture/hit-test/canvas methods describe inputs/returns without padding inherited overrides/private fields. |

The completed App checkpoint passed 5,335 solution tests, zero failures, one Linux-only skip;
App itself passed 2,032, 104 above baseline. Focused 120- and 80-case selections overlap that total.
The 470-file canonical sample and 98/38-file thread samples matched. Of 431 frames, 417 were exact;
all 14 changed regions were inspected: six temporary labels, two LF-related size labels reproduced
by a controlled Core swap, four intentional dimension-label alignments, one loading state and one
ten-pixel single-channel control-edge difference reproduced by an unchanged final binary. No
product test was suppressed to obtain that result.

## 7. Nfty.Desktop

### Correctness and requested startup behavior

| ID | Change made | Result |
|---|---|---|
| W01 | Detach named shell-caption handlers on replacement/close; accept a delayed close answer only for the shell asked. | Old shells cannot control replacement windows, repeated attachment cannot double-toggle maximize, and a stale leave answer cannot close the new shell. |
| W02 | Clean up transferred subject/view-model/window ownership on failed construction/settings/show, sharing idempotent content disposal. | Failed viewer handoff no longer leaks accepted resources; normal close uses the same cleanup contract. |
| W03 | Complete pending dialog answers on close; wait for busy cancellation cleanup; dismiss finished cards while preserving unknown busy work. | Viewer/launch close cannot orphan prompts or dispose active job content. Escape does not equate cancellation with completion. Independent review caught and corrected the finished/unknown-card edges. |
| W04 | Let ViewerLaunchWindow own PendingOpen, normalization errors, cancellation and final subject handoff. | Invalid viewer launch paths show an error instead of silently exiting. A result arriving after close is disposed and never opens a late viewer, including uncancellable decryption. |
| W05 | Share live-window selection across pickers, clipboard and default viewer display lookup; dispose selected storage handles after path extraction. | Native services work after the shell closes while a viewer remains. Spaces, percent/hash and Unicode paths are preserved, and handles are released once. |
| W06 | Put copied native bitmaps in an owning disposable clipboard transfer; clean up failed writes. | Releasing/clearing the transfer frees its bitmap rather than relying on ordinary DataTransfer's empty Dispose. Real headless Clear verifies disposal. |
| W07 | Make the Linux diagnostic helper pure, restrict the runtime catch to Linux and include all recognized-library packages. | Xrandr/Xi/Xcursor instructions now name packages that supply those libraries; non-Linux failures are not given Linux installation advice. |
| W08 | Pass reveal paths as a single ArgumentList value and dispose the returned process handle. | Legal Unix embedded quotes are not misparsed through handcrafted argument quoting; three duplicate launch branches become one request. No real file manager is invoked in tests. |
| R01 — user request | Main and all new viewer windows request CenterScreen; viewer startup is seeded from the live opening display, with native/primary fallback. | Old X/Y settings do not override centering. Remembered size/maximize/backdrop/wireframe survive. Active viewers take priority over a still-visible shell, and viewers remain unowned so they can outlive it. |

R01 delegates physical frame size, selected-screen scaling and working-area centering to pinned
Avalonia behavior. It does not add cursor-position P/Invoke. Explicit placement APIs and old X/Y
settings remain compatible for callers that specifically use them. Managed placement tests are
not presented as native mixed-DPI monitor execution.

### Simplifications

| ID | Status and implementation | Result |
|---|---|---|
| S01 | Done: one visible-window resolver. | Picker, clipboard and viewer opening-display policies no longer drift. |
| S02 | Done: one desktop dialog-lifetime helper. | Both viewer windows share close/Escape cancellation semantics. |
| S03 | Done: launch window owns one tracked open task. | Startup error/cancellation/cleanup is reviewable rather than buried in an anonymous application callback. |
| S04 | Done: shared idempotent viewer ownership cleanup. | Normal close and exceptional handoff do not maintain separate disposal protocols. |
| S05 | Done: one folder-opener request. | Platform selection, argument transport and process-handle lifetime are explicit. |

### XML groups

| ID | Correction and result |
|---|---|
| D01 | App composition names viewer hosting and the owned viewer-only launch. |
| D02 | Main close docs state current-shell checks/forced-OS limits; stale size/theme markup claims are removed. |
| D03 | Picker docs state live-window, local-path and storage ownership rather than claiming headless testing is impossible. |
| D04 | Viewer host docs state opening-window priority, centered startup and retained appearance. |
| D05 | Saved normal size/legacy coordinates are distinguished from centered host startup and explicit placement APIs. |
| D06 | Clipboard docs describe transfer-owned native bitmap lifetime. |
| D07 | Entry point/builder/diagnostic parameters and returns, plus PendingOpen, have accurate contracts. |
| D08 | Folder-opener request docs distinguish generated launch arguments from executing a native file manager. |

The final Desktop selection passed 79/79, including 28 new dedicated cases. Two capture tests
produced 18 frames—two shell and 16 actual viewer-window states—which were all SHA-256 identical
to the accepted App capture checkpoint; representative light/dark frames were also inspected.

One unsuccessful full App attempt remains disclosed: after controlled mutation tests, copied-back
source retained old timestamps and an incremental build reused the intentional mutant binary.
That run reported 2,055 passes, two detector failures and one skip. The fixed source was verified,
timestamps advanced and the assembly rebuilt; both detectors and the final 79-test selection pass.
That stale-binary run is not called a green full suite. The coordinator's forced non-incremental
build/final full solution gate is the authoritative final check. No more source mutations were used.

## 8. Nfty.Ticket.Sample

| ID | Change made | Result |
|---|---|---|
| TS01 | Select HSV or HSL tint math from the Menu-authoritative color model. | The illustrative pixel agrees with the real Core colorizer for HSL instead of teaching an HSV-only renderer; HSV behavior remains. |
| TS02 | Format color and geometry reports invariantly. | Identical Menu/seed input prints the same English/French report, wire and DNA. |
| TS03 | Describe exhaustion of the 1,000-attempt budget accurately. | Failure says no legal ticket was obtained within the budget, rather than claiming every possible roll is forbidden. |

This top-level sample has no handwritten public API requiring XML groups. Usage, assembly/namespace,
ownership and included snippets were checked. HSV-only commentary and the cross-sample claim that
this program renders PNG files were corrected. Its single prepared Menu, explicit bounded loop and
single RNG stream were retained: no sample framework, extra dependency or second generator was
added. It still references Nfty.Ticket only, with no packages, preserving its embedding lesson.

## 9. Nfty.Embedding.Sample

| ID | Change made | Result |
|---|---|---|
| ES01 | Prepare once and retry the same seed-7 RNG stream up to 1,000 times when rules reject a draw. | A valid book whose first draw is forbidden can complete rather than failing after writing cooked images. The loop does not claim mathematical proof of impossibility on budget exhaustion. |
| ES02 | Format output numbering and byte-count reports invariantly. | English/French runs retain identical reports, filenames and emitted PNG/GLB hashes. |

This top-level program also has no handwritten public XML surface. Included snippets and image,
Menu, model, StackRise/face and cookbook ownership were reviewed. Documentation now states its
three-distinct-DNA prerequisite and bounded retry policy. PreparedMenu is reused within the explicit
loop; Core/Model references and transitive imaging remain. The example does not promise a
whole-output-directory transaction or silently recover from unreadable input.

The five sample regression cases invoke actual compiled entry points. Their first selection had
four failures and one pass; the corrected focused run passed all five. The cases overlap the final
CLI/solution suite and are not an extra independent suite total. Temporary synthetic archives,
real Colorizer pixels and emitted payload hashes are used, with Console/culture state restored.

## 10. Tooling, CI and integration

### Findings

| ID | Change made | Result |
|---|---|---|
| T01 | Emit complete release-subprocess stdout/stderr on successful and failed exits. | Successful linker/native-tool warnings cannot disappear from package evidence. The release owner still inspects diagnostics; printing them does not itself enforce zero warnings. |
| T02 | Fail packaging when a runnable extracted CLI fails demo extraction or omits its demo; retain source fallback only when the target cannot run. Validate version/RID and resolved cleanup containment before deletion. | Broken embedded/single-file artifacts cannot be laundered by source fallback. Linux binaries are not treated as native macOS executables; cleanup cannot escape the checkout's real .release directory. |
| T03 | Authenticate diagram write requests with a random page token, custom header, request-size limit, exact light/dark keys and both SVG-root checks before opening outputs. | Unauthenticated/cross-origin or traversing writes and invalid/incomplete pairs are refused; normal output names remain. |
| T04 | Propagate capture subprocess failures/diagnostics, refuse stale-frame reuse, verify both shot dimensions/focus, dispose capture resources and stop failed pointer placement before clicking. | Failed captures cannot masquerade as successful old figures, and placement failures cannot produce unintended mouse input. Pair/direct-shot defaults are documented accurately. |
| T05 | Detect compiler-generated record-struct signatures including private PrintMembers. | Generated declarations/indexes identify 19 record structs correctly instead of ordinary structs and omit incidental equality-interface presentation. |
| T06 | Verify the font license before creating a downloadable archive. | A non-strict documentation build cannot emit an unlicensed font bundle merely because the warning is nonfatal. |
| T07 | Generate Linux desktop-entry Exec lines with correct argument escaping/printf replacement rather than injecting paths into sed replacement syntax. | Extraction paths containing ampersand, percent, dollar, quotes/backticks/backslashes are not corrupted; an isolated real Bash installer run verifies the generated entry. |
| T08 | Render each generic declaration's actual CLR/base/interface and nullable-context constraints, including nearest-context notnull inheritance. | Twelve public generic types and two methods show their required constraints. Implied ValueType/new() under struct are not redundantly printed. No generic nullability/decompiler redesign was attempted. |
| T09 | Synchronize Linux release installation commands with the corrected Desktop diagnostic/manual. | apt/dnf/pacman instructions include Xrandr, Xi and Xcursor packages; no packages were installed during this check. |

Documentation/simplification changes were unnumbered in the tooling audit:

| Scope | Action and result |
|---|---|
| API help | Check mode says generation is in memory, not a temporary folder; Ticket lists both net10.0 and netstandard2.1. |
| Capture README | Uses current --no-embed invocation, embedded demo location, dimensions and failure behavior. |
| Diagram README/module | States that its palette mirrors theme tokens rather than claiming it parses XAML. |
| Reflection/Python cleanup | Remove one identical reflection branch, an unused renderer dictionary and one unused import. No framework extraction or broad code-style rewrite. |
| CI | Add tool regressions and actual standalone H01 live/disposed callback probes on Windows/Linux. Local tests do not claim that hosted jobs have already run. |
| Reviewed unchanged tools | Demo drawing/build-check/contact sheet, icon generation/extraction/icon drawing, capture crop/label, site notification, shared warning/pin/SDK/source configuration were checked without regenerating art or changing dependencies. |

Tool evidence: Release surface build passed with zero warnings/errors; the real reflection dump
regenerated all 269 API pages, then check/lint passed. The diff includes 19 record-struct declarations,
12 generic type and two method constraints plus indexes. All 14 Python scripts parse as AST and both
workflow files parse as YAML. The 19-case tool selection passed 18 and skipped one symlink case
because this Windows account cannot create directory symlinks; Linux CI owns that case. Actual
PowerShell guard scripts, loopback HTTP boundaries and isolated Git Bash installer paths ran.
Strict MkDocs passed, and its downloadable font bundle contains eight original TTFs plus OFL.txt
with matching bytes. Material's existing upstream migration notice is not a strict-build warning.

The first wrong-width probe reached the old real screen-copy path and created `unused.png`.
Ownership was identified and that artifact removed. The harness now intercepts bitmap allocation
before screen access and uses a temporary working directory. This setup mistake is not claimed as
an intended native capture test.

## Verification interpretation and deliberate differences

| Boundary | What the evidence establishes | What it does not establish |
|---|---|---|
| Seeded generation | Repeated 470-file comparisons preserve sampled images, metadata, DNA, rarity, indexes, extracted Menu data, seeded renders and model bytes across the relevant corrected checkpoints. | Exhaustive equality for every possible cookbook/seed/size. |
| Culture and threads | Direct 1/4-thread comparisons match 98 demo and 38 fixture files; explicit four-culture checks cover the cited reports/writers. Samples add actual English/French entry-point checks. | Execution on another CPU architecture or a blanket inference from culture tests to all native platforms. |
| ZIP/container bytes | Extracted payloads are compared; data files retain the guarantees above. | Equality of build/filesystem/ZIP timestamps or whole release archives containing those timestamps. |
| Count/probability text | Corrected exactness, endpoints, tiny values, precision uncertainty and demo totals are intentional. | A claim that all formerly displayed counts were correct or that preview chips equal legal uniqueness. |
| Image/model output | Ordinary accepted generated payloads are preserved in sampled comparisons. | Pixel equality for preview defects intentionally fixed by watertight coverage/UV corrections. |
| Seal/authentication | New headers have unambiguous versioned authentication; recorded old seals still open. | Retrofitting stronger authentication onto historical ambiguous labels or requiring old readers to open new authentication versions. |
| Failure behavior | Unsafe/malformed/unsupported inputs now fail clearly; recoverable failed writes retain prior groups or named backups. | Multi-file atomicity across power loss/process termination, or interprocess exclusion after preflight. |
| Resource ownership | Allocation, delayed-callback, held-writer/picker, failure-adoption and explicit disposal cases cover demonstrated lifetimes. | A generic guarantee that every third-party native implementation releases at exactly the same instant. |
| Rendering | Captured names, hashes, changed-region classifications and controlled same-binary checks provide concrete frame evidence. | Treating markup inspection as a rendered proof or classifying an unexplained difference as harmless noise. |
| External exported models | Three fresh GLB/glTF exports open in an independent three.js viewer, visually preserve the checked orientation/opacity/edges/depth, and have zero Khronos errors/warnings. | A general glTF importer, every third-party viewer, or every possible exported model. |
| Hosted CI | The workflows now contain additional reviewed tool/H01 gates and parse locally. | That GitHub-hosted jobs have run successfully before their actual status is observed. |

Protected invariants remain: ImageSharp stays pinned at 3.1.11; frozen fixture archives were not
regenerated; archived design records were not edited; depth was not inserted into DNA; no stored
integer layer-depth field was added; source art was not resampled; new layout colors were not
invented; tests use temporary state rather than real recents, registry or credentials.

## Explicit deferrals and platform limits

The following were deliberately left out rather than silently counted as finished:

- Ticket S04/S05: unmeasured color preparation and DNA-allocation optimization.
- Core S08 and the optional footprint traversal consolidation: unresolved policy differences and no
  necessity for the fixes.
- Model solid-mask composition and off-screen DDA proposals: no measurement/preservation proof.
- Existing BACKLOG proposals, including GPU/performance work, large-image Rise lag, walker
  consolidation and more pinning providers. The external-viewer validation entry is now marked
  completed; stale viewer/file-association backlog descriptions were also reconciled by the
  coordinator with shipped behavior.
- Native macOS Keychain replacement, real Linux keyring failure, native mixed-DPI movement, live
  cloud upload and a second CPU architecture were not executed by the
  recorded project-stage tests. Managed status-policy and pure geometry checks are identified as
  such. Final native release checks, if subsequently executed, must be recorded specifically.
- No live account pins/credentials/site dispatch were used to test provider policies. HTTP/native
  boundaries used stubs, synthetic secrets or isolated interfaces.
- Tool review did not run a real Linux desktop installation, interactive browser Mermaid rendering
  or fresh demo/icon drawing. The diagram CDN dependency and interactive timeout remain visible
  developer-tool constraints.

The historical per-stage gates provide a traceable progression: 4,851 solution passes after
Ticket; 4,979 after Core; 5,148 after Model; 5,228 after CLI; 5,231 after canonical LF; 5,335 after
App. Each had zero failures and one Windows-host Linux-only secret-tool skip at its completed
checkpoint. These totals are successive snapshots, not numbers to sum. The final source includes
the remaining Desktop/sample/tooling work, so only the final full solution result recorded at the
top of this report should be used as the release gate.

## Evidence index

The findings above were reconciled against the stage reports, completed implementation checklists
and verification reports under `docs/audit/01-ticket*` through `09-tooling.md`, plus
`06-release-gates.md`. Those files preserve original reproductions, independent mathematical
checks, red/green logs and historical approvals. This report explains each action rather than
substituting links for the result.

Detailed local artifacts are retained under ignored `artifacts/audit/`, including payload hashes,
frame inventories, native-boundary probes, failed-before evidence and final logs. In particular,
Desktop's `desktop-final-focused.log`, `desktop-frame-comparison.csv` and
`desktop-source-freeze.csv` separate the final fixed source from the disclosed stale-mutant run.
Final local, hosted-CI, package and publication evidence is retained under
`artifacts/audit/release-2.4.8/audited-final/`. The additional 2.4.9 tasks above remain explicitly
open until their own checks and publication finish.
