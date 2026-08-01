# nfty GUI — Explorer add recipe (A2c) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Add a new (empty) recipe to the open cookbook from the Explorer, persisted to the source `.cbk` and selected.

**Architecture:** New `CookBookEdits.UpsertRecipe` splices a recipe + its weight. The New Recipe wizard gains `DerivedId` + a blank-id-gated `Create` that closes with the VM. The Explorer's async `Add` dispatches on selection: CookBook root → build an empty recipe → validate → `UpsertRecipe` → `CookBookPersistence.PersistAsync` (A2a) → `ApplyBook` selecting it.

**Tech Stack:** .NET 10, Avalonia 11.2.3, CommunityToolkit.Mvvm, `Nfty.Core.Editing`/`Formats`/`Model`, xUnit + Avalonia.Headless.XUnit.

## Global Constraints
- **Blank-id guard (F1):** gate the wizard `Create` on `!string.IsNullOrWhiteSpace(DerivedId)` AND reject an empty derived id in the Explorer (authoritative — the wizard can be bypassed).
- **No image ownership** on this path (an empty recipe has no images) — no dispose bookkeeping.
- **`RecipeWeights`:** `UpsertRecipe` must add `RecipeWeights[id] = weight` so the recipe participates in the weighted roll.
- **Don't regress A2b:** keep the recipe→add-ingredient branch of `Add` intact; add the cookbook branch alongside.
- Determinism/idiom: `StringComparer.Ordinal` where ids sort; no RNG; token brushes only in Views; `[AvaloniaFact]` for Avalonia-constructing tests. Build 0 warnings. Conventional commits. Agents: caveman-ultra terse chat; code/commits/reports normal prose.

## File Structure
- `src/Nfty.Core/Editing/CookBookEdits.cs` — add `UpsertRecipe` (T1).
- `src/Nfty.App/ViewModels/NewRecipeViewModel.cs` — `DerivedId`, gated `Create`→`Close(this)` (T2).
- `src/Nfty.App/ViewModels/ExplorerViewModel.cs` — `Add` dispatch + add-recipe flow (T3).
- Tests: `tests/Nfty.Core.Tests/CookBookEditsTests.cs` (T1); `tests/Nfty.App.Tests/NewRecipeViewModelTests.cs` (T2); `tests/Nfty.App.Tests/ExplorerAddRecipeTests.cs` (T3).

---

### Task 1: Core — `CookBookEdits.UpsertRecipe`

**Files:** Modify `src/Nfty.Core/Editing/CookBookEdits.cs`; Test `tests/Nfty.Core.Tests/CookBookEditsTests.cs` (append).

**Interfaces:**
- Produces: `LoadedCookBook CookBookEdits.UpsertRecipe(LoadedCookBook book, LoadedRecipe recipe, double weight)`.

- [ ] **Step 1: Failing tests** — append to `CookBookEditsTests.cs` (reuse its `Ing` helper + `TwoRecipeBook`):
```csharp
    [Fact]
    public void UpsertRecipe_adds_a_new_recipe_with_its_weight()
    {
        var newRecipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("bird", "Bird", new List<string>(),
                System.Array.Empty<IncompatibilityRule>()),
            Ingredients = new List<LoadedIngredient>(),
        };
        var b = CookBookEdits.UpsertRecipe(TwoRecipeBook(), newRecipe, 25);
        Assert.Contains(b.Recipes, r => r.Manifest.Id == "bird");
        Assert.Equal(25, b.Manifest.RecipeWeights["bird"]);
        Assert.Equal(3, b.Recipes.Count);                       // cat, dog, bird
        Assert.Contains(b.Recipes, r => r.Manifest.Id == "cat"); // existing kept
    }

    [Fact]
    public void UpsertRecipe_replaces_an_existing_recipe_and_updates_its_weight()
    {
        var replacement = new LoadedRecipe
        {
            Manifest = new RecipeManifest("dog", "Dog2", new List<string>(),
                System.Array.Empty<IncompatibilityRule>()),
            Ingredients = new List<LoadedIngredient>(),
        };
        var b = CookBookEdits.UpsertRecipe(TwoRecipeBook(), replacement, 5);
        Assert.Equal(2, b.Recipes.Count);                       // still cat, dog (replaced)
        Assert.Equal("Dog2", b.Recipes.Single(r => r.Manifest.Id == "dog").Manifest.Name);
        Assert.Equal(5, b.Manifest.RecipeWeights["dog"]);
    }
```

