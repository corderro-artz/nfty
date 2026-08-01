# nfty GUI — Create a loose ingredient from scratch (B3a) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Landing's New Ingredient wizard creates a standalone `.igt` (own canvas, one blank variant) and opens it in the editor.

**Architecture:** The wizard gains a `CanvasSize` string + `TryGetCanvas` parser (its `Build(Dimensions)` already exists). `DesktopFilePicker.SaveFileAsync` is wired over Avalonia's `StorageProvider`. Landing's New Ingredient becomes async: wizard (default Loose) → parse canvas → Save picker → `Build` → `IngredientArchive.Write` → B1's `OpenLooseIngredient`. No `Nfty.Core` change.

**Tech Stack:** .NET 10, Avalonia 11.2.3 (`StorageProvider.SaveFilePickerAsync`), CommunityToolkit.Mvvm, `Nfty.Core.Formats.IngredientArchive`, xUnit + Avalonia.Headless.XUnit.

## Global Constraints
- **No `Nfty.Core` change** — reuse `IngredientArchive.Write`, the wizard's `Build`, B1's `OpenLooseIngredient`.
- **Save picker is head-specific** (`DesktopFilePicker`, not headless-testable) — verify in the manual smoke; the VM/Landing logic IS unit-tested via a stub picker.
- **Disposal:** Landing writes the built ingredient then disposes it; `OpenLooseIngredient` reads a fresh independent copy — no shared-image tangle (mirror A2b).
- **Context7 for the Avalonia Save-picker API:** the `FilePickerSaveOptions`/`SaveFilePickerAsync` shape must be confirmed against Avalonia 11.2 via Context7 (resolve-library-id `avalonia` → query-docs), not guessed.
- Determinism/idiom: no RNG; token brushes only in Views (no raw hex); `[AvaloniaFact]` for Avalonia-constructing tests. Build 0 warnings. Conventional commits. Agents: caveman-ultra terse chat; code/commits/reports normal prose.

## File Structure
- `src/Nfty.App/ViewModels/NewIngredientViewModel.cs` — `CanvasSize` + `TryGetCanvas` (T1).
- `src/Nfty.App/Views/NewIngredientView.axaml` — bind the canvas TextBox (T1).
- `src/Nfty.Desktop/DesktopFilePicker.cs` — real `SaveFileAsync` (T2).
- `src/Nfty.App/ViewModels/LandingViewModel.cs` — async New Ingredient create-loose flow (T3).
- Tests: `tests/Nfty.App.Tests/NewIngredientViewModelTests.cs` (append, T1); `tests/Nfty.App.Tests/LandingNewIngredientTests.cs` (T3).

---

### Task 1: Wizard canvas input

**Files:** Modify `src/Nfty.App/ViewModels/NewIngredientViewModel.cs`, `src/Nfty.App/Views/NewIngredientView.axaml`; Test `tests/Nfty.App.Tests/NewIngredientViewModelTests.cs` (append).

**Interfaces:**
- Produces: `string NewIngredientViewModel.CanvasSize`; `bool TryGetCanvas(out Dimensions canvas)`.

- [ ] **Step 1: Failing tests** — append to `NewIngredientViewModelTests.cs`:
```csharp
    [Fact]
    public void TryGetCanvas_parses_WxH_and_rejects_bad_input()
    {
        var vm = Make(out _, out _);
        vm.CanvasSize = "512x512";
        Assert.True(vm.TryGetCanvas(out var c));
        Assert.Equal(512, c.Width); Assert.Equal(512, c.Height);

        vm.CanvasSize = " 8 x 8 ";
        Assert.True(vm.TryGetCanvas(out var c2));
        Assert.Equal(8, c2.Width); Assert.Equal(8, c2.Height);

        foreach (var bad in new[] { "", "abc", "0x8", "8", "8x", "-4x4", "8xY" })
        {
            vm.CanvasSize = bad;
            Assert.False(vm.TryGetCanvas(out _), $"expected '{bad}' to be rejected");
        }
    }
```

- [ ] **Step 2: Run — fail** (`CanvasSize`/`TryGetCanvas` missing). `dotnet test tests/Nfty.App.Tests --filter "FullyQualifiedName~NewIngredientViewModelTests" --nologo`.

- [ ] **Step 3: Implement** in `NewIngredientViewModel.cs`:
  - Add `[ObservableProperty] private string _canvasSize = "512x512";` (beside the other `[ObservableProperty]`s).
  - Add:
    ```csharp
    /// <summary>Parse the CanvasSize field ("{W}x{H}") into positive dimensions.</summary>
    public bool TryGetCanvas(out Dimensions canvas)
    {
        canvas = default;
        var parts = CanvasSize.Split('x', 'X');
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0].Trim(), out var w) || !int.TryParse(parts[1].Trim(), out var h)) return false;
        if (w <= 0 || h <= 0) return false;
        canvas = new Dimensions(w, h);
        return true;
    }
    ```
    (`Dimensions` is `Nfty.Core.Model` — already imported.)

