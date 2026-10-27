# Audit Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix every finding from the four rise-step9 audits that triage marked FIX, plus the app-wide window-close guard ORDER.md added. Then build the nine AUTHOR rulings of 2026-10-27 (TRIAGE.md): a Settings card, the book settings card, an HSV/HSL toggle, cooking one recipe, the extend total, Export a Menu, one byte formatter, and one loose-part service. Q1 (merge the DNA walks) is a BACKLOG entry only.

**Architecture:** The plan runs in five phases, in this order:

1. Data loss and correctness.
2. A configurable, honest thread count (`Parallelism`) with `ConfigureAwait(false)` throughout Core, and new fan-outs that each carry their own 1-vs-N byte-identity test.
3. CLI parity with the GUI.
4. Behavior-neutral simplifications (plus the Q8 and Q9 rulings), then GUI parity (Q3-Q7).
5. Visual QC defects and the missing capture states.

Core changes are committed separately from App and CLI changes.

**Tech Stack:** .NET 10, xunit.v3, Avalonia 12.1.1 (headless for tests and frames), ImageSharp 3.1.11 (pinned), System.CommandLine. No new packages.

**Spec:** the audits in `.superpowers/audits/1-feature-parity.md`, `2-simplification.md`, `3-parallel-async.md` and `4-visual-qc.md`, plus `ORDER.md`. Every finding id is classified in `.superpowers/audits/TRIAGE.md`; ids below (`P-I6`, `S-C1`, `T-R1`, `V-5`…) refer to that file. CLAUDE.md is the standing spec for every house rule cited.

## Global Constraints

