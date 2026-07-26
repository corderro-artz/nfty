# nfty GUI — Cook → generate (design spec)

**Date:** 2026-07-26
**Status:** Approved (design), pending implementation planning
**Scope:** First **behavior** slice after the visual passes: wire the CookBook detail's Cook button to
actually generate a Set via `Nfty.Core` and write it to disk, with a modal dialog for options, progress
+ cancellation, both output formats (folder or packed `.set`), a reveal-in-folder action, and clean
error surfacing.
**Builds on:** merged Explorer (shell + detail bodies), imaging bridge, visual foundation. Core seams
already exist: `Generator.GenerateAsync(book, opts, IProgress<GenerationProgress>, CancellationToken)`,
`SetWriter.WriteAsync(set, outDir, pack, IProgress<WriteProgress>, CancellationToken)`,
`GenerateOptions(Count, Seed, RecipeId?, MaxRerollsPerAsset, EnforceUniqueDna)`. The Phase-2a
`IFilePickerService`/`DesktopFilePicker` + `IDialogService` overlay are in place.

## 0. Program bar
Rock-solid, functions logically/efficiently; best practices; pull Avalonia/Core docs rather than assume;
escalate anything off. The dialog is functional now; its mockup-faithful visual polish comes with the
later visual pass (no locked Cook mockup exists — style it cleanly with the token/foundation styles).

## 1. Goals & non-goals
**Goals**
- Cook a `LoadedCookBook` into a Set on disk from the GUI: choose Count / Seed / Pack, pick the output
  location, run with a progress bar + Cancel, then a done state ("N assets → path" + Reveal).
- Both outputs: a folder of assets, or a single packed `.set` (Core `pack: true`).
- Errors (`RuleConflictException`, `UniqueSpaceExhaustedException`, invalid cookbook, I/O) surface via the
  error dialog with `ex.Message`; never crash; the `GeneratedSet` is always disposed.
- Off-UI-thread execution; cancellation honored.

**Non-goals (this slice)**
- The **Set browser** (viewing cooked output in-app), **extend/append** to an existing Set, **per-recipe**
  cook. Real editing/paint/Cook-from-Recipe. Mockup-fidelity polish of the dialog. Any `Nfty.Core` change
  (all seams exist).

## 2. Components

### 2.1 Seams
- **`IFilePickerService.PickFolderAsync(string title)`** → `Task<string?>` (local path or null on cancel).
  `DesktopFilePicker` implements via `TopLevel.StorageProvider.OpenFolderPickerAsync` +
  `IStorageFolder.TryGetLocalPath()`. The headless `FilePickerService` stub returns null (matches the
  existing open/save stubs). **Confirmed** (`SetWriter.Pack`): output is **always a folder** at `outDir`;
  `pack: true` additionally zips it to a sibling `<outDir>.set`. So Cook only ever picks a **folder** —
  Pack is a flag, not a separate save dialog.
- **`IFolderRevealer`** (new, head-agnostic interface in `Nfty.App.Services`): `void Reveal(string path)`.
  Desktop impl opens the OS file manager at the path/containing folder (e.g. `explorer.exe /select,` on
  Windows; `open`/`xdg-open` elsewhere) via `Process.Start`. A no-op stub is registered in `AddNftyApp`
  and overridden by the Desktop head (like `DesktopFilePicker`). Reveal failures are swallowed (best
  effort) — never throw into the UI.

### 2.2 `CookDialogViewModel`
Constructed per-cook via a DI factory (see 2.3). State:
```
[ObservableProperty] int Count;              // pre-filled: min(uniqueSpaceTotal, a sane default e.g. 50)
[ObservableProperty] string Seed;            // default: a short random token (e.g. Guid N, first 8)
[ObservableProperty] bool Pack;              // false = folder, true = single .set
[ObservableProperty] bool IsRunning;
[ObservableProperty] double Progress;        // 0..1
[ObservableProperty] string PhaseText;       // "Generating…" / "Writing…"
[ObservableProperty] bool IsDone;
[ObservableProperty] string ResultText;      // "N assets → path"
```
- `CookCommand` (async, disabled while `IsRunning` or Count ≤ 0):
  1. Pick location: always `PickFolderAsync("Cook to folder")`. Null → return (cancelled). (`Pack` is
     passed to `WriteAsync` as the flag; when true the sibling `<folder>.set` is produced too.)
  2. Build `new GenerateOptions(Count, Seed)` (defaults for recipe/rerolls/uniqueDNA).
  3. `IsRunning = true`; a shared `CancellationTokenSource`. Phase "Generating…":
     `using var set = await Generator.GenerateAsync(book, opts, genProgress, ct)` where `genProgress`
     maps `GenerationProgress.Fraction` → `Progress`. Phase "Writing…":
     `await SetWriter.WriteAsync(set, outDir, Pack, writeProgress, ct)` mapping `WriteProgress.Fraction`.
  4. Success → `IsDone = true`, `ResultText = "{Count} assets → {path}"`, keep the path for Reveal.
  - `OperationCanceledException` → not an error: reset to the form (or a "Cancelled" note), dispose set.
  - Other exceptions → show the error dialog (`ErrorDialogViewModel`) with `ex.Message`; dispose set;
    reset `IsRunning`.
