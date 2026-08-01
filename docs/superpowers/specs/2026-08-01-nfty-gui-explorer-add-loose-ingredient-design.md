# nfty GUI — Explorer "Add ingredient → Loose (Kitchen)" (B3b / A2c-F2) design spec

**Date:** 2026-08-01
**Status:** Approved (design), pending implementation planning
**Scope:** Close the A2c-F2 gap: when the user picks the **Loose (Kitchen)** destination in the Explorer's
Add-ingredient wizard, create + write a standalone `.igt` (as B3a does from Landing) and open it in the
editor — instead of silently upserting into the open cookbook. Marks the loose/Kitchen create surface
complete. No loose-recipe create (a dead-end), no Kitchen screen.

## 0. Program bar
Rock-solid, efficient; best practices; pull docs (Context7) rather than assume any library API; escalate
anything off. Reuse B3a's create-loose flow (`NewIngredientViewModel.TryGetCanvas`/`Build`,
`IngredientArchive.Write`, `LooseWorkspace.WrapIngredient`, the loose editor factory + `SaveFileAsync`).
No `Nfty.Core` change.

## 1. Goals & non-goals
**Goals**
- In `ExplorerViewModel.AddIngredientTo` (reached from a selected recipe), after the wizard returns,
  **branch on `result.Destination`**:
  - **IntoCookBook** (default) → the existing A2b upsert-into-the-cookbook flow (unchanged).
  - **LooseKitchen** → the B3a create-loose flow: parse canvas → `SaveFileAsync(".igt")` → `Build` →
    `IngredientArchive.Write` → open the new `.igt` in a **loose** editor (loose-save to that file). The
    open cookbook is **not** touched.
- The Explorer gains the two dependencies this needs: `IFilePickerService` and the loose-editor factory
  (`Func<LoadedIngredient, LoadedCookBook, string, IngredientEditorViewModel>`), injected via DI (the
  Landing already has both).

**Non-goals (this slice)**
- Loose **recipe** create (opening it is a read-only dead-end until loose-recipe editing exists) and the
  Explorer "Add recipe → Loose". A Kitchen screen. Refactoring Landing's B3a flow onto a shared workflow
  (a possible later DRY — this slice accepts a small duplication of the create-loose steps). Any
  `Nfty.Core` change.

## 2. Components

### 2.1 Explorer dependencies (`ExplorerViewModel` + `ServiceRegistration`)
- Ctor: append `IFilePickerService picker` and `Func<LoadedIngredient, LoadedCookBook, string,
  IngredientEditorViewModel> looseEditorFactory`; store `_picker` / `_looseEditorFactory`.
- `ServiceRegistration` — the Explorer factory (`Func<LoadedCookBook, ExplorerViewModel>`) passes
  `sp.GetRequiredService<IFilePickerService>()` and the existing loose-editor factory singleton.
- All Explorer test/capture construction sites gain the two args (a stub picker + the existing
  `ExplorerViewModelTests.LooseEditorFactory`).

