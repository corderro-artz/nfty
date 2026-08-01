# nfty GUI — Import an image into a variant (C2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Replace the selected variant's raster from a PNG — full-colour for custom (unblocking Save for custom), value-map for dynamic/static.

**Architecture:** The editor gains an `IFilePickerService` and an `ImportImage` command. Custom ingredients keep imported images verbatim in a VM-owned `_importedCustom` dict, render from them, have painting disabled, and save them as-is; dynamic/static import into the draft's `ValueMap` (clearing that variant's history). No `Nfty.Core` change.

**Tech Stack:** .NET 10, Avalonia 11.2.3, CommunityToolkit.Mvvm, ImageSharp 3.1.11 (`Image.Load<Rgba32>`), xUnit + Avalonia.Headless.XUnit.

## Global Constraints
- **No `Nfty.Core` change.** `ValueMap` keeps its grayscale-by-construction guarantee — the custom save path must NEVER route through it (that is what preserves colour).
- **Ownership:** `_importedCustom` images are VM-owned (disposed in `Dispose`, and when an entry is replaced). `_ing.VariantImages` originals are NOT owned by the editor — never dispose them.
- **Strict canvas match:** an imported PNG whose size ≠ the draft canvas is rejected with a message naming both sizes. No scaling.
- **Custom rule:** painting disabled (`CanPaint` false, `ApplyToolStroke` early-returns); canvas/preview render the effective full-colour image; Save writes `imported ?? original` per variant and is gated so a session-added custom variant with no image can't be saved blank.
- Determinism/idiom: no RNG; token brushes only (no raw hex); `[AvaloniaFact]` for Avalonia tests. Build 0 warnings. Conventional commits. Agents: caveman-ultra terse chat; code/commits/reports normal prose. Use Context7 for any uncertain ImageSharp/Avalonia API rather than guessing.

## File Structure
- `src/Nfty.App/ViewModels/IngredientEditorViewModel.cs` — picker injection, `ImportImage`, `_importedCustom`, custom render + save + `CanPaint`/`CanSave` (T1–T3).
- `src/Nfty.App/ServiceRegistration.cs` + the two editor factories + all editor construction sites — new ctor param (T1).
- `src/Nfty.App/Views/IngredientEditorView.axaml` — Import button + tool-strip disable (T4).
- Tests: `tests/Nfty.App.Tests/IngredientEditorImportTests.cs` (create, T1–T3).

---

### Task 1: Import into a dynamic/static variant

**Files:** Modify `IngredientEditorViewModel.cs`, `ServiceRegistration.cs`, editor construction sites; Test `tests/Nfty.App.Tests/IngredientEditorImportTests.cs` (create).

**Interfaces:** Produces `ImportImageCommand` (async), `CanImport`; editor ctor gains a trailing `IFilePickerService picker`.

- [ ] **Step 1: Failing test** — create the test file with a helper that writes a PNG of a given size/colour to a temp dir, then:
```csharp
    [AvaloniaFact]
    public async Task Import_replaces_a_dynamic_variants_value_map_and_clears_its_history()
    {
        // fixture: IngredientEditorSaveTests.OnDisk() is a DYNAMIC 8x8 ingredient
        // paint first so there IS history, then import a solid-180 8x8 png
        // assert: ValueAt(4,4) == 180, IsDirty true, UndoCommand.CanExecute false (history cleared)
    }

    [AvaloniaFact]
    public async Task Import_rejects_a_size_mismatch()
    {
        // import a 4x4 png into the 8x8 ingredient
        // assert: error dialog shown, ValueAt unchanged, IsDirty false
    }

    [AvaloniaFact]
    public async Task Cancelled_import_changes_nothing()
    {
        // picker returns null → nothing changes, not dirty
    }
```
  Write these as real, complete tests (the sketch above is the intent; implement fully, mirroring the fixture/dialog-stub style of `IngredientEditorSaveTests`/`ExplorerAddLooseTests` — a `SavePicker`-style stub whose `OpenFileAsync` returns the png path, and a dialogs stub recording `ErrorTitle`).

- [ ] **Step 2: Run — fail** (ctor arity / `ImportImageCommand` missing).

