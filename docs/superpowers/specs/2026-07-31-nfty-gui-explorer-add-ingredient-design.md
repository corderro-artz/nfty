# nfty GUI — Explorer add ingredient (A2b) design spec

**Date:** 2026-07-31
**Status:** Approved (design), pending implementation planning
**Scope:** Second structural-CRUD slice (A2b). Add a new ingredient to the selected recipe from the
Explorer: the New Ingredient wizard collects the fields, the Explorer builds the ingredient (with one
blank starter variant), validates + persists it to the source `.cbk`, and opens the editor on it.
Builds on A2a (`CookBookPersistence`, `ApplyBook`, session-in-Explorer) and the editor.

## 0. Program bar
Rock-solid, efficient; best practices; pull docs rather than assume; escalate anything off. Reuse A2a's
`CookBookPersistence.PersistAsync`, `CookBookEdits.UpsertIngredient`, and the editor; reuse
`Validator.ValidateIngredient`. Small, contained additions on the wizard VM (`DerivedId`,
`Build(canvas)`). No `Nfty.Core` change (the engine already has Upsert + Validate).

## 1. Goals & non-goals
**Goals**
- The Explorer's **Add** button, with a **Recipe** selected (label "Add ingredient"), opens the New
  Ingredient wizard. On **Create**, the wizard closes returning itself as the result; the Explorer builds
  a `LoadedIngredient` from it (id = slug of the name, kind, `Colorization` from the fields) plus **one
  blank starter variant** (`variant-1`, weight 1) at the cookbook canvas size, so the ingredient is
  immediately generatable.
- The Explorer validates (`Validator.ValidateIngredient` + a duplicate-id check against the recipe),
  splices it via `UpsertIngredient`, persists via `CookBookPersistence.PersistAsync`, refreshes the tree
  (`ApplyBook`, selecting the new ingredient), and **opens the editor** on it so the user paints the blank
  variant.
- Add is gated on edit-mode + a source file + a Recipe selected.

**Non-goals (this slice)**
- The **Landing / Loose-Kitchen** New-Ingredient path (slice B) — the wizard's `LooseKitchen` destination
  stays a stub there. **Add recipe** (A2c) and **add variant from the Explorer** (the editor already adds
  variants). Quantize-bucket inputs on the wizard (use defaults). Importing an image as the first variant
  (the starter is blank). Any `Nfty.Core` change.

## 2. Components

### 2.1 New Ingredient wizard (`NewIngredientViewModel`)
- Add `public string DerivedId => <slug of Name>` (mirror `NewCookBookViewModel.DerivedId`:
  lower-invariant, spaces → `-`, empty-token-stripped). Notify it on `Name` change.
- Add `public Colorization? BuildColorization()`:
  - **Dynamic** → `new Colorization(ColorModel.Hsv, 12, 4, new[] { new ColorEntry(1, new ColorRange(HueMin,
    HueMax, SatMin, SatMax), null) })` (quantize defaults 12/4).
  - **Static** → `new Colorization(ColorModel.Hsv, 12, 4, new[] { new ColorEntry(1, null, FixedColor) })`.
  - **Custom** → `null`.
