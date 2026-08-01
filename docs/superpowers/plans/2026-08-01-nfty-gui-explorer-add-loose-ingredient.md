# nfty GUI — Explorer "Add ingredient → Loose" (B3b / A2c-F2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** In the Explorer's Add-ingredient wizard, the "Loose (Kitchen)" destination writes a standalone `.igt` and opens it in a loose editor, instead of upserting into the open cookbook.

**Architecture:** Inject `IFilePickerService` + the loose-editor factory into the Explorer. `AddIngredientTo` branches on `result.Destination`: IntoCookBook keeps the A2b flow; LooseKitchen runs B3a's create-loose steps (parse canvas → SaveFileAsync → Build → IngredientArchive.Write → read + wrap + open a loose editor). No `Nfty.Core` change.

**Tech Stack:** .NET 10, Avalonia 11.2.3, CommunityToolkit.Mvvm, `Nfty.Core.Formats.IngredientArchive`, `Nfty.App.Services.LooseWorkspace`, xUnit + Avalonia.Headless.XUnit.

## Global Constraints
- **No `Nfty.Core` change** — reuse the B3a/B1 pieces (`TryGetCanvas`, `Build`, `IngredientArchive.Write`/`Read`, `LooseWorkspace.WrapIngredient`, the loose-editor factory).
- **Session isolation:** the Loose branch must NOT touch the cookbook — no `UpsertIngredient`/`PersistAsync`/`session.Open`; it only writes the `.igt` and navigates to a loose editor. A test asserts the cookbook's ingredient count is unchanged.
- **Disposal (carry the B3a F1 fix):** `built = result.Build(canvas)` is INSIDE a try (OOM on a huge canvas → error dialog, not a swallowed fault); `built` is disposed after write and on the write-failure path; the editor opens a fresh independent copy.
- **Trailing ctor params:** append `IFilePickerService` + the loose factory last, so existing sites' diffs are localized. Build-and-fix every construction site.
- Determinism/idiom: no RNG; token brushes only (no view change); `[AvaloniaFact]` for Avalonia tests. Build 0 warnings. Conventional commits. Agents: caveman-ultra terse chat; code/commits/reports normal prose. Context7 for any uncertain library API (this slice is domain C#, likely unneeded).

## File Structure
- `src/Nfty.App/ViewModels/ExplorerViewModel.cs` — inject deps + `AddIngredientTo` branch + `CreateLooseIngredient` (T1).
- `src/Nfty.App/ServiceRegistration.cs` — Explorer factory passes the two new deps (T1).
- Tests: `tests/Nfty.App.Tests/ExplorerAddLooseTests.cs` (create, T1); all Explorer construction sites updated (T1).

---

### Task 1: Explorer Add → Loose branch

**Files:** Modify `src/Nfty.App/ViewModels/ExplorerViewModel.cs`, `src/Nfty.App/ServiceRegistration.cs`; update Explorer construction sites across `tests/Nfty.App.Tests/`; Test `tests/Nfty.App.Tests/ExplorerAddLooseTests.cs` (create).

**Interfaces:**
- Consumes: `IFilePickerService.SaveFileAsync`, `NewIngredientViewModel.TryGetCanvas`/`Build`/`Destination`, `IngredientArchive.Write`/`Read`, `LooseWorkspace.WrapIngredient`, `Func<LoadedIngredient, LoadedCookBook, string, IngredientEditorViewModel>`.
- Produces: the Loose branch in `AddIngredientTo` + `CreateLooseIngredient`.

- [ ] **Step 1: Failing tests** — `tests/Nfty.App.Tests/ExplorerAddLooseTests.cs`:
```csharp
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Xunit;

namespace Nfty.App.Tests;

public class ExplorerAddLooseTests
{
    private sealed class SavePicker : IFilePickerService
    {
        private readonly string? _save;
        public SavePicker(string? save) => _save = save;
        public Task<string?> OpenFileAsync(string title, params string[] extensions) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string title, string defaultExtension) => Task.FromResult(_save);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    // Fills the New-Ingredient wizard the Explorer shows as LOOSE with a name/canvas + "clicks Create".
    private sealed class LooseWizardDialogs : IDialogService
    {
        private readonly string _name; private readonly string _canvas;
        public string? ErrorTitle { get; private set; }
        public LooseWizardDialogs(string name, string canvas) { _name = name; _canvas = canvas; }
        public ViewModelBase? Active => null;
        public event Action? Changed { add { } remove { } }
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog)
        {
            if (dialog is NewIngredientViewModel w)
            { w.Name = _name; w.Kind = LayerKind.Dynamic; w.CanvasSize = _canvas;
              w.Destination = RecipeDestination.LooseKitchen; return Task.FromResult((TResult?)(object?)w); }
            if (dialog is ErrorDialogViewModel e) { ErrorTitle = e.Title; return Task.FromResult(default(TResult)); }
            return Task.FromResult(default(TResult));
        }
        public void Close(object? result) { }
    }

    private static (ExplorerViewModel vm, CookBookSession session, string cbkDir, FakeNav nav) Explorer(
        IDialogService dialogs, IFilePickerService picker)
    {
        (var cbkPath, var session, _, _) = IngredientEditorSaveTests.OnDisk();
        var nav = new FakeNav();
        var vm = new ExplorerViewModel(session.Current!, nav, dialogs, new FakeNotYetWired(), new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
            ExplorerViewModelTests.CookFactory(dialogs), session,
            picker, ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs));
        return (vm, session, Path.GetDirectoryName(cbkPath)!, nav);
    }

    [AvaloniaFact]
    public async Task Add_loose_writes_an_igt_opens_editor_and_leaves_the_cookbook_untouched()
    {
        var igtDir = Directory.CreateTempSubdirectory().FullName;
        var igtPath = Path.Combine(igtDir, "hat.igt");
        var (vm, session, cbkDir, nav) = Explorer(new LooseWizardDialogs("Hat", "8x8"), new SavePicker(igtPath));
        try
        {
            var recipe = (LoadedRecipe)vm.Root.Children[0].Domain!;
            var before = recipe.Ingredients.Count;
            vm.ToggleLockCommand.Execute(null);
            vm.SelectNodeCommand.Execute(vm.Root.Children[0]);   // recipe "cat"
            await vm.AddCommand.ExecuteAsync(null);

            Assert.True(File.Exists(igtPath));                   // loose .igt written
            using var reread = IngredientArchive.Read(igtPath);
            Assert.Equal("hat", reread.Manifest.Id);
            Assert.Equal(8, reread.VariantImages["variant-1"].Width);
            Assert.IsType<IngredientEditorViewModel>(nav.Current);   // opened a (loose) editor
            Assert.Equal(before, ((LoadedRecipe)vm.Root.Children[0].Domain!).Ingredients.Count);   // cookbook NOT mutated
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(cbkDir, recursive: true); Directory.Delete(igtDir, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Add_loose_cancelled_picker_writes_nothing_and_leaves_the_cookbook_untouched()
    {
        var (vm, session, cbkDir, nav) = Explorer(new LooseWizardDialogs("Hat", "8x8"), new SavePicker(null));
        try
        {
            var before = ((LoadedRecipe)vm.Root.Children[0].Domain!).Ingredients.Count;
            vm.ToggleLockCommand.Execute(null);
            vm.SelectNodeCommand.Execute(vm.Root.Children[0]);
            await vm.AddCommand.ExecuteAsync(null);
            Assert.Null(nav.Current);
            Assert.Equal(before, ((LoadedRecipe)vm.Root.Children[0].Domain!).Ingredients.Count);
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(cbkDir, recursive: true); }
    }
}
```
  (Confirm the A2a `OnDisk` fixture's recipe is `cat`; `Root.Children[0].Domain` is a `LoadedRecipe`.)

- [ ] **Step 2: Run — fail** (Explorer ctor lacks the two new params; no Loose branch). `dotnet build tests/Nfty.App.Tests --nologo` will list the ctor-arity errors first — expected.

- [ ] **Step 3: Inject the deps.** In `ExplorerViewModel.cs`:
  - Fields: `private readonly IFilePickerService _picker; private readonly Func<LoadedIngredient, LoadedCookBook, string, IngredientEditorViewModel> _looseEditorFactory;`.
  - Ctor: append `, IFilePickerService picker, Func<LoadedIngredient, LoadedCookBook, string, IngredientEditorViewModel> looseEditorFactory`; assign both.
  - `ServiceRegistration.cs` Explorer factory: pass `sp.GetRequiredService<IFilePickerService>()` and
    `sp.GetRequiredService<Func<LoadedIngredient, LoadedCookBook, string, IngredientEditorViewModel>>()`
    (the loose-editor factory singleton registered in B1) as the last two args.

- [ ] **Step 4: Branch `AddIngredientTo` + add `CreateLooseIngredient`.** In `AddIngredientTo`, right after the non-blank-id guard and BEFORE `var newIng = result.Build(_book.Manifest.Canvas);`, insert:
```csharp
        if (result.Destination == RecipeDestination.LooseKitchen)
        {
            await CreateLooseIngredient(result);
            return;
        }
```
  Add the method (near `AddRecipe`):
```csharp
    private async Task CreateLooseIngredient(NewIngredientViewModel result)
    {
        if (!result.TryGetCanvas(out var canvas))
        {
            await ShowError("Invalid canvas", "Enter a canvas size like 512x512.");
            return;
        }
        var path = await _picker.SaveFileAsync("Save new ingredient", ".igt");
        if (path is null) return;   // cancelled

        LoadedIngredient built;
        try { built = result.Build(canvas); }
        catch (Exception ex) { await ShowError("Could not save", ex.Message); return; }
        try { IngredientArchive.Write(path, built.Manifest, built.VariantImages); }
        catch (Exception ex) { await ShowError("Could not save", ex.Message); built.Dispose(); return; }
        built.Dispose();

        LoadedIngredient ing;
        try { ing = IngredientArchive.Read(path); }
        catch (Exception ex) { await ShowError("Could not open", ex.Message); return; }
        var book = LooseWorkspace.WrapIngredient(ing);   // the loose editor owns + disposes this
        _nav.To(_looseEditorFactory(ing, book, path));
    }
```
  (`ShowError` is the Explorer's existing `private Task ShowError(...)`; `IngredientArchive`/`LooseWorkspace` are already reachable. Add `using Nfty.App.Services;` if `IFilePickerService` isn't resolved — it likely already is.)

- [ ] **Step 5: Fix the Explorer construction sites.** `dotnet build tests/Nfty.App.Tests --nologo` → for each `new ExplorerViewModel(...)` `CS7036`, append `, <picker>, ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs)` (use each site's local names; where a site lacks a session/dialogs/picker local, pass `new CookBookSession()` / `new FakeDialogs()` / a `new FilePickerService()` — the real no-op stub — or a throwaway). The DI factory (Step 3) covers the app itself. Repeat until 0 errors.

- [ ] **Step 6: Run — pass;** whole App suite green (the A2b `ExplorerAddIngredientTests` IntoCookBook path stays green — regression guard); `dotnet build src/Nfty.Desktop --nologo` 0 warnings.

- [ ] **Step 7: Commit** `feat(gui): Explorer Add ingredient -> Loose writes a standalone .igt`

---

### Task 2: Verification + manual smoke

**Files:** none.

- [ ] **Step 1:** `dotnet build nfty.sln --nologo` → 0 warnings. `dotnet test nfty.sln --nologo` → all pass (report Cli/App/Core totals).
- [ ] **Step 2:** `git diff --name-only <base>..HEAD -- src/Nfty.Core/` → empty (no Core change). No view edits → no hex scan.
- [ ] **Step 3: Manual smoke (user):** open a `.cbk`, edit-lock on, select a recipe → Add → in the wizard pick **Loose (Kitchen)**, set name/canvas → Create → a Save dialog appears → choose a path → the `.igt` writes and opens in the editor; Save writes to the file (not the cookbook); the recipe's ingredient list is unchanged. Picking **Into CookBook** still adds to the recipe (A2b). Cancelling the Save dialog aborts cleanly.
- [ ] **Step 4:** Commit any smoke fixups: `test(gui): verify Explorer add-loose end-to-end`.

---

## Self-Review
- **Spec coverage:** §2.1 Explorer deps + DI + sites → T1 Steps 3/5. §2.2 Loose branch + `CreateLooseIngredient` → T1 Step 4. §2.3 (no view) → n/a. §4 error handling (invalid canvas/cancel/Build-OOM/write-fail/read-fail) → T1 Step 4 code. §5 tests → T1 (Add-Loose writes + cookbook-untouched + opens editor; cancel; IntoCookBook regression via existing suite) + manual. §6 risks: ctor ripple (Step 5 build-and-fix), duplication-with-Landing (accepted), session isolation (test asserts unchanged ingredient count), disposal (Build inside try + dispose).
- **Placeholder scan:** full code in every step; the "confirm OnDisk recipe id / Domain type" and "build-and-fix the sites" notes are concrete procedures, not TBDs.
- **Type consistency:** Explorer ctor gains `IFilePickerService` + `Func<LoadedIngredient, LoadedCookBook, string, IngredientEditorViewModel>` used identically in ServiceRegistration + all test sites; `result.TryGetCanvas(out Dimensions)`/`Build(Dimensions)`/`Destination` (B3a) + `IngredientArchive.Write/Read` + `LooseWorkspace.WrapIngredient` + `ShowError` (Explorer, async) all match existing signatures; `RecipeDestination.LooseKitchen` from `Nfty.App.ViewModels`.
