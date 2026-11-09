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
- [x] Focused regressions/rendered proof, main integration, version/tag/push/release and docs.

Owner: Cook agent. Checkout `C:/Users/Corde/.codex/worktrees/ticket-audit-fixes/nfty`,
branch `codex/ux-cook-output` (the former audit branch is already deleted).

## Batch B — Settings/help interaction and selected file associations

- [x] Inspect existing behavior and establish failing tests: footer icons lack the shared
  hover/selection transitions; Settings has only three group choices and registration text
  incorrectly implies an effective default.
- [x] Restore consistent hover, selected and unhovered icon transitions without changing geometry.
- [x] Select individual supported extensions or all; choose app/viewer routing where supported.
- [x] Register/unregister selected types with honest per-type registration/effective-default
  status and a refresh path. Preserve unselected and externally changed registry values.
- [x] Reuse the catalog, per-user ledger and composition. Handle shared legacy ProgIDs without
  changing unselected sibling extensions or another installed copy's commands.
- [x] Use supported Windows Default Apps UI when Windows requires the user's existing default
  choice. Do not write UserChoice hashes or claim registration alone makes the app default.
- [x] Test with fakes/private injected registry roots; verify keyboard, themes and fixed geometry.
- [ ] Finish 2.4.11 main package/native/output proof, exact-commit hosted CI, tag/push/release.

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

Performance profiling used one coordinated .NET slot. After profiling completed, at most two
heavy .NET tasks run concurrently with separate worktree/OS build outputs; timing benchmarks are
not run concurrently. Read-only
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
**21/21 with zero skips**, executing both symlink cases. The final Linux regression selection
passed 43/43. Version [2.4.10](https://github.com/corderro-artz/nfty/releases/tag/v2.4.10) is published
from main 5bd61f3; all nine matching CI jobs passed (42610209945). All eight server digests match;
67/67 payloads match Windows/Linux, every shape and 2.4.9. Native final-window readiness and Linux
centering passed. [Release inventory](releases/2.4.10.json). Evidence is
retained under `artifacts/audit/cook-output/` on the main checkout. No real Desktop was used.

### Publication gate: dispatcher test reliability

Hosted CI 42608093420 passed eight jobs but failed Windows build-and-test in the unchanged
SetRiseSource exception-path test. It expected a posted UI exception to escape one later RunJobs
call, although awaiting BookSearch can already dispatch the arrival. The missed exception then
left the headless session's dispatcher cleanup invalid. Publication was held until the correction passed.
The test now observes and handles only its exact intentional exception through the dispatcher's
exception event, and awaits both that evidence and ExportReady. Its focused Windows selection
passed **24/24**; full App rerun passed 2,084 with three Windows skips, final Linux selection
passed 43/43, and all nine jobs in matching CI 42610209945 passed. No product exception
handling or readiness behavior changes. Earlier candidate packages were retained as superseded evidence. The published packages were
rebuilt from corrected main 5bd61f3. The first native Windows probe also needed a harness correction:
input idle can precede the final viewer; the final probe waits for the actual served.png window
within 30 seconds, then verifies continued liveness and responsiveness.

## Batch B implementation and simplification results

| Issue | Implemented change | Result |
|---|---|---|
| Footer buttons stayed red and lacked persistent selection/return | Existing tokens, wrapper transforms and base transitions; selection follows the dialog stack | Both themes show rest/hover/return/selected states without changing geometry or modal guards, including nested prompts. |
| Only group-level registration choices | Reuse one 13-type catalog with extension checkboxes, All and App/Viewer routing | Register/Unregister affects selected types; viewer-only types route honestly. Refresh does not change selection or write. |
| Registration text implied Windows default | Injected effective-ProgID query plus exact command/mode ownership check | Registered/default/other/unknown are separate evidence; no UserChoice/hash writes or administrator request. |
| Repair could affect unselected legacy siblings | Per-extension image/model handlers and selected-only ledger deltas | Preserve foreign/unselected values and legacy shared handlers referenced only by effective defaults; unknown evidence conservatively preserves them. Independent review caught and verified this edge case. |
| Windows-only native behavior could reach other platforms | OS-guarded factory returns unsupported service outside Windows | Linux build and actual factory/no-native-call tests pass. Windows interop compiles safely but is never invoked there. |
| Large Settings VM and stale guide | Focused association partial plus one row model, shared catalog/backend, three manual pages updated | Existing General/Performance/Publishing/Data behavior remains covered; no duplicated registration engine or invented parser. |

Source commit 76a7b89 is merged locally; candidate version 2.4.11. Full Windows Release solution:
**5,419 passed, three platform skips, zero failures**. Linux isolated full Release build and full App:
**2,112 passed, two capability skips, zero failures**. Both builds have zero warnings/errors.
All 1,646 source files, including five frozen fixtures, retain matching before/after Linux hashes.
Forty-four final Windows/Linux frames were inspected; strict manual/link checks pass. All registry
writes in tests use fakes or private injected roots; no real registration/default was changed.
Evidence: `artifacts/audit/settings-associations/` (102 copied files, SHA-256 verified).
