# nfty GUI — Ingredient Editor Save / persist (design spec)

**Date:** 2026-07-31
**Status:** Approved (design), pending implementation planning
**Scope:** Second of two editing slices. Persist the editor's painted `IngredientDraft` back to the
source `.cbk` on disk, and reflect the change live in the running app. Builds directly on Slice 1
(2026-07-26, painting + undo/redo), which left `Save` a `_notify.Report("Save ingredient")` stub.

## 0. Program bar
Rock-solid, efficient; best practices; pull docs rather than assume; escalate anything off. Reuses the
existing `Nfty.Core.Editing` export seam (`IngredientDraftExporter`, `CookBookEdits.UpsertIngredient`)
and `Nfty.Core.Formats.CookBookArchive` — **no new engine capability**. The one small Core-adjacent
concern (recomputing the archive hash) is handled in `Nfty.App` without touching `Nfty.Core`. Visual
polish of the editor is a later dedicated pass; this slice adds no new screen, only wires the existing
Save button + tooltips/guards.

## 1. Goals & non-goals
**Goals**
- **Save** writes the edited ingredient back into the source `.cbk`: export the draft → splice it into
  the open cookbook (`UpsertIngredient`) → write the whole archive to disk (crash-safe temp-then-replace)
  → refresh the in-memory graph so the Explorer shows the edit immediately (the user's chosen
  "splice + live refresh", not a full reload).
- The **source `.cbk` path** is tracked by the session so Save knows where to write.
- Guards: Save is disabled unless there are unsaved edits, a source `.cbk` exists, and the layer is not
  Custom; a mid-write in-flight state disables re-entry; navigating Back with unsaved edits confirms.

**Non-goals (this slice)**
- **Colorization persistence.** The Colorize rail (Dynamic/Static, hue/sat ranges, fixed colour,
  quantize) stays a **preview-only** control — Save keeps the ingredient's original `Colorization` and
  writes only the painted value-maps. (Chosen scope.)
- **Custom (full-colour) layer save.** Blocked this slice — see §3 / §6.
- Add / duplicate / delete variant (still `_notify` stubs). "Save As" / choosing a different target file.
  A full reload of the archive after save. Any `Nfty.Core` change.

## 2. Components

### 2.1 Session tracks the source path (`ICookBookSession` / `CookBookSession`)
- Add `string? SourcePath { get; }`. Change `Open(LoadedCookBook book)` → `Open(LoadedCookBook book,
  string? sourcePath = null)`; it records the path alongside the book. `Close()` clears it.
- Add `void Replace(LoadedCookBook book)`: sets `Current = book` **without disposing** the previous book
  (the incoming graph from `UpsertIngredient` reuses the previous book's images — disposing would kill
  images the new graph still references), keeps `SourcePath` unchanged, and raises `Changed`. Contrast
  with `Open`, which *does* dispose the previous book (a genuinely different book, no shared images).
- **Callers:** `LandingViewModel.OpenCookBook` already has the `path` it read from — pass it to
  `Open(book, path)`. New-cookbook creation and `.igt`/`.rcp` import call `Open(book, null)` (no source
  file ⇒ Save disabled).