- [ ] **Step 4: Bind the view** — `NewIngredientView.axaml`, the canvas section:
  ```xml
  <StackPanel Spacing="6" IsVisible="{Binding ShowCanvas}">
    <TextBlock Text="Canvas" Classes="muted" />
    <TextBox Text="{Binding CanvasSize}" Watermark="Canvas size (e.g. 512x512)" />
  </StackPanel>
  ```
  Token styles; no raw hex.

- [ ] **Step 5: Run — pass;** `dotnet test tests/Nfty.App.Tests --nologo` green; `dotnet build src/Nfty.Desktop --nologo` 0 warnings; `grep -rniE "#[0-9a-fA-F]{6}" src/Nfty.App/Views/NewIngredientView.axaml` → nothing.

- [ ] **Step 6: Commit** `feat(gui): New Ingredient wizard takes a canvas size`

---

### Task 2: Desktop Save file picker

**Files:** Modify `src/Nfty.Desktop/DesktopFilePicker.cs`. (No unit test — head-specific; manual smoke.)

**Interfaces:**
- Produces: a real `DesktopFilePicker.SaveFileAsync(string title, string defaultExtension)` returning the chosen path or null.

- [ ] **Step 1: Confirm the API via Context7.** Query Avalonia 11.2 docs for `StorageProvider.SaveFilePickerAsync` + `FilePickerSaveOptions` — verify the property names (`Title`, `DefaultExtension`, `FileTypeChoices`, optional `SuggestedFileName`) and that it returns `IStorageFile?`. Do NOT guess.

- [ ] **Step 2: Implement** — replace the stub `SaveFileAsync`:
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
  (Adjust to the exact shape Context7 confirms in Step 1. `FilePickerSaveOptions`/`FilePickerFileType` are in `Avalonia.Platform.Storage` — already imported.)

- [ ] **Step 3:** `dotnet build src/Nfty.Desktop --nologo` → 0 warnings. (No test — the whole solution build proves it compiles; behavior is manual-smoke.)

- [ ] **Step 4: Commit** `feat(desktop): implement the Save file picker over StorageProvider`

---

### Task 3: Landing New Ingredient create-loose flow

**Files:** Modify `src/Nfty.App/ViewModels/LandingViewModel.cs`; Test `tests/Nfty.App.Tests/LandingNewIngredientTests.cs` (create).

**Interfaces:**
- Consumes: `NewIngredientViewModel.TryGetCanvas`/`Build`/`Destination` (T1), `_picker.SaveFileAsync`, `IngredientArchive.Write`, B1's `OpenLooseIngredient`.

