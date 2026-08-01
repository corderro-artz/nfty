# nfty GUI — Open & edit a loose `.igt` (B1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Open a standalone `.igt` in the Ingredient Editor and save edits straight back to the `.igt`.

**Architecture:** A loose ingredient is wrapped in a synthetic single-recipe cookbook (canvas from its variant size) so the editor's canvas/structure needs are met. The editor gains an optional `looseSavePath`: when set, `Save` writes `IngredientArchive` to that path (temp+atomic) instead of the cookbook, `CanSave` drops the `.cbk`-source requirement, and the editor owns (disposes) the synthetic book. Landing's Import wires the `.igt` case through a new loose-editor factory. No `Nfty.Core` change.

**Tech Stack:** .NET 10, Avalonia 11.2.3, CommunityToolkit.Mvvm, `Nfty.Core.Formats.IngredientArchive`, xUnit + Avalonia.Headless.XUnit.

## Global Constraints
- **Optional ctor param:** the editor's `looseSavePath` is appended last and defaulted `null`, so every existing cookbook construction site is unchanged.
- **Ownership:** on the loose path the editor owns the synthetic book (`_ownedBook`) and disposes it once in `Dispose`; on the cookbook path `_ownedBook` is null (the session owns the book). Never `session.Open`/`Replace` for a loose file.
- **Custom stays unsavable:** loose `CanSave` still excludes `LayerKind.Custom`.
- **Crash-safe write:** loose Save writes `path + ".tmp"` then `File.Move(overwrite: true)`; temp deleted on failure.
- Determinism/idiom: `StringComparer.Ordinal` where ids sort; token brushes only in Views (no view change here); `[AvaloniaFact]` for Avalonia-constructing tests. Build 0 warnings. Conventional commits. Agents: caveman-ultra terse chat; code/commits/reports normal prose.

## File Structure
- `src/Nfty.App/Services/LooseWorkspace.cs` — new synthetic-cookbook wrapper (T1).
- `src/Nfty.App/ViewModels/IngredientEditorViewModel.cs` — optional `looseSavePath`, loose Save/CanSave, `_ownedBook` (T2).
- `src/Nfty.App/ViewModels/LandingViewModel.cs` + `src/Nfty.App/ServiceRegistration.cs` — `.igt` Import + loose-editor factory (T3).
- Tests: `tests/Nfty.App.Tests/LooseWorkspaceTests.cs` (T1); `tests/Nfty.App.Tests/LooseIngredientEditorTests.cs` (T2); `tests/Nfty.App.Tests/LandingImportIgtTests.cs` (T3); updates to Landing construction sites (T3).

---

### Task 1: Synthetic-cookbook wrapper

**Files:** Create `src/Nfty.App/Services/LooseWorkspace.cs`; Test `tests/Nfty.App.Tests/LooseWorkspaceTests.cs`.

**Interfaces:**
- Produces: `static LoadedCookBook LooseWorkspace.WrapIngredient(LoadedIngredient ing)`.

- [ ] **Step 1: Failing test** — `tests/Nfty.App.Tests/LooseWorkspaceTests.cs`:
```csharp
using System.Collections.Generic;
using System.Linq;
using Nfty.App.Services;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public class LooseWorkspaceTests
{
    [Fact]
    public void WrapIngredient_builds_a_one_recipe_book_sized_to_the_variants()
    {
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("aura", "Aura", LayerKind.Dynamic, null,
                new[] { new Variant("glow", "Glow", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["glow"] = new(6, 9) },
        };
        using var book = LooseWorkspace.WrapIngredient(ing);
        Assert.Equal(6, book.Manifest.Canvas.Width);
        Assert.Equal(9, book.Manifest.Canvas.Height);
        var recipe = Assert.Single(book.Recipes);
        Assert.Equal(new[] { "aura" }, recipe.Manifest.LayerOrder);
        Assert.Same(ing, recipe.Ingredients.Single());     // wraps the same ingredient
    }
}
```

- [ ] **Step 2: Run — fail** (`LooseWorkspace` missing).

