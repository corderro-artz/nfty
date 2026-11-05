# Stage 2 — Nfty.Core audit

Status: both review passes complete; implementation approved by the user on 2026-11-05. This report records the pre-fix review; subsequent changes and verification are tracked in [Core implementation](02-core-implementation.md). No application code, committed tests, dependencies, or fixtures were changed during the review itself.

Reviewed commit: `95eb1e8` on `codex/ticket-audit-fixes`, based on current upstream `5dece661785a75990576f4016eb796558ba75fd4`. Stage 1 is locally committed and tagged `audit-ticket-verified-2026-11-05`. Nothing has been merged or pushed.

## Coverage and evidence

All 117 production C# files were reviewed for implementation and XML contracts, with related tests and relevant callers:

| Area | Files | Review coverage |
|---|---:|---|
| Formats | 16 | Archive/schema boundaries, JSON, nested lifetimes, validation, loaded ownership, peeking, projection |
| Editing | 32 | Graph edits, rules, drafts, paint/history, selection, layer order, depth surfaces |
| Generation | 5 | Rolling/render split, streaming, cancellation, disposal, uniqueness and counting |
| Stats | 9 | Selection probabilities, color/trait predictions, rarity, reports and voxel statistics |
| Imaging | 15 | Decode restrictions, ICC extraction/conversion, colorization, composition, preview, stamping, opacity |
| Output | 13 | Set readers/writers, metadata, indexing, rollback, layout and legacy extension |
| Publish | 22 | Export planning/writing, naming, Menu production, sealing and IPFS orchestration |
| Shared helpers, Demo, Diagnostics | 5 | Parallel execution/error behavior, formatting, demo generation and profiler |

Fresh verification on 2026-11-05:

- `dotnet build nfty.sln --no-restore`: zero warnings and errors.
- `dotnet test tests/Nfty.Core.Tests --no-build --no-restore`: 1,622 passed, no failures or skips.
- `python tools/api-docs/build.py --lint`: clean.
- `python tools/api-docs/build.py --check`: all 268 generated pages current.
- Additional focused existing test runs passed: Formats/Editing 300; Generation/Stats 232; Publish/Output 352. These overlap the full Core run and are not additional distinct tests.
- Local probes reproduced the findings below. IPFS probes use a stub; no live upload or unpin requests were sent.

Ignored local evidence: `artifacts/audit/core-review-build.log`, `core-review-tests.log`, `core-formats-probe/{Program.cs,results.txt}`, `core-publish-probe/{Program.cs,output.txt}`, `core-imaging-probe/{Program.cs,output.txt}`, and `core-generation-probe.txt`. The generation probe source is in `%TEMP%/nfty-core-generation-audit-20261105/Program.cs`.

Passing existing tests establishes the baseline; it does not disprove the uncovered boundary cases. No output-equivalence claim is made for future Core changes.

## Pass 1 — correctness and resource behavior

22 findings: three P1, sixteen P2, three P3. P1 denotes possible disclosure, destination escape, or destructive external-state cleanup. P2 denotes incorrect results, invalid persisted state, contract failure, or a resource leak. P3 denotes lower-impact boundary behavior; C21 is explicitly latent.

### C01 — P1: folder re-export retains excluded private contents

**Location:** `Publish/SetExporter.cs:966–975`.

Exporting FullProject and then Marketplace to the same destination overlays the new plan onto the old folder. The old CookBook, `set.json`, and `nfty/` survive although the Marketplace plan excludes them. The probe reports all three remaining while `CarriesCookBook=False`. Sharing that folder can disclose the excluded project and seed.

**Proposed fix:** require a clean owned destination or stage and safely replace an owned export. Define ownership before removing anything; do not delete arbitrary user files. Verify the resulting export contains exactly the planned files after a repeated export and after cancellation. Clean-destination output should remain unchanged.

### C02 — P1: collection name `..` escapes the chosen output folder

**Location:** `Publish/SetExporter.cs:937–941`; destination construction at `:274`.

The filename sanitizer accepts `.` and `..`. A collection named `..` exports into the selected folder's parent; the probe confirms a parent `set.json` was written.

**Proposed fix:** reject or replace reserved leaf components and verify the normalized destination is an immediate child of the chosen root. Preserve collection display names; restrict the filesystem leaf. Test dot names, platform-reserved components, and normalized self-export destinations.

