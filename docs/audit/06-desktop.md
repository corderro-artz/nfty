# Desktop audit and implementation

Reviewed on 2026-11-08 in `codex/ticket-audit-fixes`, starting at `5b208dd`.
The user's later authorization to finish the remaining audits and fixes supersedes the older
report-only stop in the handoff. The subsequent request to center both the main window and new
viewers is included as R01 below. Integration and release remain the coordinating agent's work.

## Scope and method

Pass 1 covered every original `src/Nfty.Desktop` C# file, its four AXAML files, project and Windows
manifest, icon loading, and the companion window, chrome, close-guard, startup, viewer-opener,
gallery and DPI-placement tests. It traced the actual window close callbacks, service-provider
ownership, native-service window lookup, viewer subject handoff, asynchronous launch work and
clipboard image ownership. No changes were made to the shared generation or archive pipelines.

Pass 2 separately checked the XML against those call paths and looked for bounded simplifications.
It checked the compiled documentation/build boundary rather than adding descriptions to every
private helper. The resulting changes keep behavior at the desktop boundary: two small shared
helpers replace duplicated native-window lookup and dialog close handling.

The original eleven C# files remain; `DesktopWindowContext` and `DesktopDialogLifetime` are the
two added production files. Dedicated regression coverage lives in `DesktopAuditTests.cs`.
Fixtures, archived designs, package pins and the settings schema are unchanged.

## Findings and remedies

| ID | Severity | Evidence and consequence | Implemented remedy |
|---|---|---|---|
| W01 | P2 | Changing `MainWindow.DataContext` left the old shell's caption handlers attached. Reattaching a shell duplicated maximize handlers, so one command toggled twice. An asynchronous leave answer from the previous shell could also close the replacement shell. | Named handlers detach on replacement and close. A delayed affirmative answer closes only the shell that was asked. Three caption/lifetime regressions failed before correction. |
| W02 | P2 | `DesktopViewerHost.Open` accepted ownership before reading settings or creating/showing the window, but exceptional exits released neither the subject nor a partly constructed view model/window. | An explicit exceptional cleanup path releases the transferred subject and view model; a constructed window has idempotent content cleanup shared with normal close. Settings-failure and pre-show-failure tests both failed before correction. |
| W03 | P2 | Closing viewer/launch windows left pending dialog answers unresolved. Escape directly dismissed busy cards, and window close could dispose viewer content while a card still owned work. | Closing completes pending ordinary dialog answers. Busy cancellation waits for cleanup before closing the window; Escape requests cancellation while retaining the running card. Finished cancellable cards dismiss normally, while an unknown busy card refuses dismissal. The latter two cases were added after independent integration review and failed against the first helper implementation. |
| W04 | P2 | Viewer-only startup swallowed exceptions outside `ViewerOpener`, including invalid-path normalization, and could show a viewer after the launch had been closed while noncancellable decryption completed. | The launch window owns a tracked `PendingOpen`, cancellation and handoff. It shows normalization failures through its error layer, disposes a result that arrives after close, and never hands that result to a host. An actual `StartViewerOnly` invocation reproduces invalid-path behavior. A real sealed Set provides a deterministic close-during-decryption regression. |
| W05 | P2 | All picker calls used `desktop.MainWindow`, even after that window closed and an independent viewer remained. The three picker operations silently returned no path under that lifetime. Returned storage handles were also left undisposed. | A shared resolver prefers an active visible window, then the visible main window, then another visible window. Pickers, clipboard and default viewer display lookup share it. Chosen storage objects are disposed after obtaining local paths. The three picker regressions exercise paths containing spaces, percent, hash and Unicode characters. |
| W06 | P2 | A copied Avalonia `Bitmap` was deliberately never disposed. The ordinary `DataTransfer` used by `SetValueAsync` has an empty `Dispose`, so clearing/releasing the transfer did not free the bitmap. | A small `IAsyncDataTransfer` owns the bitmap until the clipboard releases it. A failed clipboard write also releases the transfer. The real headless clipboard's `ClearAsync` boundary proves the bitmap becomes disposed. Restoring the old transfer implementation makes that test fail. |
| W07 | P2 | The Linux startup diagnostic recognized missing RandR, input and cursor libraries but its installation commands omitted their packages. The parser also had a broad non-Windows guard rather than a Linux-only catch boundary. | Linux alone handles the recognized native startup failure. The diagnostic helper is pure and testable on every build host, and all three package commands now include the missing dependencies. The manual/release instructions are updated by the coordinating/tooling agents. |
| W08 | P2 | Folder reveal built a raw quoted command string, which cannot preserve arbitrary embedded quotes in legal Unix paths. Its three process-launch branches also left the returned process handle undisposed. | One platform-selected launch request carries the path in `ArgumentList`, and the best-effort launcher disposes its process handle. Three generated-request regressions fail with the original raw-argument construction and pass with exact single-argument transport. No native file manager is started by these tests. |

