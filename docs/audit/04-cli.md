# Stage 4 — Nfty.Cli audit

Status: both review passes complete on 2026-11-06; the user subsequently approved implementation. Fixes, XML corrections and bounded simplifications are complete and [verified](04-cli-verification.md). The user accepted verification and approved App review on 2026-11-06. Baseline: `95775d6`, locally tagged `audit-model-verified-2026-11-06`. No production code, committed tests, fixtures, dependencies or archived designs changed during the review-only phase. CLI checkpoint: `cf67cce`, local tag `audit-cli-verified-2026-11-06`; nothing has been merged or pushed.

## Coverage and baseline

All 12 production C# files, their project wiring and 28 companion test source files were reviewed. The production sources contain 3,885 lines; companion test sources contain 5,726 lines, including assembly configuration.

| Owner scope | Production files | Review coverage |
|---|---:|---|
| Main, model and process entry | 3 | CommandFactory.cs, CommandFactory.Model.cs, Program.cs; generation, extension, inspection, preview, Menu serving, model output and error entry |
| Authoring and sheets | 4 | CommandFactory.Authoring.cs, CommandFactory.Parity.cs, CommandFactory.Sheet.cs, ManifestFile.cs; editing, JSON inputs, image ownership, ordering and PNG grids |
| Publishing and shared boundaries | 5 | CommandFactory.Publish.cs, CommandFactory.Ipfs.cs, Passphrase.cs, ErrorReport.cs, CliGuards.cs; export modes, dependent flags, credentials, lifetime and diagnostics |

Two delegated `gpt-6.1-sol` reviewers completed separate correctness and XML/simplification passes. An independent follow-up confirmed the main output/overflow findings against source and saved executable probes.

- Fresh Release solution build: **zero warnings/errors**.
- Fresh Release CLI suite: **331 passed**, zero failures/skips.
- Focused authoring and publishing runs: 82 and 93 passed respectively; these overlap the 331-test baseline, not additional tests.
- Actual executable probes reproduced the findings below. Probes and synthetic inputs remain in ignored `artifacts/audit/`; no fixture was regenerated and no live pinning request was made.
- Core, Ticket and Model algorithms were not changed. Existing tests cover ordinary generation/thread parity, Menu-to-generation rendering parity, recipe modeling, culture parsing, editing and export behavior. Passing ordinary paths do not cover the failures below.

## Pass 1 — confirmed findings

Nine findings: **seven P2**, **two P3**. No P0/P1 finding was established. P2 denotes incorrect work, damaged output, substantial unexpected work or a broken error/security boundary; P3 denotes a lower-impact diagnostic/reporting contract error.

### L01 — P2: malformed options escape the parser as unhandled exceptions

**Locations:** `Program.cs:5`; `CommandFactory.Model.cs:118,138,219`; `CommandFactory.Publish.cs:201,228`; `CommandFactory.Authoring.cs:391,547,657,810`; `CommandFactory.Parity.cs:48,195`.

The command line is parsed before the process exception handler. Validators read typed values after conversion has failed, and the model format validator indexes an empty token list. Examples:

```text
model x.png --out x.glb --format
model x.png --out x.glb --front abc --back 0
model x.png --out x.glb --voxel-size abc
export s --out o --sheet --sheet-cols abc
set chance cat.rcp --id hat --absent abc
move recipe book.cbk --id cat --to abc
```

Actual: unhandled `ArgumentOutOfRangeException` or `InvalidOperationException`, full framework trace without `--verbose`, and abnormal process termination rather than the documented exit 1. On this Windows host, representative model failures returned 3762504530. Custom numeric parsers calling `AddError` do not make later typed getters safe.

**Remedy:** guard existing conversion errors and missing tokens before typed reads; preserve the parser's original diagnostics. Include parsing in the process error boundary as a backstop without consulting an unavailable parse result. Audit the same pattern across all validators rather than fixing only the first example.

**Verification:** actual-process malformed/missing/overflowing numeric and token cases across model, export and authoring must exit 1, name the option, produce no ordinary stack trace and write nothing. Valid parsing and help must remain unchanged.

### L02 — P2: single-model output can corrupt an existing glTF/BIN pair

**Location:** `CommandFactory.Model.cs:318–326`, `WriteModel`.

Write a valid model from a 2×2 opaque picture, retain a read-only sharing lock on its existing `.bin`, then model a 16×16 picture to the same `.gltf` destination. The command replaces the JSON first and fails writing the BIN. Actual: exit 1; new JSON declares **10,132 bytes**, while the surviving old BIN contains **1,596 bytes**. The original readable pair is damaged despite reporting failure.

