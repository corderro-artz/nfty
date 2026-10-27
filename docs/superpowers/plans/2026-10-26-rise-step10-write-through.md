# Rise Step 10: Write-Through (Option C) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** On the Rise page's recipe source, painting depth on the whole rolled character writes through to the one variant that owns the pixel. A click picks that layer, other layers dim and stop answering the pointer, the 3D tile keeps showing the whole character live, and Save persists every touched variant's `.dep` through `CookBookPersistence` behind the edit lock.

**Architecture:** `StackDepth.Combine` already decides the per-pixel front and back owners. `StackRise` starts exposing them. `RecipeRiseSource` becomes editable when it is given the session and the lock. It keeps one `DepthSurface` per touched variant (keyed by ingredient id and variant id), one page-wide `TargetedHistory`, and a cache of the roll's colorized layer images. After each edit it re-runs the union rule into the subject's existing surface, so the mesh, the faces and the atlas are the whole character. The page paints through a small `IDepthEditor` seam. The variant, picture and Set sources keep today's single-surface behavior behind the default implementation. The canvas draws the target layer at full strength over and under two dimmed `StackPreview` composites, split around it with `StackPreview.Split`. Save is one `UpsertIngredient` per dirty layer, then ONE `PersistAsync`, raised to the Explorer, the recents and the editor through the existing `Saved` path.

**Tech Stack:** .NET 10, xunit.v3, Avalonia 12.1.1 (headless for tests and frames), ImageSharp 3.1.11 (pinned). No new packages.

**Spec:** `docs/superpowers/specs/2026-09-25-rise-design.md`, sections "Write-through — option C, last" and "The stack rule — union of solids (amended 2026-10-26)". The page rules come from "The Rise page". History: the step 7, 8, 9 and 9b plans in `docs/superpowers/plans/`, and the ledgers in `.superpowers/sdd/2026-10-2*/progress.md`.

## The ownership ruling (decided in this plan; Q0)

**A front stroke picks the pixel's FRONT owner. A back stroke picks its BACK owner. It is never "the topmost solid layer".** Front and back are the owners `StackDepth.Combine` already computes: the topmost layer reaching the max front, and the topmost layer reaching the max back, with ties going to the upper layer.

Why per-button owners, not the topmost solid layer:

1. **Under the union rule, only the owner's map moves the face the stroke is about.** Take a strap at back 0 over a body at back 9. Right-clicking the strap and editing the strap's back from 0 to 1 changes nothing anywhere: the union back is still 9. A gesture with no visible effect is a button that does nothing, which the house rules forbid. The body owns that back, so the body is what a back stroke there must reach.
2. **It is the spec's own amendment** ("the owner of a front stroke is the pixel's front owner, of a back stroke its back owner"). The topmost rule in the original bullet predates the union rule.
3. **It degrades to the old rule exactly where the old rule was right.** Ties go upward, so on an all-paper stack both buttons pick the topmost solid layer, the layer whose 2D art the author sees.
4. **Picking happens once.** Once a layer is targeted, the button chooses only the SIDE of that layer's own map. Left-clicking to pick the strap and then right-dragging edits the strap's back on purpose, even where the body owns the union back. The header says which variant is being edited, so the invisible case is a stated choice, not a surprise.

## Global Constraints

