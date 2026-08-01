# nfty GUI — Explorer delete ingredient/recipe (A2a) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Delete the selected ingredient or recipe from the open cookbook in the Explorer and persist the change to the source `.cbk`.

**Architecture:** Core gains `CookBookEdits.RemoveIngredient`/`RemoveRecipe` (immutable splice, reuse surviving images). A shared `CookBookPersistence.PersistAsync` does the atomic write-back + rehash + `session.Replace` — the editor Save is refactored onto it, and the Explorer's new async `DeleteSelected` uses it too. The Explorer gets the session injected and generalizes its post-save refresh into `ApplyBook`.

**Tech Stack:** .NET 10, Avalonia 11.2.3, CommunityToolkit.Mvvm (`[RelayCommand]`/`AsyncRelayCommand`/`[NotifyCanExecuteChangedFor]`), `Nfty.Core.Editing`/`Formats`, xUnit + Avalonia.Headless.XUnit.

## Global Constraints
- **No behavior change to editor Save** — it is refactored onto `PersistAsync`; its existing round-trip/failure tests must stay green.
- **Shared-image disposal:** Remove methods reuse every surviving image; only the removed subtree is orphaned and disposed **once** by the Explorer after the swap. `PersistAsync` `Replace`s (never `Open`/dispose).
- **Hash:** in-app `Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()` (mirrors `ArchiveIo.HashFile`).
- **Crash-safe write:** temp sibling `SourcePath + ".tmp"` → `File.Move(overwrite: true)`; delete temp in `finally` on failure.
- **Delete gating:** `IsEditing && session.SourcePath != null && SelectedNode is Recipe/Ingredient`; the cookbook root is never deletable. Confirm via the Slice-2 `ConfirmDialog`.
- Determinism/idiom: `StringComparer.Ordinal` where ids sort; token brushes only in Views; `[AvaloniaFact]` for Avalonia-constructing tests. Build 0 warnings. Conventional commits. Agents: caveman-ultra terse chat; code/commits/reports normal prose.

## File Structure
- `src/Nfty.Core/Editing/CookBookEdits.cs` — add `RemoveIngredient`/`RemoveRecipe` (T1).
- `src/Nfty.App/Services/CookBookPersistence.cs` — new shared write-back helper (T2).
- `src/Nfty.App/ViewModels/IngredientEditorViewModel.cs` — refactor `Save` onto `PersistAsync` (T2).
- `src/Nfty.App/ViewModels/ExplorerViewModel.cs` — inject session; async `DeleteSelected`; `ApplyBook` (T3).
- `src/Nfty.App/ServiceRegistration.cs` — Explorer factory passes the session (T3).
- Tests: `tests/Nfty.Core.Tests/CookBookEditsTests.cs` (T1); `tests/Nfty.App.Tests/CookBookPersistenceTests.cs` (T2); `tests/Nfty.App.Tests/ExplorerDeleteTests.cs` (T3); updates to `ExplorerViewModelTests.cs`/`SmokeTests.cs`/`VisualCapture.cs` for the widened Explorer ctor (T3).

---

### Task 1: Core — `RemoveIngredient` / `RemoveRecipe`

**Files:** Modify `src/Nfty.Core/Editing/CookBookEdits.cs`; Test `tests/Nfty.Core.Tests/CookBookEditsTests.cs` (create or append).

**Interfaces:**
- Produces: `LoadedCookBook CookBookEdits.RemoveIngredient(LoadedCookBook, string recipeId, string ingredientId)`; `LoadedCookBook CookBookEdits.RemoveRecipe(LoadedCookBook, string recipeId)`.

- [ ] **Step 1: Read** `CookBookEdits.cs` (the `UpsertIngredient` pattern) and `Model/CookBookManifest.cs` (fields: `Name`, `Canvas`, `Collection`, `RecipeWeights` — confirm exact ctor/`with` shape) so the new methods match the record shapes.

