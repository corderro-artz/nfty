# nfty GUI — Ingredient Editor painting + undo/redo (design spec)

**Date:** 2026-07-26
**Status:** Approved (design), pending implementation planning
**Scope:** First of two editing slices. Wire the Ingredient Editor's tools to the existing
`Nfty.Core.Editing` engine so the user can paint a variant's value-map (brush / eraser / shapes / fill)
with undo/redo, editing an in-memory `IngredientDraft` with a live canvas. **Persistence (Save to the
`.cbk`) is Slice 2** — this slice does not write to disk.
**Builds on:** the imaging slice (editor wired to a real ingredient with filmstrip/canvas/live colorize
preview; painting/undo/save are stubs). The whole edit MODEL already exists in `Nfty.Core.Editing`:
`ValueMap` (grayscale value+alpha, `FromImage`/`ToImage`/`Set`), `VariantDraft`/`IngredientDraft`,
`EditHistory` (`Do(cmd,map)`/`Undo(map)`/`Redo(map)`, `CanUndo`/`CanRedo`), commands
`BrushStroke(Brush,path)` / `EraseStroke(size,path)` / `DrawShape(ShapeKind,PixelRect,byte)` /
`FloodFill(x,y,byte)`, `Brush(int Size, byte Value)`, `ShapeKind{Rectangle,Ellipse,Triangle}`,
`PixelRect(X,Y,Width,Height)`.

## 0. Program bar
Rock-solid, efficient; best practices; pull docs rather than assume; escalate anything off. No `Nfty.Core`
change (the engine exists) — GUI wiring only. Visual polish of the editor is a later pass; make it clean.

## 1. Goals & non-goals
**Goals**
- The editor edits an in-memory `IngredientDraft` (variants as `ValueMap`s built from the ingredient's
  images). Painting with the active tool applies the matching `Nfty.Core.Editing` command to the selected
  variant's `ValueMap` through a per-variant `EditHistory`; the canvas re-renders after each edit.
- Tools: **Brush**, **Eraser**, **Rectangle**, **Circle** (`Ellipse`), **Triangle**, **Fill**; a **value**
  (0–255, existing `BrushValue`) and a **size** control. **Undo/Redo** (per selected variant).
- The **canvas** shows the grayscale value-map being painted; the **Colorize-rail Preview** shows the
  colorized result (`VariantImagery.RenderWith`), matching the mockup.
- Pointer drag on the canvas paints (coordinates mapped to value-map pixels).

**Non-goals (this slice)**
- **Save / persist** to the `.cbk` (Slice 2). Live filmstrip-thumbnail updates. The **Select** tool
  (ui-state only). Enlarge / fill-pane as real windows. Full-colour **custom**-layer painting (`ValueMap`
  is grayscale — see §6). Any `Nfty.Core` change.

## 2. Components

### 2.1 Edit model (`IngredientEditorViewModel`)
- On construction, build `_draft = new IngredientDraft(m.Id, m.Name, m.Kind, m.Colorization,
  book.Manifest.Canvas, m.Variants.Select(v => new VariantDraft(v.Id, v.Name, v.Weight,
  ValueMap.FromImage(_ing.VariantImages[v.Id]))))`. The draft is the edit target; the read-only
  `_ing.VariantImages` are no longer the paint source.