- `TreatWarningsAsErrors` + `GenerateDocumentationFile`: every public member is documented, and every task leaves `dotnet build nfty.sln` at 0 warnings.
- **ImageSharp stays 3.1.11.** No new NuGet packages. CA2007 comes from the SDK's built-in analyzers, so it needs no package.
- US English in UI strings, identifiers, comments, CLI output and docs.
- **Determinism is non-negotiable.** Same book + same seed ⇒ byte-identical output at every thread count, locale and CPU. Every sort reaching output is `StringComparer.Ordinal`. Nothing that sums goes parallel. Depth never enters DNA. The demo still counts **615,600**, the `DemoCookBookTests` figure.
- **Manifests:** additive optional fields only. `Schema.Current` is not bumped. Nothing in this plan adds a manifest field.
- **Sync/async pairs:** every I/O entry point keeps both twins. CPU-bound work is `Task.Run` over the sync core, with no fake `async`. A genuinely async twin is the real implementation where it already is.
- **`Parallelism` is never recorded.** It must not reach `set.json`, the DNA, the seed or any hash.
- **GUI rules:** reserve space and toggle ink (`Opacity`/`IsHitTestVisible`, never `IsVisible` on anything occupying layout, except at the end of a container). Use token brushes only, defined in both theme dictionaries. No modal scrolls, except export, validity and (after T19) voxel-ready. Every page fits 1280x720. Modals are captured at ~1067x526. Verify visuals from the rendered frame (`NFTY_CAPTURE=1 NFTY_CAPTURE_DIR=<dir> dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~VisualCapture`), and keep `DarkModeContrastTests` (floor 2.0), `MinimumWindowFitTests`, `ModalFitTests`, `TextBaselineTests`, `ThemeResourceTests`, `WiringCoverageTests` and `DeadCodeAuditTests` green.
- **CLI:** commands catch nothing; `ErrorReport.Format` prints `ex.Message`. An option that cannot apply is **rejected, never ignored**. Every new option is invariant-culture.
- **Tests:** xunit.v3, `Snake_case_sentences`, fixtures built in memory, temp folders from `Directory.CreateTempSubdirectory()` deleted in `finally`. No golden images. Never touch the real `%APPDATA%`.
- **Commits:** Conventional Commits, ending `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Core commits are separate from App/CLI commits within a task.
- **Test cadence:** a targeted `--filter` per task. The full suite (`dotnet test nfty.sln`) runs once, in T20.
- **Every task's mutation probe:** after it goes green, break the fix the way the probe names and confirm the named test fails, then restore. A test that a broken fix still passes is decoration (CLAUDE.md).

## Review Focus

1. **Closing the window while a Rise save is still writing.** The close must wait for the save, then ask only if work is still unsaved. The app must never exit mid-write. Pinned in T2 (`Closing_while_a_rise_save_runs_waits_for_it_before_asking`).
2. **`--threads 64` on a 4-core CI box.** Clamped to 4, warned on stderr, output byte-identical to `--threads 1`. Pinned in T8 (`Threads_above_the_logical_count_are_clamped_and_named`) and T4 (`Effective_is_clamped_to_the_logical_processor_count`).
3. **A stale fixed-name `book.cbk.tmp` left beside a file by an older build.** The next save must succeed and must not delete the stale file's neighbor. Pinned in T3 (`A_stale_fixed_tmp_beside_the_file_does_not_block_the_replace`).
4. **An HSL Static layer whose fixed color is edited.** The layer must stay HSL, and the editor preview must match what the cook renders. Pinned in T1 (`The_editor_preview_colorizes_in_the_layers_own_model`).
5. **A literal `null` in `nfty/0007.json`.** Opening the Set, extending it and exporting it must all raise `CorruptSetException` naming that file, never a raw `JsonException`/NRE and never a silent hole. Pinned in T3 (`A_null_item_file_is_reported_as_corrupt_by_every_reader`).

---

## File map (by task)

| Task | Main files |
|---|---|
| T1 | `src/Nfty.App/ViewModels/IngredientEditorViewModel.cs`, `src/Nfty.App/Imaging/VariantImagery.cs` |
| T2 | `src/Nfty.App/Services/IUnsavedWork.cs` (new), `INavigationService.cs`, `ShellViewModel.cs`, `IngredientEditorViewModel.cs`, `RiseViewModel.Save.cs`, `src/Nfty.Desktop/MainWindow.axaml.cs` |
| T3 | `src/Nfty.Core/Formats/AtomicFile.cs` (new), `Output/SetItemFile.cs` (new), `SetReader.cs`, `SetWriter.cs`, `Generation/Generator.cs`, `Demo/DemoCookBook.cs`, `Publish/SheetGrid.cs`; App/CLI write sites |
| T4 | `src/Nfty.Core/Parallelism.cs` (new), `ParallelWork.cs`, `Generation/GeneratedSet.cs`, `Generator.cs`, `Imaging/Compositor.cs`, `Output/SetWriter.cs`, `Diagnostics/Perf.cs` |
| T6 | every `await` in `src/Nfty.Core/**`, `.editorconfig`, `Publish/SetExporter.cs`, `SpriteSheet.cs` |
| T7 | `Publish/SetExporter.cs`, `Publish/MenuWriter.cs`, `Formats/IngredientArchive.cs`, `Stats/VoxelReport.cs`, `Output/SetReader.cs`, `Output/SetWriter.cs` |
| T5 | `src/Nfty.App/Services/IParallelismSetting.cs` (new), `SettingsDialogViewModel.cs`/`SettingsDialogView.axaml` (new), `ShellChromeView.axaml`, `assets/icons/settings.svg` + `Icons.axaml` |
| T8 | `src/Nfty.Cli/CommandFactory.cs`, `LandingViewModel.cs`, `ExplorerViewModel.cs`, `CookDialogViewModel.cs`, `SetBrowserViewModel.cs`, `src/Nfty.Desktop/Program.cs`, `App.axaml.cs` |
| T9 | `src/Nfty.Cli/CommandFactory.Publish.cs`, `CommandFactory.Model.cs`, `CommandFactory.Sheet.cs` (new), `src/Nfty.Core/Publish/ModelVoxelSize.cs` (new) |
| T10 | `src/Nfty.Cli/CommandFactory.Authoring.cs`, `CommandFactory.cs` |
| T11 | Core/Ticket/Model helper sites (see the task) |
| T12 | App and CLI helper sites (see the task) |
| T13 | `src/Nfty.App/Services/LooseCreation.cs` (new), `LandingViewModel.cs`, `ExplorerViewModel.cs` |
| T14 | `src/Nfty.App/Views/PixelCanvasHost.cs` (new), `IngredientEditorView.axaml.cs`, `RiseView.axaml.cs` |
| T15 | `IngredientEditorViewModel.cs`/`View.axaml`, both wizards' VMs and views |
| T16 | `src/Nfty.Core/Editing/CookBookEdits.cs`, `BookSettingsDialogViewModel.cs`/`View.axaml` (new), `CookBookDetailView.axaml`, `ExplorerViewModel.cs` |
| T17 | `CookDialogViewModel.cs`, `CookDialogView.axaml` |
| T18 | `MenuExportDialogViewModel.cs`/`View.axaml` (new), `CookBookDetailView.axaml`, `CookBookDetailViewModel.cs` |
| T19 | the views named per defect, `Themes/Tokens.axaml`, `Styles.axaml`, `tests/Nfty.App.Tests/VisualCapture.cs`, `CLAUDE.md` |
| T20 | `tests/Nfty.App.Tests/VisualCapture.cs`, `DarkModeContrastTests.cs`, `BACKLOG.md` |

---

## Phase 1: data loss and correctness

### Task 1: The editor keeps a layer's color model (P-I6)

**Files:**
- Modify: `src/Nfty.App/ViewModels/IngredientEditorViewModel.cs:829-857` (`BuildColorization`) and the two `VariantImagery.RenderWith` call sites (`:643`, `:1417`)
- Modify: `src/Nfty.App/Imaging/VariantImagery.cs:71-92` (`RenderWith`)
- Test: `tests/Nfty.App.Tests/EditorColorModelTests.cs` (new)

**Interfaces:**
- Produces: `VariantImagery.RenderWith(IImageBridge bridge, Image<Rgba32> valueMap, bool dynamic, double hueMin, double hueMax, double satMin, double satMax, string fixedColor, ColorModel model, int salt = 0)`. `model` is new, placed before `salt`.
- Produces: `IngredientEditorViewModel.ColorModel` (read-only `ColorModel`), equal to `_ing.Manifest.Colorization?.Model ?? ColorModel.Hsv`. A Custom layer turned Dynamic/Static starts at HSV.

- [ ] **Step 1: Write the failing tests**, building an HSL Dynamic ingredient and an HSL Static ingredient in memory:
  - `Saving_an_hsl_dynamic_layer_keeps_it_hsl`: open, move `HueMax`, `Save`, reload the written `.igt`, and assert `Colorization.Model == ColorModel.Hsl`.
  - `Saving_an_hsl_static_layer_keeps_it_hsl`: same, editing `FixedColor`.
  - `Switching_kind_keeps_the_color_model`: HSL Dynamic → Static; `BuildColorization` (through the save) has `Model == Hsl`.
  - `The_editor_preview_colorizes_in_the_layers_own_model`: the editor's preview bitmap for an HSL Static layer has the same pixels as `VariantPreview` (Core) renders for that layer with the same fixed color. Compare through the bridge's ImageSharp frame. Before the fix the HSV render differs on a mid-gray value-map.
- [ ] **Step 2: Run** `dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~EditorColorModelTests`. Expected: 4 FAIL.
- [ ] **Step 3: Implement.** Replace the three `ColorModel.Hsv` literals in `BuildColorization` with `ColorModel`. `RenderWith` passes `model` to both `ColorRoller.FromFixed` and `Colorizer.Apply`, and both editor call sites pass `ColorModel`. Leave the wizards alone: they create layers (triage P-I6).
- [ ] **Step 4: Run.** The 4 tests PASS. Also run `--filter "FullyQualifiedName~IngredientEditor|FullyQualifiedName~EditorLivePreview"`: still green.
- [ ] **Step 5: Mutation probe.** Restore one `ColorModel.Hsv` literal in `BuildColorization`; the matching save test fails. Pass `ColorModel.Hsv` into `RenderWith`; the preview test fails.
- [ ] **Step 6: Commit** `fix(editor): keep an HSL layer HSL on save and in the preview`.

---

### Task 2: App-wide close guard for unsaved work (O-W1)

**Files:**
- Create: `src/Nfty.App/Services/IUnsavedWork.cs`
- Modify: `src/Nfty.App/Services/INavigationService.cs` (add `Pages`), `ShellViewModel.cs`, `IngredientEditorViewModel.cs:1722-1733` (`Back`), `RiseViewModel.Save.cs:~108-150` (the back command's body)
- Modify: `src/Nfty.Desktop/MainWindow.axaml.cs`
- Modify: every test fake implementing `INavigationService` (grep `: INavigationService` under `tests/`)
- Test: `tests/Nfty.App.Tests/CloseGuardTests.cs` (new)

**Interfaces:**
- Produces:
  ```csharp
  public interface IUnsavedWork
  {
      bool HasUnsavedWork { get; }
      /// Asks (save / discard / stay) when there is unsaved work; true = the page may go.
      Task<bool> ConfirmLeaveAsync();
  }
  ```
- Produces: `INavigationService.Pages`, an `IReadOnlyList<ViewModelBase>` listed top first.
- Produces: `ShellViewModel.ConfirmCloseAsync() : Task<bool>`.

**Behavior:**
- `ConfirmCloseAsync` walks `Pages` top first. For each `IUnsavedWork` page with `HasUnsavedWork`, it awaits `ConfirmLeaveAsync()`. The first `false` stops the walk and returns `false`.
- If a dialog is already open (`HasDialog`) and any page has unsaved work, it returns `false` without asking and says on the status line: `Close the open dialog first — {n} unsaved change(s) would be lost.` Pluralize by count.
- The editor implements `IUnsavedWork`:
  - `HasUnsavedWork` = `IsDirty`.
  - `ConfirmLeaveAsync` shows `LeaveDialogViewModel(_dialogs, "Leave the editor?", "{Name} has unsaved changes. Leaving discards them.", canSave: CanSave)`.
  - Save-and-leave awaits `Save()` and returns `!IsDirty`. Discard returns true. Stay returns false.
  - `Back` becomes `if (await ConfirmLeaveAsync()) _nav.Back();`. Its old Discard-only confirm card is replaced by the Leave card, which is the same choice Rise already offers.
- Rise implements it with today's back logic moved verbatim into `ConfirmLeaveAsync`, minus the `_nav.Back()` calls:
  - It still refuses while `!CanLeave()` (exporting or voxel-busy) by returning false and saying `Wait for the export to finish.`
  - It still awaits a running `_saveRun` before deciding.
- `MainWindow.OnClosing(WindowClosingEventArgs e)`:
  - When `_closeConfirmed` is set, it returns.
  - Otherwise it sets `e.Cancel = true` and fires `async` (`ConfirmCloseAsync()` true ⇒ `_closeConfirmed = true; Close();`).
  - This covers the titlebar close, Alt+F4 and the OS session end alike.
- House rule: a fire-and-forget gesture owes an observable handle. Expose `ShellViewModel.PendingClose` (a `Task<bool>?`) for tests, like `LandingViewModel.PendingOpen`.

- [ ] **Step 1: Write the failing tests** (`ShellViewModel` built as the existing shell tests build it, with a scripted dialog service answering the Leave card):
  - `A_clean_app_closes_without_asking`
  - `A_dirty_editor_asks_and_stay_keeps_the_app_open`
  - `A_dirty_editor_discard_lets_the_app_close`
  - `A_dirty_editor_save_and_leave_writes_then_closes`: the file on disk changed.
  - `A_dirty_page_below_the_top_is_asked_too`: push a dirty editor, then a clean page.
  - `Closing_while_a_rise_save_runs_waits_for_it_before_asking`: start a save whose `ICookBookSession.Replace` blocks on a gate; call `ConfirmCloseAsync`; assert no card yet; release the gate; assert a clean result returns true with no card shown.
  - `Closing_with_a_dialog_open_and_unsaved_work_is_refused_with_a_reason`
  - `The_editor_back_button_offers_save_as_well_as_discard`
- [ ] **Step 2: Run** `--filter FullyQualifiedName~CloseGuardTests`. Expected: FAIL (types missing).
- [ ] **Step 3: Implement as specified.** `NavigationService.Pages` returns `_stack.ToArray()`, which is already top first.
- [ ] **Step 4: Run** `CloseGuardTests` plus `--filter "FullyQualifiedName~Rise|FullyQualifiedName~IngredientEditor"`. Expected: PASS. The existing Rise leave-card tests must pass unchanged.
- [ ] **Step 5: Mutation probes.**
  - Make `ConfirmCloseAsync` look at `Current` only; `A_dirty_page_below_the_top_is_asked_too` fails.
  - Drop the `_saveRun` await; the save-running test fails.
- [ ] **Step 6: Commit** `fix(app): ask before closing the window over unsaved work`.

---

### Task 3: One atomic replace, one corrupt-item rule, a leak-free render (S-C1, S-X5, S-C9, S-C16)

**Files:**
- Create: `src/Nfty.Core/Formats/AtomicFile.cs`, `src/Nfty.Core/Output/SetItemFile.cs`
- Modify (Core): `Demo/DemoCookBook.cs:82-86`, `Publish/SheetGrid.cs:292-309`, `Output/SetReader.cs:143-155`, `Output/SetWriter.cs:245-270,352-372,677`, `Generation/Generator.cs:~400-425` (`Render`)
- Modify (App/CLI): `App/Services/CookBookPersistence.cs:34-80`, `App/Services/LooseWorkspace.cs:54-60,81-87`, `App/ViewModels/IngredientEditorViewModel.cs:1606-1620`, `App/Rise/PictureRiseSource.cs:568-576`, `App/Rise/VariantRiseSource.cs:251-262`, `Cli/CommandFactory.Authoring.cs:884-898`
- Leave `Model/ModelBatch.cs:370` alone (S-M1).
- Test: `tests/Nfty.Core.Tests/AtomicFileTests.cs`, `SetItemFileTests.cs`, `GeneratorRenderDisposalTests.cs` (new)

**Interfaces:**
- Produces:
  ```csharp
  public static class AtomicFile
  {
      public static void Replace(string path, Action<string> writeTemp);
      public static Task ReplaceAsync(string path, Func<string, Task> writeTemp);
      internal static string TempBeside(string path);  // $"{path}.{Guid.NewGuid():N}.tmp"
  }
  ```
  The writer gets a temp path that does not exist yet. On success the temp file is moved over `path` (`overwrite: true`). On any throw the temp file is deleted (best effort) and the exception propagates unchanged. Async awaits use `ConfigureAwait(false)`.
- Produces: `internal static class SetItemFile { static NftyMetadata ReadNfty(string path, string json); static OpenSeaMetadata ReadOpenSea(string path, string json); }`. Both throw `CorruptSetException(path, …)` for unparsable JSON, a literal `null`, or a missing required field. The wording follows `ReadDnaAndNumber`'s existing messages.

**Behavior:**
- `SetReader.Load`, `SetWriter.ReadDnaAndNumber`, `LoadExisting` and the deleted `Deserialize<T>` all route through `SetItemFile`. `SetReader` no longer drops `null` items silently; this is the triage ruling.
- `Generator.Render` changes:
  - Resolve `absentLayers` before compositing.
  - Clear `images` after disposing it.
  - Dispose `composed` if anything throws after it exists.

- [ ] **Step 1: Write the failing tests.**
  - `Replace_writes_through_a_unique_temp_and_moves_it_into_place`
  - `A_throwing_writer_leaves_the_original_untouched_and_no_temp_behind`
  - `A_stale_fixed_tmp_beside_the_file_does_not_block_the_replace`: pre-create `path + ".tmp"` and assert it survives untouched.
  - `ReplaceAsync_matches_Replace`
  - `A_null_item_file_is_reported_as_corrupt_by_every_reader`: a Theory over `SetReader.Read`, `SetReader.ReadAsync`, `SetWriter.ReadExisting` and `SetWriter.Write` in extend mode. Assert `CorruptSetException` with `.Path` ending in `0007.json`.
  - `An_unparsable_opensea_file_is_reported_as_corrupt_on_extend`
  - `Render_disposes_every_image_when_a_late_step_throws`: an ingredient id missing from `ingById` after compositing. Use a counting disposal hook through a test subclass, or assert `Image` is disposed via `ObjectDisposedException` on access.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~AtomicFileTests|FullyQualifiedName~SetItemFileTests|FullyQualifiedName~GeneratorRenderDisposalTests"`. Expected: FAIL.
- [ ] **Step 3: Implement Core.** Move the guid-temp remark from `CookBookPersistence.TempBeside` to `AtomicFile`, where it now governs. Point `DemoCookBook.WriteTo` and `SheetGrid.WriteInto/RemoveFrom` at it. Keep `SheetGrid`'s read-only fallback behavior.
- [ ] **Step 4: Run Core.** Expected: PASS, and also `--filter "FullyQualifiedName~SetWriter|FullyQualifiedName~SetReader|FullyQualifiedName~SheetGrid|FullyQualifiedName~DemoCookBook|FullyQualifiedName~Corrupt"`.
- [ ] **Step 5: Commit Core** `fix(core): one atomic replace and one corrupt-item rule`.
- [ ] **Step 6: Adopt it in the App and CLI.** Replace every listed hand-rolled temp+move with `AtomicFile.Replace/ReplaceAsync`. Then run `--filter "FullyQualifiedName~ExplorerTreeReorder|FullyQualifiedName~CookBookPersistence|FullyQualifiedName~Loose|FullyQualifiedName~RiseSave|FullyQualifiedName~Authoring"` in `tests/Nfty.App.Tests` and `tests/Nfty.Cli.Tests`. Fix any test that globbed `*.tmp` by exact fixed name.
- [ ] **Step 7: Mutation probes.**
  - Make `TempBeside` return `path + ".tmp"`; the stale-tmp test fails.
  - Restore `SetReader`'s `.Where(m => m is not null)`; the null-item theory fails.
- [ ] **Step 8: Commit App/CLI** `fix(app,cli): write every replaced file through AtomicFile`.

---

## Phase 2: parallelism and async

### Task 4: `Parallelism`, an honest cap, and a determinism test that means it (T-D1, T-D2, T-R1..R6, T-A1)

**Files:**
- Create: `src/Nfty.Core/Parallelism.cs`
- Modify: `src/Nfty.Core/ParallelWork.cs`, `Generation/GeneratedSet.cs:109-117`, `Generation/Generator.cs`, `Imaging/Compositor.cs:15-30`, `Output/SetWriter.cs:65-175`, `Diagnostics/Perf.cs` (table header)
- Test: `tests/Nfty.Core.Tests/ParallelismTests.cs` (new), `GeneratorParallelismTests.cs` (rewrite the theory), `SetWriterParallelismTests.cs` (new)

**Interfaces:**
- Produces:
  ```csharp
  namespace Nfty.Core;
  public sealed record Parallelism(int Requested)        // 0 = all; negative → ArgumentOutOfRangeException
  {
      public static int LogicalProcessors { get; }        // Environment.ProcessorCount
      public static int DefaultDegree { get; }            // Math.Max(1, LogicalProcessors * 2 / 3)
      public static Parallelism Default { get; }          // new(DefaultDegree)
      public static Parallelism Sequential { get; }       // new(1)
      public static Parallelism All { get; }              // new(0)
      public int Effective { get; }                       // Requested == 0 ? LP : Math.Clamp(Requested, 1, LP)
      public bool IsSequential { get; }                   // Effective == 1
      internal Configuration ImageConfiguration(bool insideParallelBody);
  }
  ```
  - `ImageConfiguration` returns a cached `Configuration.Default.Clone()` with `MaxDegreeOfParallelism = insideParallelBody ? 1 : Effective`. Cache it in a static `ConcurrentDictionary<int, Configuration>` keyed by degree, so the record's equality stays `Requested` alone.
  - **Never mutate `Configuration.Default`.**
  - `LogicalProcessors` is a property with an `internal static Func<int>` override seam, for tests only.
- Produces: `GenerateOptions(..., bool EnforceUniqueDna = true, Parallelism? Parallelism = null)`, appended last; `null` means `Parallelism.Default`.
- Produces:
  - `SetWriter.Write(GeneratedSet set, string outDir, bool pack, IAssetModeler? models = null, IProgress<WriteProgress>? progress = null, CancellationToken cancellationToken = default, Parallelism? parallelism = null)`.
  - `SetWriter.WriteAsync` gains `Parallelism? parallelism = null` last.
- Produces (internal):
  - `ParallelWork.For(int count, Parallelism p, CancellationToken ct, Action<int> body)`.
  - `ForEach<T>(…, Parallelism p, …)`.
  - `ForEachAsync<T>(…, Parallelism p, …)`.
  - `ForEachReporting<T>(IReadOnlyList<T> items, Parallelism p, CancellationToken ct, Action<T> body, Action<int>? reportDone)`. It calls `reportDone(done)` serialized under one lock, with `done` strictly increasing (T-R3).
  - The old signatures are removed; every caller passes `Parallelism`.
- Consumes: `Compositor.Composite(Dimensions, IReadOnlyList<Image<Rgba32>>, Configuration? configuration = null)`. A new optional parameter is passed to `Mutate(configuration, …)`.

**Behavior:**
- Sequential (`IsSequential` or `count == 1`) is a plain `for` loop with a per-item `ThrowIfCancellationRequested`, so no scheduler is involved.
- In parallel, `ParallelOptions { MaxDegreeOfParallelism = p.Effective, CancellationToken = ct }` is used for both `Parallel.For` and `Parallel.ForEachAsync`.
- **T-R6:** each body is wrapped to capture `(index, exception)` in a `ConcurrentBag`. After the loop, the exception with the lowest index is rethrown via `ExceptionDispatchInfo`. `OperationCanceledException` still wins when the token fired.
- Render bodies pass `p.ImageConfiguration(insideParallelBody: !p.IsSequential)` down to `Compositor.Composite`.
- `SetWriter.Write` gains the CT/progress the async twin has (T-A1), reported through `ForEachReporting`.
- **T-R2:** add a comment on `Generator.Render` that shared `VariantImages` are read-only across bodies, with the rule "never `Mutate` a source image".
- **T-R4:** the `Perf` table header says `ms (summed across threads)`.

- [ ] **Step 1: Write the failing tests.**
  - `Default_is_two_thirds_of_the_logical_processors_floored_with_a_minimum_of_one`: a Theory over LP {1,2,3,4,8,12,16,32} via the seam, expecting {1,1,2,2,5,8,10,21}.
  - `Effective_is_clamped_to_the_logical_processor_count`: `new Parallelism(64).Effective == LP`; `new(0).Effective == LP`.
  - `A_negative_degree_is_refused`
  - `Parallelism_is_equal_by_requested_degree_alone`
  - `The_inner_image_configuration_is_single_threaded_inside_a_parallel_body`, and `Configuration.Default.MaxDegreeOfParallelism` is unchanged after building one.
  - `The_sequential_path_runs_on_the_calling_thread`: record `Environment.CurrentManagedThreadId` per body.
  - `The_lowest_failing_index_is_the_exception_reported`: 64 items, items 5 and 40 throw distinct messages, run at `All` 20 times, always message 5.
  - `Progress_is_reported_in_increasing_order_one_call_at_a_time`
  - Rewrite `The_collection_does_not_depend_on_how_many_threads_render_it(int threads)` for threads ∈ {1, 2, 3, 8, 0}:
    - Pass `opts with { Parallelism = new(threads) }`.
    - Compare DNA, order and pixel hashes against the `GenerateStreaming` oracle.
    - Keep `SetMinThreads(Math.Max(threads, 2))` as the second lever so 8 workers can exist. Comment why.
    - Delete the claim that min threads is "the lever".
  - `The_same_variant_renders_identically_on_many_threads_at_once` (T-R2).
  - `SetWriter_writes_identical_bytes_at_one_and_many_threads`: write the same Set at `Sequential` and `All`, and compare every file's bytes by relative path.
  - `SetWriter_Write_honours_cancellation_and_reports_progress`.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~ParallelismTests|FullyQualifiedName~GeneratorParallelism|FullyQualifiedName~SetWriterParallelism"`. Expected: FAIL.
- [ ] **Step 3: Implement as specified.** `IngredientArchive`'s two `ParallelWork.For` calls pass `Parallelism.Default` for now; T7 threads the parameter through.
- [ ] **Step 4: Run** the filter above plus `--filter "FullyQualifiedName~Generator|FullyQualifiedName~SetWriter|FullyQualifiedName~IngredientArchive"`. Expected: PASS.
- [ ] **Step 5: Mutation probes.**
  - Drop `MaxDegreeOfParallelism`; the sequential-thread test still passes, but add a body that records the max concurrent count with `Interlocked`, and assert it ≤ `Effective` (`The_loop_never_runs_wider_than_effective`). It fails.
  - Rethrow `InnerExceptions[0]`; the lowest-index test goes flaky red within 20 runs.
- [ ] **Step 6: Commit** `feat(core): a configurable Parallelism with an honest cap`.

---

### Task 5: The thread-count Settings card (T-D4, author ruling Q2)

**Files:**
- Create: `src/Nfty.App/Services/IParallelismSetting.cs`, `src/Nfty.App/ViewModels/SettingsDialogViewModel.cs`, `src/Nfty.App/Views/SettingsDialogView.axaml(.cs)`, `assets/icons/settings.svg`; then regenerate `Themes/Icons.axaml` with `python tools/icons/build.py` and commit both.
- Modify: `src/Nfty.App/Views/ShellChromeView.axaml:~261` (a status-bar `helpbtn`-class button beside Help, `ToolTip.Tip="Settings"`), `ShellViewModel.cs` (`ShowSettingsCommand`), `ServiceRegistration`.
- Test: `tests/Nfty.App.Tests/SettingsDialogTests.cs` (new); join `DarkModeContrastTests.Screens()`, `ModalFitTests`, `VisualCapture` (`zz-dialog-settings-auto`, `zz-dialog-settings-custom`).

**Interfaces:**
- Produces:
  ```csharp
  public interface IParallelismSetting
  {
      Parallelism Current { get; }
      bool IsAuto { get; }
      void Set(int? degree);     // null = Auto
      event Action? Changed;
  }
  ```
  - `ParallelismSettingService(IStateStore store, Func<string?> env)` persists `.nfty/settings.json` `{ "threads": null | n }` with an options instance matching `IEditorViewState`'s (camelCase out, case-insensitive in). T12 folds it into `StateJson`.
  - Precedence: a valid `NFTY_THREADS` wins and the card says so; then the saved value; then `Parallelism.Default`.
  - A saved value above `LogicalProcessors` (settings copied from a bigger machine) clamps through `Effective`, and the card shows the clamp.
- Consumed by T8 (cook, export, archive reads, thumbnail gate).

**Behavior:**
- The card is `.modal` and fixed-size; it never scrolls. It has three parts:
  - Heading `Settings` (no eyebrow; a title alone).
  - One row: `WORKER THREADS`, a two-segment tray `Auto · Custom`, and a `NumericUpDown.nin` at `FieldHeightSm`, `FormatString="0"`, `MinWidth="0"`, `Minimum=1`, `Maximum=LogicalProcessors`.
  - Below the row, one muted line: Auto reads `Auto uses {DefaultDegree} of {LP} logical processors.`; Custom reads `Results are identical at any thread count; fewer threads leave more of the machine free.`
- The stepper is always laid out. In Auto it is disabled and shows `DefaultDegree`; do not disable muted text (swap ink instead).
- When `NFTY_THREADS` is set, the tray and stepper are disabled and the line reads `Set by NFTY_THREADS={n} for this session.`
- Footer: `Close`. Escape closes it via `CloseDialogCommand`.
- KeyHints: no new chord. The button's tooltip names only "Settings".

- [ ] **Step 1: Write the failing tests.**
  - `Auto_is_the_default_and_names_two_thirds_of_the_machine`
  - `A_custom_count_persists_across_a_restart`: a new service over the same temp store.
  - `NFTY_THREADS_overrides_the_saved_setting_and_says_so`
  - `A_saved_count_above_this_machine_is_clamped_and_shown`
  - `The_stepper_is_laid_out_in_auto_mode`: same `Bounds` both modes, `Opacity`/`IsEnabled` only.
  - `The_settings_card_fits_the_smallest_window` (in `ModalFitTests`)
  - `The_settings_button_opens_the_card` (a click gesture test, not a command call)
  - `The_settings_card_is_contrast_checked_in_its_env_locked_state` (in `DarkModeContrastTests.Screens()`)
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~SettingsDialog|FullyQualifiedName~ModalFit|FullyQualifiedName~DarkModeContrast|FullyQualifiedName~IconSource|FullyQualifiedName~DeadCodeAudit"`. Expected: the new tests FAIL.
- [ ] **Step 3: Implement.** Draw the glyph in the 24-unit box, stroked `currentColor`, different from its neighbors (Help, zoom). Run `build.py`.
- [ ] **Step 4: Run** the filter. Expected: PASS. Capture both frames in both themes and look.
- [ ] **Step 5: Mutation probes.**
  - Swap the env/saved precedence; the override test fails.
  - Set `IsVisible="{Binding IsCustom}"` on the stepper; the laid-out test fails.
- [ ] **Step 6: Commit** `feat(app): a Settings card for the worker thread count`.

---

### Task 6: Core never captures its caller's context (T-U2, S-C6, T-R10, T-U1)

**Files:**
- Modify: every `await` in `src/Nfty.Core/**/*.cs`. Per the audit, that is `ArchiveIo`, `SetWriter`, `SetReader`, `IngredientArchive`, `RecipeArchive`, `CookBookArchive`, `Kitchen`, `KitchenArchive`, `ParallelWork`, the new `AtomicFile`, and any others.
- Modify: `.editorconfig`; add:
  ```
  [src/Nfty.Core/**.cs]
  dotnet_diagnostic.CA2007.severity = warning
  ```
- Modify: `Publish/SpriteSheet.cs:157-163`, where the comment becomes true.
- Modify: `Publish/SetExporter.cs:213`. `ExportAsync`'s first statement is `await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);`, so the sync tail (extract, zip, PBKDF2) leaves the UI thread for every caller.
- Test: `tests/Nfty.Core.Tests/NoContextCaptureTests.cs` (new)

- [ ] **Step 1: Write the failing tests.** Use a single-threaded `SynchronizationContext` test helper that pumps on one dedicated thread and records the thread each continuation runs on.
  - `The_sync_twins_do_not_deadlock_under_a_single_threaded_context`. A Theory over `SetExporter.Export`, `MenuWriter.Write`, `SpriteSheet.Write`, and `CookBookArchive.Read` → `Write` round trip. Each is called from inside the context with a 30 s timeout. Today it hangs on the `SetReader.ReadAsync` path (watermark export).
  - `Export_returns_control_to_the_caller_before_doing_any_work`: start `ExportAsync` from the context thread, and assert the returned task is not completed when the call returns, and that no file exists yet.
- [ ] **Step 2: Run** `--filter FullyQualifiedName~NoContextCaptureTests`. Expected: FAIL or timeout.
- [ ] **Step 3: Implement.** Add the `.editorconfig` rule first and build. Every CA2007 error is a site to fix with `.ConfigureAwait(false)` (`await using` uses `.ConfigureAwait(false)` on the disposable). Do not suppress any. Then add the `ForceYielding` line.
- [ ] **Step 4: Build** `dotnet build nfty.sln` at 0 warnings. **Run** the new tests plus `--filter "FullyQualifiedName~Archive|FullyQualifiedName~SetExporter|FullyQualifiedName~MenuWriter|FullyQualifiedName~SpriteSheet"`. Expected: PASS.
- [ ] **Step 5: Mutation probe.** Remove `.ConfigureAwait(false)` from one `SetReader.ReadAsync` await. The build fails with CA2007, which shows the analyzer is live. With the rule disabled, the deadlock theory times out.
- [ ] **Step 6: Commit** `fix(core): no await in Core captures the caller's context`.

---

### Task 7: Fan-outs that cannot change a byte (T-G1, G3, G4, G6, G8, G9, T-R7)

Each fan-out follows the house shape:
- Independent bodies write into caller-owned slots, filled by **precomputed index**.
- Anything order-sensitive (dictionary insertion, zip entries, problem lists) is assembled **sequentially in the original order** afterwards.
- Progress goes through `ParallelWork.ForEachReporting`.
- On failure, every landed slot is disposed and temp files are deleted (T-R7).

**Files and API additions** (each a new optional `Parallelism? parallelism = null` last; `null` = `Default`):

| Id | Site | Shape |
|---|---|---|
| G1 | `SetExporter.StampAsync` (`:310-340`) | load, stamp and save each asset into the temp folder in parallel. `entries[i]` is set by index, not `IndexOf`. `ExportAsync`/`Export` gain `parallelism`. |
| G3 | `MenuWriter.WriteAsync` (`:108-130`) | flatten (recipe, ingredient, variant) to a list; parallel PNG encode + `DepthBytesToWrite` into slots; fill `encoded`/`depthBytesByKey` in list order. The atlas `Mutate` stays sequential with `ImageConfiguration(false)`. |
| G4 | `IngredientArchive.Write`/`WriteAsync` (`:223…`) | parallel PNG encode of every variant to `byte[]`; zip entries written sequentially in today's order. `RecipeArchive` and `CookBookArchive` writes pass it down. |
| G6 | `VoxelReport.Scan(book)` (`:79`) | `CountPartial` per variant into slots; rows assembled in today's order. |
| G8 | `SetReader.Load` (`:143`) | parse every `nfty/*.json` in parallel via `SetItemFile` into slots, then `OrderBy(SetNumber)`. |
| G9 | `SetWriter.ReadExisting`/`Async` | parse in parallel into slots; `Dnas` in enumeration order; `maxNumber` = max over slots. |

Archive **reads** (`CookBookArchive/RecipeArchive/IngredientArchive.Read*`) also gain `parallelism` and pass it to the existing decode loop.

- [ ] **Step 1: Write the failing tests.** One 1-vs-N identity test per site, each comparing `Parallelism.Sequential` against `Parallelism.All`:
  - `Watermarked_export_files_are_identical_at_one_and_many_threads`: per-file bytes of a folder export.
  - `Menu_bytes_are_identical_at_one_and_many_threads`: the `.mnu` file bytes, atlas and loose.
  - `Ingredient_archive_bytes_are_identical_at_one_and_many_threads`: also `CookBookArchive` via `Write` → bytes.
  - `Voxel_report_rows_are_identical_at_one_and_many_threads`
  - `Set_items_load_in_number_order_at_one_and_many_threads`: use a Set past 10,000 numbering, built with sparse numbers (1, 2, 9999, 10000), not 10k files.
  - `Existing_dnas_are_read_identically_at_one_and_many_threads`
  - `A_failing_stamp_leaves_no_temp_folder_behind`: one unreadable PNG; assert the `CorruptSetException`/`ImageFormatException` names the lowest-numbered bad asset and the temp folder is gone.
  - `A_failing_encode_disposes_nothing_twice_and_writes_no_archive`
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~one_and_many_threads|FullyQualifiedName~A_failing_stamp|FullyQualifiedName~A_failing_encode"`. Expected: FAIL (`parallelism` parameters missing).
- [ ] **Step 3: Implement** site by site in the table's order, building between each.
- [ ] **Step 4: Run** the filter plus `--filter "FullyQualifiedName~SetExporter|FullyQualifiedName~MenuWriter|FullyQualifiedName~Archive|FullyQualifiedName~VoxelReport|FullyQualifiedName~SetReader|FullyQualifiedName~Fixture"`. `FixtureArchiveTests` must still read the v2 fixtures. Expected: PASS.
- [ ] **Step 5: Mutation probes.**
  - Fill `encoded` in completion order (append from inside the body); the menu-bytes test fails.
  - Sort `SetReader` by path; the numbering test fails.
- [ ] **Step 6: Commit** `perf(core): stamp, encode, scan and parse in parallel without changing a byte`.

---

### Task 8: Hosts carry the thread count; the UI thread stops blocking (T-D3, T-U3, U4, U5, U11, T-R11, P-H3/I5, P-H13)

**Files:**
- CLI: `src/Nfty.Cli/CommandFactory.cs` (a global recursive option beside `--verbose` at `:32`, passed into `generate`, `extend`, `menu`, `export`, `inspect --voxel` and `model`), `CommandFactory.Publish.cs`, `CommandFactory.Model.cs`
- App:
  - Consume `IParallelismSetting` (created in T5).
  - Modify `CookDialogViewModel.cs` (`:147`, `:188-215`), `ExportDialogViewModel.cs:619`, `LandingViewModel.cs:433-440`, `ExplorerViewModel.cs:1462-1478`, `SetBrowserViewModel.cs:100-125`, `RiseViewModel.Export.cs` (the `ModelBatch` calls take no parallelism; leave them), `src/Nfty.Desktop/Program.cs`, `App.axaml.cs`
- Test: `tests/Nfty.Cli.Tests/ThreadsOptionTests.cs`, `tests/Nfty.App.Tests/OffUiThreadTests.cs`, `CookProvenanceTests.cs`, `StartupPathTests.cs` (new)

**Behavior:**
- **CLI `--threads <n|max>`.**
  - The default prints as `default: {DefaultDegree} of {LP} logical processors`.
  - `max` and `0` mean all. A negative or non-numeric value is a parse error.
  - Above LP, stderr prints exactly `--threads {n} exceeds {LP} logical processors; using {LP}.`, and the run continues.
  - `NFTY_THREADS` is read only when `--threads` is not given, with the same parse; an invalid value is an error naming the variable.
  - The value goes into `GenerateOptions.Parallelism`, `SetWriter.Write`, `SetExporter.Export`, `MenuWriter.Write`, `VoxelReport.Scan` and the archive reads.
- **App thread count.** `IParallelismSetting.Current` goes into cook (`GenerateOptions`, `SetWriter.WriteAsync`), export and archive reads. It is read at the start of each operation, so a change on the Settings card applies to the next one.
- **U3:** `LandingViewModel.OpenPath` becomes `BusyViewModel.RunAsync(…, ct => CookBookArchive.ReadAsync(path, ct, parallelism))`. Keep `PendingOpen` observable.
- **U4:** after a cook, the Explorer reads the Set via `BusyViewModel.RunAsync` + `SetReader.ReadAsync`.
- **U5:**
  - The cook dialog's `VoxelReport.Scan` moves off the constructor, using `Task.Run` after the dialog is shown.
  - `BookIsTwoDOnly` starts false and the 3D row is reserved. Its note fades in by `Opacity` (reserve space, toggle ink).
  - The Cook button stays disabled until the scan lands, and `CookCommand` re-checks it inside.
  - Use the existing `PartialAlphaCache` if it answers for this book.
- **U11/R11:** `SetItemRow` thumbnail decode passes through one shared `SemaphoreSlim(IParallelismSetting.Current.Effective)`. `_disposed` is checked **before** the decode, after acquiring the gate.
- **P-H3/I5:**
  - After `ReadExistingAsync`, `SetProvenance.Warning(existing.CookbookSha256, _book.SourceSha256)` is shown in the cook card's done state.
  - It goes in a reserved line in `WarningBrush`, styled `TextBlock.warnline` if that exists. Otherwise add the class in `Styles.axaml`, both themes via tokens.
  - It is a warning, never a refusal: the CLI's rule, and Core's wording verbatim.
- **P-H13:**
  - `Program.Main(args)` stores the first existing path argument.
  - After the shell is shown, `App.OnFrameworkInitializationCompleted` calls `LandingViewModel.OpenFromCommandLineAsync(path)`. It routes by extension through the same code the recents use (`.cbk`, `.set`/folder, `.tin`, `.igt`, `.rcp`).
  - A non-existent or unknown path shows the ordinary error card. It never crashes.

- [ ] **Step 1: Write the failing tests.**
  - `Threads_defaults_to_two_thirds_of_the_machine`: parse with no option and assert the resolved value.
  - `Threads_max_means_every_logical_processor`
  - `Threads_above_the_logical_count_are_clamped_and_named`: capture stderr; use the `LogicalProcessors` seam set to 4 and pass `--threads 64`.
  - `A_negative_thread_count_is_a_parse_error`
  - `An_invalid_NFTY_THREADS_is_an_error_naming_the_variable`
  - `Generate_at_one_and_max_threads_writes_identical_sets`: end to end, comparing the bytes of both output folders.
  - `Opening_a_book_from_landing_does_not_decode_on_the_ui_thread`: a recording `IImageBridge`/hook, or assert `PendingOpen` is not completed synchronously and a `BusyViewModel` was shown.
  - `Opening_the_set_after_a_cook_goes_through_the_busy_card`
  - `The_cook_dialog_constructor_does_not_scan_pixels`: a book whose variant access throws on the constructor thread, or a counting `VoxelReport` seam.
  - `The_cook_button_waits_for_the_partial_alpha_scan`
  - `Thumbnail_decodes_never_exceed_the_thread_count`
  - `A_row_disposed_before_its_decode_starts_never_decodes`
  - `Extending_over_a_changed_book_says_so_and_still_extends`
  - `Extending_over_the_same_book_says_nothing`
  - `A_path_given_at_startup_opens_like_a_recent`
  - `A_missing_startup_path_shows_an_error_card`
- [ ] **Step 2: Run** `tests/Nfty.Cli.Tests --filter FullyQualifiedName~ThreadsOptionTests` and `tests/Nfty.App.Tests --filter "FullyQualifiedName~OffUiThread|FullyQualifiedName~CookProvenance|FullyQualifiedName~StartupPath"`. Expected: FAIL.
- [ ] **Step 3: Implement the CLI.** Run its tests, then commit `feat(cli): --threads and NFTY_THREADS`.
- [ ] **Step 4: Implement the App.** Run its tests plus `--filter "FullyQualifiedName~CookDialog|FullyQualifiedName~Landing|FullyQualifiedName~SetBrowser|FullyQualifiedName~ModalFit|FullyQualifiedName~DarkModeContrast"`. The cook card's height must not change between states; reuse V-10's measurement from T19 if it lands first, otherwise pixel-diff two frames.
- [ ] **Step 5: Mutation probes.**
  - Move the `_disposed` check after the decode; the disposed-row test fails.
  - Drop the provenance line; its test fails.
- [ ] **Step 6: Commit** `fix(app): open books and Sets off the UI thread, gate thumbnails, warn on a changed book`.

---

## Phase 3: CLI parity

### Task 9: `export` gains every GUI axis; `sheet grid`; `model` rises a recipe; one voxel-size rule (P-G1..G4, P-I4, P-G10, P-I1)

**Files:**
- Core: create `src/Nfty.Core/Publish/ModelVoxelSize.cs`.
- CLI: modify `CommandFactory.Publish.cs`, `CommandFactory.Model.cs:24-60,183-206`, and the `generate --voxel-size` builder (`ModelVoxelSizeOption`). Create `CommandFactory.Sheet.cs`.
- App: modify `RiseViewModel.Export.cs:39-40,140` and `Views/CookDialogView.axaml:117` to use the Core constants.
- Test: `tests/Nfty.Cli.Tests/ExportParityTests.cs`, `SheetCommandTests.cs`, `ModelRecipeTests.cs`; `tests/Nfty.Core.Tests/ModelVoxelSizeTests.cs` (new)

**Interfaces:**
- Produces: `public static class ModelVoxelSize { public const double Min = 0.001; public const double Max = 1000; public const int Decimals = 3; public static string? ProblemWith(double v); }`. It returns null when the value is finite and `Min <= v <= Max`; otherwise it returns `"Voxel size must be between 0.001 and 1000, but {v} was given."` (invariant culture).

**Export flags** (overrides after the preset, like the existing pairs):
- `--models` / `--no-models`.
  - `--models` on a Set with no `models/` is an error: `This Set has no 3D models; it was cooked without --models.`
  - `--no-models` is always accepted.
- `--sheet`; `--sheet-cols N`; `--sheet-rows N`.
  - Cols or rows without `--sheet` is an error naming the option.
  - Values < 1 are parse errors.
  - Core's `SpriteSheet` fit and `ProblemWith` messages surface verbatim.
- `--watermark`; `--watermark-corner top-left|top-right|bottom-left|bottom-right`.
  - The default is `bottom-right`.
  - The corner without `--watermark` is an error.
- **P-I4:** when `--book` is given, hash it (`CookBookArchive.HashOf`). If `SetProvenance.Warning(recorded, hash)` is non-null, print it to stderr and continue; never refuse.
- `PrintExport` adds nothing new; `Plan.Parts()` already names the sheet and models.

**`sheet grid <png> [--cols N --rows N | --cell WxH] [--clear]`:**
- With no options, it prints the embedded grid (`SheetGrid.Read`) or `none`, and the detected one (`SheetGrid.Detect`).
- With cols+rows or cell, it writes via `TryFromColumnsRows`/`TryFromCellSize` + `WriteInto`. A refusal prints the `Try*` message as the error.
- `--clear` uses `RemoveFrom`.
- Cols/rows with `--cell`, or `--clear` with either, is an error.

**`model x.rcp --seed S [--out …] [format/pivot/voxel options]`:**
- It rolls `StackRoll.ForRecipe(recipe, StackRoll.RngFor(seed))`, the same draw `preview x.rcp --seed S` makes.
- It combines through `StackDepth.Combine`, mirroring `RecipeRiseSource`'s `PairsOf(picks)` → `Combine` (`src/Nfty.App/Rise/RecipeRiseSource.cs:~400-410,535`).
- It writes through `ModelWriter`.
- `--seed` is required for `.rcp` and rejected for `.png`/`.igt`. `--variant` and `--front/--back` are rejected for `.rcp`.
- `PartialAlphaException` names the layer, as for every other route.

**Voxel size:** the CLI, the Rise page and the cook dialog all use `ModelVoxelSize.Min/Max/Decimals`. The Rise page clamp moves 0.01 → 0.001.

- [ ] **Step 1: Write the failing tests.**
  - `Export_no_models_drops_the_models_folder`
  - `Export_models_on_a_set_without_models_is_refused`
  - `Export_sheet_writes_a_spritesheet_with_its_grid_chunk`: read it back with `SheetGrid.Read`.
  - `Export_sheet_cols_without_sheet_is_refused`
  - `Export_watermark_stamps_the_chosen_corner`: pixel test on a 1-asset Set, asserting non-transparent ink in the bottom-left quadrant only.
  - `Export_watermark_corner_without_watermark_is_refused`
  - `Export_with_a_different_book_warns_and_still_exports`
  - `Sheet_grid_prints_none_and_the_detected_grid`
  - `Sheet_grid_writes_and_clears_the_chunk`
  - `Sheet_grid_refuses_cols_with_cell`
  - `Model_of_a_recipe_matches_the_stack_preview_draws`: the GLB from `model chest.rcp --seed s` equals the bytes `ModelWriter` writes from `StackDepth.Combine` over `StackRoll.ForRecipe(…, RngFor("s"))`.
  - `Model_of_a_recipe_requires_a_seed`
  - `Voxel_size_bounds_are_one_rule_everywhere`: a Theory over `ModelVoxelSize.ProblemWith` at 0, 0.0009, 0.001, 1000, 1000.1, NaN, ∞; plus a reflection-free check that the Rise VM clamps a set of 0.0005 to 0.001.
- [ ] **Step 2: Run** the new test classes. Expected: FAIL.
- [ ] **Step 3: Implement the Core `ModelVoxelSize`.** Run its test. Commit `feat(core): one voxel-size range`.
- [ ] **Step 4: Implement the CLI and the App call sites.** Run the new tests plus `--filter "FullyQualifiedName~Export|FullyQualifiedName~Model|FullyQualifiedName~CommandSurface"` in `Nfty.Cli.Tests` and `--filter "FullyQualifiedName~RiseExport|FullyQualifiedName~CookDialog"` in `Nfty.App.Tests`.
- [ ] **Step 5: Mutation probe.** Let `--sheet-cols` through without `--sheet`; its refusal test fails.
- [ ] **Step 6: Commit** `feat(cli): export models, sheet and watermark; sheet grid; model a recipe`.

---

### Task 10: CLI authoring parity (P-G5, G6, G8, G9, G13, G14/I7)

**Files:**
- Modify: `src/Nfty.Cli/CommandFactory.Authoring.cs` (the `remove`, `move` and `set` groups) and `CommandFactory.cs` (`validate :360`, `inspect` → `PrintCookBook :208`)
- Test: `tests/Nfty.Cli.Tests/AuthoringParityTests.cs` (new)

**Commands.** Every write goes through `AtomicFile`. Each command validates with `Validator` before writing, like the existing authoring commands, and prints one confirmation line in the existing style.

| Command | Core call | Notes |
|---|---|---|
| `remove ingredient <cbk\|rcp> --recipe R --id I` | `CookBookEdits.RemoveIngredient` | `--recipe` required on `.cbk`, rejected on `.rcp` |
| `remove variant <cbk\|igt> [--recipe R --ingredient I] --id V` | `CookBookEdits.RemoveVariant` / igt manifest edit | the last variant is refused with Core's message |
| `remove recipe <cbk> --id R` | `CookBookEdits.RemoveRecipe` | |
| `move recipe <cbk> --id R --to N` | `CookBookEdits.MoveRecipe` | `N` 1-based, as `remove rule --at` |
| `set variant <igt\|cbk> [--recipe R --ingredient I] --id V --weight W` | manifest edit | `W` > 0, invariant parse |
| `set recipe <cbk> --id R --weight W` | `CookBookEdits.UpsertRecipe` (keep the recipe) | `W` ≥ 0 (0 = shelved) |
| `set cookbook <cbk> [--name] [--collection-name] [--symbol] [--description] [--target-supply N\|none]` | manifest `with` | at least one option required |
| `set voxel-ready <cbk> [--dry-run]` | `VoxelReady.Plan` / `Apply` | prints each change; `--dry-run` writes nothing |
| `validate <cbk\|rcp\|igt>` | `Validate` / `ValidateRecipe` / `ValidateIngredient` | dispatched by `Archives.KindOf`; numbering unchanged |
| `inspect <cbk>` | header via `IdentityReport.Render` | adds Collection, TargetSupply and RecipeOrder; the per-recipe tree stays |

- [ ] **Step 1: Write the failing tests.** One per command row, each asserting the archive re-read shows the change:
  - `Remove_ingredient_drops_the_layer_and_its_order_entry`
  - `Remove_variant_refuses_the_last_one`
  - `Remove_recipe_drops_its_weight`
  - `Move_recipe_changes_presentation_order_only`: generate before/after with the same seed and assert identical DNAs.
  - `Set_variant_weight_rewrites_one_weight`
  - `Set_recipe_weight_zero_shelves_it`
  - `Set_cookbook_changes_the_collection_name_and_target_supply`
  - `Set_cookbook_without_options_is_refused`
  - `Set_voxel_ready_dry_run_writes_nothing`
  - `Set_voxel_ready_snaps_partial_alpha`
  - `Validate_accepts_a_loose_recipe_and_ingredient`
  - `Inspect_prints_the_collection_and_target_supply`
- [ ] **Step 2: Run** `--filter FullyQualifiedName~AuthoringParityTests`. Expected: FAIL.
- [ ] **Step 3: Implement**, reusing `IsGiven`/`RejectUnless` if T12 has landed; otherwise inline, and T12 folds them.
- [ ] **Step 4: Run** the new tests plus `tests/Nfty.Cli.Tests` (whole project; it is fast). Update the manual's CLI reference page (`docs/manual/reference/`) for every new command, in Reference voice.
- [ ] **Step 5: Mutation probe.** Make `move recipe` write the book unchanged. `Move_recipe_changes_presentation_order_only` asserts `RecipeOrder` changed as well as identical DNAs, so it fails. A DNA-only assertion would pass a no-op.
- [ ] **Step 6: Commit** `feat(cli): remove, move recipe, set weights/cookbook/voxel-ready, validate loose parts`.

---

## Phase 4: simplifications (no behavior change)

### Task 11: Core one-place helpers (S-C2, C3, C4, C5, C7, C8, C10, C11, C12, C17, C18, S-T1, S-T2, S-M2, A5/A6/A7/A9/A13 Core halves; Q8 Core)

**Files:** Core, Ticket and Model only. One commit per bullet group is fine; at minimum it is one Core commit.

**Interfaces (produced, consumed by T12):**
- `Nfty.Ticket.Lookup.ById<T>`: `public static Dictionary<string, T> ById<T>(IEnumerable<T> items, Func<T, string> id)`. Last wins, `StringComparer.Ordinal`. New file `src/Nfty.Ticket/ById.cs`, namespace `Nfty.Ticket` (MenuArchive's), class `Lookup`.
- `Nfty.Core.Formats.RecipeLayers.ById(LoadedRecipe) : Dictionary<string, LoadedIngredient>`, plus `TryResolve(LoadedRecipe, out List<LoadedIngredient>)`, which is `UniqueSpace.TryResolveLayers` moved.
- `LoadedCookBook.Recipe(string id) : LoadedRecipe` (throws `KeyNotFoundException` naming the id), plus `TryRecipe(string, out LoadedRecipe?)`. `LoadedRecipe.Ingredient(string id)` / `TryIngredient`.
- `LoadedRecipe.Empty(string id, string name) : LoadedRecipe`.
- `SetLayout.Stem(int number) : string`, which is `D4` invariant.
- `CookBookArchive.HashOfAsync(string path, CancellationToken ct = default) : Task<string>`.
- `Colorization.Starter(LayerKind kind, ColorModel model, ColorRange range, string fixedSpec) : Colorization?`, plus `ColorDefaults { HueQuantize = 12, SatQuantize = 4, Range = (0,360,40,100), FixedSpec = "hex:d6249f" }`.
- `Imaging.PixelCompare.Same(Image<Rgba32> a, Image<Rgba32> b) : bool`.
- `Text.Count(int n, string noun) : string`, giving `"1 recipe"` / `"2 recipes"` (regular `s` plural only; irregulars stay at call sites).
- `Dimensions.MaxPixels = 100_000_000`.
- **Q8:** `Nfty.Core.SizeText.Of(long bytes) : string`, with `ExportPlan.SizeText()`'s exact wording moved here: `"{b} bytes"` below 1 KiB, `"{kb:0} KB"`, `"{mb:0.0} MB"`, invariant. `ExportPlan.SizeText()` calls it, so its output is unchanged.

**Behavior-neutral edits:**
- **C3:** one private lazy `Rolls(...)` iterator in `Generator`. `Generate` = `Rolls(...).ToList()` then the parallel render; `Stream` = `foreach roll → Render → report → yield`. The dedup comment is kept once.
- **C4:** delete the duplicate comment at `Generator.cs:128-132`.
- **C5:** hoist `scope`.
- **C7/C8:** `SetWriter.RecordedAt(dir)` / `RecordedAtAsync` read `set.json` once. The twin bodies share one `Accumulate` fold.
- **C10:** every `"D4"` in Core/Model goes through `SetLayout.Stem`.
- **C12/S-T1:** `Convert.ToHexStringLower`.
- **C17:** `Ordinal`.
- **C2 sites in Core:** `Validator`, `UniqueSpace`, `SelectionOdds`, `CollectionReport`, `RarityCalculator`, `VoxelReport`, and `MenuArchive` via `ById<T>`. **Check id uniqueness before any `ToDictionary` that throws** stays as is; `ById` never throws on duplicates, and `Validator` reports them first.

- [ ] **Step 1: Write the tests that pin the new helpers.**
  - `ById_keeps_the_last_of_duplicate_ids`
  - `Stem_is_four_digit_and_culture_invariant`: under `ar-SA` and `de-DE`.
  - `Starter_matches_what_both_wizards_build`, for each kind, HSV.
  - `Text_count_pluralizes_regular_nouns`
  - `Same_is_false_for_one_differing_pixel`
  - `HashOfAsync_equals_HashOf`
  - `SizeText_words_bytes_kilobytes_and_megabytes_as_the_export_plan_did`: 0, 1023, 1024, 651,264, 1,258,291.
- [ ] **Step 2: Run** them. Expected: FAIL.
- [ ] **Step 3: Implement the helpers, then the call-site swaps.** Build after each group.
- [ ] **Step 4: Run** `tests/Nfty.Core.Tests`, `tests/Nfty.Ticket.Tests` and `tests/Nfty.Model.Tests` whole. Expected: PASS, with `GeneratorParallelismTests` (the oracle for C3), `UniqueSpace*`, `SelectionOddsTests`, `DemoCookBookTests` (615,600) and `FixtureArchiveTests` unchanged.
- [ ] **Step 5: Mutation probe.** In `Rolls`, swap the order of `seen.Add` and the cancellation check so one extra draw happens on a duplicate; `GeneratorParallelismTests` / `GeneratorTests` determinism must fail.
- [ ] **Step 6: Commit** `refactor(core): one place for by-id lookup, the roll loop, the stem, starters and counts`.

---

### Task 12: CLI and App helpers (S-X1..X4, S-A2, A3, A5..A17, C2/C11 sites; Q8 Rise site)

**Files:** CLI and App only. Two commits: CLI, then App.

**CLI:**
- **X1:** `internal static bool IsGiven(this ParseResult r, Option o)`.
- **X2:** `RejectUnless(ParseResult r, bool allowed, string why, params Option[] options)`, with names taken from `o.Name`.
- **X3:** a default parameter replaces the overload.
- **X4:** `AbsentSuffix(double)` + `VariantLine(string name, string id, double weight)` shared by both printers.
- Use `RecipeLayers.ById` at `:262`, `:330`.

**App:**
- **A2:** if `Avalonia.Reactive.AnonymousObserver<T>` is public in Avalonia.Base 12.1.1 (check the XML docs in the NuGet cache), delete the four private observers. Otherwise keep one `internal sealed class LambdaObserver<T>` in `src/Nfty.App/Views/` and delete three.
- **A3:** `IDialogService.ShowErrorAsync(string title, string message)` extension. Landing callers become `await`; check `LandingNewRecipeTests` still passes.
- **A5/A6/A7/A9/A13:** call the T11 helpers. The wizards call `Colorization.Starter(Kind, ColorModel.Hsv, …)`.
- **A8:** `RiseSource.SingleFlight(Func<Task> work, Action? after = null)`. `VariantRiseSource` passes `_writeBusy` clearing as `after`.
- **A10:**
  - `Services/StateJson`: `Options` (camelCase out, case-insensitive in) and `TryRead<T>(string? json) : T?` (catches `JsonException` only).
  - The three state services use it.
  - Recents becomes case-insensitive (a fix): `Recents_load_a_hand_edited_PascalCase_path`.
- **A11:** move the orphan summary to `CreateLooseIngredient`.
- **A12:** split the editor `Save` into `SaveLooseAsync(...)` / `SaveIntoBookAsync(...)`. Precompute the variant id `HashSet`. The lock check stays first in `Save()` and in `SaveIntoBookAsync`.
- **A14:** `RgbColor.ToAvalonia()` in `App/Imaging`.
- **A15:** `Problems.Join(IEnumerable<string>) => string.Join("\n", …)` in the App.
- **A16:** `ExplorerViewModel.CommitAsync(LoadedCookBook next, string? selectId)`.
- **C11:** `CookBookPersistence` uses `CookBookArchive.HashOfAsync`.
- **Q8 (S-C13):** delete `RiseViewModel.Export.cs`'s private `SizeText` (`:412`) and call `SizeText.Of`. The Rise export card now reads `1.2 MB` / `636 KB` / `512 bytes`. Update any Rise test asserting the old `B`/`0.#` strings to the new wording; that is the ruling, not a regression. Add `Rise_and_export_word_sizes_the_same_way`.

- [ ] **Step 1: Write the two new behavior tests**, `Recents_load_a_hand_edited_PascalCase_path` and `Rise_and_export_word_sizes_the_same_way` (FAIL). Everything else is pinned by existing tests.
- [ ] **Step 2: Implement the CLI.** Run `tests/Nfty.Cli.Tests` whole. Commit `refactor(cli): IsGiven, RejectUnless, shared printer lines`.
- [ ] **Step 3: Implement the App.** Run `tests/Nfty.App.Tests --filter "FullyQualifiedName~IngredientEditor|FullyQualifiedName~Explorer|FullyQualifiedName~Landing|FullyQualifiedName~Rise|FullyQualifiedName~Recents|FullyQualifiedName~Palette|FullyQualifiedName~EditorViewState|FullyQualifiedName~DeadCodeAudit|FullyQualifiedName~WiringCoverage"`. `Executing_save_on_a_locked_book_writes_nothing` must stay green.
- [ ] **Step 4: Mutation probe.** In `SaveIntoBookAsync`, drop the lock check; `Executing_save_on_a_locked_book_writes_nothing` fails.
- [ ] **Step 5: Commit** `refactor(app): shared error, single-flight, state JSON, commit and save helpers`.

---

### Task 13: One new-loose-part service (S-A4, author ruling Q9)

**Files:**
- Create: `src/Nfty.App/Services/LooseCreation.cs`
- Modify: `LandingViewModel.cs:240-310` (`NewRecipe`, `NewIngredient`), `ExplorerViewModel.cs:~965-1045` (`CreateLooseRecipe`, `CreateLooseIngredient`, `DefaultLoosePath`)
- Test: `tests/Nfty.App.Tests/LooseCreationTests.cs` (new); keep `LandingNewRecipeTests` green.

**Interfaces:**
- Produces:
  ```csharp
  public sealed class LooseCreation(/* the picker, dialog, Kitchen and status services both VMs inject today */)
  {
      Task<string?> NewRecipeAsync(NewRecipeViewModel result);         // path written, or null
      Task<string?> NewIngredientAsync(NewIngredientViewModel result);
  }
  ```

**Behavior** (Explorer's rules plus Landing's open-after-save):
- An empty `DerivedId` is refused with an error card.
- With a Kitchen open, the default path is `{kitchen}/{id}{ext}`, with no picker. Otherwise the picker opens.
- A throwing picker becomes an error card, never an unobserved exception.
- The write goes through `LooseWorkspace` (`AtomicFile`).
- `Validator` problems are joined by `Problems.Join` (`"\n"`).
- The caller opens the new part (a recipe in a loose recipe page, an ingredient in a loose editor) and records the recent. Both screens now do this.

- [ ] **Step 1: Write the failing tests.**
  - `With_a_kitchen_open_both_screens_write_into_it_without_asking`
  - `Without_a_kitchen_both_screens_ask_for_a_path`
  - `A_picker_that_throws_shows_an_error_and_writes_nothing`
  - `An_empty_id_is_refused_on_both_screens`
  - `Both_screens_open_the_new_part_after_saving`
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~LooseCreation|FullyQualifiedName~LandingNewRecipe|FullyQualifiedName~Explorer"`. Expected: the new tests FAIL.
- [ ] **Step 3: Implement.** Delete the four drifted copies.
- [ ] **Step 4: Run** the filter plus `WiringCoverageTests` and `DeadCodeAuditTests`. Expected: PASS.
- [ ] **Step 5: Mutation probe.** Bypass the Kitchen default on Landing only; the Kitchen theory fails for the Landing case.
- [ ] **Step 6: Commit** `refactor(app): one service creates loose recipes and ingredients`.

