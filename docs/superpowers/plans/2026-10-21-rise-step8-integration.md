# Rise step 8 — entry points; Set / cook / export / sheet integration — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: use superpowers:subagent-driven-development (one fresh
> Sonnet implementer per task, a Sonnet task review after each, an **Opus 5.5** whole-step review at
> the end). Steps use checkbox (`- [ ]`) syntax for tracking. Every task leaves `dotnet build nfty.sln`
> at 0 warnings / 0 errors and the full suite green.

**Goal:** make Rise reachable from everywhere the spec names (Ingredient pane, Ingredient editor, Recipe
pane, Set browser, Landing), make cooking and exporting carry 3D models (`generate --models`, the Cook
dialog's *Include 3D models*, `models/` in a Set, the export dialog's models axis, the stamp never
reaching a model), make sheets self-describing (`nfty-grid` written, read, auto-detected, written into
foreign sheets), and give a 2D-only book its fix path (**Make voxel-ready**).

**Architecture:** `Nfty.Core` never references `Nfty.Model`, so every Core writer that must produce a
model gets it through a **seam Core defines and Model implements**: `IAssetModeler` (Core,
`Output/`) is called by `SetWriter` inside its existing per-asset parallel loop; `SetModeler`
(Model) implements it with `StackDepth.ForAsset` + `Riser.TryBuild` + `ModelWriter.Write`. Exporting
copies `models/` as files, so the number stamp — applied to a copy at export time — cannot reach a
model by construction, and a test proves it. The sheet grid is a Core value (`SheetGrid`) with one
text form, one parser, one detector and one rewriter, used by `SpriteSheet` (write), the Rise picture
source (read / detect / rewrite) and nothing else. Make voxel-ready is a Core command (`AlphaSnap`,
an ordinary `RegionEditCommand<TPixel>`) plus a Core book-level operation (`VoxelReady`) with an
explicit undo record; the GUI only previews, gates and persists. Every GUI entry point builds a
`RiseSource` and hands it to the step-7 factory `Func<RiseSource, RiseViewModel>`; the **lock owner
pushes** lock changes and the **book owner subscribes** to saves — the two step-7 carries.

**Tech Stack:** .NET 10, Avalonia 12.1.1, CommunityToolkit.Mvvm, ImageSharp 3.1.11 (pinned — do not
touch), System.CommandLine, xunit.v3, Avalonia.Headless. **Zero new packages.**

**Spec:** `docs/superpowers/specs/2026-09-25-rise-design.md` — "Where depth lives", "2D only",
"Format › Set", "Format › Spritesheets", "Entry points and integration", "CLI", "Testing". Step-7
plan `docs/superpowers/plans/2026-10-20-rise-step7-page.md` (rulings 2, 3, 10, 16 are inputs here) and
its ledger `.superpowers/sdd/2026-10-20-rise-step7-page/progress.md` (four CARRY TO STEP 8 items).

**Branch / start:** `worktree-nfty-ticket`, HEAD `0e87475` (tag `rise-step7`). **Never push, never
merge.** This branch merges into `main` as ONE product: no paid/free framing in any copy, comment,
doc or commit. The controller tags `rise-step8` after the Opus review passes.

---

## Open questions (each with the recommended answer the plan is written against)

The spec is silent or ambiguous on each of these. The plan implements the recommendation; a ruling
the other way changes only the task named.

1. **Set browser batch.** Spec: "Rise the selected asset, or check several for a batch." *Recommend:*
   the browser gets ONE `Rise` button acting on the shown asset; checking several happens in the Rise
   page's left rail, which already carries batch checkboxes over every asset of the Set. No checkbox on
   a grid of hundreds of tiles. (Task 11.)
