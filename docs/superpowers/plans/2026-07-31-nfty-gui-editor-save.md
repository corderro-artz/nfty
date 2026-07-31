# nfty GUI — Ingredient Editor Save / persist Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Persist the editor's painted `IngredientDraft` back to the source `.cbk` on disk (crash-safe), and reflect the edit live in the running Explorer.

**Architecture:** The session records the open cookbook's source path. Save exports the draft (`IngredientDraftExporter`), splices it into the open book (`CookBookEdits.UpsertIngredient`), writes the whole archive to a sibling temp then atomically replaces the original, recomputes the archive hash in-app, and swaps the in-memory graph via a non-disposing `session.Replace`. The editor raises a `Saved` event the Explorer uses to rebuild its tree in place. No `Nfty.Core` change.

**Tech Stack:** .NET 10, Avalonia 11.2.3, CommunityToolkit.Mvvm (`[ObservableProperty]`/`[RelayCommand]`/`AsyncRelayCommand`/`[NotifyCanExecuteChangedFor]`), `Nfty.Core.Editing` (`IngredientDraftExporter`, `CookBookEdits`), `Nfty.Core.Formats.CookBookArchive`, xUnit + Avalonia.Headless.XUnit.

## Global Constraints
- **No `Nfty.Core` change** — reuse the existing export/upsert/archive APIs. The archive hash is recomputed in `Nfty.App` mirroring `ArchiveIo.HashFile` **exactly**: `Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()` (lowercase hex).
- **Painted pixels only** — Save keeps the ingredient's original `Colorization`; the Colorize rail stays preview-only.
- **Custom layers blocked** — `CanSave` requires `kind != LayerKind.Custom` (saving a grayscale value-map over a full-colour PNG is data loss). Dynamic/static round-trip losslessly.
- **Crash-safe write** — `CookBookArchive.WriteAsync` opens `ZipArchiveMode.Create` (throws on an existing file), so write to `SourcePath + ".tmp"` (a **sibling**, same volume, for an atomic move) then `File.Move(tmp, SourcePath, overwrite: true)`; delete the temp in a `finally` on failure.
- **Determinism/idiom:** `StringComparer.Ordinal` where ids sort; token brushes only in Views (no raw hex outside `Tokens.axaml`); 8-digit hex is Avalonia `#AARRGGBB`. `[AvaloniaFact]` for Avalonia-constructing tests. Build 0 warnings. Conventional commits. Agents: caveman-ultra terse chat; code/commits/reports normal prose.
- **Shared-image disposal:** `UpsertIngredient` reuses every *unchanged* ingredient's images, so the session must `Replace` (never `Open`/dispose) and the editor disposes **only** the replaced ingredient's old images, exactly once (`_ing = newIng` after save so a second save can't double-dispose).

## File Structure
- `src/Nfty.App/Services/ICookBookSession.cs` — add `SourcePath`, `Open(book, path)`, `Replace(book)` (T1).
- `src/Nfty.App/ViewModels/LandingViewModel.cs` — pass the opened path to `Open` (T1).
- `src/Nfty.App/ViewModels/IngredientEditorViewModel.cs` — session injection, `IsDirty`/`IsSaving`/`CanSave`, async `Save`, `Saved` event, async `Back` discard (T2, T3).
- `src/Nfty.App/ServiceRegistration.cs` — capture `ICookBookSession` in the editor factory closure (T2).
- `src/Nfty.App/ViewModels/ConfirmDialogViewModel.cs` + `src/Nfty.App/Views/ConfirmDialogView.axaml`(+`.cs`) — reusable yes/no modal (T3).
- `src/Nfty.App/ViewModels/ExplorerViewModel.cs` — subscribe to `Saved`, rebuild tree + reselect (T4).
- Tests: `tests/Nfty.App.Tests/CookBookSessionTests.cs` (T1); `IngredientEditorSaveTests.cs` (new, T2/T3); `ExplorerViewModelTests.cs` (T4 refresh + factory/session updates); `SmokeTests.cs`/`VisualCapture.cs`/`IngredientEditorPaintTests.cs` (ctor-arg updates, T2).

---

### Task 1: Session tracks source path + non-disposing Replace

**Files:** Modify `src/Nfty.App/Services/ICookBookSession.cs`, `src/Nfty.App/ViewModels/LandingViewModel.cs`; Test `tests/Nfty.App.Tests/CookBookSessionTests.cs`.

**Interfaces:**
- Produces: `string? ICookBookSession.SourcePath`; `void Open(LoadedCookBook book, string? sourcePath = null)`; `void Replace(LoadedCookBook book)` (swaps `Current`, no dispose, raises `Changed`, keeps `SourcePath`).

- [ ] **Step 1: Failing tests** — append to `CookBookSessionTests.cs`:
```csharp
    [Fact]
    public void Open_records_the_source_path_and_Close_clears_it()
    {
        using var session = new CookBookSession();
        using var a = OneRecipeBook();           // existing helper in this test file
        session.Open(a, "C:/books/a.cbk");
        Assert.Equal("C:/books/a.cbk", session.SourcePath);
        session.Close();
        Assert.Null(session.SourcePath);
    }

    [Fact]
    public void Replace_swaps_current_without_disposing_the_previous_book()
    {
        using var session = new CookBookSession();
        var a = OneRecipeBook();
        var b = OneRecipeBook();
        session.Open(a, "C:/books/a.cbk");
        int changed = 0; session.Changed += () => changed++;
        session.Replace(b);
        Assert.Same(b, session.Current);
        Assert.Equal("C:/books/a.cbk", session.SourcePath);   // path preserved
        Assert.Equal(1, changed);
        // `a` was NOT disposed: its variant images are still usable.
        var img = a.Recipes[0].Ingredients[0].VariantImages.Values.First();
        Assert.True(img.Width > 0);                            // throws ObjectDisposedException if disposed
        a.Dispose(); b.Dispose();
    }
```
(If `CookBookSessionTests.cs` has no `OneRecipeBook()` helper, add a tiny one that builds a 1-recipe/1-ingredient/1-variant `LoadedCookBook` over a `new Image<Rgba32>(2, 2)` — mirror the fixture style already in that file.)

- [ ] **Step 2: Run — fails** (`SourcePath`/`Replace` missing). `dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~CookBookSession --nologo`.

- [ ] **Step 3: Implement** in `ICookBookSession.cs`:
  - Interface: add `string? SourcePath { get; }`, change `void Open(LoadedCookBook book);` → `void Open(LoadedCookBook book, string? sourcePath = null);`, add `void Replace(LoadedCookBook book);`.
  - Impl:
    ```csharp
    private string? _sourcePath;
    public string? SourcePath => _sourcePath;

    public void Open(LoadedCookBook book, string? sourcePath = null)
    {
        if (ReferenceEquals(_current, book)) { _sourcePath = sourcePath; return; }
        _current?.Dispose();
        _current = book;
        _sourcePath = sourcePath;
        Changed?.Invoke();
    }

    /// <summary>Swaps in a graph that shares the previous book's images (e.g. from
    /// CookBookEdits.UpsertIngredient) — so it must NOT dispose the previous book. The caller owns
    /// the lifetime of whatever images the new graph no longer references.</summary>
    public void Replace(LoadedCookBook book)
    {
        if (ReferenceEquals(_current, book)) return;
        _current = book;                 // deliberately no dispose
        Changed?.Invoke();
    }
    ```
    In `Close()` add `_sourcePath = null;`.

- [ ] **Step 4: Update the caller** — `LandingViewModel.OpenPath`: `_session.Open(book);` → `_session.Open(book, path);`.

- [ ] **Step 5: Run — passes;** `dotnet test tests/Nfty.App.Tests --nologo` whole suite green; `dotnet build src/Nfty.Desktop --nologo` 0 warnings.

- [ ] **Step 6: Commit** `feat(gui): session tracks the source .cbk path + non-disposing Replace`

---

### Task 2: Editor Save (async) — export, atomic write, splice

**Files:** Modify `src/Nfty.App/ViewModels/IngredientEditorViewModel.cs`, `src/Nfty.App/ServiceRegistration.cs`; ctor-arg updates in `tests/Nfty.App.Tests/ExplorerViewModelTests.cs` (EditorFactory helper), `SmokeTests.cs`, `VisualCapture.cs`, `IngredientEditorPaintTests.cs`; Test `tests/Nfty.App.Tests/IngredientEditorSaveTests.cs` (create).

**Interfaces:**
- Consumes: `ICookBookSession` (T1); `IngredientDraftExporter.Export(IngredientDraft) → (IngredientManifest, IReadOnlyDictionary<string,Image<Rgba32>>)`; `CookBookEdits.UpsertIngredient(LoadedCookBook, string recipeId, LoadedIngredient) → LoadedCookBook`; `CookBookArchive.WriteAsync(string, CookBookManifest, IReadOnlyList<LoadedRecipe>, CancellationToken)`.
- Produces: `bool IsDirty`, `bool IsSaving`, `bool CanSave`, `AsyncRelayCommand SaveCommand`, `event Action<LoadedCookBook>? Saved`.

- [ ] **Step 1: Failing test** — create `IngredientEditorSaveTests.cs`:
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

public class IngredientEditorSaveTests
{
    // Build a dynamic (value-map) 1-recipe cookbook on disk, return (path, session opened over it).
    private static (string path, CookBookSession session, LoadedRecipe recipe, LoadedIngredient ing) OnDisk()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "book.cbk");
        var coloriz = new Colorization(ColorModel.Hsv, 12, 4,
            new[] { new ColorEntry(1, new ColorRange(0, 360, 40, 100), null) });
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("aura", "Aura", LayerKind.Dynamic, coloriz,
                new[] { new Variant("glow", "Glow", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["glow"] = new(8, 8) },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "aura" }, System.Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        var manifest = new CookBookManifest("cb", "Book", new Dimensions(8, 8),
            new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 });
        CookBookArchive.Write(path, manifest, new[] { recipe });
        var book = CookBookArchive.Read(path);      // fresh graph with real images + hash
        var session = new CookBookSession();
        session.Open(book, path);
        var r = book.Recipes[0];
        return (path, session, r, r.Ingredients[0]);
    }

    [AvaloniaFact]
    public async Task Save_writes_the_painted_value_back_to_the_cbk()
    {
        var (path, session, recipe, ing) = OnDisk();
        try
        {
            var vm = new IngredientEditorViewModel(ing, recipe, session.Current!, new ImageBridge(),
                new FakeNav(), new FakeNotYetWired(), session);
            vm.ActiveTool = EditorTool.Fill; vm.BrushValue = 200;
            vm.ApplyToolStroke(new[] { (0, 0) });          // flood the blank value-map to 200
            Assert.True(vm.CanSave);
            await vm.SaveCommand.ExecuteAsync(null);
            Assert.False(vm.IsDirty);
            Assert.False(File.Exists(path + ".tmp"));      // temp cleaned up

            using var reread = CookBookArchive.Read(path);
            var rip = reread.Recipes[0].Ingredients.Single(i => i.Manifest.Id == "aura");
            Assert.Equal(200, ValueMap.FromImage(rip.VariantImages["glow"]).GetValue(4, 4));
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [AvaloniaFact]
    public void CanSave_is_gated_by_dirty_source_and_kind()
    {
        var (path, session, recipe, ing) = OnDisk();
        try
        {
            var vm = new IngredientEditorViewModel(ing, recipe, session.Current!, new ImageBridge(),
                new FakeNav(), new FakeNotYetWired(), session);
            Assert.False(vm.CanSave);                      // clean → disabled
            vm.ActiveTool = EditorTool.Fill; vm.BrushValue = 50;
            vm.ApplyToolStroke(new[] { (0, 0) });
            Assert.True(vm.CanSave);                        // dirty dynamic w/ source → enabled
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }
}
```
(Verify `Colorization`/`ColorEntry`/`ColorRange`/`ColorModel` constructor shapes against `src/Nfty.Core/Model/` before running — adapt the fixture to the real signatures; the paint-test fixture from Slice 1 used the same records, copy from there if they differ.)

- [ ] **Step 2: Run — fails** (7-arg ctor / `CanSave` / `SaveCommand` missing).

- [ ] **Step 3: Inject the session + add state.** In `IngredientEditorViewModel.cs`:
  - Add `using System;`, `using System.IO;`, `using System.Security.Cryptography;`, `using System.Threading.Tasks;`.
  - Add fields: `private readonly LoadedRecipe _recipe; private readonly ICookBookSession _session; private LoadedIngredient _ingRef;` — actually reuse the existing `_ing` field but make it non-`readonly` so Save can repoint it (`private LoadedIngredient _ing;`).
  - Ctor signature → `(LoadedIngredient ing, LoadedRecipe recipe, LoadedCookBook book, IImageBridge bridge, INavigationService nav, INotYetWired notify, ICookBookSession session)`; in the body assign `_recipe = recipe; _session = session;`.
  - Add observable state (place near the other `[ObservableProperty]`s):
    ```csharp
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isDirty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isSaving;

    public event Action<LoadedCookBook>? Saved;

    /// <summary>Save is offered only for edited dynamic/static ingredients with a known source file.
    /// Custom (full-colour) layers are blocked: the draft is a grayscale value-map and would overwrite
    /// their colour PNGs.</summary>
    public bool CanSave => IsDirty && _session.SourcePath is not null
        && _ing.Manifest.Kind != LayerKind.Custom && !IsSaving;
    ```
  - In `ApplyToolStroke`, after `hist.Do(cmd, map);` set `IsDirty = true;`.
    Note: `IsDirty`'s generated setter fires `NotifyCanExecuteChangedFor(SaveCommand)` only on *change*; because `CanSave` also depends on `SourcePath`/kind (constant per editor) that's fine. `SaveCommand`'s `CanExecute` reads the `CanSave` property below.

- [ ] **Step 4: Implement Save** — replace the `[RelayCommand] private void Save() => _notify.Report(...)` stub:
```csharp
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save()
    {
        if (_session.SourcePath is not string dest) return;   // guarded by CanSave; belt-and-suspenders
        IsSaving = true;
        var tmp = dest + ".tmp";
        try
        {
            // 1. Export the draft → a loaded ingredient (we own the images until Upsert adopts them).
            var (manifest, images) = IngredientDraftExporter.Export(_draft);
            var newIng = new LoadedIngredient { Manifest = manifest, VariantImages = images };

            // 2. Splice into the live book.
            var book2 = CookBookEdits.UpsertIngredient(_session.Current!, _recipe.Manifest.Id, newIng);

            // 3. Crash-safe write: sibling temp, then atomic replace.
            await CookBookArchive.WriteAsync(tmp, book2.Manifest, book2.Recipes);
            File.Move(tmp, dest, overwrite: true);

            // 4. Recompute the archive hash in-app (mirrors ArchiveIo.HashFile exactly).
            string sha;
            using (var s = File.OpenRead(dest)) sha = Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
            // LoadedCookBook is a class with init-only props (not a record) — construct explicitly.
            var book3 = new LoadedCookBook { Manifest = book2.Manifest, Recipes = book2.Recipes, SourceSha256 = sha };

            // 5. Swap the in-memory graph (no dispose — book3 shares the other ingredients' images),
            //    then free the replaced ingredient's now-orphaned images exactly once.
            var replaced = _ing;
            _session.Replace(book3);
            _ing = newIng;                                     // subsequent saves target the new ingredient
            foreach (var img in replaced.VariantImages.Values) img.Dispose();

            IsDirty = false;
            Saved?.Invoke(book3);
        }
        catch (Exception ex)
        {
            if (File.Exists(tmp)) { try { File.Delete(tmp); } catch { /* best effort */ } }
            await _dialogs.ShowAsync<object>(new ErrorDialogViewModel(_dialogs, "Could not save", ex.Message));
        }
        finally { IsSaving = false; }
    }
```
  - This needs an `IDialogService _dialogs`. The editor ctor does **not** currently take one — add `IDialogService dialogs` as an 8th ctor param, store `_dialogs`, and thread it through the factory + test constructions (below). (Alternatively reuse `_notify` for errors, but a real modal matches Landing's pattern — prefer the dialog.)

  > **Decision:** add `IDialogService` to the ctor. Final ctor arg order: `(ing, recipe, book, bridge, nav, notify, session, dialogs)`.

- [ ] **Step 5: Update the DI factory** — `ServiceRegistration.cs`, keep the Func arity at 3 by capturing the singletons:
```csharp
services.AddSingleton<Func<LoadedIngredient, LoadedRecipe, LoadedCookBook, IngredientEditorViewModel>>(sp =>
    (ing, recipe, book) => new IngredientEditorViewModel(ing, recipe, book,
        sp.GetRequiredService<IImageBridge>(),
        sp.GetRequiredService<INavigationService>(),
        sp.GetRequiredService<INotYetWired>(),
        sp.GetRequiredService<ICookBookSession>(),
        sp.GetRequiredService<IDialogService>()));
```

- [ ] **Step 6: Update the test construction sites** to the 8-arg ctor:
  - `ExplorerViewModelTests.EditorFactory` — change to build a session + dialogs:
    ```csharp
    internal static Func<LoadedIngredient, LoadedRecipe, LoadedCookBook, IngredientEditorViewModel> EditorFactory(
        INavigationService nav, ICookBookSession? session = null, IDialogService? dialogs = null)
    {
        var s = session ?? new CookBookSession();
        var d = dialogs ?? new FakeDialogs();
        return (i, r, b) => new IngredientEditorViewModel(i, r, b, new ImageBridge(), nav, new FakeNotYetWired(), s, d);
    }
    ```
    Existing `EditorFactory(nav)` callers keep compiling (optional params).
  - `SmokeTests.cs` — `editorFactory(cat.Ingredients[0], cat, smokeBook)` still works via the helper; no change beyond the helper.
  - `VisualCapture.cs` `Capture_editor_paint` and `IngredientEditorPaintTests.Editor()` — add the two args: `new IngredientEditorViewModel(ing, cat, book, new ImageBridge(), new FakeNav(), new FakeNotYetWired(), new CookBookSession(), new FakeDialogs())`. (For the paint tests the session/dialogs are unused; a fresh `CookBookSession` + `FakeDialogs` is fine.)
  - Confirm `FakeDialogs` implements `IDialogService` (it's used elsewhere in these tests); if it doesn't return from `ShowAsync`, that's fine — Save's success path never shows a dialog.

- [ ] **Step 7: Run — passes;** `dotnet test tests/Nfty.App.Tests --nologo` whole suite green; `dotnet build src/Nfty.Desktop --nologo` 0 warnings.

- [ ] **Step 8: Commit** `feat(gui): editor Save persists the draft to the source .cbk`

---

### Task 3: Back discards with confirmation + reusable confirm dialog

**Files:** Create `src/Nfty.App/ViewModels/ConfirmDialogViewModel.cs`, `src/Nfty.App/Views/ConfirmDialogView.axaml`(+`.cs`); Modify `src/Nfty.App/ViewModels/IngredientEditorViewModel.cs`; Test `tests/Nfty.App.Tests/IngredientEditorSaveTests.cs`.

**Interfaces:**
- Produces: `ConfirmDialogViewModel(IDialogService, string title, string message, string confirmLabel)` closing with a `bool` (`ShowAsync<bool>`); editor `Back` becomes `AsyncRelayCommand` that confirms when `IsDirty`.

- [ ] **Step 1: Failing test** — append:
```csharp
    [AvaloniaFact]
    public async Task Back_when_dirty_confirms_before_navigating()
    {
        var (path, session, recipe, ing) = OnDisk();
        try
        {
            var nav = new FakeNav();
            var dialogs = new FakeConfirmingDialogs(confirm: false);   // user cancels the discard
            var vm = new IngredientEditorViewModel(ing, recipe, session.Current!, new ImageBridge(),
                nav, new FakeNotYetWired(), session, dialogs);
            vm.ActiveTool = EditorTool.Fill; vm.BrushValue = 10;
            vm.ApplyToolStroke(new[] { (0, 0) });
            await vm.BackCommand.ExecuteAsync(null);
            Assert.True(dialogs.Shown);            // a confirm was shown
            Assert.Equal(0, nav.BackCount);        // cancelled → did not navigate
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }
```
  Add a tiny test double in the test file (or reuse if `FakeNav`/`FakeDialogs` already expose these):
```csharp
    private sealed class FakeConfirmingDialogs : IDialogService
    {
        private readonly bool _confirm;
        public bool Shown { get; private set; }
        public FakeConfirmingDialogs(bool confirm) => _confirm = confirm;
        public ViewModelBase? Active => null;
        public event Action? Changed { add { } remove { } }
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog)
        { Shown = true; return Task.FromResult((TResult?)(object?)_confirm); }
        public void Close(object? result) { }
    }
```
  (If `FakeNav` lacks a `BackCount`, add one — increment in its `Back()`.)

- [ ] **Step 2: Run — fails** (`BackCommand` is sync / no confirm).

- [ ] **Step 3: Build the confirm dialog.** `ConfirmDialogViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

/// <summary>Reusable yes/no modal. Closes with a bool: true = confirmed, false = cancelled.</summary>
public partial class ConfirmDialogViewModel : ViewModelBase
{
    private readonly IDialogService _dialogs;
    public string Title { get; }
    public string Message { get; }
    public string ConfirmLabel { get; }

    public ConfirmDialogViewModel(IDialogService dialogs, string title, string message, string confirmLabel)
    { _dialogs = dialogs; Title = title; Message = message; ConfirmLabel = confirmLabel; }

    [RelayCommand] private void Confirm() => _dialogs.Close(true);
    [RelayCommand] private void Cancel() => _dialogs.Close(false);
}
```
  `ConfirmDialogView.axaml` — mirror `ErrorDialogView.axaml` (tokens only, no raw hex), two buttons:
```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Nfty.App.ViewModels"
             x:Class="Nfty.App.Views.ConfirmDialogView"
             x:DataType="vm:ConfirmDialogViewModel">
  <UserControl.KeyBindings>
    <KeyBinding Gesture="Escape" Command="{Binding CancelCommand}" />
  </UserControl.KeyBindings>
  <Border Background="{DynamicResource PanelBrush}" CornerRadius="10" Padding="24" MaxWidth="420"
          BorderBrush="{DynamicResource LineStrongBrush}" BorderThickness="1">
    <StackPanel Spacing="10">
      <TextBlock Text="{Binding Title}" FontWeight="Bold" Foreground="{DynamicResource AccentTextBrush}" />
      <TextBlock Text="{Binding Message}" TextWrapping="Wrap" Classes="muted" />
      <StackPanel Orientation="Horizontal" Spacing="8" HorizontalAlignment="Right">
        <Button Content="Cancel" Command="{Binding CancelCommand}" Classes="ghost" />
        <Button Content="{Binding ConfirmLabel}" Command="{Binding ConfirmCommand}" Classes="accent" />
      </StackPanel>
    </StackPanel>
  </Border>
</UserControl>
```
  `ConfirmDialogView.axaml.cs` — the minimal `InitializeComponent` code-behind (mirror `ErrorDialogView.axaml.cs`). The `ViewLocator` resolves it by convention. Add it to `SmokeTests`' VM list so the view-resolution smoke covers it: `new ConfirmDialogViewModel(dialogs, "Discard?", "You have unsaved edits.", "Discard")`.

- [ ] **Step 4: Make Back async + confirm** — in `IngredientEditorViewModel.cs` replace `[RelayCommand] private void Back() => _nav.Back();`:
```csharp
    [RelayCommand]
    private async Task Back()
    {
        if (IsDirty)
        {
            var ok = await _dialogs.ShowAsync<bool>(
                new ConfirmDialogViewModel(_dialogs, "Discard edits?",
                    "You have unsaved changes to this ingredient.", "Discard"));
            if (!ok) return;
        }
        _nav.Back();
    }
```

- [ ] **Step 5: Run — passes;** whole App suite green; build 0 warnings; `grep -rniE "#[0-9a-fA-F]{6}" src/Nfty.App/Views/ConfirmDialogView.axaml` → nothing.

- [ ] **Step 6: Commit** `feat(gui): confirm before discarding unsaved editor edits`

---

### Task 4: Explorer live refresh on Save

**Files:** Modify `src/Nfty.App/ViewModels/ExplorerViewModel.cs`; Test `tests/Nfty.App.Tests/ExplorerViewModelTests.cs`.

**Interfaces:** Consumes editor `event Action<LoadedCookBook>? Saved` (T2). Explorer rebuilds its tree from the new book and reselects the edited ingredient by id.

- [ ] **Step 1: Failing test** — append to `ExplorerViewModelTests.cs`. Drive a real editor Save through a shared session so Explorer refreshes:
```csharp
    [AvaloniaFact]
    public void Editor_save_rebuilds_the_tree_and_reselects_the_ingredient()
    {
        // Dynamic on-disk book so Save is enabled (TwoRecipeBook is Custom → blocked).
        var (path, session, recipe, ing) = IngredientEditorSaveTests.OnDiskInternal();
        try
        {
            var nav = new FakeNav();
            var dialogs = new FakeDialogs();
            var editorFactory = EditorFactory(nav, session, dialogs);
            using var explorer = new ExplorerViewModel(session.Current!, nav, dialogs, new FakeNotYetWired(),
                new ImageBridge(), editorFactory, CookFactory(dialogs));

            // Select the ingredient, open its editor (Explorer subscribes to Saved), paint + save.
            var ingNode = explorer.Root.Children[0].Children[0];
            explorer.SelectNodeCommand.Execute(ingNode);
            var detail = (IngredientDetailViewModel)explorer.CurrentDetail!;
            // The editor is created by IngredientDetailView's Edit action → factory; build it the same way:
            var editor = editorFactory(ing, recipe, session.Current!);
            editor.Saved += explorer.OnEditorSaved;          // mirrors what OpenEditor wires
            editor.ActiveTool = EditorTool.Fill; editor.BrushValue = 123;
            editor.ApplyToolStroke(new[] { (0, 0) });
            editor.SaveCommand.Execute(null);

            // Explorer rebuilt from the saved graph and still points at the same ingredient id.
            Assert.Equal("aura", explorer.SelectedNode!.Id);
            Assert.Same(session.Current, explorer.BookForTest);   // rebuilt against the new book
            editor.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }
```
  Support this test:
  - Expose the `OnDisk()` builder from `IngredientEditorSaveTests` as `internal static … OnDiskInternal()` (rename or add a thin internal wrapper) so this file can reuse it.
  - The editor's `SaveCommand.Execute(null)` runs the async command synchronously-enough for the headless test; if the assertion races, `await ((IAsyncRelayCommand)editor.SaveCommand).ExecuteAsync(null)` in an `async Task` `[AvaloniaFact]` instead.
  - Add an `internal LoadedCookBook BookForTest => _book;` accessor (or assert via `explorer.Root.Domain`).

- [ ] **Step 2: Run — fails** (`OnEditorSaved`/refresh missing).

- [ ] **Step 3: Implement.** In `ExplorerViewModel.cs`:
  - Make the book field mutable: `private LoadedCookBook _book;` and make `Root` settable/notifying — convert `public ExplorerNode Root { get; }` to `[ObservableProperty] private ExplorerNode _root = default!;` (CommunityToolkit) and change `Roots` to `public IReadOnlyList<ExplorerNode> Roots => new[] { Root };` with an `OnRootChanged` partial that raises `OnPropertyChanged(nameof(Roots))`. In the ctor set `Root = BuildTree(book);`.
  - Add the editor-open seam. Replace the inline `() => _nav.To(_editorFactory(i, r, _book))` in `OnSelectedNodeChanged` with `() => OpenEditor(i, r)` and add:
    ```csharp
    private void OpenEditor(LoadedIngredient i, LoadedRecipe r)
    {
        var editor = _editorFactory(i, r, _book);
        editor.Saved += OnEditorSaved;
        _nav.To(editor);
    }

    /// <summary>The editor persisted an ingredient; the session now holds the spliced graph. Rebuild
    /// the tree from it and reselect the same ingredient so its detail/thumbnails refresh in place.</summary>
    internal void OnEditorSaved(LoadedCookBook book)
    {
        _book = book;
        Root = BuildTree(book);
        var id = SelectedNode?.Id;
        var reselected = id is null ? null : FindIngredientNode(Root, id);
        SelectedNode = reselected ?? Root;   // fall back to the cookbook root if it vanished (it won't)
    }

    private static ExplorerNode? FindIngredientNode(ExplorerNode root, string id) =>
        root.Children.SelectMany(r => r.Children).FirstOrDefault(n => n.Id == id);
    ```
  - `OnSelectedNodeChanged` already rebuilds `CurrentDetail` from `_book`; since `_book` is updated first, reselecting produces a detail VM over the new ingredient. Confirm the detail VMs read `_book` (they do).
  - `_editorFactory(i, r, _book)` in `OnSelectedNodeChanged` used `_book`; keep passing `_book` (now mutable).

- [ ] **Step 4: Run — passes;** whole suite green; build 0 warnings.

- [ ] **Step 5: Commit** `feat(gui): Explorer refreshes in place when the editor saves`

---

### Task 5: Full verification + manual smoke

**Files:** none (verification); optional `tests/Nfty.App.Tests/VisualCapture.cs` tweak.

- [ ] **Step 1:** `dotnet build nfty.sln --nologo` → 0 warnings. `dotnet test nfty.sln --nologo` → all pass (report totals for Cli/App/Core).
- [ ] **Step 2:** `grep -rniE "#[0-9a-fA-F]{6}" src/Nfty.App/Views/ConfirmDialogView.axaml src/Nfty.App/Views/IngredientEditorView.axaml` → no raw hex.
- [ ] **Step 3 (optional visual):** if `Capture_editor_paint` needs the new ctor args it was already updated in T2; re-render both themes (`NFTY_CAPTURE=1`) and view them to confirm the Save button renders enabled after a paint. Report what you saw.
- [ ] **Step 4: Manual smoke (user):** `dotnet run --project src/Nfty.Desktop`; open a real `.cbk`; edit a **dynamic/static** ingredient (paint), click **Save** → no error; navigate **Back** → the Explorer thumbnail reflects the edit; **reopen** the `.cbk` from disk → the edit persisted. Confirm **Save is disabled** on a **custom** layer and on a freshly-created (never-saved) cookbook; confirm **Back with unsaved edits** prompts to discard.
- [ ] **Step 5:** Commit any smoke fixups: `test(gui): verify editor Save end-to-end`.

---

## Self-Review
- **Spec coverage:** §2.1 session path + Replace → T1. §2.2 Save (export/upsert/atomic write/rehash/replace/dispose) → T2. §2.3 CanSave guard + custom block + discard confirm → T2 (guard) + T3 (discard). §2.4 Explorer live refresh → T4. §3 data flow → T2/T4. §4 error handling (temp cleanup, dialog, dirty preserved) → T2 Save catch. §5 tests → T1–T4 + T5 manual. §6 risks (shared-image disposal `_ing=newIng`, sibling-temp atomicity, exact hash format, reselect fallback) → T2 code + comments.
- **Placeholder scan:** every task carries full code or exact edits; the two "verify the record constructor" notes point at real files to check, not TBDs. No "add error handling"-style hand-waves.
- **Type consistency:** `ICookBookSession.SourcePath`/`Open(book,path)`/`Replace(book)` (T1) consumed by editor Save (T2); editor ctor `(ing,recipe,book,bridge,nav,notify,session,dialogs)` used identically in ServiceRegistration + all four test sites (T2); `Saved`/`OnEditorSaved`/`BuildTree`/`FindIngredientNode` names match across T2/T4; `CanSave`/`IsDirty`/`IsSaving`/`SaveCommand`/`BackCommand` consistent T2/T3. `LoadedCookBook` is a **class** with init-only props (not a record), so the Save code constructs `book3` explicitly (`new LoadedCookBook { Manifest = …, Recipes = …, SourceSha256 = sha }`) — no `with`. Verified against `src/Nfty.Core/Formats/Loaded.cs`.