- `CancelCommand`: cancels the CTS (enabled only while `IsRunning`).
- `RevealCommand` (enabled when `IsDone`): `_revealer.Reveal(path)`.
- `CloseCommand`: `_dialogs.Close(null)` (Esc).
The book is **borrowed**, never disposed here (the session owns it).

### 2.3 Wiring
- DI: `Func<LoadedCookBook, CookDialogViewModel>` registered in `AddNftyApp` (mirrors the editor factory),
  resolving `IFilePickerService`, `IFolderRevealer`, `IDialogService`, and an `ErrorDialogViewModel`
  factory/`IDialogService`.
- `ExplorerViewModel` ctor gains the `Func<LoadedCookBook, CookDialogViewModel> cookFactory`; the CookBook
  detail branch passes a `cook` Action: `() => _dialogs.ShowAsync<object>(cookFactory(_book))` (modal via
  the existing overlay).
- `CookBookDetailViewModel` ctor gains `Action cook`; `Cook()` invokes it (replacing the `Report` stub).
- All construction sites (DI factory, `ExplorerViewModel`, `SmokeTests`, `ExplorerViewModelTests`,
  `CookBookDetailViewModelTests`) updated in the same commits.

### 2.4 View
`CookDialogView` (a dialog `UserControl`, resolved by `ViewLocator`): the options form (Count
`NumericUpDown`, Seed `TextBox`, Pack `CheckBox`), a Cook `accent` + Cancel/Close buttons; while running,
a `ProgressBar` (bound `Progress`, or indeterminate if Total unknown) + `PhaseText` + Cancel; when done,
`ResultText` + Reveal + Close. Token/foundation styles; `Esc` closes when not running.

## 3. Data flow
```
CookBook detail → Cook() → cook Action → _dialogs.Show(cookFactory(book))
  CookDialog.Cook: pick location → GenerateOptions
    → Generator.GenerateAsync(book, opts, progress, ct)   [Task.Run, GenerationProgress]
    → SetWriter.WriteAsync(set, outDir, pack, progress, ct) [async I/O, WriteProgress]
    → set.Dispose(); IsDone; ResultText; Reveal
  throws → ErrorDialog(ex.Message); set disposed; not running
```

## 4. Testing
In-memory `LoadedCookBook`s (as the Core/App tests build them) + a real temp output dir
(`Directory.CreateTempSubdirectory()`); fakes for picker/dialog/revealer.
- **CookDialogViewModel** (`[AvaloniaFact]` where it constructs Avalonia bits; else `[Fact]`):
  - Fake picker returns a temp folder → `CookCommand` runs generate+write → assert the expected asset
    files exist on disk and `ResultText`/`IsDone` reflect the count. (Real `Generator`+`SetWriter`.)
  - Pack path: `Pack = true`, fake folder picker returns a temp dir → after cook, a sibling
    `<dir>.set` file exists beside the folder.
  - Too-large `Count` (> unique space) → `CookCommand` catches `UniqueSpaceExhaustedException` → error
    dialog shown, `IsRunning` reset, no crash.
  - Picker returns null → no run, no output, dialog stays on the form.
  - Cancel: a `CancellationTokenSource` cancelled mid-run → `OperationCanceledException` handled, not an
    error dialog. (May be a focused unit on the cancel handling rather than a race.)
- **CookBookDetailViewModel**: `Cook()` invokes the injected `cook` Action (fake records it).
- **DesktopFilePicker.PickFolderAsync** + **IFolderRevealer**: need a window / OS — **manually smoke-tested**
  (noted in the plan), not unit-tested.
- Full suite stays green; build 0 warnings.

## 5. Out of scope
- Set browser, extend/append, per-recipe cook, editor/paint, dialog visual-fidelity polish, `Nfty.Core`
  changes, cook from the Recipe pane.

## 6. Risks & escalation
- **Progress across two phases** — generate then write are sequential; a single 0..1 bar per phase with a
  phase label is fine (don't fake a unified percentage). If `GenerateAsync` reports no progress for tiny
  counts, an indeterminate bar during generate is acceptable.
- **Folder vs pack** — RESOLVED: `SetWriter.Write(set, outDir, pack)` always writes the folder at
  `outDir`; `Pack` zips it to a sibling `<outDir>.set`. Cook always picks a folder; Reveal targets that
  folder (and the `.set` sits beside it when packed).
- **Reveal** is head-specific and best-effort; never let it throw into the UI.
- **Cancellation partial output** — a cancelled write may leave a partial folder; acceptable for this
  slice (note it), or write to a temp then move (deferred).
