# Pop-out Viewer Implementation Plan (v2.1.0)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A read-only viewer, one OS window per subject, showing 2D and 3D side by side, with browsable archives, metadata JSON and sealed files view-only. It follows the Ingredient editor and the Rise page live, remembers its size, and opens from in-app buttons, Ctrl+click and middle-click.

**Architecture:** `ViewerViewModel` sits over a `ViewerSubject`, which is either a snapshot or live. `IViewerHost` decides where a viewer appears: a desktop window, a single-view overlay, or a recording host in tests. The 2D pane reuses `PixelCanvasHost` through `IZoomPan`. The 3D pane reuses `ModelPreviewView`. Archives open as a gallery through `ArchivePeek`, `SetReader`, `SealedSetReader`, `MenuArchive` and the Kitchen scan, with lazy decoding. Section 2 of the spec carries the diagram.

**Tech Stack:** .NET 10, Avalonia 12.1.1 (headless tests), xunit.v3, ImageSharp 3.1.11 (pinned). **No new packages.**

**Spec:** `docs/superpowers/specs/2026-10-27-pop-out-viewer-design.md`. The author's decisions are in `.superpowers/viewer/decisions.md`, and its scope ruling of 2026-10-27 governs. CLAUDE.md is the standing spec for every house rule.

## Global Constraints

- `TreatWarningsAsErrors` + `GenerateDocumentationFile`: every task leaves `dotnet build nfty.sln` at 0 warnings, with every public member documented.
- No new NuGet packages, and ImageSharp stays 3.1.11. There is no glTF library: `GltfReader` reads only nfty's own subset.
- `Nfty.Core` and `Nfty.Ticket` gain no UI dependency, and nothing references `Nfty.Model` from Core (`AssemblyRulesTests`).
- **Nothing in a viewer writes to a source.** Its only writes are Save image, Save still, Export model and the remembered window state.
- **Seal:** `AllowExport` travels on the SUBJECT. Every export control reads it. A subject derived from another (a gallery tile, a pop-out) inherits it.
- **Live subjects clone, never borrow.** The source's images are never handed to the viewer, because a borrowed image the source disposes kills the window (`SetInspectViewModel.Image` precedent).
- GUI house rules apply. Reserve space and toggle ink: use `Opacity`/`IsHitTestVisible`, and use `IsVisible` only at the end of a container. Use token brushes only, in both theme dictionaries. Every icon is a `StreamGeometry` generated from `assets/icons/*.svg`. Pixel art uses nearest-neighbor scaling. US English. Key hints come from `KeyHints`. `Path.ico` never takes its own `RenderTransform`.
- Tests are named `Snake_case_sentences`. Use `UiThreadWait.For`, never `GetAwaiter().GetResult()`, on the UI thread. Fixtures are built in memory, and filesystem tests use `Directory.CreateTempSubdirectory()`. Never touch the real `%APPDATA%`.
- Commit per task: Core and Model changes in one commit, App and Desktop changes in a second.
- Run each task's targeted tests. Run the full suite (`dotnet test nfty.sln`) once at the end of Task 6.

---

## Task 1: `ViewerSubject`, `ViewerViewModel` and the 2D pane state

**Files:**
- Create: `src/Nfty.App/Viewer/ViewerSubject.cs`, `SnapshotSubject.cs`, `ViewerModel.cs`, `ViewerFact.cs`
- Create: `src/Nfty.App/ViewModels/ViewerViewModel.cs`, `src/Nfty.App/ViewModels/TwoDPaneViewModel.cs`
- Create: `src/Nfty.Core/Imaging/Upscale.cs`
- Test: `tests/Nfty.App.Tests/ViewerViewModelTests.cs`, `tests/Nfty.Core.Tests/UpscaleTests.cs`