- Per-variant undo history: `Dictionary<string, EditHistory>` keyed by variant id (one stack per variant
  so undo is scoped to the variant you're editing). `ActiveMap` = the selected `VariantDraft.Map`;
  `ActiveHistory` = its `EditHistory`.
- **Canvas render** changes: the canvas shows the **grayscale value-map** — `_bridge.ToBitmap(ActiveMap.ToImage())`
  (no colorize). The **Preview** keeps colorizing via `VariantImagery.RenderWith` over the active map's
  image (so dynamic/static show the colorized companion; the colorize-rail sliders still drive it). Custom
  (`Colorization is null`) canvas + preview both show the raw map image. `RebuildSurfaces()` re-renders
  both after any edit/selection/colour change (disposing the old bitmaps, as today).
- `CanUndo`/`CanRedo` reflect `ActiveHistory`; `IsDirty` = any history has undone/redoable edits (used by
  Slice 2's Save; exposed now, harmless).

### 2.2 Tools → commands (`IngredientEditorViewModel`)
Public methods the pointer handler (2.3) calls, each building the Core command from `ActiveTool` +
`BrushValue` + `BrushSize`, running it through `ActiveHistory.Do(cmd, ActiveMap)`, then
`RebuildSurfaces()` + `CanUndo`/`CanRedo` notify:
- `StrokeBrush(IReadOnlyList<(int x,int y)> path)` → `new BrushStroke(new Brush(BrushSize, (byte)BrushValue), path)`.
- `StrokeErase(IReadOnlyList<(int x,int y)> path)` → `new EraseStroke(BrushSize, path)`.
- `DrawShapeIn(ShapeKind kind, PixelRect bounds)` → `new DrawShape(kind, bounds, (byte)BrushValue)`.
- `FillAt(int x, int y)` → `new FloodFill(x, y, (byte)BrushValue)`.
- A single entry `ApplyToolStroke(IReadOnlyList<(int,int)> points)` that dispatches on `ActiveTool`
  (Brush/Eraser → stroke over the whole `points` path; Rectangle/Circle/Triangle → a `DrawShape` whose
  `PixelRect` is the bounding box of `points[0]`→`points[^1]`; Fill → `FloodFill` at `points[0]`;
  Select → no-op) keeps the view code-behind simple (one call per completed gesture).
- `[RelayCommand] Undo` → `ActiveHistory.Undo(ActiveMap)` + rebuild + notify; `Redo` likewise. Replace
  the current `_notify.Report("Undo"/"Redo"/"Paint")` stubs. `[ObservableProperty] BrushSize` (default 8,
  min 1). `ApplyStroke` stub removed (superseded).

### 2.3 Canvas pointer interaction (`IngredientEditorView.axaml.cs`)
The canvas `Image` (source = `Canvas` bitmap, `Stretch=Uniform`) handles `PointerPressed`/`PointerMoved`/
`PointerReleased`:
- Map a pointer position in the `Image`'s control space to value-map pixel coords: compute the rendered
  image rect inside the control (Uniform letterbox: `scale = min(ctrlW/imgW, ctrlH/imgH)`, centered), then
  `px = (pointerX - offsetX)/scale`, clamped to `[0, Width)`. (The image pixel size = the draft canvas
  `Width`/`Height`.)
- Accumulate points while the button is down; on release, call `vm.ApplyToolStroke(points)`. For Fill,
  a single click's point suffices; for shapes, first+last point define the bounds; for brush/eraser, the
  full path. (Live drag preview is optional/deferred — commit on release is acceptable for this slice.)
The `IngredientEditorViewModel` exposes `ApplyToolStroke` + the tool/value/size state; the view owns only
coordinate mapping + point collection. No business logic in code-behind.

### 2.4 View additions
A `BrushSize` control (a `Slider`/`NumericUpDown`, min 1) beside the existing value slider; Undo/Redo
buttons wired to `UndoCommand`/`RedoCommand` (disabled via `CanUndo`/`CanRedo`). The canvas `Image` gets
the pointer handlers (named element + code-behind wiring). Token styles; no raw hex.

## 3. Data flow
```
pointer drag on canvas Image → view maps to value-map pixels, collects points
  → on release: vm.ApplyToolStroke(points)
       → build Core command per ActiveTool (Brush/Erase/Shape/Fill) with BrushValue/BrushSize
       → ActiveHistory.Do(cmd, ActiveMap)      [Nfty.Core.Editing]
       → RebuildSurfaces()  (canvas = ActiveMap.ToImage grayscale; preview = colorized RenderWith)
Undo/Redo → ActiveHistory.Undo/Redo(ActiveMap) → RebuildSurfaces()
```

## 4. Testing
- **VM tests** (`[AvaloniaFact]` where bitmaps are built; the edit logic itself is engine-backed): build
  the editor over a fixture ingredient (dynamic, small canvas); `ApplyToolStroke` a brush path → assert
  the `ActiveMap`'s value changed at a painted pixel (read via a VM test hook or by re-decoding the canvas
  is fragile — prefer exposing `ActiveMap` internally-visible or a `ValueAt(x,y)` test helper);
  `UndoCommand` reverts the pixel; `RedoCommand` re-applies; `CanUndo`/`CanRedo` toggle. Fill at a point
  changes the region; a shape draws its bounds. Per-variant history isolation: paint variant A, switch to
  B, `CanUndo` is false for B.
- **Pointer→pixel mapping** is view code — **manually smoke-tested** (paint in the running app), noted in
  the plan; optionally a small headless test of the mapping math if extracted to a pure function.
- **Visual:** render the editor, apply a stroke via the VM (`ApplyToolStroke`), capture the canvas → the
  painted mark shows (both themes). Iterate for clean layout.
- Full suite green; build 0 warnings; no raw hex outside `Tokens.axaml`.

## 5. Out of scope
Save/persist (Slice 2); live filmstrip thumbnails; Select tool; enlarge/fill-pane windows; full-colour
custom painting; `Nfty.Core` changes; add/duplicate/delete variant (still `Report` stubs).

## 6. Risks & escalation
- **Custom (full-colour) layers** — `ValueMap` is grayscale (value + alpha). `ValueMap.FromImage` of a
  full-colour custom image reduces it to value/alpha; painting then edits grayscale, and the canvas shows
  grayscale. For a custom ingredient the editor still opens and the colorize preview shows the raw image,
  but painting is value/alpha only — a known limitation; full-colour custom editing is out of scope and
  its Save/export implications are handled in Slice 2. If this reads badly in the manual smoke, escalate.
- **Coordinate mapping** — the Uniform-stretch letterbox math must map pointer→pixel correctly at any
  control size; verify in the manual smoke. Consider extracting the map function so it's unit-testable.
- **Per-variant history** — undo must apply to the variant it was recorded on; keying history by variant
  id (and applying to that variant's map) avoids cross-variant corruption. Switching variants must not
  clear history (drafts + histories persist for the editor's lifetime).
- **Command path types** — `BrushStroke`/`EraseStroke` take `IReadOnlyList<(int x,int y)>`; pass the
  collected integer pixel path directly. `DrawShape` takes a `PixelRect`; build it from the drag bounds
  (normalise so Width/Height are non-negative).
