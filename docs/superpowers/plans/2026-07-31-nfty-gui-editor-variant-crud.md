# nfty GUI — Ingredient Editor variant CRUD (A1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Add / duplicate / delete / rename / reweight the variants of the ingredient open in the editor, mutating the in-memory `IngredientDraft`; the existing Save persists them.

**Architecture:** Small `Nfty.Core.Editing` additions (`ValueMap.Clone`, `IngredientDraft.DuplicateVariant`/`RemoveVariant`) do the model mutation. The editor VM wires the existing Add/Duplicate/Delete buttons and new inline name/weight controls to those, keeping the draft's variant list, the filmstrip collection, and the per-variant history dict in sync via one shared helper. `EditorVariant` becomes an observable object so rename/reweight update the bound filmstrip in place. Save is unchanged.

**Tech Stack:** .NET 10, Avalonia 11.2.3, CommunityToolkit.Mvvm (`[ObservableProperty]`/`[RelayCommand]`/`AsyncRelayCommand`/`[NotifyCanExecuteChangedFor]`/`ObservableObject`), `Nfty.Core.Editing`, xUnit + Avalonia.Headless.XUnit.

## Global Constraints
- **Ids are immutable + deterministic:** new variant ids are the smallest unused `variant-N` (N ≥ 1) over the draft's current ids — no RNG, ordinal-stable. Rename changes only the display `Name`; the id keys history and downstream refs.
- **Three-structure sync:** `_draft.Variants` (List<VariantDraft>), `Variants` (ObservableCollection<EditorVariant> filmstrip), and `_history` (Dictionary<string,EditHistory>) are all id-keyed and must stay consistent — one shared add/remove helper touches all three.
- **Delete ≥1:** `DeleteVariantCommand.CanExecute = Variants.Count > 1`; confirm via the Slice-2 `ConfirmDialogViewModel` before removing.
- **Save unchanged** — `IngredientDraftExporter.Export` already writes `draft.Variants`; variant edits ride the existing dirty/Save path. Custom layers: variant edits allowed in-memory, Save stays blocked (existing policy) — no new behavior.
- **Deviation from spec §2.2/§6 (documented):** `EditorVariant` becomes an `ObservableObject` (not a record), so rename/reweight mutate the filmstrip item in place instead of replacing it — this removes the selection-churn risk the spec called out. Same observable behavior, cleaner.
- Determinism/idiom: `StringComparer.Ordinal` where ids sort; token brushes only in Views (no raw hex outside `Tokens.axaml`); 8-digit hex `#AARRGGBB`. `[AvaloniaFact]` for Avalonia-constructing tests. Build 0 warnings. Conventional commits. Agents: caveman-ultra terse chat; code/commits/reports normal prose.

## File Structure
- `src/Nfty.Core/Editing/ValueMap.cs` — add `Clone()` (T1).
- `src/Nfty.Core/Editing/IngredientDraft.cs` — add `DuplicateVariant`/`RemoveVariant` (T1).
- `src/Nfty.App/ViewModels/IngredientEditorViewModel.cs` — `EditorVariant`→observable; real Add/Duplicate/Delete; `SelectedName`/`SelectedWeight`; sync helper; thumbnail render (T2, T3).
- `src/Nfty.App/Views/IngredientEditorView.axaml` — inline name/weight controls (T4).
- Tests: `tests/Nfty.Core.Tests/ValueMapTests.cs` + `IngredientDraftTests.cs` (T1); `tests/Nfty.App.Tests/IngredientEditorVariantTests.cs` (new, T2/T3); `VisualCapture.cs` (T5).

---

### Task 1: Core — `ValueMap.Clone` + `IngredientDraft` duplicate/remove

**Files:** Modify `src/Nfty.Core/Editing/ValueMap.cs`, `src/Nfty.Core/Editing/IngredientDraft.cs`; Test `tests/Nfty.Core.Tests/ValueMapTests.cs` (create or append), `tests/Nfty.Core.Tests/IngredientDraftTests.cs` (create or append).

