# nfty GUI — Import an image into a variant (C2) design spec

**Date:** 2026-08-01
**Status:** Approved (design), pending implementation planning
**Scope:** Let the editor **replace the selected variant's raster from a PNG on disk**. For a **custom**
ingredient the image is kept **full-colour** and saved as-is (which finally unblocks Save for custom);
for **dynamic/static** it is imported as the grayscale value-map they are defined to be. Replaces the
originally-planned "paint in RGBA" (C2), which would have required breaking `ValueMap`'s grayscale
guarantee or duplicating the whole Core command set.

## 0. Program bar
Rock-solid, efficient; best practices; pull docs (Context7) rather than assume any library API; escalate
anything off. **No `Nfty.Core` change** — `ValueMap` keeps its "grayscale by construction" guarantee,
which is what makes dynamic/static colorization safe. Token brushes only.

## 1. Motivation & the rule this settles
Today every variant starts blank and can only be painted, and the editor reduces a custom layer's
full-colour PNG to grayscale on load (spec §6 of the paint slice) — so a custom ingredient can be opened
but not meaningfully edited or saved. CLAUDE.md defines custom layers as *"full-colour RGBA images
composited as-is"*: they are **imported art**, not painted rasters. This slice makes that explicit:

| Kind | Editing model | Save |
|------|---------------|------|
| **Dynamic / Static** | painted grayscale value-map (as today) + **import replaces the map** | as today |
| **Custom** | **import only — painting disabled** | **enabled**, writes full-colour |

## 2. Goals & non-goals
**Goals**
- An **Import image…** action in the editor replaces the selected variant's raster from a user-chosen
  `.png`, validated against the ingredient's canvas size.
- **Custom:** the imported image is retained **verbatim** (full colour, no value-map round-trip) and Save
  writes it; variants not imported this session keep their **original** image. Painting tools are
  disabled for custom (they cannot round-trip), so nothing silently degrades colour.
- **Dynamic/Static:** the imported PNG becomes the variant's `ValueMap` (`ValueMap.FromImage`, i.e. its
  value+alpha), paintable as usual afterwards.
- Import marks the editor dirty and updates the canvas/preview/filmstrip.

**Non-goals (this slice)**
- **Undoing an import** (it replaces a whole raster; the variant's paint history is cleared — see §2.4).
  Scaling/cropping a mismatched image (rejected with a clear message — the CookBook canvas is the single
  source of truth). Importing a whole ingredient/recipe (that is the loose-import path, shipped).
  Painting custom layers in RGBA (explicitly replaced by this slice). Any `Nfty.Core` change.

## 3. Components

### 3.1 Editor import (`IngredientEditorViewModel`)
- Inject `IFilePickerService _picker` (new trailing ctor param, defaulted-free — construction sites are
  the two editor factories + tests).
- `[RelayCommand(CanExecute = nameof(CanImport))] private async Task ImportImage()`:
  1. `var path = await _picker.OpenFileAsync("Import variant image", ".png");` — null ⇒ cancelled.
  2. `using var img = Image.Load<Rgba32>(path)` (wrapped: a failure → error dialog).
  3. **Size guard:** if `(img.Width, img.Height) != (_draft.Canvas.Width, _draft.Canvas.Height)` → error
     ("This image is {W}×{H}; the canvas is {CW}×{CH}.") and return — nothing changes.
  4. **Custom:** store a clone in `_importedCustom[variantId]` (a `Dictionary<string, Image<Rgba32>>`
     the VM owns and disposes); **Dynamic/Static:** copy into the draft variant's `ValueMap` via
     `ValueMap.FromImage(img)` written pixel-by-pixel into the existing map (so the draft keeps its
     identity), and **clear that variant's `EditHistory`** (its snapshots describe pixels that no longer
     exist).
  5. `IsDirty = true`; rebuild surfaces + the filmstrip thumbnail for that variant.
- `CanImport => SelectedVariant is not null && !IsSaving`.

### 3.2 Custom save path (`IngredientEditorViewModel.Save`)
- `CanSave` drops its `Kind != Custom` exclusion. The new rule: Save is offered whenever there is a
  target (cookbook source or `looseSavePath`) and the draft is dirty.
- Export for a **custom** ingredient no longer uses the draft's grayscale maps. Build the image dict as:
  for each variant id — `_importedCustom[id]` if present, else the **original** `_ing.VariantImages[id]`
  (unchanged full colour). Manifest still comes from `IngredientDraftExporter.Export` (ids/names/weights
  are draft-owned and may have been renamed/reweighted/added).
  - A variant **added** in this session on a custom ingredient has no original and no import — it would
    have no image. Guard: for custom, `CanSave` additionally requires every variant to have an image
    (imported or original); otherwise show "Import an image for “{name}” before saving." This keeps the
    archive valid rather than writing a blank.