### 2.2 Loose branch in `AddIngredientTo` (`ExplorerViewModel`)
After the wizard result + the non-blank-id guard, before the IntoCookBook body:
```csharp
if (result.Destination == RecipeDestination.LooseKitchen)
{
    await CreateLooseIngredient(result);
    return;
}
// … existing IntoCookBook flow (dup-check → validate → UpsertIngredient → PersistAsync → OpenEditor) …
```
and add (mirrors `LandingViewModel.NewIngredient`'s loose steps):
```csharp
private async Task CreateLooseIngredient(NewIngredientViewModel result)
{
    if (!result.TryGetCanvas(out var canvas))
    {
        await ShowError("Invalid canvas", "Enter a canvas size like 512x512.");
        return;
    }
    var path = await _picker.SaveFileAsync("Save new ingredient", ".igt");
    if (path is null) return;   // cancelled

    LoadedIngredient built;
    try { built = result.Build(canvas); }
    catch (Exception ex) { await ShowError("Could not save", ex.Message); return; }
    try { IngredientArchive.Write(path, built.Manifest, built.VariantImages); }
    catch (Exception ex) { await ShowError("Could not save", ex.Message); built.Dispose(); return; }
    built.Dispose();

    // Open the new .igt in a loose editor (read a fresh copy; the editor owns the wrapper book).
    LoadedIngredient ing;
    try { ing = IngredientArchive.Read(path); }
    catch (Exception ex) { await ShowError("Could not open", ex.Message); return; }
    var book = LooseWorkspace.WrapIngredient(ing);
    _nav.To(_looseEditorFactory(ing, book, path));
}
```
- **`ShowError` is async in the Explorer** (`Task ShowError(...)`) — reuse it (A2b added it). `IngredientArchive`
  and `LooseWorkspace` are already reachable (`Nfty.Core.Formats` + `Nfty.App.Services` imported). Add
  `using Nfty.App.Services;` only if `IFilePickerService` isn't already resolved.
- **Session isolation:** the loose editor is opened via `_looseEditorFactory` with a `looseSavePath`; its
  Save writes to the `.igt`, never the session's cookbook (B1 invariant). The Explorer's `_book`/session
  are untouched by the loose branch.
- **Disposal:** `built` (in-memory) is written then disposed; the editor opens an independent fresh copy —
  exactly B3a's ownership model (no shared images).

### 2.3 View
No new controls — the Add button + the New Ingredient wizard (with its Loose radio + canvas field from
B3a) already exist; this only changes what happens on Create when Loose is selected.

## 3. Data flow
```
Explorer Add (recipe selected) → wizard → Create
  → Destination == IntoCookBook → A2b upsert-into-cookbook (unchanged)
  → Destination == LooseKitchen →
       TryGetCanvas (invalid → error)
       → path = SaveFileAsync(".igt")           (cancel → stop)
       → built = Build(canvas) ; IngredientArchive.Write(path, …) ; built.Dispose()
       → ing = IngredientArchive.Read(path) ; book = WrapIngredient(ing)
       → nav.To(looseEditorFactory(ing, book, path))     (loose editor; Save → the .igt)
```

## 4. Error handling
- Cancelled wizard / cancelled Save picker → no-op, cookbook untouched.
- Invalid canvas → error before the Save prompt.
- `Build` OOM (huge canvas) / `Write` failure → error dialog, `built` disposed if it exists, nothing
  opened (mirrors the B3a F1 fix — `Build` is inside a try).
- Re-read failure → error dialog.

## 5. Testing
- **Explorer Add → Loose** (`[AvaloniaFact]`, a wizard-dialog stub that returns the wizard as Loose with a
  name/canvas + a stub picker returning a temp `.igt` path): a real `.igt` is written at the path (re-read
  shows the ingredient with one variant at the canvas size), the nav is an `IngredientEditorViewModel`,
  **and the selected recipe's ingredient count in the open cookbook is unchanged** (the cookbook was not
  mutated).
- **Explorer Add → IntoCookBook regression:** the existing `ExplorerAddIngredientTests` (upsert + persist +
  open) stay green after the branch is added.
- **Cancelled Save picker** on the Loose path → nothing written, nothing opened, cookbook unchanged.
- **No regression:** A2/B1–B3a + Landing suites green; full suite green; build 0 warnings; no raw hex; no
  `Nfty.Core` change.
- **Manual smoke:** open a `.cbk`, edit-lock on, select a recipe → Add → in the wizard pick **Loose
  (Kitchen)**, set name/canvas → Create → a Save dialog appears → choose a path → the `.igt` writes and
  opens in the editor (Save writes back to the file, not the cookbook); picking **Into CookBook** still
  adds to the recipe as before.

## 6. Risks & escalation
- **Ctor ripple:** two new Explorer deps touch every Explorer construction site (DI + ~8 test/capture
  sites). Mechanical; guard with the full App suite. Keep the two params trailing so the diff is localized.
- **Duplication with Landing's B3a flow:** the create-loose steps are duplicated (Explorer vs Landing).
  Accepted for isolation this slice; a shared `LooseIngredientWorkflow` service is a reasonable later DRY
  (would also refactor Landing) — do not do it here to keep the change focused and B3a untouched.
- **Session isolation:** the loose branch must not call `session.Open`/`Replace`/`UpsertIngredient`/
  `PersistAsync` — it writes the `.igt` and opens a loose editor only. A test asserts the cookbook's
  ingredient count is unchanged, guarding against an accidental upsert.
- **Disposal:** identical to B3a (`built` written then disposed; editor reads a fresh copy). The B3a review
  fix (`Build` inside the try) is carried into `CreateLooseIngredient`.