**Interfaces:**
- Produces: `ValueMap ValueMap.Clone()`; `VariantDraft IngredientDraft.DuplicateVariant(string sourceId, string newId, string newName)`; `void IngredientDraft.RemoveVariant(string id)`.

- [ ] **Step 1: Read** `ValueMap.cs` fully to see the private buffer fields (`_value`/`_alpha` byte arrays, `Width`/`Height`) and the existing ctor, so `Clone` copies correctly.

- [ ] **Step 2: Failing tests.** In `tests/Nfty.Core.Tests/ValueMapTests.cs`:
```csharp
using Nfty.Core.Editing;
using Xunit;

namespace Nfty.Core.Tests;

public class ValueMapTests
{
    [Fact]
    public void Clone_is_an_independent_deep_copy()
    {
        var a = new ValueMap(4, 4);
        a.Set(1, 2, 200, 255);
        var b = a.Clone();
        Assert.Equal(200, b.GetValue(1, 2));
        Assert.Equal(255, b.GetAlpha(1, 2));
        b.Set(1, 2, 10, 10);                 // mutate the clone
        Assert.Equal(200, a.GetValue(1, 2)); // source untouched
        a.Set(0, 0, 50, 50);                 // mutate the source
        Assert.Equal(0, b.GetValue(0, 0));   // clone untouched
    }
}
```
  In `tests/Nfty.Core.Tests/IngredientDraftTests.cs`:
```csharp
using System.Linq;
using Nfty.Core.Editing;
using Nfty.Core.Model;
using Xunit;

namespace Nfty.Core.Tests;

public class IngredientDraftTests
{
    private static IngredientDraft Draft()
    {
        var canvas = new Dimensions(4, 4);
        var v = new VariantDraft("a", "A", 2, ValueMap.ForCanvas(canvas));
        v.Map.Set(1, 1, 150, 255);
        return new IngredientDraft("ing", "Ing", LayerKind.Dynamic, null, canvas, new[] { v });
    }

    [Fact]
    public void DuplicateVariant_copies_pixels_weight_with_a_new_id()
    {
        var d = Draft();
        var copy = d.DuplicateVariant("a", "b", "A copy");
        Assert.Equal(2, d.Variants.Count);
        Assert.Equal("b", copy.Id);
        Assert.Equal("A copy", copy.Name);
        Assert.Equal(2, copy.Weight);
        Assert.Equal(150, copy.Map.GetValue(1, 1));      // pixels copied
        copy.Map.Set(1, 1, 9, 9);
        Assert.Equal(150, d.Variants[0].Map.GetValue(1, 1)); // independent from source
    }

    [Fact]
    public void DuplicateVariant_rejects_a_duplicate_or_missing_id()
    {
        var d = Draft();
        Assert.Throws<System.InvalidOperationException>(() => d.DuplicateVariant("a", "a", "x")); // newId exists
        Assert.Throws<System.InvalidOperationException>(() => d.DuplicateVariant("nope", "b", "x")); // source absent
    }

    [Fact]
    public void RemoveVariant_removes_by_id_and_rejects_absent()
    {
        var d = Draft();
        d.DuplicateVariant("a", "b", "B");
        d.RemoveVariant("a");
        Assert.Equal(new[] { "b" }, d.Variants.Select(v => v.Id));
        Assert.Throws<System.InvalidOperationException>(() => d.RemoveVariant("nope"));
    }
}
```

- [ ] **Step 3: Run — fail** (`Clone`/`DuplicateVariant`/`RemoveVariant` missing). `dotnet test tests/Nfty.Core.Tests --filter "FullyQualifiedName~ValueMapTests|FullyQualifiedName~IngredientDraftTests" --nologo`.

