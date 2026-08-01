# nfty GUI — Explorer add ingredient (A2b) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Add a new ingredient (with one blank starter variant) to the selected recipe from the Explorer, persist it to the source `.cbk`, and open the editor on it.

**Architecture:** The New Ingredient wizard gains a `DerivedId` + `BuildColorization()` + `Build(canvas)` that turns its fields into a `LoadedIngredient` (manifest + one blank value-map variant). The Explorer's `Add` becomes async: for a recipe selection it shows the wizard, validates the result, `UpsertIngredient` → `CookBookPersistence.PersistAsync` (A2a) → `ApplyBook` → opens the editor. No `Nfty.Core` change.

**Tech Stack:** .NET 10, Avalonia 11.2.3, CommunityToolkit.Mvvm, `Nfty.Core.Editing`/`Formats`/`Model`, xUnit + Avalonia.Headless.XUnit.

## Global Constraints
- **No `Nfty.Core` change** — reuse `UpsertIngredient`, `Validator.ValidateIngredient`, `ValueMap`.
- **Blank-image ownership:** `Build` returns a live `Image<Rgba32>`; on the success path it is adopted by the persisted book (do NOT dispose), on every early-return/error path the Explorer disposes it exactly once.
- **Colorization defaults:** quantize `HueQuantize=12, SatQuantize=4`; `ColorModel.Hsv`.
- **Editor handoff:** after persist, open the editor on the `(LoadedRecipe, LoadedIngredient)` resolved **from `book3`** (the graph the session now holds), never the pre-persist objects.
- **Id:** `DerivedId` = lower-invariant, spaces→`-`, empty tokens stripped (mirrors `NewCookBookViewModel.DerivedId`). Deterministic, no RNG.
- Determinism/idiom: `StringComparer.Ordinal` where ids sort; token brushes only in Views; `[AvaloniaFact]` for Avalonia-constructing tests. Build 0 warnings. Conventional commits. Agents: caveman-ultra terse chat; code/commits/reports normal prose.

## File Structure
- `src/Nfty.App/ViewModels/NewIngredientViewModel.cs` — `DerivedId`, `BuildColorization`, `Build`, `Create`→`Close(this)` (T1).
- `src/Nfty.App/ViewModels/ExplorerViewModel.cs` — async `Add` recipe flow (T2).
- Tests: `tests/Nfty.App.Tests/NewIngredientViewModelTests.cs` (T1); `tests/Nfty.App.Tests/ExplorerAddIngredientTests.cs` (T2).

---

### Task 1: New Ingredient wizard builds an ingredient

**Files:** Modify `src/Nfty.App/ViewModels/NewIngredientViewModel.cs`; Test `tests/Nfty.App.Tests/NewIngredientViewModelTests.cs` (create).

**Interfaces:**
- Produces: `string NewIngredientViewModel.DerivedId`; `Colorization? BuildColorization()`; `LoadedIngredient Build(Dimensions canvas)`; `Create` closes with the VM.

- [ ] **Step 1: Failing tests** — `tests/Nfty.App.Tests/NewIngredientViewModelTests.cs`:
```csharp
using System.Linq;
using Avalonia.Headless.XUnit;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;
using Nfty.Core.Model;
using Xunit;

namespace Nfty.App.Tests;

public class NewIngredientViewModelTests
{
    private static NewIngredientViewModel Vm() => new(new FakeDialogs(), new FakeNotYetWired());

    [Fact]
    public void DerivedId_slugs_the_name()
    {
        var vm = Vm(); vm.Name = "Left Ear";
        Assert.Equal("left-ear", vm.DerivedId);
    }

    [Fact]
    public void BuildColorization_matches_the_kind()
    {
        var vm = Vm();
        vm.Kind = LayerKind.Dynamic; vm.HueMin = 10; vm.HueMax = 200; vm.SatMin = 30; vm.SatMax = 90;
        var dyn = vm.BuildColorization()!;
        Assert.Equal(ColorModel.Hsv, dyn.Model);
        Assert.Equal(12, dyn.HueQuantize); Assert.Equal(4, dyn.SatQuantize);
        var range = dyn.Entries.Single().Range!;
        Assert.Equal((10, 200, 30, 90), (range.HueMin, range.HueMax, range.SatMin, range.SatMax));

        vm.Kind = LayerKind.Static; vm.FixedColor = "hex:d6249f";
        Assert.Equal("hex:d6249f", vm.BuildColorization()!.Entries.Single().Fixed);

        vm.Kind = LayerKind.Custom;
        Assert.Null(vm.BuildColorization());
    }

    [AvaloniaFact]
    public void Build_makes_an_ingredient_with_one_blank_starter_variant()
    {
        var vm = Vm(); vm.Name = "Hat"; vm.Kind = LayerKind.Dynamic;
        using var ing = vm.Build(new Dimensions(8, 8));
        Assert.Equal("hat", ing.Manifest.Id);
        var v = Assert.Single(ing.Manifest.Variants);
        Assert.Equal("variant-1", v.Id);
        Assert.Equal(8, ing.VariantImages["variant-1"].Width);
        Assert.Equal(0, ValueMap.FromImage(ing.VariantImages["variant-1"]).GetValue(4, 4));  // blank
        Assert.NotNull(ing.Manifest.Colorization);
    }

    [AvaloniaFact]
    public void Create_closes_the_dialog_with_the_vm()
    {
        var dialogs = new FakeDialogs();
        var vm = new NewIngredientViewModel(dialogs, new FakeNotYetWired()) { Name = "Hat" };
        object? closed = null;
        // FakeDialogs.Close stores nothing; drive via the real DialogService to capture the result:
        var real = new DialogService();
        var vm2 = new NewIngredientViewModel(real, new FakeNotYetWired()) { Name = "Hat" };
        var task = real.ShowAsync<NewIngredientViewModel>(vm2);
        vm2.CreateCommand.Execute(null);
        closed = task.Result;
        Assert.Same(vm2, closed);
    }
}
```
  (`FakeDialogs`/`FakeNotYetWired` already exist in `Fakes.cs`. If `Assert.Equal((tuple),(tuple))` is awkward, assert the four range fields individually.)