- [ ] **Step 3: Implement** `src/Nfty.App/Services/LooseWorkspace.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Nfty.Core.Formats;
using Nfty.Core.Model;

namespace Nfty.App.Services;

/// <summary>Wraps a standalone (loose) ingredient in a throwaway single-recipe cookbook so the
/// Ingredient Editor — which needs a canvas + recipe context — can open it. The wrapper is a view/edit
/// scaffold only; it is never persisted as a cookbook (loose Save writes the .igt directly). The
/// returned book owns the ingredient, so disposing the book disposes the ingredient's images.</summary>
public static class LooseWorkspace
{
    public static LoadedCookBook WrapIngredient(LoadedIngredient ing)
    {
        var img = ing.VariantImages.Values.FirstOrDefault()
            ?? throw new InvalidOperationException("A loose ingredient needs at least one variant to edit.");
        var canvas = new Dimensions(img.Width, img.Height);
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("loose", ing.Manifest.Name,
                new[] { ing.Manifest.Id }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("loose", ing.Manifest.Name, canvas,
                new Collection(ing.Manifest.Name, "", "L"),
                new Dictionary<string, double> { ["loose"] = 100 }),
            Recipes = new[] { recipe },
        };
    }
}
```

- [ ] **Step 4: Run — pass;** `dotnet test tests/Nfty.App.Tests --nologo` green; `dotnet build src/Nfty.Desktop --nologo` 0 warnings.

- [ ] **Step 5: Commit** `feat(gui): LooseWorkspace wraps a loose ingredient for the editor`

---

### Task 2: Editor loose-save path

**Files:** Modify `src/Nfty.App/ViewModels/IngredientEditorViewModel.cs`; Test `tests/Nfty.App.Tests/LooseIngredientEditorTests.cs`.

**Interfaces:**
- Produces: optional ctor param `string? looseSavePath = null`; loose `Save` (writes `IngredientArchive`) + loose `CanSave` + `_ownedBook` disposal.

- [ ] **Step 1: Failing tests** — `tests/Nfty.App.Tests/LooseIngredientEditorTests.cs`:
```csharp
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public class LooseIngredientEditorTests
{
    // Write a small dynamic .igt to a temp dir; return (path, freshly-read ingredient).
    private static (string path, LoadedIngredient ing) OnDiskIgt(LayerKind kind = LayerKind.Dynamic)
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "aura.igt");
        var coloriz = kind == LayerKind.Custom ? null
            : new Colorization(ColorModel.Hsv, 12, 4, new[] { new ColorEntry(1, new ColorRange(0, 360, 40, 100), null) });
        var manifest = new IngredientManifest("aura", "Aura", kind, coloriz, new[] { new Variant("glow", "Glow", 1) });
        var images = new Dictionary<string, Image<Rgba32>> { ["glow"] = new(8, 8) };
        IngredientArchive.Write(path, manifest, images);
        foreach (var i in images.Values) i.Dispose();
        return (path, IngredientArchive.Read(path));
    }

    private static IngredientEditorViewModel LooseEditor(LoadedIngredient ing, string path)
    {
        var book = LooseWorkspace.WrapIngredient(ing);
        return new IngredientEditorViewModel(ing, book.Recipes[0], book, new ImageBridge(),
            new FakeNav(), new FakeNotYetWired(), new CookBookSession(), new FakeDialogs(), looseSavePath: path);
    }

    [AvaloniaFact]
    public async Task Loose_save_writes_the_painted_value_back_to_the_igt()
    {
        var (path, ing) = OnDiskIgt();
        try
        {
            var vm = LooseEditor(ing, path);
            vm.ActiveTool = EditorTool.Fill; vm.BrushValue = 200;
            vm.ApplyToolStroke(new[] { (0, 0) });
            Assert.True(vm.CanSave);
            await vm.SaveCommand.ExecuteAsync(null);
            Assert.False(vm.IsDirty);
            Assert.False(File.Exists(path + ".tmp"));
            using var reread = IngredientArchive.Read(path);
            Assert.Equal(200, ValueMap.FromImage(reread.VariantImages["glow"]).GetValue(4, 4));
            vm.Dispose();   // disposes the owned synthetic book (→ ing)
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [AvaloniaFact]
    public void Loose_CanSave_needs_dirty_and_not_custom()
    {
        var (path, ing) = OnDiskIgt();
        try
        {
            var vm = LooseEditor(ing, path);
            Assert.False(vm.CanSave);                       // clean
            vm.ActiveTool = EditorTool.Fill; vm.BrushValue = 30;
            vm.ApplyToolStroke(new[] { (0, 0) });
            Assert.True(vm.CanSave);                        // dirty dynamic loose → enabled
            vm.Dispose();
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [AvaloniaFact]
    public void Loose_custom_cannot_save()
    {
        var (path, ing) = OnDiskIgt(LayerKind.Custom);
        try
        {
            var vm = LooseEditor(ing, path);
            vm.ApplyToolStroke(new[] { (0, 0) });           // dirty
            Assert.False(vm.CanSave);                       // custom blocked even when dirty
            vm.Dispose();
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }
}
```