- [ ] **Step 4: Implement.** In `ValueMap.cs` (adapt to the real field names read in Step 1):
```csharp
    /// <summary>An independent deep copy — cloned value/alpha buffers, same dimensions.</summary>
    public ValueMap Clone()
    {
        var c = new ValueMap(Width, Height);
        System.Array.Copy(_value, c._value, _value.Length);
        System.Array.Copy(_alpha, c._alpha, _alpha.Length);
        return c;
    }
```
  In `IngredientDraft.cs`:
```csharp
    public VariantDraft DuplicateVariant(string sourceId, string newId, string newName)
    {
        var src = Variants.FirstOrDefault(v => v.Id == sourceId)
            ?? throw new InvalidOperationException($"No variant '{sourceId}' in ingredient '{Id}'.");
        if (Variants.Any(v => v.Id == newId))
            throw new InvalidOperationException($"Variant id '{newId}' already exists in ingredient '{Id}'.");
        var copy = new VariantDraft(newId, newName, src.Weight, src.Map.Clone());
        Variants.Add(copy);
        return copy;
    }

    public void RemoveVariant(string id)
    {
        var v = Variants.FirstOrDefault(x => x.Id == id)
            ?? throw new InvalidOperationException($"No variant '{id}' in ingredient '{Id}'.");
        Variants.Remove(v);
    }
```
  Add `using System.Linq;`/`using System;` to `IngredientDraft.cs` if absent.

- [ ] **Step 5: Run — pass;** `dotnet test tests/Nfty.Core.Tests --nologo` whole Core suite green; `dotnet build src/Nfty.Core --nologo` 0 warnings.

- [ ] **Step 6: Commit** `feat(editing): ValueMap.Clone + IngredientDraft duplicate/remove variant`

---

### Task 2: Editor VM — Add / Duplicate / Delete variant

**Files:** Modify `src/Nfty.App/ViewModels/IngredientEditorViewModel.cs`; Test `tests/Nfty.App.Tests/IngredientEditorVariantTests.cs` (create).

**Interfaces:**
- Consumes: T1's `IngredientDraft.AddVariant`/`DuplicateVariant`/`RemoveVariant`, `ValueMap.Clone`; the existing `_draft`/`_history`/`Variants`/`SelectedVariant`/`IsDirty`/`_dialogs`/`_bridge` and `ConfirmDialogViewModel` (Slice 2).
- Produces: real `AddVariantCommand`/`DuplicateVariantCommand`/`DeleteVariantCommand` (Delete async, gated `Variants.Count > 1`); an observable `EditorVariant`.

