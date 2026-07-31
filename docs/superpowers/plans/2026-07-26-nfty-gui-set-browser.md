# nfty GUI — Set browser Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** View a cooked Set in-app — a `Nfty.Core.SetReader` (reads a cooked folder or `.set` zip → `LoadedSet`) plus a virtualized Set-browser screen (thumbnail grid + item detail) reached from Landing's "Open a cooked .set…".

**Architecture:** `SetReader` parses `set.json` + `nfty/NNNN.json` and returns item metadata + **image paths** (no decode); a `.set` is extracted to a temp dir owned by the `LoadedSet` (`IDisposable`). `SetBrowserViewModel` decodes small thumbnails from the paths; the view is a virtualized grid + detail rail. Landing's Open-set reads then navigates to the browser (a nav-stack page freed on Back). First `Nfty.Core` addition in the GUI work — additive, with round-trip tests.

**Tech Stack:** .NET 10, Avalonia 11.2.3 (virtualizing panel / `ItemsRepeater`, `Bitmap.DecodeToWidth`), CommunityToolkit.Mvvm, System.IO.Compression (`ZipFile`), `System.Text.Json` (`Nfty.Core.Formats.Json.Options`), xUnit + Avalonia.Headless.XUnit.

## Global Constraints
- The output format is authoritative (`SetWriter`): `images/NNNN.png` (`D4`), `nfty/NNNN.json` (`NftyMetadata`), `set.json` (`SetManifest`); packed `.set` = zip of the folder. All JSON via `Nfty.Core.Formats.Json.Options` (camelCase). `SetReader` reads `nfty/*.json` (rich) — NOT the OpenSea `metadata/*.json`.
- `SetReader` uses no ImageSharp — JSON + file paths + unzip only. `LoadedSet` owns/deletes an extracted temp dir; a folder read owns nothing.
- GUI: colours via `{DynamicResource}` tokens only, no raw hex; both themes; `[AvaloniaFact]` for Avalonia-constructing tests; filesystem tests use `Directory.CreateTempSubdirectory()` and clean up.
- The Set browser is a nav-stack **page**, `IDisposable`, freed by `NavigationService.Back()` (disposes popped pages) — disposes its thumbnails + the `LoadedSet`. No `Nfty.Core` regression (existing Core tests stay green). Build 0 warnings. Conventional commits. Agents: caveman-ultra terse chat; code/commits/reports normal prose.

## File Structure
- `src/Nfty.Core/Output/SetReader.cs` — NEW: `SetReader`, `LoadedSet`, `SetItem` (T1).
- `src/Nfty.App/ViewModels/SetBrowserViewModel.cs` — NEW (T2).
- `src/Nfty.App/Views/SetBrowserView.axaml`(+`.cs`) — NEW (T3).
- `src/Nfty.App/ViewModels/LandingViewModel.cs` + `ServiceRegistration.cs` — Open-set wiring + factory (T4).
- Tests: `tests/Nfty.Core.Tests/SetReaderTests.cs` (T1); `tests/Nfty.App.Tests/{SetBrowserViewModelTests,LandingOpenSetTests,SmokeTests,VisualCapture}.cs` (T2/T3/T4/T5).

---

### Task 1: Core `SetReader` + `LoadedSet`

**Files:** Create `src/Nfty.Core/Output/SetReader.cs`; Test `tests/Nfty.Core.Tests/SetReaderTests.cs`.

**Interfaces:**
- Consumes: `SetManifest`/`NftyMetadata`/`RarityAttribute`/`LayerColor` (Nfty.Core.Output.Metadata), `Nfty.Core.Formats.Json.Options`.
- Produces:
```csharp
public record SetItem(int Number, string ImagePath, string Dna, string Recipe,
    IReadOnlyList<RarityAttribute> Rarity, IReadOnlyList<LayerColor> Layers);
public sealed class LoadedSet : IDisposable {
    public required SetManifest Manifest { get; init; }
    public required IReadOnlyList<SetItem> Items { get; init; }
    // internal temp-dir cleanup
}
public static class SetReader { public static LoadedSet Read(string path); public static Task<LoadedSet> ReadAsync(string path, CancellationToken ct = default); }
```

