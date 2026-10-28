# Pop-out asset viewer: design (v2.1.0)

Date: 2026-10-27. Status: approved scope, ready to plan.
Source of truth for the author's wishes: `.superpowers/viewer/decisions.md`, including the
**scope ruling of 2026-10-27** at its foot. This spec builds that ruling and nothing past it.

## 1. What it is

A viewer is a separate window that shows ONE thing: an asset, a variant, a recipe roll, the Rise
page's subject, a loose picture or model, or the contents of an archive. It shows the thing in 2D,
in 3D, or both side by side. Nothing in a viewer edits anything. Several viewers can be open at
once, each in its own OS window, so you can move one to a second screen and maximize it.

**In v2.1.0:** one OS window per subject, 2D and 3D side by side, browsable archives, metadata
JSON, sealed files view-only, live follow from the Ingredient editor and the Rise page,
remembered size, opened from in-app buttons and from Ctrl+click or middle-click.

**Deferred to `BACKLOG.md`:** OS file association, single-instance hand-off, drag-and-drop onto
a window, and `nfty view <file>`. Section 9 lists the full set.

## 2. Architecture

```
caller (Set browser, inspector, editor, Rise page, Explorer row, gallery tile)
   │  ViewerRequest  (a ViewerSubject, plus the seal flag)
   ▼
IViewerHost ── DesktopViewerHost   → new ViewerWindow per request           (Nfty.Desktop)
           ├── OverlayViewerHost   → one full-screen overlay at a time      (Nfty.App, single-view)
           └── RecordingViewerHost → keeps the requests (tests)
   ▼
ViewerViewModel  (one per window)
   ├── TwoDPane   : IZoomPan  → ViewerView hosts PixelCanvasHost (the T14 canvas, unchanged)
   ├── ThreeDPane            → the existing ModelPreviewView + ModelPreviewViewModel
   ├── InfoPanel             → name, number, size, DNA, traits/rarity
   └── Gallery (archives only) → ArchiveGalleryViewModel; a tile opens a NEW viewer
```

### 2.1 `ViewerSubject`, the one abstraction

`ViewerSubject` (abstract, `IDisposable`, in `Nfty.App/Viewer/`) answers what the window shows.
It never answers how the window shows it.

- `Title` and `Facts`: the info panel's lines, already worded.
- `Image<Rgba32>? Art`: the 2D picture, or null when the subject has no picture (a loose `.glb`).
- `ViewerModel? Model`: a `Mesh` plus its texture, or null when the subject cannot rise.
- `bool IsLive`: true while the subject is linked to an open editing source.
- `event Action? Changed`: raised when the art or the model changed (live subjects only).
- `bool AllowExport`: false inside a sealed export, from the existing seal flag
  (`SetBrowserViewModel.IsSealed` → `SetInspectViewModel.AllowExport`).

There are two kinds of subject.

- **`SnapshotSubject`** owns its own copy of the art and the mesh. It never changes after it is
  built. Set assets, gallery items, loose files and metadata JSON are all snapshots.
- **`LiveSubject`** wraps a `RiseSubject` (the Rise page) or the editor's rendered variant. It
  listens to the source's existing change points: `RiseSubject.UpdateCombined`/`Reseed` and the
  editor's `RebuildSurfaces`. It re-raises them as `Changed`, coalesced to one per dispatcher
  frame. **It never takes the source's images.** It clones them on each `Changed`, so a stroke
  the source later disposes cannot leave a viewer drawing a disposed bitmap.
  `SetInspectViewModel.Image` is the precedent for that rule (the getter must be idempotent).
- **A live subject becomes a snapshot when its source closes.** The source page raises
  `Detached` from its `Dispose`. The subject keeps its last clone, drops the link, and sets
  `IsLive` to false. The window's live badge then reads `snapshot`. The window never closes
  because its source went away.

### 2.2 The 2D pane

`ViewerView` hosts `PixelCanvasHost` exactly as the editor and the Rise page do. Its
`TwoDPaneViewModel` implements `IZoomPan`, so zoom anchored on the pointer, pan, the clamp, and
the lattice all come from the one function that already owns them (`TryGeometry`). There is no
third copy of the zoom arithmetic. Nearest-neighbor scaling comes from
`RenderOptions.BitmapInterpolationMode="None"` on the view root, and `PixelPerfectRenderingTests`
covers the new view for free, because it derives its view list from the markup.

### 2.3 The 3D pane

The 3D pane is the existing `ModelPreviewView` (software raster into a `WriteableBitmap`) bound
to a `ModelPreviewViewModel`. Nothing new renders. Mesh sources:

- **Rise page or editor (live):** the source's own `Riser.TryBuild` result.
- **Set asset:** if the Set has a `models/` block, the asset's model file is read back with
  **`Nfty.Model.Gltf.GltfReader`**. That is a new reader, and it reads ONLY the subset
  `ModelWriter` writes: one mesh, one primitive, POSITION/NORMAL/TEXCOORD_0, `UNSIGNED_INT`
  indices, and one embedded PNG. Otherwise the asset rises through the existing
  `SetRiseSource` when its source book is found by hash. Failing both, the asset is 2D only.
- **Variant or recipe roll from a book:** `VariantRiseSource` / `RecipeRiseSource`, read-only form.
- **Loose `.glb`/`.gltf`:** `GltfReader`. A file nfty did not write is refused, and the message
  names the first unsupported feature. A generic glTF loader is out of scope: it is bulky, and
  the zero-packages rule forbids a library.

### 2.4 The archive gallery

An archive opens as a read-only grid of tiles. Each tile opens a new viewer.

| Opened | Gallery lists | Read with |
|---|---|---|
| `.cbk` | recipes, then every ingredient's variants | `ArchivePeek.CookBookTree` for the list; a variant's PNG decoded on tile realization |
| `.rcp` / `.igt` | layers / variants | `ArchivePeek.Recipe` / `Ingredient` + lazy decode |
| `.set` / Set folder | assets in Set numbering | `SetReader.ReadAsync` (a `BusyViewModel` covers the unpack) |
| `.tin` | assets, view-only | passphrase prompt, then `SealedSetReader.Open` |
| `.mnu` | recipes and art cells | `MenuArchive.Open` (no imaging library; crop in App) |
| `.ktn` | the Kitchen's discovered archives | the existing Kitchen scan; a tile opens that archive's gallery |

Thumbnails decode off the UI thread through `ThumbnailGate`, the way the Set browser does it,
with the breathing-diamond placeholder (`Border.tload`) and the missing-image mark
(`Border.tmissing`). A metadata JSON (`metadata/NNNN.json` or `nfty/NNNN.json`) opens as its
asset image plus its traits. The image is found next to the JSON through `SetLayout`. If it is
not there, the info panel still shows the traits and the 2D pane carries the missing mark.

## 3. What opens what

| From | Gesture | Subject |
|---|---|---|
| Set browser tile | Ctrl+click or middle-click | snapshot asset (+ model) |
| Set inspector | **Pop out** button | the same, on the inspector's current asset |
| Ingredient editor | **Pop out** button on the corner tile; Ctrl/middle-click on the tile | live variant (as generation would render it) |
| Rise page | **Pop out** on the 3D tile; Ctrl/middle-click on the canvas or the tile | live Rise subject |
| Explorer tree row / Recipe pane layer row | context-free: Ctrl/middle-click | snapshot variant / recipe roll |
| Landing | **Open in viewer…** (file picker: every supported extension) | by extension |
| Gallery tile | click | the item, in a new viewer |

A plain left click keeps doing what it does today everywhere. Ctrl+click and middle-click are
additions. A button named **Pop out** carries the `popout` glyph, which is new in
`assets/icons/` and generated through `tools/icons/build.py`.

## 4. Controls (trimmed)

The author asked to keep only what makes sense. Each cut has a reason.

**2D:** zoom anchored on the pointer (wheel), drag to pan, **Fit**, **1:1**, a backdrop of
**checker / light / dark**, a pixel-grid toggle, the info panel, **Save image…** at 1x / 2x / 4x /
8x, and **Copy**. Save and Copy are disabled and say why when `AllowExport` is false.
- *Cut: custom backdrop color.* It needs a color picker in the viewer, which is bulky for a
  backdrop. Light and dark cover the cases checker cannot.
- *Cut: arbitrary upscale.* Integer multiples are what nearest-neighbor can render honestly, and
  four of them cover sharing. The upscale is done in Core by `Imaging/Upscale.cs`
  (nearest-neighbor, whole pixels), never by Avalonia.

**3D:** orbit, pan, zoom, **Reset**, four preset angles (**Front, Back, Side, Top**), a
**turntable** toggle with three speeds (slow, medium, fast) in place of a slider, **Save still**
(PNG of the current raster target), and **Export model…**. Export is a byte copy of the model
file when one exists (Set `models/`, loose `.glb`). Otherwise it goes through `ModelWriter` in
the format the user picks. It is disabled under a seal.
- *Cut: lighting toggle and background toggle.* The raster has one shading path, and a second
  one is a renderer feature, not a viewer one. The background follows the theme.