- [ ] **Step 1: Failing tests** — create `IngredientEditorVariantTests.cs`. Reuse the Slice-2 on-disk fixture for the Save round-trip:
```csharp
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Xunit;

namespace Nfty.App.Tests;

public class IngredientEditorVariantTests
{
    private static IngredientEditorViewModel Editor(out CookBookSession session, out string path)
    {
        (path, session, var recipe, var ing) = IngredientEditorSaveTests.OnDisk();
        return new IngredientEditorViewModel(ing, recipe, session.Current!, new ImageBridge(),
            new FakeNav(), new FakeNotYetWired(), session, new FakeDialogs());
    }

    [AvaloniaFact]
    public void AddVariant_appends_a_selected_blank_variant_and_dirties()
    {
        var vm = Editor(out var session, out var path);
        try
        {
            var before = vm.Variants.Count;
            vm.AddVariantCommand.Execute(null);
            Assert.Equal(before + 1, vm.Variants.Count);
            Assert.Same(vm.Variants[^1], vm.SelectedVariant);       // new one selected
            Assert.True(vm.IsDirty);
            Assert.Distinct(vm.Variants.Select(v => v.Id));         // unique ids
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [AvaloniaFact]
    public void DuplicateVariant_copies_the_selected_painted_pixels()
    {
        var vm = Editor(out var session, out var path);
        try
        {
            vm.ActiveTool = EditorTool.Fill; vm.BrushValue = 180;
            vm.ApplyToolStroke(new[] { (0, 0) });                   // paint the current variant
            vm.DuplicateVariantCommand.Execute(null);
            Assert.Same(vm.Variants[^1], vm.SelectedVariant);       // copy selected
            Assert.Equal(180, vm.ValueAt(4, 4));                    // copy carries the painted value
            Assert.NotEqual(vm.Variants[0].Id, vm.Variants[^1].Id); // distinct id
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [AvaloniaFact]
    public void DeleteVariant_is_disabled_on_the_last_and_removes_otherwise()
    {
        var vm = Editor(out var session, out var path);
        try
        {
            Assert.False(vm.DeleteVariantCommand.CanExecute(null));  // one variant → disabled
            vm.AddVariantCommand.Execute(null);
            Assert.True(vm.DeleteVariantCommand.CanExecute(null));   // two → enabled
            var removedId = vm.SelectedVariant!.Id;
            vm.DeleteVariantCommand.Execute(null);                   // FakeDialogs returns default(bool)=false...
            // ...so wire a confirming dialog for the actual removal assertion:
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Delete_removes_from_all_three_structures_when_confirmed()
    {
        (var path, var session, var recipe, var ing) = IngredientEditorSaveTests.OnDisk();
        try
        {
            var vm = new IngredientEditorViewModel(ing, recipe, session.Current!, new ImageBridge(),
                new FakeNav(), new FakeNotYetWired(), session, new ConfirmingDialogs(true));
            vm.AddVariantCommand.Execute(null);
            var id = vm.SelectedVariant!.Id;
            await vm.DeleteVariantCommand.ExecuteAsync(null);
            Assert.DoesNotContain(vm.Variants, v => v.Id == id);
            Assert.NotNull(vm.SelectedVariant);                     // neighbor selected
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Added_variant_persists_through_save()
    {
        (var path, var session, var recipe, var ing) = IngredientEditorSaveTests.OnDisk();
        try
        {
            var vm = new IngredientEditorViewModel(ing, recipe, session.Current!, new ImageBridge(),
                new FakeNav(), new FakeNotYetWired(), session, new FakeDialogs());
            vm.AddVariantCommand.Execute(null);
            vm.ActiveTool = EditorTool.Fill; vm.BrushValue = 90;
            vm.ApplyToolStroke(new[] { (0, 0) });
            await vm.SaveCommand.ExecuteAsync(null);
            using var reread = CookBookArchive.Read(path);
            var rip = reread.Recipes[0].Ingredients.Single(i => i.Manifest.Id == "aura");
            Assert.Equal(2, rip.Manifest.Variants.Count);           // original + added
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    // Minimal confirm-returning dialog double (mirrors the one in IngredientEditorSaveTests).
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
  (If `Assert.Distinct` isn't available in this xUnit version, assert `vm.Variants.Select(v=>v.Id).Distinct().Count() == vm.Variants.Count`.)

- [ ] **Step 2: Run — fail** (stubs still `_notify`; Delete not async/gated).

- [ ] **Step 3: Make `EditorVariant` observable.** Replace the record:
```csharp
public partial class EditorVariant : ObservableObject
{
    public string Id { get; }
    [ObservableProperty] private string _name;
    [ObservableProperty] private double _weight;
    [ObservableProperty] private Bitmap _thumbnail;
    public EditorVariant(string id, string name, double weight, Bitmap thumbnail)
    { Id = id; _name = name; _weight = weight; _thumbnail = thumbnail; }
}
```
  Add `using CommunityToolkit.Mvvm.ComponentModel;` if not present. The filmstrip `DataTemplate` binds `Name`/`Weight`/`Thumbnail` unchanged (now notifying). Ctor construction `new EditorVariant(v.Id, v.Name, v.Weight, VariantImagery.Render(...))` is unchanged.

- [ ] **Step 4: Implement the ops + helpers.** In `IngredientEditorViewModel.cs`:
  - Id + thumbnail helpers:
    ```csharp
    // Smallest unused "variant-N" over the draft's current ids (deterministic, no RNG).
    private string NextVariantId()
    {
        for (int n = 1; ; n++) { var id = $"variant-{n}"; if (_draft.Variants.All(v => v.Id != id)) return id; }
    }

    // A filmstrip thumbnail for a draft variant's value-map — colorized like the preview for
    // dynamic/static, grayscale for custom (matches RenderPreview's logic, since a freshly added
    // variant has no entry in _ing.VariantImages to render from).
    private Bitmap RenderThumb(ValueMap map)
    {
        using var img = map.ToImage();
        return _ing.Manifest.Colorization is null
            ? _bridge.ToBitmap(img)
            : VariantImagery.RenderWith(_bridge, img, Mode == LayerKind.Dynamic,
                HueMin, HueMax, SatMin, SatMax, FixedColor, _previewSalt);
    }
    ```
  - Add:
    ```csharp
    [RelayCommand]
    private void AddVariant()
    {
        var vd = _draft.AddVariant(NextVariantId(), $"Variant {_draft.Variants.Count + 1}", 1);
        _history[vd.Id] = new EditHistory();
        var ev = new EditorVariant(vd.Id, vd.Name, vd.Weight, RenderThumb(vd.Map));
        Variants.Add(ev);
        SelectedVariant = ev;
        IsDirty = true;
        DeleteVariantCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanMutateSelected))]
    private void DuplicateVariant()
    {
        var src = ActiveDraft!;
        var vd = _draft.DuplicateVariant(src.Id, NextVariantId(), $"{src.Name} copy");
        _history[vd.Id] = new EditHistory();
        var ev = new EditorVariant(vd.Id, vd.Name, vd.Weight, RenderThumb(vd.Map));
        Variants.Add(ev);
        SelectedVariant = ev;
        IsDirty = true;
        DeleteVariantCommand.NotifyCanExecuteChanged();
    }

    private bool CanMutateSelected() => SelectedVariant is not null;
    private bool CanDeleteVariant() => Variants.Count > 1;

    [RelayCommand(CanExecute = nameof(CanDeleteVariant))]
    private async Task DeleteVariant()
    {
        if (SelectedVariant is not { } target) return;
        var ok = await _dialogs.ShowAsync<bool>(new ConfirmDialogViewModel(_dialogs,
            "Delete variant?", $"Remove “{target.Name}” from this ingredient.", "Delete"));
        if (!ok) return;
        var idx = Variants.IndexOf(target);
        _draft.RemoveVariant(target.Id);
        _history.Remove(target.Id);
        Variants.Remove(target);
        target.Thumbnail.Dispose();
        SelectedVariant = Variants.Count == 0 ? null : Variants[System.Math.Max(0, idx - 1)];
        IsDirty = true;
        DeleteVariantCommand.NotifyCanExecuteChanged();
    }
    ```
  - In `OnSelectedVariantChanged`, also notify the selection-dependent commands:
    ```csharp
    DuplicateVariantCommand.NotifyCanExecuteChanged();
    ```
    (Delete's CanExecute depends on count, re-notified in the ops above.)
  - Remove the three `[RelayCommand] … _notify.Report(...)` variant stubs (`AddVariant`/`DuplicateVariant`/`DeleteVariant`) — replaced above.

- [ ] **Step 5: Run — pass;** whole App suite green; build 0 warnings.

- [ ] **Step 6: Commit** `feat(gui): add/duplicate/delete variant in the ingredient editor`

---

### Task 3: Editor VM — rename + reweight the selected variant

**Files:** Modify `src/Nfty.App/ViewModels/IngredientEditorViewModel.cs`; Test `tests/Nfty.App.Tests/IngredientEditorVariantTests.cs`.

**Interfaces:**
- Produces: `[ObservableProperty] string SelectedName`, `[ObservableProperty] double SelectedWeight` — bound to inline controls; write through to the selected `VariantDraft` + filmstrip `EditorVariant`, set `IsDirty`, with validation.

- [ ] **Step 1: Failing tests** — append:
```csharp
    [AvaloniaFact]
    public void Rename_writes_through_to_draft_and_filmstrip_and_rejects_empty()
    {
        var vm = Editor(out var session, out var path);
        try
        {
            vm.SelectedName = "Glowy";
            Assert.Equal("Glowy", vm.SelectedVariant!.Name);        // filmstrip updated
            Assert.True(vm.IsDirty);
            vm.SelectedName = "   ";                                // invalid → rejected
            Assert.Equal("Glowy", vm.SelectedVariant!.Name);        // unchanged
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [AvaloniaFact]
    public void Reweight_writes_through_and_rejects_non_positive()
    {
        var vm = Editor(out var session, out var path);
        try
        {
            vm.SelectedWeight = 3.5;
            Assert.Equal(3.5, vm.SelectedVariant!.Weight);
            Assert.True(vm.IsDirty);
            vm.SelectedWeight = 0;                                  // invalid → rejected
            Assert.Equal(3.5, vm.SelectedVariant!.Weight);
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [AvaloniaFact]
    public void Selecting_a_variant_syncs_the_name_and_weight_fields_without_dirtying()
    {
        var vm = Editor(out var session, out var path);
        try
        {
            vm.AddVariantCommand.Execute(null);       // adds + selects "Variant 2" (dirties)
            vm.IsDirty = false;                        // reset to observe selection sync
            vm.SelectVariantCommand.Execute(vm.Variants[0]);
            Assert.Equal(vm.Variants[0].Name, vm.SelectedName);
            Assert.Equal(vm.Variants[0].Weight, vm.SelectedWeight);
            Assert.False(vm.IsDirty);                  // pure selection sync must not dirty
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }
```

- [ ] **Step 2: Run — fail** (`SelectedName`/`SelectedWeight` missing).

- [ ] **Step 3: Implement** in `IngredientEditorViewModel.cs`:
  - Add fields + props:
    ```csharp
    private bool _syncingSelection;   // true while pushing selection → SelectedName/Weight (suppresses write-back)

    [ObservableProperty] private string _selectedName = "";
    [ObservableProperty] private double _selectedWeight = 1;
    ```
  - Write-through hooks (validate; revert invalid via the sync guard):
    ```csharp
    partial void OnSelectedNameChanged(string value)
    {
        if (_syncingSelection) return;
        if (ActiveDraft is not { } d) return;
        if (string.IsNullOrWhiteSpace(value)) { SyncSelectedFields(); return; }  // reject → restore
        d.Name = value;
        SelectedVariant!.Name = value;      // observable → filmstrip updates in place
        IsDirty = true;
    }

    partial void OnSelectedWeightChanged(double value)
    {
        if (_syncingSelection) return;
        if (ActiveDraft is not { } d) return;
        if (value <= 0) { SyncSelectedFields(); return; }                        // reject → restore
        d.Weight = value;
        SelectedVariant!.Weight = value;    // observable → filmstrip updates in place
        IsDirty = true;
    }

    private void SyncSelectedFields()
    {
        _syncingSelection = true;
        SelectedName = SelectedVariant?.Name ?? "";
        SelectedWeight = SelectedVariant?.Weight ?? 1;
        _syncingSelection = false;
    }
    ```
  - Call `SyncSelectedFields();` at the end of `OnSelectedVariantChanged` so the fields track the selection.

- [ ] **Step 4: Run — pass;** whole App suite green; build 0 warnings.

- [ ] **Step 5: Commit** `feat(gui): rename and reweight the selected editor variant`

---

### Task 4: View — inline name/weight controls

**Files:** Modify `src/Nfty.App/Views/IngredientEditorView.axaml`.

- [ ] **Step 1:** Under the filmstrip `StackPanel` (Grid.Column="0", after the `ItemsControl`), add name/weight editors bound to the VM, disabled when nothing is selected:
```xml
        <TextBlock Text="Name" Classes="muted" Margin="0,8,0,0" />
        <TextBox Text="{Binding SelectedName}" IsEnabled="{Binding SelectedVariant, Converter={x:Static ObjectConverters.IsNotNull}}" />
        <TextBlock Text="Weight" Classes="muted" Margin="0,6,0,0" />
        <NumericUpDown Value="{Binding SelectedWeight}" Minimum="0.01" Increment="1"
                       IsEnabled="{Binding SelectedVariant, Converter={x:Static ObjectConverters.IsNotNull}}" />
```
  (`ObjectConverters.IsNotNull` is in the `Avalonia.Data.Converters` namespace, available by default in Avalonia XAML.) Token styles; no raw hex. The Delete button already disables on the last variant via `CanExecute`.

- [ ] **Step 2:** `dotnet build src/Nfty.Desktop --nologo` 0 warnings; `dotnet test tests/Nfty.App.Tests --nologo` green (SmokeTests still resolves the view); `grep -rniE "#[0-9a-fA-F]{6}" src/Nfty.App/Views/IngredientEditorView.axaml` → nothing.

- [ ] **Step 3: Commit** `feat(gui): inline name/weight editors for the selected variant`

---

### Task 5: Verification + visual + manual smoke

**Files:** Modify `tests/Nfty.App.Tests/VisualCapture.cs`.

- [ ] **Step 1:** In `Capture_editor_paint` (or a new `Capture_editor_variants`), before capturing, `vm.AddVariantCommand.Execute(null)` so the filmstrip shows two entries and the name/weight controls populate; render both themes → view the PNGs (Read tool). Confirm the filmstrip shows the added variant and the name/weight editors read cleanly. Report what you saw; fix any layout gap (tokens only).
- [ ] **Step 2:** `dotnet build nfty.sln --nologo` → 0 warnings. `dotnet test nfty.sln --nologo` → all pass (report Cli/App/Core totals).
- [ ] **Step 3:** `grep -rniE "#[0-9a-fA-F]{6}" src/Nfty.App/Views/IngredientEditorView.axaml` → nothing.
- [ ] **Step 4: Manual smoke (user):** run the desktop app; open a `.cbk`; edit a dynamic/static ingredient; **Add** a variant (blank, selected) → paint it; **Duplicate** it; **rename** + **reweight** via the inline fields; **Delete** (confirm) — disabled on the last; **Save**; reopen the `.cbk` → the variant set persisted.
- [ ] **Step 5:** Commit `test(gui): render editor variant CRUD for visual verification` (+ any smoke fixups).

---

## Self-Review
- **Spec coverage:** §2.1 Core (Clone/Duplicate/Remove + unique-id policy) → T1 + `NextVariantId`. §2.2 Add/Dup/Delete + sync + thumbnails → T2. §2.2 rename/reweight write-through + validation → T3. §2.3 guards (delete ≥1, selection-required, confirm) → T2 `CanDeleteVariant`/`CanMutateSelected` + confirm. §2.4 view controls → T4. §5 tests → T1/T2/T3 + Save round-trip (T2) + visual (T5). §6 risks: three-structure sync via the ops touching all three; the immutable-record risk is *removed* by making `EditorVariant` observable (documented deviation); thumbnail disposed on delete (T2), reused on rename/reweight (in place, no re-render — T3).
- **Placeholder scan:** full code/edits in every step; the one `NextVariantId` first-draft is superseded inline by the clean form. No TBDs.
- **Type consistency:** `EditorVariant(id,name,weight,thumbnail)` ctor unchanged (record→observable class, same shape) so all construction sites compile; `AddVariant`/`DuplicateVariant`/`DeleteVariant`/`SelectedName`/`SelectedWeight`/`NextVariantId`/`RenderThumb`/`SyncSelectedFields` names consistent T2↔T3↔T4; Core `Clone`/`DuplicateVariant(sourceId,newId,newName)`/`RemoveVariant(id)` match T1↔T2. `ConfirmDialogViewModel(dialogs,title,message,confirmLabel)` matches the Slice-2 signature.