- [ ] **Step 2: Run — fail** (`looseSavePath` param missing).

- [ ] **Step 3: Implement** in `IngredientEditorViewModel.cs`:
  - Add `using System.Collections.Generic;` if absent (for the exporter dictionary) — likely already present.
  - Add fields: `private readonly string? _looseSavePath; private readonly LoadedCookBook? _ownedBook;`.
  - Ctor: append `, string? looseSavePath = null` to the parameter list (after `dialogs`). In the body:
    `_looseSavePath = looseSavePath; if (looseSavePath is not null) _ownedBook = book;` (the editor owns the
    synthetic book on the loose path).
  - `CanSave` → branch on loose:
    ```csharp
    public bool CanSave => IsDirty && !IsSaving && _ing.Manifest.Kind != LayerKind.Custom
        && (_looseSavePath is not null || _session.SourcePath is not null);
    ```
  - `Save` → branch at the top of the `try` (keep the outer `if (…) return; IsSaving = true; try { … }
    catch { errordialog } finally { IsSaving = false; }` shell, but the guard must allow the loose path):
    - Change the early return to: `if (_ing.Manifest.Kind == LayerKind.Custom) return; if (_looseSavePath is
      null && _session.SourcePath is null) return;`
    - Inside the try, first branch:
    ```csharp
    if (_looseSavePath is string loosePath)
    {
        var (manifest, images) = IngredientDraftExporter.Export(_draft);
        var tmp = loosePath + ".tmp";
        try
        {
            await IngredientArchive.WriteAsync(tmp, manifest, images);
            File.Move(tmp, loosePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp)) { try { File.Delete(tmp); } catch { /* best effort */ } }
            foreach (var i in images.Values) i.Dispose();   // exporter copies — ours to free
        }
        IsDirty = false;
        return;   // loose has no book/session/Explorer to refresh
    }
    ```
    Then the existing cookbook body follows unchanged.
    (`IngredientArchive` is in `Nfty.Core.Formats` — already imported.)
  - `Dispose` → also dispose the owned book: append `_ownedBook?.Dispose();` (disposes the synthetic book →
    the ingredient's images). Order: after the existing thumbnail/canvas/preview disposals.

- [ ] **Step 4: Run — pass;** the cookbook Save tests (`IngredientEditorSaveTests`) + whole App suite green;
  build 0 warnings.

- [ ] **Step 5: Commit** `feat(gui): editor saves a loose ingredient straight back to its .igt`

---

### Task 3: Landing Import of a `.igt`

**Files:** Modify `src/Nfty.App/ViewModels/LandingViewModel.cs`, `src/Nfty.App/ServiceRegistration.cs`; update Landing construction sites in `tests/Nfty.App.Tests/` (LandingViewModelTests, LandingOpenFlowTests, LandingOpenSetTests, SmokeTests, VisualCapture, WiringCoverageTests) + add a `LooseEditorFactory` helper; Test `tests/Nfty.App.Tests/LandingImportIgtTests.cs`.

**Interfaces:**
- Consumes: `IngredientArchive.Read`, `LooseWorkspace.WrapIngredient`, the editor's `looseSavePath`.
- Produces: `Func<LoadedIngredient, LoadedCookBook, string, IngredientEditorViewModel>` injected into Landing; the `.igt` Import branch.

- [ ] **Step 1: Failing test** — `tests/Nfty.App.Tests/LandingImportIgtTests.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Nfty.Core.Output;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public class LandingImportIgtTests
{
    private sealed class StubPicker : IFilePickerService
    {
        private readonly string? _path;
        public StubPicker(string? path) => _path = path;
        public Task<string?> OpenFileAsync(string title, params string[] ext) => Task.FromResult(_path);
        public Task<string?> SaveFileAsync(string title, string suggestedName, params string[] ext) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private static LandingViewModel Landing(FakeNav nav, IFilePickerService picker, IDialogService dialogs)
    {
        var session = new CookBookSession(); var notify = new FakeNotYetWired();
        return new LandingViewModel(nav, dialogs, notify, picker, new RecentsService(), session,
            book => new ExplorerViewModel(book, nav, dialogs, notify, new ImageBridge(),
                ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
                ExplorerViewModelTests.CookFactory(dialogs), session),
            set => new SetBrowserViewModel(set),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs));
    }

    private static string WriteIgt(int variants)
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "aura.igt");
        var vs = Enumerable.Range(0, variants).Select(i => new Variant($"v{i}", $"V{i}", 1)).ToArray();
        var manifest = new IngredientManifest("aura", "Aura", LayerKind.Dynamic,
            new Colorization(ColorModel.Hsv, 12, 4, new[] { new ColorEntry(1, new ColorRange(0, 360, 40, 100), null) }),
            vs.Length == 0 ? new[] { new Variant("v0", "V0", 1) } : vs);   // manifest needs >=1 variant to write
        var images = (vs.Length == 0 ? new[] { "v0" } : vs.Select(v => v.Id)).ToDictionary(id => id, _ => (Image<Rgba32>)new Image<Rgba32>(8, 8));
        IngredientArchive.Write(path, manifest, images);
        foreach (var i in images.Values) i.Dispose();
        return path;
    }

    [AvaloniaFact]
    public async Task Import_igt_opens_the_editor()
    {
        var path = WriteIgt(1);
        var nav = new FakeNav();
        try
        {
            var vm = Landing(nav, new StubPicker(path), new FakeDialogs());
            await vm.ImportCommand.ExecuteAsync(null);
            Assert.IsType<IngredientEditorViewModel>(nav.Current);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }
}
```
  (`IFilePickerService`'s real method names/signatures must match — read `src/Nfty.App/Services/IFilePickerService.cs` and adjust the `StubPicker` to implement it exactly. `RecentsService` may need a parameterless ctor — if not, use the existing test double.)

- [ ] **Step 2: Run — fail** (Landing ctor lacks the loose factory; `.igt` still the stub).

- [ ] **Step 3: Add the `LooseEditorFactory` test helper** in `ExplorerViewModelTests.cs` (beside `EditorFactory`):
```csharp
internal static Func<LoadedIngredient, LoadedCookBook, string, IngredientEditorViewModel> LooseEditorFactory(
    INavigationService nav, ICookBookSession session, IDialogService dialogs) =>
    (ing, book, path) => new IngredientEditorViewModel(ing, book.Recipes[0], book, new ImageBridge(),
        nav, new FakeNotYetWired(), session, dialogs, looseSavePath: path);
```

- [ ] **Step 4: Inject the factory + wire Import.** In `LandingViewModel.cs`:
  - Add field `private readonly Func<LoadedIngredient, LoadedCookBook, string, IngredientEditorViewModel> _looseEditorFactory;`
    and a trailing ctor param `Func<LoadedIngredient, LoadedCookBook, string, IngredientEditorViewModel> looseEditorFactory`;
    assign it. Add `using Nfty.Core.Editing;`? not needed; `LoadedIngredient`/`LoadedCookBook` are in
    `Nfty.Core.Formats` (already imported).
  - Rewrite the `.igt` branch of `Import` (currently `else _notify.Report("… coming soon")`):
    ```csharp
    if (kind == ArchiveKind.CookBook) { OpenPath(path); return; }
    if (kind == ArchiveKind.Ingredient) { OpenLooseIngredient(path); return; }
    _notify.Report("Importing a loose recipe needs the Kitchen (coming soon)");   // .rcp → B2
    ```
    and add:
    ```csharp
    private void OpenLooseIngredient(string path)
    {
        LoadedIngredient ing;
        try { ing = IngredientArchive.Read(path); }
        catch (Exception ex) { ShowError("Could not open", ex.Message); return; }
        if (ing.VariantImages.Count == 0)
        {
            ShowError("Can't open", "This ingredient has no variants to edit.");
            ing.Dispose(); return;
        }
        var book = LooseWorkspace.WrapIngredient(ing);   // the editor will own + dispose this
        _nav.To(_looseEditorFactory(ing, book, path));
    }
    ```
    (`ArchiveKind.Ingredient` — confirm the enum member name in `Archives.cs`.)
  - Update `ServiceRegistration.cs` `AddTransient<LandingViewModel>()`: it is DI-resolved, so register the
    loose factory as a service the container injects:
    ```csharp
    services.AddSingleton<Func<LoadedIngredient, LoadedCookBook, string, IngredientEditorViewModel>>(sp =>
        (ing, book, path) => new IngredientEditorViewModel(ing, book.Recipes[0], book,
            sp.GetRequiredService<IImageBridge>(), sp.GetRequiredService<INavigationService>(),
            sp.GetRequiredService<INotYetWired>(), sp.GetRequiredService<ICookBookSession>(),
            sp.GetRequiredService<IDialogService>(), looseSavePath: path));
    ```
    `AddTransient<LandingViewModel>()` then resolves the new ctor param automatically.

- [ ] **Step 5: Update the Landing construction sites** (append the loose factory arg). In each of
  `LandingViewModelTests.cs`, `LandingOpenFlowTests.cs`, `LandingOpenSetTests.cs`, `SmokeTests.cs`,
  `VisualCapture.cs`, `WiringCoverageTests.cs`, the `new LandingViewModel(…, set => new
  SetBrowserViewModel(set))` call gains a trailing `, ExplorerViewModelTests.LooseEditorFactory(nav,
  session, dialogs)` (use whatever local `nav`/`session`/`dialogs` names each site has; where a site lacks
  a session/dialogs local, pass `new CookBookSession()` / the local dialogs). Build to find them all
  (`dotnet build tests/Nfty.App.Tests`), fix each `CS7036`.

- [ ] **Step 6: Run — pass;** whole App suite green; build 0 warnings.

- [ ] **Step 7: Commit** `feat(gui): Landing imports a loose .igt into the editor`

---

### Task 4: Verification + manual smoke

**Files:** none.

- [ ] **Step 1:** `dotnet build nfty.sln --nologo` → 0 warnings. `dotnet test nfty.sln --nologo` → all pass (report Cli/App/Core totals).
- [ ] **Step 2:** `git diff --name-only <base>..HEAD -- src/Nfty.Core/` → empty (no Core change). No view edits → no hex scan.
- [ ] **Step 3: Manual smoke (user):** File → Import → pick a `.igt` → it opens in the editor → paint / add a variant / rename → Save → reopen the same `.igt` to confirm the edit persisted; a `.cbk` still opens the Explorer; a `.rcp` still shows the "coming soon" note; a zero-variant `.igt` shows an error.
- [ ] **Step 4:** Commit any smoke fixups: `test(gui): verify loose .igt open + save end-to-end`.

---

## Self-Review
- **Spec coverage:** §2.1 `LooseWorkspace.WrapIngredient` → T1. §2.2 editor `looseSavePath`/loose Save/CanSave/`_ownedBook` → T2. §2.3 Landing `.igt` Import + factory → T3. §2.4 (no view) → n/a. §4 error handling (unreadable/zero-variant/write-fail) → T3 (`OpenLooseIngredient`) + T2 (temp cleanup). §5 tests → T1/T2/T3 + regression (cookbook Save suite) + manual. §6 risks: ownership (`_ownedBook` disposed once, loose only), canvas-from-variants, custom unsavable, session never touched — all in T2/T3 code + comments.
- **Placeholder scan:** full code in every step; the "confirm IFilePickerService signatures / ArchiveKind member / RecentsService ctor" notes point at real files, not TBDs.
- **Type consistency:** `WrapIngredient(LoadedIngredient)→LoadedCookBook` (T1) used by T2/T3; editor ctor gains `string? looseSavePath = null` used identically in the DI factory + `LooseEditorFactory` helper + T2 tests; `IngredientArchive.WriteAsync(string, IngredientManifest, IReadOnlyDictionary<string,Image<Rgba32>>, ct)` matches source; `Func<LoadedIngredient, LoadedCookBook, string, IngredientEditorViewModel>` consistent across ServiceRegistration/Landing/tests; `CookBookManifest(Id,Name,Dimensions,Collection,RecipeWeights)` + `Collection(Name,Description,Symbol)` + `RecipeManifest(Id,Name,LayerOrder,Rules)` match `Nfty.Core.Model`.