- [ ] **Step 2: Run — fail** (`UpsertRecipe` missing).

- [ ] **Step 3: Implement** in `CookBookEdits.cs` (match the `CookBookManifest with { RecipeWeights = … }` shape used by `RemoveRecipe`):
```csharp
    /// <summary>Adds a recipe to a cookbook (or replaces one with the same id) and sets its selection
    /// weight. Reuses every other recipe/image by reference; disposes nothing.</summary>
    public static LoadedCookBook UpsertRecipe(LoadedCookBook book, LoadedRecipe recipe, double weight)
    {
        var recipes = book.Recipes.Where(r => r.Manifest.Id != recipe.Manifest.Id).Append(recipe).ToList();
        var weights = book.Manifest.RecipeWeights
            .Where(kv => kv.Key != recipe.Manifest.Id)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        weights[recipe.Manifest.Id] = weight;
        return new LoadedCookBook
        {
            Manifest = book.Manifest with { RecipeWeights = weights },
            Recipes = recipes,
            SourceSha256 = book.SourceSha256,
        };
    }
```

- [ ] **Step 4: Run — pass;** `dotnet test tests/Nfty.Core.Tests --nologo` green; `dotnet build src/Nfty.Core --nologo` 0 warnings.

- [ ] **Step 5: Commit** `feat(editing): CookBookEdits.UpsertRecipe`

---

### Task 2: New Recipe wizard — `DerivedId` + gated `Create`

**Files:** Modify `src/Nfty.App/ViewModels/NewRecipeViewModel.cs`; Test `tests/Nfty.App.Tests/NewRecipeViewModelTests.cs` (create or append).

**Interfaces:**
- Produces: `string NewRecipeViewModel.DerivedId`; `Create` closes with the VM, gated on a non-blank id.

