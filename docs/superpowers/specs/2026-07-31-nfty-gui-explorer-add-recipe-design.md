# nfty GUI — Explorer add recipe (A2c) design spec

**Date:** 2026-07-31
**Status:** Approved (design), pending implementation planning
**Scope:** Third structural-CRUD slice (A2c). Add a new (empty) recipe to the open cookbook from the
Explorer: the New Recipe wizard collects name + weight, the Explorer builds an empty recipe, validates +
persists it to the source `.cbk`, and selects it. Completes the Explorer add/delete surface (delete=A2a,
add-ingredient=A2b, add-recipe=A2c). Builds on A2a (`CookBookPersistence`, `ApplyBook`) and A2b (wizard
`DerivedId`/`Create`→`Close(this)` pattern, the F1 blank-id guard).

## 0. Program bar
Rock-solid, efficient; best practices; pull docs rather than assume; escalate anything off. Reuse
`CookBookPersistence.PersistAsync`, `ApplyBook`, `Validator.ValidateRecipe`, and the A2b wizard/guard
patterns. One small `Nfty.Core.Editing` addition (`CookBookEdits.UpsertRecipe`), mirroring
`UpsertIngredient`.

## 1. Goals & non-goals
**Goals**
- The Explorer's **Add** button, with the **CookBook root** selected (label "Add recipe"), opens the New
  Recipe wizard. On **Create** the wizard closes returning itself; the Explorer builds an **empty**
  `LoadedRecipe` (`RecipeManifest(DerivedId, Name, [], [])`, no ingredients) — **not** validated (an
  empty recipe is intentionally not-yet-generatable), splices it via a new
  `CookBookEdits.UpsertRecipe(book, recipe, weight)` (adding `RecipeWeights[id] = weight`), persists via
  `CookBookPersistence.PersistAsync`, refreshes the tree (`ApplyBook`), and **selects the new recipe**.
- The new recipe starts empty; the user then adds ingredients to it via A2b. No editor opens (recipes are
  not edited directly).
- Add is gated on edit-mode + a source file + the CookBook root selected; a blank-derived id is rejected
  (F1) and a duplicate recipe id is rejected.

**Non-goals (this slice)**
- The **Loose-Kitchen** recipe path (slice B) — the wizard's `LooseKitchen` destination stays a Landing
  stub. Editing a recipe's **layer order / incompatibility rules** (a later slice). Opening anything after
  add. Requiring the recipe to be non-empty (it's legal and expected to be filled next).

## 2. Components

### 2.1 New Recipe wizard (`NewRecipeViewModel`)
- Add `public string DerivedId => <slug of Name>` (same as `NewIngredientViewModel`: lower-invariant,
  spaces→`-`, empty tokens stripped); notify on `Name` change and re-notify `CreateCommand`.
- `Create` closes with the VM and is gated on a non-blank id:
  `private bool CanCreate() => !string.IsNullOrWhiteSpace(DerivedId);`
  `[RelayCommand(CanExecute = nameof(CanCreate))] private void Create() => Dialogs.Close(this);`
  (was `Notify.Report + Close(null)`). The Landing path uses `ShowAsync<object>` and ignores the result, so
  this is compatible.
- No `Build` method — an empty recipe has no images; the Explorer constructs the `LoadedRecipe` directly
  from `DerivedId`/`Name`/`Weight`.

### 2.2 Core addition (`Nfty.Core.Editing.CookBookEdits`)
- `LoadedCookBook UpsertRecipe(LoadedCookBook book, LoadedRecipe recipe, double weight)` — returns a new
  graph with `recipe` added (or, if its id already exists, replaced), and `RecipeWeights[recipe.Id] =
  weight`; every other recipe/image is reused by reference. Mirrors `UpsertIngredient`'s contract (disposes
  nothing; the caller owns any replaced subtree — not relevant for a fresh add).

### 2.3 Explorer add dispatch (`ExplorerViewModel`)
- Restructure the existing async `Add` to dispatch on the selection (keeping A2b intact):
  - **Recipe** selected → the A2b add-ingredient flow (unchanged).
  - **CookBook root** selected → the new add-recipe flow (below).
  - **Ingredient** selected, or not-editing / no-source → the `_notify.Report(AddLabel)` stub.