**Remedy:** stage and commit the complete model with rollback, preserving the requested output extension's casing. Reuse or narrowly extract the safe `ModelDestination.Folder`/`FolderSink.Add` mechanism. `ModelBatch.WriteFolder` is also a direct-write convenience method and is not a safe substitute. Do not introduce another generic batch framework.

**Verification:** failure installing either file preserves both prior payloads; successful `.GLTF` casing, sibling BIN naming, all three formats and model bytes remain unchanged. Exercise secondary restoration failure without deleting recoverable backups.

### L03 — P2: generate silently merges stale assets and can publish duplicate DNA as unique

**Location:** `CommandFactory.cs:911–934`, `Generate`.

The command sends a new run starting at asset 1 into the writer's extension-capable destination without checking for an existing Set. Generate four assets, then generate two with a different seed to the same folder. Actual: success reports two generated, but four assets remain; the tail belongs to the old run and the manifest records the new seed.

A bounded two-outcome probe is stronger: generate two unique assets, then generate one whose DNA matches the retained second asset. Actual: **two assets, one distinct DNA, `uniqueDna: true`**, with both commands using ordinary uniqueness mode. New-run deduplication cannot see the retained tail.

**Remedy:** refuse occupied Set-owned output before rolling a new collection. Allow unrelated Kitchen files; do not blanket-refuse every nonempty folder. Do not change `SetWriter`'s deliberate extension semantics or silently delete an existing collection. Direct the user to `extend` or a fresh destination.

**Verification:** existing Set payloads and metadata remain byte-identical after refusal; fresh and unrelated-file destinations still work. Include the duplicate-DNA case and ensure ordinary new output/thread parity remains unchanged.

### L04 — P2: an already-met extension target can overflow into billions of requested assets

**Location:** `CommandFactory.cs:982–987`, `Extend`.

The code subtracts `target - have` before checking whether the target has already been met. With one existing asset, `--to -2147483648` wraps into **2,147,483,647** requested additions. A tiny two-outcome book with `--max-rerolls 1` confirms that positive count in the failure diagnostic. A separate demo probe performed substantial generation instead of returning; it was bounded by a 30-second timeout, with no output-write stage reached.

Already-met absolute targets deliberately return a no-op, as documented by the existing implementation and `Generator.cs:181–182`.

**Remedy:** compare `target <= have` before subtraction and return the existing no-op response. Subtract only for a larger target. Merely adding checked arithmetic would change an existing benign case into an error.

**Verification:** int.MinValue, negative, zero, equal and smaller targets do no generation or writes; a larger target generates exactly the difference. Keep ordinary extension DNA/model behavior unchanged.

### L05 — P2: a supplied sealing key is silently ignored for an unsealed export

**Location:** `CommandFactory.Publish.cs:361–363`.

```text
export <set> --out <fresh-directory> --key env:NFTY_AUDIT_NOT_SET
```

Actual: exit 0, ordinary readable plaintext `.set`; the nonexistent key source is never checked. The CLI reference explicitly promises that dependent options are refused rather than ignored. Supplying a key must not appear to request protection while silently producing an open archive.

**Remedy:** reject an explicitly supplied `--key` unless the effective export options enable sealing. Check the effective preset/overrides, so `sealedcritique` remains valid without a redundant `--seal`.

**Verification:** refused commands write nothing; explicit sealing and sealed presets still consume their source and produce authenticated sealed output. Do not make `--key` silently enable sealing.

### L06 — P2: invalid credential-source errors repeat potentially secret input

**Location:** `Passphrase.cs:93`.

```text
ipfs status --pinata --pinata-key AUDIT_LITERAL_JWT_9f2
```

Actual stderr repeats the complete literal `AUDIT_LITERAL_JWT_9f2`. A person mistyping a raw JWT or passphrase into a source option therefore copies it into error logs. Argument/history exposure already happened; echoing it creates an additional disclosure. No live credential or network request was used in the probe.

**Remedy:** explain the option and supported source syntax without including the unrecognized source value. Keep source names and ordinary nonsecret path/variable diagnostics useful.

**Verification:** a synthetic secret marker must be absent from ordinary and verbose process output, including inner exceptions; exit code remains 1 and the source guidance remains clear.

### L07 — P2: opposite content switches silently select one result

**Location:** `CommandFactory.Publish.cs:219–226,257–262`.

```text
export <set> --out <fresh-directory> --folder --no-images --images
```

Actual: success with no `images/`; the negative switch wins even though the positive appears last. `--opensea/--no-opensea` and `--nfty/--no-nfty` behave similarly. Models, collection and Set-manifest opposite pairs already reject this ambiguity. No precedence policy was found for the three missing pairs.