### C03 — P1: failed upload unpins an already existing CID

**Location:** `Publish/Ipfs/MarketplaceUpload.cs:155,198,220–237`.

Every returned CID is treated as acquired by the current operation. Repeating an identical art upload and then failing metadata upload causes rollback to unpin art already supporting a previous successful drop. A stub with a pre-existing pin reproduced the removal.

**Proposed fix:** track pin acquisition/ownership and undo only pins acquired by this operation. Extend the provider seam without unnecessarily breaking existing callers. Test pre-existing pins, repeated CIDs within a run, new pins, failure and cancellation. Successful output/CID identity must remain unchanged.

### C04 — P2: failed packing leaves an enlarged manifest after asset rollback

**Location:** `Output/SetWriter.cs:139–145,233–241`; `Output/WriteRollback.cs:55–58`.

An extend writes the new manifest and index before packing. If Pack fails, rollback removes newly added assets but preserves the overwritten manifest. Creating a directory at the expected `<leaf>.set` path reproduces a readable Set with `Manifest.Count=2` and only one item.

**Proposed fix:** restore the prior manifest after failure and invalidate or restore the cache; stage packed output before replacing an existing archive. Cover sync and async failure after manifest writing. Preserve the author's working folder and the documented limitation that canceled extension can leave older items' rarity regraded.

### C05 — P2: seal header authentication has an ambiguous encoding

**Location:** `Publish/SealManifest.cs:91–107`; MAC use in `Publish/Seal.cs:293–294`.

Unescaped `|` delimiters make distinct authenticated headers identical. Collection `A|1`, count `2`, note `C` has the same canonical bytes as collection `A`, count `1`, note `2|C`. Rewriting only these fields is accepted by `ExtractTo`, with the original payload intact.

This proves collection/count/note tampering, not payload decryption or policy bypass.

**Proposed fix:** version the authenticated encoding and use unambiguous fields. Do not silently change `/1` MAC bytes. Existing seals must remain readable under an explicit legacy policy; historical ambiguous headers cannot acquire stronger authentication retroactively. Test the collision and normal old/new seal round-trips. New seal bytes will intentionally differ; this requires a compatibility decision before implementation.

### C06 — P2: additional numbered images overwrite canonical exported PNGs

**Location:** `Publish/SetExporter.cs:711–728`.

Number detection ignores directory and extension. A valid include-list containing `images/0001.png` and `images/hi-res/0001.webp` plans both as `images/0001.png`. The alternate bytes replace the primary PNG and the additional file disappears. `SetLayoutTests` explicitly recognizes such subdirectories as part of a Set.

**Proposed fix:** rename only canonical per-item primary paths, retain other included paths, and reject destination collisions before writing. Test folder and archive exports with additional numbered files and renamed collections. Preserve the whole-directory inclusion contract.

### C07 — P2: malformed collection elements escape reader and Validator boundaries

**Location:** `Formats/Json.cs:32`, `Formats/IngredientArchive.cs:207`, `Formats/Validator.cs:153`.

`variants:[null]` throws `NullReferenceException`; `rules:[null]` loads and then crashes Validator; `layerOrder:[null]` loads and then throws `ArgumentNullException("key")`. Nullable annotations do not validate nested JSON elements. Validator's report-without-throwing contract also applies to directly constructed graphs.

**Proposed fix:** reject malformed nested elements with the existing archive corruption boundary and return validation problems for malformed in-memory graphs. Cover sync/async readers and direct Validator calls without regenerating historical fixtures.

### C08 — P2: undefined enum values pass validation and alter semantics

**Location:** `Formats/Validator.cs:152,396,437`.

Undefined numeric `LayerKind`, `ColorModel`, and `RuleType` values deserialize and pass validation. Probes show an unknown rule generating successfully while applying no constraint, an unknown color model falling through to a supported model, and a grayscale unknown layer kind validating.

**Proposed fix:** report undefined values at the legality gate, including in-memory graphs. Preserve any historically supported numeric representations of defined values. The framework converter allows integer values by default; this is documented behavior, so nullable/static typing cannot close the gap. [Microsoft converter documentation](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.serialization.jsonstringenumconverter.-ctor?view=net-10.0).