- [ ] **Step 2: Failing tests** — `tests/Nfty.Core.Tests/CookBookEditsTests.cs`:
```csharp
using System.Linq;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.Core.Tests;

public class CookBookEditsTests
{
    private static LoadedIngredient Ing(string id, params string[] variantIds) => new()
    {
        Manifest = new IngredientManifest(id, id, LayerKind.Custom, null,
            variantIds.Select(v => new Variant(v, v, 1)).ToArray()),
        VariantImages = variantIds.ToDictionary(v => v, _ => new Image<Rgba32>(2, 2)),
    };

    private static LoadedCookBook Book()
    {
        var cat = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "bg", "aura" }, System.Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { Ing("bg", "day"), Ing("aura", "glow") },
        };
        var dog = new LoadedRecipe
        {
            Manifest = new RecipeManifest("dog", "Dog", new[] { "bg" }, System.Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { Ing("bg", "day") },
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(2, 2),
                new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 60, ["dog"] = 40 }),
            Recipes = new[] { cat, dog },
        };
    }

    [Fact]
    public void RemoveIngredient_drops_it_from_ingredients_and_layer_order_keeping_others()
    {
        var b = CookBookEdits.RemoveIngredient(Book(), "cat", "aura");
        var cat = b.Recipes.Single(r => r.Manifest.Id == "cat");
        Assert.DoesNotContain(cat.Ingredients, i => i.Manifest.Id == "aura");
        Assert.DoesNotContain("aura", cat.Manifest.LayerOrder);
        Assert.Contains(cat.Ingredients, i => i.Manifest.Id == "bg");   // sibling kept
        Assert.Equal(2, b.Recipes.Count);                               // other recipe untouched
    }

    [Fact]
    public void RemoveRecipe_drops_the_recipe_and_its_weight()
    {
        var b = CookBookEdits.RemoveRecipe(Book(), "dog");
        Assert.DoesNotContain(b.Recipes, r => r.Manifest.Id == "dog");
        Assert.False(b.Manifest.RecipeWeights.ContainsKey("dog"));
        Assert.Single(b.Recipes);
    }

    [Fact]
    public void Remove_rejects_absent_ids()
    {
        Assert.Throws<KeyNotFoundException>(() => CookBookEdits.RemoveIngredient(Book(), "cat", "nope"));
        Assert.Throws<KeyNotFoundException>(() => CookBookEdits.RemoveIngredient(Book(), "nope", "bg"));
        Assert.Throws<KeyNotFoundException>(() => CookBookEdits.RemoveRecipe(Book(), "nope"));
    }
}
```

- [ ] **Step 3: Run — fail** (methods missing).

- [ ] **Step 4: Implement** in `CookBookEdits.cs` (match the real `CookBookManifest`/`RecipeManifest` `with` shapes confirmed in Step 1):
```csharp
    public static LoadedCookBook RemoveIngredient(LoadedCookBook book, string recipeId, string ingredientId)
    {
        var recipe = book.Recipes.FirstOrDefault(r => r.Manifest.Id == recipeId)
            ?? throw new KeyNotFoundException($"No recipe '{recipeId}' in cookbook '{book.Manifest.Id}'.");
        if (recipe.Ingredients.All(i => i.Manifest.Id != ingredientId))
            throw new KeyNotFoundException($"No ingredient '{ingredientId}' in recipe '{recipeId}'.");

        var recipes = book.Recipes.Select(r =>
        {
            if (r.Manifest.Id != recipeId) return r;
            var ings = r.Ingredients.Where(i => i.Manifest.Id != ingredientId).ToList();
            var order = r.Manifest.LayerOrder.Where(id => id != ingredientId).ToList();
            return new LoadedRecipe { Manifest = r.Manifest with { LayerOrder = order }, Ingredients = ings };
        }).ToList();

        return new LoadedCookBook { Manifest = book.Manifest, Recipes = recipes, SourceSha256 = book.SourceSha256 };
    }

    public static LoadedCookBook RemoveRecipe(LoadedCookBook book, string recipeId)
    {
        if (book.Recipes.All(r => r.Manifest.Id != recipeId))
            throw new KeyNotFoundException($"No recipe '{recipeId}' in cookbook '{book.Manifest.Id}'.");

        var recipes = book.Recipes.Where(r => r.Manifest.Id != recipeId).ToList();
        var weights = book.Manifest.RecipeWeights.Where(kv => kv.Key != recipeId)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        return new LoadedCookBook
        {
            Manifest = book.Manifest with { RecipeWeights = weights },
            Recipes = recipes,
            SourceSha256 = book.SourceSha256,
        };
    }
```

