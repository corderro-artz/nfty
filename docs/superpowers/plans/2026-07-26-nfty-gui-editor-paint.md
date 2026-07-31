# nfty GUI — Ingredient Editor painting + undo/redo Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Wire the Ingredient Editor's tools to `Nfty.Core.Editing` so a user can paint a variant's value-map (brush/eraser/shapes/fill) with undo/redo, editing an in-memory `IngredientDraft` with a live canvas. No persistence (Slice 2). No `Nfty.Core` change.

**Architecture:** The editor builds an `IngredientDraft` from the ingredient (variants as `ValueMap`s); each tool maps to a Core `IEditCommand` run through a per-variant `EditHistory`; the canvas renders the grayscale value-map and the Colorize preview renders the colorized result. The view code-behind maps pointer drags to value-map pixels and calls one VM entry point.

**Tech Stack:** .NET 10, Avalonia 11.2.3 (pointer events, `Image` Uniform-stretch coord math), CommunityToolkit.Mvvm, `Nfty.Core.Editing` (`ValueMap`/`IngredientDraft`/`VariantDraft`/`EditHistory`/`BrushStroke`/`EraseStroke`/`DrawShape`/`FloodFill`/`Brush`/`ShapeKind`/`PixelRect`), xUnit + Avalonia.Headless.XUnit.

## Global Constraints
- No `Nfty.Core` change (the editing engine exists). Canvas = grayscale value-map (`ActiveMap.ToImage()`); Preview = colorized (`VariantImagery.RenderWith`). The `LoadedIngredient.VariantImages` are read once to seed the draft, then the DRAFT is the paint target.
- The `IngredientDraft`/`VariantDraft`/`EditHistory` live for the editor's lifetime; per-variant history keyed by variant id (undo scoped to the edited variant). `ActiveMap.ToImage()` allocates a new `Image<Rgba32>` — wrap in `using` and dispose after `ToBitmap`/`RenderWith`.
- No business logic in view code-behind — only pointer→pixel mapping + point collection + one `vm.ApplyToolStroke(points)` call. Colours via `{DynamicResource}` tokens only; no raw hex. `[AvaloniaFact]` for Avalonia-constructing tests. Build 0 warnings. Conventional commits. Agents: caveman-ultra terse chat; code/commits/reports normal prose.
- Custom (full-colour) layers: `ValueMap` is grayscale — painting a custom layer edits value/alpha only (known limitation, spec §6); the editor still opens.

## File Structure
- `src/Nfty.App/ViewModels/IngredientEditorViewModel.cs` — draft + history + canvas/preview split (T1); tool dispatch + undo/redo + BrushSize (T2).
- `src/Nfty.App/Nfty.App.csproj` — `InternalsVisibleTo("Nfty.App.Tests")` if absent (T2, for the `ValueAt` test hook).
- `src/Nfty.App/Views/IngredientEditorView.axaml`(+`.cs`) — canvas pointer handlers + BrushSize/Undo/Redo controls (T3).
- Tests: `tests/Nfty.App.Tests/IngredientEditorPaintTests.cs` (T1/T2); `VisualCapture.cs` (T4).

---

### Task 1: Editor edits an `IngredientDraft` (grayscale canvas + colorized preview)

**Files:** Modify `src/Nfty.App/ViewModels/IngredientEditorViewModel.cs`; Test `tests/Nfty.App.Tests/IngredientEditorPaintTests.cs` (create).

**Interfaces:**
- Consumes: `Nfty.Core.Editing.IngredientDraft`/`VariantDraft`/`ValueMap`; `book.Manifest.Canvas` (`Dimensions`).
- Produces: `_draft` (IngredientDraft), `ActiveDraft`/`ActiveMap`; canvas renders grayscale value-map, preview renders colorized.

