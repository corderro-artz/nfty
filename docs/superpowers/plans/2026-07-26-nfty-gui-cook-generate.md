# nfty GUI — Cook → generate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Wire the CookBook detail's Cook button to generate a Set via `Nfty.Core` and write it to disk — a modal dialog (Count/Seed/Pack) → folder pick → `GenerateAsync`+`WriteAsync` with progress + cancel → done/Reveal, errors to the error dialog.

**Architecture:** A `CookDialogViewModel` runs the real async Core pipeline (`Generator.GenerateAsync` → `SetWriter.WriteAsync`) with `IProgress`/`CancellationToken`, opened from CookBook detail via a DI factory (mirrors the editor factory). New seams: `IFilePickerService.PickFolderAsync` and `IFolderRevealer` (Desktop impls; headless stubs). No `Nfty.Core` change.

**Tech Stack:** .NET 10, Avalonia 11.2.3, CommunityToolkit.Mvvm (`[ObservableProperty]`/`[RelayCommand]`, `AsyncRelayCommand`), `Nfty.Core` Generator/SetWriter, xUnit + Avalonia.Headless.XUnit.

## Global Constraints
- No `Nfty.Core` change (all seams exist). No behavioural change to unrelated code.
- Colours via `{DynamicResource}` tokens only in the view; no raw hex. Dialog uses foundation/token styles (functional now; mockup-fidelity later).
- The `LoadedCookBook` is **borrowed** by the dialog — never disposed there (the session owns it). The `GeneratedSet` **is** disposed by the dialog (`using`/finally).
- Tests building Avalonia controls use `[AvaloniaFact]`; filesystem tests use `Directory.CreateTempSubdirectory()` and clean up. Build 0 warnings. Conventional commits.
- Every ctor/DI change updates all construction sites in the same commit (DI, SmokeTests, ExplorerViewModelTests, CookBookDetailViewModelTests). Agents: caveman-ultra terse chat; code/commits/reports normal prose.
- Confirmed: `SetWriter.Write(set, outDir, pack)` always writes the folder at `outDir`; `pack:true` zips a sibling `<outDir>.set`. Cook picks a folder only.

## File Structure
- `src/Nfty.App/Services/IFilePickerService.cs` — add `PickFolderAsync` (T1).
- `src/Nfty.Desktop/DesktopFilePicker.cs` — implement `PickFolderAsync` (T1).
- `src/Nfty.App/Services/IFolderRevealer.cs` — NEW interface + no-op stub (T2); Desktop impl `src/Nfty.Desktop/DesktopFolderRevealer.cs` (T2).
- `src/Nfty.App/ViewModels/CookDialogViewModel.cs` — NEW (T3).
- `src/Nfty.App/Views/CookDialogView.axaml`(+`.cs`) — NEW (T4).
- `src/Nfty.App/ServiceRegistration.cs`, `ViewModels/ExplorerViewModel.cs`, `ViewModels/CookBookDetailViewModel.cs`, `src/Nfty.Desktop/App.axaml.cs` — wiring (T2/T5).
- Tests: `CookDialogViewModelTests.cs` (T3), `CookBookDetailViewModelTests.cs` + `SmokeTests.cs` (T5).

---

### Task 1: `PickFolderAsync` seam

**Files:** Modify `src/Nfty.App/Services/IFilePickerService.cs`, `src/Nfty.Desktop/DesktopFilePicker.cs`; Test `tests/Nfty.App.Tests/` (a small interface/stub test, or fold into an existing picker test).

**Interfaces:** Produces `Task<string?> IFilePickerService.PickFolderAsync(string title)`.

- [ ] **Step 1: Failing test** — add to a fake/stub test (e.g. `LandingOpenFlowTests` already has a `StubPicker`; or add `PickerStubTests`): assert `new FilePickerService().PickFolderAsync("x")` returns null (stub contract). If the test doubles implement `IFilePickerService`, add `PickFolderAsync` to them too.
```csharp
    [Fact]
    public async Task Stub_folder_picker_returns_null()
        => Assert.Null(await new FilePickerService().PickFolderAsync("x"));
```
- [ ] **Step 2: Run — fails** (no `PickFolderAsync`).
- [ ] **Step 3: Implement.** In `IFilePickerService.cs` add to the interface `Task<string?> PickFolderAsync(string title);` and to `FilePickerService` (stub): `public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);`. In `DesktopFilePicker.cs`:
```csharp
    public async Task<string?> PickFolderAsync(string title)
    {
        var top = TopLevel;
        if (top is null) return null;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
```
Confirm `FolderPickerOpenOptions`/`OpenFolderPickerAsync` names against Avalonia 11.2 (Context7 if unsure). Add `PickFolderAsync` to any other `IFilePickerService` implementor (grep `: IFilePickerService`) — including test doubles — so the solution compiles.
- [ ] **Step 4: Run — passes.** `dotnet test tests/Nfty.App.Tests --nologo` green; `dotnet build src/Nfty.Desktop --nologo` 0 warnings.
- [ ] **Step 5: Commit** `feat(gui): folder-picker seam (PickFolderAsync)`