- [ ] **Step 5: Run — pass;** `dotnet test tests/Nfty.Core.Tests --nologo` green; `dotnet build src/Nfty.Core --nologo` 0 warnings.

- [ ] **Step 6: Commit** `feat(editing): CookBookEdits.RemoveIngredient/RemoveRecipe`

---

### Task 2: Shared `CookBookPersistence.PersistAsync` + refactor editor Save

**Files:** Create `src/Nfty.App/Services/CookBookPersistence.cs`; Modify `src/Nfty.App/ViewModels/IngredientEditorViewModel.cs`; Test `tests/Nfty.App.Tests/CookBookPersistenceTests.cs` (create).

**Interfaces:**
- Produces: `static Task<LoadedCookBook> CookBookPersistence.PersistAsync(ICookBookSession session, LoadedCookBook book2, CancellationToken ct = default)`.

- [ ] **Step 1: Failing test** — `tests/Nfty.App.Tests/CookBookPersistenceTests.cs`:
```csharp
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Xunit;

namespace Nfty.App.Tests;

public class CookBookPersistenceTests
{
    [AvaloniaFact]
    public async Task PersistAsync_writes_the_spliced_book_and_replaces_the_session()
    {
        (var path, var session, var recipe, var ing) = IngredientEditorSaveTests.OnDisk();
        try
        {
            var book2 = CookBookEdits.RemoveRecipe(session.Current!, "cat");   // any real mutation
            var book3 = await CookBookPersistence.PersistAsync(session, book2);
            Assert.Same(book3, session.Current);                 // session replaced
            Assert.False(File.Exists(path + ".tmp"));            // temp cleaned
            using var reread = CookBookArchive.Read(path);
            Assert.DoesNotContain(reread.Recipes, r => r.Manifest.Id == "cat");
            Assert.Equal(reread.SourceSha256, book3.SourceSha256); // hash matches the written file
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [AvaloniaFact]
    public async Task PersistAsync_throws_without_a_source_path()
    {
        using var session = new CookBookSession();
        (var path, var s2, _, _) = IngredientEditorSaveTests.OnDisk();
        try
        {
            session.Open(s2.Current!, null);   // no source path
            await Assert.ThrowsAsync<System.InvalidOperationException>(
                () => CookBookPersistence.PersistAsync(session, s2.Current!));
        }
        finally { s2.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }
}
```
  (The `cat` recipe/`aura` ingredient come from the Slice-2 `OnDisk` fixture — verify those ids against `IngredientEditorSaveTests.OnDisk` and adjust if different.)

- [ ] **Step 2: Run — fail** (`CookBookPersistence` missing).

- [ ] **Step 3: Implement** `src/Nfty.App/Services/CookBookPersistence.cs`:
```csharp
using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Nfty.Core.Formats;

namespace Nfty.App.Services;

/// <summary>Persists an already-spliced cookbook graph back to the session's source archive:
/// crash-safe temp-then-atomic-replace, recompute the archive hash, and swap the session's Current
/// via its non-disposing Replace. Disposes nothing and shows no UI — the caller owns error handling
/// and the lifetime of whatever its mutation orphaned.</summary>
public static class CookBookPersistence
{
    public static async Task<LoadedCookBook> PersistAsync(ICookBookSession session, LoadedCookBook book2,
        CancellationToken ct = default)
    {
        if (session.SourcePath is not string dest)
            throw new InvalidOperationException("The cookbook has no source file to save to.");
        var tmp = dest + ".tmp";
        try
        {
            await CookBookArchive.WriteAsync(tmp, book2.Manifest, book2.Recipes, ct);
            File.Move(tmp, dest, overwrite: true);
            string sha;
            using (var s = File.OpenRead(dest)) sha = Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
            var book3 = new LoadedCookBook { Manifest = book2.Manifest, Recipes = book2.Recipes, SourceSha256 = sha };
            session.Replace(book3);
            return book3;
        }
        catch
        {
            if (File.Exists(tmp)) { try { File.Delete(tmp); } catch { /* best effort */ } }
            throw;
        }
    }
}
```

