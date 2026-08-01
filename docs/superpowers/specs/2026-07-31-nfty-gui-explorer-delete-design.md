# nfty GUI — Explorer delete ingredient/recipe (A2a) design spec

**Date:** 2026-07-31
**Status:** Approved (design), pending implementation planning
**Scope:** First structural-CRUD slice (A2a). Delete the selected **ingredient** or **recipe** from the
open cookbook in the Explorer and persist the change to the source `.cbk`. Introduces a shared
write-back helper (the editor's Save persistence, extracted so both paths share it) and injects the
session into the Explorer. Builds on the editing slices (Save/persist, Explorer live-refresh) on `main`.

## 0. Program bar
Rock-solid, efficient; best practices; pull docs rather than assume; escalate anything off. Reuse the
Slice-2 machinery (atomic write-back, `session.Replace`, `ConfirmDialog`, Explorer tree rebuild). Small,
justified `Nfty.Core.Editing.CookBookEdits` additions (`RemoveIngredient`, `RemoveRecipe`). Extract the
editor Save's persistence into a shared helper rather than duplicating it.

## 1. Goals & non-goals
**Goals**
- The Explorer's **Delete** (currently a `_notify` stub, gated by the edit-lock `IsEditing`) removes the
  selected ingredient or recipe from the in-memory cookbook and writes the whole `.cbk` back to disk
  (crash-safe temp-then-atomic-replace), then refreshes the tree in place.
- A single **write-back helper** persists a spliced `LoadedCookBook` to the session's source path
  (temp write → atomic move → rehash → `session.Replace`), used by both the editor Save and Explorer
  delete. The editor Save is refactored onto it (no behavior change).
- Delete confirms first (reusing the Slice-2 `ConfirmDialog`) and is disabled when there is no source
  file, when not in edit mode, or when the cookbook root is selected.

**Non-goals (this slice)**
- **Add** ingredient/recipe/variant (slices A2b/A2c) and wiring the New* wizards. Deleting the **cookbook**
  itself (close the book instead). **Minimum-count guards** — an empty recipe (no ingredients) or empty
  cookbook (no recipes) is allowed; it is not generatable but is editable and recoverable via add, and
  `Validator` already reports it at generation. Undo of a delete. Loose/Kitchen deletion.

## 2. Components

### 2.1 Core additions (`Nfty.Core.Editing.CookBookEdits`)
- `LoadedCookBook RemoveIngredient(LoadedCookBook book, string recipeId, string ingredientId)` — returns
  a new graph with the ingredient dropped from the recipe's `Ingredients` and its id removed from
  `LayerOrder`; every other recipe/ingredient/image is reused by reference. Throws
  `KeyNotFoundException` if the recipe or ingredient id is absent.
- `LoadedCookBook RemoveRecipe(LoadedCookBook book, string recipeId)` — returns a new graph without that
  recipe (and its entry removed from the cookbook's per-recipe selection weights if present). Throws if
  the recipe id is absent.
- Both mirror `UpsertIngredient`'s contract: **dispose nothing** — the caller owns the lifetime of the
  removed subtree's images (the Explorer disposes the removed `LoadedIngredient`/`LoadedRecipe` after the
  swap; `LoadedRecipe.Dispose` cascades to its ingredients' images).

### 2.2 Shared write-back (`Nfty.App/Services/CookBookPersistence`)
A static helper (no state; pure I/O over the session):
```
static Task<LoadedCookBook> PersistAsync(ICookBookSession session, LoadedCookBook book2, CancellationToken ct = default)
```
- Requires `session.SourcePath` (throws `InvalidOperationException` if null — callers gate on it).
- Writes `book2` to `SourcePath + ".tmp"` via `CookBookArchive.WriteAsync`, then `File.Move(tmp, dest,
  overwrite: true)`; deletes the temp on any failure (`finally`).
- Recomputes the archive hash in-app (`Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()`,
  mirroring `ArchiveIo.HashFile`) → `book3 = new LoadedCookBook { Manifest, Recipes, SourceSha256 }`.
- `session.Replace(book3)` (non-disposing — `book3` shares the unchanged images) and returns `book3`.
- **Does not** dispose anything or show dialogs — the caller owns error handling and disposal of whatever
  the mutation orphaned.
- **Refactor:** the editor `Save` builds `book2 = UpsertIngredient(...)` then `book3 = await
  CookBookPersistence.PersistAsync(_session, book2)`, keeping its own dispose-replaced-images + `Saved`
  event. Its existing round-trip/failure tests must stay green (behavior unchanged).

### 2.3 Explorer (`ExplorerViewModel`)
- **Inject `ICookBookSession _session`** (ctor param; the DI factory and tests updated). Keep the existing
  `_book` field (updated on refresh); the session provides `SourcePath` + `Replace`.
- Replace `[RelayCommand(CanExecute = nameof(CanEdit))] DeleteSelected` (stub) with an async command:
  - `CanExecute = IsEditing && _session.SourcePath is not null && SelectedNode?.Kind is
    ExplorerNodeKind.Recipe or ExplorerNodeKind.Ingredient`. Re-notified on `IsEditing` (already wired via
    `NotifyCanExecuteChangedFor`) and on `SelectedNode` change.
  - Confirm via `ConfirmDialogViewModel` ("Delete “{name}”?" / "Delete").
  - Build `book2`:
    - Ingredient node (`Domain is (LoadedRecipe r, LoadedIngredient i)`) → `RemoveIngredient(_book,
      r.Manifest.Id, i.Manifest.Id)`; parent-to-select = the recipe id.
    - Recipe node (`Domain is LoadedRecipe r`) → `RemoveRecipe(_book, r.Manifest.Id)`; parent-to-select =
      the cookbook root id.
  - `book3 = await CookBookPersistence.PersistAsync(_session, book2)`.
  - Dispose the removed domain object (`i.Dispose()` / `r.Dispose()`) — its images are no longer
    referenced by `book3`.
  - `ApplyBook(book3, parentId)` (below).
  - Errors → `ErrorDialogViewModel`; on failure the tree is unchanged and nothing is disposed.
- **`ApplyBook(LoadedCookBook book, string? selectId)`** — generalize the current `OnEditorSaved`: set
  `_book = book`, `Root = BuildTree(book)`, and select the node whose id matches `selectId` (searching
  recipes then ingredients), falling back to `Root`. `OnEditorSaved(book)` becomes
  `ApplyBook(book, SelectedNode?.Id)`.

### 2.4 View
No new controls — the Delete button already exists and binds `DeleteSelectedCommand` (now async + more
tightly gated). The edit-lock already governs visibility/enable of edit affordances.

## 3. Data flow
```
Delete (enabled: editing, source .cbk exists, a recipe/ingredient selected)
  → ConfirmDialog
  → book2 = CookBookEdits.RemoveIngredient(_book, recipeId, ingId)   // or RemoveRecipe(_book, recipeId)
  → book3 = CookBookPersistence.PersistAsync(session, book2)         // temp write → atomic move → rehash → Replace
  → removed.Dispose()                                                // free the orphaned subtree's images
  → ApplyBook(book3, parentId)                                       // rebuild tree, select the parent
```

## 4. Error handling
- Write/IO failure inside `PersistAsync` → temp cleaned up, exception propagates; the Explorer catches it,
  shows `ErrorDialogViewModel`, and leaves the tree + in-memory book untouched (no dispose, no `Replace`
  has occurred yet because `PersistAsync` replaces only after a successful move+rehash... note: `Replace`
  is the last step, so a failure before it leaves `session.Current` = the old book).
- Absent ids can't reach the command (they come from the live selection); if they somehow do,
  `KeyNotFoundException` surfaces through the same dialog.
- Null `SourcePath` is gated by `CanExecute`; `PersistAsync` also throws defensively.

## 5. Testing
- **Core:** `RemoveIngredient` drops the ingredient from `Ingredients` and `LayerOrder`, keeps the others
  and their images, and throws on an absent recipe/ingredient id; `RemoveRecipe` drops the recipe (and its
  weight entry) and throws on absent.
- **Persistence:** `PersistAsync` writes `book2` to the session's `SourcePath`, the re-read archive equals
  `book2`'s structure, `session.Current` is the returned `book3` (fresh `SourceSha256`), no `.tmp` remains;
  throws when `SourcePath` is null.
- **Explorer** (`[AvaloniaFact]`, reusing the Slice-2 on-disk fixture): with `IsEditing` on and a
  confirming dialog, delete an ingredient → the re-read `.cbk` no longer contains it, the tree rebuilt, the
  recipe is selected; delete a recipe → gone from disk, root selected. `CanExecute` is false when not
  editing, when `SourcePath` is null, and when the cookbook root is selected. A declined confirm removes
  nothing.
- **Editor regression:** the editor Save round-trip + failure-path tests stay green after the refactor onto
  `PersistAsync`.
- Full suite green; build 0 warnings; no raw hex outside `Tokens.axaml`.
- **Manual smoke:** open a `.cbk`, toggle the edit lock, delete an ingredient (confirm) → it disappears and
  the file on disk no longer has it; delete a recipe; reopen to confirm persistence; Delete is disabled on
  the cookbook root and when the lock is off.

## 6. Risks & escalation
- **Shared-image disposal** (same edge as Slice 2): `book3` reuses every *surviving* recipe/ingredient's
  images; only the removed subtree is orphaned and disposed, exactly once. `PersistAsync` must `Replace`
  (never `Open`/dispose). Deleting a recipe disposes all its ingredients' images via `LoadedRecipe.Dispose`
  — confirm none are shared with a surviving recipe (ids are unique per recipe, images are per-variant, so
  no cross-recipe sharing exists).
- **Refactor risk:** extracting the editor Save's persistence must not change its behavior — guard with the
  existing Save tests before/after.
- **Empty states:** deleting the last ingredient/recipe leaves a non-generatable book; allowed and
  recoverable, but the confirm copy should be plain ("Delete “X”?") so the user isn't surprised. If this
  reads badly in smoke, escalate (a guard or a warning is a cheap follow-up).
- **Session in Explorer:** injecting the session widens the Explorer ctor (factory + tests). Keep the
  `_book` field as the tree's source of truth (updated by `ApplyBook`); the session is only for
  `SourcePath` + `Replace`.