---

### Task 2: `IFolderRevealer` seam

**Files:** Create `src/Nfty.App/Services/IFolderRevealer.cs`, `src/Nfty.Desktop/DesktopFolderRevealer.cs`; Modify `src/Nfty.App/ServiceRegistration.cs` (register no-op stub), `src/Nfty.Desktop/App.axaml.cs` (register Desktop impl after `AddNftyApp`).

**Interfaces:** Produces `interface IFolderRevealer { void Reveal(string path); }` + `NoopFolderRevealer` (stub) + `DesktopFolderRevealer`.

- [ ] **Step 1: Failing test** — `[Fact]` that `new NoopFolderRevealer().Reveal("x")` does not throw.
- [ ] **Step 2: Run — fails.**
- [ ] **Step 3: Implement.** `IFolderRevealer.cs`:
```csharp
namespace Nfty.App.Services;

/// <summary>Opens the OS file manager at a path. Head-specific; a no-op stub is used off-desktop.</summary>
public interface IFolderRevealer { void Reveal(string path); }

public sealed class NoopFolderRevealer : IFolderRevealer { public void Reveal(string path) { } }
```
`DesktopFolderRevealer.cs` (best-effort, never throws into the UI):
```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;
using Nfty.App.Services;

namespace Nfty.Desktop;

public sealed class DesktopFolderRevealer : IFolderRevealer
{
    public void Reveal(string path)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Process.Start(new ProcessStartInfo("open", $"\"{path}\"") { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo("xdg-open", $"\"{path}\"") { UseShellExecute = true });
        }
        catch { /* best effort — reveal must never crash the app */ }
    }
}
```
`ServiceRegistration.cs`: `services.AddSingleton<IFolderRevealer, NoopFolderRevealer>();` (after `IImageBridge`). `App.axaml.cs`: after `.AddNftyApp()` add `.AddSingleton<IFolderRevealer, DesktopFolderRevealer>()` (last-wins overrides the stub, same pattern as `DesktopFilePicker`).
- [ ] **Step 4: Run — passes;** build 0 warnings.
- [ ] **Step 5: Commit** `feat(gui): folder-revealer seam (Desktop opens OS file manager)`

---

### Task 3: `CookDialogViewModel` (the cook pipeline)

**Files:** Create `src/Nfty.App/ViewModels/CookDialogViewModel.cs`; Test `tests/Nfty.App.Tests/CookDialogViewModelTests.cs`.

**Interfaces:**
- Consumes: `IFilePickerService.PickFolderAsync`/`SaveFileAsync` (T1), `IFolderRevealer` (T2), `IDialogService`, Core `Generator.GenerateAsync`/`SetWriter.WriteAsync`/`GenerateOptions`/`UniqueSpace`.
- Produces: `CookDialogViewModel(LoadedCookBook book, IFilePickerService picker, IFolderRevealer revealer, IDialogService dialogs)` with `Count`/`Seed`/`Pack`/`IsRunning`/`Progress`/`PhaseText`/`IsDone`/`ResultText` and `CookCommand`/`CancelCommand`/`RevealCommand`/`CloseCommand`.