- [ ] **Step 1: Write the failing test**
```csharp
// tests/Nfty.Core.Tests/SetReaderTests.cs
using System.IO;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.Core.Tests;

public class SetReaderTests
{
    // Minimal 1-recipe, 2-variant custom cookbook (custom = no colorization) with an 8x8 canvas.
    private static LoadedCookBook TinyBook()
    {
        LoadedIngredient Ing() => new()
        {
            Manifest = new IngredientManifest("bg", "bg", LayerKind.Custom, null,
                new[] { new Variant("a", "A", 1), new Variant("b", "B", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>>
                { ["a"] = new(8, 8), ["b"] = new(8, 8) },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "bg" }, System.Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { Ing() },
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(8, 8),
                new Collection("VaporCats", "desc", "VC"), new Dictionary<string, double> { ["cat"] = 100 }),
            Recipes = new[] { recipe },
        };
    }

    private static string CookTo(bool pack)
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        using var set = Generator.Generate(TinyBook(), new GenerateOptions(Count: 2, Seed: "seed1"));
        SetWriter.Write(set, dir, pack);
        return dir;
    }

    [Fact]
    public void Reads_a_cooked_folder()
    {
        var dir = CookTo(pack: false);
        using var loaded = SetReader.Read(dir);
        Assert.Equal("VaporCats", loaded.Manifest.Name);
        Assert.Equal(2, loaded.Manifest.Count);
        Assert.Equal(2, loaded.Items.Count);
        Assert.All(loaded.Items, i => Assert.True(File.Exists(i.ImagePath)));
        Assert.All(loaded.Items, i => Assert.False(string.IsNullOrEmpty(i.Dna)));
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Reads_a_packed_set_and_cleans_up_temp_on_dispose()
    {
        var dir = CookTo(pack: true);
        string archive = dir + ".set";
        string? tempSeen;
        using (var loaded = SetReader.Read(archive))
        {
            Assert.Equal(2, loaded.Items.Count);
            tempSeen = Path.GetDirectoryName(loaded.Items[0].ImagePath);   // inside the extracted temp dir
            Assert.True(File.Exists(loaded.Items[0].ImagePath));
        }
        // after Dispose, the extracted temp dir is gone (the archive + original dir remain)
        Assert.False(Directory.Exists(Path.GetDirectoryName(tempSeen!)));
        Directory.Delete(dir, recursive: true); File.Delete(archive);
    }

    [Fact]
    public void Missing_set_json_throws()
    {
        var empty = Directory.CreateTempSubdirectory().FullName;
        Assert.ThrowsAny<System.Exception>(() => SetReader.Read(empty));
        Directory.Delete(empty, recursive: true);
    }
}
```

- [ ] **Step 2: Run — fails** (`SetReader` missing): `dotnet test tests/Nfty.Core.Tests --filter FullyQualifiedName~SetReaderTests`

