# Rise step 7 — the Rise page — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: use superpowers:subagent-driven-development (one fresh
> Sonnet implementer per task, a Sonnet task review after each, an **Opus 5.5** whole-step review at
> the end). Steps use checkbox (`- [ ]`) syntax for tracking. Every task leaves `dotnet build nfty.sln`
> at 0 warnings / 0 errors and the full suite green.

**Goal:** a new three-pane Avalonia page — left SOURCE rail, centre depth canvas + toolstrip + 3D
tile, right DEPTH | EXPORT rail, footer Back / Save depth — where an author paints per-pixel front /
back depth on pixel art and sees the risen model live, for three sources: an Ingredient **variant**
(editable, saved through `CookBookPersistence`), a **Recipe** roll (combined depth, read-only, reroll
die) and a cooked **Set** (read-only, batch-checkable, sealed = viewable but not exportable).

**Architecture:** the depth editing is a Core edit surface (`DepthSurface : IEditSurface<DepthPixel>`)
driven by two `RegionEditCommand<DepthPixel>` commands and the existing `EditHistory<T>` — so undo /
redo / no-op detection / snapshot are the SAME code the pixel editor runs, not a second copy. What a
gesture touches comes from a new `Footprint` class extracted out of `RegionEditCommand.StampDiscs` and
`FloodFill`. Depth for a stack comes from `StackDepth.ForSelection` (Nfty.Model) fed by the same
`(ingredientId, variantId)` list a `GeneratedAsset` carries or `SetItemSelection.Resolve` recovers from
a Set's metadata. The page is a `RiseViewModel` over an abstract `RiseSource` (three subclasses now;
step 8 adds picture/sheet as a fourth/fifth subclass, not a rewrite). The canvas mapping the editor
owns (`Geometry`, zoom/pan clamp, anchored zoom, lattice brush) is extracted into `CanvasMapping` /
`PixelLattice` so both pages run ONE mapping. The 3D tile is the step-6 `ModelPreviewView`, re-parented
(never duplicated) between the corner tile and the canvas slot.

**Tech Stack:** .NET 10, Avalonia 12.1.1, CommunityToolkit.Mvvm, ImageSharp 3.1.11 (pinned — do not
touch), xunit.v3, Avalonia.Headless. **Zero new packages** (`System.IO.Compression` is BCL).

**Spec:** `docs/superpowers/specs/2026-09-25-rise-design.md` ("The preview — software rasterizer",
"The Rise page", "Icons", "Testing"); `docs/superpowers/specs/2026-09-22-model-preview-design.md`;
approved mockup `docs/design/rise/rise-page-mockup.html` (the running app wins wherever they differ).

**Branch / start:** `worktree-nfty-ticket`, HEAD `e5fe8b6`. Paid branch: **never push, never merge,
never touch `main`.** The controller tags `rise-step7` after the Opus review passes.

---

## Global constraints (verified in code at `e5fe8b6`)

| Fact | Value | Where |
|------|-------|-------|
| Window minimum | 1280 × 720 | `ShellViewModel.MinWindowWidth/Height` |
| Shell scale | 1.2 | `ShellViewModel.BaseScale` |
| Chrome reserve | 36 + 29 + 24 = 89 | `ShellViewModel.ChromeReserve` |
| Page area at minimum (sweeps) | `(1280 − 24) / 1.2` × `(720 − 89) / 1.2` ≈ **1046.7 × 525.8** | `MinimumWindowFitTests.PageArea`, `VisualCapture.Capture_explorer_at_minimum` |
| Page capture frame (default) | 1180 × 720 | `VisualCapture.Capture` |
| Pane columns | **262, \*, 300** | `IngredientEditorView.axaml` line 32 |
| Pane header row | 41 (`Border.pane-hrow`, padding 16,0, bottom hairline) | `Styles.axaml` |
| Toolstrip | `Border.pane-hrow.toolstrip`: Height NaN, MinHeight 41, padding 12,4, `ctl:StripPanel Spacing=14 LineSpacing=6` | `Styles.axaml`, editor |
| Rail tabs | `Border.pane-hrow.tabrow` + `Button.railtab` (+ `Border.tabcount` badge) | `Styles.axaml` |
| Rail body | `StackPanel Margin="13,13,13,16" Spacing="15"` inside a `ScrollViewer` | editor |
| Form field / compact cell | `FieldHeight` 32 / `FieldHeightSm` 26 / `StepperWidth` 17 | `Tokens.axaml` |
| Footer band | `Border.modal-foot` (padding 16,8; buttons Height 30) → ~47px | `Styles.axaml`, editor footer |
| Canvas host | fixed 320 × 320, `CornerRadius 10`, centred in `Panel x:Name="CanvasBackdrop"` | editor |
| Corner tile | 120 (enlarged 208), margin `0,0,26,26`, 26px `ScrimBrush` caption bar | editor `PreviewHeight` |
| Grid chip | `Border.gridchip` bottom-left, margin `26,0,0,26` | editor |
| Canvas zoom | min 1, max 16, step 1.25, arrow pan 24 | `IngredientEditorViewModel.Zoom.cs` |
| Companion repaint budget | 33 ms (~30/s); canvas never skipped | `IngredientEditorViewModel.TileBudgetMs` |
| Depth per side | 0..255 bytes; thickness = 1 + front + back | spec |
| Solid | `Opacity.IsSolid` (A == 255); partial = `Opacity.IsPartial` | `Nfty.Core/Imaging/Opacity.cs` |
| Contrast floor / baseline tolerance | 2.0 / 1.25 px | `DarkModeContrastTests`, `TextBaselineTests` |
| `Nfty.Model` rule D7 | **no field writable after construction** anywhere in the assembly (`AssemblyRulesTests`) | — mutable editing state therefore lives in `Nfty.Core.Editing`, not `Nfty.Model` |
| Docs | `TreatWarningsAsErrors` + `GenerateDocumentationFile`: every public member needs `///` docs | `Directory.Build.props` |
| Spelling | US English everywhere product speaks | CLAUDE.md |
| Commit trailer (implementers) | `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>` | brief |

**Mouse map (author, 2026-10-20):** left = front +1, right = back +1, Shift = −1 (floors at 0),
**Ctrl = constrain** (Shift is taken), Alt = shape from centre, middle-drag = pan, wheel = zoom
anchored on the pointer. Spec's *Testing* bullet still says "right-click adds front, Shift+left
subtracts back" — that line predates the swap; **tests follow the table** (left = front).

**House rules every GUI task obeys** (CLAUDE.md): verify visuals from rendered frames in BOTH themes;
reserve space / toggle ink (Opacity + IsHitTestVisible, never IsVisible on layout-occupying controls,
except the recorded positional exception); token brushes only (`Tokens.axaml`, both dictionaries);
pointer capture + `PointerCaptureLost` = abort; page takes focus on attach (`Focusable`,
`IsTabStop=False`); `RenderOptions.BitmapInterpolationMode="None"` on every view root that shows an
`<Image`; `Background="Transparent"` on anything that must hit-test; stated width needs stated
alignment; `MinWidth="0"` before any `Width` < 64 on `TextBox`/`NumericUpDown`; `FormatString` on every
`NumericUpDown`; a disabled button is a label — gates are re-checked inside the command.

---

## Notes for the controller

**Final review runs on Opus 5.5** (binding user instruction: "make sure the final review of each step
… is done with Opus 5.5"), scoped to the full diff from `e5fe8b6` to the step's last commit.

**Pre-flight (shared files, sequential by task order):** `Styles.axaml` (T10, T11, T12, T13),
`Tokens.axaml` (T10), `KeyHints.cs` (T10), `VisualCapture.cs` (T3, T10, T11, T12, T13),
`DarkModeContrastTests.cs` / `MinimumWindowFitTests.cs` / `TextBaselineTests.cs` (T13),
`ServiceRegistration.cs` + `ShellViewModel.cs` (T8), `ModelPreviewView*.cs` (T3 only),
`IngredientEditorView.axaml.cs` (T4 only), `RegionEditCommand.cs` + `FloodFill.cs` (T1 only).

**Rulings made where spec and code left a gap** (each: why / cost if wrong):

1. **Depth editing lives in `Nfty.Core.Editing`, reusing `RegionEditCommand<T>` + `EditHistory<T>`.**
   Why: `Nfty.Model` forbids any field written after construction (D7, `AssemblyRulesTests`), and
   Core already owns the edit/undo machinery — a second history type would be the "second copy of the
   undo snapshot" `RegionEditCommand`'s own remarks warn about. `DepthSurface.AlphaOf` returns 255 and
   `WithAlpha` is identity (commands run `OpacityLock.Unlocked`, so `Admit` never consults them).
   Cost if wrong: moving four small files.
2. **The variant source rises the variant's STORED pixels** (a Dynamic/Static variant is its
   grayscale value-map), exactly what `nfty model x.igt --variant v` writes. Why: one rendering path
   for CLI and GUI (CLAUDE.md's `VariantPreview` rule). Cost if wrong: a gray model for colorized
   layers; a "rise as colorized sample" option is additive in step 8. **Author should hear this.**
3. **The recipe roll is `Generator.GenerateStreaming(book, new GenerateOptions(1, seed.ToString(),
   RecipeId, EnforceUniqueDna: false))`** — byte-for-byte the Recipe pane hero's `HeroOptions()` — and
   its depth is `StackDepth.ForSelection(recipe, asset.Traits…)`. The roll is still `Ticketer.TryRoll`
   (Generator calls it). Why: step 8's "Rise" button beside Reroll must show the stack the pane shows.
   Cost if wrong: none to the format.
4. **Set depth is recovered by NAME** (`SetItemSelection.Resolve`: layer name → variant name, both
   recorded in `nfty/NNNN.json`). A variant name matching zero or two variants → that asset is paper,
   said in the DEPTH note. The book is found by hash (`CookBookLocator`) and read with
   `CookBookArchive.Read` off the UI thread (full decode: depth maps live in `.igt` entries that
   `ArchivePeek` does not read). Cost if wrong: an ambiguously named book shows paper for some assets.
5. **Standing preview.** A gesture's ±1 is APPLIED to the surface from press to release (undone and
   re-applied on each move, undone on cancel, replaced by the committed command on release), not the
   editor's apply-render-undo, because the cell-label overlay renders AFTER the call returns. Undo,
   Redo, Save, item change and Back all cancel a standing preview first. Cost if wrong: one flag.
6. **Dirty = an item's depth differs from its last-saved depth** (byte compare against a normalized
   baseline), not "history non-empty". Footer: `unsaved depth on 2 variants` / `no unsaved depth` /
   `read-only source — nothing to save`. Undo back to saved = clean. Cost if wrong: wording.
7. **Leave dialog is a new 3-outcome card** (`LeaveDialogViewModel`, `LeaveChoice { Stay, Discard,
   SaveAndLeave }`, default `Stay` so Esc/dismiss keeps editing) built from `ConfirmDialogView`'s exact
   structure; `ConfirmDialogViewModel` is bool-only and shared app-wide, so it is not widened.
8. **Manifest size:** exact bytes for a single model (built while the EXPORT tab is showing, refreshed
   on option or depth change); a batch shows counts and names but **no size** — an estimate is a figure
   the product cannot stand behind (`SetExporter.Plan` precedent: the screen states what export writes).
9. **Batch zip is deterministic:** entries ordinal by name, `LastWriteTime` fixed at
   1980-01-01T00:00Z. Folder shape writes `<picked>/<BatchStem>/…`.
10. **Geometry-fixed rule:** the EXPORT tab's SHAPE section is always laid out and only *inked* for a
    batch (Opacity + IsHitTestVisible). The DEPTH tab's **2D ONLY** section is the LAST thing in the
    rail and uses `IsVisible` — the recorded *positional* exception (nothing below it to shove). Step 8
    appends **Make voxel-ready** at the end of that section, so its arrival moves nothing either. No
    copy anywhere mentions the unbuilt button (a control that reports itself unbuilt is a known defect).
