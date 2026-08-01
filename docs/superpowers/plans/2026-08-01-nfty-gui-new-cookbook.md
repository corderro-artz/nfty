# nfty GUI — Create a new CookBook (C0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Landing's New CookBook wizard writes an empty `.cbk` and opens it in the Explorer, so a user can start a project in the GUI.

**Architecture:** The wizard's `Create` closes with the VM (gated on a non-blank derived id). `CookBookPersistence.WriteNew` writes the archive overwrite-safely (temp + atomic move). Landing builds the manifest from the wizard's bound fields, writes, and reuses the existing `OpenPath` (read → `session.Open(book, path)` → Explorer). No `Nfty.Core` change.

**Tech Stack:** .NET 10, Avalonia 11.2.3, CommunityToolkit.Mvvm, `Nfty.Core.Formats.CookBookArchive`, xUnit + Avalonia.Headless.XUnit.

## Global Constraints
- **No `Nfty.Core` change.** No view change (the wizard view already binds every field).
- **Overwrite-safe write:** `CookBookArchive.Write` opens `ZipArchiveMode.Create` (`FileMode.CreateNew`) and throws on an existing path — a Save picker can legitimately return one, so write to `path + ".tmp"` then `File.Move(tmp, path, overwrite: true)`; delete the temp in a `finally`.
- **Blank-id guard (the F1 lesson):** gate the wizard's `Create` on `!string.IsNullOrWhiteSpace(DerivedId)` AND re-check in Landing before writing.
- **Empty cookbook is the intended starting state** — do NOT validate at create time (`Validator` would reject zero recipes); the cook path reports it later, exactly as A2c's empty recipe.
- Determinism/idiom: no RNG; `[AvaloniaFact]` for Avalonia-constructing tests. Build 0 warnings. Conventional commits. Agents: caveman-ultra terse chat; code/commits/reports normal prose. Context7 for any uncertain library API (this slice is domain C#, likely unneeded).

## File Structure
- `src/Nfty.App/ViewModels/NewCookBookViewModel.cs` — gated `Create` → `Close(this)` (T1).
- `src/Nfty.App/Services/CookBookPersistence.cs` — add `WriteNew` (T2).
- `src/Nfty.App/ViewModels/LandingViewModel.cs` — async create flow (T3).
- Tests: `tests/Nfty.App.Tests/NewCookBookViewModelTests.cs` (append, T1); `tests/Nfty.App.Tests/CookBookPersistenceTests.cs` (append, T2); `tests/Nfty.App.Tests/LandingNewCookBookTests.cs` (create, T3).

---

### Task 1: Wizard closes with its result

**Files:** Modify `src/Nfty.App/ViewModels/NewCookBookViewModel.cs`; Test `tests/Nfty.App.Tests/NewCookBookViewModelTests.cs` (append; if an existing test asserts the old `Notify.Report("Create CookBook")` stub, replace it — the contract legitimately changed, same as the other two wizards).

**Interfaces:**
- Produces: `Create` closes the dialog with the VM, `CanExecute` = non-blank `DerivedId`.

- [ ] **Step 1: Failing tests** — append to `NewCookBookViewModelTests.cs`:
```csharp
    [Fact]
    public void Create_is_disabled_until_the_name_yields_a_non_blank_id()
    {
        var vm = Make(out _, out _);   // use the file's existing factory helper; adapt the name if it differs
        Assert.False(vm.CreateCommand.CanExecute(null));
        vm.Name = "   ";
        Assert.False(vm.CreateCommand.CanExecute(null));
        vm.Name = "Vapor Pets";
        Assert.True(vm.CreateCommand.CanExecute(null));
    }

    [Fact]
    public async System.Threading.Tasks.Task Create_closes_the_dialog_with_the_vm()
    {
        var real = new DialogService();
        var vm = new NewCookBookViewModel(real, new FakeNotYetWired()) { Name = "Vapor Pets" };
        var task = real.ShowAsync<NewCookBookViewModel>(vm);
        vm.CreateCommand.Execute(null);
        Assert.Same(vm, await task);
    }
```
  (Add `using Nfty.App.Services;` if `DialogService` isn't resolved.)

- [ ] **Step 2: Run — fail** (`Create` still reports + closes with null). `dotnet test tests/Nfty.App.Tests --filter "FullyQualifiedName~NewCookBookViewModelTests" --nologo`.

- [ ] **Step 3: Implement** in `NewCookBookViewModel.cs` — replace `[RelayCommand] private void Create() { Notify.Report("Create CookBook"); Dialogs.Close(null); }`:
```csharp
    private bool CanCreate() => !string.IsNullOrWhiteSpace(DerivedId);

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private void Create() => Dialogs.Close(this);
```
  and extend the existing name hook so the button enables as the user types:
```csharp
    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(DerivedId));
        CreateCommand.NotifyCanExecuteChanged();
    }
```

- [ ] **Step 4: Run — pass;** whole App suite green; `dotnet build src/Nfty.Desktop --nologo` 0 warnings.

- [ ] **Step 5: Commit** `feat(gui): New CookBook wizard closes with its result`

---

### Task 2: Overwrite-safe `CookBookPersistence.WriteNew`

**Files:** Modify `src/Nfty.App/Services/CookBookPersistence.cs`; Test `tests/Nfty.App.Tests/CookBookPersistenceTests.cs` (append).

**Interfaces:**
- Produces: `static void CookBookPersistence.WriteNew(string path, CookBookManifest manifest, IReadOnlyList<LoadedRecipe> recipes)`.

- [ ] **Step 1: Failing tests** — append:
```csharp
    private static CookBookManifest EmptyManifest() => new("vp", "VaporPets",
        new Dimensions(64, 64), new Collection("VaporPets", "desc", "VP"),
        new Dictionary<string, double>());

    [AvaloniaFact]
    public void WriteNew_writes_a_readable_empty_cookbook()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "book.cbk");
        try
        {
            CookBookPersistence.WriteNew(path, EmptyManifest(), System.Array.Empty<LoadedRecipe>());
            Assert.False(File.Exists(path + ".tmp"));
            using var reread = CookBookArchive.Read(path);
            Assert.Equal("vp", reread.Manifest.Id);
            Assert.Equal("VaporPets", reread.Manifest.Name);
            Assert.Equal(64, reread.Manifest.Canvas.Width);
            Assert.Equal("VP", reread.Manifest.Collection.Symbol);
            Assert.Empty(reread.Recipes);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public void WriteNew_replaces_an_existing_file()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "book.cbk");
        try
        {
            File.WriteAllText(path, "stale");
            CookBookPersistence.WriteNew(path, EmptyManifest(), System.Array.Empty<LoadedRecipe>());
            using var reread = CookBookArchive.Read(path);   // replaced, not rejected
            Assert.Equal("vp", reread.Manifest.Id);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
```
  (Add `using Nfty.Core.Model;` if the manifest/`Dimensions`/`Collection` types aren't resolved in that file.)

- [ ] **Step 2: Run — fail** (`WriteNew` missing).

- [ ] **Step 3: Implement** in `CookBookPersistence.cs` (beside `PersistAsync`):
```csharp
    /// <summary>Writes a cookbook to a user-chosen path, replacing an existing file: sibling temp plus an
    /// atomic move (CookBookArchive.Write opens CreateNew and would throw on an existing path). Used when
    /// creating a new .cbk.</summary>
    public static void WriteNew(string path, CookBookManifest manifest, IReadOnlyList<LoadedRecipe> recipes)
    {
        var tmp = path + ".tmp";
        try
        {
            CookBookArchive.Write(tmp, manifest, recipes);
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp)) { try { File.Delete(tmp); } catch { /* best effort */ } }
        }
    }
```
  (Add `using Nfty.Core.Model;` / `using System.Collections.Generic;` if needed.)

- [ ] **Step 4: Run — pass;** whole App suite green; build 0 warnings.

- [ ] **Step 5: Commit** `feat(gui): CookBookPersistence.WriteNew writes a new .cbk overwrite-safely`

---

### Task 3: Landing New CookBook create flow

**Files:** Modify `src/Nfty.App/ViewModels/LandingViewModel.cs`; Test `tests/Nfty.App.Tests/LandingNewCookBookTests.cs` (create).

**Interfaces:**
- Consumes: `NewCookBookViewModel` fields (T1), `CookBookPersistence.WriteNew` (T2), `_picker.SaveFileAsync`, the existing private `OpenPath`.

- [ ] **Step 1: Failing test** — `tests/Nfty.App.Tests/LandingNewCookBookTests.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Xunit;

namespace Nfty.App.Tests;

public class LandingNewCookBookTests
{
    private sealed class SavePicker : IFilePickerService
    {
        private readonly string? _save;
        public SavePicker(string? save) => _save = save;
        public Task<string?> OpenFileAsync(string title, params string[] extensions) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string title, string defaultExtension) => Task.FromResult(_save);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    // Fills the New-CookBook wizard and "clicks Create"; records any error dialog.
    private sealed class WizardDialogs : IDialogService
    {
        private readonly string _name;
        public string? ErrorTitle { get; private set; }
        public WizardDialogs(string name) => _name = name;
        public ViewModelBase? Active => null;
        public event Action? Changed { add { } remove { } }
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog)
        {
            if (dialog is NewCookBookViewModel w)
            { w.Name = _name; w.Symbol = "VP"; w.Width = 64; w.Height = 64; w.Description = "d";
              return Task.FromResult((TResult?)(object?)w); }
            if (dialog is ErrorDialogViewModel e) { ErrorTitle = e.Title; return Task.FromResult(default(TResult)); }
            return Task.FromResult(default(TResult));
        }
        public void Close(object? result) { }
    }

    private static (LandingViewModel vm, FakeNav nav, CookBookSession session) Landing(
        IDialogService dialogs, IFilePickerService picker)
    {
        var nav = new FakeNav(); var notify = new FakeNotYetWired(); var session = new CookBookSession();
        var vm = new LandingViewModel(nav, dialogs, notify, picker, new RecentsService(), session,
            book => new ExplorerViewModel(book, nav, dialogs, notify, new ImageBridge(),
                ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
                ExplorerViewModelTests.CookFactory(dialogs), session,
                picker, ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs)),
            set => new SetBrowserViewModel(set),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs));
        return (vm, nav, session);
    }

    [AvaloniaFact]
    public async Task New_cookbook_writes_a_cbk_and_opens_the_explorer()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "vapor.cbk");
        var (vm, nav, session) = Landing(new WizardDialogs("Vapor Pets"), new SavePicker(path));
        try
        {
            await vm.NewCookBookCommand.ExecuteAsync(null);
            Assert.True(File.Exists(path));
            using (var reread = CookBookArchive.Read(path))
            {
                Assert.Equal("vapor-pets", reread.Manifest.Id);
                Assert.Equal(64, reread.Manifest.Canvas.Width);
                Assert.Empty(reread.Recipes);                     // empty starting book
            }
            Assert.IsType<ExplorerViewModel>(nav.Current);        // opened in the Explorer
            Assert.NotNull(session.Current);
            Assert.Equal(path, session.SourcePath);               // source set → Add/Save/Cook enabled
        }
        finally { session.Dispose(); Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Cancelling_the_save_picker_writes_nothing()
    {
        var (vm, nav, session) = Landing(new WizardDialogs("Vapor Pets"), new SavePicker(null));
        try
        {
            await vm.NewCookBookCommand.ExecuteAsync(null);
            Assert.Null(nav.Current);
            Assert.Null(session.Current);
        }
        finally { session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task A_blank_name_errors_and_writes_nothing()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "vapor.cbk");
        var dialogs = new WizardDialogs("   ");
        var (vm, nav, session) = Landing(dialogs, new SavePicker(path));
        try
        {
            await vm.NewCookBookCommand.ExecuteAsync(null);
            Assert.NotNull(dialogs.ErrorTitle);
            Assert.False(File.Exists(path));
            Assert.Null(nav.Current);
        }
        finally { session.Dispose(); Directory.Delete(dir, recursive: true); }
    }
}
```
  (Confirm `NewCookBookViewModel`'s property names — `Name`/`Symbol`/`Width`/`Height`/`Description` — and the Explorer/Landing ctor shapes, which gained params in B3b; adapt the `Landing` helper to the real signatures.)

- [ ] **Step 2: Run — fail** (`NewCookBook` is the sync stub).

- [ ] **Step 3: Implement** in `LandingViewModel.cs` — replace `[RelayCommand] private void NewCookBook() => _dialogs.ShowAsync<object>(new NewCookBookViewModel(_dialogs, _notify));`:
```csharp
    [RelayCommand]
    private async Task NewCookBook()
    {
        var wizard = new NewCookBookViewModel(_dialogs, _notify);
        var result = await _dialogs.ShowAsync<NewCookBookViewModel>(wizard);
        if (result is null) return;   // cancelled
        if (string.IsNullOrWhiteSpace(result.DerivedId))
        {
            ShowError("Invalid cookbook", "The cookbook needs a name.");
            return;
        }
        string? path;
        try { path = await _picker.SaveFileAsync("Save new cookbook", ".cbk"); }
        catch (Exception ex) { ShowError("Could not save", ex.Message); return; }
        if (path is null) return;   // cancelled the picker

        var manifest = new CookBookManifest(result.DerivedId, result.Name,
            new Dimensions(result.Width, result.Height),
            new Collection(result.Name, result.Description, result.Symbol),
            new Dictionary<string, double>());   // no recipes yet
        try { CookBookPersistence.WriteNew(path, manifest, Array.Empty<LoadedRecipe>()); }
        catch (Exception ex) { ShowError("Could not save", ex.Message); return; }

        OpenPath(path);   // reads it back (fresh hash), session.Open(book, path), → Explorer
    }
```
  (Add `using Nfty.Core.Model;` / `using System.Collections.Generic;` to Landing if not already present.)

- [ ] **Step 4: Run — pass;** whole App suite green; `dotnet build src/Nfty.Desktop --nologo` 0 warnings.

- [ ] **Step 5: Commit** `feat(gui): Landing creates a new cookbook and opens it`

---

### Task 4: Verification + manual smoke

**Files:** none.

- [ ] **Step 1:** `dotnet build nfty.sln --nologo` → 0 warnings. `dotnet test nfty.sln --nologo` → all pass (report Cli/App/Core totals).
- [ ] **Step 2:** `git diff --name-only <base>..HEAD -- src/Nfty.Core/` → empty (no Core change). No view edits → no hex scan.
- [ ] **Step 3: Manual smoke (user):** Landing → **New CookBook** → name/symbol/canvas/description → Create → a Save dialog appears → choose a path → the Explorer opens on the new (empty) cookbook → toggle edit → **Add recipe** → **Add ingredient** → paint → **Save** → **Cook**. Re-creating over the same path replaces it cleanly; cancelling the Save dialog aborts.
- [ ] **Step 4:** Commit any smoke fixups: `test(gui): verify new-cookbook create end-to-end`.

---

## Self-Review
- **Spec coverage:** §2.1 wizard gated `Create`→`Close(this)` → T1. §2.2 `WriteNew` (temp+move) → T2. §2.3 Landing async create + `OpenPath` reuse → T3. §2.4 (no view) → n/a. §4 error handling (cancel/blank/write-fail) → T3. §5 tests → T1 (wizard) + T2 (write + replace) + T3 (create/cancel/blank + SourcePath set) + manual. §6 risks: empty cookbook renders (T3 asserts the Explorer opens over zero recipes), overwrite (T2 asserts replace), session replacement (smoke note).
- **Placeholder scan:** full code in every step; the "confirm the wizard property names / ctor shapes" notes point at real files (the Landing+Explorer ctors gained params in B3b), not TBDs.
- **Type consistency:** `CookBookManifest(Id,Name,Dimensions,Collection,RecipeWeights)` + `Collection(Name,Description,Symbol)` + `Dimensions(int,int)` match `Nfty.Core.Model`; `CookBookArchive.Write(string, CookBookManifest, IReadOnlyList<LoadedRecipe>)` matches source; `WriteNew` mirrors it; `OpenPath(string)` is Landing's existing private method; `NewCookBookCommand` name unchanged (now `AsyncRelayCommand`); the test Landing helper matches the post-B3b Landing/Explorer ctor arity.