- [ ] **Step 4: Refactor the editor Save** onto it. In `IngredientEditorViewModel.Save`, replace the write/move/rehash/Replace block (steps 3–5 inline) so it becomes:
```csharp
            var (manifest, images) = IngredientDraftExporter.Export(_draft);
            var newIng = new LoadedIngredient { Manifest = manifest, VariantImages = images };
            var book2 = CookBookEdits.UpsertIngredient(_session.Current!, _recipe.Manifest.Id, newIng);

            var replaced = _ing;
            var book3 = await CookBookPersistence.PersistAsync(_session, book2);
            _ing = newIng;
            foreach (var img in replaced.VariantImages.Values) img.Dispose();

            IsDirty = false;
            Saved?.Invoke(book3);
```
  Keep the outer `if (SourcePath/Custom) return; IsSaving=true; try { … } catch { errordialog } finally { IsSaving=false; }`. The temp-cleanup now lives in `PersistAsync`; the editor's `catch` still shows the error dialog. Remove the now-unused `using System.Security.Cryptography;` / `File`/`SHA256` bits from the editor **only if** nothing else uses them (leave the usings if other members do).

- [ ] **Step 5: Run — pass;** the editor Save round-trip/failure tests (`IngredientEditorSaveTests`) + persistence tests + whole App suite green; `dotnet build src/Nfty.Desktop --nologo` 0 warnings.

- [ ] **Step 6: Commit** `refactor(gui): extract CookBookPersistence; editor Save uses it`

---

### Task 3: Explorer — async delete + session injection

**Files:** Modify `src/Nfty.App/ViewModels/ExplorerViewModel.cs`, `src/Nfty.App/ServiceRegistration.cs`; update ctor call sites in `tests/Nfty.App.Tests/ExplorerViewModelTests.cs`, `SmokeTests.cs`, `VisualCapture.cs`; Test `tests/Nfty.App.Tests/ExplorerDeleteTests.cs` (create).

**Interfaces:**
- Consumes: `ICookBookSession`, `CookBookEdits.RemoveIngredient`/`RemoveRecipe` (T1), `CookBookPersistence.PersistAsync` (T2), `ConfirmDialogViewModel`.
- Produces: async `DeleteSelectedCommand`; `ApplyBook(LoadedCookBook, string?)`.