- [ ] **Step 1: Failing test**
```csharp
// tests/Nfty.App.Tests/IngredientEditorPaintTests.cs
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public class IngredientEditorPaintTests
{
    // A small dynamic ingredient (value-map layer) with one variant on an 8x8 canvas.
    private static (LoadedIngredient ing, LoadedRecipe recipe, LoadedCookBook book) Fixture()
    {
        var coloriz = new Colorization(ColorModel.Hsv, 12, 4,
            new[] { new ColorEntry(1, new ColorRange(0, 360, 40, 100), null) });
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("aura", "Aura", LayerKind.Dynamic, coloriz,
                new[] { new Variant("glow", "Glow", 1), new Variant("spark", "Spark", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["glow"] = new(8, 8), ["spark"] = new(8, 8) },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "aura" }, System.Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(8, 8),
                new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 }),
            Recipes = new[] { recipe },
        };
        return (ing, recipe, book);
    }

    private static IngredientEditorViewModel Editor()
    {
        var (ing, recipe, book) = Fixture();
        return new IngredientEditorViewModel(ing, recipe, book, new ImageBridge(), new FakeNav(), new FakeNotYetWired());
    }

    [AvaloniaFact]
    public void Canvas_and_preview_build_over_a_draft()
    {
        using var vm = Editor();
        Assert.NotNull(vm.Canvas);
        Assert.NotNull(vm.Preview);
        Assert.Equal(0, vm.ValueAt(2, 2));   // seeded from a blank 8x8 image → value 0
    }
}
```
(`ValueAt` is an internal test hook added in Task 2; if this test is run before Task 2, gate it — but simplest: introduce `ValueAt` + `InternalsVisibleTo` here in Task 1 so the test compiles. See Step 3.)

- [ ] **Step 2: Run — fails** (`ValueAt` missing / draft not built).

- [ ] **Step 3: Implement the draft + render split.** In `IngredientEditorViewModel.cs`:
  - Add usings: `using Nfty.Core.Editing;`.
  - Add fields after `_ing`:
    ```csharp
    private readonly IngredientDraft _draft;
    private readonly Dictionary<string, EditHistory> _history = new(StringComparer.Ordinal);
    ```
  - In the ctor (which already has `book`), build the draft + histories BEFORE the filmstrip loop (or right after `_ing` assignment):
    ```csharp
    _draft = new IngredientDraft(ing.Manifest.Id, ing.Manifest.Name, ing.Manifest.Kind, ing.Manifest.Colorization,
        book.Manifest.Canvas,
        ing.Manifest.Variants.Select(v => new VariantDraft(v.Id, v.Name, v.Weight,
            ValueMap.FromImage(ing.VariantImages[v.Id]))));
    foreach (var v in _draft.Variants) _history[v.Id] = new EditHistory();
    ```
  - Add active-draft accessors:
    ```csharp
    private VariantDraft? ActiveDraft =>
        SelectedVariant is null ? null : _draft.Variants.FirstOrDefault(d => d.Id == SelectedVariant.Id);
    private ValueMap? ActiveMap => ActiveDraft?.Map;
    internal byte ValueAt(int x, int y) => ActiveMap!.GetValue(x, y);   // test hook
    ```
    Add `using System.Linq;` if needed.
  - Replace `SelectedMap` + `RenderCanvas`/`RenderPreview` with draft-backed renders:
    ```csharp
    // Canvas shows the grayscale VALUE-MAP being painted; Preview shows the colorized companion.
    private Bitmap RenderCanvas()
    {
        using var img = ActiveMap!.ToImage();
        return _bridge.ToBitmap(img);
    }
    private Bitmap RenderPreview()
    {
        using var img = ActiveMap!.ToImage();
        return _ing.Manifest.Colorization is null
            ? _bridge.ToBitmap(img)
            : VariantImagery.RenderWith(_bridge, img, Mode == LayerKind.Dynamic,
                HueMin, HueMax, SatMin, SatMax, FixedColor, _previewSalt);
    }
    ```
    Delete the old `SelectedMap` property. `RebuildSurfaces` stays (its `if (SelectedVariant is null) return;` guard still applies; it now uses `ActiveMap`).
  - Add `[assembly: InternalsVisibleTo("Nfty.App.Tests")]` — put it in `src/Nfty.App/Nfty.App.csproj` as `<ItemGroup><InternalsVisibleTo Include="Nfty.App.Tests" /></ItemGroup>` (preferred) OR an `AssemblyInfo`-style attribute. Confirm which the repo uses; add if absent.

- [ ] **Step 4: Run — passes;** `dotnet test tests/Nfty.App.Tests --nologo` whole App suite green; `dotnet build src/Nfty.Desktop --nologo` 0 warnings.