**Remedy:** add matching contradictory-pair guards before any export work. Avoid inventing a last-token-wins policy for only part of the surface.

**Verification:** both orders of each pair fail without output. Single positive/negative overrides and presets retain their current contents and bytes.

### L08 — P3: malformed authoring manifests bypass the structural boundary

**Locations:** `ManifestFile.cs:38`; `CommandFactory.Authoring.cs:904–905`.

An Ingredient authoring JSON with valid required fields but `"variants":[null]` deserializes successfully, then `new ingredient` reaches `DistinctBy(v => v.Id)` and reports a generic null-reference error. The diagnostic omits the input file and offending element. Core archive readers already reject this structural shape; the fresh CLI manifest boundary does not invoke that protection.

**Remedy:** apply the shared manifest structure validation before callers enumerate nested data or load images. Preserve optional-field defaults and the existing schema-version policy; do not build a second JSON parser.

**Verification:** null collection elements and required nested structures produce named invalid-manifest diagnostics, leave no archive and leak no decoded images. Valid hand-authored JSON continues to round-trip through `Json.Options`.

### L09 — P3: one authoring confirmation formats weights using the current culture

**Location:** `CommandFactory.Authoring.cs:741`.

Under `de-DE`, `add recipe ... --weight 0.5` succeeds but confirms `(weight 0,5)` rather than `(weight 0.5)`. Numeric parsing and archive bytes remain correct; the textual report violates the invariant formatting rule.

**Remedy:** format the weight with `CultureInfo.InvariantCulture`; use the existing invariant numeric helpers in nearby summaries where applicable.

**Verification:** confirmations match under dot/comma cultures while resulting archive payloads remain unchanged.

## Pass 2 — XML completeness and correctness

This pass was separate from the correctness review. Public members have summaries; the compiler's successful documentation build does not establish complete parameter/return/ownership contracts. Thirteen grouped documentation items follow.

| ID | Location | Required correction |
|---|---|---|
| D01 | CommandFactory.Model.cs:17,235–241 | Replace the obsolete two-form/Ingredient-only descriptions: modeling also accepts a seeded Recipe. |
| D02 | CommandFactory.cs:22–26 | Describe the actual partial command surface and process error boundary rather than promising a four-line Program. Keep this accurate after L01. |
| D03 | CommandFactory.cs:69–77 | Document ResolveThreads' null-parse exception and clarify requested versus effective parallelism; avoid implying the returned requested value itself was clamped. |
| D04 | CommandFactory.Authoring.cs:14,438; five public group factories | NewGroup's Kitchen form does not require manifest/parts; RemoveGroup now has four subcommands. Add concise return contracts to New/Add/Set/Remove/Move factories. |
| D05 | CommandFactory.Authoring.cs:455 | ParseTarget's XML incorrectly says IDs must be lowercase hyphenated words. State the delimiter restriction without denying supported uppercase/Unicode IDs. |
| D06 | CommandFactory.Parity.cs:8–12 | Its class summary overstates that every edit delegates to a Core edit method; several setters construct graph changes locally. |
| D07 | CommandFactory.Authoring.cs:249,899,931; CommandFactory.Parity.cs:77 | State image ownership/partial-load cleanup for loading helpers and borrowed graph ownership for EditBook callbacks. The original loaded owner is disposed by EditBook. |
| D08 | Pct, RuleLine, InvariantNumber, ParseInvariant | Add missing internal helper parameter/return contracts, including parse fallback/error behavior. |
| D09 | Passphrase.cs:3–25,44–56 | Describe both passphrases and Pinata credentials, per-source first-line/whitespace behavior, nonempty results and propagated I/O/access/prompt-cancellation failures. Qualify process visibility by platform/access permissions. |
| D10 | Passphrase.cs:125–152 | Document injected prompt delegates/writer and key behavior: Enter, Backspace, Escape and ignored controls. Clarify that secret characters are not echoed; prompts/newlines are. |
| D11 | CommandFactory.Publish.cs:431–456 | Explain that a peeked cleartext header is unverified until successful authenticated opening; absent key prints header claims only. |
| D12 | CliGuards.cs:42–58 | Add exception contracts and clarify that absence means null; empty strings count as supplied. |
| D13 | Private command/helper factories across all partials | Fill missing concise intent summaries, without narrating implementations. Includes 19 authoring/parity/sheet helpers; main/model command factories and printers; publishing token helpers; and private-nested PinningOptions/Create/AddTo/Validate/Build, upload/status and progress adapter contracts. WriteModel should describe complete-file commit/error behavior after L02. |