---

### Task 14: One zoomable pixel-canvas host (S-A1)

**Files:**
- Create: `src/Nfty.App/Views/PixelCanvasHost.cs`
- Create: `src/Nfty.App/ViewModels/IZoomPan.cs`, with `double Zoom`, `double PanX`, `double PanY`, `int GridSize` and `bool ShowPixelGrid`. It is implemented by `IngredientEditorViewModel` and `RiseViewModel`.
- Modify: `IngredientEditorView.axaml.cs:455-504,609-717`, `RiseView.axaml.cs:217-302,580-596`

**Interfaces:**
- Produces: `internal sealed class PixelCanvasHost`.
  - The constructor is `(Image image, Panel surface, Panel backdrop, Func<IZoomPan?> vm, Func<string, IBrush> brush)`.
  - Members: `Rect Geometry()`, `void ApplyCanvasTransform()`, `void ClampPan()`, `void ZoomAt(Point pointer, double factor)`, `void RebuildBackdrop()`, `void Detach()`.
- **CLAUDE.md's "zoom exists in exactly one function" becomes true again:** `Geometry()` is defined only here.

- [ ] **Step 1: No new behavior.** The pinning tests already exist: `CanvasGridTests`, `EditorOverlayTests`, `EditorLivePreviewTests`, the Rise canvas, zoom and gesture tests, and `RiseWriteThroughViewTests`. Run them first to record a green baseline: `--filter "FullyQualifiedName~CanvasGrid|FullyQualifiedName~EditorOverlay|FullyQualifiedName~EditorLivePreview|FullyQualifiedName~Rise"`.
- [ ] **Step 2: Extract.** Move the arithmetic as is; both views keep their own event wiring and call the host.
- [ ] **Step 3: Run** the same filter. Expected: PASS with no test edited. Capture `editor-zoom*` and `rise-*` frames before and after, and pixel-diff them: zero changed pixels (the dead-rule proof).
- [ ] **Step 4: Mutation probe.** Offset `Geometry()` by one pixel in the host; `CanvasGridTests` phase and the Rise overlay tests both fail. That shows one function serves both views.
- [ ] **Step 5: Commit** `refactor(app): one zoomable pixel-canvas host for editor and Rise`.