- *Cut: wireframe and voxel edges.* Each is a new rendering path in `Raster`. That is too bulky
  for a viewer, and it is noted in the BACKLOG.
- *Cut: a speed slider.* Three speeds say the same thing in one control's width.

**Layout:** a segmented `2D | Both | 3D` control. **Both** is the default when the subject has a
model. A subject with no model shows 2D only, and the control is dimmed with its reason.

## 5. Window lifetime

- Avalonia's `IClassicDesktopStyleApplicationLifetime.ShutdownMode` defaults to
  `OnLastWindowClose`. `App` now states it **explicitly**, so a later edit cannot silently
  change it. Viewers therefore outlive the main window, and the app exits when the last window
  closes.
- Closing the main window still runs the existing close guard (`ShellViewModel.BeginClose`).
  Pages are disposed as they are today. Each disposed source raises `Detached`, which turns its
  live viewers into snapshots. Viewers hold nothing unsaved, so they never ask before closing.
- `services.Dispose()` stays on `desktop.Exit`. It runs after the last window, so a viewer
  outliving the shell keeps its services until then.
- Each `ViewerWindow` has **its own `DialogService`**. The app-wide one draws into the main
  window, which may already be closed. The passphrase prompt for a `.tin` opened from inside a
  gallery uses the viewer's own dialog service.

## 6. Remembered size

`IViewerWindowState` (`viewer-window.json`, same shape as `IEditorViewState`) holds **two**
entries, one for **2D-only** and one for **3D/Both**: width, height, position, and maximized.
A new window opens with the entry for its starting layout, and a window that is closing writes
the entry for its current layout. Position is clamped onto a screen that still exists, so an
unplugged monitor cannot strand a window. Zoom, pan and camera are NOT remembered, for the same
reason as the editor: they belong to the image. Corrupt state reads as defaults, and a failed
save is swallowed.

## 7. Sealed, mobile, errors

- **Sealed (`.tin`):** passphrase prompt (`PassphraseDialogViewModel`, re-asks on a wrong
  passphrase), then a gallery in which every subject has `AllowExport = false`. Save, Copy,
  Save still and Export model are disabled, with the existing tooltip wording. Popping out from
  a sealed Set browser carries the flag along. **The flag travels on the subject, never on the
  window**, so a gallery tile cannot launder it.
- **Single-view platforms** (`ISingleViewApplicationLifetime`): `OverlayViewerHost` shows the same
  `ViewerView` full-screen above the shell with a close button, one at a time. A second request
  replaces the first. Remembered size does not apply there.
- **Errors:** an unreadable file, an unknown extension (`Archives.TryKindOf`, plus `.png/.jpg/.glb/
  .gltf/.json`), or an unsupported glTF lands on the viewer's own error card with the engine's
  message (`ErrorReport` wording), and the window stays open. A failure while opening from a
  page reports on that page and opens no window. A gallery tile whose decode fails shows the
  missing mark and does not fail the gallery.

## 8. Testing

`Avalonia.Headless` does create `Window`s (existing tests already do), but it does not run a
real desktop lifetime or a second OS window manager. The seam is therefore **`IViewerHost`**:

- The VM and page tests inject `RecordingViewerHost` and assert *which subject was requested*,
  and with what seal flag (for example
  `Ctrl_click_on_a_set_tile_requests_a_viewer_for_that_asset`).
- `ViewerView` is rendered headless inside a plain `Window` at **1180x720** (page frame) and
  **1067x526** (smallest real area): `MinimumWindowFitTests`-style fit, contrast in both themes.
- `DesktopViewerHost` gets one headless test that opens a `ViewerWindow`, closes it, and checks
  the remembered state was written. Lifetime/shutdown is asserted on the **stated
  `ShutdownMode`** (`App_states_shutdown_on_last_window_close`), not on a real exit.
- Live follow: drive a real `RiseViewModel` stroke and a real editor stroke, then pump until
  `Changed`, then assert the viewer's art equals the source's art byte for byte.
  Then dispose the source and assert `IsLive == false` and that the art is unchanged.
- `GltfReader`: round-trip every fixture model through `ModelWriter` (GLB, `.gltf+.bin`, data URI)
  and assert vertices, indices and texture are identical. A foreign-feature file is refused by
  name.

## 9. Deferred (BACKLOG)

These are deferred: OS file association (opt-in, per-user, removable), single-instance
hand-off, drag-and-drop onto any window, `nfty view <file>`, custom backdrop color, wireframe
and voxel-edge display, lighting and background toggles, and a generic (non-nfty) glTF reader.