- [ ] **Step 5: Commit** `feat(gui): editor edits an IngredientDraft (grayscale canvas + colorized preview)`

---

### Task 2: Tool dispatch + undo/redo

**Files:** Modify `src/Nfty.App/ViewModels/IngredientEditorViewModel.cs`; Test `tests/Nfty.App.Tests/IngredientEditorPaintTests.cs`.

**Interfaces:**
- Consumes: T1's `_draft`/`_history`/`ActiveDraft`/`ActiveMap`/`ValueAt`; Core `BrushStroke`/`EraseStroke`/`DrawShape`/`FloodFill`/`Brush`/`ShapeKind`/`PixelRect`.
- Produces: `void ApplyToolStroke(IReadOnlyList<(int x,int y)> points)`; `[ObservableProperty] int BrushSize`; `UndoCommand`/`RedoCommand` with `CanUndo`/`CanRedo`.

- [ ] **Step 1: Failing tests** (append):
```csharp
    [AvaloniaFact]
    public void Brush_paints_and_undo_reverts()
    {
        using var vm = Editor();
        vm.ActiveTool = EditorTool.Brush; vm.BrushValue = 200; vm.BrushSize = 1;
        Assert.Equal(0, vm.ValueAt(4, 4));
        vm.ApplyToolStroke(new[] { (4, 4) });
        Assert.True(vm.ValueAt(4, 4) > 0);   // painted
        Assert.True(vm.UndoCommand.CanExecute(null));
        vm.UndoCommand.Execute(null);
        Assert.Equal(0, vm.ValueAt(4, 4));   // reverted
        Assert.True(vm.RedoCommand.CanExecute(null));
        vm.RedoCommand.Execute(null);
        Assert.True(vm.ValueAt(4, 4) > 0);   // re-applied
    }

    [AvaloniaFact]
    public void Fill_changes_the_region()
    {
        using var vm = Editor();
        vm.ActiveTool = EditorTool.Fill; vm.BrushValue = 150;
        vm.ApplyToolStroke(new[] { (0, 0) });
        Assert.Equal(150, vm.ValueAt(7, 7));   // flood filled the blank canvas
    }

    [AvaloniaFact]
    public void History_is_per_variant()
    {
        using var vm = Editor();
        vm.ActiveTool = EditorTool.Brush; vm.BrushValue = 200; vm.BrushSize = 1;
        vm.ApplyToolStroke(new[] { (4, 4) });               // paint variant "glow"
        Assert.True(vm.UndoCommand.CanExecute(null));
        vm.SelectVariantCommand.Execute(vm.Variants[1]);    // switch to "spark"
        Assert.False(vm.UndoCommand.CanExecute(null));      // spark has no history
    }
```

- [ ] **Step 2: Run — fails** (`ApplyToolStroke`/`BrushSize` missing; Undo/Redo are stubs).