- [ ] **Step 2: Run — fail** (`DerivedId`/`BuildColorization`/`Build` missing; `Create` closes with null).

- [ ] **Step 3: Implement** in `NewIngredientViewModel.cs`:
  - Add usings: `using System;`, `using System.Collections.Generic;`, `using Nfty.Core.Editing;`, `using Nfty.Core.Formats;`, `using SixLabors.ImageSharp;`, `using SixLabors.ImageSharp.PixelFormats;`.
  - Add:
    ```csharp
    public string DerivedId => string.Join('-',
        Name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Build the colorization config from the wizard fields (quantize defaults 12/4).</summary>
    public Colorization? BuildColorization() => Kind switch
    {
        LayerKind.Dynamic => new Colorization(ColorModel.Hsv, 12, 4,
            new[] { new ColorEntry(1, new ColorRange(HueMin, HueMax, SatMin, SatMax), null) }),
        LayerKind.Static => new Colorization(ColorModel.Hsv, 12, 4,
            new[] { new ColorEntry(1, null, FixedColor) }),
        _ => null,   // Custom — composited as-is
    };

    /// <summary>Turn the wizard into a loaded ingredient with one blank starter variant at the given
    /// canvas size. The caller owns the returned image until the book adopts it.</summary>
    public LoadedIngredient Build(Dimensions canvas)
    {
        const string variantId = "variant-1";
        var manifest = new IngredientManifest(DerivedId, Name, Kind, BuildColorization(),
            new[] { new Variant(variantId, "Variant 1", 1) });
        var images = new Dictionary<string, Image<Rgba32>>(StringComparer.Ordinal)
        {
            [variantId] = ValueMap.ForCanvas(canvas).ToImage(),
        };
        return new LoadedIngredient { Manifest = manifest, VariantImages = images };
    }
    ```
  - Notify `DerivedId` on name change — extend the existing `OnNameChanged` or add one:
    ```csharp
    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(DerivedId));
    ```
    (If a name-changed partial already exists, add the `OnPropertyChanged` line to it.)
  - Change `Create`: `[RelayCommand] private void Create() => Dialogs.Close(this);` (drop the `Notify.Report`).

- [ ] **Step 4: Run — pass;** `dotnet test tests/Nfty.App.Tests --nologo` whole suite green; `dotnet build src/Nfty.Desktop --nologo` 0 warnings.

- [ ] **Step 5: Commit** `feat(gui): New Ingredient wizard builds an ingredient with a blank variant`

---

### Task 2: Explorer add-ingredient flow

**Files:** Modify `src/Nfty.App/ViewModels/ExplorerViewModel.cs`; Test `tests/Nfty.App.Tests/ExplorerAddIngredientTests.cs` (create).

**Interfaces:**
- Consumes: `NewIngredientViewModel.Build` (T1), `CookBookEdits.UpsertIngredient`, `Validator.ValidateIngredient`, `CookBookPersistence.PersistAsync` (A2a), `ApplyBook`/`OpenEditor` (existing).

