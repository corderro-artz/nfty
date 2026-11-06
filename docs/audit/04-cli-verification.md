# Stage 4 — CLI fixes and verification

Status: complete, verified and accepted by the user on 2026-11-06. The user approved the Stage 4 findings and simplification pass. Baseline: `95775d6`, local tag `audit-model-verified-2026-11-06`. CLI checkpoint: local tag `audit-cli-verified-2026-11-06` in the isolated `codex/ticket-audit-fixes` worktree. No merge or push.

## Completed implementation

| Findings | Result |
|---|---|
| L01 | Validators retain parser diagnostics when lazy conversion fails, including missing string values. Parsing is inside the process error boundary; malformed inputs no longer escape as unhandled process failures. |
| L02 | Single-model output commits its complete group through the existing transactional folder sink. Failed replacement restores prior files; secondary restoration failure retains recoverable backups and reports their directory. Requested output extension casing and valid encoded bytes are preserved. |
| L03 | A new generation run refuses occupied Set-owned output before rolling. Checks include manifest/index, occupied Set directories, the selected packed archive and numbered model targets. Unrelated Kitchen files, models and differently named archives remain usable. Existing data is not deleted. |
| L04 | Extension compares target and existing count before subtraction. Already-met targets, including int.MinValue, return the existing no-op response without wrapping into positive generation work. |
| L05 | Explicit key sources require effective sealing after presets/overrides. Invalid combinations fail before export; explicit sealing and the sealed preset remain authenticated and readable with the correct key. |
| L06 | Unrecognized credential-source errors explain the accepted syntax without repeating the supplied potentially secret value, including verbose output. |
| L07 | Both orders of each opposite images/OpenSea/nfty switch pair are refused before output. |
| L08 | Authoring JSON uses Core's existing structural gate before callers enumerate nested data or decode images. Errors identify the manifest and offending structure. |
| L09 | Added-recipe weight confirmations format invariantly. Archive and numeric parsing behavior remain unchanged. |

D01–D13 are complete: stale form/group descriptions, parameter/return/exception contracts, borrowed image lifetimes, source/prompt behavior, unverified seal-header claims and concise private intent summaries were corrected.

S01–S08 are complete with one preservation refinement. The weight-edit simplification retains UpsertRecipe's historical JSON weight-key order and restores the original Recipes list directly, removing the quadratic search. Directly using SetRecipeWeight would change authoring payload bytes. Identity editing reuses EditIdentity; preview depth uses LayerDepth; form detection is cached; export planning is lazy/shared; secret files are read one line at a time; HttpClient lifetime belongs to each action; repeated replacement explanations are centralized.

## Scope and preservation

- Three delegated `gpt-6.1-sol` owners implemented authoring, publishing and model-I/O scopes. Main-agent work covered shared conversion guards, process entry, new-output safety, overflow and integration.
- The Core changes are a friend grant for the existing structural/layout boundary and an unchanged packed-path calculation extracted into SetLayout for reuse. No RNG, DNA, probability, rarity, colorization or mesh algorithm changed.
- ModelBatch.WriteFolder reuses the existing rollback mechanism rather than adding another writer framework. Empty groups, safe nested paths and repeated destination payloads retain their supported behavior.
- Failed/ignored malformed inputs intentionally change to clear refusal. Ordinary successful output remains byte-identical in the comparisons below.
- Frozen fixtures, archived designs, package versions, Ticket and production App code remain unchanged. No live provider requests were made.

## Regression and integrated checks

Regressions were observed failing before their fixes. Recorded RED runs include 15 authoring failures, 17 publishing failures, parser/model recovery failures, seven generation/overflow failures and two additional output-collision failures. Preservation tests also passed before changes; they are not described as reproduced defects.

Independent review exposed eight remaining missing-string conversion cases after the first implementation. All eight were reproduced RED, fixed at their validator boundaries, then passed. A separate direct-parser sweep of **5,400 cases** now has **zero uncaught exceptions**, including missing arguments, malformed/overflowing numeric and boolean values, empty strings and option-like values. This is finite boundary coverage, not a proof for every argument vector.

Focused checks also cover actual-process exit/trace behavior, synthetic-secret redaction with and without verbose mode, explicit/preset authenticated sealing, the duplicate-DNA overwrite scenario, no-op extension targets, unrelated workspace preservation, complete model-pair rollback and retained backups after secondary recovery failure.