---

## Phase 4b: GUI parity (author rulings Q3–Q7)

Every task in this phase also owes the following:
- **Fit:** modal fit at the real modal area, about 1067x526 (`ModalFitTests`, derived from `ShellViewModel`, never a stated size).
- **Space:** reserve space and toggle ink. Prove it by pixel-diffing two captured states.
- **Contrast and capture:** every new screen or state is in `DarkModeContrastTests.Screens()` in its riskiest-ink state, and captured in `VisualCapture` in both themes, with the capture verifying its own state.
- **Wording:** US English. Any chord printed comes from `KeyHints`.
- **Commands:** `WiringCoverageTests` and `DeadCodeAuditTests` stay green. Each command whose body matters gets a gesture test that clicks the real control.

### Task 15: HSV / HSL toggle in the editor and both wizards (P-H5, author ruling Q4)

**Files:**
- Modify: `IngredientEditorViewModel.cs` (`ColorModel` becomes a settable `[ObservableProperty]`, marking dirty through `ColorsChanged`), `IngredientEditorView.axaml` (colorize rail), `NewIngredientViewModel.cs`/`NewIngredientView.axaml`, `ImportImageViewModel.cs`/`ImportImageView.axaml`, `IngredientDetailViewModel.cs:422` (label reads the saved choice; already does)
- Test: `tests/Nfty.App.Tests/ColorModelToggleTests.cs` (new)

