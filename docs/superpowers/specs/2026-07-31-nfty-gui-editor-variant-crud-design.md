# nfty GUI — Ingredient Editor variant CRUD (design spec)

**Date:** 2026-07-31
**Status:** Approved (design), pending implementation planning
**Scope:** First authoring/mutation slice (A1). Add / duplicate / delete / rename / reweight the variants
of the ingredient open in the editor, mutating the in-memory `IngredientDraft`; the existing Save
persists them. Builds on the two editing slices (paint+undo, Save/persist) already on `main`.

## 0. Program bar
Rock-solid, efficient; best practices; pull docs rather than assume; escalate anything off. Reuse the
existing editor draft/history/Save machinery and the `IngredientDraftExporter` (Save needs **no**
change — it already writes `draft.Variants`). Small, justified `Nfty.Core.Editing` additions
(`ValueMap.Clone`, `IngredientDraft.DuplicateVariant`/`RemoveVariant`). Visual polish is the later
dedicated pass; make it clean.

## 1. Goals & non-goals
**Goals**
- The editor's **Add / Duplicate / Delete** variant buttons (currently `_notify` stubs) mutate the open
  `IngredientDraft` and its filmstrip, keeping the three parallel structures — the draft's
  `List<VariantDraft>`, the VM's `ObservableCollection<EditorVariant>` filmstrip, and the per-variant
  `Dictionary<string,EditHistory>` — consistent, all keyed by variant id.
- **Rename / reweight** the selected variant via inline controls (name + weight).
- Every mutation marks the editor dirty; the existing **Save** writes the new variant set back to the
  `.cbk` with no Save-path change.
- **Delete** is disabled when a single variant remains (a zero-variant ingredient can't generate) and
  prompts for confirmation first (reusing the Slice-2 `ConfirmDialog`).

**Non-goals (this slice)**
- Variant **reordering**. Per-variant colorization. Any structural CRUD on ingredients or recipes (that
  is slice A2). Importing an external image as a variant (variants here are painted value-maps; a new
  variant starts blank or is a copy). Undo/redo of variant add/delete (the paint `EditHistory` is
  per-variant and unaffected; list-level undo is out of scope).

## 2. Components

### 2.1 Core additions (`Nfty.Core.Editing`)
- `ValueMap.Clone()` → a new `ValueMap` with copied value+alpha buffers (used by Duplicate). Exact-pixel
  tested: mutating the clone doesn't touch the source and vice-versa.
- `IngredientDraft`:
  - `AddVariant(id, name, weight)` already exists (blank `ValueMap.ForCanvas(Canvas)`).
  - `VariantDraft DuplicateVariant(string sourceId, string newId, string newName)` — appends a new
    `VariantDraft(newId, newName, source.Weight, source.Map.Clone())`, returns it. Throws if `sourceId`
    is absent or `newId` already exists.
  - `void RemoveVariant(string id)` — removes by id; throws if absent. (Does not enforce a minimum — the
    ≥1 guard is a UI policy, §2.3.)
  - `Name`/`Weight` on `VariantDraft` are already settable (rename/reweight mutate them directly).
- **Unique-id policy:** the editor generates new ids as the smallest unused `variant-N` (N ≥ 1) over the
  draft's current ids — deterministic, no RNG, ordinal-stable. Ids are immutable once created (they key
  history and any downstream references); rename changes only the display `Name`.

### 2.2 Editor VM (`IngredientEditorViewModel`)
Replace the three `_notify` stubs with real commands. A shared private helper adds/removes a filmstrip
`EditorVariant` and its history alongside the draft change, then selects the affected variant and sets
`IsDirty = true`.
- **`AddVariant`** → `_draft.AddVariant(NextId(), "Variant {n}", 1)`; new `EditHistory`; render a
  filmstrip thumbnail; append `EditorVariant`; select it.
- **`DuplicateVariant`** (enabled when a variant is selected) → `_draft.DuplicateVariant(SelectedVariant.Id,
  NextId(), "{name} copy")`; new `EditHistory`; thumbnail; append; select the copy.
- **`DeleteVariant`** (`CanExecute = Variants.Count > 1`) → confirm via `ConfirmDialog`
  ("Delete variant?" / "Delete") ; on confirm remove from `_draft`, `_history`, and `Variants`, dispose
  the removed filmstrip thumbnail, and select a neighbor (previous, else first).
- **Rename / reweight:** `SelectedName` (string) and `SelectedWeight` (double) `[ObservableProperty]`s
  mirror the selected variant. Setting them writes through to the selected `VariantDraft` (`Name`/`Weight`)
  and replaces the filmstrip `EditorVariant` record (records are immutable) with the updated name/weight,
  then sets `IsDirty`. Validation: an empty/whitespace name is rejected (kept at the last valid value);
  weight is clamped to `> 0` (a `NumericUpDown` with `Minimum` just above 0, e.g. 0.01). Changing the
  selected variant refreshes `SelectedName`/`SelectedWeight` from the new selection.