2. **`set.json` `models` block shape.** *Recommend:* `{ "format": "glb", "pivot": "bottom-center",
   "voxelSize": 1, "skipped": [7, 12] }` — CLI tokens as strings (Core cannot see Model's enums),
   `skipped` = asset numbers with nothing fully opaque (D5: skipped and named, never failing the cook),
   ascending, omitted when empty. Additive optional field: **no schema bump.** (Task 3.)
3. **Extend over a Set with models.** *Recommend:* a Set is all-modeled or not. `extend` (CLI and the
   Cook dialog's extend path) **inherits** the recorded options and has no model flags; `SetWriter`
   refuses a modeler on a Set cooked without models and a missing/different modeler on a Set cooked
   with them, naming the difference. (Tasks 3, 5, 7.)
4. **Export presets and the models axis.** *Recommend:* `ExportOptions.Models` (default `true`);
   `Marketplace` turns it off (art + standard fields only), the other three keep it. The axis is a
   reserved row, inked only when the Set carries models, otherwise disabled with the reason ("this
   Set was cooked without 3D models"). `set.json` is copied verbatim when the axis is off — the same
   rule every other axis already follows. (Tasks 6, 8.)
5. **When a foreign sheet is rewritten.** Spec: "always and silently, once one is known".
   *Recommend:* as soon as a grid other than 1 × 1 is known — from auto-detect or manual entry — and
   again whenever the author changes it. A file whose detection gives none / 1 × 1 is a picture and is
   never touched. A file that cannot be written keeps the grid for the session; the status line says
   which happened, with the byte delta. (Task 12.)
6. **Auto-detect algorithm** (spec: "equal-sized cells separated by fully transparent rows and
   columns"). *Recommend:* per axis, the **smallest** divisor `c` of the image side such that no run
   of content (maximal span of columns — resp. rows — holding any pixel with `A > 0`) crosses a
   multiple of `c`; equal sizes hold by construction. `null` when the result is 1 × 1 or the image is
   empty. `Count` = (index of the last cell holding content, reading order) + 1. Manual entry is
   always available beside it. (Task 2.)
7. **Make voxel-ready's scope, preview and undo.** *Recommend:* scope = everything the 2D-only state
   it fixes covers — the **whole book** for a book layer (the flag is book-wide; fixing one layer would
   leave the book 2D-only and the button would lie about its outcome), the ingredient for a loose
   `.igt`, the session art for a picture/sheet. Preview = a card listing each affected variant with
   before / after thumbnails and the counts that go solid / clear, Apply or Cancel. Requires no unsaved
   depth on the page (Save or discard first — a structural write must not interleave with an unsaved
   one). Undo = one reserved **Undo voxel-ready** line in the same rail slot until the page is left,
   reverting exactly those variants' pixels and refusing (named) if any changed since. (Tasks 1, 14.)
8. **Stale art (carry 3).** *Recommend two guards:* (a) the editor's **Rise** button requires the
   editor to have no unsaved edits ("Save the layer first — Rise reads the saved picture"); (b)
   `VariantRiseSource.SaveAsync` compares each written variant's art against the live variant's pixels
   and refuses the whole save, naming every mismatched variant, writing nothing. (a) makes the case
   unreachable today; (b) keeps it unreachable for any later route. (Task 10.)
9. **`IngredientEditorViewModel.IsReadOnly` pull-only gap (carry 4).** *Recommend:* fix it with the
   same push — `ExplorerViewModel.OnIsEditingChanged` notifies the open editor and the open Rise page.
   One method each; leaving a known gap beside the fix for its twin is the "second copy" this repo
   keeps removing. (Task 9.)
10. **A Rise save made from an editor-launched Rise.** *Recommend:* the editor subscribes to the Rise
    page's `Saved`, refreshes its own `_depths` and `_ing` from the book (it is clean — Q8a), and
    re-raises its own `Saved`, so the Explorer tree and the Recents line refresh through the ONE path
    that already exists (`OnEditorSaved`, `RememberOnSave`). (Task 10.)
11. **Picture / sheet "Save as .igt".** *Recommend:* a **Custom** ingredient (full-color art is never
    colorized), every cell a variant, weight 1, ids `cell-1`…`cell-N` in reading order, names
    `Cell 1`…; a single picture is one variant whose id is `WizardViewModelBase.DeriveId(stem)` and name
    the file stem; ingredient name = file stem. Written to a path from `SaveFileAsync`; the page stays
    on the picture source, depth marked saved, status line names the file. (Task 13.)
12. **Changing a sheet's grid with unsaved depth.** *Recommend:* ask first (re-slicing discards every
    cell's depth); unchanged grid keeps per-cell depth for the session. (Task 13.)
13. **Cook dialog model options.** *Recommend:* session defaults (GLB, bottom-center, 1), not stored in
    the book or app state — the spec gives them no home and the defaults are the CLI's. (Task 7.)
14. **2D-only marks in the Explorer tree + Kitchen shelf, and the editor's opacity-lock warning
    wording** ("saving a see-through pixel makes the book 2D-only"). Spec items assigned to no earlier
    step. *Recommend:* included here (Task 15). The existing warning also says "voxelise" — British,
    fixed in the same edit.
15. **BACKLOG M5 (mesh copied three times) "before step 8".** *Recommend:* measure under `generate
    --models` with `Perf` (Task 17), record the numbers in that BACKLOG entry, optimize nothing in this
    step unless the copy is more than 10% of `SetModeler.Model`'s time.
16. **OpenSea `animation_url` pointing at the model.** *Recommend:* out of scope; marketplace metadata
    is untouched. One BACKLOG entry (Task 16).
17. **Variant rises its STORED pixels** (step-7 ruling 2: a Dynamic/Static variant rises gray). Stands.
    A "rise as colorized sample" option is not added. The author should hear this again now that the
    pane button makes it visible.

---

## Global constraints (verified in code at `0e87475`)

| Fact | Value | Where |
|------|-------|-------|
| Assembly arrow | `Nfty.Core` / `Nfty.Ticket` never reference `Nfty.Model` | `AssemblyRulesTests` |
| `Nfty.Model` D7 | no field writable after construction | `AssemblyRulesTests` |
| Window minimum / scale | 1280 × 720 / 1.2 | `ShellViewModel.MinWindowWidth/Height`, `BaseScale` |
| Modal area at minimum | ~1067 × 526 (`ModalFitTests`); export card geometry 40 / 287 / 100 / 47 = 496 | CLAUDE.md, `ExportDialogLayoutTests` |
| Set numbering | `setNumber.ToString("D4")` stem | `SetWriter.PathsFor` |
| Set include list | `SetLayout.Directories` (`images`, `metadata`, `nfty`) | `Output/SetLayout.cs` |
| Model tokens | `glb` / `gltf` / `gltf-embedded`; `bottom-center` / `center` / `min-corner` | `CommandFactory.Model.cs` `ModelFormatNames`, `PivotNames` |
| Rise factory | `Func<RiseSource, RiseViewModel>` (singleton) | `ServiceRegistration.AddNftyApp` |
| Lock push | `RiseSource.NotifyLockChanged()` — no caller yet | `Rise/RiseSource.cs` |
| Rise save event | `RiseViewModel.Saved(LoadedCookBook)` — no subscriber yet | `RiseViewModel.Save.cs` |
| Solid / partial | `Opacity.IsSolid` (A == 255), `Opacity.IsPartial`, `Opacity.HasPartialAlpha(book/recipe/ing/img)` | `Imaging/Opacity.cs` |
| Listing | `VoxelReport.Scan(book/recipe/ing)` → `VoxelVariant(IngredientName, VariantName, VariantId, Pixels, Partial)` | `Stats/VoxelReport.cs` |
| Parallel work | `ParallelWork.ForEach/ForEachAsync`, results into per-item slots, never collected from the loop | `ParallelWork.cs` |
| PNG text | `PngMetadata.TextData` + `PngTextData(keyword, value, languageTag, translatedKeyword)`; text under `TextCompressionThreshold` (1024) is written as `tEXt` | ImageSharp 3.1.11 |
| Docs | every public member `///`-documented (`TreatWarningsAsErrors`) | `Directory.Build.props` |
| Spelling | US English everywhere | CLAUDE.md |
| Determinism | ordinal sorts on everything reaching a file; byte-identical output for identical input, at any thread count | CLAUDE.md |
| Archive formats | additive optional fields only; **no schema bump** in this step | spec "Format" |
| Commit trailer (implementers) | `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>` | brief |

**House rules every GUI task obeys** (CLAUDE.md, restated where they bite in this step): verify from
rendered frames, BOTH themes, at the minimum window (modals at their real area); reserve space /
toggle ink — `Opacity` + `IsHitTestVisible`, never `IsVisible` on layout-occupying controls (except
the recorded positional exception at the end of a container); token brushes only; a disabled button is
a label — every gate re-checked inside the command; a button naming an action performs it; a count is
a button that opens what it counted; empty states say which emptiness; `MinWidth="0"` before any
`Width` < 64 on `TextBox`/`NumericUpDown`; `FormatString` on every `NumericUpDown`; status lines are
said AFTER navigation; a fire-and-forget task a test must observe gets a `Pending*` handle.

## Review Focus

1. **Cooking a book whose rules leave an asset fully transparent** (all layers absent or clear art)
   with `--models`: the cook must finish, the Set must list that number under `skipped`, and the CLI
   must say so. Pinned in Task 3 (`A_fully_transparent_asset_is_skipped_and_recorded`) and Task 5.
2. **Extending a modeled Set** from the GUI's extend path, not only the CLI: models for the new assets,
   same options, old model files untouched byte for byte. Pinned in Task 3
   (`Extending_a_modeled_set_models_only_the_new_assets`) and Task 7.
3. **A 16-bit or palette PNG opened as a foreign sheet**: the rewrite must keep every pixel exactly
   (not round them to 8-bit). Pinned in Task 2 (`Rewriting_a_16_bit_sheet_keeps_every_pixel`).
4. **The edit lock flipping while Rise is open from the Explorer** (it cannot today — the titlebar
   lock is hidden on Rise — but a later shortcut would make it reachable): Save, footer and note must
   follow at once. Pinned in Task 9 (`Flipping_the_lock_reaches_an_open_rise_page`).
5. **A grid entered manually that does not divide the picture** (e.g. 3 columns on a 64px-wide sheet):
   refused with the numbers, never sliced unevenly. Pinned in Task 2 (`A_grid_that_does_not_divide_the_picture_is_refused`)
   and Task 13.

---

## Notes for the controller

**Final review runs on Opus 5.5**, scoped to `0e87475..HEAD`.

**Shared files (sequential by task order):** `SetWriter.cs` (T3 only), `Metadata.cs` (T3),
`SetLayout.cs` (T3), `SetExporter.cs` + `ExportOptions.cs` (T6), `SpriteSheet.cs` (T2),
`ServiceRegistration.cs` (T9, T10, T11, T12), `ExplorerViewModel.cs` (T9, T10, T15),
`IngredientEditorViewModel*.cs` (T10, T15), `RiseViewModel*.cs` (T12, T13, T14), `RiseView.axaml`
(T13, T14), `Styles.axaml` (T7, T8, T9, T11, T12, T13, T14, T15), `VisualCapture.cs` (every GUI task),
`CommandFactory*.cs` (T4, T5), `DarkModeContrastTests.cs` / `MinimumWindowFitTests.cs` /
`TextBaselineTests.cs` / `RiseWiringTests.cs` (T17), `docs/manual/**` + `CLAUDE.md` (T16).

**Frames:** archive every capture round under `.superpowers/frames-archive/<view>/` (latest only, per
the user) and refresh the Rise frames gallery artifact as step 7 did.

---

## Task list

| # | Task | Project(s) |
|---|------|-----------|
| 1 | `AlphaSnap` command + `VoxelReady` (plan, apply, undo) | Nfty.Core |
| 2 | `SheetGrid` (text, parse, read, detect, rewrite) + `SpriteSheet` writes `nfty-grid` | Nfty.Core |
| 3 | Set models seam: `models/`, `set.json` block, `IAssetModeler`, `SetWriter` | Nfty.Core |
| 4 | `ModelTokens`, `StackDepth.ForAsset`, `SetModeler` | Nfty.Model, Nfty.Cli, Nfty.App |
| 5 | CLI: `generate --models`, `extend` inherits, `inspect` reports models | Nfty.Cli, Nfty.Core |
| 6 | Export: models axis + stamp isolation | Nfty.Core, Nfty.Model.Tests |
| 7 | Cook dialog: *Include 3D models* | Nfty.App |
| 8 | Export dialog: models row + IMAGES note | Nfty.App |
| 9 | Rise from the Explorer: pane button, recipe action row, lock push, save refresh | Nfty.App |
| 10 | Rise from the Ingredient editor: clean gate, depth refresh, stale-art guard | Nfty.App |
| 11 | Rise from the Set browser | Nfty.App |
| 12 | Picture / sheet source + Landing *Rise a picture…* + grid read / detect / rewrite | Nfty.App |
| 13 | Sheet grid row (manual entry) + *Save as .igt* | Nfty.App |
| 14 | *Make voxel-ready* on the Rise page | Nfty.App |
| 15 | 2D-only marks (Explorer tree, Kitchen shelf) + opacity-lock warning wording | Nfty.App |
| 16 | Manual pages + CLAUDE.md + BACKLOG | docs |
| 17 | Integration pass: sweeps, journeys, perf, full capture set | tests |

---

### Task 1: `AlphaSnap` and `VoxelReady`

**Files:**
- Create: `src/Nfty.Core/Editing/AlphaSnap.cs`, `src/Nfty.Core/Editing/VoxelReady.cs`
- Test: `tests/Nfty.Core.Tests/VoxelReadyTests.cs`

**Interfaces — Consumes:** `RegionEditCommand<TPixel>` (`protected RegionEditCommand(OpacityLock)`,
`protected abstract IReadOnlyList<(int x, int y, TPixel pixel)> ComputePixels(IEditSurface<TPixel>)`),
`IEditSurface<TPixel>.AlphaOf/WithAlpha`, `EditHistory<TPixel>`, `ColorMap.FromImage` / `ToImage`,
`CookBookEdits.UpsertIngredient`, `VoxelReport.CountPartial`, `Opacity.IsPartial`.

**Produces:**
```csharp
namespace Nfty.Core.Editing;
/// Snaps every partially transparent pixel at 50%: A >= 128 -> 255, else 0. Color channels untouched.
public sealed class AlphaSnap<TPixel> : RegionEditCommand<TPixel> where TPixel : struct
{ public AlphaSnap(); }                         // runs OpacityLock.Unlocked: it IS the alpha edit; threshold = VoxelReady.Threshold

public sealed record VoxelReadyChange(string RecipeId, string IngredientId, string IngredientName,
    string VariantId, string VariantName, int ToSolid, int ToClear);
public sealed class VoxelReadyResult : IDisposable      // owns Originals; dispose frees them
{
    public LoadedCookBook Book { get; }                  // the snapped graph (not owned)
    public IReadOnlyList<VoxelReadyChange> Changes { get; }   // RecipeId, IngredientId, VariantId ordinal
}
public static class VoxelReady
{
    public const byte Threshold = 128;
    public static IReadOnlyList<VoxelReadyChange> Plan(LoadedCookBook book);   // nothing written
    public static Image<Rgba32> Snap(Image<Rgba32> art);                        // new image; art untouched
    public static VoxelReadyResult Apply(LoadedCookBook book);                  // new graph via UpsertIngredient
    public static LoadedCookBook Undo(LoadedCookBook current, VoxelReadyResult applied); // throws InvalidOperationException naming every variant whose pixels changed since Apply
}
```
`Apply` snaps through `ColorMap.FromImage` + `new AlphaSnap<Rgba32>()` + `ToImage()` — one snap rule,
the command's. Depth maps are untouched (a pixel turning solid has no stored depth, so it rises at
paper; one turning clear never had any). `Originals` (internal) holds a clone of each changed
variant's pre-snap image keyed `(recipeId, ingredientId, variantId)`.

- [ ] **Step 1: Failing tests.**

```csharp
public class VoxelReadyTests
{
    [Theory]
    [InlineData(1, 0)] [InlineData(127, 0)] [InlineData(128, 255)] [InlineData(254, 255)]
    [InlineData(0, 0)] [InlineData(255, 255)]
    public void Snaps_at_fifty_percent(byte before, byte after)
    {
        var map = new ColorMap(1, 1);
        map.Set(0, 0, new Rgba32(10, 20, 30, before));
        new EditHistory<Rgba32>().Do(new AlphaSnap<Rgba32>(), map);
        Assert.Equal(new Rgba32(10, 20, 30, after), map.Get(0, 0));
    }

    [Fact]
    public void Snapping_is_undoable_through_the_history()
    {
        var map = new ColorMap(2, 1);
        map.Set(0, 0, new Rgba32(1, 2, 3, 127)); map.Set(1, 0, new Rgba32(4, 5, 6, 128));
        var h = new EditHistory<Rgba32>();
        h.Do(new AlphaSnap<Rgba32>(), map);
        h.Undo(map);
        Assert.Equal(127, map.Get(0, 0).A); Assert.Equal(128, map.Get(1, 0).A);
    }

    [Fact] public void A_snap_on_clean_art_is_a_no_op_and_records_no_undo_step()   // h.CanUndo false
    [Fact] public void Plan_lists_every_partial_variant_in_the_book_with_its_counts() // two recipes, one 127 + one 128 pixel -> ToClear 1, ToSolid 1; clean variants absent; ordinal order
    [Fact] public void Apply_snaps_every_listed_variant_and_leaves_the_book_voxel_ready() // Opacity.HasPartialAlpha(result.Book) == false
    [Fact] public void Apply_leaves_depth_maps_and_clean_variants_untouched()     // same DepthMap bytes; clean image ReferenceEquals the input's
    [Fact] public void Undo_restores_the_exact_pixels()                           // per-pixel equal to the pre-Apply images
    [Fact] public void Undo_refuses_naming_a_variant_whose_pixels_changed_since()  // Upsert a different image for one; message contains its name; nothing restored
    [Fact] public void Disposing_the_result_frees_the_originals()
}
```
Fixtures: 1×1 / 2×1 images built in memory with a private `Book(...)` builder shaped like
`GeneratorTests.OneRecipeBook`.

- [ ] **Step 2:** run `dotnet test tests/Nfty.Core.Tests --filter FullyQualifiedName~VoxelReadyTests`
  → fails to compile (types missing).
- [ ] **Step 3:** implement. `ComputePixels` returns only pixels whose alpha changes (so clean art is a
  no-op — `EditHistory.Do` records nothing for an empty change, as every other command relies on).
- [ ] **Step 4:** green; full Core suite green.
- [ ] **Step 5: Mutation probes** — threshold `>` instead of `>=` → the 128 row goes red; `Undo` without
  the changed-since check → the refusal test goes red. Report both.
- [ ] **Step 6: Commit** `feat(core): make voxel-ready — snap partial alpha at 50%, undoable`.

---

### Task 2: `SheetGrid` and the `nfty-grid` chunk

**Files:**
- Create: `src/Nfty.Core/Publish/SheetGrid.cs`
- Modify: `src/Nfty.Core/Publish/SpriteSheet.cs` (`WriteAsync` adds the chunk before `SaveAsync`)
- Test: `tests/Nfty.Core.Tests/SheetGridTests.cs`, extend `tests/Nfty.Core.Tests/SpriteSheetTests.cs`

**Produces:**
```csharp
namespace Nfty.Core.Publish;
public sealed record SheetGrid(int Columns, int Rows, int CellWidth, int CellHeight, int Count)
{
    public const string ChunkKeyword = "nfty-grid";
    public string ToChunkText();                       // "cols=8;rows=4;cell=64x64;count=29", invariant
    public static SheetGrid? Parse(string text);       // null on anything malformed
    public static SheetGrid? Read(string pngPath);     // Image.Identify; null when no chunk / unparsable
    public static SheetGrid? Detect(Image<Rgba32> image);           // Open question 6
    public static string? ProblemWith(SheetGrid grid, int width, int height); // null when it tiles exactly
    public static SheetGrid FromColumnsRows(int columns, int rows, int width, int height); // Count = columns*rows
    public static SheetGrid FromCellSize(int cellWidth, int cellHeight, int width, int height);
    public static long WriteInto(string pngPath, SheetGrid grid);  // returns bytes added (may be negative)
}
```
- `Parse`: `;`-separated `key=value`, keys in any order, unknown keys ignored (an additive future key
  must not make this build refuse a sheet), the four keys required, every number a positive invariant
  integer, `count <= cols*rows`.
- `ProblemWith`: `"64 px does not divide into 3 columns"`-style sentence naming both numbers, or null.
  `FromColumnsRows` / `FromCellSize` throw `ArgumentException` carrying that sentence.
- `WriteInto`: `Image.Load(path)` (**not** `Load<Rgba32>` — keeps the source pixel format, so a 16-bit
  or palette PNG re-encodes at its own depth), replace any existing `nfty-grid` text entry, save to
  `{path}.{guid}.tmp` with `new PngEncoder()`, `File.Move(tmp, path, overwrite: true)`, delete the tmp
  in `finally`. Lets `UnauthorizedAccessException` / `IOException` propagate (the caller decides
  "session only").
- `SpriteSheet.WriteAsync` adds `new PngTextData(SheetGrid.ChunkKeyword, grid.ToChunkText(), "", "")`
  with `Count = items.Count` before `SaveAsync`.

- [ ] **Step 1: Failing tests.**

```csharp
[Fact] public void The_chunk_text_is_the_spec_form() =>
    Assert.Equal("cols=8;rows=4;cell=64x64;count=29", new SheetGrid(8, 4, 64, 64, 29).ToChunkText());
[Fact] public void Parse_round_trips_and_ignores_unknown_keys() =>
    Assert.Equal(new SheetGrid(8, 4, 64, 64, 29), SheetGrid.Parse("count=29;future=1;cell=64x64;rows=4;cols=8"));
[Theory] [InlineData("cols=8;rows=4;cell=64x64")] [InlineData("cols=0;rows=4;cell=64x64;count=1")]
[InlineData("cols=2;rows=2;cell=8x8;count=5")] [InlineData("cols=2;rows=2;cell=8by8;count=1")]
public void Parse_refuses_malformed_text(string text) => Assert.Null(SheetGrid.Parse(text));
[Fact] public void A_sheet_nfty_writes_carries_its_grid()      // SpriteSheet.Write 3 items, Fit layout; SheetGrid.Read(path) == (layout.Columns, layout.Rows, cellW, cellH, 3)
[Fact] public void Writing_a_sheet_twice_gives_identical_bytes()
[Fact] public void Detect_finds_equal_cells_between_transparent_gutters() // 3x2 cells of 8x8, each a 4x4 blob at varying offsets inside its cell -> (3, 2, 8, 8, 6)
[Fact] public void Detect_counts_to_the_last_cell_with_content()         // 3x2 grid, last two cells empty -> Count 4
[Fact] public void Detect_says_nothing_for_a_single_picture()            // one blob -> null
[Fact] public void Detect_says_nothing_for_an_empty_image()
[Fact] public void A_grid_that_does_not_divide_the_picture_is_refused()  // ProblemWith(cols 3 on 64 wide) contains "64" and "3"
[Fact] public void Rewriting_a_foreign_sheet_keeps_every_pixel_and_adds_the_grid() // Rgba32 PNG without chunk: WriteInto -> pixels equal, Read == grid, returned delta == new length - old length
[Fact] public void Rewriting_a_16_bit_sheet_keeps_every_pixel()          // Image<Rgba64> with values not representable in 8 bits; compare as Rgba64
[Fact] public void Rewriting_replaces_an_existing_grid_rather_than_adding_a_second() // exactly one nfty-grid entry after two writes
[Fact] public void A_failed_rewrite_leaves_the_file_untouched_and_no_temp_behind()   // read-only file: throws, bytes unchanged, no *.tmp in the folder
```
- [ ] **Step 2:** red. **Step 3:** implement. **Step 4:** green; full Core suite.
- [ ] **Step 5: Mutation probes** — `Load<Rgba32>` instead of `Load` → the 16-bit test goes red;
  greedy *largest* divisor in `Detect` → the gutter test goes red. Report.
- [ ] **Step 6: Commit** `feat(core): sheets carry their grid — nfty-grid chunk, detect, rewrite`.

---

### Task 3: The Set models seam

**Files:**
- Modify: `src/Nfty.Core/Output/SetLayout.cs` (`ModelsDir = "models"`, added to `Directories`)
- Modify: `src/Nfty.Core/Output/Metadata.cs` (`SetModels`; `SetManifest` gains `SetModels? Models = null` LAST)
- Create: `src/Nfty.Core/Output/IAssetModeler.cs` (`IAssetModeler`, `SetFile`)
- Modify: `src/Nfty.Core/Output/SetWriter.cs` (`Write`/`WriteAsync` take `IAssetModeler? models = null`
  as their last parameter; `ExistingSet` gains `SetModels? Models = null`)
- Test: `tests/Nfty.Core.Tests/SetModelsWriterTests.cs`

**Produces:**
```csharp
namespace Nfty.Core.Output;
/// set.json's record of the models a cook made. Tokens are the CLI's.
public sealed record SetModels(string Format, string Pivot, float VoxelSize, IReadOnlyList<int>? Skipped = null);
public sealed record SetFile(string FileName, byte[] Bytes);
public interface IAssetModeler
{
    SetModels Options { get; }                     // Skipped ignored here; the writer fills it
    /// Files for one asset named from baseName ("0007" -> "0007.glb"), or null when nothing in it
    /// is fully opaque. Called concurrently for different assets; must be a pure function of its input.
    IReadOnlyList<SetFile>? Model(GeneratedAsset asset, string baseName);
}
```
`SetWriter` (both overloads, identical bytes): inside the existing per-asset `ParallelWork` body, after
the three files, `models?.Model(asset, stem)` → each file to `models/<FileName>` (`CheckPlain`-style
guard: a `FileName` with a separator is an `InvalidOperationException`); a null result writes the
asset's index into a per-asset `bool[] skipped` slot (never a shared list). After the loop:
`Skipped` = recorded skipped (extend) ∪ this run's numbers, ascending, null when empty. Refusals,
checked **before anything is written**, each an `InvalidOperationException` whose message names the
folder and the options:
- existing Set has models, `models` is null → "…was cooked with 3D models (glb, bottom-center, voxel 1); extending it needs the same options";
- existing Set has items and no models, `models` non-null → "…was cooked without 3D models; a Set is modeled whole or not at all";
- both present, `Format`/`Pivot`/`VoxelSize` differ → message names both triples.
`ReadExisting`/`ReadExistingAsync` populate `ExistingSet.Models` from `set.json`. `BuildSetManifest`
writes `Models` only when a modeler ran (or the existing Set had one). An unmodeled Set's `set.json`
must not gain a `"models": null` key — `Json.Options` does NOT ignore nulls (verified at `0e87475`), so
the parameter carries `[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]`, pinned
by `An_unmodeled_set_json_has_no_models_key` (add to Step 1).

- [ ] **Step 1: Failing tests** (a fake modeler inside the test file: one file `"{baseName}.fake"` with
  bytes `[asset.SetNumber]`, null for a configured set of numbers; counts concurrent calls).

```csharp
[Fact] public void A_cook_with_a_modeler_writes_one_model_per_asset_beside_its_image()  // models/0001.fake .. 0003.fake, bytes == [n]
[Fact] public void The_manifest_records_the_model_options()                           // set.json models == ("fake", "center", 0.5f, null)
[Fact] public void A_cook_without_a_modeler_writes_no_models_folder_and_no_block()
[Fact] public void A_fully_transparent_asset_is_skipped_and_recorded()                 // modeler returns null for #2 -> no 0002.*, Skipped == [2]
[Fact] public void Pack_carries_the_models()                                           // .set zip entries include models/0001.fake
[Fact] public void Extending_a_modeled_set_models_only_the_new_assets()               // old model bytes unchanged; new ones written; Skipped merged ascending
[Fact] public void Extending_a_modeled_set_without_a_modeler_is_refused_before_writing() // folder bytes identical before/after
[Fact] public void Extending_an_unmodeled_set_with_a_modeler_is_refused()
[Fact] public void Extending_with_different_options_is_refused_naming_both()
[Fact] public void Sync_and_async_writers_produce_identical_bytes()                   // every file under both folders compared
[Fact] public void A_model_file_name_that_leaves_the_folder_is_refused()               // fake returns "../x"
[Fact] public void ReadExisting_reports_the_recorded_models()
[Fact] public void An_unmodeled_set_json_has_no_models_key()
```
- [ ] **Step 2:** red. **Step 3:** implement. **Step 4:** green; full Core suite (the `SetLayout` include
  list feeds `Pack`, `FilesIn` and `SetExporter.PlanFrom` — every existing Set/export test must stay green).
- [ ] **Step 5: Mutation probes** — drop `ModelsDir` from `Directories` → `Pack_carries_the_models` red;
  remove the before-writing refusal → the extend-without refusal test red. Report.
- [ ] **Step 6: Commit** `feat(core): a Set can carry models/ — writer seam, manifest block, extend rules`.

---

### Task 4: `ModelTokens`, `StackDepth.ForAsset`, `SetModeler`

**Files:**
- Create: `src/Nfty.Model/ModelTokens.cs`, `src/Nfty.Model/SetModeler.cs`
- Modify: `src/Nfty.Model/StackDepth.cs` (`ForAsset`)
- Modify: `src/Nfty.Cli/CommandFactory.Model.cs` (`ModelFormatNames`, `PivotNames`, `PivotOf`,
  `ModelFormatOf` replaced by `ModelTokens` — one mapping)
- Modify: `src/Nfty.App/Rise/RecipeRiseSource.cs` (its `asset.Traits` → `ForSelection` lines → `ForAsset`)
- Test: `tests/Nfty.Model.Tests/SetModelerTests.cs`, `tests/Nfty.Model.Tests/ModelTokensTests.cs`

**Produces:**
```csharp
namespace Nfty.Model;
public static class ModelTokens
{
    public static IReadOnlyList<string> FormatNames { get; }   // "glb", "gltf", "gltf-embedded"
    public static IReadOnlyList<string> PivotNames { get; }    // "bottom-center", "center", "min-corner"
    public static string NameOf(ModelFormat format);  public static string NameOf(Pivot pivot);
    public static ModelFormat FormatOf(string token);  public static Pivot PivotOf(string token); // ArgumentException on unknown
    public static SetModels Record(RiseOptions options);       // tokens + VoxelSize
    public static RiseOptions OptionsOf(SetModels models);
}
public static partial class StackDepth
{
    /// The combined depth of one generated asset; null for an all-absent roll (paper).
    public static DepthMap? ForAsset(LoadedRecipe recipe, GeneratedAsset asset, string source = "the asset");
}
public sealed class SetModeler : IAssetModeler
{
    /// Throws PartialAlphaBookException (new, in Nfty.Model) when Opacity.HasPartialAlpha(book):
    /// "CookBook 'Name' is 2D only: 3 variants have partly transparent pixels (Hat / Ghost, …). A model
    /// needs every pixel fully opaque or fully clear — run nfty inspect <book> --voxel for the list, or
    /// use Make voxel-ready on the Rise page." Lists at most 5 names then "and N more".
    public SetModeler(LoadedCookBook book, RiseOptions options);
}
public sealed class PartialAlphaBookException : InvalidOperationException
{ public string BookName { get; } public IReadOnlyList<VoxelVariant> Variants { get; } }
```
`SetModeler.Model`: recipe by `asset.RecipeId` (ordinal); `ForAsset` → `Riser.TryBuild(asset.Image,
depth, options, $"asset #{asset.SetNumber:D4}")`; null mesh → null; else `ModelWriter.Write(mesh,
asset.Image, options, baseName)` mapped to `SetFile`. Reads only (images are never mutated), so
concurrent calls are safe — the test proves it at 1, 2, 8 threads.

- [ ] **Step 1: Failing tests.**

```csharp
[Fact] public void Tokens_round_trip_every_format_and_pivot()
[Fact] public void ForAsset_matches_ForSelection_over_the_assets_traits()     // byte-equal DepthMap
[Fact] public void ForAsset_is_null_for_an_all_absent_roll()
[Fact] public void A_two_d_only_book_is_refused_by_name()                    // message contains book name and "Hat / Ghost"
[Fact] public void The_model_of_an_asset_is_the_one_nfty_model_writes_for_its_picture() // SetModeler.Model bytes == ModelWriter.Write(Riser.TryBuild(asset.Image, ForAsset(...)), ...) bytes
[Fact] public void Each_asset_rises_with_its_own_stack_depth()               // recipe with a front-3 hat layer; asset with hat: POSITION max z == (0.5+3)*voxel; without: 0.5*voxel
[Fact] public void Models_are_byte_identical_at_any_thread_count()          // Generator.Generate 12 assets; SetWriter.Write with SetModeler under ParallelWork thread caps 1, 2, 8 (the GeneratorParallelismTests mechanism); every models/ file equal
[Fact] public void Every_format_writes_valid_files_into_the_set()            // GltfValidator.AssertValid on each format's files
```
- [ ] **Step 2:** red. **Step 3:** implement; switch the CLI's `model` command and `RecipeRiseSource` to
  `ModelTokens` / `ForAsset` (their existing tests are the regression net: `ModelCommandTests`,
  `RecipeRiseSource*` must stay green unchanged). **Step 4:** green; full suite.
- [ ] **Step 5: Mutation probe** — `ForAsset` reversing the traits (top-first) → the hat test red. Report.
- [ ] **Step 6: Commit** `feat(model): SetModeler rises every asset of a cook; one token mapping`.

---

### Task 5: CLI — `generate --models`, `extend`, `inspect`

**Files:**
- Modify: `src/Nfty.Cli/CommandFactory.cs` (`Generate`, `Extend`), `src/Nfty.Core/Stats/SetReport.cs`
- Test: `tests/Nfty.Cli.Tests/GenerateModelsCommandTests.cs`, extend `InspectSetTests.cs`

**Surface (spec "CLI"; names exactly):**
```
nfty generate <cookbook> --count N --out DIR [--models [--model-format glb|gltf|gltf-embedded]
              [--pivot bottom-center|center|min-corner] [--voxel-size F]]
```
- `--model-format` / `--pivot` / `--voxel-size` **without** `--models` → parse error ("…only with
  --models; without it no model is made") — rejected, never ignored. `--model-format` (not `--format`)
  because `generate` already writes images. Validation reuses the `model` command's voxel-size rule.
- `--models` on a 2D-only book: `SetModeler`'s `PartialAlphaBookException` → `ErrorReport` prints its
  message, exit 1, **nothing written** (the modeler is built before `Generator.Generate`).
- After writing: `Generated 50 → out` plus, when models were made, `models: glb · bottom-center · voxel 1`
  and, when any were skipped, `skipped (nothing fully opaque): #0007, #0012`.
- `extend`: reads `SetWriter.ReadExisting(dir).Models`; non-null → builds `SetModeler(book,
  ModelTokens.OptionsOf(models))` and passes it; prints the same models line. A 2D-only book over a
  modeled Set → the same refusal.
- `SetReport` gains one line under the existing header when `Manifest.Models` is non-null:
  `models     glb · bottom-center · voxel 1 · 2 skipped`.

- [ ] **Step 1: Failing tests** (`Run(...)` / `Parse(...)` helpers as in `ModelCommandTests`):
```csharp
[Fact] public void Generate_with_models_writes_a_model_per_asset()           // models/0001.glb..0003.glb; GltfValidator valid
[Fact] public void Generate_without_models_writes_none()
[Fact] public void Model_options_without_models_are_a_parse_error()          // each of the three, error names the option
[Fact] public void A_two_d_only_book_is_refused_and_nothing_is_written()     // PartialAlphaBookException; out dir absent or empty
[Fact] public void Generate_models_is_byte_identical_twice()
[Fact] public void Extend_inherits_the_recorded_model_options()             // cooked with gltf/center/0.5, extend -> new assets as .gltf+.bin
[Fact] public void Inspect_reports_the_models_line()                         // InspectSetTests
[Fact] public void Generate_prints_skipped_assets()                          // book with an all-clear variant forced as the only roll
```
- [ ] **Step 2:** red. **Step 3:** implement. **Step 4:** green; full suite.
- [ ] **Step 5: Mutation probe** — remove the without-`--models` validator → that test red. Report.
- [ ] **Step 6:** run `dotnet run --project src/Nfty.Cli -- generate src/Nfty.Core/Demo/ChestDemo.cbk
  --count 3 --out <scratch> --models` and `inspect <scratch>`; paste both outputs in the report.
- [ ] **Step 7: Commit** `feat(cli): generate --models; extend keeps the Set's models; inspect says so`.

---

### Task 6: Export — the models axis and stamp isolation

**Files:**
- Modify: `src/Nfty.Core/Publish/ExportOptions.cs` (`public bool Models { get; init; } = true;`,
  `For(Marketplace)` sets `Models = false`), `src/Nfty.Core/Publish/SetExporter.cs` (`PlanFrom` adds
  `SetLayout.ModelsDir` to `wanted` when `options.Models`), `ExportPlan.Parts()` ("3D models (12)")
- Test: extend `tests/Nfty.Core.Tests/SetExporterTests.cs`; create `tests/Nfty.Model.Tests/ExportStampIsolationTests.cs`

Rules: `ExportPlan.Parts()` lists the models part only when files under `models/` are in the plan
(an unmodeled Set never mentions it whatever the flag). `MatchingPreset()` unchanged in shape — the
new field participates, so Marketplace + ticking models is "no preset", as for every other axis.

- [ ] **Step 1: Failing tests.**
```csharp
// SetExporterTests (fixture Set written with the Task 3 fake modeler)
[Fact] public void Models_are_exported_when_asked_and_the_set_has_them()     // plan entries include models/0001.fake
[Fact] public void Models_are_left_out_when_the_box_is_off()
[Fact] public void The_marketplace_preset_leaves_models_out()
[Fact] public void A_set_without_models_never_lists_the_part()               // Parts() has no "3D models"
[Fact] public void A_sealed_export_carries_the_models()                      // SealedSetReader round trip lists models/0001.fake
// ExportStampIsolationTests (real SetModeler, real GLB)
[Fact]
public void A_model_exported_with_the_debug_mark_armed_matches_the_unstamped_asset()
{
    // cook 2 assets (Glb) -> export Folder with NumberWatermark = true, Models = true
    var (json, bin) = GltfValidator.ReadGlb(File.ReadAllBytes(Path.Combine(outDir, "models", "0001.glb")));
    using var texture = Image.Load<Rgba32>(GltfValidator.ReadImage(json, bin));
    using var unstamped = Image.Load<Rgba32>(Path.Combine(setDir, "images", "0001.png"));
    using var stamped = Image.Load<Rgba32>(Path.Combine(outDir, "images", "0001.png"));
    Assert.True(PixelsEqual(texture, unstamped));
    Assert.False(PixelsEqual(stamped, unstamped));   // the stamp really ran — not a vacuous pass
}
```
- [ ] **Step 2:** red. **Step 3:** implement. **Step 4:** green; full suite.
- [ ] **Step 5: Mutation probe** — temporarily make the test's cook use a modeler that textures each
  model from the asset image with `NumberStamp` already applied (what "models built after stamping"
  would produce) → the isolation test goes red; restore. Report.
- [ ] **Step 6: Commit** `feat(core): export carries models/; the debug mark never reaches one`.

---

### Task 7: Cook dialog — *Include 3D models*

**Files:**
- Modify: `src/Nfty.App/ViewModels/CookDialogViewModel.cs`, `src/Nfty.App/Views/CookDialogView.axaml`,
  `Styles.axaml` (only if a class is needed), `ServiceRegistration.cs` (no new deps — `SetModeler` is
  constructed in the VM)
- Test: `tests/Nfty.App.Tests/CookDialogModelsTests.cs`; `VisualCapture.cs` (`Capture_cook_dialog_models`)

**VM surface (new members):**
```csharp
[ObservableProperty] private bool _includeModels;              // the checkbox
[ObservableProperty] private bool _showModelOptions;           // collapsed section, starts false
[ObservableProperty] private ModelFormat _modelFormat = ModelFormat.Glb;
[ObservableProperty] private Pivot _modelPivot = Pivot.BottomCenter;
[ObservableProperty] private double _voxelSize = 1;            // NumericUpDown, FormatString "0.###", Minimum 0.001
public bool BookIsTwoDOnly { get; }                            // Opacity.HasPartialAlpha(book), computed once in the ctor
public string ModelsNote { get; }                              // why it is off, or what it does
```
- **Hidden-and-explained for a 2D-only book:** the checkbox row stays laid out (reserve space), is
  disabled, and `ModelsNote` says `This book is 2D only — 3 variants have partly transparent pixels. Open
  a layer in Rise and use Make voxel-ready.` (counts from `VoxelReport.Scan(book)`). `Cook()` re-checks
  `!BookIsTwoDOnly` before building a modeler (disabled is a label).
- **Extending a modeled Set:** after `ReadExistingAsync`, `existing.Models` non-null forces the run to use
  `new SetModeler(_book, ModelTokens.OptionsOf(existing.Models))` whatever the checkbox says, and the
  result line says `+ models`; an unmodeled Set with the box ticked → the `SetWriter` refusal surfaces
  as the dialog's existing error state (no silent drop). Write: `SetWriter.WriteAsync(set, dir, Pack,
  writeProgress, _cts.Token, models: modeler)`.
- Result text gains ` · 50 models` (and ` · 2 skipped` when any).

- [ ] **Step 1: Failing tests** (real temp folders, `IFilePickerService` stub returning the folder):
```csharp
[AvaloniaFact] public void Ticking_include_models_cooks_a_model_per_asset()
[AvaloniaFact] public void The_options_section_starts_collapsed_and_its_choices_reach_the_files()  // gltf-embedded -> models/0001.gltf only
[AvaloniaFact] public void A_two_d_only_book_disables_the_box_and_says_why()   // IsEnabled false; note names the count and Make voxel-ready
[AvaloniaFact] public void Executing_cook_on_a_two_d_only_book_with_the_box_forced_writes_no_models() // set IncludeModels = true behind the gate
[AvaloniaFact] public void Extending_a_modeled_set_keeps_making_models()
```
- [ ] **Step 2:** red. **Step 3:** implement. Markup: the checkbox row, then a disclosure toggle
  (`Model options`) whose section holds the three controls. A collapsed section that reserved its space
  would not be collapsed, so this is a REVEAL (CLAUDE.md "Second exception = REVEAL"): it uses
  `IsVisible`, may reflow, and sits LAST in the form so only the footer moves. `ModalFitTests` asserts
  the EXPANDED card fits the modal area. **Step 4:** green.
- [ ] **Step 5: Frames** — `Capture_cook_dialog_models` at the modal area (VisualCapture decides from
  the `modal` class): collapsed, expanded, 2D-only; both themes. LOOK; fix; recapture.
  `ModalFitTests` + `DarkModeContrastTests` (add the 2D-only state to `Screens()` — its note is
  the riskiest ink) green.
- [ ] **Step 6: Commit** `feat(app): the Cook dialog can include 3D models`.

---

### Task 8: Export dialog — models row and the IMAGES note

**Files:**
- Modify: `src/Nfty.App/ViewModels/ExportDialogViewModel.cs`, `src/Nfty.App/Views/ExportDialogView.axaml`
- Test: extend `tests/Nfty.App.Tests/ExportDialogLayoutTests.cs`; create `ExportDialogModelsTests.cs`

**VM surface:** `[ObservableProperty] private bool _models = true;` (flows into the options record
exactly as `_images` does), `public bool SetHasModels { get; }` (read once: `SetLayout.ModelsDir`
exists under the Set and holds a file), `public string ModelsTip` (`"3D models of every asset, as
cooked"` / `"This Set was cooked without 3D models."`).

- CONTENTS tab: a fourth content row **"3D models"** after the three existing ones, always laid out,
  disabled + tip when `!SetHasModels` (reserve space, toggle ink).
- IMAGES tab: one muted line under the DEBUG MARK group: `The mark is drawn on exported images only —
  models/ is never stamped.` — inked only when `SetHasModels && NumberWatermark`.
- **Geometry is a measured property of this card** (CLAUDE.md "EXPORT CARD = THREE TABBED PAGES"):
  re-measure each page's want and the pinned block at the modal area; update the page floor and
  `Border.modal.export Border.ship` height **from the measurement**, and the recorded
  `40 / 287 / 100 / 47 = 496` numbers in CLAUDE.md (Task 16 edits the doc; record the new numbers in
  the task report). Card must still fit the 720 minimum; `ExportDialogLayoutTests`' short-window case
  stays meaningful (lower it only if it stops being short, and say so).

- [ ] **Step 1: Failing tests.**
```csharp
[AvaloniaFact] public void The_models_row_is_enabled_only_when_the_set_carries_models()
[AvaloniaFact] public void Unticking_models_drops_them_from_the_manifest_parts()   // Parts no longer lists "3D models"
[AvaloniaFact] public void The_marketplace_tile_unticks_models()
[AvaloniaFact] public void The_images_tab_says_the_mark_never_reaches_models()      // opacity 1 only with models + mark
[AvaloniaFact] public void Every_tab_is_the_same_height_with_the_models_row()       // existing ExportDialogLayoutTests pattern, all three tabs
```
- [ ] **Step 2–4:** red → implement → green (`ExportDialogLayoutTests`, `ModalFitTests` unchanged in
  meaning, numbers updated from measurement).
- [ ] **Step 5: Frames** — all three tabs, modeled and unmodeled Set, both themes, at the modal area.
  LOOK for: the new row's baseline against its siblings, the disabled row's contrast (the sweep floor
  2.0 — swap ink rather than dim muted text), the note not wrapping into the footer.
- [ ] **Step 6: Commit** `feat(app): export dialog offers models/ and says the mark stays off them`.

---

### Task 9: Rise from the Explorer — pane button, recipe action row, lock push, save refresh

**Files:**
- Modify: `src/Nfty.App/ViewModels/ExplorerViewModel.cs`, `IngredientDetailViewModel.cs`,
  `RecipeDetailViewModel.cs`, `src/Nfty.App/Views/IngredientDetailView.axaml`, `RecipeDetailView.axaml`,
  `src/Nfty.App/ServiceRegistration.cs`, `Styles.axaml`
- Test: `tests/Nfty.App.Tests/RiseEntryExplorerTests.cs`, `VisualCapture.cs`

**Wiring (exact):**
- `ExplorerViewModel` ctor gains a LAST optional parameter `Func<RiseSource, RiseViewModel>? riseFactory
  = null`; `ServiceRegistration` passes `sp.GetRequiredService<Func<RiseSource, RiseViewModel>>()`.
- The Rise factory registration wraps the VM like `RememberOnSave`: a new private
  `RememberOnRiseSave(sp, RiseViewModel)` re-records the Recent on `Saved` (same body).
- `private RiseViewModel? _openRise;` + `private void OpenRise(RiseSource source)`: builds via the
  factory, `rise.Saved += OnEditorSaved` (the same refresh an editor save gets — one path), clears
  `_openRise` when the page is disposed (`RiseViewModel` gains `public event Action? Closed;` raised
  first thing in `Dispose`, mirroring the editor's), `_nav.To(rise)`; the opening line is said by the
  shell after navigation (step 7 `SayOpeningLine`) — say nothing here.
- `OnIsEditingChanged` gains: `_openRise?.Source.NotifyLockChanged(); _openEditor?.NotifyLockChanged();`
  (Q9 — `IngredientEditorViewModel.NotifyLockChanged()` = `NotifySaveAvailability()` +
  `OnPropertyChanged(nameof(IsReadOnly))`, internal).
- **Ingredient pane:** `IngredientDetailViewModel` ctor gains LAST optional `Action<string>? rise =
  null`; `[RelayCommand(CanExecute = nameof(CanRise))] private void Rise()` → `_rise?.Invoke(selected
  variant id)`; `CanRise` = a variant is selected and `_rise` is non-null. Explorer passes `variantId =>
  OpenRise(new VariantRiseSource(r, i, variantId, _bridge, _session, () => IsEditing))` — the LIVE
  `r`/`i` read at call time from `_book` by id (never the node's captured graph — see the hoisting
  comments in `OnSelectedNodeChanged`). Button sits in the hero action row beside *Export preview…*,
  `IconRise`, label **Rise**.
- **Recipe pane action row:** the lone die under the hero becomes a row: `Reroll` (existing command,
  die glyph) and **Rise** adjacent, centered as a pair, fixed widths. `RecipeDetailViewModel` ctor gains
  LAST optional `Action<int>? rise = null`; `RiseCommand` → `_rise(RollSeed)`; Explorer passes
  `seed => OpenRise(new RecipeRiseSource(_book, liveRecipe, seed, _bridge))`. The roll Rise shows is
  the hero's roll (step-7 ruling 3) — asserted, not assumed.

- [ ] **Step 1: Failing tests** (Explorer built as the app builds it, real `CookBookSession` on a temp
  `.cbk`, real `RiseViewModel` factory):
```csharp
[AvaloniaFact] public void The_ingredient_pane_rises_the_selected_variant()        // nav.Current is RiseViewModel; Source is VariantRiseSource; SelectedItem.Id == selected variant
[AvaloniaFact] public void The_recipe_pane_rises_the_roll_it_shows()              // Rise.Subject.Art pixels == hero image pixels for the same RollSeed
[AvaloniaFact] public void Rerolling_then_rising_rises_the_new_roll()
[AvaloniaFact] public void Flipping_the_lock_reaches_an_open_rise_page()           // Rise.CanSave flips without any depth edit; footer text changes
[AvaloniaFact] public void Flipping_the_lock_reaches_an_open_editor()               // editor.CanSave / SaveNoteText change on flip (Q9)
[AvaloniaFact] public void A_rise_save_rebuilds_the_tree_and_rerecords_the_recent() // depth chip on the pane refreshes; recents meta re-added
[AvaloniaFact] public void A_rise_opened_on_a_locked_book_cannot_save()
[AvaloniaFact] public void The_rise_and_reroll_buttons_share_one_row()              // same Bounds.Y, centered pair
```
- [ ] **Step 2–4:** red → implement → green; `WiringCoverageTests`, `DeadCodeAuditTests` green.
- [ ] **Step 5: Mutation probes** — delete the `NotifyLockChanged` line → the lock test red; capture the
  node's graph instead of reading `_book` at call time, save twice → the pane shows the stale variant
  (write that test if the existing ones do not catch it). Report.
- [ ] **Step 6: Frames** — Ingredient pane and Recipe pane at 1180 × 720 and the minimum, both themes.
  LOOK: the action row reads as one control group, the Rise glyph at `sm`, nothing in the hero moved.
- [ ] **Step 7: Commit** `feat(app): rise a layer or a roll from the Explorer; the lock and saves reach the page`.

---

### Task 10: Rise from the Ingredient editor — clean gate, depth refresh, stale-art guard

**Files:**
- Modify: `src/Nfty.App/ViewModels/IngredientEditorViewModel.cs`, `src/Nfty.App/Views/IngredientEditorView.axaml`,
  `src/Nfty.App/Rise/VariantRiseSource.cs`, `ServiceRegistration.cs` (both editor factories pass the
  Rise factory)
- Test: `tests/Nfty.App.Tests/RiseFromEditorTests.cs`, extend `tests/Nfty.App.Tests/VariantRiseSourceTests.cs`

**Editor surface:**
- ctor gains LAST optional `Func<RiseSource, RiseViewModel>? rise = null`.
- `[RelayCommand(CanExecute = nameof(CanRise))] private void Rise()`; `CanRise` = `_rise is not null &&
  !IsDirty && !IsSaving && SelectedVariant is not null`; `RiseTip` = `"Save the layer first — Rise reads
  the saved picture."` when dirty, else `"Rise this variant into a model."`. The command re-checks the
  gate (disabled is a label).
- Source: `new VariantRiseSource(_recipe, _ing, SelectedVariant.Id, _bridge, _session, _isEditing,
  _looseSavePath)` — the editor is clean (gate above), so `_ing` IS the saved ingredient.
- `rise.Saved += OnRiseSaved` where `OnRiseSaved(LoadedCookBook book)`: re-point `_ing` at the book's
  ingredient with this id, replace `_depths` with a copy of its `Depths`, `RefreshFromBook(book)`, then
  `Saved?.Invoke(book)` (Q10).
- A loose save raises no book. `VariantRiseSource` gains `public event Action<LoadedIngredient>?
  LooseSaved`, raised with `_looseLive` after the move; the editor subscribes (through the source it
  built) and replaces `_ing` / `_depths` from it.
- **Stale-art guard (Q8b)** in `VariantRiseSource.SaveCoreAsync`, before validating: for every written
  subject, compare `s.Art` to `live.VariantImages[item.Id]` pixel for pixel (`ProcessPixelRows` row span
  `SequenceEqual`); any mismatch → `InvalidOperationException("The picture of Oak, Pine changed after
  Rise opened it; its depth was painted on the old picture and was not saved. Reopen Rise on the new
  picture.")`, nothing written.

- [ ] **Step 1: Failing tests.**
```csharp
[AvaloniaFact] public void The_editor_rises_its_selected_variant()
[AvaloniaFact] public void Rise_is_refused_while_the_editor_has_unsaved_edits()     // CanRise false; executing the command anyway navigates nowhere
[AvaloniaFact] public void Saving_the_editor_after_a_rise_save_keeps_the_rise_depth() // rise: front 2 on a pixel, save; back; editor paints + saves; reopen archive: depth still 2
[AvaloniaFact] public void A_rise_save_refreshes_the_explorer_through_the_editor()
[AvaloniaFact] public void A_loose_rise_save_reaches_the_loose_editor()            // loose .igt path, same as the book test
// VariantRiseSourceTests
[Fact] public void Save_refuses_depth_painted_on_a_picture_that_has_since_changed() // session.Replace with a different image for the variant; SaveAsync throws naming it; archive bytes unchanged
```
- [ ] **Step 2–4:** red → implement → green.
- [ ] **Step 5: Mutation probes** — skip the `_depths` refresh → the keep-depth test red; drop the
  stale-art compare → the refusal test red. Report.
- [ ] **Step 6: Frames** — editor at minimum, both themes, clean and dirty (button dim + tip). The
  editor footer/toolbar must not reflow (`ToolstripLayoutTests`, `PaletteStripLayoutTests` green).
- [ ] **Step 7: Commit** `feat(app): rise from the editor; depth survives the editor's next save`.

---

### Task 11: Rise from the Set browser

**Files:**
- Modify: `src/Nfty.App/ViewModels/SetBrowserViewModel.cs`, `src/Nfty.App/Views/SetBrowserView.axaml`,
  `ServiceRegistration.cs` (browser factory passes `INavigationService`, `IImageBridge` and the Rise factory)
- Test: `tests/Nfty.App.Tests/RiseFromSetBrowserTests.cs`

`SetBrowserViewModel` ctor gains LAST optional `Func<RiseSource, RiseViewModel>? rise = null,
INavigationService? nav = null, IImageBridge? bridge = null`. `[RelayCommand(CanExecute = nameof(CanRise))]
private void Rise()` → `new SetRiseSource(_set, Shown.Number, _bookCandidates, bridge)` → `nav.To(...)`.
`CanRise` = `Shown is not null && rise/nav/bridge non-null` — **sealed Sets included** (view-only;
export refusal is the source's). Button in the page header's control half (CLAUDE.md: left = facts,
right = controls), before Export, `IconRise`. **The Set is borrowed** (`SetRiseSource` does not
dispose it); Back returns to the same browser with the same selection.

- [ ] **Step 1: Failing tests.**
```csharp
[AvaloniaFact] public void Rise_opens_on_the_shown_asset()                    // InitialItem.Id == $"{Shown.Number:D4}"
[AvaloniaFact] public void A_sealed_set_can_be_risen_but_not_exported()      // Source.ExportRefusal non-null
[AvaloniaFact] public void Coming_back_keeps_the_browser_and_its_selection() // set not disposed; SelectedItem same row
[AvaloniaFact] public void The_rise_button_sits_with_the_controls()           // header right half, before Export
```
- [ ] **Step 2–4:** red → implement → green.
- [ ] **Step 5: Frames** — Set browser header at minimum and 1366, both themes; the header row must not
  wrap or push Export (measure ink, not `Bounds`).
- [ ] **Step 6: Commit** `feat(app): rise an asset from the Set browser`.

---

### Task 12: Picture / sheet source, Landing *Rise a picture…*, grid read / detect / rewrite

**Files:**
- Create: `src/Nfty.App/Rise/PictureRiseSource.cs`
- Modify: `src/Nfty.App/Rise/RiseSource.cs` (`RiseSourceKind.Picture`, `RiseSourceKind.Sheet`),
  `src/Nfty.App/ViewModels/LandingViewModel.cs`, `src/Nfty.App/Views/LandingView.axaml`,
  `ServiceRegistration.cs` (Landing gets the Rise factory + `IStatusService`)
- Test: `tests/Nfty.App.Tests/PictureRiseSourceTests.cs`, `tests/Nfty.App.Tests/LandingRiseTests.cs`

**Produces:**
```csharp
namespace Nfty.App.Rise;
public sealed class PictureRiseSource : RiseSource
{
    /// Loads the picture, finds its grid (chunk -> detect -> none), slices cells, and — for a foreign
    /// sheet — writes the grid into the file (Q5). Throws on an unreadable picture.
    public static PictureRiseSource Open(string path, IImageBridge bridge);
    public string Path { get; }
    public SheetGrid? Grid { get; }                  // null = one picture
    public GridOrigin GridOrigin { get; }            // Chunk, Detected, Manual, None
    public string? GridWriteNote { get; }            // "Wrote the grid into sheet.png (+74 bytes)." / "sheet.png is read-only — the grid is kept for this session." / null
    public void ApplyGrid(SheetGrid? grid);          // Task 13 uses it; re-slices, rewrites (Q5)
}
public enum GridOrigin { None, Chunk, Detected, Manual }
```
- Kind `Sheet` when `Grid` is non-null (cells = items in reading order, `RiseItem` id `cell-{n}`, name
  `#{n}`, caption `cell {n} of {Count}`, export base name `{stem}-{n}`), else `Picture` (one item, id
  `picture`, name and export base name = file stem). Cells past `Count` are not items.
- `IsEditable` true, `IsLocked` false, `CanBatch` = sheet, `BatchStem` = `{stem}-models`; export through
  the base `SnapshotExport`. Until Task 13 adds Save as .igt, `CanSave` is false and the footer says
  `depth on loose art is kept for this session` (Task 13 replaces it).
- `IsTwoDOnly` = any cell has a partial pixel (per-item refusal is the existing `IsRisable`).
  `DepthNote` / `OpeningLine` / `Facts`: `"sheet 8 × 4 · 64×64"` plus the origin word
  (`from the file` / `detected` / `entered`).
- The rewrite runs on `Open` only when `GridOrigin == Detected` (a chunk already present is never
  rewritten — no write on every open).
- **Landing:** OPEN column gains **Rise a picture…** (`IconRise`) as its last entry; `RisePictureCommand`
  → `OpenFileAsync("Rise a picture", ".png")` → `PictureRiseSource.Open` off the UI thread through
  `BusyViewModel.RunAsync` (decode + detect + rewrite is file work) → `nav.To(rise)` → after
  navigation, `status.Say(source.GridWriteNote)` when non-null (the page's own opening line first,
  then this — the status is a last-message board, so combine into one sentence:
  `"{OpeningLine} {GridWriteNote}"`). Fire-and-forget → `LandingViewModel.PendingOpen` (existing
  handle) carries it.

- [ ] **Step 1: Failing tests.**
```csharp
[Fact] public void A_plain_picture_opens_as_one_item()
[Fact] public void A_sheet_with_a_chunk_opens_its_cells_and_is_not_rewritten()     // file bytes unchanged
[Fact] public void A_foreign_sheet_is_detected_and_the_grid_written_in()          // Read(path) == detected grid; pixels identical; GridWriteNote names the byte delta
[Fact] public void A_read_only_foreign_sheet_keeps_the_grid_for_the_session()     // file unchanged; note says read-only
[Fact] public void Cells_past_the_count_are_not_items()
[Fact] public void A_partial_alpha_cell_is_not_risable_but_its_neighbors_are()
[Fact] public void Exporting_checked_cells_writes_one_model_each()               // through RiseViewModel export path, folder shape
[AvaloniaFact] public void Landing_rise_a_picture_opens_the_rise_page()          // stub picker returns a temp sheet; await PendingOpen; nav current is RiseViewModel over a Sheet source
[AvaloniaFact] public void Landing_needs_no_scrollbar_at_the_smallest_window()   // existing test stays green with the sixth OPEN entry
```
- [ ] **Step 2–4:** red → implement → green. `RiseViewModel.IsTileRail` already covers non-variant,
  non-recipe kinds — verify the sheet rail renders as tiles (virtualized, step 7 Task 10).
- [ ] **Step 5: Mutation probe** — rewrite on every open (not only `Detected`) → the chunk test red. Report.
- [ ] **Step 6: Frames** — Landing at minimum + 1366 (OPEN column of six; three column heads still on
  one line), Rise over a picture and over a sheet, both themes.
- [ ] **Step 7: Commit** `feat(app): rise a loose picture or a sheet; foreign sheets learn their grid`.

---

### Task 13: Sheet grid row and *Save as .igt*

**Files:**
- Modify: `src/Nfty.App/Rise/PictureRiseSource.cs`, `src/Nfty.App/ViewModels/RiseViewModel.cs` (+ a new
  `RiseViewModel.Grid.cs` partial), `src/Nfty.App/Views/RiseView.axaml`, `Styles.axaml`
- Test: `tests/Nfty.App.Tests/RiseSheetGridTests.cs`, `tests/Nfty.App.Tests/PictureSaveAsIgtTests.cs`

**Grid row** (spec: "A sheet adds a reserved grid row (auto / columns × rows / cell size)"): a segmented
tray `auto · columns × rows · cell size` + two compact numeric cells (`FieldHeightSm`, `MinWidth="0"`,
`FormatString="0"`) + Apply. VM: `GridMode` (`Auto`, `ColumnsRows`, `CellSize`), `GridA`, `GridB`,
`GridProblem` (from `SheetGrid.ProblemWith`, shown in the reserved line under the row, Apply disabled
while non-null), `ApplyGridCommand` (re-checks the problem; if any cell is dirty, asks first with
`ConfirmDialogViewModel` — Q12; then `PictureRiseSource.ApplyGrid`, status says the rewrite note).
The row is reserved on `Picture` and `Sheet` sources only (a picture becomes a sheet by entering a
grid); Set sources never lay it out.

**Save as .igt** (Q11): `PictureRiseSource.CanSave` = true; `SaveTargetName` = `".igt file"`; `SaveAsync`
asks the page for a path through a delegate `Func<Task<string?>> pickPath` passed at construction by
the Landing factory (`picker.SaveFileAsync("Save as Ingredient", ".igt")`), builds the manifest with
`Kind = LayerKind.Custom`, `Colorization = null`, ids/names per Q11, depths from each cell's
`ToDepthMap()`, runs `Validator.ValidateIngredient`, writes with `IngredientArchive.WriteAsync` (tmp +
move, as `VariantRiseSource` does), marks every subject saved. Footer label `Save as .igt…`. The leave
card offers *Save and leave* through the same path.

- [ ] **Step 1: Failing tests.**
```csharp
[AvaloniaFact] public void Entering_columns_and_rows_reslices_and_rewrites()       // 2x1 on a 16x8 picture -> two items; file chunk cols=2
[AvaloniaFact] public void A_grid_that_does_not_divide_disables_apply_and_says_why()
[AvaloniaFact] public void Changing_the_grid_with_unsaved_depth_asks_first()        // dialog stub answers false -> grid unchanged, depth kept
[AvaloniaFact] public void The_grid_row_moves_nothing_when_its_problem_line_appears() // pixel-diff the rail below the row: identical
[Fact] public void Save_as_igt_writes_a_custom_ingredient_with_every_cell_and_its_depth() // IngredientArchive.Read: Custom, N variants, cell-1.., depth bytes equal
[Fact] public void Save_as_igt_for_one_picture_names_the_variant_after_the_file()
[Fact] public void A_canceled_save_dialog_writes_nothing_and_keeps_the_depth_dirty()
[Fact] public void Save_as_igt_is_byte_identical_twice()
```
- [ ] **Step 2–4:** red → implement → green.
- [ ] **Step 5: Frames** — Rise over a sheet: grid row in each mode, with a problem line, both themes,
  minimum window. `TextBaselineTests` on the row.
- [ ] **Step 6: Commit** `feat(app): enter a sheet's grid by hand; save loose depth as an .igt`.

---

### Task 14: *Make voxel-ready* on the Rise page

**Files:**
- Create: `src/Nfty.App/ViewModels/VoxelReadyDialogViewModel.cs`, `src/Nfty.App/Views/VoxelReadyDialogView.axaml(.cs)`
- Modify: `src/Nfty.App/Rise/RiseSource.cs` (`public virtual bool CanMakeVoxelReady => false;`,
  `public virtual Task<VoxelReadyOutcome?> MakeVoxelReadyAsync(...)`, `UndoVoxelReadyAsync()`),
  `VariantRiseSource.cs`, `PictureRiseSource.cs`, `RiseViewModel.Depth.cs`, `RiseView.axaml`
- Test: `tests/Nfty.App.Tests/RiseVoxelReadyTests.cs`

**Behavior (Q7):**
- **Where:** appended at the END of the DEPTH tab's 2D ONLY section (step-7 ruling 10's slot — its
  arrival moves nothing). Inked when `Source.CanMakeVoxelReady`: variant sources (book or loose) and
  picture/sheet sources with any partial pixel. Recipe and Set sources show the listing only — a
  sentence says the fix lives on a layer (`Open a layer of this book in Rise to make it voxel-ready.`).
- **Gate:** `!HasUnsavedDepth && !Source.IsLocked && !IsExporting && !IsSaving`; tip says which
  (`Save or discard depth first.` / `The CookBook is read-only — unlock it in the Explorer.`). Re-checked
  in the command and again in the source (lock).
- **Preview:** `VoxelReadyDialogViewModel(IReadOnlyList<VoxelReadyChange> changes, Func<VoxelReadyChange,
  (Bitmap Before, Bitmap After)> thumbs)` — rows `Layer / Variant · 12 px solid · 3 px clear` with the
  two thumbnails (nearest-neighbor, `RenderOptions.BitmapInterpolationMode="None"`), list scrolls inside
  a capped card (the Validity card precedent: problem count is the book's business), Apply / Cancel.
  Book scope lists every variant from `VoxelReady.Plan(session.Current)`.
- **Apply:** book layer → `VoxelReady.Apply(session.Current)` → `CookBookPersistence.PersistAsync(session,
  result.Book)` → reseed each affected `RiseItem` of this source with the snapped art + its stored depth
  (`Replace` + dispose-after-notify, as reroll does) → raise `Saved(book)` (Explorer + recents refresh,
  Task 9) → keep the `VoxelReadyResult` for undo. Loose `.igt` → same over the one-recipe wrapper,
  written to the `.igt`. Picture/sheet → snap the session art only (no file write; Save as .igt carries
  it).
- **Undo:** the slot then shows `Voxel-ready: 9 variants snapped · Undo` (reserved line, same place),
  `UndoVoxelReadyCommand` → `VoxelReady.Undo(session.Current, result)` → persist → reseed → `Saved`;
  refusal message shown in the error card. The result is disposed on undo or when the page is disposed.

- [ ] **Step 1: Failing tests.**
```csharp
[AvaloniaFact] public void The_button_lists_every_partial_variant_in_the_book_before_applying() // dialog rows == VoxelReady.Plan
[AvaloniaFact] public void Applying_writes_the_snapped_pixels_and_the_book_stops_being_two_d_only() // reopen .cbk: Opacity.HasPartialAlpha false; 127->0, 128->255
[AvaloniaFact] public void Cancel_writes_nothing()                                   // archive bytes unchanged
[AvaloniaFact] public void Undo_restores_the_archive_pixels()
[AvaloniaFact] public void A_locked_book_refuses_even_when_the_command_is_forced()  // execute command with lock closed: bytes unchanged
[AvaloniaFact] public void Unsaved_depth_disables_it_and_says_why()
[AvaloniaFact] public void The_snapped_variant_becomes_risable_on_the_canvas()       // SelectedItem.IsRisableOrUnknown true; Mesh non-null
[AvaloniaFact] public void A_picture_snaps_in_the_session_only()                    // file bytes unchanged; item risable
[AvaloniaFact] public void Its_arrival_moves_nothing_above_it()                      // pixel-diff the rail above the 2D ONLY section with/without the button
```
- [ ] **Step 2–4:** red → implement → green.
- [ ] **Step 5: Mutation probes** — remove the lock re-check in the source → the forced test red; skip
  the reseed → the risable test red. Report.
- [ ] **Step 6: Frames** — DEPTH tab with the button, the dialog (at the modal area), the undo line;
  both themes. `ModalFitTests` covers the new card; `DarkModeContrastTests.Screens()` gains the dialog
  with its longest list (riskiest ink: counts on rows).
- [ ] **Step 7: Commit** `feat(app): make voxel-ready — preview, apply, undo, lock-gated`.

---

### Task 15: 2D-only marks and the opacity-lock warning

**Files:**
- Modify: `src/Nfty.App/Models/ExplorerNode.cs` (`bool IsTwoDOnly` — LAST optional record parameter),
  `ExplorerViewModel.BuildTree` (book: `Opacity.HasPartialAlpha(book)`; ingredient:
  `Opacity.HasPartialAlpha(ing)` — decoded pixels, recomputed, never the stored flag), the tree item
  template (`Icon2dOnly`, `ti` size, `WarningBrush`, tooltip `2D only — has partly transparent pixels`),
  `KitchenShelfViewModel.KitchenCard` (`bool TwoDOnly = false`, from `ArchivePeek`'s stored
  `PartialAlpha` — the listing cache is exactly what it is for), the shelf card template,
  `IngredientEditorViewModel.Palette.cs` (`ToggleOpacityLock` copy)
- Test: `tests/Nfty.App.Tests/TwoDOnlyMarksTests.cs`

**Warning copy (spec: "saving a see-through pixel makes the book 2D-only"; US English):**
title `Allow partial transparency?`, body: `A pixel that is only partly there cannot become a voxel. Save
one, and this CookBook becomes 2D only: it can no longer be cooked into 3D models until every such
pixel is made fully opaque or fully clear (Make voxel-ready on the Rise page does this).\n\nThis
applies to what you paint from here on; nothing already on the canvas changes.`, button `Allow partial
alpha`. Loose `.igt` editor says `this Ingredient` instead of `this CookBook`.

- [ ] **Step 1: Failing tests.**
```csharp
[AvaloniaFact] public void A_partial_alpha_layer_and_its_book_carry_the_mark()     // node.IsTwoDOnly for book + that ingredient only
[AvaloniaFact] public void The_mark_follows_a_save()                                 // voxel-ready (Task 14) or editor save clears it after ApplyBook
[Fact] public void The_kitchen_shelf_marks_a_two_d_only_cookbook()
[AvaloniaFact] public void The_warning_names_the_two_d_only_consequence_before_any_pixel() // dialog text contains "2D only"; OpacityMode still Locked until accepted
[AvaloniaFact] public void The_mark_reserves_its_slot()                              // tree row geometry identical with/without (Opacity, not IsVisible)
```
- [ ] **Step 2–4:** red → implement → green (`DeadCodeAuditTests.Every_icon_is_drawn_somewhere` unaffected).
- [ ] **Step 5: Frames** — Explorer tree with a 2D-only book, Kitchen shelf, the warning dialog; both
  themes; the glyph at `ti` beside the kind mark must not read as a second kind mark (render and look).
- [ ] **Step 6: Commit** `feat(app): mark 2D-only books and layers; the opacity warning says what it costs`.

---

### Task 16: Manual, CLAUDE.md, BACKLOG

**Files (Diátaxis — each page in exactly one quadrant; how-to titled by the reader's goal):**
- Create: `docs/manual/how-to/make-a-3d-model.md` (Rise a layer / a roll / an asset / a picture;
  export one or a batch), `docs/manual/how-to/cook-with-models.md` (Cook dialog + `generate --models`,
  extend), `docs/manual/how-to/fix-a-2d-only-book.md` (listing, Make voxel-ready, undo),
  `docs/manual/how-to/rise-a-spritesheet.md` (grid: from the file / detected / entered; the rewrite),
  `docs/manual/understand/depth.md` (paper + front + back, one solid, why 2D only is derived, why the
  stamp never reaches a model), `docs/manual/reference/rise.md` (page controls table — mouse map as
  shipped: left = front, right = back, Shift lowers, Ctrl constrains)
- Modify: `docs/manual/reference/cli.md` (`model`, `generate --models …`, `extend`), `reference/set-contents.md`
  (`models/`, the `models` block), `reference/file-formats.md` (`depth/*.dep` if absent, `nfty-grid`),
  `reference/opening-screen.md` (*Rise a picture…*), `how-to/soft-edges.md` (cross-link the 2D-only
  consequence), `how-to/share-a-collection.md` (models axis), `reference/glossary.md` (Rise, paper,
  2D only), each `index.md`, `mkdocs.yml` nav
- Modify: `CLAUDE.md` — `Nfty.Model` paragraph (drop "Out of this assembly's scope so far: `generate
  --models`, `models/` in Set, stamp isolation (step 8)"; add `SetModeler` / `IAssetModeler` seam and
  `ModelTokens`), Set paragraph (`SetLayout` include list now four directories), SPRITESHEET paragraph
  (`nfty-grid`), EXPORT CARD numbers (Task 8's measurement), CLI command list (`generate --models`).
- Modify: `BACKLOG.md` — M5 entry updated with Task 17's numbers; new entry for Q16 (`animation_url`).

- [ ] **Step 1:** write pages; `mkdocs build --strict` (if available locally; else state so) — no broken
  links, every new page reachable from nav. **Step 2:** grep the manual for "paid", "free", "premium",
  "Pro" → none. **Step 3: Commit** `docs: Rise in the manual; CLAUDE.md follows step 8`.

Screenshots for the new pages are deliberately NOT taken here — the manual reshoot against the demo
is BACKLOG's (it needs step 9's depth-carrying demo); pages say what to do without figures.

---

### Task 17: Integration pass

**Files:** `tests/Nfty.App.Tests/DarkModeContrastTests.cs`, `MinimumWindowFitTests.cs`,
`TextBaselineTests.cs`, `RiseWiringTests.cs`, `WiringCoverageTests.cs`; create
`tests/Nfty.App.Tests/RiseEntryJourneyTests.cs`, `tests/Nfty.Model.Tests/SetModelsPerfTests.cs`

- [ ] **Step 1: Sweeps.** Contrast `Screens()` gains: cook dialog 2D-only, export CONTENTS with an
  unmodeled Set (disabled row), voxel-ready dialog, Rise over a sheet with a grid problem line. Fit
  `Pages()` gains: Rise over a sheet (grid row), Landing with six OPEN entries. Baselines: the grid row,
  the models row, the recipe action row. `RiseWiringTests` picks up the new commands (it derives them
  by reflection — confirm `RiseWiringTests` fails if `ApplyGridCommand` is unbound). Each sweep
  addition probed once (recolor / unbind) and restored.
- [ ] **Step 2: Journeys** (`RiseEntryJourneyTests`, the `OptionalLayerJourneyTests` shape — start at the
  VM a view binds, cross the real seams, land in real files, reopen them):
  `Pane_rise_save_lands_in_the_cbk_and_cooks_into_the_model` (Ingredient pane → Rise → front 3 on the
  hat → Save → Cook dialog with models → `models/0001.glb` POSITION max z == 3.5);
  `A_two_d_only_book_is_fixed_and_then_cooks_models` (Cook dialog refuses → Rise → Make voxel-ready →
  Cook dialog cooks models); `A_foreign_sheet_is_risen_saved_as_igt_and_its_cells_reopen_with_depth`.
  Probe one far end of each (e.g. zero the depth before `ModelWriter`) → red.
- [ ] **Step 3: Perf (measured, never asserted).** `SetModelsPerfTests` — `[Theory] [InlineData(64)]
  [InlineData(512)]`: cook 50 assets with `SetModeler`, `Perf.Enable()`; report `SetModeler.Model`,
  `Riser.TryBuild`, `ModelWriter.Write`, and the mesh-copy share (Q15). Run with
  `-l "console;verbosity=detailed"`; put the table in the report and in BACKLOG's M5 entry.
- [ ] **Step 4: Full capture set**, both themes; look at every frame this step touched beside its
  step-7 neighbor; fix any divergence; recapture. Archive + refresh the gallery.
- [ ] **Step 5: Drive the real app** (`dotnet run --project src/Nfty.Desktop`): every entry point once,
  cook with models, export with the mark armed, open a foreign sheet copy (never the only copy of a
  file you care about — the rewrite is real). Note anything a test did not catch; fix with a test.
- [ ] **Step 6:** `dotnet build nfty.sln` 0/0; `dotnet test nfty.sln` all five projects green; record
  per-project counts.
- [ ] **Step 7: Commit** `test: step 8 joins the sweeps; entry journeys; models perf measured`.

**Then:** controller runs the **Opus 5.5** whole-step review on `0e87475..HEAD`, applies fixes, runs the
pre-merge conflict/issue check against `main` (user ruling 2026-10-21 — STOP before merging), tags
`rise-step8`.

---

## Self-review (writing-plans)

**Spec coverage.** Entry points table: Ingredient pane / editor (T9, T10), Recipe pane action row (T9),
Set browser (T11, Q1), Landing *Rise a picture…* (T12), Cook dialog with collapsed options and
2D-only hidden-and-explained (T7), Export dialog models axis + IMAGES note (T8), CLI (T4, T5). Set:
`models/` four-pad (T3), `SetLayout.Directories` include list carrying it into `Pack` (T3), `models`
block (T3, Q2), stamp never reaches a model + its test (T6). Spritesheets: chunk written (T2), read /
auto-detect / manual (T2, T12, T13), foreign rewrite with byte size, read-only = session (T2, T12, Q5).
2D only: `generate --models` refuses by name (T4, T5), single item refused by name (existing `Riser`),
editor warns at the toggle (T15), rail glyph on book + items (T15), listing + Make voxel-ready 50% /
undoable / edit-lock gated (T1, T14). "Where depth lives": loose PNG/sheet keeps depth for the session
and in exports, Save as .igt (T12, T13); Set depth by hash (existing `SetRiseSource`, T11). Testing:
stamp (T6), sheet (T2), 2D only (T1, T5, T14). Step-7 carries: (1) lock push (T9), (2) Saved → Explorer,
recents, editor depths (T9, T10), (3) stale art (T10, Q8), (4) editor `IsReadOnly` (T9, Q9). BACKLOG:
M5 measured (T17, Q15).

**Placeholders.** Test bodies not written out carry their assertions in the trailing comment; no
production signature is left open.

**Type consistency.** `SetModels` / `IAssetModeler` / `SetFile` (T3) → `ModelTokens.Record/OptionsOf`,
`SetModeler` (T4) → CLI (T5), Cook dialog (T7). `ExportOptions.Models` (T6) → export dialog (T8).
`SheetGrid` (T2) → `PictureRiseSource` (T12) → grid row (T13). `VoxelReady` / `VoxelReadyChange` /
`VoxelReadyResult` (T1) → T14. `RiseViewModel.Closed` (T9) used by the Explorer only.
`PartialAlphaBookException` (T4) → T5, T7.