- [ ] **Step 3: Implement `SetReader.cs`**
```csharp
using System.IO.Compression;
using System.Text.Json;
using Nfty.Core.Formats;

namespace Nfty.Core.Output;

public record SetItem(int Number, string ImagePath, string Dna, string Recipe,
    IReadOnlyList<RarityAttribute> Rarity, IReadOnlyList<LayerColor> Layers);

/// <summary>A cooked Set read from disk for browsing: the manifest + per-item metadata and image
/// paths (images are NOT decoded here). If read from a .set archive, owns the extracted temp dir.</summary>
public sealed class LoadedSet : IDisposable
{
    public required SetManifest Manifest { get; init; }
    public required IReadOnlyList<SetItem> Items { get; init; }
    internal string? TempDir { get; init; }

    public void Dispose()
    {
        if (TempDir is not null && Directory.Exists(TempDir))
            try { Directory.Delete(TempDir, recursive: true); } catch { /* best effort */ }
    }
}

public static class SetReader
{
    public static LoadedSet Read(string path)
    {
        string dir = path;
        string? temp = null;
        if (File.Exists(path))   // a .set archive (or any file) → extract to a temp dir
        {
            temp = Directory.CreateTempSubdirectory("nfty-set-").FullName;
            ZipFile.ExtractToDirectory(path, temp);
            dir = temp;
        }

        string setJson = Path.Combine(dir, "set.json");
        if (!File.Exists(setJson))
        {
            if (temp is not null) try { Directory.Delete(temp, recursive: true); } catch { }
            throw new FileNotFoundException($"Not a cooked Set — 'set.json' was not found in {path}.");
        }
        var manifest = JsonSerializer.Deserialize<SetManifest>(File.ReadAllText(setJson), Json.Options)
            ?? throw new InvalidOperationException($"Could not read the Set manifest in {path}.");

        string nftyDir = Path.Combine(dir, "nfty");
        string imagesDir = Path.Combine(dir, "images");
        var items = new List<SetItem>();
        if (Directory.Exists(nftyDir))
        {
            foreach (var file in Directory.EnumerateFiles(nftyDir, "*.json")
                         .OrderBy(f => f, StringComparer.Ordinal))
            {
                var m = JsonSerializer.Deserialize<NftyMetadata>(File.ReadAllText(file), Json.Options);
                if (m is null) continue;
                string stem = m.SetNumber.ToString("D4");
                items.Add(new SetItem(m.SetNumber, Path.Combine(imagesDir, $"{stem}.png"),
                    m.Dna, m.Recipe, m.Rarity, m.Layers));
            }
        }

        return new LoadedSet { Manifest = manifest, Items = items, TempDir = temp };
    }

    public static Task<LoadedSet> ReadAsync(string path, CancellationToken ct = default) =>
        Task.Run(() => Read(path), ct);
}
```
Confirm `NftyMetadata`'s member names (`SetNumber`, `Dna`, `Recipe`, `Rarity`, `Layers`) against `src/Nfty.Core/Output/Metadata.cs` (they are, per the spec) — adjust if any differ. `Json.Options` is in `Nfty.Core.Formats`.

- [ ] **Step 4: Run — passes.** Then `dotnet test tests/Nfty.Core.Tests --nologo` (whole Core suite green — additive, no regression).

- [ ] **Step 5: Commit**
```bash
git add src/Nfty.Core/Output/SetReader.cs tests/Nfty.Core.Tests/SetReaderTests.cs
git commit -m "feat(core): SetReader — read a cooked folder or .set into a LoadedSet"
```

---

### Task 2: `SetBrowserViewModel`

**Files:** Create `src/Nfty.App/ViewModels/SetBrowserViewModel.cs`; Test `tests/Nfty.App.Tests/SetBrowserViewModelTests.cs`.

**Interfaces:**
- Consumes: `LoadedSet`/`SetItem` (T1); `Avalonia.Media.Imaging.Bitmap.DecodeToWidth`.
- Produces: `SetBrowserViewModel(LoadedSet set) : IDisposable` with `Name`/`Count`/`Seed`, `IReadOnlyList<SetItemRow> Items` (`SetItemRow(int Number, Bitmap Thumbnail, SetItem Item)`), `[ObservableProperty] SetItemRow? SelectedItem`, and detail projections (`SelectedDna`/`SelectedRecipe`/`SelectedRarity`).

- [ ] **Step 1: Failing test** (build a real cooked temp set via SetWriter/Generator like Task 1, then read):
```csharp
// tests/Nfty.App.Tests/SetBrowserViewModelTests.cs
using System.IO;
using Avalonia.Headless.XUnit;
using Nfty.App.ViewModels;
using Nfty.Core.Generation;
using Nfty.Core.Output;
using Xunit;

namespace Nfty.App.Tests;

public class SetBrowserViewModelTests
{
    // Reuse a helper that cooks a tiny set to a temp folder and reads it (mirror SetReaderTests' book).
    private static LoadedSet CookedSet(out string dir)
    {
        dir = Directory.CreateTempSubdirectory().FullName;
        using var set = Generator.Generate(CoreTestBook.Tiny(), new GenerateOptions(2, "seed1"));
        SetWriter.Write(set, dir, pack: false);
        return SetReader.Read(dir);
    }

    [AvaloniaFact]
    public void Exposes_items_with_thumbnails_and_header()
    {
        var loaded = CookedSet(out var dir);
        using var vm = new SetBrowserViewModel(loaded);
        Assert.Equal("VaporCats", vm.Name);
        Assert.Equal(2, vm.Count);
        Assert.Equal(2, vm.Items.Count);
        Assert.All(vm.Items, r => Assert.NotNull(r.Thumbnail));
        vm.SelectedItem = vm.Items[0];
        Assert.False(string.IsNullOrEmpty(vm.SelectedDna));
        vm.Dispose();
        Directory.Delete(dir, recursive: true);
    }
}
```
Provide a tiny shared `CoreTestBook.Tiny()` builder in the App test project (copy the `TinyBook()` from Task 1 into a small internal helper `tests/Nfty.App.Tests/CoreTestBook.cs`), since the App tests can't see `Nfty.Core.Tests` internals.