- **Thumbnails:** Add/Duplicate render a filmstrip bitmap the same way the ctor does
  (`VariantImagery.Render`-equivalent over the draft map, or `_bridge.ToBitmap(map.ToImage())` for the
  grayscale value-map) — match the ctor's approach so a new variant looks like existing ones.
- Save is unchanged; `IsDirty`/`CanSave` already gate it. Editor `Dispose` already disposes filmstrip
  thumbnails — ensure any thumbnail removed mid-session is disposed at removal.

### 2.3 Guards & policy
- **Delete ≥1:** `DeleteVariantCommand.CanExecute = Variants.Count > 1`; re-notified after every
  add/delete. Confirm dialog before the actual delete.
- **Duplicate/rename/reweight** require a selection (`SelectedVariant is not null`); disabled otherwise
  (a zero-variant ingredient shows an empty filmstrip — Add is always enabled).
- **Custom layers:** variant CRUD is allowed (it edits the draft's grayscale value-maps like paint), but
  **Save remains blocked for custom** (Slice-2 policy) — so custom variant edits stay in-memory only,
  consistent with the existing custom limitation. No new behavior here.

### 2.4 View (`IngredientEditorView`)
- The Add / Duplicate / Delete buttons already exist and bind `AddVariantCommand` /
  `DuplicateVariantCommand` / `DeleteVariantCommand` — no rebinding needed; Delete now disables on the
  last variant via `CanExecute`.
- Add an inline **name `TextBox`** (bound `SelectedName`) and a **weight `NumericUpDown`**
  (bound `SelectedWeight`, `Minimum="0.01"`) for the selected variant, near the filmstrip. Token styles;
  no raw hex. Disabled/empty when no variant is selected.

## 3. Data flow
```
Add    → _draft.AddVariant(NextId(),"Variant n",1) + new EditHistory + EditorVariant(thumb) → select → IsDirty
Dup    → _draft.DuplicateVariant(selId, NextId(), "name copy")  (ValueMap.Clone) + history + thumb → select → IsDirty
Delete → ConfirmDialog → _draft.RemoveVariant(selId); _history.Remove; Variants.Remove(+dispose thumb) → select neighbor → IsDirty
Rename → SelectedName set → selectedDraft.Name = value; replace filmstrip record → IsDirty
Reweight → SelectedWeight set → selectedDraft.Weight = value; replace filmstrip record → IsDirty
Save   → (unchanged) IngredientDraftExporter.Export(_draft) → …atomic write… → persists the new variant set
```

## 4. Error handling
- `DuplicateVariant`/`RemoveVariant` throw on a bad id — not reachable from the UI (ids come from the
  live selection/draft), but surfaced through the editor's error path if they ever do.
- Rename to empty / weight ≤ 0 are prevented at the property setter (revert to last valid), not thrown.

## 5. Testing
- **Core** (exact-pixel + list, self-contained per convention): `ValueMap.Clone` is an independent deep
  copy; `IngredientDraft.DuplicateVariant` appends a same-pixels, same-weight, new-id variant and rejects
  a duplicate id; `RemoveVariant` removes by id and rejects an absent id.
- **VM** (`[AvaloniaFact]`): Add appends a blank variant to draft+filmstrip+history and selects it, dirty
  set; Duplicate copies the selected variant's painted pixels (paint a pixel, duplicate, assert the copy's
  `ValueAt` matches) with a distinct id, selected; Delete removes from all three and selects a neighbor;
  `DeleteVariantCommand.CanExecute` is false with one variant, true with two; rename writes through to the
  draft + filmstrip and rejects empty; reweight writes through and rejects ≤ 0; a new/duplicated variant's
  id is unique.
- **Round-trip** (reuses the Slice-2 on-disk fixture): Add a variant, paint it, Save, re-read the `.cbk`,
  assert the ingredient now has the extra variant with its painted value.
- Full suite green; build 0 warnings; no raw hex outside `Tokens.axaml`.
- **Visual:** render the editor after adding a variant → the filmstrip shows the new entry and the
  name/weight controls (both themes). Manual smoke: add/duplicate/delete/rename/reweight in the running
  app, Save, reopen the `.cbk`, confirm the variant set persisted.

## 6. Risks & escalation
- **Three-structure sync** is the sharp edge: `_draft.Variants`, `Variants` (filmstrip), and `_history`
  must stay consistent and id-keyed through every op. A shared add/remove helper (one place that touches
  all three) avoids drift; the VM tests assert all three after each op.
- **Immutable filmstrip record on rename/reweight:** `EditorVariant` is a record, so rename/reweight must
  *replace* the collection item (not mutate it) to update the bound filmstrip — and re-select it so the
  selection stays on the same logical variant. If this reads badly (selection flicker), escalate.
- **Thumbnail lifetime:** a removed variant's thumbnail must be disposed at removal (not only in editor
  `Dispose`), and a replaced filmstrip record (rename/reweight) must reuse or dispose the old thumbnail to
  avoid a leak — rename/reweight don't change pixels, so reuse the existing thumbnail rather than
  re-rendering.
- **Custom + Save:** variant edits on a custom layer stay in memory (Save blocked) — matches the existing
  limitation; don't special-case it into a data-loss path.
