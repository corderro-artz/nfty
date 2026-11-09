# Post-audit usability follow-ups

Baseline: main `6d79243`, published v2.4.9. Its final main CI run 42532337662 completed successfully;
the audited release, closeout report and live documentation work are complete.

The user authorized the following additional changes and requested incremental commits, version
tags, pushes and releases from main. Three implementation agents are explicitly authorized for
this phase. Work is isolated by batch so unfinished parallel changes cannot enter another release.
Versions are assigned in verified merge order, starting with 2.4.10.

## Batch A — Cook destination safety and naming

- [x] Reproduce current behavior using a temporary Desktop-like folder, never the real Desktop.
  The baseline Cook dialog writes into an unrelated nonempty folder; Core intentionally supports
  Kitchen coexistence, so this is a GUI policy gap rather than a Core writer defect.
- [x] Add a clear early refusal for a fresh Set targeting an unrelated nonempty folder. Preserve
  legitimate existing-Set extension and authoritative Set validation.
- [x] Add an unchecked-by-default “Name after CookBook” option. Derive a safe lowercase,
  dash-separated child folder name from the CookBook's name; do not change collection metadata.
- [x] Treat the selected folder as a parent in named mode; preserve unrelated parent files. Refuse
  unsafe occupied children/files/links, never silently suffix or overwrite them, and create no
  empty child before the writer owns it. Preserve packed output and cancellation behavior.
- [x] Keep the recently cleaned dialog's fixed geometry, keyboard paths and both themes.
- [ ] Focused regressions/rendered proof, main integration, version/tag/push/release and docs.

Owner: Cook agent. Checkout `C:/Users/Corde/.codex/worktrees/ticket-audit-fixes/nfty`,
branch `codex/ux-cook-output` (the former audit branch is already deleted).

## Batch B — Settings/help interaction and selected file associations

- [x] Inspect existing behavior and establish failing tests: footer icons lack the shared
  hover/selection transitions; Settings has only three group choices and registration text
  incorrectly implies an effective default.
- [ ] Restore consistent hover, selected and unhovered icon transitions without changing geometry.
- [ ] Select individual supported extensions or all; choose app/viewer routing where supported.
- [ ] Register/unregister selected types with honest per-type registration/effective-default
  status and a refresh path. Preserve unselected and externally changed registry values.
- [ ] Reuse the catalog, per-user ledger and composition. Handle shared legacy ProgIDs without
  changing unselected sibling extensions or another installed copy's commands.
- [ ] Use supported Windows Default Apps UI when Windows requires the user's existing default
  choice. Do not write UserChoice hashes or claim registration alone makes the app default.
- [ ] Test with fakes/private injected registry roots; verify keyboard, themes and fixed geometry.
- [ ] Main integration, version/tag/push/release and current documentation/screenshots.

Owner: Settings agent. Checkout `C:/Users/Corde/.codex/worktrees/nfty-settings-integration/nfty`,
branch `codex/ux-settings-associations`.

## Batch C — Model preview responsiveness and lighting

- [x] Trace existing pooled buffers, bitmap reuse and input coalescing; record bounded orbit
  baselines and a failing missing-light-control test before implementation.
- [ ] Improve measured hot paths and scheduling while preserving valid default render pixels,
  validation, camera/projection/depth math and export payloads. Qualify timing claims.
- [ ] Add shared viewer/Rise light value 0–100, RGB color and quick light reset. Default 100%/white
  reproduces existing pixels. Resetting lights must not reset the camera.
- [ ] Invalidate any preview cache when lighting changes; preserve alpha/background behavior.
- [ ] Assess antialiasing options and quality/performance costs. The request is an assessment;
  no AA or GPU renderer rewrite is included without a further decision.
- [ ] Focused correctness/performance/rendered proof, API XML generation if affected, main
  integration, version/tag/push/release and documentation.

Owner: Preview agent. Checkout `C:/Users/Corde/.codex/worktrees/nfty-preview-lighting/nfty`,
branch `codex/ux-preview-lighting`.

## Integration policy and evidence

One .NET build/test/profile/publish slot is coordinated across the three worktrees. Read-only
investigation, edits and independent documentation work can continue concurrently. Each verified
batch merges into clean main before its version is built and released. No feature branch release
is published. Default deterministic output, protected fixtures and archived designs remain intact;
ImageSharp stays 3.1.11. Documentation, tracker and evidence are updated per batch.

Windows references: [Default Apps deep links](https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-default-apps-settings),
[effective ProgID query](https://learn.microsoft.com/en-us/windows/win32/api/shlwapi/ne-shlwapi-assocstr).
Registration and actual OS default choice are distinct states. Core archive/generation policy is
not changed merely to enforce the Cook dialog's new destination policy.

## Batch A implementation and simplification results

| Issue | Implemented change | Result |
|---|---|---|
| Unrelated populated output could be accepted | GUI-only destination preflight before generation; authoritative SetReader validation for extension | The reproduced Desktop-like case now receives an actionable refusal and retains every original byte/name. Core's intentional Kitchen coexistence remains available to other callers. |
| Repeated manual folder naming | Unchecked Name after CookBook; one portable lowercase/hyphenated child of the selected parent | Named cooking, packing and Open Set use the same resolved child; a valid child Set extends normally. |
| Unsafe or occupied child | Refuse occupied files/non-Set folders and links in Set-owned destinations | No silent suffix, replacement or writes through linked Set metadata. Cancellation/failure creates no unused child. |
| Avoid duplicate parsers and generation paths | Small internal policy helper reuses existing ID/device-name rules, SetReader and the existing generator/writer | No changes to math, RNG, DNA, formats or image ownership. Existing-Set preflight adds a metadata validation read; it decodes no images. |
| Extra option without layout growth | Two fixed option rows and a compact named chevron for model settings | 421 DIP card and painted footer remain unchanged in both themes; keyboard paths verified. |
| Documentation accuracy | Cooking guide and four exact production screenshot crops refreshed | Each theme's captured cook produced 50 assets and 50 GLB models. |

Focused proof: 58 passed, two explicit Windows symlink-creation privilege skips; the corrected
production capture/export journey selection adds three passes, overlapping the eventual full suite.
28 option/error frames were inspected; all 22 comparable states retain exact footer/outside pixels.
Root full Release solution build passed with zero warnings/errors: **5,392 passed, zero failed,
three explicit Windows platform/privilege skips**. Linux Cook/output/export selection passed
**21/21 with zero skips**, executing both symlink cases. Package-output and publication gates remain
pending for version 2.4.10. Evidence is
retained under `artifacts/audit/cook-output/` on the main checkout. No real Desktop was used.

### Publication gate: dispatcher test reliability

Hosted CI 42608093420 passed eight jobs but failed Windows build-and-test in the unchanged
SetRiseSource exception-path test. It expected a posted UI exception to escape one later RunJobs
call, although awaiting BookSearch can already dispatch the arrival. The missed exception then
left the headless session's dispatcher cleanup invalid. Release 2.4.10 remains unpublished.
The test now observes and handles only its exact intentional exception through the dispatcher's
exception event, and awaits both that evidence and ExportReady. Its focused Windows selection
passed **24/24**; full App, Linux and new hosted CI checks remain required. No product exception
handling or readiness behavior changes. Candidate packages built before this gate fix will be
superseded; focused/cross-platform/full App and hosted CI proof must pass before publication.