- [ ] **Step 2: Run — fails.**

- [ ] **Step 3: Implement `SetBrowserViewModel.cs`**
```csharp
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Nfty.Core.Output;

namespace Nfty.App.ViewModels;

public record SetItemRow(int Number, Bitmap Thumbnail, SetItem Item);

public partial class SetBrowserViewModel : ViewModelBase, IDisposable
{
    private const int ThumbW = 128;
    private readonly LoadedSet _set;

    public string Name { get; }
    public int Count { get; }
    public string Seed { get; }
    public IReadOnlyList<SetItemRow> Items { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedDna))]
    [NotifyPropertyChangedFor(nameof(SelectedRecipe))]
    [NotifyPropertyChangedFor(nameof(SelectedRarity))]
    [NotifyPropertyChangedFor(nameof(SelectedNumber))]
    private SetItemRow? _selectedItem;

    public SetBrowserViewModel(LoadedSet set)
    {
        _set = set;
        Name = set.Manifest.Name;
        Count = set.Manifest.Count;
        Seed = set.Manifest.Seed;
        Items = set.Items.Select(i => new SetItemRow(i.Number, Decode(i.ImagePath), i)).ToList();
        SelectedItem = Items.Count > 0 ? Items[0] : null;
    }

    private static Bitmap Decode(string path)
    {
        using var fs = File.OpenRead(path);
        return Bitmap.DecodeToWidth(fs, ThumbW);   // small downscaled thumbnail
    }

    public string SelectedNumber => SelectedItem is null ? "" : $"#{SelectedItem.Number:D4}";
    public string SelectedDna => SelectedItem?.Item.Dna ?? "";
    public string SelectedRecipe => SelectedItem?.Item.Recipe ?? "";
    public IReadOnlyList<RarityAttribute> SelectedRarity => SelectedItem?.Item.Rarity ?? System.Array.Empty<RarityAttribute>();

    public void Dispose()
    {
        foreach (var r in Items) r.Thumbnail.Dispose();
        _set.Dispose();
    }
}
```
Confirm `Bitmap.DecodeToWidth(Stream, int)` exists in Avalonia 11.2.3 (it does — `Avalonia.Media.Imaging.Bitmap.DecodeToWidth`); if the signature differs, adjust (pull Context7 `/avaloniaui/avalonia-docs` "Bitmap DecodeToWidth"). `using System.IO;`/`System.Linq;` as needed. `RarityAttribute` is `Nfty.Core.Output`.

- [ ] **Step 4: Run — passes;** whole App suite green.

- [ ] **Step 5: Commit**
```bash
git add src/Nfty.App/ViewModels/SetBrowserViewModel.cs tests/Nfty.App.Tests/SetBrowserViewModelTests.cs tests/Nfty.App.Tests/CoreTestBook.cs
git commit -m "feat(gui): SetBrowserViewModel (thumbnails + item detail)"
```

---

### Task 3: `SetBrowserView`

**Files:** Create `src/Nfty.App/Views/SetBrowserView.axaml`(+`.cs`); Modify `tests/Nfty.App.Tests/SmokeTests.cs` (ViewLocator row).

