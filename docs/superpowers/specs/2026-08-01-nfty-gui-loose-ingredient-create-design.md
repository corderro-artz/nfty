# nfty GUI — Create a loose ingredient from scratch (B3a) design spec

**Date:** 2026-08-01
**Status:** Approved (design), pending implementation planning
**Scope:** First create-from-scratch "Kitchen" slice (B3a). Landing's **New Ingredient** wizard creates a
standalone `.igt` on disk (its own canvas, one blank starter variant) and opens it in the editor. Wires
the real desktop **Save** file picker and the wizard's canvas input. Reuses B1's loose-open flow. No
Kitchen screen. Loose **recipe** create + the Explorer "Add → Loose" (A2c-F2) are B3b.

## 0. Program bar
Rock-solid, efficient; best practices; pull docs (Context7) rather than assume any library API — in
particular Avalonia's `StorageProvider.SaveFilePickerAsync`. Escalate anything off. Reuse B1
(`OpenLooseIngredient`, `LooseWorkspace`, the editor's loose-save) and the wizard's existing `Build`.
No `Nfty.Core` change.

## 1. Goals & non-goals
**Goals**
- Landing **New Ingredient** opens the New Ingredient wizard defaulted to the **Loose (Kitchen)**
  destination; on **Create** it saves a new `.igt` (chosen via the Save file picker) containing the
  configured ingredient (id/name/kind/colorization) with **one blank starter variant** at the wizard's
  canvas size, then opens it in the editor (via B1's `OpenLooseIngredient`, so painting + Save write back
  to that `.igt`).
- The wizard's **Canvas** input is wired: a `CanvasSize` string (default `512x512`) parsed to
  `Dimensions`; an invalid value is rejected with an error dialog.
- The desktop **Save** file picker (`DesktopFilePicker.SaveFileAsync`) is implemented over Avalonia's
  `StorageProvider`.

**Non-goals (this slice)**
- Loose **recipe** create; the Explorer "Add ingredient → Loose (Kitchen)" path (A2c-F2) — both B3b.
- A Kitchen screen / managing loose files. The **Into CookBook** destination from Landing (there is no
  open cookbook there — it errors with guidance). Any `Nfty.Core` change.

## 2. Components

### 2.1 New Ingredient wizard canvas (`NewIngredientViewModel` + `NewIngredientView.axaml`)
- Add `[ObservableProperty] private string _canvasSize = "512x512";`.
- Add `public bool TryGetCanvas(out Dimensions canvas)`: parse `CanvasSize` as `"{W}x{H}"`
  (case-insensitive `x`, trimmed), both > 0 → `canvas = new Dimensions(w, h); return true;` else
  `canvas = default; return false;`.
- View: bind the existing canvas `TextBox` (currently an unbound `Watermark="Canvas size (e.g. 1000x1000)"`
  placeholder, shown when `ShowCanvas`) to `{Binding CanvasSize}`. Token styles; no raw hex. `Build` is
  unchanged — the caller passes the parsed `Dimensions`.

### 2.2 Desktop Save file picker (`DesktopFilePicker.SaveFileAsync`)
Replace the `Task.FromResult<string?>(null)` stub with a real implementation over the window's
`StorageProvider` (mirror `OpenFileAsync`):
```csharp
public async Task<string?> SaveFileAsync(string title, string defaultExtension)
{
    var top = TopLevel;
    if (top is null) return null;
    var ext = defaultExtension.StartsWith('.') ? defaultExtension : "." + defaultExtension;
    var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
    {
        Title = title,
        DefaultExtension = ext.TrimStart('.'),
        FileTypeChoices = new[] { new FilePickerFileType("nfty") { Patterns = new[] { "*" + ext } } },
    });
    return file?.TryGetLocalPath();
}
```
> Confirm the exact `FilePickerSaveOptions` / `SaveFilePickerAsync` shape against Avalonia 11.2 via Context7
> before finalizing (property names like `DefaultExtension`, `FileTypeChoices`, `SuggestedFileName`). This
> is head-specific and not headless-testable; verify in the manual smoke.

### 2.3 Landing New Ingredient create flow (`LandingViewModel`)
Replace the stub `[RelayCommand] private void NewIngredient() => _dialogs.ShowAsync<object>(new
NewIngredientViewModel(_dialogs, _notify));` with an async flow:
```csharp
[RelayCommand]
private async Task NewIngredient()
{
    var wizard = new NewIngredientViewModel(_dialogs, _notify) { Destination = RecipeDestination.LooseKitchen };
    var result = await _dialogs.ShowAsync<NewIngredientViewModel>(wizard);
    if (result is null) return;   // cancelled

    if (result.Destination == RecipeDestination.IntoCookBook)
    {
        ShowError("No cookbook open", "Open or create a cookbook, then add ingredients from the Explorer.");
        return;
    }
    if (!result.TryGetCanvas(out var canvas))
    {
        ShowError("Invalid canvas", "Enter a canvas size like 512x512.");
        return;
    }
    var path = await _picker.SaveFileAsync("Save new ingredient", ".igt");
    if (path is null) return;   // cancelled the picker

    var built = result.Build(canvas);   // manifest + one blank variant (we own its images)
    try { IngredientArchive.Write(path, built.Manifest, built.VariantImages); }
    catch (Exception ex) { ShowError("Could not save", ex.Message); built.Dispose(); return; }
    built.Dispose();

    OpenLooseIngredient(path);   // B1: reads it back + opens the editor with a loose-save path
}
```
- `Build`/`TryGetCanvas` come from §2.1; `OpenLooseIngredient` is B1's existing private method (reads the
  `.igt`, wraps it, opens the loose editor). `IngredientArchive` is in `Nfty.Core.Formats` (imported).