**Behavior:**
- A two-segment tray `HSV · HSL` is its own row under the Dynamic/Static tray, so the two trays never read as one four-option control (the rail-tabs lesson).
- It is always laid out. On Custom it is disabled, with the existing Custom reason line.
- A Custom layer turned colorized starts at HSV.
- Changing it re-renders the preview at once (T1's `RenderWith` model) and marks the editor dirty.
- No confirm card: the range is the same numbers read in another model, so nothing is lost.
- The wizards pass the choice into `Colorization.Starter(kind, model, …)` (T11).
- `ApproxColorsText` is unchanged; the bucket count does not depend on the model.
- The CLI colorization verbs are deferred (ruling).

- [ ] **Step 1: Write the failing tests.**
  - `Choosing_hsl_in_the_editor_saves_an_hsl_layer`
  - `Changing_the_model_marks_the_editor_dirty_and_rerenders_the_preview`
  - `The_model_tray_is_laid_out_but_disabled_on_a_custom_layer`
  - `The_new_ingredient_wizard_creates_the_chosen_model`
  - `The_import_wizard_creates_the_chosen_model`
  - `Both_wizards_still_fit_the_smallest_window_with_the_model_row` (`ModalFitTests`)
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~ColorModelToggle|FullyQualifiedName~ModalFit|FullyQualifiedName~EditorStripAlignment|FullyQualifiedName~DarkModeContrast"`. Expected: FAIL.
- [ ] **Step 3: Implement.** In the wizards' `.modal.split`, the row goes in the COLOR column. That column never goes empty on Custom: the sentence stays and the tray dims.
- [ ] **Step 4: Run** the filter plus `IngredientEditor*`, `TextBaselineTests` and `MinimumWindowFitTests`. Capture `editor-color-hsl` and `wizard-ingredient-hsl` in both themes and look. Expected: PASS.
- [ ] **Step 5: Mutation probe.** Drop the dirty flag on the model change; the dirty test fails.
- [ ] **Step 6: Commit** `feat(app): choose HSV or HSL in the editor and both wizards`.

---

### Task 16: The book settings card (P-H7..H10, author ruling Q3)

**Files:**
- Core (own commit): `src/Nfty.Core/Editing/CookBookEdits.cs` gains:
  - `EditIdentity(LoadedCookBook book, string name, <collection record> collection, int? targetSupply)`, using the manifest's real collection type.
  - `SetRecipeWeight(LoadedCookBook book, string recipeId, double weight)`.
  - `SetPalette(LoadedCookBook book, IReadOnlyList<string> swatches)`.
  - Each returns a new book. No manifest field is added.
- App: create `ViewModels/BookSettingsDialogViewModel.cs`, `Views/BookSettingsDialogView.axaml(.cs)`. Modify `CookBookDetailView.axaml` (the title becomes a `Button.titlelink` opening the card, with a transparent presenter and the same text and geometry) and `ExplorerViewModel` (apply via `CommitAsync` behind the edit lock).
- Test: `tests/Nfty.Core.Tests/CookBookIdentityEditTests.cs`, `tests/Nfty.App.Tests/BookSettingsDialogTests.cs` (new)

**Behavior:**
- The card is `.modal.split` (880) and never scrolls.
- **Left, IDENTITY:**
  - Book name.
  - Collection name, with a live `mints as {collection} #1` under it (`.mintname` grammar).
  - Symbol.
  - Description.
  - Target supply: a stepper plus an explicit `None` choice, never 0.
- **Right, RECIPE WEIGHTS:** rows of name, weight `NumericUpDown` (`FormatString="0.##"`) and a share bar (`ShareWidthConverter`). Weight 0 reads `shelved`. More rows than fit page like the CookBook card's table.
- **PALETTE:** the book's swatches with ×, plus `Add the editor's saved palette` (from `IPaletteService`).
- Fields are `FieldHeight` form fields.
- **Save:**
  - It is disabled until something changes, and while the book is locked. The lock is re-checked inside the command.
  - `Validator` runs before the write, and problems are shown in the card.
  - The write goes through `CookBookPersistence`.
- A reserved muted footer line appears whenever a weight differs from the saved one: `Changing a weight changes which assets a seed produces.`

- [ ] **Step 1: Write the failing Core tests.**
  - `EditIdentity_changes_only_the_named_fields`
  - `SetRecipeWeight_zero_shelves_and_keeps_the_recipe`
  - `SetPalette_round_trips_through_the_archive`
  - `Identity_edits_do_not_change_the_collection_for_a_seed`
- [ ] **Step 2: Implement Core, run** `tests/Nfty.Core.Tests --filter FullyQualifiedName~CookBookIdentityEdit`, and **commit** `feat(core): edit a book's identity, recipe weights and palette`.
- [ ] **Step 3: Write the failing App tests.**
  - `The_card_opens_from_the_cookbook_title` (a gesture)
  - `Saving_renames_the_collection_shown_on_the_card`
  - `Target_supply_none_clears_it`
  - `A_weight_change_warns_that_the_collection_changes`
  - `The_card_refuses_to_save_on_a_locked_book_even_when_forced`
  - `An_invalid_edit_is_reported_and_not_written`
  - `The_card_fits_the_smallest_window_with_twelve_recipes`
  - `The_title_line_keeps_its_geometry_as_a_button`
- [ ] **Step 4: Implement the App and run** `--filter "FullyQualifiedName~BookSettings|FullyQualifiedName~ModalFit|FullyQualifiedName~DarkModeContrast|FullyQualifiedName~TextBaseline|FullyQualifiedName~UniqueDnaDisplay|FullyQualifiedName~CollectionNameOnCard"`. Expected: PASS. Capture `zz-dialog-book-settings` with a weight changed, in both themes.
- [ ] **Step 5: Mutation probe.** Remove the in-command lock check; the forced-save test fails.
- [ ] **Step 6: Commit** `feat(app): a book settings card for identity, recipe weights and palette`.

---

### Task 17: Cook dialog: one recipe, and extend shows the total (P-H1, P-I3, author rulings Q5, Q7)

**Files:**
- Modify: `CookDialogViewModel.cs` (`RecipeChoices`, `SelectedRecipe`, `ResultingTotalText`), `Views/CookDialogView.axaml`
- Test: `tests/Nfty.App.Tests/CookRecipeAndTotalTests.cs` (new)

**Behavior:**
- **Q5:**
  - A `ComboBox` labeled `RECIPE` offers `Any recipe (by weight)`, then the recipes in `RecipeOrder`. Shelved ones are listed as `{name} (shelved)`, since `--recipe` cooks them too.
  - The choice becomes `GenerateOptions.RecipeId`.
  - **Ruling made here:** while extending, the combo is disabled with the reserved reason `An extend continues the Set's own mix.` Restricting would silently change a Set's mix mid-collection.
- **Q7:**
  - A reserved `→ {N} total` (invariant, thousands-separated) sits beside Count. N = existing + Count while extending; on a fresh cook it is ink-less.
  - Count keeps meaning "how many more".
- **Fit:** measure the card in every state (fresh, extending, models on/off, running, done with T8's provenance line). The height must be identical across states and fit 1067x526 against the 19px slack. If the combo does not fit, take the room from the models note (T19 V-10 makes it two fixed lines). The card never scrolls.

- [ ] **Step 1: Write the failing tests.**
  - `Choosing_a_recipe_cooks_only_that_recipe`
  - `Any_recipe_cooks_by_weight`
  - `A_shelved_recipe_can_be_chosen_and_is_labeled`
  - `The_recipe_choice_is_disabled_with_a_reason_while_extending`
  - `Extending_shows_the_resulting_total`
  - `A_fresh_cook_reserves_the_total_without_ink`
  - `The_cook_card_is_one_height_in_every_state`
  - `The_cook_card_fits_the_smallest_window_with_the_recipe_choice`
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~CookRecipeAndTotal|FullyQualifiedName~CookDialog|FullyQualifiedName~ModalFit|FullyQualifiedName~DarkModeContrast|FullyQualifiedName~TextBaseline"`. Expected: FAIL.
- [ ] **Step 3: Implement and measure.** Capture `cook-recipe-choice` and `cook-extending-total` in both themes.
- [ ] **Step 4: Run** the filter. Expected: PASS.
- [ ] **Step 5: Mutation probe.** Drop `RecipeId` from the options; the only-that-recipe test fails.
- [ ] **Step 6: Commit** `feat(app): cook one recipe, and show the total an extend reaches`.

---

### Task 18: "Export a Menu…" from the CookBook card (P-H4, author ruling Q6)

**Files:**
- Create: `ViewModels/MenuExportDialogViewModel.cs`, `Views/MenuExportDialogView.axaml(.cs)`
- Modify: `CookBookDetailView.axaml:~346` (a `tbtn` `Export Menu…` beside Reports), `CookBookDetailViewModel.cs`
- Test: `tests/Nfty.App.Tests/MenuExportDialogTests.cs` (new)

**Behavior:**
- The card is `.modal`, fixed, and never scrolls. It has:
  - Heading `Export a Menu`.
  - The sentence `A Menu carries every weight, rule and color of this book plus its art, so a game or server can roll and render assets without the CookBook.`
  - An `Atlas · Loose` tray with one muted line each: `One PNG holds every variant; smallest to ship.` and `One PNG per variant; easiest to swap art later.`
  - A path row (picker; default `{book}.mnu` beside the `.cbk`).
  - A reserved determinate progress bar.
  - `Cancel` / `Export`.
- It calls `MenuWriter.WriteAsync(book, path, new MenuWriteOptions { Art = … }, progress, ct, parallelism: IParallelismSetting.Current)` (T7 parameter).
- An atlas too large shows `MenuWriter.CheckAtlasFits`'s message in reserved space and points at Loose.
- On success, the status line says `Wrote {file} ({SizeText.Of(bytes)}).` (Q8).
- It reads only, so it is allowed on a locked book.
- It is disabled on an invalid book, with the reason.

- [ ] **Step 1: Write the failing tests.**
  - `Exporting_an_atlas_menu_writes_a_readable_mnu`: `MenuArchive.Open` + `Ticketer.TryRoll` + `MenuRenderer.Render` equal `serve`'s output for the same seed.
  - `Exporting_a_loose_menu_writes_one_png_per_variant`
  - `An_atlas_too_large_is_refused_in_the_card_and_points_at_loose`
  - `The_menu_export_works_on_a_locked_book`
  - `The_menu_export_is_disabled_on_an_invalid_book_with_a_reason`
  - `The_button_opens_the_card` (a gesture)
  - `The_menu_card_fits_the_smallest_window`
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~MenuExportDialog|FullyQualifiedName~ModalFit|FullyQualifiedName~DarkModeContrast|FullyQualifiedName~ToolbarButtonParity|FullyQualifiedName~MinimumWindowFit|FullyQualifiedName~DnaSpaceLayout"`. Expected: FAIL.
- [ ] **Step 3: Implement.** The CookBook card's action band must still fit at the smallest window.
- [ ] **Step 4: Run** the filter. Capture `zz-dialog-menu-export` (atlas-refused state) in both themes. Expected: PASS.
- [ ] **Step 5: Mutation probe.** Ignore the tray and always write Atlas; the loose test fails.
- [ ] **Step 6: Commit** `feat(app): export a Menu from the CookBook card`.

---

## Phase 5: visual QC

### Task 19: Visual defects (V-1..V-13)

**Method for every item:**
1. Capture the frame named in the audit, both themes.
2. Confirm the defect is still present on the current HEAD. Step 10 rebuilt the Rise layers rail, so V-2 may differ.
3. Fix it.
4. Recapture and look.
5. Add the named layout assertion.

Defects that are gone on capture are recorded in the commit message as already fixed, and their test is still added.

| Id | Fix | Test (`tests/Nfty.App.Tests/`) |
|---|---|---|
| V-1 | New Recipe card in `.modal.split` (two columns, like the other wizards): identity left, Resulting mix + destination right. Remove the body `ScrollViewer`. The mix list is bounded by rows paged like the CookBook card if it can exceed the column. | `ModalFitTests`: `New_recipe_fits_the_smallest_window_with_a_many_recipe_book` (12 recipes) |
| V-2 | The layers rail scroller snaps `MaxHeight` to a whole number of row pitches, reading the room from the parent star row (the `RarityRailSnap` rule; `double.IsNaN` first). | `RiseLayersRailSnapTests.Rows_fold_on_a_row_boundary_at_every_size` (a 12-layer recipe) |
| V-3 | The validity list folds on whole rows, the same snap. | `ValidityDialogLayoutTests.Fourteen_problems_fold_on_a_row_boundary` |
| V-4 | No code change; it already snaps rows. Add it as the third allowed scrolling modal in CLAUDE.md ("Validity card = second modal allowed to scroll" bullet), for the same reason. | `ModalFitTests` exemption list names it with a reason |
| V-5 | The corner tile's caption + button row gets a backing band: new token `TileCaptionBrush` (panel ground at ~0.85 alpha) in **both** dictionaries, applied by one `Border.tilecap` style used by RiseView and IngredientEditorView. The enlarge button's focus ring uses the app's focus style, not Fluent's red. | `DarkModeContrastTests` scores the caption over a white-art and a black-art tile (add both states to `Screens()`) |
| V-6 | `VisualCapture` Set fixtures cook the Chest demo (`DemoCookBook`) instead of transparent placeholders. | `CaptureReferences`-style self-check: the capture helper asserts the first tile's thumbnail has a non-transparent pixel before saving |
| V-7 | The Rise asset grid's row host snaps to whole tile rows (the same snap). | `RiseAssetGridSnapTests.Tiles_fold_on_a_row_boundary_at_the_smallest_window` |
| V-8 | The editor footer hint sits in a star column with `TextTrimming` off and wraps; the Back button is in an `Auto` column with an 8px gap. | `EditorFooterLayoutTests.The_rise_hint_never_touches_the_back_button` (ink measured with `FormattedText`) |
| V-9 | The import name-collision sentence uses `WarningBrush` and aligns to the NAME column's left edge. | `ImportImageLayoutTests.Collision_sentence_aligns_with_the_name_field` |
| V-10 | The cook card's 3D-row note gets a fixed two-line height (reserved), so both states are the same height. | `CookDialogLayoutTests.The_card_is_the_same_height_in_every_models_state` (measured, not a literal) |
| V-11 | The format descriptions wrap (`TextWrapping="Wrap"`, a stated `MaxLines` of 2) inside the reserved row height; the full text is on the tooltip. | `RiseExportLayoutTests.Format_descriptions_are_never_ellipsized` |
| V-12 | The gallery harness gives kind chips their real stated width. | none (harness only); check the frame |
| V-13 | The disabled "Export model…" drops the accent class while disabled (the neutral `tbtn` style), so the 0.38 dim lands on a neutral ground. | `DarkModeContrastTests` sealed-export state (already in `Screens()`; confirm it fails before the fix) |

- [ ] **Step 1: Capture the baseline** (the full `VisualCapture` run) to a scratch directory and confirm each defect.
- [ ] **Step 2: Write the failing layout tests** listed above. Run `--filter "FullyQualifiedName~ModalFit|FullyQualifiedName~Snap|FullyQualifiedName~LayoutTests|FullyQualifiedName~DarkModeContrast"`. Expected: the new tests FAIL.
- [ ] **Step 3: Fix each defect.** One commit per defect or per pair of the same shape (`fix(app): …`).
- [ ] **Step 4: Run** the filter plus `MinimumWindowFitTests`, `TextBaselineTests` and `ThemeResourceTests`. Expected: PASS. Recapture and look at every touched frame in both themes.
- [ ] **Step 5: Mutation probes.**
  - Remove one snap; its test fails at at least one size.
  - Remove `TileCaptionBrush` from the dark dictionary; `ThemeResourceTests` fails.
- [ ] **Step 6: Commit CLAUDE.md** (the V-4 note) as `docs: the voxel-ready card is the third modal allowed to scroll`.

---

### Task 20: Missing capture states, BACKLOG for deferred items, and the full suite (V-M1..M7, V-M9)

**Files:**
- Modify: `tests/Nfty.App.Tests/VisualCapture.cs` and `DarkModeContrastTests.cs` (`Screens()`). Each new screen joins in its riskiest-ink state.
- Modify: `BACKLOG.md`

**Captures to add.** Each capture helper verifies the state it claims before saving (the `CaptureReferences` rule).

| Id | Frames |
|---|---|
| V-M1 | `report-collection`, `report-identity` |
| V-M2 | `passphrase-prompt`, `passphrase-wrong`, `passphrase-viewonly` |
| V-M3 | `wizard-kitchen`, `kitchen-workspace` |
| V-M4 | `zz-dialog-validity-halfbuilt` (empty recipe named) |
| V-M5 | `set-browser-art` (Chest demo), `set-browser-missing` (a tile with `ImageMissing`), `set-browser-loading`, `set-browser-hover`, `set-browser-odds` (`1 in N`, exact with the book reachable) |
| V-M6 | `ingredient-12-variants` (page 1 of the 2x2 strip), `explorer-long-names`, `explorer-filtered`, `editor-locked` |
| V-M7 | `export-folder`, `export-seal-mismatch`, `export-corner-disarmed`, `export-sheet-refused` |
| V-M9 | `cook-running` (a determinate bar), `cook-failed` |

**BACKLOG entries.** One section each, with the reason and what would pick it up, in BACKLOG's existing shape:
- T-G2 (sheet decode window)
- T-G5 (book-wide decode)
- T-G7 (parallel Validator pixel checks)
- T-G10/R12 (pipelined `ModelBatch.Export`)
- T-U6/U7 (measure CookBook card and Explorer validation/counting cost: add `Perf` scopes `Card.Validate`, `Card.UniqueSpace`, `Explorer.Validate` first)
- **S-C14 / author ruling Q1: a deep review, and possibly a merge, of the `UniqueSpace` and `SelectionOdds` walks.** The entry must carry enough to pick it up cold:
  - **What:** `UniqueSpace.Walk(depth, BigInteger)` and `SelectionOdds.Walk(depth, double)` walk the same tree. Both use the same selection dictionary, the same absent-as-omission rule, the same `RulesEngine.IsLegal` leaf and the same `DefaultEnumerationBudget` gate. `SelectionOdds`' own remark says "the walk is UniqueSpace's".
  - **Why not now:** the two walkers are one copy too many only once a third appears.
  - **Risk:** every DNA figure on every surface (the CLI `stats`, the CookBook card, the cook failure message, the Set rail's exact odds) depends on one of them. The `SpaceCertainty` direction (`Exact`/`AtLeast`/`AtMost`/`Unknown`) and the budget-exhaustion behavior must survive exactly. Float summation order in `SelectionOdds` must not change, since the order of additions is the number.
  - **Shape if done:** a generic `RuleWalk.Sum<T>` over `INumber<T>`, allocation-free.
  - **Pinned by:** `UniqueSpace*`, `SelectionOddsTests` (prediction against a real cook), `DemoCookBookTests` (615,600) and `GeneratorTests`' failure-message tests.
  - **Pick up when:** a third walker is written, or a bug is fixed in one walker that the other also has.

- [ ] **Step 1: Add the captures.** Run `NFTY_CAPTURE=1 NFTY_CAPTURE_DIR=<scratch> dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~VisualCapture`. Look at every new frame in both themes. Any defect found becomes a fix in this task, with its own layout test, or a BACKLOG entry if it is AUTHOR-shaped.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~DarkModeContrast|FullyQualifiedName~ModalFit|FullyQualifiedName~TextBaseline"`. Expected: PASS with the new screens.
- [ ] **Step 3: Write the BACKLOG entries**, including the Q1 entry above.
- [ ] **Step 4: Run the full suite** `dotnet test nfty.sln`, then `dotnet build nfty.sln` at 0 warnings. Record the pass counts in the commit body.
- [ ] **Step 5: Commit** `test(app): capture the states no frame showed; backlog what the audits deferred`.

---

## Not in this plan (see TRIAGE.md)

All nine AUTHOR questions are ruled (2026-10-27) and are planned above. Q1 is a BACKLOG entry only (T20). Still out of scope by ruling:
- CLI colorization, kind and palette verbs (Q4).
- Opening and serving a `.mnu` in the GUI (Q6).

Every SKIP carries its reason in TRIAGE.md.