- [ ] **Step 1: Failing tests** — `tests/Nfty.App.Tests/ExplorerDeleteTests.cs`. Build a dynamic on-disk book, an Explorer over `session.Current` with a confirming dialog and `IsEditing` on:
```csharp
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Models;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Xunit;

namespace Nfty.App.Tests;

public class ExplorerDeleteTests
{
    private static ExplorerViewModel Explorer(out CookBookSession session, out string path, IDialogService dialogs)
    {
        (path, session, _, _) = IngredientEditorSaveTests.OnDisk();
        var nav = new FakeNav();
        return new ExplorerViewModel(session.Current!, nav, dialogs, new FakeNotYetWired(), new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
            ExplorerViewModelTests.CookFactory(dialogs), session);
    }

    [AvaloniaFact]
    public async Task Delete_ingredient_persists_and_reselects_the_recipe()
    {
        var vm = Explorer(out var session, out var path, new ConfirmingDialogs(true));
        try
        {
            vm.ToggleLockCommand.Execute(null);                       // IsEditing = true
            var recipeNode = vm.Root.Children[0];
            var ingNode = recipeNode.Children[0];
            vm.SelectNodeCommand.Execute(ingNode);
            Assert.True(vm.DeleteSelectedCommand.CanExecute(null));
            await vm.DeleteSelectedCommand.ExecuteAsync(null);

            using var reread = CookBookArchive.Read(path);
            Assert.DoesNotContain(reread.Recipes[0].Ingredients, i => i.Manifest.Id == ingNode.Id);
            Assert.Equal(recipeNode.Id, vm.SelectedNode!.Id);        // parent recipe reselected
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Delete_recipe_persists_and_selects_the_root()
    {
        var vm = Explorer(out var session, out var path, new ConfirmingDialogs(true));
        try
        {
            vm.ToggleLockCommand.Execute(null);
            var recipeNode = vm.Root.Children[0];
            vm.SelectNodeCommand.Execute(recipeNode);
            await vm.DeleteSelectedCommand.ExecuteAsync(null);
            using var reread = CookBookArchive.Read(path);
            Assert.DoesNotContain(reread.Recipes, r => r.Manifest.Id == recipeNode.Id);
            Assert.Equal(vm.Root.Id, vm.SelectedNode!.Id);
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [AvaloniaFact]
    public void CanDelete_requires_editing_a_source_file_and_a_non_root_node()
    {
        var vm = Explorer(out var session, out var path, new FakeDialogs());
        try
        {
            vm.SelectNodeCommand.Execute(vm.Root.Children[0]);       // recipe, but not editing
            Assert.False(vm.DeleteSelectedCommand.CanExecute(null)); // lock on → disabled
            vm.ToggleLockCommand.Execute(null);
            Assert.True(vm.DeleteSelectedCommand.CanExecute(null));  // editing + recipe + source
            vm.SelectNodeCommand.Execute(vm.Root);                   // cookbook root
            Assert.False(vm.DeleteSelectedCommand.CanExecute(null)); // root not deletable
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    private sealed class ConfirmingDialogs : IDialogService
    {
        private readonly bool _v;
        public ConfirmingDialogs(bool v) => _v = v;
        public ViewModelBase? Active => null;
        public event System.Action? Changed { add { } remove { } }
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase d) => Task.FromResult((TResult?)(object?)_v);
        public void Close(object? result) { }
    }
}
```
  (`ExplorerViewModelTests.EditorFactory` already takes `(nav, session?, dialogs?)`. If `SelectNodeCommand`/`ToggleLockCommand`/`Root.Children` names differ, adjust to the real members.)

- [ ] **Step 2: Run — fail** (ctor arity; `DeleteSelected` still the sync stub).