**Interfaces:**
```csharp
public abstract class ViewerSubject : IDisposable {
    public abstract string Title { get; }
    public abstract IReadOnlyList<ViewerFact> Facts { get; }      // record ViewerFact(string Label, string Value)
    public abstract Image<Rgba32>? Art { get; }                   // owned by the subject
    public abstract ViewerModel? Model { get; }                   // record ViewerModel(Mesh Mesh, Image<Rgba32> Texture, IReadOnlyList<ModelFile>? Files)
    public virtual bool IsLive => false;
    public bool AllowExport { get; init; } = true;
    public event Action? Changed;  protected void RaiseChanged();
}
public sealed class SnapshotSubject : ViewerSubject  // ctor clones art + texture; Files = the original model bytes when the subject came from a file
public enum ViewerLayout { TwoD, Both, ThreeD }
public enum ViewerBackdrop { Checker, Light, Dark }
public static class Upscale { public static Image<Rgba32> By(Image<Rgba32> src, int factor); } // factor in {1,2,4,8}, else ArgumentOutOfRangeException
```
- `TwoDPaneViewModel : IZoomPan`: `Zoom` is clamped to `[1, 64]`. `FitCommand` sets zoom 1 and pan 0. `OneToOneCommand` sets the zoom at which one art pixel equals one device pixel, which is `CanvasMapping`'s fitted scale inverted. `Backdrop` and `PixelGrid` are view state.
- `ViewerViewModel(ViewerSubject subject, IFilePickerService picker, IClipboardService clipboard, IDialogService dialogs)`: `Layout` defaults to `Both` when `Model != null` and to `TwoD` otherwise. `CanShowThreeD => Subject.Model != null`. `SaveImageCommand(int factor)`, `CopyCommand` and `CanExport => Subject.AllowExport`. Its `ExportNote` reads `"View-only: this file was sealed."` when export is blocked.