- [ ] **Step 1: Write the failing tests**
```csharp
// tests/Nfty.App.Tests/CookDialogViewModelTests.cs
using System.IO;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Xunit;

namespace Nfty.App.Tests;

public class CookDialogViewModelTests
{
    private sealed class FolderPicker : IFilePickerService
    {
        private readonly string? _folder;
        public FolderPicker(string? folder) => _folder = folder;
        public Task<string?> OpenFileAsync(string t, params string[] e) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string t, string e) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string t) => Task.FromResult(_folder);
    }
    private sealed class RecordingRevealer : IFolderRevealer
    { public string? Revealed; public void Reveal(string p) => Revealed = p; }

    // A tiny valid 2-recipe book with enough unique space (reuse ExplorerViewModelTests.TwoRecipeBook()).
    private static LoadedCookBook Book() => ExplorerViewModelTests.TwoRecipeBook();

    [AvaloniaFact]
    public async Task Cook_writes_a_set_to_the_chosen_folder()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var vm = new CookDialogViewModel(Book(), new FolderPicker(dir), new RecordingRevealer(), new FakeDialogs());
        vm.Count = 2; vm.Seed = "seed1"; vm.Pack = false;
        await vm.CookCommand.ExecuteAsync(null);
        Assert.True(vm.IsDone);
        Assert.True(File.Exists(Path.Combine(dir, "set.json")));   // Core wrote the set
        Assert.Contains("2", vm.ResultText);
    }

    [AvaloniaFact]
    public async Task Pack_produces_a_sibling_set_archive()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var vm = new CookDialogViewModel(Book(), new FolderPicker(dir), new RecordingRevealer(), new FakeDialogs());
        vm.Count = 2; vm.Seed = "seed1"; vm.Pack = true;
        await vm.CookCommand.ExecuteAsync(null);
        Assert.True(File.Exists(dir + ".set"));
    }

    [AvaloniaFact]
    public async Task Cancelled_pick_does_nothing()
    {
        var vm = new CookDialogViewModel(Book(), new FolderPicker(null), new RecordingRevealer(), new FakeDialogs());
        vm.Count = 2; vm.Seed = "s";
        await vm.CookCommand.ExecuteAsync(null);
        Assert.False(vm.IsDone);
        Assert.False(vm.IsRunning);
    }

    [AvaloniaFact]
    public async Task Too_large_count_surfaces_an_error_dialog()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var dialogs = new FakeDialogs();
        var vm = new CookDialogViewModel(Book(), new FolderPicker(dir), new RecordingRevealer(), dialogs);
        vm.Count = 100000; vm.Seed = "s"; vm.Pack = false;   // exceeds the fixture's unique space
        await vm.CookCommand.ExecuteAsync(null);
        Assert.False(vm.IsDone);
        Assert.False(vm.IsRunning);
        Assert.IsType<ErrorDialogViewModel>(dialogs.Active);   // error surfaced, no crash
    }
}
```
Note: `FakeDialogs` (existing test double) must expose `Active` and record `ShowAsync`. If it doesn't set `Active` on `ShowAsync`, extend it minimally (mirror the real `DialogService`) — in this task's commit.

- [ ] **Step 2: Run — fails** (no `CookDialogViewModel`).

- [ ] **Step 3: Implement `CookDialogViewModel`.**
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Output;

namespace Nfty.App.ViewModels;