- [ ] **Step 1:** Add a `SetBrowserViewModel` row to the `SmokeTests` ViewLocator list (construct from a cooked temp set via the `CoreTestBook`/`SetReader` helper); run — fails ("View not found").
- [ ] **Step 2:** Create `SetBrowserView.axaml` (`x:DataType="vm:SetBrowserViewModel"`): a header row (`Name` mono bold · `Count` items · `Seed` muted); a 2-column `Grid` — a **virtualized** thumbnail grid (main) + an item-detail rail (`~300`, `IsVisible` when `SelectedItem` not null). The grid: a `ScrollViewer` containing an `ItemsControl` whose `ItemsPanel` is a virtualizing panel — use Avalonia's `ItemsRepeater` with a `UniformGridLayout` (ItemWidth/ItemHeight ~140) OR an `ItemsControl` with `<VirtualizingStackPanel>`-backed wrapping; confirm the 11.2 approach via docs. Each tile: a `Button` (or selectable Border) showing the `Thumbnail` `Image` + `#{Number}`, setting `SelectedItem` on click. Detail rail: `SelectedNumber`, `SelectedRecipe`, `SelectedDna` (mono, wrap), and a rarity `ItemsControl` over `SelectedRarity` (`Trait_type` · `Value` · `RarityPct%`). Token/foundation styles; `x:DataType` on templates; no raw hex.
- [ ] **Step 3:** Run — SmokeTests passes (view resolves); `dotnet build src/Nfty.Desktop --nologo` 0 warnings.
- [ ] **Step 4: Commit** `feat(gui): SetBrowserView (virtualized grid + item detail)`

**Doc-pull (objective):** before Step 2, confirm the Avalonia 11.2 virtualized-grid approach (`ItemsRepeater`+`UniformGridLayout` vs a virtualizing `ItemsControl`) — Context7 `/avaloniaui/avalonia-docs` "ItemsRepeater UniformGridLayout virtualization". Pick the one that virtualizes a large item set; note the choice.

---

### Task 4: Wire Landing "Open a cooked .set…"

**Files:** Modify `src/Nfty.App/ViewModels/LandingViewModel.cs`, `src/Nfty.App/ServiceRegistration.cs`; Test `tests/Nfty.App.Tests/LandingOpenSetTests.cs`.

**Interfaces:** DI `Func<LoadedSet, SetBrowserViewModel> setBrowserFactory`; `LandingViewModel.OpenSet` enabled, reads + navigates.