- [ ] **Step 1: Failing tests** — `tests/Nfty.App.Tests/ExplorerAddIngredientTests.cs`. Use a dialog stub that "fills + Creates" the wizard the Explorer shows:
```csharp
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Models;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Xunit;

namespace Nfty.App.Tests;

public class ExplorerAddIngredientTests
{
    // A dialog stub that acts as the user: fills the New-Ingredient wizard the Explorer shows and
    // "clicks Create" (returns it), records any error dialog, and returns a set confirm value otherwise.
    private sealed class AddDialogs : IDialogService
    {
        private readonly string _name; private readonly LayerKind _kind;
        public string? ErrorTitle { get; private set; }
        public AddDialogs(string name, LayerKind kind) { _name = name; _kind = kind; }
        public ViewModelBase? Active => null;
        public event Action? Changed { add { } remove { } }
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog)
        {
            if (dialog is NewIngredientViewModel w) { w.Name = _name; w.Kind = _kind; return Task.FromResult((TResult?)(object?)w); }
            if (dialog is ErrorDialogViewModel e) { ErrorTitle = e.Title; return Task.FromResult(default(TResult)); }
            return Task.FromResult(default(TResult));
        }
        public void Close(object? result) { }
    }

    private static (ExplorerViewModel vm, CookBookSession session, string path, FakeNav nav) Explorer(IDialogService dialogs)
    {
        (var path, var session, _, _) = IngredientEditorSaveTests.OnDisk();
        var nav = new FakeNav();
        var vm = new ExplorerViewModel(session.Current!, nav, dialogs, new FakeNotYetWired(), new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
            ExplorerViewModelTests.CookFactory(dialogs), session);
        return (vm, session, path, nav);
    }

    [AvaloniaFact]
    public async Task Add_ingredient_persists_selects_and_opens_the_editor()
    {
        var dialogs = new AddDialogs("Hat", LayerKind.Dynamic);
        var (vm, session, path, nav) = Explorer(dialogs);
        try
        {
            vm.ToggleLockCommand.Execute(null);
            vm.SelectNodeCommand.Execute(vm.Root.Children[0]);   // recipe "cat"
            await vm.AddCommand.ExecuteAsync(null);

            using var reread = CookBookArchive.Read(path);
            Assert.Contains(reread.Recipes[0].Ingredients, i => i.Manifest.Id == "hat");
            Assert.Equal("hat", vm.SelectedNode!.Id);            // new ingredient selected
            Assert.IsType<IngredientEditorViewModel>(nav.Current); // editor opened on it
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Add_duplicate_id_reports_an_error_and_writes_nothing()
    {
        var dialogs = new AddDialogs("aura", LayerKind.Dynamic);   // "aura" already exists in "cat"
        var (vm, session, path, nav) = Explorer(dialogs);
        try
        {
            vm.ToggleLockCommand.Execute(null);
            vm.SelectNodeCommand.Execute(vm.Root.Children[0]);
            await vm.AddCommand.ExecuteAsync(null);
            Assert.NotNull(dialogs.ErrorTitle);                  // error surfaced
            using var reread = CookBookArchive.Read(path);
            Assert.Single(reread.Recipes[0].Ingredients.Where(i => i.Manifest.Id == "aura")); // still one
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }
}
```
  (Confirm the A2a `OnDisk` fixture: recipe `cat` contains ingredient `aura` on an 8×8 canvas. Adjust ids if different.)

- [ ] **Step 2: Run — fail** (`Add` is the sync `_notify` stub).