public partial class CookDialogViewModel : ViewModelBase
{
    private readonly LoadedCookBook _book;
    private readonly IFilePickerService _picker;
    private readonly IFolderRevealer _revealer;
    private readonly IDialogService _dialogs;
    private CancellationTokenSource? _cts;
    private string? _outDir;

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(CookCommand))] private int _count = 50;
    [ObservableProperty] private string _seed = Guid.NewGuid().ToString("N")[..8];
    [ObservableProperty] private bool _pack;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(CookCommand))] [NotifyCanExecuteChangedFor(nameof(CancelCommand))] private bool _isRunning;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _phaseText = "";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(RevealCommand))] private bool _isDone;
    [ObservableProperty] private string _resultText = "";

    public CookDialogViewModel(LoadedCookBook book, IFilePickerService picker, IFolderRevealer revealer, IDialogService dialogs)
    { _book = book; _picker = picker; _revealer = revealer; _dialogs = dialogs; }

    private bool CanCook() => !IsRunning && Count > 0;

    [RelayCommand(CanExecute = nameof(CanCook))]
    private async Task Cook()
    {
        var dir = await _picker.PickFolderAsync("Cook to folder");
        if (dir is null) return;

        _cts = new CancellationTokenSource();
        IsRunning = true; IsDone = false; Progress = 0;
        GeneratedSet? set = null;
        try
        {
            var opts = new GenerateOptions(Count, Seed);
            PhaseText = "Generating…";
            var genProgress = new Progress<GenerationProgress>(p => Progress = p.Fraction);
            set = await Generator.GenerateAsync(_book, opts, progress: genProgress, cancellationToken: _cts.Token);

            PhaseText = "Writing…"; Progress = 0;
            var writeProgress = new Progress<WriteProgress>(p => Progress = p.Fraction);
            await SetWriter.WriteAsync(set, dir, Pack, writeProgress, _cts.Token);

            _outDir = dir;
            ResultText = $"{set.Assets.Count} assets → {dir}";
            IsDone = true;
        }
        catch (OperationCanceledException) { PhaseText = "Cancelled"; }
        catch (Exception ex)
        {
            await _dialogs.ShowAsync<object>(new ErrorDialogViewModel(_dialogs, "Cook failed", ex.Message));
        }
        finally
        {
            set?.Dispose();
            IsRunning = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))] private void Cancel() => _cts?.Cancel();
    private bool CanCancel() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanReveal))] private void Reveal() { if (_outDir is not null) _revealer.Reveal(_outDir); }
    private bool CanReveal() => IsDone;

    [RelayCommand] private void Close() => _dialogs.Close(null);
}
```
(Uses `using` on the `LoadedCookBook`? NO — it's borrowed; never dispose `_book`. Dispose only the `GeneratedSet`.)

- [ ] **Step 4: Run — passes.** `dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~CookDialogViewModelTests` PASS; whole App suite green.

- [ ] **Step 5: Commit** `feat(gui): CookDialogViewModel runs the real generate+write pipeline`

---

### Task 4: `CookDialogView` + ViewLocator

**Files:** Create `src/Nfty.App/Views/CookDialogView.axaml`(+`.cs`); Modify `tests/Nfty.App.Tests/SmokeTests.cs` (ViewLocator row).

- [ ] **Step 1:** Add a `CookDialogViewModel` row to the `SmokeTests` ViewLocator list (construct with fakes: `new CookDialogViewModel(ExplorerViewModelTests.TwoRecipeBook(), new FilePickerService(), new NoopFolderRevealer(), dialogs)`); run — fails (no view resolves → "View not found").
- [ ] **Step 2:** Create `CookDialogView.axaml` (a `Border.card`/panel dialog, `x:DataType="vm:CookDialogViewModel"`): a title "Cook"; the form — `NumericUpDown` bound `Count` (Minimum 1), `TextBox` bound `Seed`, `CheckBox` bound `Pack` ("Pack into a single .set"); a footer with Cook (`accent`, `CookCommand`) + Close (`tbtn`, `CloseCommand`). A running section (`IsVisible="{Binding IsRunning}"`): `ProgressBar` (Minimum 0 Maximum 1, `Value="{Binding Progress}"`), `PhaseText`, Cancel (`CancelCommand`). A done section (`IsVisible="{Binding IsDone}"`): `ResultText`, Reveal (`RevealCommand`) + Close. Token/foundation styles; no raw hex. Code-behind: `InitializeComponent` + `Esc`→Close when not running (optional; mirror ErrorDialogView).
- [ ] **Step 3:** Run — SmokeTests passes (view resolves); build 0 warnings.
- [ ] **Step 4: Commit** `feat(gui): CookDialogView`

---

### Task 5: Wire Cook from the CookBook detail

**Files:** Modify `src/Nfty.App/ServiceRegistration.cs`, `ViewModels/ExplorerViewModel.cs`, `ViewModels/CookBookDetailViewModel.cs`; Tests `CookBookDetailViewModelTests.cs`, `ExplorerViewModelTests.cs`, `SmokeTests.cs`.

**Interfaces:** `CookBookDetailViewModel(LoadedCookBook book, INotYetWired notify, Action cook)`; `ExplorerViewModel` ctor gains `Func<LoadedCookBook, CookDialogViewModel> cookFactory`; DI registers that factory.

- [ ] **Step 1: Failing test** — in `CookBookDetailViewModelTests`, assert `Cook()` invokes the injected action:
```csharp
    [AvaloniaFact]
    public void Cook_invokes_the_cook_action()
    {
        bool cooked = false;
        var vm = new CookBookDetailViewModel(ExplorerViewModelTests.TwoRecipeBook(), new FakeNotYetWired(), () => cooked = true);
        vm.CookCommand.Execute(null);
        Assert.True(cooked);
    }