- **Dynamic/Static** save is unchanged (draft → `IngredientDraftExporter`).

### 3.3 Painting disabled for custom
- `ApplyToolStroke` early-returns when `_ing.Manifest.Kind == LayerKind.Custom`; expose
  `public bool CanPaint => _ing.Manifest.Kind != LayerKind.Custom;` so the view can disable the tool
  strip. The canvas for a custom ingredient shows the **original/imported full-colour image** (not the
  grayscale value-map) — see §3.4.

### 3.4 Custom canvas/preview rendering
- For custom, `RenderCanvas`/`RenderPreview` use the effective image (`_importedCustom[id]` ?? the
  original `_ing.VariantImages[id]`) rather than `ActiveMap.ToImage()`, so what the user sees is the real
  full-colour art. Dynamic/static rendering is unchanged (grayscale canvas + colorized preview).
- This removes the long-standing "custom shows grayscale" limitation from the paint slice's §6.

### 3.5 View (`IngredientEditorView.axaml`)
- An **Import…** button beside the variant Add/Duplicate/Delete row, bound `ImportImageCommand`.
- The tool strip is disabled when `!CanPaint` (`IsEnabled="{Binding CanPaint}"` on the tools panel), so a
  custom ingredient reads as import-only. Token styles; no raw hex.

## 4. Data flow
```
Import… → OpenFileAsync(".png") → Image.Load          (cancel/error → stop)
  → size == canvas ?                                   (mismatch → error, nothing changes)
  → custom:  _importedCustom[variantId] = clone        (full colour retained)
    dyn/sta: draft map ← ValueMap.FromImage(img); clear that variant's history
  → IsDirty = true; rebuild canvas/preview/thumbnail
Save (custom)  → manifest from the draft + images = imported ?? original (full colour) → archive
Save (dyn/sta) → unchanged (draft → IngredientDraftExporter)
```

## 5. Error handling
- Cancelled picker → no-op. Unreadable/invalid PNG → error dialog, nothing changes.
- Size mismatch → error naming both sizes, nothing changes.
- Custom with a variant lacking any image → Save disabled with an explanatory message (§3.2).
- **Disposal:** `_importedCustom` images are owned by the VM and disposed in `Dispose` (and when an entry
  is replaced by a later import of the same variant). The originals in `_ing.VariantImages` are **not**
  owned by the editor (the session/loose wrapper owns them) — never dispose those.

## 6. Testing
- **Import (dynamic):** a PNG of the right size replaces the variant's value-map (assert a known pixel
  via `ValueAt`), sets dirty, and clears that variant's undo history (`CanUndo` false).
- **Import (custom):** the imported image is retained full-colour — after Save, re-reading the archive
  shows a **non-grayscale** pixel (R≠G≠B) at a known position, proving no value-map round-trip.
- **Size mismatch** → error dialog, the map/import dict unchanged, not dirty.
- **Custom painting disabled:** `CanPaint` false for custom; `ApplyToolStroke` changes nothing.
- **Custom Save enabled:** previously blocked; now saves (with all variants having images) and is disabled
  when a session-added variant has no image.
- **Cancelled picker** → nothing changes.
- **No regression:** the paint/variant-CRUD/save/loose suites stay green; full suite green; build 0
  warnings; no raw hex; **no `Nfty.Core` diff**.
- **Visual:** render the editor over a custom ingredient with an imported colour image — the canvas shows
  full colour, the tool strip is visibly disabled.
- **Manual smoke:** open a custom ingredient → Import a PNG → it appears in full colour → Save → reopen to
  confirm the colour survived; open a dynamic ingredient → Import a grayscale PNG → paint over it → Save.

## 7. Risks & escalation
- **Two save paths** (custom = images as-is; dynamic/static = draft export) is the sharp edge: the custom
  path must never route through `ValueMap`, or colour is silently lost — the test asserting a non-gray
  pixel *after a round-trip* is the guard.
- **History vs. wholesale replacement:** importing over a painted dynamic variant invalidates its undo
  snapshots, so that variant's history is cleared (documented, tested). Import itself is not undoable this
  slice.
- **Ownership:** imported images are VM-owned and disposed; originals are not. Getting this backwards
  either leaks or disposes images the session still renders — mirror the care from the loose slices.
- **Custom + added variant:** an added-but-never-imported custom variant has no image; Save is gated
  rather than writing a blank raster. If that gate reads confusingly in the smoke, escalate.
- **Canvas equality is strict:** no scaling. If real art frequently mismatches, an explicit
  scale-on-import is a follow-up — do not silently resize here.