- [ ] **Step 3: Implement** in `IngredientEditorViewModel.cs`:
  - Field `private readonly IFilePickerService _picker;`; ctor gains a trailing `IFilePickerService picker` (BEFORE the existing optional `string? looseSavePath = null` — required params must precede optional ones); assign it.
  - Add:
    ```csharp
    private bool CanImport() => SelectedVariant is not null && !IsSaving;

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportImage()
    {
        if (ActiveDraft is not { } target) return;
        string? path;
        try { path = await _picker.OpenFileAsync("Import variant image", ".png"); }
        catch (Exception ex) { await ShowErrorAsync("Could not import", ex.Message); return; }
        if (path is null) return;   // cancelled

        Image<Rgba32> img;
        try { img = Image.Load<Rgba32>(path); }
        catch (Exception ex) { await ShowErrorAsync("Could not import", ex.Message); return; }
        try
        {
            var canvas = _draft.Canvas;
            if (img.Width != canvas.Width || img.Height != canvas.Height)
            {
                await ShowErrorAsync("Wrong size",
                    $"This image is {img.Width}×{img.Height}; the canvas is {canvas.Width}×{canvas.Height}.");
                return;
            }
            // dynamic/static: the PNG becomes the variant's value-map (custom handled in Task 2)
            var src = ValueMap.FromImage(img);
            for (int y = 0; y < canvas.Height; y++)
                for (int x = 0; x < canvas.Width; x++)
                    target.Map.Set(x, y, src.GetValue(x, y), src.GetAlpha(x, y));
            _history[target.Id] = new EditHistory();   // old snapshots describe pixels that are gone
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();
            IsDirty = true;
            RebuildSurfaces();
            RefreshThumbnail(target.Id);
        }
        finally { img.Dispose(); }
    }

    /// <summary>Re-render one filmstrip entry's thumbnail after its pixels changed.</summary>
    private void RefreshThumbnail(string variantId)
    {
        var entry = Variants.FirstOrDefault(v => v.Id == variantId);
        var vd = _draft.Variants.FirstOrDefault(v => v.Id == variantId);
        if (entry is null || vd is null) return;
        var old = entry.Thumbnail;
        entry.Thumbnail = RenderThumb(vd.Map);
        old.Dispose();
    }
    ```
  - Add a `private async Task ShowErrorAsync(string title, string message) => await _dialogs.ShowAsync<object>(new ErrorDialogViewModel(_dialogs, title, message));` if no equivalent exists.
  - Add usings for `SixLabors.ImageSharp`/`PixelFormats` if absent.
  - **Construction sites:** update `ServiceRegistration`'s two editor factories (pass `sp.GetRequiredService<IFilePickerService>()`), `ExplorerViewModelTests.EditorFactory`/`LooseEditorFactory`, and every `new IngredientEditorViewModel(...)` in tests/capture. Build-and-fix until 0 errors (`new FilePickerService()` — the no-op stub — is fine where a test doesn't import).

- [ ] **Step 4: Run — pass;** whole App suite green; `dotnet build src/Nfty.Desktop --nologo` 0 warnings.
- [ ] **Step 5: Commit** `feat(gui): import a PNG into a dynamic/static variant`

---

### Task 2: Custom = full-colour import, painting disabled, rendered as-is

**Files:** Modify `IngredientEditorViewModel.cs`; Test append to `IngredientEditorImportTests.cs`.

**Interfaces:** Produces `_importedCustom`, `CanPaint`, custom-aware `RenderCanvas`/`RenderPreview`/`RenderThumb`.