- [ ] **Step 1:** Write failing tests: `Upscale_by_four_repeats_every_pixel_in_a_four_by_four_block`, `Upscale_refuses_a_factor_that_is_not_1_2_4_or_8`, `A_subject_without_a_model_opens_in_2d_and_dims_the_layout_control`, `A_subject_with_a_model_opens_side_by_side`, `A_sealed_subject_disables_save_and_copy_and_says_why`, `Save_image_at_2x_writes_twice_the_art_size_with_whole_pixels`, `Fit_resets_zoom_and_pan`, `Zoom_is_clamped_between_1_and_64`, `Snapshot_subject_owns_a_copy_not_the_callers_image` (dispose the caller's image after construction, then read `Art`).
- [ ] **Step 2:** Implement `Upscale` (exact pixel copy, no ImageSharp resampler), the subjects and the VMs.
- [ ] **Step 3:** `dotnet test tests/Nfty.Core.Tests --filter FullyQualifiedName~UpscaleTests` and `dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~ViewerViewModelTests`. Both must pass.
- [ ] **Step 4:** Commit: `feat(core): whole-pixel Upscale` and `feat(app): viewer subject and view model`.

## Task 2: `ViewerView`, `IViewerHost`, the window, lifetime and remembered size

**Files:**
- Create: `src/Nfty.App/Views/ViewerView.axaml(.cs)` (root `UserControl.viewer`; header row: title, live badge, layout segment; body: 2D pane | 3D pane | info rail 240px; footer: export controls)
- Create: `src/Nfty.App/Services/IViewerHost.cs` (with `OverlayViewerHost`), `src/Nfty.App/Services/IViewerWindowState.cs`
- Create: `src/Nfty.Desktop/ViewerWindow.axaml(.cs)`, `src/Nfty.Desktop/DesktopViewerHost.cs`
- Modify: `src/Nfty.App/ServiceRegistration.cs`, `src/Nfty.Desktop/App.axaml.cs` (state `desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;`, register `DesktopViewerHost`), `assets/icons/popout.svg`, then run `python tools/icons/build.py`
- Test: `tests/Nfty.App.Tests/ViewerViewTests.cs`, `ViewerWindowStateTests.cs`, `tests/Nfty.App.Tests/RecordingViewerHost.cs`

**Interfaces:**
```csharp
public interface IViewerHost { void Open(ViewerSubject subject); }          // takes ownership of the subject
public sealed record ViewerWindowEntry(double Width, double Height, double? X, double? Y, bool Maximized);
public sealed record ViewerWindowState(ViewerWindowEntry TwoD, ViewerWindowEntry ThreeD);
public interface IViewerWindowState { ViewerWindowState Current { get; } void Save(ViewerWindowState state); } // file "viewer-window.json"
```
- Defaults are TwoD `900x700` and ThreeD `1280x760`, with no position. The window minimum is `640x480`. The 2D pane hosts `PixelCanvasHost` exactly as `RiseView` does, using `Image` + `surface` + `backdrop`, with the wheel anchored on the pointer and drag to pan. Checker is the existing lattice backdrop, and Light/Dark are `TileBrush`/`PanelBrush` grounds. The 3D pane is `ModelPreviewView`.
- **The layout switch reserves space.** The two panes share a fixed `*,*` grid. `TwoD` and `ThreeD` set the other column to `0` and hide that pane with `Opacity` and `IsHitTestVisible`. The rail's width is fixed, so nothing in the rail moves.
- Each `ViewerWindow` creates its own `DialogService` and hosts a dialog layer. `Escape` closes the window's open dialog, and with none open it does nothing. `Ctrl+W` closes the window.
- `DesktopViewerHost.Open`: if `MainWindow` is null or closed, owner = null. The window is NOT owned by the main window, because an owned window closes with its owner. Position is clamped to `Screens.All` working areas, and the window falls back to centered.
- `OverlayViewerHost` (registered only when the lifetime is `ISingleViewApplicationLifetime`): pushes `ViewerView` full-screen into the shell's overlay slot (new `ShellViewModel.Overlay` property + `ContentControl` above the dialog layer) and shows a close button. A second `Open` disposes and replaces the first.

- [ ] **Step 1:** Write failing tests: `App_states_shutdown_on_last_window_close` (reflect on `App` after `OnFrameworkInitializationCompleted` under the headless lifetime, or assert the source line through a static `App.Shutdown` constant that `App` assigns), `The_viewer_window_is_not_owned_by_the_main_window`, `Closing_a_viewer_writes_the_size_for_its_layout`, `A_corrupt_window_state_reads_as_defaults`, `A_remembered_position_off_every_screen_opens_centered`, `Switching_layout_moves_nothing_in_the_info_rail` (pixel-diff of the rail region across the three layouts), `The_viewer_fits_the_smallest_window` (render at 1067x526: no control desires more than given, and the rail's buttons are hit-testable), `Escape_with_no_dialog_open_leaves_the_viewer_open`, `A_second_overlay_replaces_and_disposes_the_first`.
- [ ] **Step 2:** Implement. Add `ViewerView` to `DarkModeContrastTests.Screens()` in the riskiest state: a sealed subject, so the disabled Save is shown together with `ExportNote`.
- [ ] **Step 3:** Targeted tests, plus `ThemeResourceTests`, `PixelPerfectRenderingTests`, `WiringCoverageTests`, `DeadCodeAuditTests`, `IconSourceTests` and `DarkModeContrastTests`. All must pass.
- [ ] **Step 4:** Look at the rendered frame of `ViewerView` at 1180x720 and 1067x526 in both themes before committing. Commit.

## Task 3: The 3D sources (`GltfReader`, Set models, turntable, presets, still and export)

**Files:**
- Create: `src/Nfty.Model/Gltf/GltfReader.cs` (`public static ReadModel Read(string path)` and `Read(ReadOnlySpan<byte> glb)`, where `ReadModel` = `Mesh` + texture PNG bytes. It throws `UnsupportedModelException` naming the feature.)
- Create: `src/Nfty.App/Viewer/ModelSubjects.cs` (Set asset → model file if `set.json` has `models`, else `SetRiseSource` by hash, else none; loose `.glb/.gltf`)
- Modify: `src/Nfty.App/ViewModels/ModelPreviewViewModel.cs` (add `Preset(ViewAngle)` where `ViewAngle { Front, Back, Side, Top }` frames on the bounds like `Reset`; add `Turntable` with `TurntableSpeed { Off, Slow, Medium, Fast }` = `0, 20, 45, 90` degrees per second, advanced by `Tick(TimeSpan)`, so tests need no timer); `ModelPreviewView` drives `Tick` from a `DispatcherTimer` at 30 Hz, which runs only while the speed is not `Off` and the view is attached; add `SaveStill` (encode the current raster target as PNG)
- Modify: `ViewerViewModel` (`ExportModelCommand`: copy `ViewerModel.Files` byte for byte when present, else `ModelWriter.Write` in the chosen `ModelTokens` format; `SaveStillCommand`; both disabled when `!AllowExport`)
- Test: `tests/Nfty.Model.Tests/GltfReaderTests.cs`, `tests/Nfty.App.Tests/ViewerThreeDTests.cs`

- [ ] **Step 1:** Write failing tests: `A_glb_nfty_wrote_reads_back_the_same_mesh_and_texture`, `A_gltf_with_a_bin_file_reads_back_the_same_mesh`, `A_data_uri_gltf_reads_back_the_same_mesh` (all three built through `ModelWriter` from a 4x4 fixture), `A_model_with_two_meshes_is_refused_by_name`, `A_model_with_a_sparse_accessor_is_refused_by_name`, `A_set_asset_with_a_recorded_model_opens_with_that_model`, `A_set_asset_without_models_rises_from_the_book_found_by_hash`, `A_set_asset_with_no_book_and_no_model_is_2d_only`, `Turntable_at_medium_turns_45_degrees_in_one_second`, `Turntable_off_does_not_turn`, `The_front_preset_looks_down_negative_z`, `Exporting_a_recorded_model_copies_its_bytes_exactly`, `A_sealed_subject_cannot_save_a_still_or_export_a_model`.
- [ ] **Step 2:** Implement `GltfReader` against the `Gltf/` record model `ModelWriter` already uses, reading through the same records rather than a second schema. Implement the subjects, the presets and the turntable.
- [ ] **Step 3:** Run `dotnet test tests/Nfty.Model.Tests --filter FullyQualifiedName~GltfReaderTests`, the App targeted tests, and `AssemblyRulesTests`.
- [ ] **Step 4:** Commit: `feat(model): read back nfty's own glTF` and `feat(app): viewer 3D pane`.

## Task 4: The archive gallery, metadata JSON and sealed files

**Files:**
- Create: `src/Nfty.App/ViewModels/ArchiveGalleryViewModel.cs`, `src/Nfty.App/Viewer/GalleryItem.cs`, `src/Nfty.App/Viewer/ViewerOpener.cs` (path → subject or gallery, by extension: `Archives.TryKindOf`, then `.png/.jpg/.jpeg/.glb/.gltf/.json`, else error)
- Modify: `ViewerView` (when the subject is a gallery, the body is the tile grid; the rail shows the archive's facts)
- Test: `tests/Nfty.App.Tests/ArchiveGalleryTests.cs`, `ViewerOpenerTests.cs`

**Interfaces:**
```csharp
public sealed class GallerySubject : ViewerSubject { public ArchiveGalleryViewModel Gallery { get; } }   // Art/Model null
public sealed class GalleryItem : ObservableObject {     // a tile
    public string Caption { get; } public Bitmap? Thumbnail { get; } public bool IsLoading { get; } public bool ImageMissing { get; }
    public Func<ViewerSubject> Open { get; }               // lazy: nothing is decoded until a viewer is opened from the tile
}
public static class ViewerOpener { public static Task<ViewerSubject?> OpenAsync(string path, IDialogService dialogs, CancellationToken ct = default); }
```
- `.cbk/.rcp/.igt` are listed from `ArchivePeek` manifests. A tile decodes its own variant PNG only when realized, through `ThumbnailGate`, with `Border.tload`/`Border.tmissing`. Opening a tile reads the containing `.igt` and keeps only that variant's image. A Dynamic/Static variant shows its stored gray value-map, as the Rise page shows it.
- `.set`/a Set folder: `SetReader.ReadAsync` under `BusyViewModel`, tiles in Set numbering. `.tin`: `PassphraseDialogViewModel` loop in the window's own dialog service (re-asks on `SealedSetException`), then every tile's subject has `AllowExport = false`.
- `.mnu`: `MenuArchive.Open`, one tile per art cell, cropped by the cell rect in App. `.ktn`: the existing Kitchen discovery, one tile per archive, and a tile opens that archive's gallery.
- `.json`: if it parses as `nfty/NNNN.json` or `metadata/NNNN.json`, the image is resolved through `SetLayout` next to it. The traits go to `Facts`, and a missing image sets the missing mark. Any other JSON is an error card: `"Not an nfty asset metadata file."`

- [ ] **Step 1:** Write failing tests: `A_cookbook_lists_its_recipes_and_variants_without_decoding_any_image` (count decodes through a test hook on `ThumbnailGate`), `A_tile_decodes_its_image_only_when_realized`, `A_set_lists_its_assets_in_set_numbering_past_9999` (build the names `9999.json` and `10000.json`), `A_sealed_set_asks_for_the_passphrase_and_re_asks_when_wrong`, `Every_tile_of_a_sealed_set_is_view_only`, `A_menu_tile_shows_its_cropped_cell`, `A_kitchen_tile_opens_that_archives_gallery`, `A_metadata_json_opens_its_image_and_traits`, `A_metadata_json_whose_image_is_gone_shows_the_missing_mark`, `An_unknown_extension_opens_the_error_card_and_not_a_window`, `A_missing_tile_image_does_not_fail_the_gallery`.
- [ ] **Step 2:** Implement.
- [ ] **Step 3:** Run the targeted tests plus `DarkModeContrastTests` (add a gallery state with one missing tile). Commit.

## Task 5: Opening points, Ctrl/middle-click and live follow

**Files:**
- Create: `src/Nfty.App/Viewer/LiveSubject.cs`
- Modify: `SetBrowserView(.axaml.cs)` + VM (Ctrl/middle-click on a tile), `SetInspectView` + VM (**Pop out**), `IngredientEditorView` + VM (**Pop out** on the corner tile, Ctrl/middle-click on the tile), `RiseView` + VM (**Pop out** on the 3D tile, Ctrl/middle-click on canvas or tile), `ExplorerView` tree row + `RecipeDetailView` layer row (Ctrl/middle-click), `LandingView` + VM (**Open in viewer…**, picker filter = every supported extension)
- Modify: `RiseSubject` and the editor: raise `Detached` from `Dispose`
- Test: `tests/Nfty.App.Tests/ViewerOpeningTests.cs`, `ViewerLiveFollowTests.cs`

**Interfaces:**
```csharp
public sealed class LiveSubject : ViewerSubject {
    public LiveSubject(ILiveViewerSource source);   // clones on construction and on every source change
    public override bool IsLive { get; }            // false after the source detaches; art kept
}
public interface ILiveViewerSource {               // implemented by RiseViewModel and IngredientEditorViewModel
    string ViewerTitle { get; } Image<Rgba32> ViewerArt { get; } ViewerModel? ViewerModel { get; } bool AllowExport { get; }
    event Action? ViewerChanged; event Action? Detached;
}
```
- `ViewerChanged` is raised from the existing single points: after `RebuildSurfaces` in the editor, and after `UpdateCombined`/`Reseed`/`RebuildMesh` on the Rise page. `LiveSubject` coalesces to one `Changed` per dispatcher frame through `Dispatcher.UIThread.Post` at `Background` priority with a pending flag. The editor's art is **the layer as generation would render it** (`VariantPreview`), not the raw paint surface, so the viewer agrees with the corner tile.
- Gestures: middle button = `PointerUpdateKind.MiddleButtonPressed`; Ctrl+left = `KeyModifiers.Control` + left press. Both are **tunnel** handlers where the host control marks the press handled (`TreeView`, the canvas). Neither changes selection or arms a tool. A plain left click is unchanged everywhere, so assert it.
- Every opening point passes its seal flag: the Set browser's `IsSealed` becomes `AllowExport = false`.
- Every **Pop out** button has a tooltip from `KeyHints` naming the Ctrl+click alternative. `WiringCoverageTests` covers the commands, and `DeadCodeAuditTests` covers the glyph.

- [ ] **Step 1:** Write failing tests: `Ctrl_click_on_a_set_tile_requests_a_viewer_for_that_asset`, `Middle_click_on_a_set_tile_requests_a_viewer_for_that_asset`, `A_plain_click_on_a_set_tile_still_opens_the_inspector_and_no_viewer`, `Popping_out_of_a_sealed_set_requests_a_view_only_subject`, `Pop_out_from_the_inspector_opens_its_current_asset`, `Ctrl_click_on_the_rise_canvas_paints_nothing_and_opens_a_viewer`, `Ctrl_click_on_a_tree_row_does_not_change_the_selection`, `A_rise_stroke_updates_a_live_viewer_byte_for_byte` (pump until `Changed`), `An_editor_stroke_updates_a_live_viewer_with_the_rendered_layer`, `Ten_strokes_in_one_frame_raise_one_change`, `Closing_the_rise_page_turns_its_viewer_into_a_snapshot_and_keeps_the_art`, `Disposing_the_source_image_after_a_change_leaves_the_viewer_drawable` (render the view after the source disposes), `Open_in_viewer_from_landing_routes_by_extension`.
- [ ] **Step 2:** Implement.
- [ ] **Step 3:** Run the targeted tests plus `WiringCoverageTests`, `DeadCodeAuditTests`, `EditorLivePreviewTests` and `RisePagePerfTests` (a stroke must not get slower: the clone is posted, never inline). Look at the rendered frames of every page that gained a **Pop out** button at 1280x720 (`MinimumWindowFitTests` green). Commit.

## Task 6: Docs, CLAUDE.md, manual, BACKLOG and capture frames

**Files:**
- Modify: `CLAUDE.md` (a **Viewer** paragraph under *GUI: state and house rules*: subjects clone and never borrow; the seal travels on the subject; `IViewerHost` is the seam because headless has no second OS window; `ShutdownMode` is stated; per-window `DialogService`; `GltfReader` reads nfty's subset only)
- Create: `docs/manual/how-to/view-an-asset-in-its-own-window.md` (How-to, titled by goal: open, the controls, live follow, sealed, what is not there yet). Modify `mkdocs.yml` nav and `docs/manual/how-to/index.md`.
- Modify: `BACKLOG.md`, one entry per deferred item, each with its reason and context so it can be picked up cold. The items are: OS file association (opt-in, per-user, no admin rights, removable); single-instance hand-off; drag-and-drop onto any window; `nfty view <file>`; custom backdrop color; wireframe and voxel edges; lighting and background toggles; a generic glTF reader.
- Modify: `tests/Nfty.App.Tests/VisualCaptureStates.cs`, adding these frames in both themes: `viewer-2d`, `viewer-both`, `viewer-3d`, `viewer-gallery-cbk`, `viewer-gallery-sealed`, `viewer-metadata`, `viewer-snapshot` (live badge after detach), each at 1180x720, plus `viewer-both-min` at 1067x526. The capture helper verifies the arranged state before saving (layout value, `IsLive`, `AllowExport`).
- Modify: `src/Nfty.*` version → `2.1.0` (`Directory.Build.props`).

- [ ] **Step 1:** Write the docs and the BACKLOG entries, then run `mkdocs build --strict` if it is available, or check the nav paths by hand.
- [ ] **Step 2:** `NFTY_CAPTURE=1 NFTY_CAPTURE_DIR=<dir> dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~VisualCapture`, then **look at** every viewer frame in both themes. Replace the frames gallery with the latest only.
- [ ] **Step 3:** `dotnet build nfty.sln` (0 warnings) and `dotnet test nfty.sln`. Everything must be green.
- [ ] **Step 4:** Commit: `docs: pop-out viewer manual, CLAUDE.md, backlog` and `chore: version 2.1.0`. Do not push or merge. Stop for the author's conflict check.