- [ ] **Step 1: Failing tests** — `tests/Nfty.App.Tests/NewRecipeViewModelTests.cs` (if the file exists, append + update any `Create_reports_not_yet_wired`):
```csharp
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

public class NewRecipeViewModelTests
{
    private static NewRecipeViewModel Vm() => new(new FakeDialogs(), new FakeNotYetWired());

    [Fact]
    public void DerivedId_slugs_the_name()
    {
        var vm = Vm(); vm.Name = "Night Sky";
        Assert.Equal("night-sky", vm.DerivedId);
    }

    [Fact]
    public void Create_is_disabled_until_the_name_yields_a_non_blank_id()
    {
        var vm = Vm();
        Assert.False(vm.CreateCommand.CanExecute(null));
        vm.Name = "  ";
        Assert.False(vm.CreateCommand.CanExecute(null));
        vm.Name = "Bird";
        Assert.True(vm.CreateCommand.CanExecute(null));
    }

    [Fact]
    public async System.Threading.Tasks.Task Create_closes_the_dialog_with_the_vm()
    {
        var real = new DialogService();
        var vm = new NewRecipeViewModel(real, new FakeNotYetWired()) { Name = "Bird" };
        var task = real.ShowAsync<NewRecipeViewModel>(vm);
        vm.CreateCommand.Execute(null);
        Assert.Same(vm, await task);
    }
}
```
  (If `NewRecipeViewModelTests.cs` already exists with a `Create_reports_not_yet_wired` test, replace that test with the three above — `Create`'s contract changes from notify-stub to close-with-vm.)

- [ ] **Step 2: Run — fail** (`DerivedId` missing; `Create` closes with null / reports).

- [ ] **Step 3: Implement** in `NewRecipeViewModel.cs`:
  - Add `using System;`.
  - Add after the radio-button properties:
    ```csharp
    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(DerivedId));
        CreateCommand.NotifyCanExecuteChanged();
    }

    /// <summary>The recipe id derived from the name: lower-case, spaces to dashes.</summary>
    public string DerivedId => string.Join('-',
        Name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    ```
  - Replace `Create`:
    ```csharp
    private bool CanCreate() => !string.IsNullOrWhiteSpace(DerivedId);

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private void Create() => Dialogs.Close(this);
    ```

- [ ] **Step 4: Run — pass;** whole App suite green; build 0 warnings.

- [ ] **Step 5: Commit** `feat(gui): New Recipe wizard derives an id and closes with its result`

---

### Task 3: Explorer add-recipe flow

**Files:** Modify `src/Nfty.App/ViewModels/ExplorerViewModel.cs`; Test `tests/Nfty.App.Tests/ExplorerAddRecipeTests.cs` (create).

**Interfaces:**
- Consumes: `NewRecipeViewModel` (T2), `CookBookEdits.UpsertRecipe` (T1), `Validator.ValidateRecipe`, `CookBookPersistence.PersistAsync`/`ApplyBook`/`ShowError` (existing).

- [ ] **Step 1: Failing tests** — `tests/Nfty.App.Tests/ExplorerAddRecipeTests.cs`:
```csharp
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Xunit;

namespace Nfty.App.Tests;

public class ExplorerAddRecipeTests
{
    private sealed class AddRecipeDialogs : IDialogService
    {
        private readonly string _name; private readonly double _weight;
        public string? ErrorTitle { get; private set; }
        public AddRecipeDialogs(string name, double weight = 50) { _name = name; _weight = weight; }
        public ViewModelBase? Active => null;
        public event Action? Changed { add { } remove { } }
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog)
        {
            if (dialog is NewRecipeViewModel w) { w.Name = _name; w.Weight = _weight; return Task.FromResult((TResult?)(object?)w); }
            if (dialog is ErrorDialogViewModel e) { ErrorTitle = e.Title; return Task.FromResult(default(TResult)); }
            return Task.FromResult(default(TResult));
        }
        public void Close(object? result) { }
    }

    private static (ExplorerViewModel vm, CookBookSession session, string path) Explorer(IDialogService dialogs)
    {
        (var path, var session, _, _) = IngredientEditorSaveTests.OnDisk();
        var nav = new FakeNav();
        var vm = new ExplorerViewModel(session.Current!, nav, dialogs, new FakeNotYetWired(), new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
            ExplorerViewModelTests.CookFactory(dialogs), session);
        return (vm, session, path);
    }

    [AvaloniaFact]
    public async Task Add_recipe_on_the_root_persists_with_weight_and_selects_it()
    {
        var dialogs = new AddRecipeDialogs("Bird", 25);
        var (vm, session, path) = Explorer(dialogs);
        try
        {
            vm.ToggleLockCommand.Execute(null);
            vm.SelectNodeCommand.Execute(vm.Root);          // cookbook root
            await vm.AddCommand.ExecuteAsync(null);

            using var reread = CookBookArchive.Read(path);
            Assert.Contains(reread.Recipes, r => r.Manifest.Id == "bird");
            Assert.Equal(25, reread.Manifest.RecipeWeights["bird"]);
            Assert.Equal("bird", vm.SelectedNode!.Id);       // new recipe selected
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Add_recipe_duplicate_or_blank_reports_and_writes_nothing()
    {
        foreach (var name in new[] { "cat", "   " })   // existing id / blank
        {
            var dialogs = new AddRecipeDialogs(name);
            var (vm, session, path) = Explorer(dialogs);
            try
            {
                var before = CookBookArchive.Read(path).Recipes.Count;
                vm.ToggleLockCommand.Execute(null);
                vm.SelectNodeCommand.Execute(vm.Root);
                await vm.AddCommand.ExecuteAsync(null);
                Assert.NotNull(dialogs.ErrorTitle);
                using var reread = CookBookArchive.Read(path);
                Assert.Equal(before, reread.Recipes.Count);
                vm.Dispose();
            }
            finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
        }
    }
}
```
  (Confirm the A2a `OnDisk` fixture has a recipe `cat`. `NewRecipeViewModel` exposes `Weight` — verify the property name.)

- [ ] **Step 2: Run — fail** (add-recipe branch missing — the root selection currently falls to the stub).

- [ ] **Step 3: Implement** in `ExplorerViewModel.Add`. Insert a CookBook-root branch **before** the stub fallthrough, keeping the existing recipe (add-ingredient) branch. Restructure the head of `Add`:
```csharp
    [RelayCommand]
    private async Task Add()
    {
        if (!IsEditing || _session.SourcePath is null) { _notify.Report(AddLabel); return; }

        switch (SelectedNode?.Domain)
        {
            case LoadedRecipe recipe:
                await AddIngredientTo(recipe);       // the existing A2b flow, extracted verbatim
                return;
            case LoadedCookBook:
                await AddRecipe();
                return;
            default:
                _notify.Report(AddLabel);
                return;
        }
    }

    private async Task AddRecipe()
    {
        var wizard = new NewRecipeViewModel(_dialogs, _notify);
        var result = await _dialogs.ShowAsync<NewRecipeViewModel>(wizard);
        if (result is null) return;
        if (string.IsNullOrWhiteSpace(result.DerivedId))
        {
            await ShowError("Invalid recipe", "The recipe needs a name.");
            return;
        }
        if (_book.Recipes.Any(r => r.Manifest.Id == result.DerivedId))
        {
            await ShowError("Duplicate recipe", $"A recipe “{result.DerivedId}” already exists.");
            return;
        }
        try
        {
            var recipe = new LoadedRecipe
            {
                Manifest = new RecipeManifest(result.DerivedId, result.Name,
                    Array.Empty<string>(), Array.Empty<IncompatibilityRule>()),
                Ingredients = Array.Empty<LoadedIngredient>(),
            };
            var problems = Validator.ValidateRecipe(recipe);
            if (problems.Count > 0) { await ShowError("Invalid recipe", string.Join("\n", problems)); return; }

            var book2 = CookBookEdits.UpsertRecipe(_book, recipe, result.Weight);
            var book3 = await CookBookPersistence.PersistAsync(_session, book2);
            ApplyBook(book3, recipe.Manifest.Id);
        }
        catch (Exception ex)
        {
            await ShowError("Could not add recipe", ex.Message);
        }
    }
```
  - Extract the existing A2b add-ingredient body into `private async Task AddIngredientTo(LoadedRecipe recipe)` (the code currently inside the old `Add` after the `recipe` bind) — move it verbatim, so A2b behavior is unchanged. Its own dup/validate/persist/OpenEditor logic stays intact.
  - Add `using Nfty.Core.Model;` if `RecipeManifest`/`IncompatibilityRule` aren't resolved (they're in `Nfty.Core.Model`; confirm the using — `LoadedRecipe`/`LoadedIngredient` are in `Nfty.Core.Formats`, already present). `IncompatibilityRule` is in `Nfty.Core.Model` — add the using if needed.