There is no reason to add paragraph-length XML to obvious private plumbing. Ownership, failure and borrowed-lifetime information take precedence. ErrorReport's existing contract did not yield a separate documentation defect.

## Pass 2 — bounded simplifications

| ID | Location | Proposal and preservation boundary |
|---|---|---|
| S01 | CommandFactory.cs:733–734 | Replace per-layer LayerOrder.ToList().IndexOf with existing LayerDepth.DepthOf. This removes repeated list allocations and centralizes depth semantics; do not claim it eliminates the underlying linear search. Preserve drawn order and real depths. |
| S02 | CommandFactory.Parity.cs:292–300 | Replace upsert/rebuild/reorder and repeated First searches with existing CookBookEdits.SetRecipeWeight. It already updates only the weight dictionary and retains Recipes, SourceSha256 and other manifest fields. Preserve missing-ID errors, zero-weight shelving and listing order. |
| S03 | CommandFactory.Parity.cs:337–353 | Reuse CookBookEdits.EditIdentity for its existing identity transformation rather than restating the graph construction. Preserve null versus empty values and optional supply clearing. |
| S04 | CommandFactory.Publish.cs:291,345 | Cache one lazy default SetExporter.Plan result for art-only checks and explicit-model/book checks. Equivalent combined probes currently repeat archive work. No measured speed claim; preserve refusal/warning timing and ensure no decoded owner outlives disposal. |
| S05 | Passphrase.cs:79 | Read one file line with StreamReader instead of loading all ignored trailing content. Preserve BOM detection, CR/LF, leading/interior spaces and empty-first-line behavior. |
| S06 | CommandFactory.Ipfs.cs:61–74,82–125 | Make HttpClient lifetime explicit in each action and pass it into service construction. Dispose on success and failure. This is bounded ownership cleanup, not evidence of a resource-exhaustion incident in a short-lived process. |
| S07 | CommandFactory.cs:588–599,667; CommandFactory.Model.cs:84–110 | Resolve the input form once per validator and reuse the local result; remove the one-use IsRecipePath wrapper. Keep the authoritative Archives mapping and exact form-specific errors. |
| S08 | CommandFactory.Authoring.cs:417,519,633,686,735,852 | Consolidate repeated archive-replacement explanations in WriteReplacing's contract. Retain caller-specific rationale. This is comment simplification, not a behavioral refactor. |

Avoid splitting the command surface into a generic execution/graph-edit framework merely to reduce method length. The separate command forms have different ownership, validation and error timing. No GPU, mesh-wall, probability-walk or other existing BACKLOG deferral is reopened by this CLI review.

## Approval and verification boundary

Suggested implementation scope: L01–L09, D01–D13 and S01–S08, with finite regressions and a final output comparison. Source algorithms for rolling, DNA, colorization, meshing and rarity should remain unchanged. L01/L05/L07/L08 intentionally turn broken or silently ignored inputs into clear failures; L02/L03 protect existing outputs; L04 preserves the already-met-target no-op policy.

Verification should include zero-warning Release build, the whole solution suite, real process error/exit tests, output failure recovery, deterministic byte samples, four-culture formatting and existing rendered captures if shared Model I/O is extracted. Use synthetic secret markers and stub providers; do not contact a live account merely to test these guards.

The review-only approval stop was observed. The user subsequently authorized fixes; the [implementation checklist](04-cli-implementation.md) records completion and preservation refinements. The [final verification report](04-cli-verification.md) records all gates and limits. The user accepted verification on 2026-11-06. CLI is committed as `cf67cce`, locally tagged `audit-cli-verified-2026-11-06`; App review was authorized. No merge or push.

## Local evidence

- `artifacts/audit/cli-review-build.log`, `cli-review-tests.log`
- `artifacts/audit/cli-parser-probe.json`
- `artifacts/audit/cli-pair-52c223/results.json` and `original.gltf.txt`
- `artifacts/audit/cli-main-probes-ba9c0c/results.json`
- The directory named by `artifacts/audit/cli-generate-duplicate-probe-location.txt`, containing commands, matching DNA and manifest evidence
- `artifacts/audit/cli-overflow-09ae32/results.json`, using a tiny book and reroll budget 1
- `artifacts/audit/cli-authoring-probes/probe.csproj` and its inputs; malformed manifests under `cli-authoring-*`
- `artifacts/audit/cli-publish-review-evidence.txt`

All substantive failures above were reproduced before proposing changes. These are finite checks on Windows x64, not a claim that all possible inputs or platforms have been proved correct.