- [ ] **Step 1: Failing tests** — a CUSTOM fixture ingredient (adapt `IngredientEditorSaveTests.OnDisk`'s builder, or add a `LayerKind` parameter to it):
```csharp
    [AvaloniaFact]
    public async Task Import_into_a_custom_variant_keeps_full_colour()
    { /* import a png with a NON-gray pixel (e.g. 10,200,40); assert the effective image keeps R!=G!=B */ }

    [AvaloniaFact]
    public void Custom_cannot_be_painted()
    { /* CanPaint false; ApplyToolStroke changes nothing, does not dirty */ }
```

- [ ] **Step 2: Run — fail.**

- [ ] **Step 3: Implement:**
  - `private readonly Dictionary<string, Image<Rgba32>> _importedCustom = new(StringComparer.Ordinal);`
  - `private bool IsCustom => _ing.Manifest.Kind == LayerKind.Custom;`
  - `public bool CanPaint => !IsCustom;`
  - `private Image<Rgba32>? EffectiveCustomImage(string variantId) => _importedCustom.TryGetValue(variantId, out var im) ? im : (_ing.VariantImages.TryGetValue(variantId, out var o) ? o : null);`
  - In `ImportImage`, branch before the value-map path:
    ```csharp
    if (IsCustom)
    {
        if (_importedCustom.TryGetValue(target.Id, out var prev)) { _importedCustom.Remove(target.Id); prev.Dispose(); }
        _importedCustom[target.Id] = img.Clone();   // VM owns this copy
        IsDirty = true; RebuildSurfaces(); RefreshThumbnail(target.Id);
        return;   // (inside the try; `img` is disposed by the finally)
    }
    ```
  - `ApplyToolStroke`: `if (IsCustom) return;` at the top.
  - `RenderCanvas`/`RenderPreview`/`RenderThumb`: when `IsCustom`, render `EffectiveCustomImage(id)` via `_bridge.ToBitmap(...)` (no colorize, no value-map); a null effective image → keep today's blank behaviour. Dynamic/static paths unchanged.
  - `Dispose`: `foreach (var i in _importedCustom.Values) i.Dispose();`

- [ ] **Step 4: Run — pass;** suite green; 0 warnings.
- [ ] **Step 5: Commit** `feat(gui): custom variants import full colour and are not painted`

---

### Task 3: Custom Save writes full colour

**Files:** Modify `IngredientEditorViewModel.cs`; Test append.

- [ ] **Step 1: Failing test** — the headline guarantee:
```csharp
    [AvaloniaFact]
    public async Task Custom_save_round_trips_full_colour()
    {
        // custom ingredient in a .cbk; import a png with pixel (10,200,40); SaveCommand;
        // re-read the archive and assert that pixel is still (10,200,40) — R!=G!=B proves
        // it did NOT go through the grayscale ValueMap.
    }

    [AvaloniaFact]
    public void Custom_save_is_blocked_when_a_variant_has_no_image()
    { /* add a variant on a custom ingredient (no import) → CanSave false */ }
```

- [ ] **Step 2: Run — fail** (Save still excludes custom / routes through the draft).

- [ ] **Step 3: Implement:**
  - `CanSave`: drop `&& _ing.Manifest.Kind != LayerKind.Custom`; add `&& (!IsCustom || AllCustomVariantsHaveImages)` where
    `private bool AllCustomVariantsHaveImages => _draft.Variants.All(v => EffectiveCustomImage(v.Id) is not null);`
  - In `Save`, remove the `Kind == Custom` early return, and build the export for custom without the draft's maps:
    ```csharp
    var (manifest, images) = IngredientDraftExporter.Export(_draft);
    if (IsCustom)
    {
        foreach (var i in images.Values) i.Dispose();       // discard the grayscale export
        images = _draft.Variants.ToDictionary(v => v.Id,
            v => EffectiveCustomImage(v.Id)!.Clone(), StringComparer.Ordinal);   // fresh copies we own
    }
    ```
    then the existing write paths use `manifest`/`images` as before. Ensure BOTH the cookbook path (which hands `images` to `UpsertIngredient` → the book adopts them) and the loose path (which disposes `images` after writing) still hold: for the cookbook path the clones are adopted (do NOT dispose); for the loose path they are disposed in its existing `finally`.
  - Keep everything else (dirty/IsSaving/error dialog/Saved event) unchanged.

- [ ] **Step 4: Run — pass;** whole App suite green (the dynamic save round-trip must still pass); 0 warnings.
- [ ] **Step 5: Commit** `feat(gui): custom ingredients save their full-colour images`

---

### Task 4: View — Import button + disabled tools for custom

**Files:** Modify `src/Nfty.App/Views/IngredientEditorView.axaml`.

- [ ] **Step 1:** Add `<Button Content="Import…" Command="{Binding ImportImageCommand}" Classes="tbtn" />` to the variant toolbar row (beside Add/Duplicate/Delete). Add `IsEnabled="{Binding CanPaint}"` to the tool-strip `StackPanel` (the one holding Brush/Eraser/…/Fill + Undo/Redo + Value + Brush size). Token styles; no raw hex.
- [ ] **Step 2:** `dotnet build src/Nfty.Desktop --nologo` 0 warnings; `dotnet test tests/Nfty.App.Tests --nologo` green; `grep -rniE "#[0-9a-fA-F]{6}" src/Nfty.App/Views/IngredientEditorView.axaml` → nothing.
- [ ] **Step 3: Commit** `feat(gui): editor Import button; tools disabled for custom layers`

---

### Task 5: Verification (orchestrator)

- [ ] `dotnet build nfty.sln --nologo` → 0 warnings; `dotnet test nfty.sln --nologo` → all pass (report totals).
- [ ] `git diff --name-only <base>..HEAD -- src/Nfty.Core/` → empty.
- [ ] Visual: render the editor over a custom ingredient with an imported colour image (canvas shows colour, tools visibly disabled).
- [ ] Manual smoke (user): custom ingredient → Import → colour appears → Save → reopen and confirm colour survived; dynamic ingredient → Import a grayscale PNG → paint over it → Save.

---

## Self-Review
- **Spec coverage:** §3.1 import + picker + size guard → T1. §3.3/§3.4 custom paint-disabled + full-colour render → T2. §3.2 custom save path + gate → T3. §3.5 view → T4. §5 error handling (cancel/unreadable/mismatch/disposal) → T1–T3 code. §6 tests → T1–T3 + visual/manual (T5). §7 risks: two save paths (T3's round-trip colour test is the guard), history cleared on import (T1 test), ownership (VM owns `_importedCustom` + clones; originals never disposed), strict size (T1 test).
- **Placeholder scan:** T1 gives full code; T2/T3 give the exact edits; the T1 test sketch is explicitly "implement fully, mirroring these existing files" (a procedure, not a TBD) — the agent must write real tests.
- **Type consistency:** `IFilePickerService.OpenFileAsync(title, params ext)` matches the interface; `ValueMap.FromImage/GetValue/GetAlpha/Set`, `EditHistory`, `IngredientDraftExporter.Export`, `_bridge.ToBitmap` match existing usage; the editor ctor's new `IFilePickerService` must precede the optional `looseSavePath`, so every construction site passes it positionally before any loose path.