- [ ] **Step 3: Implement.** In `IngredientEditorViewModel.cs`:
  - Add `[ObservableProperty] private int _brushSize = 8;`.
  - Add the dispatch entry + bounds helper:
    ```csharp
    /// <summary>Commit one completed gesture as a Core edit command against the active variant.</summary>
    public void ApplyToolStroke(IReadOnlyList<(int x, int y)> points)
    {
        if (ActiveDraft is null || points.Count == 0) return;
        var map = ActiveDraft.Map;
        var hist = _history[ActiveDraft.Id];
        IEditCommand? cmd = ActiveTool switch
        {
            EditorTool.Brush => new BrushStroke(new Brush(BrushSize, (byte)BrushValue), points),
            EditorTool.Eraser => new EraseStroke(BrushSize, points),
            EditorTool.Fill => new FloodFill(points[0].x, points[0].y, (byte)BrushValue),
            EditorTool.Rectangle => new DrawShape(ShapeKind.Rectangle, BoundsOf(points), (byte)BrushValue),
            EditorTool.Circle => new DrawShape(ShapeKind.Ellipse, BoundsOf(points), (byte)BrushValue),
            EditorTool.Triangle => new DrawShape(ShapeKind.Triangle, BoundsOf(points), (byte)BrushValue),
            _ => null,   // Select — no-op this slice
        };
        if (cmd is null) return;
        hist.Do(cmd, map);
        RebuildSurfaces();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private static PixelRect BoundsOf(IReadOnlyList<(int x, int y)> pts)
    {
        var (ax, ay) = pts[0]; var (bx, by) = pts[^1];
        int x = System.Math.Min(ax, bx), y = System.Math.Min(ay, by);
        int w = System.Math.Abs(bx - ax) + 1, h = System.Math.Abs(by - ay) + 1;
        return new PixelRect(x, y, w, h);
    }

    private bool CanUndo() => ActiveDraft is not null && _history[ActiveDraft.Id].CanUndo;
    private bool CanRedo() => ActiveDraft is not null && _history[ActiveDraft.Id].CanRedo;
    ```
  - Replace the Undo/Redo stubs:
    ```csharp
    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() { _history[ActiveDraft!.Id].Undo(ActiveDraft.Map); RebuildSurfaces(); UndoCommand.NotifyCanExecuteChanged(); RedoCommand.NotifyCanExecuteChanged(); }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() { _history[ActiveDraft!.Id].Redo(ActiveDraft.Map); RebuildSurfaces(); UndoCommand.NotifyCanExecuteChanged(); RedoCommand.NotifyCanExecuteChanged(); }
    ```
  - In `OnSelectedVariantChanged`, after the rebuild, notify undo/redo can-execute (the active history changed): add `UndoCommand.NotifyCanExecuteChanged(); RedoCommand.NotifyCanExecuteChanged();` (guard for null commands during ctor if the source-generated commands aren't yet initialised — CommunityToolkit initialises them lazily; if a null-ref occurs during ctor, null-check `UndoCommand?`).
  - Remove the now-superseded `ApplyStroke` stub.

- [ ] **Step 4: Run — passes;** whole App suite green; build 0 warnings.

- [ ] **Step 5: Commit** `feat(gui): editor tools paint via Nfty.Core.Editing with undo/redo`

---

### Task 3: Canvas pointer interaction + view controls

**Files:** Modify `src/Nfty.App/Views/IngredientEditorView.axaml`(+`.cs`).

**Interfaces:** Consumes `vm.ApplyToolStroke`, `vm.BrushSize`, `UndoCommand`/`RedoCommand`, `Canvas` bitmap.

- [ ] **Step 1: Wire the canvas pointer handlers** in `IngredientEditorView.axaml.cs`. Name the canvas `Image` (e.g. `x:Name="CanvasImage"`) in the axaml; in code-behind, on `PointerPressed` start collecting, `PointerMoved` (while pressed) accumulate, `PointerReleased` commit:
```csharp
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Nfty.App.ViewModels;

namespace Nfty.App.Views;

public partial class IngredientEditorView : UserControl
{
    private readonly List<(int x, int y)> _points = new();
    private bool _drawing;

    public IngredientEditorView()
    {
        InitializeComponent();
        var img = this.FindControl<Image>("CanvasImage")!;
        img.PointerPressed += (_, e) => { _drawing = true; _points.Clear(); AddPoint(img, e); };
        img.PointerMoved += (_, e) => { if (_drawing) AddPoint(img, e); };
        img.PointerReleased += (_, e) =>
        {
            if (!_drawing) return;
            _drawing = false;
            AddPoint(img, e);
            if (DataContext is IngredientEditorViewModel vm && _points.Count > 0)
                vm.ApplyToolStroke(_points.ToArray());
            _points.Clear();
        };
    }

    // Map the pointer position (control space) to value-map pixel coords, honouring Stretch=Uniform.
    private void AddPoint(Image img, PointerEventArgs e)
    {
        if (img.Source is not Bitmap bmp) return;
        var p = e.GetPosition(img);
        double imgW = bmp.PixelSize.Width, imgH = bmp.PixelSize.Height;
        double cw = img.Bounds.Width, ch = img.Bounds.Height;
        if (imgW <= 0 || imgH <= 0 || cw <= 0 || ch <= 0) return;
        double scale = System.Math.Min(cw / imgW, ch / imgH);
        double offX = (cw - imgW * scale) / 2, offY = (ch - imgH * scale) / 2;
        int px = (int)((p.X - offX) / scale);
        int py = (int)((p.Y - offY) / scale);
        px = System.Math.Clamp(px, 0, (int)imgW - 1);
        py = System.Math.Clamp(py, 0, (int)imgH - 1);
        var pt = (px, py);
        if (_points.Count == 0 || _points[^1] != pt) _points.Add(pt);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
```
(If the view already has a code-behind with `InitializeComponent`, merge — don't duplicate.)

- [ ] **Step 2: axaml** — give the canvas `Image` `x:Name="CanvasImage"` and ensure it's inside the canvas host `Border`; add a **Brush size** control (`Slider` or `NumericUpDown` bound `BrushSize`, Minimum 1) beside the existing Value slider; ensure the Undo/Redo buttons bind `UndoCommand`/`RedoCommand` (add if missing). Token styles; no raw hex.

- [ ] **Step 3:** `dotnet build src/Nfty.Desktop --nologo` 0 warnings; `dotnet test tests/Nfty.App.Tests --nologo` green (SmokeTests still resolves the view).

- [ ] **Step 4: Commit** `feat(gui): editor canvas pointer painting + brush-size/undo/redo controls`

---

### Task 4: Visual capture + full verification + manual smoke

**Files:** Modify `tests/Nfty.App.Tests/VisualCapture.cs`.

- [ ] **Step 1:** Add a `Capture_editor_paint` `[AvaloniaFact]` (guarded by `NFTY_CAPTURE`): build the editor over the Task-1 fixture, `vm.ActiveTool = EditorTool.Fill; vm.BrushValue = 200; vm.ApplyToolStroke(new[]{(0,0)})` (fill so the canvas visibly changes), render the real `IngredientEditorView` (DataContext = vm) in both themes → `editor-paint-{v}.png`; dispose vm.
- [ ] **Step 2:** Render + **view** the PNGs (Read tool) — confirm the editor chrome (filmstrip, tools, canvas showing the painted value, colorize rail + preview) reads cleanly and the canvas reflects the fill, both themes. Report what you saw; fix any obvious layout gap (tokens only, no hex).
- [ ] **Step 3:** `dotnet build nfty.sln --nologo` 0 warnings; `dotnet test nfty.sln --nologo` all pass (report total); `grep -rniE "#[0-9a-f]{6}" src/Nfty.App/Views/IngredientEditorView.axaml` → no raw hex.
- [ ] **Step 4: Manual smoke (user):** `dotnet run --project src/Nfty.Desktop`; open a cookbook; select an ingredient → ✏ Edit; pick Brush, set a value, **drag on the canvas** — the mark appears; Undo/Redo work; switch variants (independent history); pick Fill/Rectangle/Circle. (Save does nothing yet — Slice 2.) Confirm the pointer maps correctly at different window sizes.
- [ ] **Step 5:** Commit `test(gui): render the editor painting for visual verification` (+ any smoke fixups).

---

## Self-Review
- **Spec coverage:** §2.1 draft + grayscale canvas / colorized preview → T1. §2.2 tools → commands + undo/redo + BrushSize → T2. §2.3 pointer interaction + coord mapping → T3. §2.4 view controls → T3. §4 tests → T1/T2 (VM paint/undo/redo/fill/per-variant), T4 (visual), manual smoke (pointer). §6 custom limitation carried in code comments. No `Nfty.Core` change.
- **Placeholder scan:** T1/T2 carry full code; T3 gives the complete code-behind + axaml deltas; the `InternalsVisibleTo` step is concrete (check + add). No TBDs.
- **Type consistency:** `_draft`/`ActiveDraft`/`ActiveMap`/`ValueAt` (T1) used by `ApplyToolStroke`/Undo/Redo (T2) and the view (T3); `ApplyToolStroke(IReadOnlyList<(int,int)>)`, `BrushSize`, `UndoCommand`/`RedoCommand` names match across T2/T3/T4; Core `BrushStroke(Brush,path)`/`EraseStroke(int,path)`/`DrawShape(ShapeKind,PixelRect,byte)`/`FloodFill(int,int,byte)`/`Brush(int,byte)`/`EditHistory.Do/Undo/Redo(map)`/`ValueMap.FromImage/ToImage/GetValue`/`IngredientDraft(...)`/`VariantDraft(id,name,weight,map)` all match Nfty.Core.Editing.