- [ ] **Step 1: Failing test** — `tests/Nfty.App.Tests/LandingNewIngredientTests.cs`. A dialog stub fills the wizard (Loose, name, canvas) + Creates; a picker stub returns a temp `.igt` path:
```csharp
using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Xunit;

namespace Nfty.App.Tests;

public class LandingNewIngredientTests
{
    private sealed class SavePicker : IFilePickerService
    {
        private readonly string? _save;
        public SavePicker(string? save) => _save = save;
        public Task<string?> OpenFileAsync(string title, params string[] extensions) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string title, string defaultExtension) => Task.FromResult(_save);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    // Fills the New-Ingredient wizard as Loose with a name/canvas and "clicks Create" (returns it);
    // records any error dialog.
    private sealed class WizardDialogs : IDialogService
    {
        private readonly string _name; private readonly string _canvas; private readonly RecipeDestination _dest;
        public string? ErrorTitle { get; private set; }
        public WizardDialogs(string name, string canvas, RecipeDestination dest = RecipeDestination.LooseKitchen)
        { _name = name; _canvas = canvas; _dest = dest; }
        public ViewModelBase? Active => null;
        public event Action? Changed { add { } remove { } }
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog)
        {
            if (dialog is NewIngredientViewModel w)
            { w.Name = _name; w.Kind = LayerKind.Dynamic; w.CanvasSize = _canvas; w.Destination = _dest;
              return Task.FromResult((TResult?)(object?)w); }
            if (dialog is ErrorDialogViewModel e) { ErrorTitle = e.Title; return Task.FromResult(default(TResult)); }
            return Task.FromResult(default(TResult));
        }
        public void Close(object? result) { }
    }

    private static (LandingViewModel vm, FakeNav nav) Landing(IDialogService dialogs, IFilePickerService picker)
    {
        var nav = new FakeNav(); var notify = new FakeNotYetWired(); var session = new CookBookSession();
        var vm = new LandingViewModel(nav, dialogs, notify, picker, new RecentsService(), session,
            book => new ExplorerViewModel(book, nav, dialogs, notify, new ImageBridge(),
                ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
                ExplorerViewModelTests.CookFactory(dialogs), session),
            set => new SetBrowserViewModel(set),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs));
        return (vm, nav);
    }

    [AvaloniaFact]
    public async Task New_ingredient_writes_an_igt_and_opens_the_editor()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "hat.igt");
        var (vm, nav) = Landing(new WizardDialogs("Hat", "8x8"), new SavePicker(path));
        try
        {
            await vm.NewIngredientCommand.ExecuteAsync(null);
            Assert.True(File.Exists(path));
            using var reread = IngredientArchive.Read(path);
            Assert.Equal("hat", reread.Manifest.Id);
            Assert.Single(reread.Manifest.Variants);
            Assert.Equal(8, reread.VariantImages["variant-1"].Width);
            Assert.IsType<IngredientEditorViewModel>(nav.Current);   // opened in the editor
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Into_cookbook_from_landing_errors_and_writes_nothing()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "hat.igt");
        var dialogs = new WizardDialogs("Hat", "8x8", RecipeDestination.IntoCookBook);
        var (vm, nav) = Landing(dialogs, new SavePicker(path));
        try
        {
            await vm.NewIngredientCommand.ExecuteAsync(null);
            Assert.NotNull(dialogs.ErrorTitle);
            Assert.False(File.Exists(path));
            Assert.Null(nav.Current);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Cancelling_the_save_picker_writes_nothing()
    {
        var (vm, nav) = Landing(new WizardDialogs("Hat", "8x8"), new SavePicker(null));   // picker cancelled
        await vm.NewIngredientCommand.ExecuteAsync(null);
        Assert.Null(nav.Current);
    }
}
```
  (Confirm `IngredientArchive.Read`/`Write` + the `variant-1` id from the wizard's `Build`.)

- [ ] **Step 2: Run — fail** (`NewIngredient` is the sync stub / not async).

- [ ] **Step 3: Implement** in `LandingViewModel.cs` — replace `[RelayCommand] private void NewIngredient() => _dialogs.ShowAsync<object>(new NewIngredientViewModel(_dialogs, _notify));`:
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
  (`NewIngredient` is now `AsyncRelayCommand` → the generated command is still `NewIngredientCommand`; `LandingView.axaml`'s binding is unchanged. `IngredientArchive`/`OpenLooseIngredient`/`ShowError` already reachable.)

- [ ] **Step 4: Run — pass;** whole App suite green; `dotnet build src/Nfty.Desktop --nologo` 0 warnings.

- [ ] **Step 5: Commit** `feat(gui): Landing creates a loose ingredient and opens it in the editor`

---

### Task 4: Verification + manual smoke

**Files:** none.

- [ ] **Step 1:** `dotnet build nfty.sln --nologo` → 0 warnings. `dotnet test nfty.sln --nologo` → all pass (report Cli/App/Core totals).
- [ ] **Step 2:** `git diff --name-only <base>..HEAD -- src/Nfty.Core/` → empty (no Core change). `grep -rniE "#[0-9a-fA-F]{6}" src/Nfty.App/Views/NewIngredientView.axaml` → nothing.
- [ ] **Step 3: Manual smoke (user):** run the desktop app; Landing → New Ingredient → the wizard opens with Loose pre-selected + a canvas field → set name/kind/colour/canvas → Create → a native Save dialog appears → choose a path → the `.igt` writes and opens in the editor → paint → Save → Import that `.igt` to confirm it round-tripped; a bad canvas string errors before the Save dialog; cancelling the Save dialog aborts cleanly; picking Into-CookBook errors with guidance.
- [ ] **Step 4:** Commit any smoke fixups: `test(gui): verify loose-ingredient create end-to-end`.

---

## Self-Review
- **Spec coverage:** §2.1 wizard `CanvasSize`/`TryGetCanvas` + view binding → T1. §2.2 `DesktopFilePicker.SaveFileAsync` → T2. §2.3 Landing async create-loose flow → T3. §2.4 view → T1. §4 error handling (cancel/into-cookbook/invalid-canvas/write-fail + disposal) → T3. §5 tests → T1 (parse) + T3 (write+open, into-cookbook error, cancel) + manual (picker). §6 risks: SaveFilePickerAsync API (Context7, T2 Step 1), disposal (T3 built.Dispose + fresh read), strict canvas parse (T1), destination default (T3).
- **Placeholder scan:** full code in every step; the "confirm SaveFilePickerAsync shape via Context7" and "confirm IngredientArchive/variant-1" notes point at real verification, not TBDs.
- **Type consistency:** `CanvasSize`/`TryGetCanvas(out Dimensions)`/`Build(Dimensions)`/`Destination` (T1) consumed by Landing (T3); `SaveFileAsync(string,string)→Task<string?>` matches `IFilePickerService`; `IngredientArchive.Write(string, IngredientManifest, IReadOnlyDictionary<string,Image<Rgba32>>)` matches source; `OpenLooseIngredient(string)` is B1's method; `NewIngredientCommand` name unchanged (AsyncRelayCommand). `Dimensions(int,int)` from `Nfty.Core.Model`.