These changes do not promise that the operating system can never terminate the process. Ordinary
window close requests are guarded; forced session/process termination remains an OS decision.

## R01: centered startup

The main window now requests `CenterScreen`. The launch window already did. Every new viewer also
requests `CenterScreen`, including one with old stored X/Y coordinates. Its remembered size,
maximized state, backdrop and wireframe choice remain in effect. Existing coordinate fields and
the explicit `ViewerWindow.Place`/`ViewerWindowPlacement` APIs remain compatible for callers that
specifically request placement; the desktop host no longer restores coordinates.

When a live opening window is available, the host seeds the new viewer's initial position inside
that window's display. It does not pass an owner to `Show`, preserving independent viewer lifetime.
Default lookup prefers the active viewer over a still-visible shell. With no usable reference,
Avalonia uses the native initial display and then its primary-display fallback.

The pinned [Avalonia `Window` implementation](https://github.com/AvaloniaUI/Avalonia/blob/e33eaed9c106846b200680751022385d9cc5dc6f/src/Avalonia.Controls/Window.cs#L1140-L1215)
selects the screen before showing, computes the physical frame using the selected display's
scaling, and centers in its working area. The code delegates that calculation to Avalonia rather
than duplicating DIP/pixel and taskbar arithmetic. No cursor-position P/Invoke was introduced.
The existing tests still prove a viewer remains open after its shell closes and that its remembered
size and appearance survive.

## XML and simplification mapping

| ID | Scope | Result |
|---|---|---|
| D01 | `App` | Composition now names native viewer hosting as well as pickers, clipboard and folder reveal; viewer-only launch documentation matches the owned asynchronous operation. |
| D02 | `MainWindow` | Close documentation describes the current-shell check and OS termination limit. Removed stale markup commentary claiming the theme control did not exist and that the window dimensions were a scaled old mockup size. |
| D03 | `DesktopFilePicker` | Replaced the inaccurate claim that it cannot be headless-tested with the live-window, local-path and storage-ownership contract. |
| D04 | `DesktopViewerHost` | Constructors and remarks describe the opening window, active-viewer priority, centered startup and retained remembered appearance. |
| D05 | `ViewerWindow` | Normal-size persistence and stored legacy coordinates are distinguished from centered host startup. The explicit placement method describes its remaining caller-controlled purpose. Cleanup ownership is documented. |
| D06 | `DesktopClipboard` | Documents native bitmap ownership through the transfer; removes obsolete main-window-only and historical no-op-service descriptions. |
| D07 | `Program` and launch operation | Adds entry-point/builder XML, parameters and return contracts for the dependency diagnostic, and the public `PendingOpen` lifetime. |
| D08 | `DesktopFolderRevealer` | The launch-request helper documents the native argument boundary without implying a file manager was executed in tests. |
| S01 | Native window lookup | One visible-window resolver replaces picker/clipboard/default-viewer differences; closes W05 and supplies R01's opening-display preference. |
| S02 | Desktop dialog lifetime | One shared helper supplies Escape and close cancellation semantics to both desktop viewer windows; closes W03. |
| S03 | Viewer launch flow | The launch window owns one opening task instead of burying its asynchronous lifetime and errors in an anonymous application event handler; closes W04. |
| S04 | Ownership cleanup | Idempotent viewer cleanup serves both normal close and failed handoff; closes W02 without a second disposal protocol. |
| S05 | Folder reveal | One platform-selected executable and launch request replace three repeated process branches; closes W08. |

No new manifest field, RNG operation, report formatting, image resampling, theme color or content
layout was introduced. No general window framework or broad shared-App refactor was added.

## Verification evidence

Ignored evidence is under `artifacts/audit/`:

- `desktop-red.log`: six expected lifetime failures, one existing clean-close pass.
- `desktop-red-2.log`: six expected busy-close/Escape and centering failures, seven passes.
- `desktop-picker-red.log`: all three real picker-service calls fail to obtain the live viewer's path.
- `desktop-red-4.log`: invalid launch, delayed old-shell answer and native bitmap disposal failures.
- `desktop-red-5.log`: two independently reviewed Escape-state failures; real late-decryption test passes with the handoff guard.
- `desktop-red-6.log`: three expected cross-platform diagnostic-helper failures before the Linux boundary moved to the entry-point catch.
- `desktop-mutation-red.log`: reverting bitmap transfer ownership and the canceled-launch handoff guard gives exactly two failures; the latter records one unwanted viewer handoff instead of zero.
- `desktop-green-4.log`: 70 focused tests pass before the final Escape and diagnostic additions.
- `desktop-folder-red.log`: three argument-transport cases fail before the `ArgumentList` change; the rebuilt bitmap and late-launch detectors both pass, confirming restoration of their fixed source.
- `desktop-final-focused.log`: **79 passed, zero failed/skipped** with the final source, including **28 new Desktop regression cases**. The Release dependency build prints no warnings or errors.
- `desktop-final-capture.log`: both requested capture tests pass, producing 18 PNGs: two shell frames and 16 actual viewer-window frames covering both themes, 2D/3D/gallery/sealed states and minimum/extended layouts.
- `desktop-frame-comparison.csv`: **18/18 rendered PNGs are SHA-256 byte-identical** to the accepted `app-final-frames-v2` checkpoint. The dark 2D viewer and light shell were also visually inspected. No content geometry or ink changed in these frames.

`desktop-final-app.log` is **not a final green-suite claim**: it records 2,055 passed, two failed and
one Linux-only skip. After the two targeted mutation checks, copying the fixed source back retained
old modification times; the incremental build reused the intentionally mutated Desktop binary.
Both mutation detectors then failed in the full run. The fixed files were checked, their times
advanced, and the dependency assembly rebuilt before `desktop-folder-red.log` and the final 79-test
green run. The coordinating agent will force a non-incremental solution build and run the entire
suite against that build. This unsuccessful verification attempt remains preserved explicitly.

Initial test-harness setup failures (Avalonia's locked lifetime setter, removed locator methods) and
one misplaced XML comment were corrected; they are not counted as reproduced product defects.

## Source checks and limits

The clipboard ownership reasoning uses the pinned [clipboard contract](https://github.com/AvaloniaUI/Avalonia/blob/e33eaed9c106846b200680751022385d9cc5dc6f/src/Avalonia.Base/Input/Platform/IClipboard.cs)
and [ordinary transfer implementation](https://github.com/AvaloniaUI/Avalonia/blob/e33eaed9c106846b200680751022385d9cc5dc6f/src/Avalonia.Base/Input/DataTransfer.cs).
The headless clipboard stub does not dispose a prior transfer on `SetDataAsync`; the regression uses
its actual `ClearAsync` boundary, which does. No claim of immediate native replacement disposal is
inferred from that stub.

Package names were checked against official [Ubuntu RandR](https://packages.ubuntu.com/noble/libxrandr2),
[input](https://packages.ubuntu.com/noble/libxi6), [cursor](https://packages.ubuntu.com/noble/libxcursor1),
[Fedora RandR](https://packages.fedoraproject.org/pkgs/libXrandr/libXrandr/),
[input](https://packages.fedoraproject.org/pkgs/libXi/libXi/),
[cursor](https://packages.fedoraproject.org/pkgs/libXcursor/libXcursor/), and
[Arch RandR](https://archlinux.org/packages/extra/x86_64/libxrandr/),
[input](https://archlinux.org/packages/extra/x86_64/libxi/),
[cursor](https://archlinux.org/packages/extra/x86_64/libxcursor/) package pages. Tests parse the
generated install-command arguments; they do not install packages or simulate a missing native
loader on another OS.

Headless native-service tests substitute only the platform lifetime/storage interfaces, restore
the application lifetime in `finally`, and use temporary directories. They never read or modify
real recents, settings, registry associations or credentials. Rendered content captures verify
window geometry/content, while native multi-monitor/DPI startup remains a separate integration
check. The coordinating agent owns the final full-solution build/tests, package checks and native
release verification; this report does not claim those outcomes before they run.