- Add `public LoadedIngredient Build(Dimensions canvas)`:
  - `variantId = "variant-1"`; `manifest = new IngredientManifest(DerivedId, Name, Kind, BuildColorization(),
    new[] { new Variant(variantId, "Variant 1", 1) })`.
  - `images = { [variantId] = ValueMap.ForCanvas(canvas).ToImage() }` (a blank canvas-sized PNG).
  - returns `new LoadedIngredient { Manifest = manifest, VariantImages = images }`. **Caller owns the
    image** (disposes it if it doesn't get adopted by the book).
- **Create** now closes with the VM as the result: `Dialogs.Close(this)` (was `Close(null)` + a
  `_notify.Report`). Cancel still closes with `null`. (The Landing path shows the wizard with
  `ShowAsync<object>` and ignores the result, so this is compatible.)
- `Build`/`BuildColorization`/`DerivedId` live on the wizard so the primitives→domain mapping is unit
  testable without the Explorer.

### 2.2 Explorer add flow (`ExplorerViewModel`)
- `Add` (currently `_notify.Report(AddLabel)`) becomes async and, **for a Recipe selection**, runs the add
  flow; the CookBook ("Add recipe" → A2c) and Ingredient ("Add variant") cases stay `_notify` stubs.
  - `CanExecute` for the recipe path: `IsEditing && _session.SourcePath is not null && SelectedNode?.Kind
    is ExplorerNodeKind.Recipe`. (Keep a plain `Add` enabled for the other kinds' stubs, or gate the whole
    button — simplest: `AddCommand` always enabled, and inside, non-Recipe kinds just report the stub.)
  - Flow (Recipe `r = (LoadedRecipe)SelectedNode.Domain`):
    1. `var wizard = new NewIngredientViewModel(_dialogs, _notify);` then
       `var result = await _dialogs.ShowAsync<NewIngredientViewModel>(wizard);` — null ⇒ cancelled, return.
    2. `var newIng = result.Build(_book.Manifest.Canvas);` (owns the blank image).
    3. **Validate:** if `r.Ingredients.Any(i => i.Manifest.Id == newIng.Manifest.Id)` → error dialog
       ("An ingredient “{id}” already exists in this recipe."), dispose `newIng`, return. Then
       `Validator.ValidateIngredient(newIng)` → if problems, error dialog with the joined messages, dispose
       `newIng`, return.
    4. `var book2 = CookBookEdits.UpsertIngredient(_book, r.Manifest.Id, newIng);`
    5. `var book3 = await CookBookPersistence.PersistAsync(_session, book2);`
    6. `ApplyBook(book3, newIng.Manifest.Id);` (selects the new ingredient node).
    7. **Open the editor** on it: resolve the new `(LoadedRecipe, LoadedIngredient)` from `book3` (the
       reused recipe now containing `newIng`) and `OpenEditor(newIng, recipeFromBook3)` — the same path a
       user's ✏ Edit takes, so the editor's `Saved` is wired and painting the blank variant then Save
       persists.
  - **Errors** (validation, write) → `ErrorDialogViewModel`; on any failure nothing is persisted and the
    blank image is disposed.
- The wizard is constructed directly by the Explorer (`new NewIngredientViewModel(_dialogs, _notify)`);
  no new DI factory needed (the Explorer already holds `_dialogs` + `_notify`).

### 2.3 View
No new controls — the **Add** button already exists and binds `AddCommand` (now async for the recipe
path). The New Ingredient wizard view already exists (used from Landing); it renders unchanged.

## 3. Data flow
```
Add (Recipe selected, editing, source file)
  → wizard = NewIngredientViewModel; result = await ShowAsync<NewIngredientViewModel>(wizard)  (null → cancel)
  → newIng = result.Build(canvas)                         // manifest + Colorization + one blank variant
  → dup-id check + Validator.ValidateIngredient           // fail → error dialog, dispose newIng
  → book2 = CookBookEdits.UpsertIngredient(_book, recipeId, newIng)
  → book3 = CookBookPersistence.PersistAsync(session, book2)
  → ApplyBook(book3, newIng.Id)                            // select the new ingredient
  → OpenEditor(newIng, recipeFromBook3)                    // paint the blank variant; editor Save persists
```

## 4. Error handling
- Cancelled wizard (`null` result) → no-op.
- Duplicate id or `Validator` problems → error dialog, the blank image disposed, nothing written.
- Write failure inside `PersistAsync` → temp cleaned up (A2a), error dialog, `_book`/tree unchanged, the
  blank image disposed (it never reached a persisted book). **Disposal note:** on the success path the
  blank image becomes `book3`'s (via Upsert → not disposed by the Explorer); on any failure path before
  `Replace`, the Explorer disposes `newIng`.

## 5. Testing
- **Wizard** (unit, no Avalonia needed for the mapping): `DerivedId` slugs the name; `BuildColorization`
  yields a dynamic hue/sat range entry, a static fixed entry, and null for custom (with the right quantize
  defaults); `Build(canvas)` returns an ingredient with one `variant-1` at the canvas size and the built
  colorization; `Create` closes the dialog with the VM as the result.
- **Explorer add** (`[AvaloniaFact]`, reuse the A2a on-disk fixture + a dialog stub that returns a
  pre-filled `NewIngredientViewModel` from `ShowAsync`): adding to the selected recipe writes an ingredient
  to the `.cbk` (re-read shows it with one variant), selects the new ingredient, and navigates the nav to
  an `IngredientEditorViewModel`. A **duplicate id** → error dialog, nothing written. A **cancelled**
  wizard → nothing written. `AddCommand` for a recipe is gated on editing + source file.
- **No regressions:** A2a delete + editor Save suites stay green; full suite green; build 0 warnings; no
  raw hex.
- **Manual smoke:** open a `.cbk`, edit-lock on, select a recipe → **Add ingredient** → fill the wizard →
  Create → the ingredient appears in the tree and the editor opens on its blank variant → paint → Save →
  reopen the `.cbk` to confirm it persisted; try a duplicate name → error.

## 6. Risks & escalation
- **Blank-image ownership** is the sharp edge: the wizard's `Build` hands the Explorer a live
  `Image<Rgba32>`. On success it is adopted by `book3` (do **not** dispose); on every early-return/error it
  must be disposed exactly once. A single `try` around build→validate→persist with a `disposed` flag, or
  explicit dispose on each failure branch, keeps this correct — mirror A2a's discipline.
- **Editor handoff after persist:** `OpenEditor` needs the `(LoadedRecipe, LoadedIngredient)` from
  `book3`, not the pre-persist objects — resolve them from `book3` by id so the editor edits the graph the
  session now holds. Getting this wrong edits a detached ingredient whose Save would splice a stale book.
- **`Build` on the UI vs. the wizard:** constructing an ImageSharp image inside a VM is fine (App layer),
  but keep `Build` synchronous and cheap (one blank raster). If the canvas is large this is still trivial.
- **Colorization correctness:** `Validator.ValidateIngredient` is the gate — if a built `Colorization`
  (e.g. a malformed `FixedColor` spec) fails validation, the error surfaces to the user rather than
  writing a broken ingredient. Do not bypass the validator.
- **Custom kind:** a custom ingredient's starter variant is a blank value-map (grayscale); consistent with
  the editor's existing custom limitation. Saving it later is still blocked for custom (Slice-2 policy) —
  acceptable; the ingredient is created and selectable, just not editor-savable until full-colour custom
  editing lands (slice C).