- [ ] **Step 4: Run — pass;** the A2b `ExplorerAddIngredientTests` stay green (regression guard); whole App suite green; build 0 warnings.

- [ ] **Step 5: Commit** `feat(gui): add a recipe to the cookbook from the Explorer`

---

### Task 4: Verification + manual smoke

**Files:** none.

- [ ] **Step 1:** `dotnet build nfty.sln --nologo` → 0 warnings. `dotnet test nfty.sln --nologo` → all pass (report Cli/App/Core totals).
- [ ] **Step 2:** `git diff --name-only <base>..HEAD -- src/Nfty.Core/` → only `CookBookEdits.cs`. No view edits → no hex scan.
- [ ] **Step 3: Manual smoke (user):** open a `.cbk`, edit-lock on, select the cookbook root → **Add** → name a recipe + weight → Create → the empty recipe appears + is selected → select it → **Add** an ingredient (A2b) → paint → Save → reopen to confirm both persisted; try a duplicate/blank recipe name → error.
- [ ] **Step 4:** Commit any smoke fixups: `test(gui): verify Explorer add-recipe end-to-end`.

---

## Self-Review
- **Spec coverage:** §2.1 wizard `DerivedId`/gated `Create` → T2. §2.2 Core `UpsertRecipe` (+ weight) → T1. §2.3 Explorer `Add` dispatch + add-recipe flow → T3. §2.4 (no view) → n/a. §4 error handling (cancel/blank/dup/validate/write → error dialog, nothing written) → T3. §5 tests → T1/T2/T3 + regression (A2b suite) + manual. §6 risks: empty recipe validates (T1/T3 assert), `RecipeWeights` set (T1 asserts), Add-dispatch regression (A2b tests), blank-id guard at both layers (T2 + T3).
- **Placeholder scan:** full code in every step; the "extract A2b body verbatim" step references existing code, not a TBD; the two "confirm the fixture/property" notes point at real files.
- **Type consistency:** `UpsertRecipe(book,recipe,weight)` (T1) consumed in T3; `NewRecipeViewModel.DerivedId`/`Weight`/`Create` (T2) consumed in T3; `RecipeManifest(Id,Name,LayerOrder,Rules)` + `IncompatibilityRule` match `Nfty.Core.Model`; `LoadedRecipe{Manifest,Ingredients}` init props; `PersistAsync`/`ApplyBook`/`ShowError`/`Validator.ValidateRecipe→IReadOnlyList<string>` match existing signatures; `ShowAsync<NewRecipeViewModel>` returns the `Close(this)` result. The `AddIngredientTo` extraction preserves the A2b method bodies (`Build`, `adopted` flag, `OpenEditor`).