```
- [ ] **Step 2: Run — fails** (ctor arity / Cook stubs `Report`).
- [ ] **Step 3: Implement.**
  - `CookBookDetailViewModel`: add `private readonly Action _cook;`, ctor param `Action cook`, `Cook()` → `_cook();` (remove the `_notify.Report("Cook")`).
  - `ServiceRegistration`: add `services.AddSingleton<Func<LoadedCookBook, CookDialogViewModel>>(sp => book => new CookDialogViewModel(book, sp.GetRequiredService<IFilePickerService>(), sp.GetRequiredService<IFolderRevealer>(), sp.GetRequiredService<IDialogService>()));`. Extend the `Func<LoadedCookBook, ExplorerViewModel>` registration to also resolve + pass the cook factory.
  - `ExplorerViewModel`: ctor gains `Func<LoadedCookBook, CookDialogViewModel> cookFactory` (store it); the CookBook detail branch becomes `new CookBookDetailViewModel(_book, _notify, () => _dialogs.ShowAsync<object>(_cookFactory(_book)))`.
  - Update every `new ExplorerViewModel(...)` / `new CookBookDetailViewModel(...)` site (SmokeTests, ExplorerViewModelTests incl. its `EditorFactory`-style helper, CookBookDetailViewModelTests). For tests, pass a stub cook factory `b => new CookDialogViewModel(b, new FilePickerService(), new NoopFolderRevealer(), dialogs)` and a `() => {}` cook action where a bare CookBookDetailViewModel is built. Grep: `grep -rn "new ExplorerViewModel(\|new CookBookDetailViewModel(" tests src`.
- [ ] **Step 4: Run — passes;** `dotnet build src/Nfty.Desktop --nologo` 0 warnings; whole App suite green (report totals; grep proves all sites updated).
- [ ] **Step 5: Commit** `feat(gui): Cook button opens the Cook dialog`

---

### Task 6: Full verification + manual smoke

**Files:** none (verification).

- [ ] **Step 1:** `dotnet build nfty.sln --nologo` 0 warnings; `dotnet test nfty.sln --nologo` all pass (report total).
- [ ] **Step 2:** `grep -rn "new ExplorerViewModel(\|new CookBookDetailViewModel(\|new CookDialogViewModel(" src tests` — confirm arities consistent; `grep -rniE "#[0-9a-f]{6}" src/Nfty.App/Views/CookDialogView.axaml` — no raw hex.
- [ ] **Step 3: Manual smoke (user-driven):** `dotnet run --project src/Nfty.Desktop`; open `tests/fixtures/VaporPets.cbk`; select the cookbook; **Cook set** → dialog → set a small Count + Seed → Cook → pick a folder → watch progress → done → **Reveal** opens the folder (assets + `set.json`; `.set` beside it if Pack). Try a too-large Count → error dialog. Confirm Cancel mid-run. (Folder picker + Reveal are head/OS-specific — this manual step is their verification.)
- [ ] **Step 4:** Commit only if smoke-driven fixups needed.

---

## Self-Review
- **Spec coverage:** §2.1 PickFolderAsync → T1. §2.1 IFolderRevealer → T2. §2.2 CookDialogViewModel (options/progress/cancel/done/reveal/errors, borrow book/dispose set) → T3. §2.4 CookDialogView → T4. §2.3 wiring (factory, ExplorerViewModel, CookBookDetailViewModel.Cook) → T5. §4 tests → T3 (VM incl. write/pack/cancel/error), T5 (Cook action), T1/T2 (stubs), T6 (verify + manual smoke for picker/reveal). No `Nfty.Core` change anywhere.
- **Placeholder scan:** T1-T3/T5 carry full code; T4 gives the view structure + bindings + the SmokeTests gate (the dialog has no locked mockup, so structure+visual-cleanliness is the bar, verified by SmokeTests resolution + the manual smoke). Doc-pull note only where objective (FolderPicker API name).
- **Type consistency:** `CookDialogViewModel(book, picker, revealer, dialogs)`, `CookBookDetailViewModel(book, notify, cook)`, `ExplorerViewModel(..., cookFactory)`, `Func<LoadedCookBook, CookDialogViewModel>` — defined in T3/T5 and used consistently; `GenerateOptions(Count, Seed)`, `Generator.GenerateAsync(book, opts, progress:, cancellationToken:)`, `SetWriter.WriteAsync(set, dir, pack, progress, ct)`, `GenerationProgress.Fraction`/`WriteProgress.Fraction`, `ErrorDialogViewModel(dialogs, title, message)` match Core/App.