11. **Preview carry-ins (step 6 `progress.md` "Carry into step 7", all six):**
    (a) eye entering the sprite → `Camera` minimum distance becomes **1.05 × Radius** (eye always
    outside the bounding sphere), proven by a "fully zoomed in is never blank" test, retuned only if
    the rendered frame says so; (b) MarkDirty-ahead-of-input → a **33 ms repaint budget that applies
    only while interacting** (`HostInteracting` or the view's own drag), so the idle coalescing tests
    keep their meaning, and `HostInteracting = false` repaints exactly; (c) Auto slot → the tile and
    the canvas slot are both sized (bound tile size; star cell); (d) `SetSubject` keeps the old radius
    → it now **keeps target / yaw / pitch / distance and adopts the new radius** (limits + clip planes
    follow the depth edit; the step-6 test asserting `Assert.Same` is updated); item change re-frames
    via `Reset(aspect)`; (e) M3 → render at **device pixels** (bounds × render scaling × accumulated
    layout scale, bitmap DPI scaled to match); (f) the fill-rule test that passes with the rule deleted
    is made to fail on its own (nearer depth for the second triangle), mutation-probed.
12. **Ctrl constrains on this page only.** `KeyHints` gains Rise-specific hints (`RiseConstrain` = "⌃"
    on macOS, "Ctrl" elsewhere; the view tests `KeyModifiers.Control`). The editor's hints are untouched.
13. **Canvas host is the editor's fixed 320 px tile**, not the mockup's larger canvas (running app wins).
14. **Rise view state (grid on/off, step, tile size, tab) is session-only in step 7** — not written to
    `IEditorViewState`, which belongs to the editor. Cost if wrong: a later additive store.
15. **Icons in step 7's own UI** (so `DeadCodeAuditTests.Every_icon_is_drawn_somewhere` holds):
    `IconRise` on the 3D tile caption; `Icon2dOnly` on every "2D only" chip and the 2D ONLY header.
    Task 10 draws placeholders (`IconLock`) in those two spots; Task 12 swaps them in.
16. **Save writes through the session's LIVE graph** (`session.Current`), raises
    `RiseViewModel.Saved(LoadedCookBook)` — nothing subscribes in step 7. **Step 8 must** wire it to
    the Explorer (`OnEditorSaved`-style refresh), the recents re-record (`RememberOnSave`), and — if Rise
    is opened from an open Ingredient Editor — refresh that editor's `_depths` before the editor can
    save again (otherwise the editor writes its stale depth over Rise's).
17. **Recipe layers in the left rail are inert** (no command, no hover state) until step 10; each row
    shows the layer's own stored-depth summary.
18. **WiringCoverageTests matches commands by NAME across all views**, and Rise reuses names the
    editor binds (`UndoCommand`, `SelectToolCommand`, …), so it cannot see a dead Rise command. Task 13
    adds `RiseWiringTests` deriving Rise's commands by reflection and requiring each in
    `RiseView.axaml` / `RiseView.axaml.cs`.
19. **Spec's Testing bullet (mouse map) is stale** vs the 2026-10-20 swap — controller corrects the
    spec line (doc-only) when the step lands; the author should hear it.

---

## Task list

| # | Task | Project(s) |
|---|------|-----------|
| 1 | Core: `Footprint` extraction + depth edit surface & commands | Nfty.Core |
| 2 | Stack depth for a selection, Set selection recovery, model batch writer | Nfty.Model, Nfty.Core |
| 3 | 3D preview carry-ins (budget, device pixels, camera radius/limits, fill-rule test) | Nfty.Model, Nfty.App |
| 4 | Canvas mapping extraction (`CanvasMapping`, `PixelLattice`), editor refactored onto it | Nfty.App |
| 5 | Rise source model + variant source (incl. save) | Nfty.App |
| 6 | Recipe and Set sources | Nfty.App |
| 7 | `RiseViewModel` core: selection, tools, gestures, history, live mesh, tile, readout | Nfty.App |
| 8 | DEPTH rail, footer, Save/Back, leave dialog, registration, opening line | Nfty.App |
| 9 | EXPORT rail: format, pivot, voxel size, shape, manifest, export | Nfty.App |
| 10 | `RiseView` page: layout, tile re-parenting, overlays, captures | Nfty.App |
| 11 | Canvas interaction: pointer, keys, hover readout — gesture tests | Nfty.App |
| 12 | Icons `rise` + `2d-only` | assets, Nfty.App |
| 13 | Integration pass: sweeps, perf scopes, full capture set, cross-page refinement | Nfty.App.Tests |

---

### Task 1: Core — `Footprint` extraction and the depth edit surface

**Files:**
- Create: `src/Nfty.Core/Editing/Footprint.cs`
- Modify: `src/Nfty.Core/Editing/RegionEditCommand.cs` (`StampDiscs` delegates; `Between` moves out)
- Modify: `src/Nfty.Core/Editing/FloodFill.cs` (`ComputePixels` delegates to `Footprint.Region`)
- Create: `src/Nfty.Core/Editing/DepthSurface.cs` (`DepthPixel`, `DepthSide`, `DepthLook`,
  `DepthSummary`, `ThicknessBucket`, `DepthSurface`)
- Create: `src/Nfty.Core/Editing/DepthStep.cs`, `src/Nfty.Core/Editing/DepthFillAll.cs`
- Test: `tests/Nfty.Core.Tests/FootprintTests.cs`, `tests/Nfty.Core.Tests/DepthEditingTests.cs`

**Interfaces — Consumes:** `IEditSurface<TPixel>`, `RegionEditCommand<TPixel>` (protected ctor
`(OpacityLock)`, abstract `ComputePixels(IEditSurface<TPixel>)`), `EditHistory<TPixel>`,
`ColorMap.FromImage(Image<Rgba32>)`, `Opacity.IsSolid/IsPartial`, `DepthMap.FromBytes/Front/Back`,
`PixelRect(int X, int Y, int Width, int Height)`.

**Produces:**
```csharp
namespace Nfty.Core.Editing;
public static class Footprint
{
    public static List<(int x, int y)> Discs(int width, int height, IReadOnlyList<(int x, int y)> path, int size);
    public static List<(int x, int y)> Region<TPixel>(IEditSurface<TPixel> surface, int seedX, int seedY) where TPixel : struct;
    public static List<(int x, int y)> Box(PixelRect rect, int width, int height);
}
public readonly record struct DepthPixel(byte Front, byte Back);
public enum DepthSide { Front, Back }
public enum DepthLook { Empty, Paper, Uniform, Varied }
public readonly record struct DepthSummary(DepthLook Look, byte Front, byte Back);
public readonly record struct ThicknessBucket(int Total, int Count);
public sealed class DepthSurface : IEditSurface<DepthPixel>
{
    public DepthSurface(Image<Rgba32> art, DepthMap? stored);
    public int Width { get; } public int Height { get; }
    public int SolidCount { get; } public int PartialCount { get; }
    public bool IsSolid(int x, int y);
    public Rgba32 ArtAt(int x, int y);
    public List<(int x, int y)> SameColorRegion(int x, int y);
    public DepthMap? ToDepthMap();                 // null when every solid pixel is 0·0
    public bool SameDepthAs(DepthMap? other);      // null == all zero
    public IReadOnlyList<ThicknessBucket> Histogram();
    public DepthSummary Summary();
    // IEditSurface<DepthPixel>: InBounds, Get, Set, AlphaOf (=> 255), WithAlpha (=> identity)
}
public sealed class DepthStep : RegionEditCommand<DepthPixel>
{ public DepthStep(IReadOnlyCollection<(int x, int y)> pixels, DepthSide side, int delta); }
public sealed class DepthFillAll : RegionEditCommand<DepthPixel>
{ public DepthFillAll(byte front, byte back); }
```

- [ ] **Step 1: Write `FootprintTests` (failing — `Footprint` does not exist).**

```csharp
using Nfty.Core.Editing;
using Xunit;

namespace Nfty.Core.Tests;

public class FootprintTests
{
    [Fact]
    public void A_size_one_disc_is_one_pixel() =>
        Assert.Equal(new[] { (2, 2) }, Footprint.Discs(5, 5, new[] { (2, 2) }, 1));

    [Fact]
    public void A_size_two_disc_is_a_plus_and_size_three_is_the_full_square()
    {
        Assert.Equal(5, Footprint.Discs(5, 5, new[] { (2, 2) }, 2).Count);
        var three = Footprint.Discs(5, 5, new[] { (2, 2) }, 3);
        Assert.Equal(9, three.Count);
        Assert.Contains((1, 1), three);
        Assert.Contains((3, 3), three);
    }

    [Fact]
    public void A_path_is_joined_so_a_fast_stroke_has_no_gaps() =>
        Assert.Equal(new[] { (0, 0), (1, 0), (2, 0), (3, 0), (4, 0), (5, 0) },
            Footprint.Discs(8, 1, new[] { (0, 0), (5, 0) }, 1).OrderBy(p => p.x).ToArray());

    [Fact]
    public void Discs_clip_to_the_canvas_and_never_repeat_a_pixel()
    {
        var cells = Footprint.Discs(3, 3, new[] { (0, 0), (0, 0), (2, 0), (0, 0) }, 3);
        Assert.All(cells, p => Assert.True(p.x is >= 0 and < 3 && p.y is >= 0 and < 3));
        Assert.Equal(cells.Count, cells.Distinct().Count());
    }

    [Fact]
    public void Region_is_the_four_connected_run_of_the_seed_pixel()
    {
        var map = new ValueMap(4, 1);
        new BrushStroke<GrayPixel>(new Brush<GrayPixel>(1, new GrayPixel(50, 255)), new[] { (2, 0) }).Apply(map);
        Assert.Equal(new[] { (0, 0), (1, 0) }, Footprint.Region(map, 0, 0).OrderBy(p => p.x).ToArray());
        Assert.Equal(new[] { (3, 0) }, Footprint.Region(map, 3, 0));          // walled off by (2,0)
    }

    [Fact]
    public void Region_of_an_out_of_bounds_seed_is_empty() =>
        Assert.Empty(Footprint.Region(new ValueMap(2, 2), 5, 5));

    [Fact]
    public void Box_covers_its_width_and_height_and_is_clipped()
    {
        Assert.Equal(6, Footprint.Box(new PixelRect(1, 1, 3, 2), 8, 8).Count);
        Assert.Equal(4, Footprint.Box(new PixelRect(-1, -1, 3, 3), 8, 8).Count);   // (0..1, 0..1)
    }

    [Fact]
    public void The_brush_and_the_fill_still_touch_exactly_their_footprint()
    {
        var map = new ValueMap(6, 6);
        new BrushStroke<GrayPixel>(new Brush<GrayPixel>(3, new GrayPixel(90, 255)), new[] { (2, 2), (4, 2) }).Apply(map);
        var painted = Enumerable.Range(0, 6).SelectMany(y => Enumerable.Range(0, 6).Select(x => (x, y)))
            .Where(p => map.GetValue(p.x, p.y) == 90).ToHashSet();
        Assert.True(painted.SetEquals(Footprint.Discs(6, 6, new[] { (2, 2), (4, 2) }, 3)));
    }
}
```
(`ValueMap.GetValue`, `BrushStroke`, `Brush`, `GrayPixel` are the existing Core types used by
`FloodFillTests`; add `using System.Linq;` if the project does not import it implicitly.)

- [ ] **Step 2: Run, see red.** `dotnet test tests/Nfty.Core.Tests --filter FullyQualifiedName~FootprintTests` → compile error.

- [ ] **Step 3: Implement `Footprint`.** Move the body of `RegionEditCommand.StampDiscs` (disc
  geometry: `d = Math.Max(1, size)`, `r = d / 2.0`, `ir = (int)Math.Ceiling(r)`, the `seen` set, the
  `dx*dx + dy*dy > r*r` test, `Stamp(path[0])` then `Between` for each consecutive pair) into
  `Footprint.Discs`, bounds-checked with `x >= 0 && y >= 0 && x < width && y < height` (identical to
  every surface's `InBounds`). Move `Between` (Bresenham) as a private static of `Footprint`. Move
  `FloodFill`'s BFS (queue, `seen`, `Visit`, exact-equality against the seed pixel via
  `EqualityComparer<TPixel>.Default`) into `Footprint.Region`, returning the visited matching cells in
  BFS order; an out-of-bounds seed returns an empty list. `Box` walks rows then columns over the rect
  intersected with `[0,width) × [0,height)`. Then:

```csharp
// RegionEditCommand.StampDiscs — same signature, now a projection of the footprint.
protected static List<(int x, int y, TPixel pixel)> StampDiscs(
    IEditSurface<TPixel> surface, IReadOnlyList<(int x, int y)> path, int size,
    Func<int, int, TPixel> paint)
{
    var cells = Footprint.Discs(surface.Width, surface.Height, path, size);
    var pixels = new List<(int x, int y, TPixel pixel)>(cells.Count);
    foreach (var (x, y) in cells) pixels.Add((x, y, paint(x, y)));
    return pixels;
}

// FloodFill.ComputePixels — the early-out stays here (it compares against the ADMITTED fill).
var pixels = new List<(int, int, TPixel)>();
if (!surface.InBounds(_seedX, _seedY)) return pixels;
if (EqualityComparer<TPixel>.Default.Equals(surface.Get(_seedX, _seedY), Admit(surface, _fill))) return pixels;
foreach (var (x, y) in Footprint.Region(surface, _seedX, _seedY)) pixels.Add((x, y, _fill));
return pixels;
```
Keep every existing remark comment that explains WHY (joining the path, the per-visit allocation
note) — move it with the code it describes.

- [ ] **Step 4: Run Footprint + every existing editing test.**
  `dotnet test tests/Nfty.Core.Tests` → all green (BrushStroke, EraseStroke, FloodFill, DrawShape,
  MoveSelection, StrokeContinuity, OpacityLock, ColorEditing tests unchanged).

- [ ] **Step 5: Write `DepthEditingTests` (failing).**

```csharp
using Nfty.Core.Editing;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.Core.Tests;

public class DepthEditingTests
{
    /// <summary>'#' solid in color A, '*' solid in color B, '.' clear, '~' half alpha.</summary>
    private static Image<Rgba32> Art(params string[] rows)
    {
        var img = new Image<Rgba32>(rows[0].Length, rows.Length);
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[0].Length; x++)
                img[x, y] = rows[y][x] switch
                {
                    '#' => new Rgba32(200, 120, 40, 255),
                    '*' => new Rgba32(30, 60, 200, 255),
                    '~' => new Rgba32(200, 40, 40, 128),
                    _ => new Rgba32(0, 0, 0, 0),
                };
        return img;
    }

    private static DepthSurface Surface(params string[] rows) { using var a = Art(rows); return new DepthSurface(a, null); }

    [Fact]
    public void A_new_surface_is_paper_on_every_solid_pixel()
    {
        var s = Surface("#.", ".*");
        Assert.Equal(2, s.SolidCount);
        Assert.Equal(new DepthPixel(0, 0), s.Get(0, 0));
        Assert.False(s.IsSolid(1, 0));
        Assert.Null(s.ToDepthMap());                          // flat has ONE spelling: absent
        Assert.Equal(DepthLook.Paper, s.Summary().Look);
    }

    [Fact]
    public void Stored_depth_on_a_transparent_pixel_is_dropped()
    {
        using var art = Art("#.");
        var stored = DepthMap.FromBytes(2, 1, new byte[] { 2, 1, 9, 9 });
        var s = new DepthSurface(art, stored);
        Assert.Equal(new DepthPixel(2, 1), s.Get(0, 0));
        Assert.Equal(new DepthPixel(0, 0), s.Get(1, 0));
        Assert.Equal(new byte[] { 2, 1, 0, 0 }, s.ToDepthMap()!.ToBytes());
    }

    [Fact]
    public void A_step_raises_each_touched_solid_pixel_once_however_often_the_path_crosses_it()
    {
        var s = Surface("###");
        var history = new EditHistory<DepthPixel>();
        var pixels = new[] { (0, 0), (1, 0), (0, 0), (2, 0), (1, 0) };
        Assert.True(history.Do(new DepthStep(pixels, DepthSide.Front, +1), s));
        Assert.All(new[] { 0, 1, 2 }, x => Assert.Equal(new DepthPixel(1, 0), s.Get(x, 0)));
    }

    [Fact]
    public void Lowering_floors_at_zero_and_raising_caps_at_255()
    {
        using var art = Art("##");
        var s = new DepthSurface(art, DepthMap.FromBytes(2, 1, new byte[] { 0, 0, 255, 255 }));
        var h = new EditHistory<DepthPixel>();
        Assert.False(h.Do(new DepthStep(new[] { (0, 0) }, DepthSide.Back, -1), s));   // already 0: no-op
        Assert.False(h.Do(new DepthStep(new[] { (1, 0) }, DepthSide.Front, +1), s));  // already 255
        Assert.False(h.CanUndo);
    }

    [Fact]
    public void A_step_over_transparent_pixels_changes_nothing_and_is_not_an_undo_entry()
    {
        var s = Surface("#.");
        var h = new EditHistory<DepthPixel>();
        Assert.False(h.Do(new DepthStep(new[] { (1, 0) }, DepthSide.Front, +1), s));
        Assert.False(h.CanUndo);
        Assert.Equal(new DepthPixel(0, 0), s.Get(1, 0));
    }

    [Fact]
    public void Front_and_back_are_independent()
    {
        var s = Surface("#");
        var h = new EditHistory<DepthPixel>();
        h.Do(new DepthStep(new[] { (0, 0) }, DepthSide.Back, +1), s);
        h.Do(new DepthStep(new[] { (0, 0) }, DepthSide.Back, +1), s);
        h.Do(new DepthStep(new[] { (0, 0) }, DepthSide.Front, +1), s);
        Assert.Equal(new DepthPixel(1, 2), s.Get(0, 0));
    }

    [Fact]
    public void Undo_and_redo_walk_the_history_exactly()
    {
        var s = Surface("##");
        var h = new EditHistory<DepthPixel>();
        h.Do(new DepthStep(new[] { (0, 0), (1, 0) }, DepthSide.Front, +1), s);
        h.Do(new DepthStep(new[] { (1, 0) }, DepthSide.Front, +1), s);
        h.Undo(s);
        Assert.Equal(new DepthPixel(1, 0), s.Get(1, 0));
        h.Undo(s);
        Assert.Null(s.ToDepthMap());
        h.Redo(s); h.Redo(s);
        Assert.Equal(new DepthPixel(2, 0), s.Get(1, 0));
    }

    [Fact]
    public void Fill_all_sets_every_solid_pixel_and_only_solid_pixels()
    {
        var s = Surface("#.*");
        Assert.True(new EditHistory<DepthPixel>().Do(new DepthFillAll(3, 1), s));
        Assert.Equal(new byte[] { 3, 1, 0, 0, 3, 1 }, s.ToDepthMap()!.ToBytes());
        Assert.Equal(new DepthSummary(DepthLook.Uniform, 3, 1), s.Summary());
    }

    [Fact]
    public void Same_color_region_is_four_connected_and_exact()
    {
        var s = Surface("##*", "#.*", "**#");
        Assert.Equal(new[] { (0, 0), (0, 1), (1, 0) }, s.SameColorRegion(0, 0).OrderBy(p => p).ToArray());
        Assert.Empty(s.SameColorRegion(1, 1));                // a clear pixel has no color to fill by
    }

    [Fact]
    public void The_histogram_counts_solid_pixels_by_total_thickness()
    {
        using var art = Art("###.");
        var s = new DepthSurface(art, DepthMap.FromBytes(4, 1, new byte[] { 0, 0, 1, 1, 1, 1, 7, 7 }));
        Assert.Equal(new[] { new ThicknessBucket(1, 1), new ThicknessBucket(3, 2) }, s.Histogram());
        Assert.Equal(DepthLook.Varied, s.Summary().Look);
    }

    [Fact]
    public void Partial_alpha_is_counted_and_is_never_solid()
    {
        var s = Surface("#~~");
        Assert.Equal(2, s.PartialCount);
        Assert.Equal(1, s.SolidCount);
        Assert.False(s.IsSolid(1, 0));
        Assert.Equal(DepthLook.Empty, Surface("..").Summary().Look);
    }

    [Fact]
    public void Same_depth_treats_absent_as_flat()
    {
        var s = Surface("#");
        Assert.True(s.SameDepthAs(null));
        new DepthFillAll(1, 0).Apply(s);
        Assert.False(s.SameDepthAs(null));
        Assert.True(s.SameDepthAs(s.ToDepthMap()));
    }

    [Fact]
    public void A_step_refuses_a_delta_that_is_not_one() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new DepthStep(new[] { (0, 0) }, DepthSide.Front, 2));
}
```

- [ ] **Step 6: Implement.** `DepthSurface` holds three readonly arrays: `bool[] _solid` (w·h),
  `byte[] _depth` (w·h·2, `DepthMap`'s own front-then-back layout) and a `ColorMap _art`
  (`ColorMap.FromImage(art)` — a copy, so the caller keeps ownership of `art`). The constructor
  throws `ArgumentException` naming both sizes when `stored` is another size; it copies stored depth
  only onto solid pixels; it counts `SolidCount`/`PartialCount`. `Set` ignores out-of-bounds AND
  non-solid pixels ("transparent pixels have no depth"). `ToDepthMap` returns `null` when every byte
  is zero, else `DepthMap.FromBytes(Width, Height, _depth)`. `Histogram` buckets `1 + f + b` over solid
  pixels, ascending by `Total`. `Summary`: `Empty` (no solid), `Paper` (all 0·0), `Uniform(f,b)`,
  `Varied`. `SameColorRegion` returns `[]` for a non-solid seed, else `Footprint.Region(_art, x, y)`.
  `DepthStep`: copy `pixels` with `.ToArray()` in the ctor; validate `delta is 1 or -1`
  (`ArgumentOutOfRangeException`) and `Enum.IsDefined(side)`; base `OpacityLock.Unlocked`;
  `ComputePixels` requires `surface is DepthSurface` (else `ArgumentException`), dedups, skips
  out-of-bounds / non-solid, clamps to 0..255, and **emits only pixels whose value changes** (so a
  no-op gesture is not an undo entry). `DepthFillAll` emits every solid pixel whose value differs from
  `(front, back)`. XML-doc every public member; the class remarks state ruling 1 (why the alpha members
  are inert).

- [ ] **Step 7: Run.** `dotnet test tests/Nfty.Core.Tests` → green. `dotnet build nfty.sln` → 0/0.
- [ ] **Step 8: Mutation-probe** (report results): delete the dedup → the "once however often" test
  goes red; delete the non-solid skip in `Set` → "Stored depth on a transparent pixel" goes red.
- [ ] **Step 9: Commit** `feat(core): footprint extraction + depth edit surface and commands`.

**Must stay green:** all of `Nfty.Core.Tests`; `Nfty.App.Tests` editor suites (`IngredientEditorPaintTests`,
`EditorToolGesturesTests`, `EditorLivePreviewTests`, `EditorMovePreviewTests`).

---

### Task 2: Stack depth for a selection, Set selection recovery, the model batch writer

**Files:**
- Modify: `src/Nfty.Model/StackDepth.cs` (add `ForSelection`)
- Create: `src/Nfty.Core/Output/SetItemSelection.cs`
- Create: `src/Nfty.Model/ModelBatch.cs` (`ModelInput`, `ModelBatchResult`, `ModelBatch`)
- Test: `tests/Nfty.Model.Tests/StackDepthTests.cs` (extend), `tests/Nfty.Core.Tests/SetItemSelectionTests.cs`,
  `tests/Nfty.Model.Tests/ModelBatchTests.cs`

**Interfaces — Consumes:** `StackDepth.Combine`, `LoadedRecipe`/`LoadedIngredient.Depths`,
`SetItem(int Number, string ImagePath, string Dna, string Recipe, IReadOnlyList<RarityAttribute> Rarity,
IReadOnlyList<LayerColor> Layers, IReadOnlyList<string>? AbsentLayers = null)`, `SetWriter.TypeTrait`
(`"Type"`), `Riser.TryBuild`, `ModelWriter.Write`, `PartialAlphaException`, `RiseOptions`, `ModelFile`.

**Produces:**
```csharp
// Nfty.Model
public static DepthMap StackDepth.ForSelection(LoadedRecipe recipe,
    IReadOnlyList<(string IngredientId, string VariantId)> presentBottomFirst, string source = "the selection");
public sealed record ModelInput(string BaseName, Image<Rgba32> Art, DepthMap? Depth);
public sealed record ModelBatchResult(IReadOnlyList<ModelFile> Files, IReadOnlyList<string> Skipped);
public static class ModelBatch
{
    public static string SafeBaseName(string name);
    public static IReadOnlyList<string> UniqueBaseNames(IReadOnlyList<string> names);
    public static IReadOnlyList<string> FileNamesFor(string baseName, ModelFormat format);
    public static ModelBatchResult Build(IEnumerable<ModelInput> inputs, RiseOptions options);
    public static void WriteFolder(string directory, IReadOnlyList<ModelFile> files);
    public static void WriteZip(string zipPath, IReadOnlyList<ModelFile> files);
}
// Nfty.Core.Output
public static class SetItemSelection
{
    public static IReadOnlyList<(string IngredientId, string VariantId)>? Resolve(LoadedRecipe recipe, SetItem item);
}
```

- [ ] **Step 1: Failing tests — `ForSelection`** (append to `StackDepthTests`):

```csharp
[Fact]
public void A_selection_takes_depth_from_the_topmost_solid_layer_and_ignores_color()
{
    // body: 4x1 solid, front 1 everywhere. lock: only x=2 solid, front 3. Value-maps, Dynamic-style.
    using var body = Art.FromAscii("####");
    using var lockArt = Art.FromAscii("..#.");
    var recipe = new LoadedRecipe
    {
        Manifest = new RecipeManifest("chest", "Chest", new[] { "body", "lock" }, Array.Empty<IncompatibilityRule>()),
        Ingredients = new[]
        {
            Layer("body", "oak", body, Art.Depth(body, (_, _) => (1, 0))),
            Layer("lock", "gold", lockArt, Art.Depth(lockArt, (_, _) => (3, 0))),
        },
    };

    var depth = StackDepth.ForSelection(recipe, new[] { ("body", "oak"), ("lock", "gold") });

    Assert.Equal(new byte[] { 1, 0, 1, 0, 3, 0, 1, 0 }, depth.ToBytes());
    // Colorizing changes no alpha, so the stack rule over colorized layers gives the same bytes.
    using var tinted = Colorizer.Apply(lockArt, 200, 0.5, ColorModel.Hsv);
    var (art, viaCombine) = StackDepth.Combine(new[] {
        (body, (DepthMap?)Art.Depth(body, (_, _) => (1, 0))), (tinted, (DepthMap?)Art.Depth(lockArt, (_, _) => (3, 0))) });
    art.Dispose();
    Assert.Equal(viaCombine.ToBytes(), depth.ToBytes());
}

[Fact]
public void A_selection_naming_a_missing_variant_is_refused_by_name()
{
    using var body = Art.FromAscii("#");
    var recipe = new LoadedRecipe
    {
        Manifest = new RecipeManifest("chest", "Chest", new[] { "body" }, Array.Empty<IncompatibilityRule>()),
        Ingredients = new[] { Layer("body", "oak", body, null) },
    };
    var ex = Assert.Throws<KeyNotFoundException>(() => StackDepth.ForSelection(recipe, new[] { ("body", "walnut") }));
    Assert.Contains("walnut", ex.Message);
}

private static LoadedIngredient Layer(string id, string variant, Image<Rgba32> img, DepthMap? depth) => new()
{
    Manifest = new IngredientManifest(id, id, LayerKind.Custom, null, new[] { new Variant(variant, variant, 1) }),
    VariantImages = new Dictionary<string, Image<Rgba32>> { [variant] = img },
    Depths = depth is null ? DepthMaps.None : new Dictionary<string, DepthMap> { [variant] = depth },
};
```
(`Layer` hands BORROWED images to the ingredient; the test never disposes the ingredient. Add the
`using Nfty.Core.Formats; using Nfty.Core.Imaging;` the file needs.)

- [ ] **Step 2: Implement `ForSelection`** — resolve each `(ingId, varId)` against
  `recipe.Ingredients` (build the lookup with a loop, last wins, matching `StackRoll`'s tolerance),
  throw `KeyNotFoundException` naming the missing layer or variant and `source`, build
  `(ing.VariantImages[varId], ing.Depths.GetValueOrDefault(varId))` pairs, call `Combine(pairs,
  source)`, dispose the composited art, return the depth. Doc remark: *only alpha decides the stack
  rule and `Colorizer` keeps alpha, so the stored (uncolorized) images are used; an all-absent roll has
  no layers — callers treat it as paper and do not call this.*

- [ ] **Step 3: Failing tests — `SetItemSelectionTests`** (round-trip through a real cook):

```csharp
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.Core.Tests;

public class SetItemSelectionTests
{
    private static LoadedCookBook Book(string lockName2 = "Iron")
    {
        LoadedIngredient Ing(string id, string name, params (string Id, string Name)[] vs) => new()
        {
            Manifest = new IngredientManifest(id, name, LayerKind.Custom, null,
                vs.Select(v => new Variant(v.Id, v.Name, 1)).ToList()),
            VariantImages = vs.ToDictionary(v => v.Id, v => new Image<Rgba32>(2, 2, new Rgba32((byte)v.Id[0], 9, 9, 255))),
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("chest", "Chest", new[] { "body", "lock" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[]
            {
                Ing("body", "Body", ("oak", "Oak"), ("walnut", "Walnut")),
                Ing("lock", "Lock", ("gold", "Gold"), ("iron", lockName2)),
            },
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Chest Demo", new Dimensions(2, 2),
                new Collection("Chest Demo", "", "CH"), new Dictionary<string, double> { ["chest"] = 1 }),
            Recipes = new[] { recipe },
        };
    }

    [Fact]
    public void Every_cooked_asset_maps_back_onto_the_layers_that_made_it()
    {
        using var book = Book();
        using var set = Generator.Generate(book, new GenerateOptions(4, "s"));
        var dir = Directory.CreateTempSubdirectory("nfty-sel-").FullName;
        SetWriter.Write(set, dir, pack: false);
        using var read = SetReader.Read(dir);

        foreach (var item in read.Items)
        {
            var asset = set.Assets.Single(a => a.SetNumber == item.Number);
            var expected = asset.Traits.Select(t => (t.IngredientId, t.VariantId)).ToList();
            Assert.Equal(expected, SetItemSelection.Resolve(book.Recipes[0], item));
        }
    }

    [Fact]
    public void A_variant_name_two_variants_share_cannot_be_resolved()
    {
        using var book = Book(lockName2: "Gold");             // gold and iron are both named "Gold"
        var item = new SetItem(1, "x.png", "dna", "chest",
            new[] { new RarityAttribute("Type", "Chest", 100), new RarityAttribute("Body", "Oak", 50),
                    new RarityAttribute("Lock", "Gold", 50) },
            Array.Empty<LayerColor>());
        Assert.Null(SetItemSelection.Resolve(book.Recipes[0], item));
    }

    [Fact]
    public void Another_recipe_and_an_absent_layer_are_handled()
    {
        using var book = Book();
        var other = new SetItem(1, "x.png", "dna", "barrel", Array.Empty<RarityAttribute>(), Array.Empty<LayerColor>());
        Assert.Null(SetItemSelection.Resolve(book.Recipes[0], other));

        var lockless = new SetItem(2, "x.png", "dna", "chest",
            new[] { new RarityAttribute("Type", "Chest", 100), new RarityAttribute("Body", "Walnut", 50),
                    new RarityAttribute("Lock", "none", 50) },
            Array.Empty<LayerColor>(), AbsentLayers: new[] { "Lock" });
        Assert.Equal(new[] { ("body", "walnut") }, SetItemSelection.Resolve(book.Recipes[0], lockless));
    }
}
```
(Check `GeneratedSet.Assets` / `SetNumber` names against `GeneratedSet.cs` before running; adjust the
test, not the production type, if a name differs.)

- [ ] **Step 4: Implement `SetItemSelection.Resolve`** — recipe id must equal `item.Recipe`
  (ordinal) else `null`; traits = rarity rows minus `SetWriter.TypeTrait` minus names in
  `item.AbsentLayers`; walk `recipe.Manifest.LayerOrder`; skip a layer whose NAME is absent; a missing
  trait or a variant-name match count ≠ 1 → `null`; else append `(ingredientId, variant.Id)`. Remarks
  cite `SelectionOdds.Of(SetItem)` as the same name-based reading and say why names (they are all a
  Set records).

- [ ] **Step 5: Failing tests — `ModelBatchTests`.**

```csharp
using System.IO.Compression;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Tests;

public class ModelBatchTests
{
    [Theory]
    [InlineData("Body/Oak: v2?", "Body-Oak- v2-")]
    [InlineData("  ..  ", "model")]
    [InlineData("Lid", "Lid")]
    public void Base_names_are_plain_file_names_on_every_platform(string asked, string expected) =>
        Assert.Equal(expected, ModelBatch.SafeBaseName(asked));

    [Fact]
    public void Colliding_names_get_a_counter_ignoring_case() =>
        Assert.Equal(new[] { "Oak", "oak-2", "Oak-3", "Walnut" },
            ModelBatch.UniqueBaseNames(new[] { "Oak", "oak", "Oak", "Walnut" }));

    [Theory]
    [InlineData(ModelFormat.Glb)]
    [InlineData(ModelFormat.GltfWithBin)]
    [InlineData(ModelFormat.GltfEmbedded)]
    public void The_names_the_manifest_lists_are_the_names_the_writer_writes(ModelFormat format)
    {
        using var art = Art.FromAscii("##");
        var options = new RiseOptions { Format = format };
        var mesh = Riser.TryBuild(art, null, options)!;
        Assert.Equal(ModelBatch.FileNamesFor("Body-Oak", format),
            ModelWriter.Write(mesh, art, options, "Body-Oak").Select(f => f.FileName));
    }

    [Fact]
    public void A_batch_skips_and_names_what_cannot_rise_and_writes_the_rest()
    {
        using var ok = Art.FromAscii("##");
        using var see = Art.FromAscii("#~");
        using var none = Art.FromAscii("..");
        var result = ModelBatch.Build(new[]
        {
            new ModelInput("a", ok, null), new ModelInput("b", see, null), new ModelInput("c", none, null),
        }, new RiseOptions());
        Assert.Equal(new[] { "a.glb" }, result.Files.Select(f => f.FileName));
        Assert.Equal(2, result.Skipped.Count);
        Assert.StartsWith("b:", result.Skipped[0]);
        Assert.StartsWith("c:", result.Skipped[1]);
    }

    [Fact]
    public void The_zip_is_byte_identical_for_identical_input_and_lists_every_file()
    {
        using var art = Art.FromAscii("##");
        var files = ModelBatch.Build(new[] { new ModelInput("b", art, null), new ModelInput("a", art, null) },
            new RiseOptions { Format = ModelFormat.GltfWithBin }).Files;
        var dir = Directory.CreateTempSubdirectory("nfty-zip-").FullName;
        string one = Path.Combine(dir, "1.zip"), two = Path.Combine(dir, "2.zip");
        ModelBatch.WriteZip(one, files);
        ModelBatch.WriteZip(two, files);
        Assert.Equal(File.ReadAllBytes(one), File.ReadAllBytes(two));
        using var zip = ZipFile.OpenRead(one);
        Assert.Equal(new[] { "a.bin", "a.gltf", "b.bin", "b.gltf" }, zip.Entries.Select(e => e.FullName));
    }

    [Fact]
    public void A_folder_receives_every_file()
    {
        using var art = Art.FromAscii("#");
        var files = ModelBatch.Build(new[] { new ModelInput("x", art, null) }, new RiseOptions()).Files;
        var dir = Path.Combine(Directory.CreateTempSubdirectory("nfty-dir-").FullName, "Body-models");
        ModelBatch.WriteFolder(dir, files);
        Assert.True(File.Exists(Path.Combine(dir, "x.glb")));
    }
}
```

- [ ] **Step 6: Implement `ModelBatch`.** `SafeBaseName`: replace every char in the explicit set
  `\ / : * ? " < > |` and every char `< 0x20` with `-` (an explicit set, NOT
  `Path.GetInvalidFileNameChars()`, which differs by OS), trim leading/trailing spaces and dots, empty →
  `"model"`. `UniqueBaseNames`: `SafeBaseName` each, then on a case-insensitive collision append `-2`,
  `-3`, … (skipping any suffix already taken). `FileNamesFor`: Glb → `[b.glb]`, GltfWithBin →
  `[b.gltf, b.bin]`, GltfEmbedded → `[b.gltf]`. `Build` enumerates `inputs` ONCE, finishing each input
  before asking for the next (documented: a lazy sequence may free an input when the next is
  requested): `Riser.TryBuild(art, depth, options, $"'{baseName}'")`; `PartialAlphaException` →
  skipped `"{baseName}: {ex.Message}"`; `null` → skipped `"{baseName}: nothing in it is fully opaque,
  so there is nothing to rise"`; else `ModelWriter.Write(mesh, art, options, baseName)`. `WriteFolder`
  creates the directory and writes each file. `WriteZip` writes entries **ordinal-sorted by name**,
  `CompressionLevel.Optimal`, `LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero)`,
  `FileMode.Create`. Every type is static or a positional record — D7 holds.

- [ ] **Step 7: Run.** `dotnet test tests/Nfty.Model.Tests` and `tests/Nfty.Core.Tests` → green
  (`AssemblyRulesTests` included). Build 0/0.
- [ ] **Step 8: Commit** `feat(model): stack depth for a selection, set selection recovery, model batch`.

**Must stay green:** `StackDepthTests`, `RiserTests`, `GltfWriterTests`, `GlbWriterTests`,
`AssemblyRulesTests` (D7 + one-way arrow), `SelectionOddsTests`, `SetReaderTests`, `SetWriterTests`.

---

### Task 3: 3D preview carry-ins

**Files:**
- Modify: `src/Nfty.Model/Render/Camera.cs` (`MinDistanceFactor` 0.05 → 1.05; add `WithSubject`)
- Modify: `src/Nfty.App/ViewModels/ModelPreviewViewModel.cs` (`SetSubject` adopts the new radius)
- Modify: `src/Nfty.App/Views/ModelPreviewView.axaml.cs` (interaction budget, device pixels, Perf scope)
- Modify tests: `tests/Nfty.Model.Tests/CameraTests.cs`, `tests/Nfty.Model.Tests/RasterTests.cs`,
  `tests/Nfty.App.Tests/ModelPreviewViewModelTests.cs`, `tests/Nfty.App.Tests/ModelPreviewViewTests.cs`

**Produces:**
```csharp
public Camera Camera.WithSubject(Vector3 min, Vector3 max);            // same Target/Distance/Yaw/Pitch, new Radius (distance re-clamped)
public void ModelPreviewViewModel.SetSubject(Vector3 min, Vector3 max); // now: CameraState = CameraState.WithSubject(min, max) unless the radius is unchanged
internal bool ModelPreviewView.HostInteracting { get; set; }            // true while a host gesture runs; false => RepaintNow()
internal Func<long> ModelPreviewView.Clock { get; set; } = Stopwatch.GetTimestamp;
internal static readonly TimeSpan ModelPreviewView.RepaintBudget = TimeSpan.FromMilliseconds(33);
```

- [ ] **Step 1 (a): red test — "fully zoomed in is never blank"** in `RasterTests`:

```csharp
[Theory]
[InlineData(0f, 0f)]
[InlineData(30f, 20f)]
[InlineData(-60f, 45f)]
public void Fully_zoomed_in_the_frame_is_never_blank(float yawDeg, float pitchDeg)
{
    using var art = Art.FromAscii(Enumerable.Range(0, 16).Select(y =>
        new string(Enumerable.Range(0, 16).Select(x => (x - 7.5) * (x - 7.5) + (y - 7.5) * (y - 7.5) <= 49 ? '#' : '.').ToArray())).ToArray());
    var mesh = Riser.TryBuild(art, null, new RiseOptions())!;
    var framed = Camera.FramedOn(mesh.Min, mesh.Max);
    var camera = new Camera(framed.Target, framed.Distance, yawDeg * MathF.PI / 180f,
        pitchDeg * MathF.PI / 180f, framed.Radius).Zoom(1e-6f);
    var target = new RenderTarget(64, 64);
    target.Clear(new Rgba32(0, 0, 0, 0));
    Raster.Render(mesh, art, camera, target);
    Assert.True(target.Color.Count(c => c.A == 255) > 0, "the eye is inside the sprite: blank frame");
}
```
Run → red at 0.05 (record which cases). Change `MinDistanceFactor` to **1.05f**, update the XML doc
(`1.05 × Radius`: the eye stays outside the subject's bounding sphere) and update `CameraTests`'
`Zoom_scales_distance_and_clamps_at_the_minimum` (`0.05f` → `1.05f`) and rename/adjust
`Zooming_far_out_stops_at_twenty_radii_and_back_in_stops_at_a_twentieth` to the new floor. Re-run →
green. Capture `preview-zoomed-in-{light,dark}.png` (Task 10 wires the page; here use a 200×200
`ModelPreviewView` in a window, `ZoomStep(1)` ×40, `RepaintNow`) and LOOK: if 1.05 still lands the eye
uncomfortably close, raise it; if the zoom stops too far out to inspect a 512px sprite, lower it no
further than the test allows. Record the chosen factor and why in the report.

- [ ] **Step 2 (d): `Camera.WithSubject` + VM.** Factor the radius formula out of `FramedOn` into a
  private static `RadiusOf(min, max)` (`MathF.Max(Vector3.Distance(min, max) / 2f, MinRadius)`), used
  by both. `WithSubject` returns `new Camera(Target, Distance, Yaw, Pitch, RadiusOf(min, max))`.
  `SetSubject` stores the bounds and, when `RadiusOf` differs from `CameraState.Radius`, assigns
  `CameraState = CameraState.WithSubject(min, max)`. Replace the step-6 test that asserted
  `Assert.Same(before, vm.CameraState)` with:

```csharp
[Fact]
public void Set_subject_keeps_the_view_and_adopts_the_new_radius()
{
    var vm = New();
    vm.OrbitStep(new Vector2(1, 0));
    vm.ZoomStep(1f);
    var before = vm.CameraState;
    vm.SetSubject(new Vector3(-50, -50, -2), new Vector3(50, 50, 2));
    var after = vm.CameraState;
    Assert.Equal(before.Target, after.Target);
    Assert.Equal(before.Yaw, after.Yaw);
    Assert.Equal(before.Pitch, after.Pitch);
    Assert.Equal(Vector3.Distance(new(-50, -50, -2), new(50, 50, 2)) / 2f, after.Radius, precision: 3);
    Assert.True(after.Distance >= after.MinDistance);
}

[Fact]
public void Set_subject_with_the_same_extent_changes_nothing()
{
    var vm = New();
    var before = vm.CameraState;
    vm.SetSubject(new Vector3(-1, -1, -1), new Vector3(1, 1, 1));   // New() frames ±1
    Assert.Same(before, vm.CameraState);
}
```

- [ ] **Step 3 (b): interaction budget.** In `ModelPreviewView`: `long _lastRepaint` set in
  `RepaintNow` from `Clock()`. `Interacting => HostInteracting || _dragOrigin is not null`.
  `MarkDirty`: unchanged when not interacting (one Render-priority post). When interacting and
  `Clock() - _lastRepaint < RepaintBudget` (in Stopwatch ticks), schedule
  `DispatcherTimer.RunOnce(OnBudgetElapsed, remaining, DispatcherPriority.Render)` instead (verify the
  overload against the installed Avalonia 12.1.1 assembly; if absent, a one-shot `DispatcherTimer`);
  `OnBudgetElapsed` repaints only if still pending AND the clock now says the budget elapsed, else
  reschedules — so a frozen test clock holds deterministically. `HostInteracting` setter: on
  true→false call `RepaintNow()` (exact on gesture end). Wrap `RepaintNow`'s rasterize in
  `using var _ = Perf.Measure("ModelPreviewView.Repaint");`. Tests:

```csharp
[AvaloniaFact]
public void While_the_host_is_interacting_repaints_wait_for_the_budget_and_the_end_is_exact()
{
    var (window, view, vm) = Render();          // the file's existing fixture helper
    try
    {
        long now = 1_000_000;
        view.Clock = () => now;                  // frozen: inside the budget forever
        view.RepaintNow();
        int before = view.RepaintCount;
        view.HostInteracting = true;
        vm.Orbit(new Vector2(5, 0)); Dispatcher.UIThread.RunJobs();
        vm.Orbit(new Vector2(5, 0)); Dispatcher.UIThread.RunJobs();
        Assert.Equal(before, view.RepaintCount);
        Assert.True(view.RepaintPending);
        view.HostInteracting = false;
        Assert.Equal(before + 1, view.RepaintCount);
        Assert.False(view.RepaintPending);
    }
    finally { window.Close(); }
}

[AvaloniaFact]
public void A_held_repaint_lands_once_the_budget_has_passed()
{
    var (window, view, vm) = Render();
    try
    {
        view.RepaintNow();
        int before = view.RepaintCount;
        view.HostInteracting = true;
        vm.Orbit(new Vector2(5, 0));
        var until = DateTime.UtcNow.AddSeconds(2);
        while (view.RepaintCount == before && DateTime.UtcNow < until) Dispatcher.UIThread.RunJobs();
        Assert.Equal(before + 1, view.RepaintCount);
    }
    finally { window.Close(); }
}
```
`Several_camera_changes_in_one_turn_make_one_repaint` and every other existing view test must stay
green unchanged (the budget does not apply when idle).

- [ ] **Step 4 (e): device pixels.** In `RepaintNow` compute
  `double scale = (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1) * (this.TransformToVisual(top)?.M11 ?? 1)`
  (`top` = the TopLevel; `M11` of the accumulated transform carries the shell's `LayoutTransformControl`
  scale); render target `ceil(Bounds.Width × scale) × ceil(Bounds.Height × scale)`; the
  `WriteableBitmap` DPI = `96 × scale` so it lays out at `Bounds`. Camera pointer maths keep using
  logical `Bounds` (aspect is unchanged). Test:

```csharp
[AvaloniaFact]
public void Under_a_layout_scale_the_bitmap_has_one_pixel_per_device_pixel()
{
    var vm = new ModelPreviewViewModel(new Vector3(-1), new Vector3(1));
    var view = new Views.ModelPreviewView { Width = 100, Height = 100, DataContext = vm, Mesh = CubeMesh(), Texture = CubeTexture() };
    var host = new LayoutTransformControl { LayoutTransform = new ScaleTransform(1.2, 1.2), Child = view };
    var window = new Window { Content = host, Width = 400, Height = 400 };
    window.Show(); Dispatcher.UIThread.RunJobs();
    try
    {
        view.RepaintNow();
        Assert.Equal(new PixelSize(120, 120), ((WriteableBitmap)view.Surface.Source!).PixelSize);
    }
    finally { window.Close(); }
}
```
(`CubeMesh`/`CubeTexture`: reuse the file's existing mesh fixture helpers by their real names.)

- [ ] **Step 5 (f): the fill-rule test.** In `RasterTests.The_fill_rule_does_not_depend_on_draw_order`
  give triangle A (drawn second) a strictly NEARER depth than B so the depth test can never decide the
  diagonal (construct its three `Raster.ScreenVertex` values directly with the nearer depth component —
  read `ScreenVertex`'s field order and the depth comparison in `FillTriangle` first). Probe: delete
  the top-left rule in `FillTriangle` → this test must now go red on its own; restore.
- [ ] **Step 6: Run** `dotnet test tests/Nfty.Model.Tests` and
  `dotnet test tests/Nfty.App.Tests --filter "FullyQualifiedName~ModelPreview"` → green; then the full
  App suite. Build 0/0.
- [ ] **Step 7: Commit** `fix(preview): step-6 carry-ins — eye outside the subject, radius follows depth, interaction budget, device pixels`.

---

### Task 4: Canvas mapping extraction

**Files:**
- Create: `src/Nfty.App/Services/CanvasMapping.cs`
- Create: `src/Nfty.App/Views/PixelLattice.cs` (`internal static`)
- Modify: `src/Nfty.App/Views/IngredientEditorView.axaml.cs` (`Geometry`, `ClampPan`, `ZoomAt`,
  `AddPoint`, `BuildBackdrop` delegate; behaviour byte-identical)
- Modify: `src/Nfty.App/ViewModels/IngredientEditorViewModel.Zoom.cs` (constants alias `CanvasMapping`)
- Test: `tests/Nfty.App.Tests/CanvasMappingTests.cs`

**Produces:**
```csharp
namespace Nfty.App.Services;
public readonly record struct CanvasFrame(double Scale, double OffX, double OffY);
public static class CanvasMapping
{
    public const double ZoomMin = 1, ZoomMax = 16, ZoomStep = 1.25, PanStep = 24;
    public static bool TryFrame(int artW, int artH, double surfaceW, double surfaceH, double zoom, double panX, double panY, out CanvasFrame frame);
    public static (double X, double Y) ClampPan(int artW, int artH, double surfaceW, double surfaceH, double zoom, double panX, double panY);
    public static (double X, double Y) PanKeeping(int artW, int artH, double surfaceW, double surfaceH, CanvasFrame before, double zoomBefore, double zoomAfter, double atX, double atY);
    public static (int X, int Y) PixelAt(CanvasFrame frame, double x, double y, int artW, int artH);   // clamped — gestures
    public static (int X, int Y)? PixelUnder(CanvasFrame frame, double x, double y, int artW, int artH); // null off the art — readouts
}
// Nfty.App.Views
internal static class PixelLattice
{
    internal static DrawingBrush? Build(double cell, double artX, double artY, IBrush light, IBrush dark); // null when cell < 2
}
```
The arithmetic is the editor's, verbatim: `scale = min(sw/artW, sh/artH) × zoom`,
`off = (s − art×scale)/2 + pan`; clamp room `|s − art × fit × zoom| / 2`; anchored pan
`at − c × newScale − (s − art × newScale)/2` with `c = (at − off)/scale`; pixel
`(int)((p − off)/scale)` clamped. The lattice brush is the editor's `DrawingGroup` (tile = 2·cell,
two dark squares, light ground) with the `Mod(art, tile)` phase.

- [ ] **Step 1: Failing `CanvasMappingTests`** (pure `[Fact]`s):

```csharp
using Nfty.App.Services;
using Xunit;

namespace Nfty.App.Tests;

public class CanvasMappingTests
{
    [Fact]
    public void Fitted_square_art_fills_the_tile()
    {
        Assert.True(CanvasMapping.TryFrame(8, 8, 320, 320, 1, 0, 0, out var f));
        Assert.Equal(new CanvasFrame(40, 0, 0), f);
    }

    [Fact]
    public void Wide_art_letterboxes_vertically()
    {
        CanvasMapping.TryFrame(16, 8, 320, 320, 1, 0, 0, out var f);
        Assert.Equal(new CanvasFrame(20, 0, 80), f);
    }

    [Fact]
    public void Zoom_and_pan_compose_into_one_frame()
    {
        CanvasMapping.TryFrame(8, 8, 320, 320, 2, 10, 0, out var f);
        Assert.Equal(80, f.Scale);
        Assert.Equal(-150, f.OffX);
    }

    [Fact]
    public void A_pixel_centre_maps_back_to_its_pixel_and_off_art_is_null()
    {
        CanvasMapping.TryFrame(8, 8, 320, 320, 1, 0, 0, out var f);
        Assert.Equal((3, 5), CanvasMapping.PixelAt(f, 3.5 * 40, 5.5 * 40, 8, 8));
        Assert.Equal((7, 0), CanvasMapping.PixelAt(f, 999, -5, 8, 8));
        Assert.Null(CanvasMapping.PixelUnder(f, 999, 5, 8, 8));
    }

    [Fact]
    public void Pan_is_zero_at_fit_and_bounded_by_half_the_overhang_when_zoomed()
    {
        Assert.Equal((0, 0), CanvasMapping.ClampPan(8, 8, 320, 320, 1, 50, -50));
        Assert.Equal((160, -160), CanvasMapping.ClampPan(8, 8, 320, 320, 2, 999, -999));
    }

    [Fact]
    public void An_anchored_zoom_keeps_the_point_under_the_pointer()
    {
        CanvasMapping.TryFrame(8, 8, 320, 320, 1, 0, 0, out var before);
        var (px, py) = CanvasMapping.PanKeeping(8, 8, 320, 320, before, 1, 2, 100, 60);
        CanvasMapping.TryFrame(8, 8, 320, 320, 2, px, py, out var after);
        Assert.Equal((100 - before.OffX) / before.Scale, (100 - after.OffX) / after.Scale, 9);
        Assert.Equal((60 - before.OffY) / before.Scale, (60 - after.OffY) / after.Scale, 9);
    }

    [Fact]
    public void No_frame_without_size() => Assert.False(CanvasMapping.TryFrame(8, 8, 0, 320, 1, 0, 0, out _));
}
```

- [ ] **Step 2: Implement `CanvasMapping` + `PixelLattice`, then refactor the editor** so
  `Geometry`, `ClampPan`, `ZoomAt`, `AddPoint` and `BuildBackdrop` call them (keep the editor's caching
  `_backdropFrom`, `Flatten`, and every remark comment; the remarks now say "the arithmetic is
  `CanvasMapping`'s — shared with the Rise page"). `IngredientEditorViewModel.ZoomMin/ZoomMax/ZoomStep/
  PanStep` become `= CanvasMapping.ZoomMin` etc. (they are public consts tests reference).
- [ ] **Step 3: Run** `dotnet test tests/Nfty.App.Tests --filter "FullyQualifiedName~CanvasMapping|FullyQualifiedName~CanvasZoom|FullyQualifiedName~CanvasGrid|FullyQualifiedName~EditorOverlay|FullyQualifiedName~EditorToolGestures|FullyQualifiedName~EditorLivePreview"` → green, then the full App suite.
- [ ] **Step 4: Pixel-prove no change.** Before touching the editor, run
  `NFTY_CAPTURE=1 NFTY_CAPTURE_DIR=<dir>/before dotnet test tests/Nfty.App.Tests --filter "FullyQualifiedName~Capture_editor"`;
  after the refactor run the same into `<dir>/after`; byte-compare every `editor-*.png` pair and report
  "N frames byte-identical". Keep `<dir>/after` — Task 13 diffs against it.

**Must stay green:** `CanvasZoomTests`, `CanvasGridTests`, `EditorOverlayTests`, `EditorToolGesturesTests`,
`EditorLivePreviewTests`, `EditorMovePreviewTests`, `ToolstripLayoutTests`, `EditorViewStateTests`.
- [ ] **Step 5: Commit** `refactor(app): one canvas mapping — CanvasMapping and PixelLattice, editor moved onto them`.

---

### Task 5: Rise source model and the variant source

**Files:**
- Create: `src/Nfty.App/Rise/RiseSubject.cs`, `src/Nfty.App/Rise/RiseItem.cs`,
  `src/Nfty.App/Rise/RiseSource.cs`, `src/Nfty.App/Rise/VariantRiseSource.cs`
- Test: `tests/Nfty.App.Tests/RiseFixtures.cs`, `tests/Nfty.App.Tests/VariantRiseSourceTests.cs`

**Interfaces — Consumes:** Task 1 (`DepthSurface`, `EditHistory<DepthPixel>`), Task 2 (`ModelInput`),
`IImageBridge.ToBitmap`, `ICookBookSession`, `CookBookEdits.UpsertIngredient`,
`CookBookPersistence.PersistAsync`, `IngredientArchive.WriteAsync(path, manifest, images, depths)`,
`Validator.ValidateIngredient`, `LayerDepth.DepthOf(manifest, id)`, `LayerDepth.Count(manifest)`,
`Opacity.HasPartialAlpha(LoadedCookBook)`.

**Produces** (namespace `Nfty.App.Rise`):
```csharp
public sealed class RiseSubject : IDisposable
{
    public RiseSubject(Image<Rgba32> art, DepthMap? stored, bool ownsArt = true);
    public Image<Rgba32> Art { get; }
    public DepthSurface Depth { get; private set; }
    public EditHistory<DepthPixel> History { get; private set; }
    public bool IsRisable { get; }            // PartialCount == 0 && SolidCount > 0 (art never changes)
    public bool IsDirty { get; }              // !Depth.SameDepthAs(saved)
    public void MarkSaved();                  // saved = Depth.ToDepthMap()
    public void Reseed(DepthMap? stored);     // new surface + empty history + saved = normalized stored
}
public sealed partial class RiseItem : ObservableObject, IDisposable
{
    public RiseItem(string id, string name, string caption, string exportBaseName, Bitmap? thumbnail, bool ownsThumbnail);
    public string Id, Name, Caption, ExportBaseName { get; }
    [ObservableProperty] Bitmap? Thumbnail; [ObservableProperty] bool IsChecked; [ObservableProperty] bool IsSelected;
    public RiseSubject? Subject { get; }      // null until the source loads it (Set)
    internal void Attach(RiseSubject subject);
    public string DepthChip { get; }          // "2D only" | "empty" | "paper" | "f · b" | "varied" | "" (not loaded)
    public bool IsChipWarning { get; }        // partial alpha
    public bool IsChipShown { get; }          // Subject loaded
    public bool IsRisableOrUnknown { get; }   // Subject?.IsRisable ?? true
    public bool IsDirty { get; }
    public void RefreshDepth();               // re-raises DepthChip / IsDirty
}
public enum RiseSourceKind { Variant, Recipe, Set }
public sealed record PartialAlphaRow(string Name, string CountText);
public abstract class RiseSource : IDisposable
{
    public abstract RiseSourceKind Kind { get; }
    public abstract string Kicker { get; }                 // left header, e.g. "BODY · VARIANTS"
    public abstract string Title { get; }                  // "Body / Oak" — messages and the leave card
    public abstract IReadOnlyList<RiseItem> Items { get; }
    public abstract RiseItem InitialItem { get; }
    public abstract IReadOnlyList<string> Facts { get; }   // left subhead chips
    public virtual bool BookIsTwoDOnly => false;
    public virtual bool IsEditable => false;               // depth may be painted (scratch) and saved
    public virtual bool IsLocked => false;                 // editable, but the edit lock is closed
    public virtual bool CanBatch => false;
    public virtual string? ExportRefusal => null;
    public abstract string BatchStem { get; }
    public virtual string? ReadOnlyPill => null;
    public abstract string DepthNote { get; }
    public abstract string OpeningLine { get; }
    public virtual string RailFootNote => "";
    public virtual IReadOnlyList<PartialAlphaRow> PartialAlphaRows { get; }   // loaded subjects with PartialCount > 0, "{n:N0} px"
    public abstract RiseSubject Open(RiseItem item);
    public virtual IEnumerable<ModelInput> ExportInputs(IReadOnlyList<RiseItem> items, IReadOnlyList<string> baseNames);
    public virtual Task SaveAsync() => throw new NotSupportedException("This source is read-only.");
    public event Action? Changed;  protected void RaiseChanged();
    public virtual void Dispose();                         // disposes every item (subjects, owned thumbnails)
}
public sealed class VariantRiseSource : RiseSource
{
    public VariantRiseSource(LoadedRecipe recipe, LoadedIngredient ingredient, string selectedVariantId,
        IImageBridge bridge, ICookBookSession session, Func<bool>? isEditing = null, string? looseSavePath = null);
    public event Action<LoadedCookBook>? Saved;
}
```

**Behaviour (variant):** one item per `ingredient.Manifest.Variants` in manifest order; subject art
= `ingredient.VariantImages[id].Clone()` (owned — ruling 2: STORED pixels), depth seeded from
`ingredient.Depths.GetValueOrDefault(id)`; thumbnail `bridge.ToBitmap(art)`; caption
`weight {w:0.##} · {pct}%` (invariant, pct = round(w / Σw × 100)); export base name
`{ingredient.Name}-{variant.Name}`; `BatchStem = $"{ingredient.Name}-models"`; `Kicker =
$"{ING NAME UPPER} · VARIANTS"`; `Title = $"{ingredient.Name} / {variant.Name}"` (selected);
`Facts = [kind word lower, $"layer {LayerDepth.DepthOf} of {LayerDepth.Count} in {recipe.Name}"]` —
for a loose `.igt` (`looseSavePath` set) the second fact is `"loose ingredient"`; `CanBatch = true`;
`IsEditable = true`; `IsLocked = isEditing is not null && !isEditing()` (read LIVE, like the editor);
`BookIsTwoDOnly` = `Opacity.HasPartialAlpha(session.Current!)` for a book source,
`Opacity.HasPartialAlpha(ingredient)` for a loose one — computed once at construction; `DepthNote` paper explanation (`"Thickness is in voxels: 1 + front + back. A flat
sprite is paper, one voxel thick."`) or, when locked, `"The CookBook is read-only. Try depth freely
here — Save needs the lock opened in the Explorer."`; `OpeningLine` per Task 8's table.

**Save** (`SaveAsync`): `if (IsLocked) return;` **first line** (a disabled button is a label).
Build `depths` = copy of the LIVE ingredient's `Depths`, then per item with a loaded subject:
`map = subject.Depth.ToDepthMap()` → set or remove. Validate with
`Validator.ValidateIngredient(new LoadedIngredient { Manifest, VariantImages, Depths = depths })` →
problems → throw `InvalidOperationException(string.Join(NewLine, problems))` (the VM shows it).
**Loose:** `IngredientArchive.WriteAsync(tmp, manifest, images, depths)` → `File.Move(tmp, path, true)`
(tmp cleanup in `finally`), no session. **Book:** live ingredient = `session.Current!.Recipes.First(r
=> r.Manifest.Id == recipeId).Ingredients.First(i => i.Manifest.Id == ingredientId)`; `newIng = new
LoadedIngredient { Manifest = live.Manifest, VariantImages = live.VariantImages, Depths = depths }`
(images SHARED — dispose nothing); `book3 = await CookBookPersistence.PersistAsync(session,
CookBookEdits.UpsertIngredient(session.Current!, recipeId, newIng))`; `Saved?.Invoke(book3)`. Then
every subject `MarkSaved()` and every item `RefreshDepth()`.

- [ ] **Step 1: `RiseFixtures`** — an 8×8 two-recipe-free fixture written to disk:

```csharp
internal static class RiseFixtures
{
    // Body "oak": a rounded slab, rows 1-3 color A, rows 4-6 color B (same-color fill has two regions).
    internal static readonly string[] Oak =
        { "........", "..AAAA..", ".AAAAAA.", ".AAAAAA.", ".BBBBBB.", ".BBBBBB.", "..BBBB..", "........" };
    // Body "ghost": the same slab with one half-alpha pixel at (3,3) — 2D only.
    // Lock "gold": a 2x2 solid at (3,3)-(4,4). All Custom.
    // NAMES: ingredient ids body/lock are named "Body"/"Lock"; variant ids oak/ghost/gold are named
    // "Oak"/"Ghost"/"Gold" — so export bases read "Body-Oak" and Set rarity values read "Oak".
    // Oak has 32 solid pixels (4 + 6 + 6 + 6 + 6 + 4).

    internal static Image<Rgba32> Picture(string[] rows) { /* A=(178,122,70,255) B=(94,59,34,255) G=(240,204,98,255) '~'=(200,40,40,128) */ }

    /// <summary>The book in memory: recipe "chest" = layers ["body", "lock"]; body variants oak (weight 3), ghost (1); lock variant gold.</summary>
    internal static LoadedCookBook ChestBook(DepthMap? oakDepth = null);

    /// <summary>Writes the book to a temp .cbk, reads it back (so SourceSha256 is set) and opens it in a session.</summary>
    internal static (CookBookSession Session, string Path) OnDisk(LoadedCookBook book)
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("nfty-rise-").FullName, "Chest.cbk");
        CookBookPersistence.WriteNew(path, book.Manifest, book.Recipes);
        book.Dispose();
        var session = new CookBookSession();
        session.Open(CookBookArchive.Read(path), path);
        return (session, path);
    }

    internal static VariantRiseSource Variant(CookBookSession s, string variant = "oak", bool editing = true)
    {
        var recipe = s.Current!.Recipes.Single(r => r.Manifest.Id == "chest");
        var body = recipe.Ingredients.Single(i => i.Manifest.Id == "body");
        return new VariantRiseSource(recipe, body, variant, new ImageBridge(), s, () => editing);
    }
}
```

- [ ] **Step 2: Failing `VariantRiseSourceTests`:**

```csharp
[AvaloniaFact]
public void Items_are_the_variants_with_stored_pixels_weights_and_chips()
{
    var (s, _) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
    using var src = RiseFixtures.Variant(s);
    Assert.Equal(new[] { "oak", "ghost" }, src.Items.Select(i => i.Id));
    Assert.Equal("weight 3 · 75%", src.Items[0].Caption);
    Assert.Equal("paper", src.Items[0].DepthChip);
    Assert.Equal("2D only", src.Items[1].DepthChip);
    Assert.True(src.Items[1].IsChipWarning);
    Assert.Equal("Body-Oak", src.Items[0].ExportBaseName);
    Assert.Equal("BODY · VARIANTS", src.Kicker);
    Assert.Single(src.PartialAlphaRows);
    Assert.Equal("1 px", src.PartialAlphaRows[0].CountText);
}

[AvaloniaFact]
public async Task Saving_writes_the_painted_depth_into_the_cookbook_on_disk()
{
    var (s, path) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
    using var src = RiseFixtures.Variant(s);
    LoadedCookBook? saved = null;
    src.Saved += b => saved = b;
    var oak = src.Open(src.Items[0]);
    oak.History.Do(new DepthStep(new[] { (2, 1) }, DepthSide.Front, +1), oak.Depth);
    Assert.True(src.Items[0].IsDirty);

    await src.SaveAsync();

    Assert.NotNull(saved);
    Assert.False(src.Items[0].IsDirty);
    using var reread = CookBookArchive.Read(path);
    var map = reread.Recipes[0].Ingredients.Single(i => i.Manifest.Id == "body").Depths["oak"];
    Assert.Equal(1, map.Front(2, 1));
    Assert.Equal(0, map.Front(3, 1));
}

[AvaloniaFact]
public async Task Saving_on_a_locked_book_writes_nothing_even_when_called_directly()
{
    var (s, path) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
    var before = File.ReadAllBytes(path);
    using var src = RiseFixtures.Variant(s, editing: false);
    var oak = src.Open(src.Items[0]);
    oak.History.Do(new DepthFillAll(2, 2), oak.Depth);
    Assert.True(src.IsLocked);

    await src.SaveAsync();

    Assert.Equal(before, File.ReadAllBytes(path));
    Assert.True(src.Items[0].IsDirty);
}

[AvaloniaFact]
public async Task Painting_back_to_paper_removes_the_stored_map()
{
    var oakDepth = DepthMap.FromBytes(8, 8, Enumerable.Repeat((byte)1, 128).ToArray());
    var (s, path) = RiseFixtures.OnDisk(RiseFixtures.ChestBook(oakDepth));
    using var src = RiseFixtures.Variant(s);
    var oak = src.Open(src.Items[0]);
    oak.History.Do(new DepthFillAll(0, 0), oak.Depth);
    await src.SaveAsync();
    using var reread = CookBookArchive.Read(path);
    Assert.False(reread.Recipes[0].Ingredients.Single(i => i.Manifest.Id == "body").Depths.ContainsKey("oak"));
}

[AvaloniaFact]
public async Task A_loose_ingredient_saves_to_its_own_igt()
{
    // Build a loose .igt on disk from the fixture's body ingredient, open it through a synthetic
    // one-recipe wrapper exactly as the loose-editor factory does, rise, save, re-read the .igt.
}
```
(Write the loose test fully: `IngredientArchive.Write(path, manifest, images, DepthMaps.None)`, then
`IngredientArchive.Read(path)`, wrap it in a `LoadedRecipe` + `LoadedCookBook` like
`ServiceRegistration`'s loose factory does, pass `looseSavePath: path`, `isEditing: null`.)

- [ ] **Step 3: Implement** the four files. `RiseItem.DepthChip`: `Subject is null → ""`;
  `PartialCount > 0 → "2D only"`; then by `Summary().Look`: Empty → `"empty"`, Paper → `"paper"`,
  Uniform → `$"{f} · {b}"`, Varied → `"varied"`. `ExportInputs` default: `Open(item)` each, yield
  `new ModelInput(name, s.Art, s.Depth.ToDepthMap())`. `Dispose` disposes items; an item disposes its
  subject and, when `ownsThumbnail`, its thumbnail.
- [ ] **Step 4: Run** `dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~VariantRiseSource` → green; full suite green (WiringCoverage/DeadCode unaffected: no VM, no markup yet). **Must stay green:** `CookBookPersistenceTests`, `IngredientEditorDepthTests`, `IngredientEditorSaveTests`, `EditorReadOnlyTests`, `SessionLifecycleTests`.
- [ ] **Step 5: Commit** `feat(app): rise source model and the variant source, saved through the cookbook`.

---

### Task 6: Recipe and Set sources

**Files:**
- Create: `src/Nfty.App/Rise/RecipeRiseSource.cs`, `src/Nfty.App/Rise/RiseLayerRow.cs`,
  `src/Nfty.App/Rise/SetRiseSource.cs`
- Test: `tests/Nfty.App.Tests/RecipeRiseSourceTests.cs`, `tests/Nfty.App.Tests/SetRiseSourceTests.cs`

**Interfaces — Consumes:** `Generator.GenerateStreaming`, `GenerateOptions(Count, Seed, RecipeId,
EnforceUniqueDna:)`, `GeneratedAsset.Traits/ColorRolls/AbsentLayers/Image`,
`StackDepth.ForSelection`, `PartialAlphaException`, `VariantPreview.Render(ing, vid, RolledColor)`,
`SetItemSelection.Resolve`, `SetItemRow(int, string, SetItem)` (thumbnail decode off-thread),
`CookBookLocator.Find/Nearby`, `CookBookArchive.Read`, `SealedSet`, `UnsupportedSchemaVersionException`.

**Produces:**
```csharp
public sealed class RiseLayerRow : IDisposable
{ public int Depth; public string Name, VariantName, KindMark, DepthText; public LayerKind Kind; public bool IsAbsent; public Bitmap? Thumbnail; }
public sealed class RecipeRiseSource : RiseSource
{
    public RecipeRiseSource(LoadedCookBook book, LoadedRecipe recipe, int seed, IImageBridge bridge);
    public int Seed { get; }
    public string? Problem { get; }                 // the book cannot cook: Generator's message
    public IReadOnlyList<RiseLayerRow> Layers { get; }
    public void Reroll();                           // Seed++, re-roll, replace the one item's subject, RaiseChanged()
}
public sealed class SetRiseSource : RiseSource
{
    public SetRiseSource(LoadedSet set, int selectedNumber, Func<IEnumerable<string>>? bookCandidates, IImageBridge bridge);
    public Task BookSearch { get; }                 // observable handle for the off-thread search (PendingReorder precedent)
    public string? BookName { get; }
    public bool IsSealed { get; }
}
```

**Recipe behaviour:** roll = `Generator.GenerateStreaming(book, new GenerateOptions(1,
Seed.ToString(CultureInfo.InvariantCulture), recipe.Manifest.Id, EnforceUniqueDna: false)).First()`
(ruling 3). Art = `asset.Image.Clone()`; dispose the asset. Present layers =
`asset.Traits.Select(t => (t.IngredientId, t.VariantId))`; depth = empty list → `null`, else
`StackDepth.ForSelection(recipe, present, $"the {recipe.Manifest.Name} recipe")`, catching
`PartialAlphaException` → `null` (the subject's own partial count already marks the item 2D only).
Any exception from the generator (invalid book) → `Problem = ex.Message`, art = a clear canvas-sized
image. One item: id `"roll"`, name `recipe.Name`, caption `$"seed {Seed}"`, export base
`$"{recipe.Name}-{Seed}"`. `Layers` = one row per `recipe.Manifest.LayerOrder` id (1-based, bottom
first): present → thumbnail `VariantPreview.Render(ing, vid, new RolledColor(h, s))` for a layer with
a colour roll (Custom: the clone), `DepthText` from a throwaway `DepthSurface(variantImage,
ing.Depths…)`.`Summary()` worded like the chip (`"depth 1 · 1"`, `"paper"`, `"varied"`); absent → row
`IsAbsent`, VariantName `"left out of this roll"`. `Kicker = $"{RECIPE} · LAYERS"`, `Facts =
[$"seed {Seed}", $"canvas {w} × {h}", "1 → paints first, furthest back"]`, `ReadOnlyPill =
"combined depth · read-only"`, `RailFootNote = "Layers are read-only here until write-through."`,
`DepthNote = "A recipe shows its stack's combined depth: each pixel takes the depth of the topmost
solid layer. Change a layer's depth on its own variant."`, `CanBatch = false`, `IsEditable = false`.
`Reroll` re-raises `Layers` too (old rows disposed).

**Set behaviour:** items in `set.Items` order (already by number); id `$"{n:D4}"`, name `$"#{n:D4}"`,
caption = recipe id, export base `$"{set.Manifest.Name}-{n:D4}"`; one `SetItemRow` per item; the item's
`Thumbnail` follows the row's (subscribe to `PropertyChanged`, read `row.Thumbnail` — which starts the
off-thread decode); `ownsThumbnail: false`; the source disposes the rows. `Open(item)`: decode
`Image.Load<Rgba32>(row.ImagePath)` (missing/unreadable → a 1×1 clear image: the item is "empty", never
a crash); depth = `DepthFor(item)` = book loaded ? `Resolve` → `ForSelection` (`null` on any miss or
`PartialAlphaException`) : `null`. `BookSearch` = started in the ctor exactly like
`SetBrowserViewModel.BeginSourceBookSearch`: candidates read on the calling (UI) thread,
`Task.Run(Find → CookBookArchive.Read)`, catching IOException / InvalidDataException /
UnauthorizedAccessException / UnsupportedSchemaVersionException as "not found"; the result is
**posted** to the UI thread, where every already-loaded subject is `Reseed(DepthFor(item))`, items
`RefreshDepth()`, `RaiseChanged()`. A disposed source drops a late result and disposes the book.
`IsSealed = set is SealedSet`; `ExportRefusal` when sealed: `"This Set is sealed: it can be risen and
looked at here, but nothing leaves it — the rule that disables Save image."`; `ReadOnlyPill = "cooked
Set · read-only"`; `CanBatch = true`; `BatchStem = $"{set.Manifest.Name}-models"`; `Facts =
[$"assets {count}", $"seed {set.Manifest.Seed}", $"recipes {distribution count}"]`; `DepthNote` by state:
searching `"Looking for the CookBook that cooked this Set…"`, found `$"Depth comes from {BookName}, the
book that cooked this Set."`, not found `"The CookBook that cooked this Set was not found, so every
asset is paper."`, plus — when the selected loaded asset could not be mapped — `" This asset's layers
could not be matched to that book by name, so it is paper."`. `ExportInputs` override: an unloaded
item is decoded for the yield and disposed after (never attached). The source does NOT dispose the
`LoadedSet` (borrowed from whoever opened it).

- [ ] **Step 1: Failing `RecipeRiseSourceTests`:**

```csharp
[AvaloniaFact]
public void The_roll_is_the_recipe_panes_hero_for_the_same_seed()
{
    using var book = RiseFixtures.ChestBook();
    var recipe = book.Recipes.Single(r => r.Manifest.Id == "chest");
    using var src = new RecipeRiseSource(book, recipe, seed: 7, new ImageBridge());
    using var hero = Generator.GenerateStreaming(book,
        new GenerateOptions(1, "7", "chest", EnforceUniqueDna: false)).First();
    var art = src.Open(src.Items[0]).Art;
    for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++) Assert.Equal(hero.Image[x, y], art[x, y]);
}

[AvaloniaFact]
public void Combined_depth_takes_the_topmost_solid_layer()
{
    // oak depth front 1 everywhere, gold depth front 4: (3,3) reads 4, (1,2) reads 1.
}

[AvaloniaFact]
public void Reroll_moves_the_seed_and_replaces_the_subject()
{
    // Seed 7 -> 8; Items[0].Subject is a new instance; Changed raised once.
}

[AvaloniaFact]
public void A_recipe_source_is_read_only_and_says_so()
{
    // IsEditable false, ReadOnlyPill "combined depth · read-only", CanBatch false, SaveAsync throws NotSupportedException.
}
```
(Write the elided bodies fully; for the topmost test pass `ChestBook(oakDepth)` and give gold a depth
map via an overload `ChestBook(DepthMap? oakDepth = null, DepthMap? goldDepth = null)`; use a seed for
which the roll picks `oak` — with oak weight 3 vs ghost 1, assert on `Layers` which variant was rolled
and pick the seed in the test by searching 1..50 for one whose roll is oak.)

- [ ] **Step 2: Failing `SetRiseSourceTests`:**

```csharp
[AvaloniaFact]
public async Task A_found_book_gives_each_asset_its_combined_depth()
{
    var oakDepth = DepthMap.FromBytes(8, 8, Enumerable.Repeat((byte)2, 128).ToArray());
    var (s, bookPath) = RiseFixtures.OnDisk(RiseFixtures.ChestBook(oakDepth));
    var setDir = Path.Combine(Path.GetDirectoryName(bookPath)!, "Chest Set");
    using (var cooked = Generator.Generate(s.Current!, new GenerateOptions(3, "launch")))
        SetWriter.Write(cooked, setDir, pack: false);
    using var set = SetReader.Read(setDir);
    using var src = new SetRiseSource(set, set.Items[0].Number, () => Array.Empty<string>(), new ImageBridge());

    await src.BookSearch;
    Dispatcher.UIThread.RunJobs();

    Assert.Equal("Chest.cbk", src.BookName);
    var subject = src.Open(src.Items.First(i => /* an asset whose body is oak */ true));
    Assert.False(subject.Depth.SameDepthAs(null));
}

[AvaloniaFact]
public async Task No_book_means_paper_and_the_note_says_so() { /* set folder with no .cbk nearby */ }

[AvaloniaFact]
public void A_sealed_set_is_viewable_and_refuses_export()
{
    // Build a SealedSet exactly as SealedSetGatingTests.OpenSealed does; IsSealed, ExportRefusal not null,
    // Open(Items[0]).Art has the asset's pixels.
}

[AvaloniaFact]
public void Unloaded_assets_are_decoded_for_export_and_not_kept()
{
    // ExportInputs over two unloaded items yields two inputs; afterwards both items' Subject are still null.
}
```
(Fill in the bodies; pick the oak asset as the item whose `Rarity` row `Body` has value `"Oak"` —
the fixture's variant NAME.)

**Must stay green:** `SetBrowserViewModelTests`, `ExactCombinedChanceTests`, `SealedSetGatingTests`,
`RecipeDetailViewModelTests` (the hero path this source mirrors), `SessionLifecycleTests` (leak counter:
every decoded image the sources open is disposed with them).

- [ ] **Step 3: Implement**, run `--filter "FullyQualifiedName~RiseSource"` then the full App suite.
- [ ] **Step 4: Commit** `feat(app): recipe and set rise sources — the hero's roll, depth recovered by hash and name`.

---

### Task 7: `RiseViewModel` core

**Files:**
- Create: `src/Nfty.App/ViewModels/RiseViewModel.cs` (core partial),
  `src/Nfty.App/ViewModels/RiseViewModel.Canvas.cs` (zoom/pan/grid — mirrors
  `IngredientEditorViewModel.Zoom.cs` on `CanvasMapping` constants)
- Test: `tests/Nfty.App.Tests/RiseViewModelTests.cs`

**Interfaces — Consumes:** Tasks 1–6; `StrokeConstraint` is the VIEW's (Task 11); `Riser.TryBuild`,
`RiseOptions`, `Mesh`, `ModelPreviewViewModel`, `IImageBridge`, `Perf.Measure`, `DieFaceConverter.Faces`.

**Produces** (`Nfty.App.ViewModels.RiseViewModel : ViewModelBase, IDisposable`):
```csharp
public RiseViewModel(RiseSource source, IImageBridge bridge, INavigationService nav, IDialogService dialogs,
    IFilePickerService picker, IStatusService status);
public RiseSource Source { get; }
public IReadOnlyList<RiseItem> Items { get; }                 // Source.Items
[ObservableProperty] RiseItem SelectedItem;                   // change: cancel preview, Selection=null, Subject=Source.Open, rebuild canvas+mesh, raise SubjectChanged
[RelayCommand] void SelectItem(RiseItem item);
public RiseSubject Subject { get; }
public DepthSurface Surface => Subject.Depth;                 // what the label overlay reads
[ObservableProperty] Bitmap Canvas;                           // bridge.ToBitmap(Subject.Art), rebuilt on item change only
[ObservableProperty] Mesh? Mesh;                              // Riser over Subject (null when not risable)
public Image<Rgba32> Texture => Subject.Art;
public ModelPreviewViewModel Preview { get; }
public bool IsPaintable => Source.IsEditable && Subject.IsRisable;   // a locked variant still paints (scratch)
public bool IsVariantRail / IsRecipeRail / IsTileRail { get; } // by Source.Kind (Set → tile; default branch → tile)
public bool ShowReadOnlyPill => Source.ReadOnlyPill is not null; public string? ReadOnlyPill => Source.ReadOnlyPill;
// tools — reuse EditorTool; only Brush, Line, Rectangle, Fill, Select are offered
[ObservableProperty] EditorTool ActiveTool = EditorTool.Brush;  // setting any other tool is ignored (reverted)
public bool IsToolBrush/IsToolLine/IsToolRectangle/IsToolFill/IsToolSelect { get; }
[RelayCommand] void SelectTool(EditorTool tool);
[ObservableProperty] int BrushSize = 1;  public int BrushSizeMax; [RelayCommand] GrowBrush; ShrinkBrush;
[ObservableProperty] PixelRect? Selection; public bool HasSelection; [RelayCommand] ClearSelection; SelectAll;
internal bool SelectionContains(int x, int y);
// gestures (points are canvas pixels AFTER the view's constraint transform)
public void PreviewDepthStroke(IReadOnlyList<(int x, int y)> points, DepthSide side, bool lower);
public void ApplyDepthStroke(IReadOnlyList<(int x, int y)> points, DepthSide side, bool lower);
public void CancelDepthPreview();
[RelayCommand(CanExecute=nameof(CanUndo))] void Undo(); [RelayCommand(CanExecute=nameof(CanRedo))] void Redo();
// readout
[ObservableProperty] (int X, int Y)? HoverPixel;
public string ReadoutText { get; }                            // "front 1 · back 0 · total 2" or "" (reserved line)
public string DimensionsText { get; }                         // "8 × 8 · 37 solid"
// tile
[ObservableProperty] bool PreviewSwapped; [ObservableProperty] bool PreviewEnlarged;
public double TileSize => PreviewEnlarged ? 208 : 120;
public bool ShowPaintCanvas => !PreviewSwapped;
public string TileCaption => PreviewSwapped ? "CANVAS" : "3D";
public string SwapTip / EnlargeTip { get; }
[RelayCommand] void SwapPreview(); [RelayCommand] void EnlargePreview();
public bool ShowTwoDOnly => Subject.Depth.PartialCount > 0;   // the tile's "2D only" ink
// recipe
[ObservableProperty] int DieFace = 4; public bool HasDie => Source is RecipeRiseSource;
[RelayCommand(CanExecute=nameof(HasDie))] void Reroll();     // die: Random.Shared 1..5 stepping over the current face
// events
public event Action? DepthChanged;      // the label overlay invalidates
public event Action? SubjectChanged;    // the view re-frames the camera with the real aspect
public event Action? GestureEnded;      // the view clears ModelPreviewView.HostInteracting (exact repaint)
public event Action? BackdropChanged;   // canvas view moved (zoom/pan/grid) — same contract as the editor
internal const int MeshBudgetMs = 33;
```

**Gesture semantics** (the region a stroke touches, `w×h` = art size):
Brush → `Footprint.Discs(w, h, points, BrushSize)`; Line → `Footprint.Discs(w, h, [points[0],
points[^1]], BrushSize)`; Rectangle → `Footprint.Box(BoundsOf(points), w, h)` (inclusive, the editor's
`BoundsOf`); Fill → `Surface.SameColorRegion(points[0])`, **commit-only** (no preview, as the editor);
Select → if `Selection` contains `points[0]`, `Footprint.Box(Selection, w, h)` (the "click inside
applies its ±1 to every solid pixel in it, once"), else it MARKS: no depth, and on release `Selection =
start == end ? null : BoundsOf(points)`. `delta = lower ? -1 : +1`.
**Standing preview (ruling 5):** `PreviewDepthStroke` undoes the standing command, builds `new
DepthStep(region, side, delta)`, `Apply`s it and keeps it standing; raises `DepthChanged`; rebuilds
the mesh only if `MeshBudgetMs` has elapsed since the last rebuild (canvas labels never skipped).
`ApplyDepthStroke`: undo the standing command, then (inside `Perf.Measure("Rise.DepthClick")`)
`Subject.History.Do(new DepthStep(...), Surface)`; ALWAYS rebuild the mesh; `AfterDepthEdit()`; raise
`GestureEnded`. `CancelDepthPreview`: undo standing, rebuild, `GestureEnded`. Not paintable → every
gesture entry point returns at once (Select marking still works — marking changes no depth).
`RebuildMesh` (inside `Perf.Measure("Rise.MeshRebuild")`): `Subject.IsRisable ? Riser.TryBuild(Subject.Art,
Surface.ToDepthMap(), new RiseOptions(), Source.Title) : null`. `AfterDepthEdit`: `SelectedItem.RefreshDepth()`,
raise `DepthChanged`, notify `ReadoutText`, Undo/Redo `CanExecute` (Task 8 adds its own notifications
through a partial method `partial void OnDepthEdited();` declared here, implemented in Task 8/9 partials).
Undo/Redo/item change/Dispose call `CancelDepthPreview()` first.

- [ ] **Step 1: Failing `RiseViewModelTests`** (pure VM; window not needed except `[AvaloniaFact]` for
  bitmaps):

```csharp
private static RiseViewModel Vm(RiseSource src) =>
    new(src, new ImageBridge(), new FakeNav(), new FakeDialogs(), new FilePickerService(), new StatusService());

[AvaloniaFact]
public void A_click_raises_the_front_by_one_and_is_one_undo_step()
{
    var (s, _) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
    using var vm = Vm(RiseFixtures.Variant(s));
    vm.ApplyDepthStroke(new[] { (2, 1) }, DepthSide.Front, lower: false);
    Assert.Equal(new DepthPixel(1, 0), vm.Surface.Get(2, 1));
    Assert.True(vm.UndoCommand.CanExecute(null));
    vm.UndoCommand.Execute(null);
    Assert.Equal(new DepthPixel(0, 0), vm.Surface.Get(2, 1));
}

[AvaloniaFact]
public void A_long_stroke_raises_every_pixel_it_crosses_exactly_once()
{
    var (s, _) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
    using var vm = Vm(RiseFixtures.Variant(s));
    var path = new[] { (1, 2), (6, 2), (1, 2), (6, 2) };
    for (int i = 1; i <= path.Length; i++) vm.PreviewDepthStroke(path[..i], DepthSide.Back, false);
    vm.ApplyDepthStroke(path, DepthSide.Back, false);
    Assert.All(Enumerable.Range(1, 6), x => Assert.Equal(new DepthPixel(0, 1), vm.Surface.Get(x, 2)));
}

[AvaloniaFact]
public void Shift_lowers_and_floors_at_zero()
{
    // FillFront=2 via DepthFillAll on Subject; two lowering clicks then a third -> 0, third is a no-op (no extra undo step).
}

[AvaloniaFact]
public void A_cancelled_gesture_leaves_the_depth_exactly_as_it_was()
{
    var (s, _) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
    using var vm = Vm(RiseFixtures.Variant(s));
    vm.PreviewDepthStroke(new[] { (2, 2), (5, 2) }, DepthSide.Front, false);
    Assert.Equal(1, vm.Surface.Get(3, 2).Front);          // the standing preview IS on the surface
    vm.CancelDepthPreview();
    Assert.Null(vm.Surface.ToDepthMap());
    Assert.False(vm.UndoCommand.CanExecute(null));
}

[AvaloniaFact]
public void Select_marks_then_a_click_inside_applies_to_every_solid_pixel_once()
{
    // ActiveTool=Select; Apply [(1,1),(3,3)] marks Selection (1,1,3,3); Apply [(2,2)] Front +1:
    // the 8 solid pixels of rows 1-3, cols 1-3 read front 1, (1,1) (clear) reads 0; one undo step.
}

[AvaloniaFact]
public void Same_colour_fill_raises_one_region()
{
    // ActiveTool=Fill; Apply [(3,2)] -> every 'A' pixel front 1, every 'B' pixel front 0.
}

[AvaloniaFact]
public void Every_depth_change_rebuilds_the_mesh_and_the_thickness_reaches_it()
{
    var (s, _) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
    using var vm = Vm(RiseFixtures.Variant(s));
    var flatDepth = vm.Mesh!.Max.Z - vm.Mesh.Min.Z;
    vm.ApplyDepthStroke(new[] { (2, 2) }, DepthSide.Front, false);
    Assert.True(vm.Mesh!.Max.Z - vm.Mesh.Min.Z > flatDepth);
}

[AvaloniaFact]
public void A_two_d_only_item_cannot_be_painted_and_has_no_mesh()
{
    var (s, _) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
    using var vm = Vm(RiseFixtures.Variant(s, "ghost"));
    Assert.False(vm.IsPaintable);
    Assert.Null(vm.Mesh);
    Assert.True(vm.ShowTwoDOnly);
    vm.ApplyDepthStroke(new[] { (2, 2) }, DepthSide.Front, false);
    Assert.False(vm.UndoCommand.CanExecute(null));
}

[AvaloniaFact]
public void A_locked_variant_still_paints_as_a_scratch_surface()
{
    var (s, _) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
    using var vm = Vm(RiseFixtures.Variant(s, editing: false));
    Assert.True(vm.IsPaintable);
}

[AvaloniaFact]
public void A_recipe_source_ignores_strokes_and_offers_the_die()
{
    // RecipeRiseSource; ApplyDepthStroke changes nothing; HasDie; Reroll changes DieFace (never to the same face) and Subject.
}

[AvaloniaFact]
public void The_readout_names_the_pixel_under_the_pointer_and_is_blank_elsewhere()
{
    // HoverPixel=(2,1) after one front click -> "front 1 · back 0 · total 2"; HoverPixel=(0,0) (clear) -> ""; null -> "".
    // DimensionsText == "8 × 8 · 32 solid".
}

[AvaloniaFact]
public void Changing_items_cancels_the_preview_drops_the_marquee_and_reframes()
{
    // PreviewDepthStroke on oak, Selection set, subscribe SubjectChanged; SelectItemCommand(ghost) ->
    // oak surface unchanged (no standing preview), Selection null, SubjectChanged raised once.
}

[AvaloniaFact]
public void Swapping_keeps_both_halves_on_screen_and_names_the_other()
{
    // TileCaption "3D" -> SwapPreview -> "CANVAS", ShowPaintCanvas false; EnlargePreview: TileSize 120 -> 208 -> 120.
}

[AvaloniaFact]
public void Only_the_five_rise_tools_can_be_armed()
{
    // SelectTool(Eraser) leaves ActiveTool at Brush; SelectTool(Line) arms Line; leaving Select drops the marquee.
}
```
(Write every elided body fully, asserting exact pixels from the fixture — `Oak` rows above.)

- [ ] **Step 2: Implement** both partials. View-state members (`Zoom`, `PanX/PanY`, `IsZoomed`,
  `ZoomText`, `ZoomTip`, `ZoomToFit/ZoomIn/ZoomOut`, `PanBy`, `GridSize`, `GridSizeMax`, `GridSizeText`,
  `ShowPixelGrid`, `GridToggleTip`, `ToggleGrid`) follow `IngredientEditorViewModel.Zoom.cs` and the
  editor's grid members exactly (same clamps at the property, same NaN-first guard, same strings), on
  `CanvasMapping` constants, raising `BackdropChanged`; none marks anything dirty. `Preview` is built
  from the first mesh's bounds or `±Vector3.One` when there is no mesh. `Dispose`: cancel preview,
  dispose `Canvas`, `Source.Dispose()`.
- [ ] **Step 3: Run** `--filter FullyQualifiedName~RiseViewModel` → green; full App suite.
  **WiringCoverageTests will FAIL** for commands no markup binds yet — add each new command name to
  `WiringCoverageTests.Exempt` with reason `"Rise page markup lands in Task 10"`; Task 10 removes them
  (the exemption-staleness test forces it).
- [ ] **Step 4: Commit** `feat(app): rise page view model — gestures, history, live mesh, tile`.

**Must stay green:** `WiringCoverageTests` (with the temporary exemptions), `DeadCodeAuditTests`
(`No_command_has_an_empty_body`), `ModelPreviewViewModelTests`, `StrokeConstraintTests`.

---

### Task 8: DEPTH rail, footer, Save / Back, leave dialog, registration, opening line

**Files:**
- Create: `src/Nfty.App/ViewModels/RiseViewModel.Depth.cs`, `src/Nfty.App/ViewModels/RiseViewModel.Save.cs`
- Create: `src/Nfty.App/ViewModels/LeaveDialogViewModel.cs`, `src/Nfty.App/Views/LeaveDialogView.axaml(.cs)`
- Modify: `src/Nfty.App/ServiceRegistration.cs`, `src/Nfty.App/ViewModels/ShellViewModel.cs`
- Test: `tests/Nfty.App.Tests/RiseDepthRailTests.cs`, `tests/Nfty.App.Tests/RiseSaveTests.cs`,
  `tests/Nfty.App.Tests/ServiceRegistrationTests.cs` (one test added)

**Produces:**
```csharp
// RiseViewModel.Depth.cs
[ObservableProperty] bool IsDepthTab = true;  public bool IsExportTab => !IsDepthTab;
[RelayCommand] void ShowDepthTab(); [RelayCommand] void ShowExportTab();
[ObservableProperty] int FillFront = 1; [ObservableProperty] int FillBack = 1;    // clamped 0..255 at the property
[RelayCommand(CanExecute=nameof(IsPaintable))] void FillAll();                    // History.Do(new DepthFillAll(...)); AfterDepthEdit
public string FillAllLabel { get; }        // $"Apply to {solid:N0} solid pixels" (invariant; "1 solid pixel")
public string SolidHeader { get; }         // $"{solid:N0} solid px"
public IReadOnlyList<ThicknessRow> Thickness { get; }   // record ThicknessRow(string Label, int Count, string CountText, double Share)
public string DeepestText { get; }         // $"deepest {max}" or "—" when nothing is solid
public IReadOnlyList<PartialAlphaRow> PartialAlphaRows => Source.PartialAlphaRows;
public bool HasPartialAlphaRows { get; }
public string PartialAlphaCountText { get; }   // "1 item" / "2 items"
public string DepthNote { get; }           // Source.DepthNote, or for a 2D-only subject: "This item has partial alpha, so it stays 2D: a voxel is solid or empty."
public bool BookIsTwoDOnly => Source.BookIsTwoDOnly;
// RiseViewModel.Save.cs
public bool HasUnsavedDepth { get; }       // Items.Any(i => i.IsDirty)
public string FooterText { get; }          // see table below
public bool ShowSave => Source.IsEditable; // inked; space reserved either way (Opacity/IsHitTestVisible in the view)
public bool CanSave => Source.IsEditable && !Source.IsLocked && HasUnsavedDepth && !IsSaving;
[ObservableProperty] bool IsSaving;
[RelayCommand(CanExecute=nameof(CanSave))] Task Save();     // re-checks CanSave-minus-IsSaving INSIDE; cancels preview; awaits Source.SaveAsync(); errors -> ErrorDialogViewModel("Could not save depth", message)
[RelayCommand] Task Back();
public event Action<LoadedCookBook>? Saved;                 // forwarded from VariantRiseSource.Saved
internal void SayOpeningLine();                             // _status.Say(Source.OpeningLine)
// LeaveDialogViewModel
public enum LeaveChoice { Stay, Discard, SaveAndLeave }    // Stay = 0: a dismissed card keeps editing
public partial class LeaveDialogViewModel : ViewModelBase
{
    public LeaveDialogViewModel(IDialogService dialogs, string title, string message, bool canSave);
    public string Title, Message { get; } public bool CanSave { get; }
    [RelayCommand] void Discard(); [RelayCommand(CanExecute=nameof(CanSave))] void SaveAndLeave(); [RelayCommand] void Stay();
}
```

**Footer text:** read-only source → `"read-only source — nothing to save"`; editable, nothing dirty →
`"no unsaved depth"`; dirty → `"unsaved depth on {n} variant(s)"`; dirty AND locked → append
`" · the CookBook is read-only — unlock it in the Explorer to save"`. (The view draws the accent dot
before the text when dirty — reserved, ink toggled.)

**Back:** `CancelDepthPreview()`. If `!Source.IsEditable || !HasUnsavedDepth` → `_nav.Back()`. Else
`ShowAsync<LeaveChoice>(new LeaveDialogViewModel(dialogs, "Leave Rise?", $"{Source.Title} has unsaved
depth on {n} variant(s). Leaving discards it.", canSave: !Source.IsLocked))`: Stay → return; Discard →
`_nav.Back()`; SaveAndLeave → `await Save`, then `_nav.Back()` only if `!HasUnsavedDepth`.

**Opening lines** (`RiseSource.OpeningLine`, said by the shell after the page arrives):

| Source | Line |
|---|---|
| Variant, editing | `Rising “Body / Oak” — left raises the front, right the back, Shift lowers. Save depth writes it into the CookBook.` |
| Variant, locked | `“Body” is read-only — try depth freely, but unlock the CookBook to save it.` |
| Recipe | `Rolled with seed 7 — reroll from the toolstrip. Depth is combined from the layers and read-only here.` |
| Set | `Viewing #0007 of a cooked Set. Read-only.` (+ ` Sealed: it can be viewed here, not exported.`) |

(Use `KeyHints` for any key name printed: `KeyHints.Constrain` is "Shift"/"⇧".)

**Registration:** `services.AddSingleton<Func<RiseSource, RiseViewModel>>(sp => source => new
RiseViewModel(source, sp.GetRequiredService<IImageBridge>(), sp.GetRequiredService<INavigationService>(),
sp.GetRequiredService<IDialogService>(), sp.GetRequiredService<IFilePickerService>(),
sp.GetRequiredService<IStatusService>()))` — the factory step 8's buttons call. **Shell:** in
`OnCurrentPageChanged`, beside `(value as ExplorerViewModel)?.SayLockState();`, add
`(value as RiseViewModel)?.SayOpeningLine();`.

**LeaveDialogView:** a copy of `ConfirmDialogView.axaml`'s structure — `Border.modal.small`, kicker
`CONFIRM` with `Ellipse.kdot`, `TextBlock.m-h` title, `TextBlock.m-sub` message, `Border.modal-foot`
with `TextBlock.keyhint` "Esc to keep editing", `Button.tbtn` "Discard changes", `Button.accent` "Save
and leave" (`IsEnabled` via command). `UserControl.KeyBindings` Escape → `StayCommand`.

- [ ] **Step 1: Failing tests** — `RiseDepthRailTests` (fill-all applies to every solid pixel, one
  undo step, disabled for recipe/2D-only; FillFront clamps 300 → 255 and −1 → 0; histogram rows after
  a fill of 1·1 = one row `("3 voxels", 32)` with `Share 1.0`; `PartialAlphaRows` on the variant source lists
  ghost; `DepthNote` switches for the ghost item) and `RiseSaveTests`:

```csharp
[AvaloniaFact]
public async Task Back_with_unsaved_depth_asks_and_Stay_keeps_the_page()
{
    var (s, _) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
    var nav = new FakeNav();
    var dialogs = new ScriptedDialogs(LeaveChoice.Stay);
    using var vm = new RiseViewModel(RiseFixtures.Variant(s), new ImageBridge(), nav, dialogs,
        new FilePickerService(), new StatusService());
    vm.ApplyDepthStroke(new[] { (2, 1) }, DepthSide.Front, false);
    await vm.BackCommand.ExecuteAsync(null);
    Assert.IsType<LeaveDialogViewModel>(dialogs.Shown);
    Assert.Equal(0, nav.BackCount);
}

[AvaloniaFact]
public async Task Discard_leaves_and_Save_and_leave_writes_then_leaves()
{
    // Discard: nav.BackCount 1, file bytes unchanged. SaveAndLeave: file re-read carries the depth, BackCount 1.
}

[AvaloniaFact]
public async Task On_a_locked_book_Save_is_disabled_and_executing_it_writes_nothing()
{
    var (s, path) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
    var before = File.ReadAllBytes(path);
    using var vm = new RiseViewModel(RiseFixtures.Variant(s, editing: false), new ImageBridge(), new FakeNav(),
        new FakeDialogs(), new FilePickerService(), new StatusService());
    vm.ApplyDepthStroke(new[] { (2, 1) }, DepthSide.Front, false);
    Assert.False(vm.SaveCommand.CanExecute(null));
    await vm.SaveCommand.ExecuteAsync(null);          // the generated command does not consult CanExecute
    Assert.Equal(before, File.ReadAllBytes(path));
    Assert.Contains("unlock it in the Explorer", vm.FooterText);
}

[AvaloniaFact]
public void Undoing_back_to_the_saved_depth_is_clean()
{
    // click, undo -> HasUnsavedDepth false, FooterText "no unsaved depth".
}

[AvaloniaFact]
public void A_read_only_source_leaves_without_asking_and_offers_no_save() { /* recipe source */ }

[AvaloniaFact]
public void Arriving_on_the_page_says_its_opening_line()
{
    var nav = new NavigationService();
    var status = new StatusService();
    var shell = new ShellViewModel(nav, new FakeDialogs(), new ThemeService(), status);
    var (s, _) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
    nav.To(new RiseViewModel(RiseFixtures.Variant(s), new ImageBridge(), nav, new FakeDialogs(),
        new FilePickerService(), status));
    Assert.StartsWith("Rising “Body / Oak”", status.Last);
}
```
`ScriptedDialogs` (test-local): records `Shown`, returns the scripted `LeaveChoice` for
`ShowAsync<LeaveChoice>`, `default` otherwise (the `IngredientEditorSaveTests.FakeConfirmingDialogs`
shape). Add to `ServiceRegistrationTests`: the `Func<RiseSource, RiseViewModel>` resolves and builds a
page from a variant source.

- [ ] **Step 2: Implement**; the `partial void OnDepthEdited()` from Task 7 now notifies `Thickness`,
  `DeepestText`, `FillAllLabel`, `SolidHeader`, `HasUnsavedDepth`, `FooterText`, `SaveCommand`.
- [ ] **Step 3: Run** the three filters + full suite. Add new commands to `WiringCoverageTests.Exempt`
  ("markup lands in Task 10") except LeaveDialog's, which its own view binds now.
- [ ] **Step 4: Capture** `LeaveDialogView` both themes (`VisualCapture` frames it at the modal area
  automatically via `.modal`); compare against `zz-dialog-*` confirm frames; fix divergence.
- [ ] **Step 5: Commit** `feat(app): rise depth rail, save and leave, registration and opening line`.

**Must stay green:** `ShellViewModelTests` (status cleared on page change, Explorer's lock line),
`ServiceRegistrationTests`, `ModalFitTests` (the leave card is a modal: it must fit 1067 × 526),
`DarkModeContrastTests` (unchanged list), `ViewLocatorTests`.

---

### Task 9: EXPORT rail

**Files:**
- Create: `src/Nfty.App/ViewModels/RiseViewModel.Export.cs`
- Test: `tests/Nfty.App.Tests/RiseExportTests.cs`

**Produces:**
```csharp
public sealed partial class FormatCard : ObservableObject
{ public ModelFormat Format; public string Title, Summary; [ObservableProperty] bool IsSelected; }
public IReadOnlyList<FormatCard> FormatCards { get; }      // GLB / glTF + bin / glTF embedded (mockup copy)
[ObservableProperty] ModelFormat Format = ModelFormat.Glb; [RelayCommand] void PickFormat(FormatCard card);
[ObservableProperty] Pivot Pivot = Pivot.BottomCenter;
public bool IsPivotBottomCenter { get; set; } IsPivotCenter { get; set; } IsPivotMinCorner { get; set; }   // RadioButton two-way
[ObservableProperty] double VoxelSize = 1;                 // finite, clamped to [0.01, 1000] at the property; rounded to 0.01
public string ModelDimensionsText { get; }                 // "model 6 × 6 × 1" (mesh extent × voxel size, "0.##")
[ObservableProperty] bool Packed = true;                   // single file .zip vs folder
public bool IsBatch => ExportItems.Count > 1;              // Shape section inked only then
public IReadOnlyList<RiseItem> ExportItems { get; }        // CanBatch ? checked IsRisableOrUnknown items : [SelectedItem] when risable
public int ExportCount; public string ExportBadge;
public string CheckedSummary { get; }                      // "2 of 3 checked for export"
public string CheckAllLabel { get; }                       // "Check all" / "Clear"
[RelayCommand] void ToggleCheckAll();
public string ManifestTitle { get; }                       // single "Body-oak.glb"; batch packed "Body-models.zip"; folder "Body-models/"; none "Nothing to export"
public IReadOnlyList<string> ManifestParts { get; }       // see below
public string ManifestSize { get; }                        // single: exact ("12.4 KB", invariant); batch: ""
public string? ExportProblem { get; }                      // refusal / nothing / 2D only — said in words
public string ExportLabel { get; }                         // "Export model…" / "Export 3 models…"
[RelayCommand(CanExecute=nameof(CanExport))] Task Export();
public RiseOptions ExportOptions => new() { Format = Format, Pivot = Pivot, VoxelSize = (float)VoxelSize };
```
**Names come from `ModelBatch`**: base names = `ModelBatch.UniqueBaseNames(ExportItems.Select(i =>
i.ExportBaseName))`; manifest file names = `ModelBatch.FileNamesFor(base, Format)`; single parts:
GltfWithBin → the two file names, GLB → `["texture inside", $"pivot {words}"]`, embedded → `["buffer
inside", $"pivot {words}"]` (words: "bottom center" / "center" / "min corner"); batch parts:
`[$"{n} models", $"{files} files", first three file names joined ", " + (n > 3 ? ", …" : "")]`.
**Size (ruling 8):** recomputed (`ModelBatch.Build` of the one input, sum of bytes) only while
`IsExportTab`, on tab entry, option change and `OnDepthEdited`; batch → `""`.
**Export:** re-check `CanExport` INSIDE (sealed refusal first — the picker must never open for a
sealed Set). Single: `_picker.SaveFileAsync($"Export {ManifestTitle}", Format == Glb ? ".glb" :
".gltf")`; base = the chosen file's stem; `ModelBatch.Build` → `WriteFolder(chosenDir, files)`; status
`$"Exported {names} to {dir}."`. Batch: `BusyViewModel.RunAsync(dialogs, "Exporting models", $"Rising
{n} models…", (busy, ct) => Task.Run(() => ModelBatch.Build(Source.ExportInputs(items, bases),
ExportOptions)))`; packed → `SaveFileAsync(..., ".zip")` → `WriteZip`; folder → `PickFolderAsync` →
`WriteFolder(Path.Combine(picked, ModelBatch.SafeBaseName(Source.BatchStem)), files)`; status lists
skipped names (`"skipped #0004 (partial alpha)"`). Errors → `ErrorDialogViewModel("Could not
export", message)`. `CanExport` = `Source.ExportRefusal is null && ExportCount > 0 && !IsExporting`.

- [ ] **Step 1: Failing `RiseExportTests`:** sealed Set → `ExportCommand.CanExecute` false,
  `ExportProblem` is the refusal, executing writes nothing and **never calls the picker** (a scripted
  picker that throws if called); single glTF+bin → files written under the picked stem with both names;
  manifest names equal written names for a 3-item variant batch in folder shape (read the directory);
  packed batch writes a zip whose entries equal the manifest's listing; a batch with the ghost checked
  writes the others and the status names ghost as skipped; Shape section ink follows `IsBatch`; Voxel
  size clamps (0 → 0.01, NaN → 1, 1.234 → 1.23); `ModelDimensionsText` for Oak at voxel 2 =
  `"model 12 × 12 × 2"`.
  ```csharp
  private sealed class ScriptedPicker(string? save = null, string? folder = null, bool throwIfAsked = false) : IFilePickerService
  {
      public Task<string?> OpenFileAsync(string title, params string[] extensions) => throw new InvalidOperationException();
      public Task<string?> SaveFileAsync(string title, string defaultExtension) =>
          throwIfAsked ? throw new InvalidOperationException("asked") : Task.FromResult(save);
      public Task<string?> PickFolderAsync(string title) =>
          throwIfAsked ? throw new InvalidOperationException("asked") : Task.FromResult(folder);
  }
  ```
- [ ] **Step 2: Implement**, run `--filter FullyQualifiedName~RiseExport`, full suite; exempt new
  commands as in Task 7.
- [ ] **Step 3: Commit** `feat(app): rise export — formats, pivot, voxel size, batch shape, live manifest`.

**Must stay green:** `SealedSetGatingTests`, `ExportDialogTests`, `ModelBatchTests` (Task 2).
**Captures:** none yet (no markup) — Task 10 photographs the EXPORT tab.

---

### Task 10: `RiseView` — the page

**Files:**
- Create: `src/Nfty.App/Views/RiseView.axaml`, `src/Nfty.App/Views/RiseView.axaml.cs`
- Create: `src/Nfty.App/Controls/DepthLabelLayer.cs`
- Modify: `src/Nfty.App/Themes/Styles.axaml` (Rise-only classes, each used), `Themes/Tokens.axaml`
  (`CellLabelOnLightBrush`, `CellLabelOnDarkBrush` in BOTH dictionaries — same values both themes:
  `#B30A0A0C` and `#D1FFFFFF`, commented as art-relative not theme-relative ink),
  `Services/KeyHints.cs` (Rise hints), `tests/Nfty.App.Tests/VisualCapture.cs` (`Capture_rise_page`),
  `tests/Nfty.App.Tests/WiringCoverageTests.cs` (drop the Task 7–9 exemptions)
- Test: `tests/Nfty.App.Tests/RiseViewLayoutTests.cs`

**Layout contract** (`UserControl` root: `RenderOptions.BitmapInterpolationMode="None"`,
`Focusable="True" IsTabStop="False"`; must construct and lay out with NO DataContext —
`PixelPerfectRenderingTests` does exactly that):
```
Grid RowDefinitions="*,Auto"
 ├ Grid ColumnDefinitions="262,*,300"
 │  ├ Border.pane — SOURCE: pane-hrow [TextBlock.pane-h Kicker | TextBlock.hcount Items.Count]
 │  │    subhead: WrapPanel of Border.idchip Facts (+ warning "2D only" chip w/ icon when BookIsTwoDOnly)
 │  │    ScrollViewer list — three templates, exactly one IsVisible by IsVariantRail/IsRecipeRail/IsTileRail
 │  │      variant: Button.vcard rows (Classes.sel=IsSelected) = [checkbox 15 | thumb 40 | name+caption | depth chip]
 │  │      recipe : inert rows = [depth no. 18 | thumb 32 | name + "variant · depth text" | kind mark]
 │  │      tile   : 3-column tiles (thumb, mono caption, checkbox top-left, "2D" flag top-right reserved)
 │  │    rail foot (fixed 47, top hairline): CheckedSummary + CheckAll button  |  RailFootNote with lock icon
 │  ├ Border.pane — CANVAS: pane-hrow.toolstrip StripPanel groups:
 │  │      [tools: brush line rect fill select] [brush size .nin 46] [undo redo] [die, HasDie] [legend]
 │  │    Panel x:Name="CanvasBackdrop" (star row)
 │  │      Border CanvasHost 320×320 → Panel x:Name="CanvasSurface" Background=Transparent
 │  │           Image x:Name="CanvasImage" Source=Canvas Stretch=Uniform (IsVisible ShowPaintCanvas)
 │  │           ctl:DepthLabelLayer x:Name="DepthLabels" IsHitTestVisible=False
 │  │           Canvas x:Name="CanvasOverlay" IsHitTestVisible=False (marquee)
 │  │      Border x:Name="BigHost" (fills the backdrop; the 3D view lives here when swapped)
 │  │      read-only pill: top-centre Border.idchip-shaped, lock icon + ReadOnlyPill (IsVisible = ShowReadOnlyPill; fixed per page)
 │  │      Border.gridchip bottom-left: grid toggle · step .nin 58 · tsep · zoom chip (the editor's, verbatim)
 │  │      Border x:Name="ModelTile" bottom-right, Width/Height = TileSize, margin 0,0,26,26:
 │  │           Border x:Name="TileHost" (3D view lives here by default) + Image Source=Canvas (IsVisible PreviewSwapped)
 │  │           2D-only ink (centred "2D only" WarningBrush + "partial alpha cannot rise" muted; Opacity by ShowTwoDOnly)
 │  │           caption 26 ScrimBrush: [IconRise(placeholder IconLock until T12) + TileCaption | enlarge | swap]
 │  │    readout (fixed 28, top hairline): ReadoutText (left, reserved) | DimensionsText (right, faint)
 │  └ Border.pane.last — RAIL: pane-hrow.tabrow [railtab DEPTH | railtab EXPORT + tabcount ExportBadge]
 │       ScrollViewer: DEPTH stack | EXPORT stack (IsVisible by tab)
 │       EXPORT only, pinned below the scroller: Border.ship (TextBlock.fn ManifestTitle | ManifestSize;
 │            WrapPanel of TextBlock.part ManifestParts; TextBlock.caveat ExportProblem) + Button.accent full ExportLabel (IconDownload)
 └ Border.modal-foot: [● FooterText] … [Button.tbtn.ghost Back] [Button.accent Save depth (IconCheck)]  — Save: Opacity/IsHitTestVisible by ShowSave
```
DEPTH stack (`Margin="13,13,13,16" Spacing="15"`): FILL ALL (`ctlh` grid: plabel + cv SolidHeader;
row `Front [nin] Back [nin]` — `NumericUpDown.nin` `MinWidth="0" Width="58" FormatString="0"
Minimum=0 Maximum=255`; `Button.tbtn` full FillAllLabel); THICKNESS (plabel + cv DeepestText; rows
`Grid 62,*,44`: mono label, track `Border` with `RarityBarBrush` fill whose width multi-binds
`ShareWidthConverter` to the track's `Bounds.Width`, count right-aligned); the dashed note
(`Rectangle.dashed.btn` + `TextBlock.vlock`) with DepthNote; **2D ONLY last** (`IsVisible =
HasPartialAlphaRows`): plabel with `Icon2dOnly`-placeholder + warning cv PartialAlphaCountText; rows
`Border` on `BgAlt2Brush`: name | count (WarningBrush, mono); note "A voxel is solid or empty, so
partial alpha cannot rise." EXPORT stack: FORMAT (`Button.preset` ×3 stacked, `Classes.sel`,
`TextBlock.pn` / `.pd`); PIVOT (3 `RadioButton GroupName="pivot"` each `Grid *,Auto` label + faint
hint); VOXEL SIZE (`NumericUpDown.qnt` `Minimum=0.01 Increment=0.25 FormatString="0.##"` + cv
ModelDimensionsText); SHAPE (two `RadioButton GroupName="shape"` "Single file" + hint ".zip", "Folder" +
hint "one file per model" — **always laid out**, `Opacity`/`IsHitTestVisible` by IsBatch).

**Code-behind (this task):** `Geometry` via `CanvasMapping.TryFrame` on `_surface.Bounds` +
`CanvasImage` bitmap size + VM zoom/pan; `ApplyCanvasTransform` (centre origin scale+translate on the
Image only — the editor's); `ClampPan`; `BuildBackdrop` via `PixelLattice.Build` with the editor's
`_backdropFrom` cache and `Flatten`; `DrawBand` (marquee only, the editor's dashed accent rectangle);
`DepthLabels.Frame = frame` on every re-layout; subscriptions to `BackdropChanged`, `DepthChanged`
(→ `DepthLabels.InvalidateVisual()`), `SubjectChanged` (→ `vm.Preview.Reset(aspect of the 3D view's
Bounds)` + `RepaintNow`), `GestureEnded` (→ `_preview.HostInteracting = false`). **One**
`ModelPreviewView` (`x:Name="ModelView"`, `DataContext="{Binding Preview}"`, `Mesh`/`Texture` bound)
declared inside `TileHost`; on `PreviewSwapped` change the code-behind moves it: `TileHost.Child =
null; BigHost.Child = ModelView` (and back). Never two instances. Focus on attach (`AttachedToVisualTree
+= (_, _) => Focus();`) — the 3D view takes focus only on its first attach (step-6 M6), so the page wins.

**`DepthLabelLayer : Control`** — styled properties `Surface` (`DepthSurface?`) and `Frame`
(`CanvasFrame`); `public const double MinCellPx = 18;` `Render`: return when `Frame.Scale < MinCellPx`
or no surface; for each SOLID pixel whose cell intersects `Bounds`, draw `1 + f + b` as
`FormattedText` (mono family from `MonoFontFamily`, SemiBold, size `0.42 × cell`, invariant) centred
in the cell, ink = `CellLabelOnLightBrush` when the art pixel's luminance `0.3R + 0.59G + 0.11B >
125`, else `CellLabelOnDarkBrush` (looked up with `TryFindResource` on the control's theme variant).
One control, one `Render` — never a TextBlock per cell.

**KeyHints additions:** `RiseConstrain` ("⌃" on macOS, "Ctrl" elsewhere); `RiseBrushTool`
`"Brush  (B)" + Tail($"hold {RiseConstrain} for a straight stroke")`; `RiseLineTool`,
`RiseRectangleTool` (`{RiseConstrain} squares it, {FromCenter} draws from the center`), `RiseFillTool`
`"Same-color fill  (G)"`, `RiseSelectTool` `"Select a region, then click inside it to raise it  (M)"`;
`RiseLegend` `$"left front · right back · {Constrain} lowers"`. Undo/redo reuse `UndoTool`/`RedoTool`.

- [ ] **Step 1: Failing `RiseViewLayoutTests`** at the minimum page area
  (`(MinWindowWidth − 24) / BaseScale × (MinWindowHeight − ChromeReserve) / BaseScale`):
  1. the three panes measure 262 / star / 300 and the footer is ≤ 48 tall;
  2. **swap keeps the way back:** `ModelTile` has non-zero bounds and is hit-testable in both states;
     the same `ModelPreviewView` instance's visual parent is `TileHost` before and `BigHost` after;
     `TileCaption` names the other half;
  3. the 3D view is in a SIZED slot both ways (`Bounds.Width ≥ 100` in the tile, ≥ 300 swapped);
  4. **reserved geometry:** the readout row is the same height with and without a hover pixel; the
     Save button's bounds are identical for variant and recipe sources; toggling a batch on/off leaves
     every EXPORT control's Y unchanged (assert via `TranslatePoint` of the Voxel size box and the
     Export button);
  5. the rail-foot band is exactly 47 in all three sources;
  6. every compact numeric cell is `FieldHeightSm` (26) tall and `Bounds.Width == Width`;
  7. at **1366 × 768** the toolstrip is ONE line, and at 1280 every wrapped line carries real content
     (`ToolstripLayoutTests` pattern; if the legend forces a wrap at 1366, shorten `RiseLegend` — do not
     shrink a control);
  8. the cell-label layer draws: zoom so `Frame.Scale ≥ 18`, render the frame (`CaptureRenderedFrame`)
     and assert the label ink colour appears inside a known solid cell; at fit on a 512px canvas it
     draws nothing (time the `Render` — must not walk 262,144 cells' text).
- [ ] **Step 2: Implement** markup, code-behind, label layer, styles, tokens, hints. Remove the
  Task 7–9 `WiringCoverageTests.Exempt` entries; `No_exemption_is_stale` proves they are bound.
- [ ] **Step 3: Captures** — add `VisualCapture.Capture_rise_page` writing, for BOTH themes, at
  **1180 × 720** AND at the **minimum page area** (`rise-<state>-<theme>[-min].png`): `variant-painted`
  (oak with a front/back pattern, zoomed so labels show), `variant-locked`, `variant-ghost` (2D-only
  listing + tile ink), `recipe` (pill, die, inert layers), `set-batch-export` (tiles checked, EXPORT tab,
  shape inked), `set-sealed-export` (refusal caveat), `swapped`, `enlarged`. Each arranged state is
  VERIFIED inside the helper before saving (e.g. assert `vm.IsExportTab` and the ship border is on
  screen) — CLAUDE.md's "a capture asserts nothing" lesson. Run
  `NFTY_CAPTURE=1 NFTY_CAPTURE_DIR=<dir> dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~Capture_rise_page`
  and **look at every PNG**. Compare side by side with `editor-enabled-*`, `editor-zoom-*`,
  `zz-recipe-*`, set-browser and `export-*` frames: header heights, rule weights, chip shapes,
  caption bar, footer, kicker typography must match; fix divergence in this task.
- [ ] **Step 4: Sweeps** — full App suite green, in particular `PixelPerfectRenderingTests`,
  `ThemeResourceTests`, `DeadCodeAuditTests` (every new class/token used), `WiringCoverageTests`,
  `IconSourceTests`.
- [ ] **Step 5: Commit** `feat(app): the rise page — three panes, re-parented 3D tile, depth labels`.

---

### Task 11: Canvas interaction — pointer, keys, hover readout

**Files:**
- Modify: `src/Nfty.App/Views/RiseView.axaml.cs`
- Test: `tests/Nfty.App.Tests/RiseGestureTests.cs`

**Behaviour** (the editor's handlers are the template — read `IngredientEditorView.axaml.cs` first):
- `CanvasSurface.PointerPressed`: ignore unless `vm.ShowPaintCanvas`; `e.Pointer.Capture(_surface)`;
  middle (`PointerUpdateKind.MiddleButtonPressed || IsMiddleButtonPressed`) → pan (editor's
  `_panning`); else side = right button ? `Back` : `Front`; `lower = Shift`; `_constrain = Ctrl`;
  `_fromCenter = Alt`; `_applyingSelection = tool == Select && vm.SelectionContains(first)`; add the
  first point (`CanvasMapping.PixelAt`); `_preview.HostInteracting = true`;
  `vm.PreviewDepthStroke(Gesture(), side, lower)`; redraw marquee.
- `PointerMoved`: panning → `vm.PanBy` (anchor follows pointer); drawing → add point, update
  modifiers (Shift mid-drag changes `lower`? **No** — side and sign are fixed at press, like the
  editor's right-button eraser; only Ctrl/Alt constrain live), preview, redraw; not drawing → hover:
  `vm.HoverPixel = CanvasMapping.PixelUnder(...)`.
- `PointerReleased`: panning → end; drawing → `vm.ApplyDepthStroke(Gesture(), side, lower)` (the VM
  raises `GestureEnded` → `HostInteracting = false` → exact repaint), release capture, redraw.
- `PointerCaptureLost`: panning off; drawing → `vm.CancelDepthPreview()`. `PointerExited` (not
  drawing) → `HoverPixel = null`.
- `PointerWheelChanged`: anchored zoom through `CanvasMapping.PanKeeping` + `ClampPan`.
- `Gesture()` = `StrokeConstraint.Apply(tool, constrain: _constrain, fromCenter: _fromCenter,
  movingSelection: _applyingSelection, _points, w, h)` — the ONE helper preview and commit both call.
- Keys (tunnel, `handledEventsToo: true`, focus-guarded by the editor's `FocusIsOurs` adapted to
  `RiseView`): mid-drag Escape aborts (cancel); `B L R G M` arm tools; `[`/`]` brush size; `Ctrl+A` →
  `SelectAllCommand`; `+`/`-`/`0` zoom; arrows pan when zoomed. `KeyBindings` on the UserControl:
  Escape → `ClearSelectionCommand`, Ctrl+Z → Undo, Ctrl+Y and Ctrl+Shift+Z → Redo.

- [ ] **Step 1: Failing `RiseGestureTests`** — drive the REAL control (`window.MouseDown(point,
  MouseButton.Left, RawInputModifiers.Shift)` etc.; compute points with a test-local fitted mapper from
  `CanvasImage.Bounds` + bitmap size, NOT through `CanvasMapping` — the `CanvasZoomTests.FittedMapper`
  pattern), on a variant source at 1180 × 720:

```csharp
[AvaloniaFact] public void A_left_click_raises_the_front_by_one()
[AvaloniaFact] public void A_right_click_raises_the_back_by_one()
[AvaloniaFact] public void Shift_left_click_lowers_the_front_and_floors_at_zero()
[AvaloniaFact] public void Shift_right_click_lowers_the_back()
[AvaloniaFact] public void Dragging_back_and_forth_raises_each_pixel_once()           // brush, 3 passes over row 2
[AvaloniaFact] public void Ctrl_constrains_a_line_to_forty_five_degrees()             // Line (1,2)->(6,3) with Ctrl lands on row 2 only
[AvaloniaFact] public void A_click_on_a_transparent_pixel_changes_nothing()
[AvaloniaFact] public void Losing_capture_mid_drag_leaves_the_depth_untouched()
[AvaloniaFact] public void Keys_arm_the_tools_but_not_while_typing_in_a_field()       // "L" arms Line; focus the Front box, type "L": tool unchanged
[AvaloniaFact] public void Ctrl_Z_undoes_the_last_stroke()
[AvaloniaFact] public void Hovering_a_solid_pixel_fills_the_readout_and_leaving_blanks_it()
[AvaloniaFact] public void A_recipe_source_ignores_clicks()
[AvaloniaFact] public void Strokes_do_nothing_while_the_3d_view_fills_the_pane()     // swapped: click lands on the 3D view, depth unchanged
[AvaloniaFact] public void The_3d_frame_repaints_exactly_when_the_stroke_ends()       // RepaintCount rises by ≥1 on MouseUp; HostInteracting false after
```
Each asserts on `vm.Surface` pixels (exact), never on a command having been called.
- [ ] **Step 2: Implement.** **Step 3:** run `--filter FullyQualifiedName~RiseGesture`, full suite.
- [ ] **Step 4: Mutation-probe** two: swap the left/right side mapping → the left/right tests go red;
  drop `PointerCaptureLost`'s cancel → the capture test goes red. Report.
- [ ] **Step 5: Drive the real app** (`dotnet run --project src/Nfty.Desktop` is not wired to Rise until
  step 8 — instead run the `Capture_rise_page` frames after scripted strokes and LOOK at them).
- [ ] **Step 6: Commit** `feat(app): rise canvas gestures — left front, right back, shift lowers, ctrl constrains`.

---

### Task 12: Icons `rise` and `2d-only`

**Files:**
- Create: `assets/icons/rise.svg`, `assets/icons/2d-only.svg` (each with `<title>IconRise</title>` /
  `<title>Icon2dOnly</title>` and a `<desc>` saying what it is and what it must not look like)
- Regenerate: `src/Nfty.App/Themes/Icons.axaml` (`python tools/icons/build.py`)
- Modify: `src/Nfty.App/Views/RiseView.axaml` (swap the two placeholders), `assets/icons/README.md`
  (glyph count 45 → 47 — count the directory first; state the real number)
- Modify: `tests/Nfty.App.Tests/VisualCapture.cs` (`Capture_icon_set`)

Glyph rules (`assets/icons/README.md`): 24-unit box, ONE `<path>`, stroked `currentColor`
width 2, round caps/joins, reads at 12px, differs from its neighbours. **rise** = a solid with a
visible depth face — a pixel sheet with one raised block/extrusion edge — and must NOT read as
`kitchen`'s archive box (a lidded box). **2d-only** = a flat sheet (a single thin parallelogram or
sheet with a folded corner), distinct from `rise` at a glance.

- [ ] **Step 1:** draw both, run `python tools/icons/build.py`, `dotnet test tests/Nfty.App.Tests
  --filter FullyQualifiedName~IconSourceTests` → green.
- [ ] **Step 2:** `Capture_icon_set` renders EVERY `Icon*` geometry in a grid at 12, 14 and 18 px in
  both themes (`icons-12-dark.png` …), with `rise` placed beside `kitchen` and `2d-only` beside `rise`.
  LOOK. If `rise` can be mistaken for `kitchen` at 12px, redraw. Record the comparison in the report.
- [ ] **Step 3:** swap `IconLock` placeholders for `IconRise` (tile caption) and `Icon2dOnly` (chips,
  2D ONLY header, book chip). `DeadCodeAuditTests.Every_icon_is_drawn_somewhere` green.
- [ ] **Step 4:** recapture `Capture_rise_page` (both themes) and look at the two glyphs in place.
- [ ] **Step 5: Commit** `feat(icons): rise and 2d-only glyphs, drawn and generated`.

---

### Task 13: Integration pass — sweeps, perf, captures, refinement

**Files:**
- Modify: `tests/Nfty.App.Tests/DarkModeContrastTests.cs` (`Screens()`),
  `tests/Nfty.App.Tests/MinimumWindowFitTests.cs` (`Pages()`), `tests/Nfty.App.Tests/TextBaselineTests.cs`
- Create: `tests/Nfty.App.Tests/RiseWiringTests.cs`, `tests/Nfty.App.Tests/RisePagePerfTests.cs`
- Modify (only as the frames demand): `RiseView.axaml`, `Styles.axaml`

- [ ] **Step 1: Contrast sweep** — add `("rise-2d-only", RiseView over the variant source with
  `ghost` selected, DEPTH tab)` — its riskiest ink: WarningBrush counts on `BgAlt2Brush` rows, the
  warning "2D only" chips, the DISABLED fill-all button and dimmed tools — and `("rise-export-sealed",
  Set source over a SealedSet, EXPORT tab)` — the refusal caveat on the ship ground. Build them the way
  the app builds them (real callbacks, no stub that renders a control disabled the app never disables).
  Probe: recolour the 2D ONLY count to `FgFaintBrush` → the sweep must fail in at least one theme;
  restore.
- [ ] **Step 2: Fit sweep** — add `rise/variant`, `rise/recipe`, `rise/set-export` to `Pages()`. The
  canvas pane is the star column: also assert by name that `CanvasHost` is fully inside its pane at the
  minimum area (a star cell arranged at zero passes the desired-size sweep).
- [ ] **Step 3: Baselines** — `Sweep("rise-depth", …)`, `Sweep("rise-export", …)` at 1180 × 720.
  Fix any row over 1.25 px (fold label into run, or `VerticalAlignment="Bottom"`, per CLAUDE.md).
- [ ] **Step 4: `RiseWiringTests`** —

```csharp
[Fact]
public void Every_rise_command_is_bound_in_the_rise_view()
{
    var root = RepoRoot();
    var markup = Regex.Replace(File.ReadAllText(Path.Combine(root, "src/Nfty.App/Views/RiseView.axaml")),
        "<!--.*?-->", "", RegexOptions.Singleline);
    var code = File.ReadAllText(Path.Combine(root, "src/Nfty.App/Views/RiseView.axaml.cs"));
    var missing = typeof(RiseViewModel).GetProperties()
        .Where(p => typeof(ICommand).IsAssignableFrom(p.PropertyType))
        .Select(p => p.Name)
        .Where(n => !Regex.IsMatch(markup, $@"\b{n}\b") && !Regex.IsMatch(code, $@"\b{n}\s*\.\s*Execute"))
        .ToList();
    Assert.True(missing.Count == 0, "Rise commands nothing on the Rise page invokes: " + string.Join(", ", missing));
}
```
- [ ] **Step 5: `RisePagePerfTests`** (measured, NEVER asserted as wall-clock) — `[AvaloniaTheory]
  [InlineData(64)] [InlineData(512)]`: a Custom ingredient whose one variant is the `RisePerfTests`
  disc at `size`, a variant source + VM + `RiseView` in a 1180 × 720 window; `Perf.Enable(); Reset();`
  then 20 single clicks (`ApplyDepthStroke`), one 40-move brush drag (previews then apply), one swap +
  orbit; assert only that `Rise.DepthClick`, `Rise.MeshRebuild`, `Riser.TryBuild` and
  `ModelPreviewView.Repaint` appear in `Perf.Snapshot()`; write `Perf.Report()` to the output. Run with
  `dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~RisePagePerf -l "console;verbosity=detailed"`
  and put both tables in the report. If a 512px mesh rebuild exceeds the 33 ms budget, say so — the
  canvas is never skipped, so it costs 3D smoothness only; do not optimise in this step (`BACKLOG.md`
  entry if it does).
- [ ] **Step 6: Full capture set + refinement** — run every `Capture_*` (both themes), look at all
  `rise-*` frames beside the editor / recipe / set-browser / export frames and the approved mockup;
  fix any divergence (spacing, rule weights, caption typography, chip shapes, empty space). Each fix
  re-captured and compared. Pixel-diff the editor frames against Task 4's baseline: still identical.
- [ ] **Step 7: Full suite** `dotnet test nfty.sln` → all five projects green; `dotnet build nfty.sln`
  0/0. Record the per-project counts.
- [ ] **Step 8: Commit** `test(app): rise page joins the sweeps; perf measured; frames refined`.

**Then:** controller runs the **Opus 5.5** whole-step review on `e5fe8b6..HEAD`, applies fixes, corrects
the spec's stale mouse-map Testing bullet (ruling 19), and tags `rise-step7`.

---

## Self-review (writing-plans)

**Spec coverage.** Canvas: Geometry/zoom/pan/lattice (T4, T10), mouse table (T11), tools ±1 once per
gesture (T1, T7), select-then-click (T7), reroll die via `Ticketer.TryRoll` (T6 via Generator, T7 die),
cell labels by one overlay `Render` (T10), reserved status line (T7/T10), corner tile that swaps and
never disappears (T7/T10). Left rail: siblings per source, checkboxes for batch, 2D-only glyph on items
and book (T5/T6/T10/T12); sheet grid row → step 8. Right rail: DEPTH fill-all + listing (T8), EXPORT
format/pivot/voxel/folder-or-zip through `ModelWriter` (T2/T9); Make voxel-ready → step 8 with its
slot ruled (ruling 10). Page rules: own history (T1/T7), leaving asks (T8), saving is structure, lock
gated inside Save (T5/T8), recipe/asset read-only (T6), sealed refusal (T6/T9), perf measured (T3/T13).
Preview: interaction budget + exact on end (T3/T7/T11). Icons (T12). Testing section: gesture tests with
real pointer events (T11), both themes at the minimum window (T10/T13), contrast in riskiest state
(T13), swap keeps its way back (T10). Six step-6 carry-ins: ruling 11 → T3. Brief rulings 1–11: scope
(no entry points; factory registered T8), sources, mouse, footer Save, inert layers, tools, live 3D,
icons, verification, mockup decisions (chips, histogram, pill, readout, labels, tile, export cards +
manifest), branch discipline.

**Placeholders.** Elided test bodies are marked "write fully" with the exact assertions stated; no
TBDs in production signatures.

**Type consistency.** `DepthSurface`/`DepthPixel`/`DepthSide` (T1) → `RiseSubject` (T5) → VM (T7);
`ModelInput`/`ModelBatch` (T2) → `RiseSource.ExportInputs` (T5/T6) → export (T9); `CanvasFrame`/
`CanvasMapping` (T4) → view (T10/T11) and `DepthLabelLayer.Frame` (T10); `StackDepth.ForSelection` (T2)
→ recipe and Set sources (T6); `SetItemSelection.Resolve` (T2) → Set source (T6); `HostInteracting`
(T3) → view (T11); `LeaveChoice` (T8) → tests (T8).
