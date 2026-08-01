# nfty GUI — Open & edit a loose `.igt` (B1) design spec

**Date:** 2026-07-31
**Status:** Approved (design), pending implementation planning
**Scope:** First "loose / Kitchen" slice (B1). Open a standalone `.igt` (an ingredient not inside a
cookbook) in the existing Ingredient Editor and save edits back to the `.igt` file. No Kitchen screen
(undesigned), no `.rcp` handling (B2). Wires the Landing **Import** stub for the `.igt` case.

## 0. Program bar
Rock-solid, efficient; best practices; pull docs rather than assume; escalate anything off. Reuse the
whole Ingredient Editor (paint / variant CRUD / colorize / preview) unchanged; the only editor change is
an **optional** loose-save path so existing cookbook construction sites are untouched. Reuse
`IngredientArchive` for the loose read/write. No `Nfty.Core` change.

## 1. Goals & non-goals
**Goals**
- Landing **Import** of a `.igt` opens it in the Ingredient Editor: the loose ingredient is wrapped in a
  synthetic single-recipe cookbook (so the editor's canvas/structure needs are met), and the editor is
  given the `.igt` path as a **loose-save target**.
- **Save** on a loose ingredient writes it straight back to the `.igt`
  (`IngredientArchive.WriteAsync(path, manifest, images)`) — no cookbook, no session. All the editor's
  existing behaviour (paint, undo/redo, add/duplicate/delete/rename/reweight variants, colorize preview)
  works as-is.
- The loose ingredient's **canvas** is derived from its variant image size (all variants share one size,
  guaranteed by the archive's own validation on write).

**Non-goals (this slice)**
- Loose **`.rcp`** open (B2) — Import of a `.rcp` stays the existing "coming soon" stub. The
  **New-Ingredient "Loose Kitchen"** create-from-scratch path (B3, also the A2c-F2 follow-up). A **Kitchen
  screen** / managing multiple loose files. Any `Nfty.Core` change. Editing a **custom** loose ingredient's
  full colour (Save stays blocked for custom, as in the cookbook editor).

## 2. Components

### 2.1 Synthetic cookbook wrapper (`Nfty.App` helper)
A small static helper (e.g. `LooseWorkspace.WrapIngredient`):
```
static LoadedCookBook WrapIngredient(LoadedIngredient ing)
```
- Canvas = the ingredient's variant image size: `var img = ing.VariantImages.Values.First(); new
  Dimensions(img.Width, img.Height)`. (A zero-variant `.igt` can't be edited meaningfully — guard: if
  there are no variants, surface an error on import rather than wrapping.)
- Builds `new LoadedCookBook { Manifest = new CookBookManifest("loose", ing.Manifest.Name, canvas,
  new Collection(ing.Manifest.Name, "", "L"), new Dictionary<string,double> { ["loose"] = 100 }),
  Recipes = new[] { new LoadedRecipe { Manifest = new RecipeManifest("loose", ing.Manifest.Name,
  new[] { ing.Manifest.Id }, Array.Empty<IncompatibilityRule>()), Ingredients = new[] { ing } } } }`.
- `SourceSha256` stays null (never came from a `.cbk`). The wrapper is a **view/edit scaffold only** — it
  is never persisted as a cookbook; saving goes to the `.igt` via the loose-save path (§2.2).
- The synthetic book **owns** the ingredient's images (it wraps the same `LoadedIngredient`); disposing the
  book disposes the ingredient. The editor does not own them (caller/host owns).

### 2.2 Editor loose-save (`IngredientEditorViewModel`)
- Add an **optional** ctor param `string? looseSavePath = null` (appended last; existing cookbook
  construction sites pass nothing and are unchanged). Store `_looseSavePath`.
- **`CanSave`**: when `_looseSavePath is not null` → `IsDirty && _ing.Manifest.Kind != LayerKind.Custom`
  (a loose ingredient always has a save target, so no `SourcePath` requirement); otherwise the existing
  cookbook rule (`IsDirty && _session.SourcePath is not null && kind != Custom && !IsSaving`). Keep
  `!IsSaving` in both.
- **`Save`**: branch at the top of the try —
  - **Loose** (`_looseSavePath is string loosePath`): `var (manifest, images) =
    IngredientDraftExporter.Export(_draft); try { await IngredientArchive.WriteAsync(loosePath, manifest,
    images); } finally { foreach (var i in images.Values) i.Dispose(); }` (the exporter's images are ours
    to dispose — `IngredientArchive.WriteAsync` only reads them). Set `IsDirty = false`. The `Saved` event
    is cookbook-specific; for loose, do not raise it (no book) — or raise a parameterless notification is
    unnecessary here since nothing listens. Crash-safety: write to a sibling temp then
    `File.Move(overwrite)` — mirror `CookBookPersistence` (extract a tiny shared atomic-write helper, or
    inline the temp+move around `IngredientArchive.WriteAsync`).
  - **Cookbook** (`_looseSavePath is null`): the existing flow, untouched.
  - Errors → the existing error dialog; `IsSaving` guards both; the loose path never touches
    `_session`/`_recipe`.
- Nothing else in the editor changes — `_draft`, paint, variant CRUD, colorize all already work from the
  ingredient + the (synthetic) canvas.

### 2.3 Landing Import (`LandingViewModel`)
- The `.igt` branch of `Import` (today: `_notify.Report("… needs the Kitchen (coming soon)")`) becomes:
  1. `LoadedIngredient ing; try { ing = IngredientArchive.Read(path); } catch (Exception ex) { ShowError…;
     return; }`
  2. If `ing.VariantImages.Count == 0` → `ShowError("Can't open", "This ingredient has no variants to
     edit."); ing.Dispose(); return;`
  3. `var book = LooseWorkspace.WrapIngredient(ing);` (canvas from the variants).
  4. Open the editor via a factory: `_nav.To(_looseEditorFactory(ing, book, path));` where the factory
     builds `new IngredientEditorViewModel(ing, book.Recipes[0], book, bridge, nav, notify, session,
     dialogs, looseSavePath: path)`.
  - **Session ownership:** the loose book is not opened into the `ICookBookSession` (that's for real
    `.cbk`s and would dispose the current cookbook). The loose editor owns the wrapped book's lifetime:
    dispose the synthetic book (and thus the ingredient) when the editor is disposed. Since the editor
    doesn't currently own its ingredient, the **factory/Landing** wires disposal — simplest: the loose
    editor's `Dispose` also disposes the wrapped book. (Add a `_ownedBook` field set only on the loose
    path.)
  - The `.cbk` and `.rcp` branches are unchanged (`.rcp` still the stub).
- A new injected factory `Func<LoadedIngredient, LoadedCookBook, string, IngredientEditorViewModel>
  _looseEditorFactory` (built in `ServiceRegistration`, capturing bridge/nav/notify/session/dialogs), so
  Landing stays free of the editor's other dependencies.

### 2.4 View
No new views — the Ingredient Editor renders the loose ingredient exactly as a cookbook one. The Save
button is enabled by the loose `CanSave`. Back returns to the previous page (Landing).

## 3. Data flow
```
Import(.igt) → IngredientArchive.Read(path) → (guard: has variants)
  → book = LooseWorkspace.WrapIngredient(ing)          // synthetic 1-recipe cookbook, canvas = variant size
  → editor = IngredientEditorViewModel(ing, book.Recipes[0], book, …, looseSavePath: path)  // owns `book`
  → nav.To(editor)
Save (loose):  export draft → IngredientArchive.WriteAsync(tmp) → File.Move(tmp, path, overwrite) → IsDirty=false
Editor.Dispose: dispose thumbnails/canvas/preview  +  dispose the owned synthetic book (→ the ingredient)
```

## 4. Error handling
- Unreadable/invalid `.igt` → error dialog on import, nothing opens.
- Zero-variant `.igt` → error dialog, not opened (can't derive a canvas / nothing to edit).
- Loose Save write failure → temp cleaned up, error dialog, `IsDirty` preserved for retry.
- **Disposal:** the loose editor owns exactly one thing extra — the synthetic book (which owns the
  ingredient's images). Dispose it once, in the editor's `Dispose`. The exporter's per-Save images are
  disposed right after each write (they're copies, not the draft's live maps).

## 5. Testing
- **Wrapper:** `WrapIngredient` yields a 1-recipe cookbook whose canvas equals the ingredient's variant
  size and whose single recipe lists the ingredient in `LayerOrder`.
- **Loose Save round-trip** (`[AvaloniaFact]`): write a small `.igt` to a temp dir; `IngredientArchive.Read`
  it; wrap + open the editor with `looseSavePath`; paint a known pixel; `await SaveCommand.ExecuteAsync`;
  re-read the `.igt` and assert the variant's `ValueMap.FromImage(...).GetValue` carries the painted value;
  no `.tmp` left.
- **CanSave (loose):** disabled when clean, enabled when dirty for a dynamic/static loose ingredient,
  disabled for a **custom** loose ingredient even when dirty.
- **Import wiring** (`[AvaloniaFact]`): `Import` of a real temp `.igt` navigates the nav to an
  `IngredientEditorViewModel`; a zero-variant `.igt` shows an error and does not navigate.
- **No regression:** the cookbook editor Save (`IngredientEditorSaveTests`) + all existing suites stay
  green (the new ctor param is optional/defaulted); build 0 warnings; no raw hex.
- **Manual smoke:** File → Import → pick a `.igt` → it opens in the editor → paint / add a variant / rename
  → Save → reopen the same `.igt` to confirm the edit persisted; a `.cbk` still opens the Explorer and a
  `.rcp` still shows the "coming soon" note.

## 6. Risks & escalation
- **Ingredient/book ownership** is the sharp edge. The loose editor now owns the wrapped book (→ the
  ingredient's images). It must dispose it exactly once and only on the loose path (`_ownedBook`), and must
  NOT dispose it on the cookbook path (there the session owns the book). Guard with a nullable
  `_ownedBook`.
- **Loose canvas from variants:** all variants of a valid `.igt` share one size (the archive's writer
  enforced it); taking the first variant's dimensions is safe. A hand-crafted broken `.igt` with mismatched
  sizes would already fail `Validator`; the editor just needs one canvas — first variant is fine, and a
  mismatch surfaces later at cook time, not here.
- **Custom loose ingredient:** paint reduces to grayscale value/alpha (the standing custom limitation), so
  Save stays blocked for custom — a loose custom `.igt` can be opened/viewed but not saved, matching the
  cookbook editor. Acceptable; full-colour custom editing is slice C.
- **Editor Save refactor:** the loose branch must not perturb the cookbook branch — keep them as two arms
  of one `if`, guard with the existing `IngredientEditorSaveTests`. If the shared atomic-write helper
  extraction gets fiddly, inline the temp+move for the loose path this slice and unify later.
- **Session isolation:** never `session.Open`/`Replace` for a loose file — that would dispose the user's
  currently-open cookbook. The loose editor is fully independent of the session.