- Add-recipe flow (with the CookBook root selected, `IsEditing`, `SourcePath != null`):
  1. `var wizard = new NewRecipeViewModel(_dialogs, _notify); var result = await
     _dialogs.ShowAsync<NewRecipeViewModel>(wizard);` — null ⇒ cancelled, return.
  2. Guard: `if (string.IsNullOrWhiteSpace(result.DerivedId)) { error "The recipe needs a name."; return; }`
  3. `if (_book.Recipes.Any(r => r.Manifest.Id == result.DerivedId)) { error "A recipe “{id}” already
     exists."; return; }`
  4. `var recipe = new LoadedRecipe { Manifest = new RecipeManifest(result.DerivedId, result.Name,
     Array.Empty<string>(), Array.Empty<IncompatibilityRule>()), Ingredients = Array.Empty<LoadedIngredient>() };`
  5. **No `ValidateRecipe` at add time.** A fresh recipe is intentionally empty, and
     `Validator.ValidateRecipe` rejects an empty `layerOrder` ("would generate a fully-transparent
     asset") — so validating here would make adding a recipe impossible. The empty recipe is persisted
     as-is (it round-trips through `CookBookArchive`); the user fills it via "Add ingredient" next, and
     the cook path validates the whole book at generation time (same "empty states allowed, caught at
     generation" philosophy as A2a delete).
  6. `var book2 = CookBookEdits.UpsertRecipe(_book, recipe, result.Weight);`
  7. `var book3 = await CookBookPersistence.PersistAsync(_session, book2);`
  8. `ApplyBook(book3, recipe.Manifest.Id);` — selects the new (empty) recipe node.
  - Errors → `ErrorDialogViewModel`; on any failure nothing is persisted. **No image ownership** to manage
    (the recipe is empty), so no dispose bookkeeping — simpler than A2b.
- `CanExecute`/gating: the whole `AddCommand` stays always-enabled (kinds it can't handle fall to the
  stub), matching A2b.

### 2.4 View
No new controls — the **Add** button already binds `AddCommand`. The New Recipe wizard view already exists
(used from Landing); it renders unchanged.

## 3. Data flow
```
Add (CookBook root selected, editing, source file)
  → wizard = NewRecipeViewModel; result = await ShowAsync<NewRecipeViewModel>(wizard)   (null → cancel)
  → guard blank id; dup-id check
  → recipe = LoadedRecipe{ RecipeManifest(id, name, [], []), Ingredients=[] }
  → Validator.ValidateRecipe(recipe)                       // empty recipe is legal
  → book2 = CookBookEdits.UpsertRecipe(_book, recipe, result.Weight)   // + RecipeWeights[id]=weight
  → book3 = CookBookPersistence.PersistAsync(session, book2)
  → ApplyBook(book3, recipe.Id)                            // select the new recipe
```

## 4. Error handling
- Cancelled wizard (`null`) → no-op.
- Blank id, duplicate id, or `Validator` problems → error dialog, nothing written.
- Write failure inside `PersistAsync` → temp cleaned up (A2a), error dialog, `_book`/tree unchanged.
- No live images are allocated for an empty recipe, so there is no disposal path to get wrong.

## 5. Testing
- **Core:** `UpsertRecipe` adds the recipe and sets its weight, keeps existing recipes + images, and
  replaces (not duplicates) when the id already exists; `RecipeWeights` gains the entry.
- **Wizard:** `DerivedId` slugs the name; `Create` is disabled for a blank/whitespace name and closes the
  dialog with the VM otherwise.
- **Explorer** (`[AvaloniaFact]`, reuse the A2a on-disk fixture + a dialog stub that fills+Creates the New
  Recipe wizard): adding with the cookbook root selected writes a recipe to the `.cbk` (re-read shows it
  with its weight), and selects it; a **duplicate** name → error, nothing written; a **blank** name →
  error, nothing written; `Add` with a recipe/ingredient selected still runs A2b/stub (no regression);
  gating (not editing / no source → stub).
- **No regressions:** A2a/A2b + editor suites stay green; full suite green; build 0 warnings; no raw hex.
- **Manual smoke:** open a `.cbk`, edit-lock on, select the cookbook root → **Add** → name a recipe + set a
  weight → Create → the empty recipe appears in the tree and is selected → add an ingredient to it (A2b) →
  Save → reopen to confirm; try a duplicate/blank name → error.

## 6. Risks & escalation
- **Empty recipe semantics:** a 0-ingredient recipe is legal to *persist* but **not** legal to
  *generate* — `Validator.ValidateRecipe` flags its empty `layerOrder` (`CheckLayerOrder`). So the add
  flow deliberately does **not** call `ValidateRecipe` (it would block every add); the empty recipe is
  written as-is and the cook path reports it at generation time. It round-trips through
  `CookBookArchive` (a `.rcp` with a manifest and zero ingredient entries).
- **`RecipeWeights` consistency:** `UpsertRecipe` must add the weight entry so the recipe participates in
  the cookbook's weighted roll; a recipe present in `Recipes` but absent from `RecipeWeights` would be a
  latent inconsistency — the test asserts both.
- **Add dispatch regression:** restructuring `Add` must not break the A2b ingredient path — keep the recipe
  branch first/unchanged and add the cookbook branch alongside; the A2b tests guard this.
- **Blank id (F1):** apply the same guard as A2b at both the wizard (`CanCreate`) and the Explorer
  (authoritative) so a bypassed wizard can't persist an empty recipe id.