- The `NewIngredient` command becomes `AsyncRelayCommand` (was a plain `[RelayCommand] void`); its binding
  in `LandingView.axaml` (`NewIngredientCommand`) is unchanged.

### 2.4 View
Only the wizard's canvas `TextBox` gains a binding (§2.1). No other view change; the editor + Landing are
reused.

## 3. Data flow
```
Landing New Ingredient → wizard (default Loose) → Create
  → (IntoCookBook → error "open a cookbook first")
  → TryGetCanvas(CanvasSize) → Dimensions           (invalid → error)
  → path = SaveFileAsync(".igt")                     (cancel → stop)
  → built = wizard.Build(canvas)                     (manifest + one blank variant)
  → IngredientArchive.Write(path, built.Manifest, built.VariantImages) ; built.Dispose()
  → OpenLooseIngredient(path)                        (B1: read + wrap + open editor, loose-save)
```

## 4. Error handling
- Cancelled wizard or cancelled Save picker → no-op.
- `IntoCookBook` from Landing → guidance error, nothing written.
- Invalid canvas string → error, nothing written (before the picker, so no wasted prompt).
- `IngredientArchive.Write` failure → error dialog, `built` disposed, nothing opened.
- **Disposal:** `built` (the in-memory ingredient the wizard produced) is disposed after write (or on
  failure); `OpenLooseIngredient` reads a fresh independent copy from disk, so there is no shared-image
  tangle between the written copy and the opened editor.

## 5. Testing
- **`TryGetCanvas`** (unit): `"512x512"` → (512,512) true; `"1000x1000"` → true; whitespace tolerated
  (`" 8 x 8 "`); rejects `""`, `"abc"`, `"0x8"`, `"8"`, negative.
- **Landing New Ingredient** (`[AvaloniaFact]`, stub picker returning a temp `.igt` path + a dialog stub
  that fills the wizard as Loose with a name/canvas and Creates): writes a real `.igt` at the path (re-read
  shows the ingredient with one variant at the canvas size) and navigates the nav to an
  `IngredientEditorViewModel`.
- **IntoCookBook from Landing** → error dialog, nothing written, no navigation.
- **Cancelled Save picker** (stub returns null) → nothing written, no navigation.
- `DesktopFilePicker.SaveFileAsync` is head-specific and **not** headless-testable — covered by the manual
  smoke, not a unit test.
- **No regression:** B1/B2 + Landing suites stay green; full suite green; build 0 warnings; no raw hex
  outside `Tokens.axaml`.
- **Manual smoke:** Landing → New Ingredient → pick Loose (default), set name/kind/colour/canvas → Create →
  a Save dialog appears → choose a path → the `.igt` is written and opens in the editor → paint → Save →
  reopen (Import) to confirm; a bad canvas string errors; cancelling the Save dialog aborts cleanly.

## 6. Risks & escalation
- **`SaveFilePickerAsync` API drift:** confirm the Avalonia 11.2 `FilePickerSaveOptions` property names via
  Context7; a wrong property compiles-fails or silently misbehaves. Head-only, so it surfaces in the manual
  smoke — verify there.
- **Disposal:** the wizard's `Build` hands Landing a live ingredient; Landing writes then disposes it, and
  `OpenLooseIngredient` reads an independent copy — no double-free, no leak (mirror A2b's disposal care).
- **Canvas parsing:** keep it strict (`WxH`, both > 0) so a typo can't create a degenerate 0-sized
  ingredient; reject with a clear message. A very large canvas is the user's choice (one blank raster).
- **Destination default:** opening the wizard from Landing pre-selects Loose so the canvas input shows and
  Create produces a file; leaving IntoCookBook reachable (with a guidance error) avoids a confusing dead
  toggle. If this reads oddly in the smoke, consider hiding the destination toggle when launched from
  Landing (a later polish).