### 2.2 Save on the editor (`IngredientEditorViewModel`)
The editor is injected with `ICookBookSession` (added to its DI factory). `Save` becomes an
`AsyncRelayCommand` gated by `CanSave` (§3):
1. `var (manifest, images) = IngredientDraftExporter.Export(_draft);` → `newIng = new LoadedIngredient
   { Manifest = manifest, VariantImages = images };` (caller owns `images`; they become `book2`'s).
2. `var book2 = CookBookEdits.UpsertIngredient(_session.Current!, _recipe.Manifest.Id, newIng);`
3. Write crash-safe: `WriteAsync` to `SourcePath + ".tmp"` (a sibling temp), then atomically replace the
   original (`File.Move(tmp, SourcePath, overwrite: true)`; on failure delete the temp in a `finally`).
   `CookBookArchive.WriteAsync` opens with `ZipArchiveMode.Create`, which **throws on an existing file**,
   so writing in place is impossible — the temp is required, not just safer.
4. Recompute the archive hash in-app (mirroring `ArchiveIo.HashFile` exactly:
   `Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()`), and produce the final graph
   `book3 = book2 with { SourceSha256 = <hash> }` so a later Cook records the correct provenance.
5. `_session.Replace(book3);` then **dispose the replaced ingredient's old images**
   (`foreach (var img in _ing.VariantImages.Values) img.Dispose();` — `UpsertIngredient` orphans them and
   documents that the caller owns their lifetime). Set `_ing = newIng` (so a second Save targets the
   right ingredient and never re-disposes freed images). Clear `IsDirty`.
6. Raise `Saved?.Invoke(book3)` (§2.4).
- **Errors:** any exception (write failure, IO) surfaces through the existing error dialog
  (`IDialogService` + `ErrorDialogViewModel`); the temp is cleaned up; `IsDirty` stays true so the user
  can retry. `IsSaving` (an `[ObservableProperty]`, `[NotifyCanExecuteChangedFor(nameof(SaveCommand))]`)
  disables the button during the await.
- Prefer the async archive API (`WriteAsync`) with a `CancellationToken`; Save is not itself cancelable
  from the UI this slice, but plumb the token for symmetry.

### 2.3 Guards (`CanSave`, dirty, discard)
- `IsDirty`: a `bool` set true whenever `ApplyToolStroke` runs a command (Slice 1 seam), cleared on
  successful Save. (It inherits Slice 1's known wart — a no-op edit still marks dirty; the already-filed
  follow-up tightens this. Acceptable here.)
- `CanSave = IsDirty && _session.SourcePath is not null && _ing.Manifest.Kind != LayerKind.Custom &&
  !IsSaving`. Backed by `[NotifyCanExecuteChangedFor(nameof(SaveCommand))]` on `IsDirty`/`IsSaving`
  (and set once at construction for the static conditions).
- **Custom layers are blocked:** the draft is a grayscale `ValueMap` (value + alpha), so exporting it for
  a Custom ingredient would overwrite the original full-colour PNGs with grayscale — data loss.
  Dynamic/static layers are *already* grayscale value-maps, so their export round-trips losslessly. A
  disabled-state tooltip explains ("Saving full-colour custom layers isn't supported yet"); a
  no-source-file tooltip explains ("Open a .cbk file to save").
- **Discard guard:** `Back` becomes async — if `IsDirty`, confirm "Discard unsaved edits?" via
  `IDialogService` before `_nav.Back()`; otherwise navigate straight back.

### 2.4 Explorer live refresh (`ExplorerViewModel`)
- The editor exposes `public event Action<LoadedCookBook>? Saved;`. `ExplorerViewModel` subscribes when
  it constructs+navigates the editor (it already does `_nav.To(_editorFactory(i, r, _book))`), and
  unsubscribes on the editor's disposal / navigation away.
- On `Saved(book2)`: update Explorer's book reference, rebuild the tree (`BuildTree(book2)`), and
  re-select the **same ingredient by id** so the detail pane + filmstrip re-render from the new images.
  `_book` becomes mutable; `Root` becomes an observable/notifying member (with `Roots` re-notified) so
  the bound `TreeView` picks up the rebuild.
- Because `Save` also calls `_session.Replace(book2)`, any subsequent consumer (Cook dialog, re-open)
  sees the saved graph too.

## 3. Data flow
```
Save (enabled: dirty, source .cbk exists, kind != Custom, not already saving)
  → IngredientDraftExporter.Export(_draft)            [Nfty.Core.Editing]  → manifest + grayscale images
  → newIng = LoadedIngredient{ manifest, images }
  → book2 = CookBookEdits.UpsertIngredient(session.Current, recipe.Id, newIng)   [Nfty.Core.Editing]
  → CookBookArchive.WriteAsync(sourcePath+".tmp", book2.Manifest, book2.Recipes) [Nfty.Core.Formats]
  → File.Move(tmp, sourcePath, overwrite:true)        (temp deleted on any failure)
  → book3 = book2 with { SourceSha256 = sha256(sourcePath) }
  → session.Replace(book3)  (no dispose; raises Changed)
  → dispose _ing's old images; _ing = newIng; IsDirty = false
  → Saved(book3)  → ExplorerViewModel rebuilds tree from book3, reselects the ingredient
Back → if IsDirty: confirm "Discard unsaved edits?" → nav.Back(); else nav.Back()
```

## 4. Error handling
- Write/IO failure → temp cleaned up (`finally`), error surfaced via `ErrorDialogViewModel`, `IsDirty`
  preserved for retry, `IsSaving` reset. The on-disk original is never left half-written (temp-then-move).
- `UpsertIngredient` throws `KeyNotFoundException` if the recipe id is absent — not expected (the editor
  was opened from that recipe) but surfaced through the same dialog rather than crashing.
- A null `SourcePath` can't reach Save (guarded by `CanSave`); belt-and-suspenders, Save early-returns
  if it's null.

## 5. Testing
- **Session:** `Open(book, path)` exposes `SourcePath`; `Close()` clears it; `Replace(book2)` swaps
  `Current`, raises `Changed`, and does **not** dispose the previous book (assert an image from the old
  book is still usable after Replace).
- **Save round-trip** (the core test): build an editor over a dynamic fixture ingredient in a temp-dir
  `.cbk`, paint a known pixel via `ApplyToolStroke`, `await SaveCommand.ExecuteAsync(null)`, then
  `CookBookArchive.Read` the file back and assert the ingredient's variant image carries the painted
  value at that pixel. Assert the original `.cbk` is a valid archive (no leftover `.tmp`).
- **CanSave states:** disabled when clean (no edits); disabled for a Custom-kind ingredient even when
  dirty; disabled when `SourcePath` is null; enabled for a dirty dynamic/static ingredient with a source.
- **Explorer refresh:** raise the editor's `Saved(book2)` (or drive a real Save) and assert Explorer's
  tree rebuilt and the same ingredient node is reselected (its detail VM points at the new ingredient).
- **Discard guard:** dirty + Back → confirm dialog shown; clean + Back → navigates without a dialog.
- **Failure path:** point Save at an unwritable target (or force `WriteAsync` to throw) → error dialog
  shown, no `.tmp` left behind, `IsDirty` still true.
- Full suite green; build 0 warnings; no raw hex outside `Tokens.axaml`.
- **Manual smoke:** run the desktop app, open a real `.cbk`, edit a dynamic/static ingredient, Save,
  navigate back → the Explorer thumbnail reflects the edit; reopen the `.cbk` from disk → the edit
  persisted. Confirm Save is disabled on a custom layer and on a freshly-created (unsaved) cookbook.

## 6. Risks & escalation
- **Custom full-colour data loss** — the motivating guard. Blocking Save for Custom is deliberate; if a
  user needs to edit a custom layer, that's a future slice that preserves colour (paint on RGBA, not a
  value-map). Do not "fix" it by silently exporting grayscale.
- **Shared-image disposal** — the sharp edge. `book2` reuses the previous book's images for every
  *unchanged* ingredient, so the session must `Replace` (not `Open`/dispose) and the editor must dispose
  **only** the replaced ingredient's old images, exactly once. Getting this wrong either leaks (never
  disposed) or crashes on a later render (disposed while still referenced). Covered by the Replace test +
  a second-save-doesn't-double-dispose consideration (`_ing = newIng`).
- **Atomic replace across filesystems** — `File.Move(tmp, dest, overwrite:true)` with the temp a sibling
  of the destination keeps it same-volume, so the move is atomic; do not place the temp in the system
  temp dir (cross-volume move degrades to copy+delete, losing atomicity).
- **Hash format drift** — the in-app hash must match `ArchiveIo.HashFile` byte-for-byte
  (lowercase hex SHA-256) or a saved-then-cooked Set records a wrong `cookbookSha256`. Mirror it exactly;
  a Core-side public hasher is an option if drift is a concern, but is avoided this slice to keep
  `Nfty.Core` untouched.
- **Explorer state after refresh** — rebuilding the tree must preserve selection by id; if the edited
  ingredient somehow vanished (it can't, Upsert re-adds it) the refresh falls back to the cookbook root
  rather than throwing.