- [ ] **Step 3: Inject the session** into `ExplorerViewModel`:
  - Ctor: add a trailing `ICookBookSession session` param; store `private readonly ICookBookSession _session;`.
  - `ServiceRegistration.cs` Explorer factory: pass `sp.GetRequiredService<ICookBookSession>()` as the last arg.
  - Update the test/capture construction sites (`ExplorerViewModelTests` helpers that `new ExplorerViewModel(...)`, `SmokeTests`, `VisualCapture`) to pass a session (a real `CookBookSession`, or the fixture's). For call sites that don't delete, a fresh `new CookBookSession()` is fine.

- [ ] **Step 4: Implement delete + `ApplyBook`.** In `ExplorerViewModel.cs`:
  - Generalize the refresh (replace `OnEditorSaved`'s body + `FindIngredientNode`):
    ```csharp
    internal void OnEditorSaved(LoadedCookBook book) => ApplyBook(book, SelectedNode?.Id);

    private void ApplyBook(LoadedCookBook book, string? selectId)
    {
        _book = book;
        Root = BuildTree(book);
        SelectedNode = FindNode(Root, selectId) ?? Root;
    }

    private static ExplorerNode? FindNode(ExplorerNode root, string? id)
    {
        if (id is null) return null;
        if (root.Id == id) return root;
        foreach (var r in root.Children)
        {
            if (r.Id == id) return r;
            var hit = r.Children.FirstOrDefault(n => n.Id == id);
            if (hit is not null) return hit;
        }
        return null;
    }
    ```
  - Replace the delete stub:
    ```csharp
    private bool CanDeleteSelected() =>
        IsEditing && _session.SourcePath is not null
        && SelectedNode?.Kind is ExplorerNodeKind.Recipe or ExplorerNodeKind.Ingredient;

    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private async Task DeleteSelected()
    {
        if (SelectedNode is not { } node) return;
        var ok = await _dialogs.ShowAsync<bool>(new ConfirmDialogViewModel(_dialogs,
            "Delete?", $"Delete “{node.Name}” — this can’t be undone.", "Delete"));
        if (!ok) return;
        try
        {
            LoadedCookBook book2;
            string? parentId;
            IDisposable removed;
            if (node.Domain is (LoadedRecipe r, LoadedIngredient i))
            {
                book2 = CookBookEdits.RemoveIngredient(_book, r.Manifest.Id, i.Manifest.Id);
                parentId = r.Manifest.Id; removed = i;
            }
            else if (node.Domain is LoadedRecipe rr)
            {
                book2 = CookBookEdits.RemoveRecipe(_book, rr.Manifest.Id);
                parentId = Root.Id; removed = rr;
            }
            else return;   // cookbook root — not deletable (also gated by CanExecute)

            var book3 = await CookBookPersistence.PersistAsync(_session, book2);
            removed.Dispose();                 // free the orphaned subtree's images (recipe cascades)
            ApplyBook(book3, parentId);
        }
        catch (Exception ex)
        {
            await _dialogs.ShowAsync<object>(new ErrorDialogViewModel(_dialogs, "Could not delete", ex.Message));
        }
    }
    ```
  - Re-notify `DeleteSelectedCommand.NotifyCanExecuteChanged()` in `OnSelectedNodeChanged` (selection affects `CanDeleteSelected`). `IsEditing` already has `[NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]`.
  - Add `using System;` if needed. Confirm the ingredient `Domain` tuple shape `(LoadedRecipe, LoadedIngredient)` matches `BuildTree` (it does — Step 0 verify).

- [ ] **Step 5: Run — pass;** whole App suite green; build 0 warnings.

- [ ] **Step 6: Commit** `feat(gui): delete ingredient/recipe from the Explorer, persisted to the .cbk`

---

### Task 4: Verification + manual smoke

**Files:** none (no new UI).

- [ ] **Step 1:** `dotnet build nfty.sln --nologo` → 0 warnings. `dotnet test nfty.sln --nologo` → all pass (report Cli/App/Core totals).
- [ ] **Step 2:** `git diff --name-only <base>..HEAD -- src/Nfty.Core/` → only `CookBookEdits.cs` changed in Core. `grep -rniE "#[0-9a-fA-F]{6}" src/Nfty.App/Views/` unchanged (no view edits this slice).
- [ ] **Step 3: Manual smoke (user):** open a `.cbk`; toggle the edit lock; select an ingredient → **Delete** (confirm) → it disappears and the on-disk file no longer contains it; select a recipe → **Delete**; reopen the `.cbk` to confirm persistence; verify Delete is disabled on the cookbook root and when the lock is off.
- [ ] **Step 4:** Commit any smoke fixups: `test(gui): verify Explorer delete end-to-end`.

---

## Self-Review
- **Spec coverage:** §2.1 Core Remove* → T1. §2.2 shared `PersistAsync` + editor refactor → T2. §2.3 Explorer session + async delete + `ApplyBook` → T3. §2.4 (no new view) → n/a. §4 error handling (temp cleanup in `PersistAsync`, error dialog, unchanged-on-failure) → T2/T3. §5 tests → T1/T2/T3 + editor regression (T2 Step 5) + manual (T4). §6 risks: disposal (remove-then-dispose-once, `Replace` not `Open`) in T2/T3; refactor guarded by editor Save tests (T2 Step 5); empty states allowed (no guard).
- **Placeholder scan:** full code in every step; the two "verify the record/tuple shape" notes point at real files to confirm, not TBDs.
- **Type consistency:** `RemoveIngredient(book,recipeId,ingredientId)`/`RemoveRecipe(book,recipeId)` (T1) consumed in T3; `PersistAsync(session, book2, ct)` (T2) consumed by editor Save (T2) + Explorer delete (T3); `ApplyBook(book, selectId)`/`FindNode`/`CanDeleteSelected`/`DeleteSelectedCommand` consistent within T3; Explorer ctor gains a trailing `ICookBookSession` used identically in ServiceRegistration + all test/capture sites. `ConfirmDialogViewModel(dialogs,title,message,confirmLabel)` + `ErrorDialogViewModel(dialogs,title,message)` match existing signatures. `CookBookManifest with { RecipeWeights = … }` / `RecipeManifest with { LayerOrder = … }` — confirm the exact property names in T1 Step 1 before relying on them.
