# nfty GUI — Open a loose `.rcp` read-only (B2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Open a standalone `.rcp` as a read-only one-recipe cookbook in the Explorer.

**Architecture:** `LooseWorkspace.WrapRecipe` wraps a loose recipe in a synthetic single-recipe cookbook (canvas from its variants). Landing's `.rcp` Import reads it, wraps it, opens it into the session with a null source path, and navigates to the Explorer — read-only falls out of the null `SourcePath` (add/delete/save are already gated on a source file). No `Nfty.Core` change.

**Tech Stack:** .NET 10, Avalonia 11.2.3, CommunityToolkit.Mvvm, `Nfty.Core.Formats.RecipeArchive`, xUnit + Avalonia.Headless.XUnit.

## Global Constraints
- **No `Nfty.Core` change** — reuse `RecipeArchive.Read`, the Explorer, the session.
- **Read-only is emergent:** `session.Open(book, null)` → `SourcePath` null → Explorer add/delete + editor Save already disabled. Do NOT add a new "read-only" flag.
- **Session semantics:** opening a loose `.rcp` uses the normal `session.Open` (replaces + disposes the previous book — standard open-a-document behavior; the session then owns the wrapped book). No bespoke ownership.
- Determinism/idiom: `StringComparer.Ordinal` where ids sort; no RNG; token brushes only (no view change here); `[AvaloniaFact]` for Avalonia-constructing tests. Build 0 warnings. Conventional commits. Agents: caveman-ultra terse chat; code/commits/reports normal prose. If any Avalonia/library API is uncertain, pull docs via Context7 rather than guessing (this slice is domain C#, so likely unneeded).

## File Structure
- `src/Nfty.App/Services/LooseWorkspace.cs` — add `WrapRecipe` (T1).
- `src/Nfty.App/ViewModels/LandingViewModel.cs` — `.rcp` Import → `OpenLooseRecipe` (T2).
- Tests: `tests/Nfty.App.Tests/LooseWorkspaceTests.cs` (append, T1); `tests/Nfty.App.Tests/LandingImportRcpTests.cs` (T2).

---

### Task 1: `LooseWorkspace.WrapRecipe`

**Files:** Modify `src/Nfty.App/Services/LooseWorkspace.cs`; Test `tests/Nfty.App.Tests/LooseWorkspaceTests.cs` (append).

**Interfaces:**
- Produces: `static LoadedCookBook LooseWorkspace.WrapRecipe(LoadedRecipe recipe)`.

- [ ] **Step 1: Failing tests** — append to `LooseWorkspaceTests.cs`:
```csharp
    private static LoadedIngredient Ing(string id, int w, int h) => new()
    {
        Manifest = new IngredientManifest(id, id, LayerKind.Dynamic, null,
            new[] { new Variant(id + "-v", "V", 1) }),
        VariantImages = new Dictionary<string, Image<Rgba32>> { [id + "-v"] = new(w, h) },
    };

    [Fact]
    public void WrapRecipe_builds_a_one_recipe_book_sized_to_the_first_variant()
    {
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "bg" }, System.Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { Ing("bg", 5, 7) },
        };
        using var book = LooseWorkspace.WrapRecipe(recipe);
        Assert.Equal(5, book.Manifest.Canvas.Width);
        Assert.Equal(7, book.Manifest.Canvas.Height);
        Assert.Same(recipe, Assert.Single(book.Recipes));
        Assert.Equal(100, book.Manifest.RecipeWeights["cat"]);   // keyed by the recipe's real id
    }

    [Fact]
    public void WrapRecipe_falls_back_to_a_default_canvas_when_the_recipe_has_no_images()
    {
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("empty", "Empty", System.Array.Empty<string>(),
                System.Array.Empty<IncompatibilityRule>()),
            Ingredients = System.Array.Empty<LoadedIngredient>(),
        };
        using var book = LooseWorkspace.WrapRecipe(recipe);
        Assert.Equal(512, book.Manifest.Canvas.Width);
        Assert.Equal(512, book.Manifest.Canvas.Height);
    }
```

- [ ] **Step 2: Run — fail** (`WrapRecipe` missing). `dotnet test tests/Nfty.App.Tests --filter "FullyQualifiedName~LooseWorkspaceTests" --nologo`.

- [ ] **Step 3: Implement** in `LooseWorkspace.cs` (add the method beside `WrapIngredient`):
```csharp
    public static LoadedCookBook WrapRecipe(LoadedRecipe recipe)
    {
        var img = recipe.Ingredients.SelectMany(i => i.VariantImages.Values).FirstOrDefault();
        var canvas = img is null ? new Dimensions(512, 512) : new Dimensions(img.Width, img.Height);
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("loose", recipe.Manifest.Name, canvas,
                new Collection(recipe.Manifest.Name, "", "L"),
                new Dictionary<string, double> { [recipe.Manifest.Id] = 100 }),
            Recipes = new[] { recipe },
        };
    }
```
  (`SelectMany`/`FirstOrDefault` need `System.Linq` — already imported in `LooseWorkspace.cs`.)

- [ ] **Step 4: Run — pass;** `dotnet test tests/Nfty.App.Tests --nologo` green; `dotnet build src/Nfty.Desktop --nologo` 0 warnings.

- [ ] **Step 5: Commit** `feat(gui): LooseWorkspace.WrapRecipe wraps a loose recipe for viewing`

---

### Task 2: Landing Import of a `.rcp`

**Files:** Modify `src/Nfty.App/ViewModels/LandingViewModel.cs`; Test `tests/Nfty.App.Tests/LandingImportRcpTests.cs` (create).

**Interfaces:**
- Consumes: `RecipeArchive.Read`, `LooseWorkspace.WrapRecipe`, the existing `_session.Open` + `_explorerFactory`.

- [ ] **Step 1: Failing test** — `tests/Nfty.App.Tests/LandingImportRcpTests.cs`. Write a real temp `.rcp`, import it, assert the Explorer opens over a source-less book and delete is disabled:
```csharp
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public class LandingImportRcpTests
{
    private sealed class StubPicker : IFilePickerService
    {
        private readonly string? _path;
        public StubPicker(string? path) => _path = path;
        public Task<string?> OpenFileAsync(string title, params string[] extensions) => Task.FromResult(_path);
        public Task<string?> SaveFileAsync(string title, string defaultExtension) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private static (LandingViewModel vm, FakeNav nav, CookBookSession session) Landing(IFilePickerService picker)
    {
        var nav = new FakeNav(); var dialogs = new FakeDialogs(); var notify = new FakeNotYetWired();
        var session = new CookBookSession();
        var vm = new LandingViewModel(nav, dialogs, notify, picker, new RecentsService(), session,
            book => new ExplorerViewModel(book, nav, dialogs, notify, new ImageBridge(),
                ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
                ExplorerViewModelTests.CookFactory(dialogs), session),
            set => new SetBrowserViewModel(set),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs));
        return (vm, nav, session);
    }

    private static string WriteRcp()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "cat.rcp");
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "Bg", LayerKind.Dynamic, null, new[] { new Variant("day", "Day", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["day"] = new(8, 8) },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "bg" }, System.Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        RecipeArchive.Write(path, recipe.Manifest, recipe.Ingredients);
        ing.Dispose();
        return path;
    }

    [AvaloniaFact]
    public async Task Import_rcp_opens_a_read_only_explorer()
    {
        var path = WriteRcp();
        var (vm, nav, session) = Landing(new StubPicker(path));
        try
        {
            await vm.ImportCommand.ExecuteAsync(null);
            var explorer = Assert.IsType<ExplorerViewModel>(nav.Current);
            Assert.NotNull(session.Current);
            Assert.Null(session.SourcePath);                 // no .cbk source → read-only
            Assert.Equal("cat", explorer.Root.Children[0].Id);   // the loose recipe is in the tree
            explorer.ToggleLockCommand.Execute(null);            // edit mode on
            explorer.SelectNodeCommand.Execute(explorer.Root.Children[0]);
            Assert.False(explorer.DeleteSelectedCommand.CanExecute(null));   // no source → still disabled
            explorer.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }
}
```
  (Confirm `RecipeArchive.Write(string, RecipeManifest, IReadOnlyList<LoadedIngredient>)` — read `src/Nfty.Core/Formats/RecipeArchive.cs` and adjust the write call to the real signature; `IFilePickerService` names must match.)

- [ ] **Step 2: Run — fail** (`.rcp` still the stub).

- [ ] **Step 3: Implement** in `LandingViewModel.cs`. Change the `.rcp`/other arm of `Import` (currently `_notify.Report("Importing a loose recipe needs the Kitchen (coming soon)")`) to dispatch to a new `OpenLooseRecipe`:
```csharp
        if (kind == ArchiveKind.CookBook) { OpenPath(path); return; }
        if (kind == ArchiveKind.Ingredient) { OpenLooseIngredient(path); return; }
        if (kind == ArchiveKind.Recipe) { OpenLooseRecipe(path); return; }
        _notify.Report("This file type can't be imported.");   // guard (unreachable for the three known kinds)
```
  and add:
```csharp
    private void OpenLooseRecipe(string path)
    {
        LoadedRecipe recipe;
        try { recipe = RecipeArchive.Read(path); }
        catch (Exception ex) { ShowError("Could not open", ex.Message); return; }
        var book = LooseWorkspace.WrapRecipe(recipe);
        _session.Open(book, null);            // no source .cbk → the Explorer is read-only; session owns `book`
        _nav.To(_explorerFactory(book));
    }
```
  (`RecipeArchive`/`LooseWorkspace` are already reachable — `Nfty.Core.Formats` + `Nfty.App.Services` are imported by Landing.)

- [ ] **Step 4: Run — pass;** whole App suite green; `dotnet build src/Nfty.Desktop --nologo` 0 warnings.

- [ ] **Step 5: Commit** `feat(gui): Landing imports a loose .rcp into a read-only Explorer`

---

### Task 3: Verification + manual smoke

**Files:** none.

- [ ] **Step 1:** `dotnet build nfty.sln --nologo` → 0 warnings. `dotnet test nfty.sln --nologo` → all pass (report Cli/App/Core totals).
- [ ] **Step 2:** `git diff --name-only <base>..HEAD -- src/Nfty.Core/` → empty (no Core change). No view edits → no hex scan.
- [ ] **Step 3: Manual smoke (user):** File → Import → pick a `.rcp` → it opens in the Explorer with the recipe selected, its layers/rules/hero visible; toggling edit mode leaves add/delete disabled (no source); a `.cbk` still opens normally and a `.igt` opens the editor. Note that importing a `.rcp` replaces the currently-open cookbook (open-a-document semantics).
- [ ] **Step 4:** Commit any smoke fixups: `test(gui): verify loose .rcp open end-to-end`.

---

## Self-Review
- **Spec coverage:** §2.1 `WrapRecipe` (canvas + fallback + weights) → T1. §2.2 Landing `.rcp` Import → T2. §2.3 (no view) → n/a. §4 error handling (unreadable `.rcp` → dialog, session untouched) → T2. §5 tests → T1 (wrap) + T2 (import + read-only) + manual. §6 risks: replace-open-cookbook (manual smoke note), canvas-from-variants + empty fallback (T1 covers both), read-only emergent (T2 asserts delete disabled).
- **Placeholder scan:** full code in every step; the "confirm RecipeArchive.Write / IFilePickerService signatures" notes point at real files, not TBDs.
- **Type consistency:** `WrapRecipe(LoadedRecipe)→LoadedCookBook` (T1) consumed by `OpenLooseRecipe` (T2); `RecipeArchive.Read(string)→LoadedRecipe` + `RecipeArchive.Write` match `Nfty.Core.Formats`; `session.Open(book, null)`/`_explorerFactory`/`_nav.To` match existing Landing members; `CookBookManifest(Id,Name,Dimensions,Collection,RecipeWeights)` + `Collection(Name,Description,Symbol)` match `Nfty.Core.Model`; the `.rcp` test builds the Landing with the B1 loose-editor factory (`ExplorerViewModelTests.LooseEditorFactory`).