- [ ] **Step 1: Failing test** — fake picker returns a real cooked `.set`/folder path → `OpenSetCommand` reads it and navigates to a `SetBrowserViewModel`; bad path → error dialog, no nav; null → no nav. Model on the existing `LandingOpenFlowTests` (its `StubPicker`, `FakeNav`, `FakeDialogs`, and the real `CookBookSession`). The `LandingViewModel` ctor gains `Func<LoadedSet, SetBrowserViewModel>`; construct it in the test as `s => new SetBrowserViewModel(s)`.
```csharp
    [AvaloniaFact]
    public async Task Open_set_reads_and_navigates_to_the_browser()
    {
        var dir = /* cook a tiny set to a temp folder (CoreTestBook + SetWriter) */;
        var nav = new FakeNav();
        var vm = MakeLanding(nav, picker: new StubPicker(dir));   // picker.OpenFileAsync → the folder/.set path
        await vm.OpenSetCommand.ExecuteAsync(null);
        Assert.IsType<SetBrowserViewModel>(nav.Current);
        Directory.Delete(dir, recursive: true);
    }
```
(Use whatever `MakeLanding` shape the existing Landing tests use; add the `setBrowserFactory` arg. If `StubPicker` returns its path from `OpenFileAsync`, that's what OpenSet calls.)

- [ ] **Step 2: Run — fails** (OpenSet is a disabled stub; ctor arity).
- [ ] **Step 3: Implement.**
  - `LandingViewModel`: ctor gains `Func<LoadedSet, SetBrowserViewModel> setBrowserFactory` (store it). Change `OpenSet` from `[RelayCommand(CanExecute = nameof(Never))] void OpenSet()` to an async command:
    ```csharp
    [RelayCommand]
    private async Task OpenSet()
    {
        var path = await _picker.OpenFileAsync("Open a cooked .set", ".set");
        if (path is null) return;
        LoadedSet set;
        try { set = SetReader.Read(path); }
        catch (Exception ex)
        {
            await _dialogs.ShowAsync<object>(new ErrorDialogViewModel(_dialogs, "Could not open the set", ex.Message));
            return;
        }
        _nav.To(_setBrowserFactory(set));
    }
    ```
    (Mirror the existing `OpenCookBook` method's picker/error/nav shape exactly — reuse its `_picker`/`_dialogs`/`_nav` fields.) Remove the `Never` guard.
  - `ServiceRegistration`: add `services.AddSingleton<Func<LoadedSet, SetBrowserViewModel>>(sp => set => new SetBrowserViewModel(set));` and extend the `LandingViewModel` registration (`AddTransient<LandingViewModel>`) — since Landing is `AddTransient`, the DI will inject the new ctor param automatically IF the factory is registered and the ctor takes it; confirm `LandingViewModel` is resolved by DI (it is) so adding the ctor param + registering the factory suffices. Update every manual `new LandingViewModel(...)` in tests to pass the factory. Grep: `grep -rn "new LandingViewModel(" tests`.
- [ ] **Step 4: Run — passes;** `dotnet build src/Nfty.Desktop --nologo` 0 warnings; whole App suite green.
- [ ] **Step 5: Commit** `feat(gui): Landing opens a cooked .set into the Set browser`

---

### Task 5: Visual capture + full verification + manual smoke

**Files:** Modify `tests/Nfty.App.Tests/VisualCapture.cs`.

- [ ] **Step 1:** Add a `Capture_set_browser` `[AvaloniaFact]` (guarded by `NFTY_CAPTURE`) that cooks a tiny set to a temp dir, reads it, and renders the real `SetBrowserView` (an item selected) in both themes → `set-browser-{v}.png`; dispose the VM + delete the temp dir.
- [ ] **Step 2:** Render + **view** the PNGs (Read tool) — confirm the thumbnail grid + selected item detail (number/dna/recipe/rarity) read cleanly in both themes; iterate on layout if needed. Report what you saw.
- [ ] **Step 3:** `dotnet build nfty.sln --nologo` 0 warnings; `dotnet test nfty.sln --nologo` all pass (report totals); `grep -rniE "#[0-9a-f]{6}" src/Nfty.App/Views/SetBrowserView.axaml` → no raw hex.
- [ ] **Step 4: Manual smoke (user):** `dotnet run --project src/Nfty.Desktop`; on Landing click **Open a cooked .set…** → pick a `.set` you cooked earlier → the browser shows the thumbnail grid; click a tile → detail; Back returns to Landing (the temp dir is cleaned up).
- [ ] **Step 5:** Commit `test(gui): render the Set browser for visual verification` (only if capture-driven fixups were needed, commit those too).

---

## Self-Review
- **Spec coverage:** §2.1 SetReader+LoadedSet (folder + .set + temp cleanup + missing set.json) → T1. §2.2 SetBrowserViewModel (header/items/thumbnails/detail/dispose) → T2. §2.3 SetBrowserView (virtualized grid + detail) → T3. §2.4 Landing OpenSet + factory → T4. §4 tests → T1 (Core round-trip), T2 (VM), T4 (Landing flow), T5 (visual). §6 risks (virtualization, DecodeToWidth, temp lifetime) → doc-pull notes in T2/T3 + the temp-cleanup test in T1.
- **Placeholder scan:** T1/T2 carry full code; T3/T4 give structure + concrete bindings + the SmokeTests/flow gates; the two doc-pull notes (DecodeToWidth, virtualization) are concrete verification steps with fallbacks, not TBDs. The `CoreTestBook.Tiny()` helper is introduced in T2 (copied from T1's `TinyBook()`); T3/T4/T5 reuse it.
- **Type consistency:** `LoadedSet`/`SetItem`/`SetReader.Read` (T1) consumed unchanged in T2/T4; `SetBrowserViewModel(LoadedSet)`/`SetItemRow`/`SelectedItem`/`SelectedDna` (T2) used in T3/T5; `Func<LoadedSet, SetBrowserViewModel>` (T4) matches; `NftyMetadata.SetNumber/Dna/Recipe/Rarity/Layers`, `SetManifest.Name/Count/Seed`, `RarityAttribute.Trait_type/Value/RarityPct`, `LayerColor` match Core.