### C09 — P2: finite weights can overflow their total

**Location:** `Formats/Validator.cs:72,333,426`.

Two `double.MaxValue` variant weights produce no validation problems, then generation throws `Absent weight must be a finite number, zero or greater, but it is NaN.` Individual finite checks do not establish a finite recipe/variant/color-table total.

**Proposed fix:** reject nonfinite totals consistently for all three tables before projection/generation. Preserve ordinary roller arithmetic and RNG consumption. Test aggregate overflow separately from individual infinity/NaN inputs.

### C10 — P2: deleting a referenced variant persists a broken recipe

**Location:** `Editing/CookBookEdits.cs:243`; GUI caller `Nfty.App/ViewModels/ExplorerViewModel.cs:1546`.

`RemoveVariant` leaves trigger/target rules referencing the removed variant. The GUI validates only the resulting ingredient before persisting, so deletion can save a recipe that can no longer generate. The probe returns `rule references unknown variant 'a'`.

**Proposed fix:** refuse removal while referenced, naming the affected rule. Cascading rule deletion would be a separate behavior choice. Test trigger and target references and verify the GUI presents the refusal. No changes to unrelated rule semantics or archived identities.

### C11 — P2: asynchronous Kitchen creation overwrites existing files

**Location:** `Formats/KitchenArchive.cs:56`.

Sync creation uses `CreateNew`; async uses `Create`. The async method overwrites an existing Kitchen, and a pre-canceled call can truncate it before reporting cancellation. The probe's subsequent read reports a corrupt local header.

**Proposed fix:** match sync creation semantics and check cancellation before acquiring a destructive handle. Test existing destinations and byte preservation under pre-cancellation. Document the no-overwrite contract.

### C12 — P2: a guaranteed selection is reported Unknown

**Location:** `Stats/SelectionOdds.cs:233,315,323`.

Recipe weight `3`, hat weights `a:7,b:3`, background `x:1`, and an exclusion of `b/x` leave only `a/x`. Its true probability is 1. Denominator arithmetic yields `0.6999999999999998`, numerator `0.7`; the resulting value above 1 is rejected, returning probability 0/Unknown.

**Proposed fix:** reconcile numerator/denominator arithmetic and permit only bounded floating-point error at the final boundary. Preserve established accumulation order and reject genuinely unmappable selections. Test guaranteed outcomes with fractional weights. No DNA or generated-pixel change.

### C13 — P2: streaming progress callback failure leaks an image

**Location:** `Generation/Generator.cs:296`.

A synchronous `IProgress<GenerationProgress>.Report` throws after rendering but before `yield return`. The caller never receives the asset. Disposing the enumerator still leaves one extra ImageSharp undisposed allocation in the probe.

**Proposed fix:** dispose the asset when pre-yield reporting fails, then rethrow. Ownership transfers on a successful yield. Test both callback failure and caller-owned successful yields using allocation diagnostics.

### C14 — P2: a reachable positive chance prints `0%`

**Location:** `Stats/SelectionOdds.cs:157–160`.

`Describe(new SelectionChance(1e-10, Exact), false)` prints `0%`. Ordinary sufficiently deep weighted stacks reach this range. Six fractional places do not guarantee a nonzero representation.

**Proposed fix:** use a positive floor marker or scientific notation below display precision, with correct wording for bounds. Test both sides of the threshold and exact zero. This intentionally corrects displayed text only.

### C15 — P2: a valid narrow saturation range produces NaN color shares

**Location:** `Imaging/ColorName.cs:123–136`.

Saturation endpoints `1.5640000000000631` and `1.5640000000000633` are distinct but become equal after division by 100. The raw-endpoint equality test misses the collapsed sampled interval; division by its zero width produces 25 NaN probability entries. HSV hue `0..0`, quantization `1/1` validates without problems.

**Proposed fix:** recognize collapsed intervals in sampled coordinates and handle them as point masses consistent with `ColorRoller`. Verify finite, normalized shares and predicted/cooked naming agreement. Preserve the existing RNG and color sampler.

### C16 — P2: allocation measurements subtract unrelated thread counters

**Location:** `Diagnostics/Perf.cs:71,148`; real async scopes in `Output/SetWriter.cs:297–304`.

