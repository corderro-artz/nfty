# Release follow-ups — 2.4.9

The audited 2.4.8 release was published from main before this work began. The user then requested
a separate cleanup of the Cook dialog's options/footer, a new main-branch commit/tag/push/release,
current manual screenshots, live documentation and removal of the merged audit worktree.

## F01 — Cook dialog presentation

The options now use aligned checkbox-text edges, shorter truthful DNA/retry hints and tighter
local spacing. One fixed model-detail region alternates the explanatory note with the existing
format/pivot/voxel fields. The note remains on the checkbox tooltip while fields are open.
This shares existing space instead of reserving a second blank row. A late partial-alpha answer
restores the unsupported-model reason in the same region and hides the inappropriate fields.

The footer reserves one Close/Cancel target and one Cook/Open Set target. Ink, hit testing and
keyboard participation change without moving or resizing the card, footer or targets. The card
is 430×421 DIP in every tested state, compared with approximately 432 DIP collapsed and 493 DIP
expanded before cleanup. The secondary/primary targets remain 70×30 and 96×30 DIP. Keyboard Space
opens/closes model settings, Tab enters visible fields and skips hidden fields.

Count, seed, Recipe selection, every model setting, production generation/extension/writing,
cancel cleanup and export behavior remain. No engine math, RNG, DNA, archive or model algorithm
was changed. XML on the affected presentation properties now describes the fixed detail region.

Focused evidence: 49 passed, including seven new cases. A separate seven-case capture selection
overlaps this total. Eighteen option/run transitions preserve exact card/footer/target bounds and
the actual painted footer separator in both themes. The first shared-slot attempt exposed a late
scan presentation regression; a failing two-theme test preceded its predicate/notification fix.
It was corrected before commit, rather than accepted as a new limitation.

## F02 — Pillow developer-tool security update

GitHub surfaced 59 open alerts against the developer-tool Pillow 12.1.1 pin: five critical,
35 high and 19 medium. They concern Python tooling, not the downloadable .NET application's
runtime dependencies. Each advisory range was checked: all include 12.1.1 and none includes
13.0.0; the selected version also meets every declared first-patched version.

The pin is now 13.0.0. Its upstream minimum requires Python 3.11, reflected in the README; CI's
Python 3.12 satisfies it. An isolated environment ran the actual scratch demo check and tooling
regressions. All 49 demo PNGs and 45 raw depth maps remain byte-identical. Fifteen manifest changes
are only the previously accepted canonical LF correction, with matching parsed values. The
embedded demo and frozen fixtures were not rewritten. Tool tests: 18 passed, one Windows symlink-
permission skip. No alert was manually dismissed. ImageSharp remains pinned at 3.1.11.

Primary references: [Pillow 13 release notes](https://pillow.readthedocs.io/en/stable/releasenotes/13.0.0.html)
and [version metadata](https://pypi.org/project/pillow/13.0.0/). Full advisory-range and payload
evidence is retained in ignored `artifacts/audit/pillow13-preflight/`.

## Documentation and screenshots

The README and 21 manual pages correct count/bound semantics, DNA versus visual uniqueness,
deterministic inputs, retry and rollback limits, layered face ownership, actual archive entry
names, runtime requirements, links, badges and platform facts. The [after-action report](AFTER-ACTION.md)
maps these corrections. Existing Diataxis categories remain; GitHub wiki remains disabled.

Four Cook images were replaced in both themes. The source frames use the actual production
MainWindow and shared services, the embedded Chest Demo, a fixed seed and real 50-asset/50-model
cooks into temporary folders. Result text and output are real. The common crop is
`(436, 135, 956, 644)` from 1392×768 frames, producing 520×509 images. Pixel equality to each
source crop is verified; no resampling or synthesized UI was used. Both cooking guides reuse
the images, and their existing captions remain accurate. Crop mappings were updated together.

The [live API reference](https://www.vaporsoft.dev/nfty/docs/reference/api/) already converts
compiler XML and reflected public signatures for Ticket, Core and Model into 269 HTML pages.
`python tools/api-docs/build.py` regenerates its Markdown input; separate `--check` and `--lint`
invocations and CI maintain it. The README now links this reusable workflow and the live result.
No additional documentation system or wiki is required.

Strict manual builds pass; all four built images match source bytes. Final full-solution,
release-package, hosted CI, live-site and cleanup results are recorded in the current tracker
and after-action report as they complete.

## Final local integration gate

Forced Release build: zero warnings/errors. The first full run passed all product checks but
rejected the new worker gate's unbounded wait through the repository's source-policy test. A
bounded wait now releases normally through the test's explicit gate and has a ten-second upper
bound. One intermediate callback-argument compile error was corrected; the closest ten cases
and the subsequent complete solution pass. Final result: **5,375 passed, zero failed, one Linux-
only skip** (App2,067). No guard was disabled or weakened.

453 rendered frames were captured. Against 431 existing frames: 403 exact, 20 intentional Cook changes,
six random temporary-path labels and two minor raster differences (two/four pixels, maximum
channel difference one/three). Twenty-two new Cook-state frames were added; none is missing.
Focused before/after regions and actual manual renders were visually inspected. Production code
was unchanged by the later test-gate correction, so the captured UI remains the verified source.

All eight package/native gates are complete. The original arbitrary six-second startup snapshots
were superseded by instrumented readiness checks: bounded Windows input-idle and actual mapped,
centered Linux content windows. The viewer predicate explicitly waits for served.png, not the
intermediate launch window. Every final probe passes with empty application stderr; the original
attempts remain retained rather than being labeled successful. See the .NET
[readiness API](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.waitforinputidle?view=net-10.0).

Version 2.4.9 is published from main c23eac4 with eight server digests/sizes matching local archives.
All 67 payloads match Windows/Linux, all shapes and 2.4.8/2.4.9. All nine hosted CI jobs passed on
the exact release commit. GitHub open dependency alerts are zero. Site deployment 42531923431
succeeded; all four live PNG hashes and current API record/constraint signatures match source.
The audit branch is deleted; 25,780 evidence files are SHA-256 preserved. Codex blocks deletion
of this chat’s primary checkout folder, which remains detached. No implementation/release task
remains pending; optional work is retained explicitly in BACKLOG.md.