Final CLI tests: **406 passed**, up from 331 (75 additional cases). Model tests: **674 passed**, up from 669 (five additional cases). Authoring, publishing and model-focused runs overlap these totals.

| Final integration gate | Result |
|---|---|
| Release solution build | Zero warnings/errors |
| Ticket tests | 235 passed |
| Ticket netstandard tests | 235 passed |
| Core tests | 1,750 passed |
| Model tests | 674 passed |
| CLI tests | 406 passed |
| App tests | 1,928 passed; one Linux-only secret-tool test skipped on Windows |
| Full solution total | **5,228 passed, zero failed, one platform-specific skip** |
| API generation, lint and check | Clean; 269 pages current |
| Strict MkDocs build | Passed |
| Whitespace/protected scope checks | Clean |

## Output and documentation evidence

- **470/470 files match baseline**, with no missing, extra or changed files, using the final Release CLI and an isolated copy of baseline binaries. Samples include demo/frozen-fixture generation and extension, metadata, DNA, PNGs, rarity/indexes, both Menu modes, seeded serving and generated models. Menu entry payloads are extracted; variable ZIP timestamps are excluded.
- Direct 1-thread versus 4-thread comparisons match all **98 demo** and **38 fixture** Set files.
- CLI generation, extension, Menu serving and standalone glTF output match across **en-US, de-DE, tr-TR and ar-SA: 76 files per culture**.
- Baseline versus final weight and identity authoring operations match all **seven nested archive payloads per operation**, including exact manifest JSON bytes and image data. ZIP container timestamps are excluded.
- Public ModelBatch.WriteFolder documentation was regenerated with the rest of the 269 API pages; documentation lint/check and the strict manual build pass.

Rendered verification compares **431 baseline and 431 final frames**, with no missing/extra frames. **421 match exactly**. All ten changed regions were inspected: six contain random temporary-path labels, two capture thumbnails while decoding is in flight, and two contain a total of five UI-border pixels differing by one intensity level. The latter are outside asset imagery. An unchanged-binary repeat restores the first border frame to the baseline pixels; the other matches the earlier Core capture and two repeated final captures. These tiny border/capture differences are documented, not claimed identical. No asset or layout drift was observed. The focused repeat added three passing capture executions, excluded from the full-suite total above.

A final XML-only correction removed an absolute secrecy claim about stdin. The solution was rebuilt at zero warnings/errors and API generation/lint/check rerun; runtime code did not change after the full-suite execution.

## Limits and stop point

- Verification uses the available Windows x64 host. Another CPU architecture/OS was not exercised. Windows sharing-lock regressions are platform-specific; cross-culture/thread checks do not replace cross-architecture execution.
- Folder rollback covers recoverable operation failures, not power loss or process termination across multiple filesystem operations. If restoration itself fails, the reported backup directory is retained for recovery.
- Preflight refuses existing output; it is not an interprocess lock against a second writer racing after the check. Separate processes must not generate into the same destination concurrently.
- Synthetic credentials and stubbed/provider-free paths test error behavior. No live upload, real secret or external model viewer was used.
- The requested stop is before App review. CLI changes remain uncommitted; no new tag, merge or push is made at this checkpoint.

## Local evidence

Ignored artifacts under `artifacts/audit/`:

- `cli-release-final-build.log`, `cli-release-final-tests.log`
- `cli-authoring-red.log`, `cli-authoring-green.log`, `cli-publish-red.log`, `cli-publish-green.log`
- `cli-model-green.log`, `model-writefolder-green.log`; corresponding model RED logs
- `cli-execution-red.log`, `cli-execution-boundary-red.log`, `cli-execution-green.log`
- `cli-missing-text-red.log`, `cli-missing-text-green.log`
- `cli-review-integrated-probe/parse-results.json` and `final-results.txt`
- `cli-verified-output-parity.json`, `cli-demo-thread-parity.json`, `cli-fixture-thread-parity.json`
- `cli-authoring-parity/results.json`, `cli-culture-output/results.json`
- `cli-api-generate.log`, `cli-manual-build.log`
- `cli-frame-parity.json`, `cli-frame-review.png`, `cli-frame-repeat-parity.json`, `cli-repeat-frames.log`, `cli-border-repeat-frames.log`
- `cli-baseline-cli/`, `cli-baseline-output/`, `cli-verified-output/`, `cli-final-frames/`

The [original review](04-cli.md), [implementation checklist](04-cli-implementation.md) and [overall tracker](REVIEW.md) preserve the finding, approval and completion history.