A scope stores the creating thread's allocation counter and subtracts it from the disposing thread's counter. Async work can change threads; the probe records `-1685496` allocated bytes after deliberate thread transfer. These figures cannot support optimization decisions.

**Proposed fix:** restrict allocation accounting to compatible synchronous scopes or explicitly mark the allocation metric unavailable after a thread change; retain wall time for async scopes. Do not substitute a process-wide counter and call concurrent allocations scope-owned. Test thread transfer and same-thread measurement. The runtime API measures allocations of the current thread. [Microsoft API documentation](https://learn.microsoft.com/en-us/dotnet/api/system.gc.getallocatedbytesforcurrentthread?view=net-10.0).

### C17 — P2: profiler reports depend on locale

**Location:** `Diagnostics/Perf.cs:120–124`.

The same snapshot formats decimal points under `en-US` and decimal commas under `de-DE`. Numeric `ToString` calls omit the invariant provider despite XML and repository report guarantees.

**Proposed fix:** format all numeric columns with `InvariantCulture`. Compare one fixed snapshot under both cultures. Wall-time values naturally vary between runs; the probe compares the same stored measurements.

### C18 — P2: non-outer cancellation exceptions violate parallel error determinism

**Location:** `ParallelWork.cs:61,70,144,153`.

The helper excludes every `OperationCanceledException` from indexed failures. A callback throwing one while the operation token is not canceled produces the original exception sequentially and `AggregateException` in the parallel `For` probe. With mixed failures it can also evade lowest-index selection.

**Proposed fix:** distinguish cancellation of the operation token from an ordinary callback failure, recording the latter by index. Cover sync/async loops, mixed low/high-index failures, and genuine outer cancellation. Preserve sequential behavior as the oracle. The framework's wrapping of unrelated cancellation is documented; nfty's helper promises to normalize it. [Microsoft parallel cancellation documentation](https://learn.microsoft.com/en-us/dotnet/standard/parallel-programming/how-to-cancel-a-parallel-for-or-foreach-loop).

### C19 — P2: extending a legacy Set loses absence rarity

**Location:** `Output/SetWriter.cs:577–578,807–808`.

For metadata shape 1, absence lives in rich rarity/`AbsentLayers`, not OpenSea attributes. Existing-item tally and regrade read only those attributes. A genuinely legacy-shaped probe loses `Hat:(none)` from item and collection rarity after extension while retaining `AbsentLayers=["Hat"]`.

**Proposed fix:** include legacy absence outcomes once during tally/regrade. Preserve legacy published OpenSea bytes, metadata shape, numbering and DNA. The existing compatibility test removes `metadataShape` from modern metadata but retains modern attributes, so it misses the historical case. Add a true shape-1 regression without changing frozen fixtures.

### C20 — P3: wrong JSON identity type escapes the corruption exception

**Location:** `Output/SetItemFile.cs:70–71`.

`{"dna":"x","setNumber":"oops"}` throws raw `InvalidOperationException` from `TryGetInt32`, with no filename context.

**Proposed fix:** check Number kind before conversion and raise the existing `CorruptSetException`. Cover sync/async reads and index fallback. Valid metadata behavior remains unchanged.

### C21 — P3, latent: nested async archive callback outlives its ZIP

**Location:** `Formats/ArchiveIo.cs:174,310`.

`ReadInner` disposes a nested archive while the returned callback task can still be pending. A deliberately delayed callback deterministically throws `ObjectDisposedException`. Current production callbacks generally finish synchronously over memory; no ordinary public-input failure was established.

**Proposed fix:** give the async helper an explicit archive lifetime spanning `await`, retaining the existing corruption/error framing. Test a genuinely incomplete callback. This is an internal lifetime defect, not evidence that current normal archive reads fail.

### C22 — P3: counting budget equality is mislabeled as strictly more space

**Location:** `Generation/UniqueSpace.cs:491`, reaching `SpaceText.Describe`.

With one reachable dynamic-color bucket and `enumerationBudget:1`, counting returns `(1,false)`, displayed as `more than 1`, although the actual total is exactly 1. The cutoff occurs at equality before discovering another distinct bucket. Fixed-only entries also do not enforce the budget consistently.

**Proposed fix:** establish a distinct bucket beyond the budget before asserting a strict lower bound, consistently across entry types/unions. Test exact-budget totals, overlapping ranges and mixed fixed/ranged entries. This boundary uses a nondefault budget; ordinary default counting is unaffected.

## Pass 2 — inline XML documentation

Public summaries and generated-page lint pass. Completeness at the compiler level does not establish semantic accuracy. Correct the following contracts after approval and regenerate API pages; do not hand-edit generated pages.

| ID | Location | Correction |
|---|---|---|
| D01 | `Generation/UniqueSpace.cs:62,170` | Optional-layer space is the sum of each legal selection's bucket product, not universally combinations multiplied by one bucket total. |
| D02 | `Generation/UniqueSpace.cs:67` | `RecipeSpace.Combos` can contain unconstrained combinations on budget fallback; qualify legality and certainty. |
| D03 | `Generation/GenerationExceptions.cs:20,34,47` | Retry exhaustion need not prove the unique space is exhausted; correct class and `Available`/`Produced` descriptions. |
| D04 | `Generation/Generator.cs`, `GenerateStreaming` | Add the cancellation exception contract. |
| D05 | `Generation/GeneratedSet.cs:112` | Document ordinary `InvalidOperationException` when rule rerolls fail with uniqueness disabled. |
| D06 | `Output/Metadata.cs:52` | `AbsentLayers` contains layer display names, not IDs. |
| D07 | `Output/Metadata.cs:82` | Reproduction requires compatible uniqueness/options as well as book and seed; align with `UniqueDna`. |
| D08 | `Publish/Ipfs/MarketplaceUpload.cs:101` | Describe cancellation rollback and distinguish pre-existing pins from newly acquired pins after C03. |
| D09 | `Publish/SetExporter.cs:394` | A stamp prefers `ExportEntry.SetNumber`; filename parsing is a fallback. |
| D10 | `Publish/Seal.cs:114`, `SealManifest.cs:32` | Align the MAC/truncation explanation with current framing; document version/legacy policy after C05. |
| D11 | `Publish/ExportOptions.cs:166` | An export without `set.json` can reopen as a read-only MarketplaceBundle; qualify the limitation. |
| D12 | `Output/SetLayout.cs:66,137` | Relative input paths produce relative paths; remove the absolute-path guarantee unless normalization is intentionally approved. |
| D13 | `Formats/Peeked.cs:22,28,44` | Loaded-object projections preserve supplied order; an ordinal guarantee depends on the producer. |
| D14 | `Formats/MenuProjection.cs:11` | Loaded projection scans whole art for partial alpha; its cost is not merely a cheap manifest copy. |
| D15 | `Formats/ArchivePeek.cs:28,35,42,59,77`, `CookBookArchive.cs:91` | Schema errors include unsupported versions below the floor, not only newer schemas. |
| D16 | `Editing/DepthSurface.cs:158` | `Overwrite` forces whole-canvas invalidation, including identical bytes; qualify `TakeDirty`'s changed-pixels wording. |
| D17 | `Formats/Loaded.cs:19` | Depth coverage can equal the number of variant images; remove the strict cardinality claim. |
| D18 | `Editing/RuleEdits.cs:94`, `Formats/Validator.cs:254` | Authoring and Validator do not perform identical checks: empty/duplicate targets and cross-rule contradictions differ. State each responsibility. |
| D19 | `Editing/CookBookEdits.cs:197` | `RemoveIngredient` returns a new graph; the caller retains ownership of the removed subtree rather than receiving it as the return value. |
| D20 | `Imaging/ImageFormats.cs:17–23` | `SkipMetadata` does not prevent all ICC bytes being retained; explain lazy parsing and the typed-load `NoIcc` step, and distinguish metadata-preserving overloads. No newly demonstrated advisory exploit. |
| D21 | `Imaging/ColorName.cs:68–90`, `Generation/UniqueSpace.cs:476,500` | Distinguish mathematical half-open uniform intervals from floating-point sampling; avoid promising exact endpoint/probability behavior the sampler cannot provide. Align with Ticket's corrected `ColorRange` XML. |
| D22 | `Diagnostics/Perf.cs:14,106,144` | Describe allocation/thread restrictions and actual invariant report behavior after C16/C17. |

## Pass 2 — simplification proposals

These are bounded proposals, not authorization to rewrite working algorithms. S01–S07 can preserve behavior; S08 needs an explicit validation policy because the current checks differ.

| ID | Location | Smaller implementation and preservation check |
|---|---|---|
| S01 | `Output/SetWriter.cs:104,111,194,196` | Build `SlotsOf(set)` once per operation and share it for indexing/skipped slots. Compare files and progress at 1 and multiple threads. |
| S02 | `Publish/SetExporter.cs:405` | Remove unused internal `StampAsync.setDir` parameter and its documentation. Build/caller check is sufficient. |
| S03 | `Publish/MarketplaceFiles.cs:21,27` | Remove unused internal `ExportNaming.IsDefault`/`Stem` wrappers after confirming no consumer. Retain the actual naming implementation. |
| S04 | `Generation/UniqueSpace.cs:419,434` | Remove unused `ReachableLayer.Id` and return the reachable variant list directly. Preserve filtering, deduplication and order; rerun counting cases. |
| S05 | `Formats/IngredientArchive.cs:62` | Scan the existing depth byte buffer for nonzero values and return it directly instead of constructing, scanning and cloning a `DepthMap`. Removes two full-buffer copies; prove masking and flat-map equivalence. |
| S06 | `Imaging/ColorName.cs:53,169` | Reuse the existing sector calculation for `Of` and point-mass shares. Preserve all hue wrapping/boundary tests; keep the finite-input guard. |
| S07 | `Imaging/Opacity.cs:49,67,79` | Express repeated existential scans with the existing `HasPartialAlpha` overloads and short-circuit `Any`, if clearer under repository style. Preserve short-circuiting and borrowed-image ownership. |
| S08 | `Editing/RuleEdits.cs:101`, `Formats/Validator.cs:263` | Consider shared nonthrowing structural-rule diagnostics with a throwing authoring wrapper; retain cross-rule checks separately. Decide empty/duplicate-target legality first; do not disguise stricter acceptance as a pure refactor. |

One further candidate, reusing `Footprint.Box` in `DrawShape.cs:28` and `MoveSelection.cs:28`, could remove duplicated clipped row-major traversal. It should remain optional until overlap/precedence and allocation behavior are compared; it is not needed to fix an identified defect.

No speculative hot-path rewrite is recommended. Rolling must remain sequential, image/result ownership explicit, and ordinal/invariant output stable. Existing lookup tables, preview delegation to Ticketer, and sequential aggregate calculations already serve these constraints.

## Deferred and excluded

- Declared backlog: eager decoding, Validator parallelism, IngredientArchive async caller-thread decoding, ImageSharp oversubscription, and the shared `UniqueSpace`/`SelectionOdds` walker.
- ZIP entry timestamps intentionally vary; compare archive entry contents for preservation, not whole ZIP bytes.
- The documented same-size Set index cache limitation is not a new finding.
- Prepared `BookOdds` retains caller-owned weight/rule collections despite immutable wording; no shipped mutation path was established. Do not count it as a confirmed runtime defect.
- No package upgrade, fixture regeneration, archived-design edit, broad formatting sweep, or schema redesign is proposed.
- Context7 is not exposed in the available tool catalog. Runtime details above were checked against official .NET documentation and local probes.

## Approval and implementation boundary

Stop here for the user's review. Recommended approval scope: C01–C20, D01–D22 and S01–S06; explicitly include or defer latent C21, nondefault-budget C22, and optional S07/S08/traversal consolidation. C05 needs a seal version/legacy policy before code changes.

After approval, dispatch separate implementation owners for archive/editing validation, generation/statistics/imaging, and publishing/output. Use a cheaper model for straightforward fixes; isolate the seal and pin-ownership work for careful review. Keep this worktree and branch isolated; no merge or push.

Before completion, require zero-warning build, full solution tests, regenerated API lint/check, focused failure/cancellation/ownership regressions, legacy fixture reads, and comparison with the reviewed commit. Ordinary valid PNGs, DNA, order, RNG consumption, metadata and archive payloads should remain identical except for explicitly approved corrected cases. Verify affected CLI/GUI workflows from actual application output; render captures for any GUI changes. Re-run the entire solution once after integration rather than repeatedly running unrelated suites per small edit.