- `Directory.Build.props`: `TreatWarningsAsErrors` + `GenerateDocumentationFile`. Every public member is documented. Each task leaves `dotnet build nfty.sln` green.
- Zero new packages. `Nfty.Model` references `Nfty.Core` only. `Nfty.Core`/`Nfty.Ticket` never reference `Nfty.Model` (`AssemblyRulesTests`).
- **Stack rule unchanged:** front = max and back = max over solid layers, owner = topmost reaching the max, ties go upward, partial alpha refused naming the layer. Write-through edits ONE layer's own map. It never writes a union map anywhere.
- **Mouse map as shipped:** left = front +1, right = back +1, Shift = −1 (floor 0), Ctrl = constrain (45° lines, square shapes), Alt = from center, middle-drag = pan. On a recipe with no target, the same buttons PICK, and Shift and Ctrl are ignored.
- **Never from a cooked Set.** `SetRiseSource` stays `IsEditable == false`, and its `SaveAsync` keeps throwing `NotSupportedException`.
- **Lock re-checked inside the command** (`RecipeRiseSource.SaveAsync` and `RiseViewModel.SaveRunAsync`), never only at `CanExecute`. A disabled button is a label.
- **One in-flight write per source:** a second Save joins the running task (`VariantRiseSource`'s rule). Reroll and target changes are refused while a save runs.
- Depth never enters DNA. The demo still counts **7,155,000** (step 9b ledger).
- Reserve space, toggle ink. The target header row is always laid out: `Opacity`/text change only, never `IsVisible` on anything that occupies layout.
- All figures are invariant culture. US English. Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Tests are xunit.v3, `Snake_case_sentences`, with fixtures in memory (`RiseFixtures.ChestBook`: recipe `chest` = `[body, lock]`, body `oak` (weight 3) / `ghost` (1), lock `gold`). Temp folders come from `Directory.CreateTempSubdirectory()` and are deleted in `finally`.
- Test cadence: targeted filters per task. The full suite runs once, in Task 5.

## Open Questions (controller rules; recommendation first)

- **Q1: Does the picking click also paint?** Recommend **no**. The click that picks changes no depth and pushes no history. Painting a +1 onto a layer the author had not seen they were choosing is the surprise the header exists to prevent. Cost: one extra click per layer switch.
- **Q2: How does the author change the target?**
  - Recommend: a **layer rail row** (now a button); **Escape** drops the target and returns to picking (after clearing a marquee, if there is one); **PageUp / PageDown** step the target one present layer up or down the stack.
  - Recommend NOT a click on a dimmed pixel. The spec makes dimmed layers "not clickable", and a click that sometimes paints and sometimes retargets is two gestures with one shape.
- **Q3: Where does the reach count come from, and how is it worded?** Recommend **`RarityCalculator`'s `TraitOdds.OverallPercent`** for (recipe, layer, variant). That is the book-wide share, absence folded in, the number the Ingredient pane already prints. Multiply it by `CookBookManifest.TargetSupply` when the book has one.
  - Wording: `in ~900 of 1,200 assets`. Exactly 100% prints `in all 1,200 assets`. With no target supply it prints `in 75% of assets` (`RarityText.Percent`, so `<0.01%` floors), and 100% prints `in every asset`.
  - The tooltip says it is the expected share from the weights, and adds "rules can change it" when the recipe has rules.
  - Not `UniqueSpace`: it counts possible DNA (millions), not minted assets, and would need a new pinned-variant count.
  - Not the Set: none exists on a recipe source, and the spec forbids writing through from one.
  - Not `SelectionOdds`: it prices a whole selection, not a variant's marginal.
  - The `~` is the house rule about exact-looking figures: an expected count is never exact.
- **Q4: What does reroll do with unsaved depth?** Recommend **keep it**.
  - Surfaces are keyed by `(ingredientId, variantId)` and outlive the roll, so a painted strap reappears on every roll that picks it. That is the reason to reroll mid-edit.
  - The history survives too. The footer counts every dirty variant, rolled or not. A reroll that drops the target's variant clears the target, and the page returns to picking.
  - Reroll is refused only while a save runs.
- **Q5: Undo across layers.** Recommend **one page-wide history**. Undo and redo act on whichever variant the step touched and retarget to it when that variant is in the current roll, so the change is on screen. When it is not in the roll, the step still applies and the status line says so: `Undid depth on Lock / Gold — not in this roll.` Silently skipping it would corrupt the stack order.
- **Q6: What do the DEPTH rail and the canvas labels describe?**
  - With a target, recommend **the target variant**: fill-all applies to the target's solid pixels as one undo step, the THICKNESS histogram, the cell labels and the readout all describe the target's own map. These are the tools that edit it.
  - With no target, recommend **the union**, read-only, as today.
  - The 3D tile always shows the whole character.
- **Q7: How dim, and how drawn?**
  - Recommend the composites below and above the target at **Opacity 0.3**, as a style class `Image.rdim` in `Styles.axaml` (opacity is not a color, so it gets no token), with the target at full strength between them.
  - Three `Image`s in the existing canvas host cell share one `Stretch`/zoom transform, so they line up by construction.
  - Hit-testing stays on the untransformed `CanvasSurface`. The VM refuses presses on non-target pixels. The images are not what decides hits.
- **Q8: What happens when the book is locked?** Recommend the variant source's rule: **painting stays live as scratch, and Save refuses**. The header still names the target, the footer says why Save is dim, and the leave card offers Discard and Stay only (`canSave: !IsLocked`).
- **Q9: What does a Save write?** Recommend **every dirty variant in ONE `PersistAsync`**: `UpsertIngredient` once per touched layer, then one write. Partial writes are worse than none. A stale-art or deleted-variant guard refuses the WHOLE save, naming every offender, exactly as `VariantRiseSource.SaveCoreAsync` does.

## Review Focus

1. **The target's variant leaves the roll on reroll.** The target clears, the page shows picking, the edits remain dirty, and Save still writes them. Pinned in Task 2 (`A_reroll_that_drops_the_targets_variant_keeps_its_depth_and_clears_the_target`).
2. **The picture changed under painted depth** (another route rewrote the variant since the roll). Save refuses the whole write, naming the variant, and the file stays unchanged. Pinned in Task 2 (`A_save_refuses_when_a_painted_variants_picture_changed`).
3. **The lock closes between painting and saving.** `NotifyLockChanged` dims Save at once, and a forced `SaveAsync` writes nothing. Pinned in Task 4 (`The_lock_refuses_write_through_even_when_forced`).
4. **A tie on an all-paper stack.** Both buttons pick the topmost solid layer. Pinned in Task 2 (`On_a_paper_stack_both_buttons_pick_the_topmost_solid_layer`).
5. **A stroke dragged across dimmed pixels.** Only the target's solid pixels change. A press that STARTS on a dimmed pixel does nothing at all: no history entry, no dirty flag, no retarget. Pinned in Tasks 3 and 4 (`A_press_on_a_dimmed_pixel_does_nothing`, `A_dimmed_layer_cannot_be_hit_through_the_view`).

---

## File map

| File | Change |
|------|--------|
| `src/Nfty.Model/StackRise.cs`, `StackDepth.cs` | owners per pixel exposed |
| `src/Nfty.Core/Editing/DepthSurface.cs` | `Overwrite(DepthMap?)` |
| `src/Nfty.Core/Editing/TargetedHistory.cs` | NEW: one undo stack over many surfaces |
| `src/Nfty.Core/Stats/VariantReach.cs` | NEW: the header's reach figure and wording |
| `src/Nfty.App/Rise/IDepthEditor.cs` | NEW: the page's paint seam + `SubjectDepthEditor` |
| `src/Nfty.App/Rise/RiseSource.cs` | `Saved` event, `DirtyCount`, `EditorFor` lifted to the base |
| `src/Nfty.App/Rise/VariantRiseSource.cs` | uses the base `Saved` |
| `src/Nfty.App/Rise/RiseSubject.cs` | `ReplaceCombined` |
| `src/Nfty.App/Rise/RiseLayerRow.cs` | observable, ids, `IsTarget`, `IsPickable` |
| `src/Nfty.App/Rise/RecipeRiseSource.cs` | write-through |
| `src/Nfty.App/ViewModels/RiseViewModel*.cs` | editor seam, pick, dim bitmaps, header, keys |
| `src/Nfty.App/Views/RiseView.axaml(.cs)`, `Themes/Styles.axaml` | three-image canvas, header chip, rail buttons |
| `src/Nfty.App/ViewModels/ExplorerViewModel.cs`, `IngredientEditorViewModel.cs` | wiring, editor reload |
| `src/Nfty.App/Services/KeyHints.cs` | PageUp/PageDown, Escape wording |

---

### Task 1: Engine pieces — owners, overwrite, targeted history, reach

**Files:**
- Modify: `src/Nfty.Model/StackRise.cs`, `src/Nfty.Model/StackDepth.cs` (store the `ownF`/`ownB` it already computes)
- Modify: `src/Nfty.Core/Editing/DepthSurface.cs`
- Create: `src/Nfty.Core/Editing/TargetedHistory.cs`, `src/Nfty.Core/Stats/VariantReach.cs`
- Test: `tests/Nfty.Model.Tests/StackDepthTests.cs`, `tests/Nfty.Core.Tests/DepthSurfaceTests.cs`, `tests/Nfty.Core.Tests/TargetedHistoryTests.cs` (new), `tests/Nfty.Core.Tests/VariantReachTests.cs` (new)

**Interfaces:**
- Produces:
  - `int StackRise.FrontOwner(int x, int y)`, `int StackRise.BackOwner(int x, int y)`: the bottom-first index into the `Combine` list, or −1 on a non-solid pixel. `ForSelection`/`ForAsset` index into the PRESENT picks in the order given.
  - `void DepthSurface.Overwrite(DepthMap? map)`: every solid pixel takes the map's pair (null = 0/0). It throws `ArgumentException` on a size mismatch. Non-solid pixels stay 0. The surface instance is unchanged, so references to it stay valid.
  - `sealed class TargetedHistory<TKey, TPixel> where TKey : notnull where TPixel : struct`:
    - `bool Do(TKey key, IEditCommand<TPixel> cmd, IEditSurface<TPixel> surface)`: no-op edits are not recorded, and redo is cleared, as in `EditHistory`.
    - `bool CanUndo`, `bool CanRedo`.
    - `TKey? PeekUndo`, `TKey? PeekRedo`.
    - `bool Undo(Func<TKey, IEditSurface<TPixel>> surfaceOf, out TKey key)`, `bool Redo(Func<TKey, IEditSurface<TPixel>> surfaceOf, out TKey key)`.
    - `void Clear()`.
  - `sealed record VariantReach(double Percent, int? TargetSupply, bool RecipeHasRules)` with `static VariantReach Of(LoadedCookBook book, string recipeId, string ingredientId, string variantId)`, `string Text`, `string Tip`. Wording per Q3. `Of` takes `TraitOdds.OverallPercent` from `RarityCalculator.Compute`. An unknown id gives `Percent = 0`, never a throw (reports degrade, never throw).

- [ ] **Step 1: Write the failing tests.**

```csharp
// StackDepthTests
[Fact] public void Owners_are_the_topmost_layers_reaching_each_max()
// body (1,9) under strap (3,0) at (2,2): FrontOwner == 1 (strap), BackOwner == 0 (body); a clear pixel → -1, -1
[Fact] public void On_a_paper_stack_both_owners_are_the_topmost_solid_layer()
// two paper layers overlapping at (1,1): FrontOwner == BackOwner == 1; body-only pixel → 0, 0

// DepthSurfaceTests
[Fact] public void Overwrite_sets_solid_pixels_and_keeps_the_instance()
// surface over a 2x2 with one clear pixel; Overwrite(map with (5,6) everywhere): solid → (5,6), clear → (0,0), ToDepthMap round-trips; Overwrite(null) → ToDepthMap() == null
[Fact] public void Overwrite_refuses_a_map_of_another_size()

// TargetedHistoryTests
[Fact] public void Undo_restores_the_surface_the_step_touched_and_names_it()
// Do("a", step, sa); Do("b", step, sb); Undo → key "b", sb restored, sa unchanged; Undo → "a"
[Fact] public void A_no_op_edit_is_not_recorded()
[Fact] public void A_new_edit_clears_redo()

// VariantReachTests (RiseFixtures-shaped book built locally: oak 3, ghost 1; lock gold 1; recipe 100%)
[Fact] public void Reach_with_a_target_supply_is_an_expected_count()
// TargetSupply 1200, oak → Text == "in ~900 of 1,200 assets"; gold → "in all 1,200 assets"
[Fact] public void Reach_without_a_target_supply_is_a_share()
// oak → "in 75% of assets"; gold → "in every asset"
[Fact] public void Reach_is_invariant_under_de_DE()
[Fact] public void Reach_tooltip_mentions_rules_only_when_the_recipe_has_them()
```

- [ ] **Step 2: Run them to see them fail.**
Run: `dotnet test tests/Nfty.Core.Tests --filter "FullyQualifiedName~TargetedHistory|FullyQualifiedName~VariantReach|FullyQualifiedName~DepthSurface"` and `dotnet test tests/Nfty.Model.Tests --filter FullyQualifiedName~StackDepthTests`
Expected: compile errors or FAIL.

- [ ] **Step 3: Implement** the interfaces above.
  - Owners are two `int[]` filled in `Combine`'s existing loop, initialized to −1.
  - `VariantReach.Text`: the count is `Math.Round(Percent / 100 * supply, MidpointRounding.AwayFromZero)`, formatted `N0`, invariant.

- [ ] **Step 4: Run the tests.** Same commands. Expected: PASS. Then `dotnet build nfty.sln`: 0 warnings.

- [ ] **Step 5: Probe.** Make `Combine` record the owner with `>=` instead of `>`, so ties go to the lower layer. `On_a_paper_stack_both_owners_are_the_topmost_solid_layer` must go red. Restore it.

- [ ] **Step 6: Commit.** `feat(rise): stack owners, depth overwrite, targeted history and variant reach`

---

### Task 2: `RecipeRiseSource` writes through

**Files:**
- Create: `src/Nfty.App/Rise/IDepthEditor.cs`
- Modify: `src/Nfty.App/Rise/RiseSource.cs`, `VariantRiseSource.cs`, `RiseSubject.cs`, `RiseLayerRow.cs`, `RecipeRiseSource.cs`
- Test: `tests/Nfty.App.Tests/RecipeRiseWriteThroughTests.cs` (new). Existing `RecipeRiseSourceTests` stay green.

**Interfaces:**
- Consumes: Task 1's `FrontOwner`/`BackOwner`, `Overwrite`, `TargetedHistory`, `VariantReach`. Also `StackDepth.Combine(pairs, source, finished)`, `StackPreview.Split`/`Render`, `CookBookEdits.UpsertIngredient`, `CookBookPersistence.PersistAsync`, `Validator.ValidateIngredient`.
- Produces:
  - `interface IDepthEditor`:
    - `DepthSurface Surface`.
    - `bool Do(IEditCommand<DepthPixel> cmd)`.
    - `bool CanUndo`, `bool CanRedo`, `void Undo()`, `void Redo()`.
    - `void Sync()`: brings the subject's union up to date after a standing preview. A no-op for a single picture.
  - `sealed class SubjectDepthEditor(RiseSubject subject) : IDepthEditor`: today's `Subject.History` behavior, verbatim.
  - `RiseSource`:
    - `public event Action<LoadedCookBook>? Saved` plus `protected void RaiseSaved(LoadedCookBook book)`. These move up from `VariantRiseSource`; its subscribers keep the same name.
    - `public virtual int DirtyCount => Items.Count(i => i.IsDirty)`.
    - `public virtual IDepthEditor EditorFor(RiseSubject subject) => new SubjectDepthEditor(subject)`. Cache it per subject, so it is the same instance until the subject changes.
  - `IDisposable RiseSubject.ReplaceCombined(DepthMap? union, StackFaces? faces)`: `Depth.Overwrite(union)`, rebuilds `Texture` from `faces`, keeps `History` and the `Depth` instance, then `MarkSaved(union)`. A derived union is never "unsaved work". It returns the retired texture and faces.
  - `RiseLayerRow : ObservableObject` gains `string IngredientId`, `string? VariantId` (null when absent), `bool IsPickable`, and `[ObservableProperty] bool IsTarget`.
  - `RecipeRiseSource`:
    - ctor gains `ICookBookSession? session = null, Func<bool>? isEditing = null`.
    - `IsEditable => session is not null && Problem is null && refusal is null`.
    - `IsLocked => isEditing is not null && !isEditing()`.
    - `ReadOnlyPill => IsEditable ? null : "combined depth · read-only"`.
    - `RiseLayerTarget? Target` (`record RiseLayerTarget(string IngredientId, string VariantId, string Name, DepthSurface Surface)`, where `Name` is `"Lock / Gold"`).
    - `bool TryPick(int x, int y, DepthSide side)`: Q0's owners. False on a clear pixel or when not editable.
    - `bool SetTarget(string? ingredientId)`: null drops the target. False for an absent, unknown or not-editable layer, or while saving.
    - `bool StepTarget(int delta)`: PageUp is +1, toward the top.
    - `VariantReach? TargetReach`.
    - `bool IsTargetSolid(int x, int y)`.
    - `Image<Rgba32>? BelowArt`, `Image<Rgba32>? TargetArt`, `Image<Rgba32>? AboveArt`: owned by the source, rebuilt on target change or reroll, all three null without a target.
    - `override int DirtyCount`, `override bool IsDirty`, `override bool CanSave`, `override Task SaveAsync()`.
    - `override IDepthEditor EditorFor(RiseSubject)` returns the write-through editor. Its `Surface` is the target's (or the union when there is no target, with `Do` refusing). `Do`, `Undo` and `Redo` go through the page-wide `TargetedHistory<(string Ing, string Variant), DepthPixel>` and call `Recombine()`.
    - `string? LastUndoNote`: Q5's off-roll sentence, or null.

- [ ] **Step 1: Write the failing tests.** Use `RiseFixtures.ChestBook` with oak at (front 1, back 9) everywhere and gold at (2, 0), an in-memory `CookBookSession`, and the `isEditing` toggle.

```csharp
[Fact] public void Left_picks_the_front_owner_and_right_picks_the_back_owner()
// at a gold pixel: TryPick(Front) → Target.Name == "Lock / Gold"; TryPick(Back) → "Body / Oak"
[Fact] public void On_a_paper_stack_both_buttons_pick_the_topmost_solid_layer()
[Fact] public void Picking_changes_no_depth()
// TryPick → DirtyCount == 0, EditorFor(subject).CanUndo == false
[Fact] public void An_edit_changes_only_the_targets_own_map_and_the_union_follows()
// target gold, Do(DepthStep({gold px}, Front, +1)): gold surface (3,0); oak untouched; subject.Depth at that px == (3,9); subject.IsDirty == false; DirtyCount == 1
[Fact] public void A_stroke_over_dimmed_pixels_changes_only_the_targets_solid_pixels()
// target gold, DepthStep over a body-only pixel and a gold pixel → only the gold pixel moved
[Fact] public void Undo_crosses_layers_and_retargets_to_the_layer_it_touched()
// edit gold, edit oak, Undo → Target == oak, oak restored; Undo → Target == gold
[Fact] public void A_reroll_keeps_unsaved_depth_on_its_variant()
// edit gold, Reroll() → gold still dirty, and its depth is back in the union when gold is rolled
[Fact] public void A_reroll_that_drops_the_targets_variant_keeps_its_depth_and_clears_the_target()
// withGhost, target oak; reroll until ghost: Target == null, DirtyCount == 1
[Fact] public void Save_writes_every_dirty_variant_in_one_persist_and_raises_saved_once()
// edit gold + oak; await SaveAsync(); session.Current gold/oak Depths equal the surfaces; ghost absent; Saved raised once; DirtyCount == 0
[Fact] public void A_second_save_while_one_runs_joins_it_and_reroll_and_retarget_are_refused()
// gate PersistAsync via a session whose Replace awaits a TCS; second SaveAsync returns the same Task; SetTarget returns false; Reroll changes no seed
[Fact] public void A_locked_source_refuses_save_and_writes_nothing()
// isEditing false: SaveAsync completes; session.Current is the same reference; DirtyCount unchanged
[Fact] public void A_save_refuses_when_a_painted_variants_picture_changed()
// edit gold; replace the session book with gold's image changed; SaveAsync faults, message names "Gold", session book unchanged
[Fact] public void After_a_save_the_source_reads_the_live_book()
// save, then edit again and save again: second persist starts from the first save's book (oak's first-save depth kept)
[Fact] public void Without_a_session_the_source_stays_read_only()
// old ctor: IsEditable false, ReadOnlyPill unchanged, TryPick false
```

- [ ] **Step 2: Run them to see them fail.**
Run: `dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~RecipeRiseWriteThrough`
Expected: FAIL / compile errors.

- [ ] **Step 3: Implement.**
  - **Roll cache.** Per roll, keep the present picks bottom-first, each with its colorized layer image (`VariantPreview.Render` with the rolled color, or the stored image) and the finished art. `Recombine()` calls `StackDepth.Combine(cached layers paired with each layer's CURRENT map, source, finished)`. The current map is the dirty surface's `ToDepthMap()`, else the live ingredient's stored map. The result goes into `subject.ReplaceCombined(rise.Depth, rise.Faces.Clone())`. Keep the returned retired value ONE step deep, and dispose it at the start of the next `Recombine` and in `Dispose`. The page re-reads `Subject.Texture` after the rebuild, so the old texture must outlive the notification (the reroll rule: "the old pictures go only AFTER the page has been told"). Pin it: `A_recombine_keeps_the_previous_texture_alive_until_the_next` (the old texture is not disposed after one edit, and is disposed after two).
  - **Surfaces.** Each is built over the variant's STORED image (stable across rerolls). Fill's same-color region partitions the same as the colorized pixels, because colorizing keeps value.
  - **Owners.** From the last `StackRise`'s `FrontOwner`/`BackOwner`, mapped to the roll's present picks.
  - **Dim art.** `StackPreview.Split(recipe.Manifest, targetIngredientId, presentIds)` gives the split. Render it with `PreviewLayer(ing, variant, null, rolled)`. `TargetArt` is the target's colorized image.
  - **Save.** Mirror `VariantRiseSource.SaveAsync`/`RunSaveAsync`/`SaveCoreAsync`: the lock is checked first, then `_inFlight` joins, then `_writeBusy` refuses. Validate each touched ingredient, then guard deleted variants and stale pictures across ALL touched variants before any write. Then one `UpsertIngredient` per layer and ONE `PersistAsync`. Mark the written snapshots saved, set `_book`/`_recipe` to the session's live graph by id, `RaiseSaved(book)`, then `RaiseChanged()`.

- [ ] **Step 4: Run the tests.**
Run: `dotnet test tests/Nfty.App.Tests --filter "FullyQualifiedName~RecipeRise|FullyQualifiedName~VariantRiseSource|FullyQualifiedName~RiseSave"`
Expected: PASS. Then `dotnet build nfty.sln`.

- [ ] **Step 5: Probe.**
  - Make `TryPick(Back)` use the front owner. `Left_picks_the_front_owner_and_right_picks_the_back_owner` must go red.
  - Drop the stale guard. `A_save_refuses_when_a_painted_variants_picture_changed` must go red.
  - Restore both.

- [ ] **Step 6: Commit.** `feat(rise): recipe source writes depth through to the owning variant`

---

### Task 3: The page paints through the target

**Files:**
- Modify: `src/Nfty.App/ViewModels/RiseViewModel.cs`, `RiseViewModel.Depth.cs`, `RiseViewModel.Save.cs`
- Test: `tests/Nfty.App.Tests/RiseWriteThroughPageTests.cs` (new). Existing `RiseViewModelTests`, `RiseGestureTests`, `RiseDepthRailTests` and `RiseSaveTests` stay green.

**Interfaces:**
- Consumes: Task 2's `IDepthEditor`, `RecipeRiseSource` API, `RiseSource.Saved`/`DirtyCount`.
- Produces on `RiseViewModel`:
  - `IDepthEditor Editor` (`Source.EditorFor(Subject)`). `Surface => Editor.Surface`. Every `Subject.History` use and every direct `Surface` edit (strokes, standing preview, fill-all, undo, redo) goes through `Editor`. `RebuildMesh` calls `Editor.Sync()` before building, inside the existing 33 ms budget.
  - `bool IsWriteThrough` (a recipe source that is editable).
  - `bool IsPicking` (`IsWriteThrough && Target is null`).
  - `bool PickAt(int x, int y, DepthSide side)`.
  - `bool IsHittable(int x, int y)` (not write-through → true; picking → solid in the union; targeted → `IsTargetSolid`).
  - `Bitmap? BelowCanvas`, `Bitmap? AboveCanvas`. `Canvas` is the target art when targeted, else the composite.
  - `string TargetHeader`: `"click a pixel to choose its layer"` / `"editing Lock / Gold · in ~900 of 1,200 assets"` / `""` when not write-through.
  - `string TargetHeaderTip` (`VariantReach.Tip`).
  - `bool ShowTargetHeader => IsWriteThrough`.
  - `[RelayCommand] void TargetLayer(RiseLayerRow row)`, `[RelayCommand] void StepTarget(int delta)`.
  - `[RelayCommand] void Escape()`: drops the marquee if there is one, else the target.
  - `ApplyDepthStroke`/`PreviewDepthStroke` return early when the press point fails `IsHittable`. While `IsPicking`, the press goes to `PickAt` and the stroke paints nothing.
  - `DirtyCount => Source.DirtyCount`.
  - `Saved` forwards `Source.Saved` for every source, not only `VariantRiseSource`.
  - The status line says `LastUndoNote` after undo or redo when it is set.

- [ ] **Step 1: Write the failing tests** (VM over `RecipeRiseSource` with a session, `FakeNav`, `RiseVoxelReadyTests.ScriptedDialogs`).

```csharp
[AvaloniaFact] public void The_first_click_picks_and_the_next_stroke_paints_the_target()
// Apply at gold px (Front) → TargetHeader starts "editing Lock / Gold"; no dirty; Apply again → gold +1, DirtyCount 1
[AvaloniaFact] public void A_press_on_a_dimmed_pixel_does_nothing()
// target gold; Preview + Apply starting on a body-only px → DirtyCount 0, CanUndo false, target still gold
[AvaloniaFact] public void The_3D_tile_shows_the_whole_character_while_a_layer_is_targeted()
// target gold, +1 front: Mesh volume == Σ(1 + unionF + unionB) over the composite's solid px (oak pixels included)
[AvaloniaFact] public void The_reach_header_names_the_target_and_its_reach()
// book TargetSupply 1200, pick oak via right-click → TargetHeader == "editing Body / Oak · in ~900 of 1,200 assets"
[AvaloniaFact] public void Fill_all_with_a_target_fills_only_that_variant()
// target gold, FillFront 4 / FillBack 1, FillAll → gold surface all (4,1); oak unchanged; one undo restores
[AvaloniaFact] public void Escape_drops_the_marquee_then_the_target()
[AvaloniaFact] public void PageUp_and_PageDown_step_the_target_through_present_layers()
[AvaloniaFact] public void Leaving_with_unsaved_write_through_depth_asks_first()
// edit gold, BackCommand → dialog shown with "unsaved depth on 1 variant"; Stay keeps the page; Discard navigates back
[AvaloniaFact] public void Undo_of_a_step_off_the_roll_says_so()
[AvaloniaFact] public void A_set_source_never_picks_or_writes()
// SetRiseSource VM: IsWriteThrough false; Apply → nothing; ShowSave false; Source.SaveAsync throws NotSupportedException
```

- [ ] **Step 2: Run them to see them fail.**
Run: `dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~RiseWriteThroughPage`
Expected: FAIL.

- [ ] **Step 3: Implement** per the Interfaces. `OnSourceChanged` keeps its surface-identity check against `Editor.Surface`. Target changes raise a property change for `Surface`, `Canvas`, `BelowCanvas`, `AboveCanvas`, `TargetHeader`, `LayerRows`, the DEPTH rail and the commands, without `AdoptSubject`, so the camera does not reset.

- [ ] **Step 4: Run the tests.**
Run: `dotnet test tests/Nfty.App.Tests --filter "FullyQualifiedName~Rise"`
Expected: PASS (all Rise tests: the seam must not change variant, picture or Set behavior).

- [ ] **Step 5: Probe.** Remove the `IsHittable` early return. `A_press_on_a_dimmed_pixel_does_nothing` must go red. Restore it.

- [ ] **Step 6: Commit.** `feat(rise): the page picks a layer, paints only it and keeps the whole character in 3D`

---

### Task 4: View, wiring and the journey

**Files:**
- Modify: `src/Nfty.App/Views/RiseView.axaml` (canvas host: `BelowImage`/`CanvasImage`/`AboveImage`, with `Classes="rdim"` on the outer two; a reserved header chip at the read-only pill's slot; layer rail rows become `Button.rrow` bound to `TargetLayerCommand` with `Classes.on="{Binding IsTarget}"`, keyboard-focusable; `KeyBinding` Escape → `EscapeCommand`)
- Modify: `src/Nfty.App/Views/RiseView.axaml.cs` (press: picking while `IsPicking`; refuses non-hittable presses before capture; the transform and `RenderOptions.BitmapInterpolationMode="None"` apply to all three images; `PageUp`/`PageDown` in `OnShortcut`, focus-guarded)
- Modify: `src/Nfty.App/Themes/Styles.axaml` (`Image.rdim` Opacity 0.3; `Button.rrow` with `Background` set at rest; a 1px accent edge for `.on` that is always laid out, transparent when off)
- Modify: `src/Nfty.App/Services/KeyHints.cs` (`LayerUp`/`LayerDown`; the toolstrip legend gains "PgUp/PgDn layer · Esc pick")
- Modify: `src/Nfty.App/ViewModels/ExplorerViewModel.cs`:
  - `RiseRecipeCallback` passes `_session, () => IsEditing`.
  - `OpenRise` subscribes `rise.Saved += OnRiseSaved`, where `OnRiseSaved(book)` = `OnEditorSaved(book)` + `_openEditor?.AdoptExternalSave(book)`.
- Modify: `src/Nfty.App/ViewModels/IngredientEditorViewModel.cs` (`internal void AdoptExternalSave(LoadedCookBook book)` finds its live ingredient by id and calls the existing `LoadDraft`; `OnRiseSaved` reuses it)
- Test: `tests/Nfty.App.Tests/RiseWriteThroughJourneyTests.cs` (new), `tests/Nfty.App.Tests/RiseWriteThroughViewTests.cs` (new)

**Interfaces:**
- Consumes: Task 3's VM members. `ServiceRegistration.RememberOnRiseSave` already subscribes to `RiseViewModel.Saved`, so recents follow for free. Assert it rather than re-wire it.

- [ ] **Step 1: Write the failing tests.**

```csharp
// RiseWriteThroughJourneyTests — Rig shaped like RiseEntryJourneyTests: ChestBook written to a temp .cbk, CookBookSession over it, Explorer unlocked
[AvaloniaFact] public async Task Painting_a_strap_pixel_writes_only_that_variants_depth()
// Explorer → Recipe pane → Rise (RiseRecipeCallback) → PickAt(gold px, Front) → ApplyDepthStroke(+1 front ×2) → SaveCommand
// → reopen the .cbk with CookBookArchive.Read: gold Depths == (2,0) at those px; oak and ghost entries byte-equal to before (absent stays absent);
//   ZIP entries other than lock's depth/gold.dep and the manifests unchanged in content
[AvaloniaFact] public async Task A_write_through_save_refreshes_the_explorer_and_the_recents()
// Explorer tree's lock node DepthChip reads the new depth; IRecentsService got one Add for the book path
[AvaloniaFact] public async Task An_open_editor_reloads_its_draft_after_a_write_through_save()
// open the editor on lock, call Explorer.OnRiseSaved(book) (internal): editor's depth for gold == saved; not dirty
[AvaloniaFact] public async Task The_lock_refuses_write_through_even_when_forced()
// paint, close the lock (ToggleLock), call Source.SaveAsync() AND SaveCommand.Execute: file bytes (SHA-256) unchanged; FooterText says read-only

// RiseWriteThroughViewTests — real RiseView at the minimum page area
[AvaloniaFact] public void A_dimmed_layer_cannot_be_hit_through_the_view()
// target gold; raise PointerPressed/Released on CanvasSurface at a body-only px (mapped via CanvasMapping) → no dirty; then at a gold px → gold +1
[AvaloniaFact] public void Rail_rows_target_their_layer_by_click_and_keyboard()
// click lock row → IsTarget; focus body row + Enter → body targeted; absent row disabled
[AvaloniaFact] public void The_target_header_keeps_its_place_with_and_without_a_target()
// header chip Bounds identical in picking and targeted states (reserve, toggle ink)
[AvaloniaFact] public void The_dimmed_images_line_up_with_the_target_at_every_zoom()
// Below/Canvas/Above Bounds and RenderTransform equal at fit and at 400%
```

- [ ] **Step 2: Run them to see them fail.**
Run: `dotnet test tests/Nfty.App.Tests --filter "FullyQualifiedName~RiseWriteThrough"`
Expected: FAIL.

- [ ] **Step 3: Implement** the files above. In the journey, every write goes through the Explorer's real seams. No test calls `CookBookPersistence` directly.

- [ ] **Step 4: Run the tests.**
Run: `dotnet test tests/Nfty.App.Tests --filter "FullyQualifiedName~Rise|FullyQualifiedName~WiringCoverage|FullyQualifiedName~DeadCodeAudit|FullyQualifiedName~PixelPerfect|FullyQualifiedName~ThemeResource"`
Expected: PASS.

- [ ] **Step 5: Probe.**
  - Make `OnRiseSaved` skip `AdoptExternalSave`. `An_open_editor_reloads_its_draft_after_a_write_through_save` must go red.
  - Remove the lock check inside `RecipeRiseSource.SaveAsync`. `The_lock_refuses_write_through_even_when_forced` must go red.
  - Restore both.

- [ ] **Step 6: Commit.** `feat(rise): write-through on the page, wired to the Explorer, recents and editor`

---

### Task 5: Frames, sweeps, perf, docs, full suite

**Files:**
- Modify: `tests/Nfty.App.Tests/VisualCapture.cs`. Add `rise-recipe-picking`, `rise-recipe-target` (gold targeted, others dimmed, header with reach) and `rise-recipe-target-3d` (swapped), both themes, at 1180×720. Each capture verifies its state before saving (the `CaptureReferences` rule).
- Modify: `tests/Nfty.App.Tests/DarkModeContrastTests.cs` `Screens()`. Add the recipe page in its riskiest state: a locked book, gold targeted, unsaved depth, so the footer's read-only sentence, the dimmed art and the header chip are all scored.
- Modify: `tests/Nfty.App.Tests/RisePagePerfTests.cs`. Add the `Rise.Recombine` scope on 64 px and 512 px two-layer stacks. Report the numbers; assert no wall-clock.
- Modify: `docs/manual/reference/rise.md` (sources table: Recipe roll → "The variant that owns the pixel"; a write-through section with the mouse map in picking vs targeted, PageUp/PageDown, Escape, and the reach figure's meaning; the Save row).
- Create: `docs/manual/how-to/shape-one-layer-on-the-whole-character.md` (how-to titled by goal; linked from `mkdocs.yml` nav and from `reference/rise.md`).
- Modify: `CLAUDE.md`. Under the `Nfty.App` Rise material and the `Nfty.Model` paragraph, add the write-through paragraph:
  - the per-button owner ruling and its reason;
  - the pick click paints nothing;
  - dimmed layers are refused by the VM, not by the images;
  - one page-wide `TargetedHistory`;
  - surfaces keyed by variant outlive rerolls;
  - Save = one persist, lock re-checked inside, stale guard over all touched variants;
  - reach = `RarityCalculator` share × `TargetSupply`, with `~`;
  - never from a Set.
  Also change the step-9b sentence "Recipe and asset sources show combined depth read-only".

- [ ] **Step 1: Capture.**
Run: `NFTY_CAPTURE=1 NFTY_CAPTURE_DIR=.superpowers/frames-step10 dotnet test tests/Nfty.App.Tests --filter "FullyQualifiedName~VisualCapture&FullyQualifiedName~Rise"`
LOOK at every new frame in both themes. Check: the target is crisp and the others are dimmed; the header chip reads in both themes; the rail row marks the target; the 3D tile shows the whole chest. Refine and recapture until each holds.

- [ ] **Step 2: Sweeps.**
Run: `dotnet test tests/Nfty.App.Tests --filter "FullyQualifiedName~DarkModeContrast|FullyQualifiedName~MinimumWindowFit|FullyQualifiedName~TextBaseline|FullyQualifiedName~RiseViewLayout|FullyQualifiedName~DeadCodeAudit|FullyQualifiedName~WiringCoverage"`
Expected: PASS. A contrast failure on the dimmed art is NOT text, so it is not scored. If the header chip fails, fix its ink, not the floor.

- [ ] **Step 3: Perf.**
Run: `dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~RisePagePerf -l "console;verbosity=detailed"`
Record `Rise.Recombine` at 512 px in the report. If it exceeds the 33 ms mesh budget alone, stop and tell the controller (recombine would then need dirty-rect scoping).

- [ ] **Step 4: Docs** as listed in Files.

- [ ] **Step 5: Full suite, once.**
Run: `dotnet build nfty.sln`, then `dotnet test nfty.sln`
Expected: 0 warnings, every project green. Report the counts per project. Do not mask a flake.

- [ ] **Step 6: Commit.** `docs(rise): write-through in the manual and CLAUDE.md; frames and sweeps`

---

## Self-review

- **Spec coverage:**
  - "Selects its owning layer": Q0, Task 2 `TryPick`, Task 3 `PickAt`.
  - "Only that variant's depth": Task 2 edit tests.
  - "Dimmed, not clickable": Tasks 3 and 4.
  - "3D tile whole, live": Task 3.
  - "Reserved header with reach": Q3, Tasks 1, 3 and 4.
  - "Never from a Set": Task 3.
  - "Reuse `StackPreview` split": Task 2 dim art.
  - Page rules (own undo, leave asks, Save is structure behind the lock through `CookBookPersistence`): Tasks 2–4.
- **Types:** `RiseLayerTarget`, `IDepthEditor`, `TargetedHistory<TKey,TPixel>`, `VariantReach` and `StackRise.FrontOwner`/`BackOwner` are named identically wherever they are used.
- **Not in scope:** writing through from a Set browser asset whose book happens to be open. The spec forbids writes from Sets, and the recipe pane is the open book's rolled stack.