- [ ] **Step 3: Implement** in `ExplorerViewModel.cs`. Replace `[RelayCommand] private void Add() => _notify.Report(AddLabel);`:
```csharp
    [RelayCommand]
    private async Task Add()
    {
        // Only "Add ingredient" (a recipe selected) is wired this slice; other kinds stay stubs.
        if (SelectedNode?.Domain is not LoadedRecipe recipe
            || !IsEditing || _session.SourcePath is null)
        {
            _notify.Report(AddLabel);
            return;
        }

        var wizard = new NewIngredientViewModel(_dialogs, _notify);
        var result = await _dialogs.ShowAsync<NewIngredientViewModel>(wizard);
        if (result is null) return;   // cancelled

        var newIng = result.Build(_book.Manifest.Canvas);   // owns the blank image
        try
        {
            if (recipe.Ingredients.Any(i => i.Manifest.Id == newIng.Manifest.Id))
            {
                await ShowError("Duplicate ingredient",
                    $"An ingredient “{newIng.Manifest.Id}” already exists in “{recipe.Manifest.Name}”.");
                newIng.Dispose(); return;
            }
            var problems = Validator.ValidateIngredient(newIng);
            if (problems.Count > 0)
            {
                await ShowError("Invalid ingredient", string.Join("\n", problems));
                newIng.Dispose(); return;
            }

            var book2 = CookBookEdits.UpsertIngredient(_book, recipe.Manifest.Id, newIng);
            var book3 = await CookBookPersistence.PersistAsync(_session, book2);   // newIng now owned by book3
            ApplyBook(book3, newIng.Manifest.Id);

            var recipe3 = book3.Recipes.First(r => r.Manifest.Id == recipe.Manifest.Id);
            var ing3 = recipe3.Ingredients.First(i => i.Manifest.Id == newIng.Manifest.Id);
            OpenEditor(ing3, recipe3);   // paint the blank variant; the editor's Save persists
        }
        catch (Exception ex)
        {
            newIng.Dispose();   // never adopted — free it
            await ShowError("Could not add ingredient", ex.Message);
        }
    }

    private Task ShowError(string title, string message) =>
        _dialogs.ShowAsync<object>(new ErrorDialogViewModel(_dialogs, title, message));
```
  - Add `using Nfty.Core.Model;`? Not needed (LayerKind used only via the domain objects; `Validator` is in `Nfty.Core.Formats`, already imported). Confirm `Validator` resolves — add `using Nfty.Core.Formats;` (already present). `LoadedRecipe`/`LoadedIngredient` from `Nfty.Core.Formats` (present).
  - **Disposal correctness:** on the success path `newIng` is adopted by `book2/book3` (via Upsert) and must NOT be disposed; the `catch` only runs for exceptions and disposes it, but note that after a *successful* `PersistAsync` the `catch` won't fire. The dup/validation early-returns dispose explicitly. This mirrors A2a — verify no double-dispose (the success path never disposes `newIng`).

- [ ] **Step 4: Run — pass;** whole App suite green; build 0 warnings.

- [ ] **Step 5: Commit** `feat(gui): add an ingredient to a recipe from the Explorer`

---

### Task 3: Verification + manual smoke

**Files:** none.

- [ ] **Step 1:** `dotnet build nfty.sln --nologo` → 0 warnings. `dotnet test nfty.sln --nologo` → all pass (report Cli/App/Core totals).
- [ ] **Step 2:** `git diff --name-only <base>..HEAD -- src/Nfty.Core/` → empty (no Core change). No view edits → no hex scan needed.
- [ ] **Step 3: Manual smoke (user):** open a `.cbk`, edit-lock on, select a recipe → **Add** → fill the New Ingredient wizard (name, kind, colour) → Create → the ingredient appears in the tree and the editor opens on its blank variant → paint → Save → reopen the `.cbk` to confirm; try adding a duplicate name → error dialog.
- [ ] **Step 4:** Commit any smoke fixups: `test(gui): verify Explorer add-ingredient end-to-end`.

---

## Self-Review
- **Spec coverage:** §2.1 wizard `DerivedId`/`BuildColorization`/`Build`/`Create`→`Close(this)` → T1. §2.2 Explorer async `Add` recipe flow (show → build → dup/validate → upsert → persist → ApplyBook → OpenEditor) → T2. §2.3 (no new view) → n/a. §4 error handling (cancel no-op, dup/validate/write → error dialog + dispose) → T2. §5 tests → T1 (wizard unit) + T2 (persist/select/open-editor + duplicate) + T3 manual. §6 risks: blank-image ownership (dispose on each failure branch, adopted on success) + editor-handoff-from-`book3` (`recipe3`/`ing3` resolved from `book3`) → T2 code + comments.
- **Placeholder scan:** full code/edits in every step; the two "confirm the fixture ids / existing partial" notes point at real files, not TBDs.
- **Type consistency:** `DerivedId`/`BuildColorization()`/`Build(Dimensions)`/`Create` (T1) consumed by the Explorer flow (T2); `IngredientManifest(Id,Name,Kind,Colorization?,IReadOnlyList<Variant>)` + `Variant(Id,Name,Weight)` + `Colorization(Model,HueQuantize,SatQuantize,Entries)` + `ColorEntry(Weight,Range,Fixed)` + `ColorRange(HueMin,HueMax,SatMin,SatMax)` match `Nfty.Core.Model`; `UpsertIngredient(book,recipeId,ingredient)` + `PersistAsync(session,book2)` + `ApplyBook(book,selectId)` + `OpenEditor(i,r)` + `Validator.ValidateIngredient→IReadOnlyList<string>` match existing signatures; `ShowAsync<NewIngredientViewModel>` returns the `Close(this)` result.
