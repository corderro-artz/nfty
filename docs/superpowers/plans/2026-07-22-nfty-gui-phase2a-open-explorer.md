# nfty GUI Phase 2a — Open/Import CookBook → Explorer (Implementation Plan)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Landing's `Open CookBook` / `.cbk` `Import` actually read a cookbook via `Nfty.Core`, own its lifetime, and navigate into the Explorer with the tree + all three detail views bound to real data.

**Architecture:** A real desktop `IFilePickerService` over Avalonia `StorageProvider` (registered in the head); an `ICookBookSession` singleton that owns the open `LoadedCookBook` and disposes the previous on reopen; `LandingViewModel` reads → `session.Open` → navigates to an `ExplorerViewModel` built from the book via a DI factory; the Explorer builds its tree from `book.Recipes` and constructs real-data detail VMs that compute metrics with `UniqueSpace`/`RarityCalculator`. Read failures show a reusable error dialog. Variant images are deferred (no bitmap bridge yet).

**Tech Stack:** .NET 10, Avalonia 11.2.3, CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection, xUnit + Avalonia.Headless.XUnit. `Nfty.Core` engine (unchanged).

**Reference spec:** `docs/superpowers/specs/2026-07-22-nfty-gui-phase2a-open-explorer-design.md`.

## Global Constraints

- `Nfty.App` stays **head-agnostic** (no desktop-only APIs); the real file picker lives in `Nfty.Desktop`. net10.0 throughout.
- MVVM = CommunityToolkit.Mvvm over `ViewModelBase : ObservableObject`; DI via Microsoft.Extensions.DependencyInjection; convention `ViewLocator`.
- **Views use `{DynamicResource ...Brush}` token brushes** — no raw hex. This slice is **functional binding only**; the mockup-faithful visual pass is separate (spec §6 note).
- **`ICookBookSession` is the single owner** of the open `LoadedCookBook` (which owns every decoded PNG). It disposes the previous book on `Open`. No ViewModel disposes the book. **No variant images are decoded/rendered this slice** (deferred).
- **No `Nfty.Core` change.** Reads use `CookBookArchive.Read`, `Archives.KindOf`, `UniqueSpace.Count`, `RarityCalculator.Compute` only.
- Pure VM/service tests use `[Fact]`; anything constructing Avalonia controls uses `[AvaloniaFact]`. Build 0 warnings. Conventional commits ending with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. Ordinal id comparisons.

**Core API reference (verified; use these exact shapes):**
- `CookBookArchive.Read(string path) : LoadedCookBook` (throws `UnsupportedSchemaVersionException`/`InvalidDataException`/IO on bad input).
- `LoadedCookBook { CookBookManifest Manifest; IReadOnlyList<LoadedRecipe> Recipes; string? SourceSha256 } : IDisposable`.
- `LoadedRecipe { RecipeManifest Manifest; IReadOnlyList<LoadedIngredient> Ingredients } : IDisposable`.
- `LoadedIngredient { IngredientManifest Manifest; IReadOnlyDictionary<string, Image<Rgba32>> VariantImages } : IDisposable`.
- `CookBookManifest(string Id, string Name, Dimensions Canvas, Collection Collection, IReadOnlyDictionary<string,double> RecipeWeights, int SchemaVersion)`; `Collection(string Name, string Description, string Symbol)`; `Dimensions(int Width, int Height)`.
- `RecipeManifest(string Id, string Name, IReadOnlyList<string> LayerOrder, IReadOnlyList<IncompatibilityRule> Rules, int SchemaVersion)`.
- `IngredientManifest(string Id, string Name, LayerKind Kind, Colorization? Colorization, IReadOnlyList<Variant> Variants, int SchemaVersion)`; `Variant(string Id, string Name, double Weight)`; `LayerKind { Static, Dynamic, Custom }`.
- `Nfty.Core.Generation.UniqueSpace.Count(LoadedCookBook, long cap = UniqueSpace.DefaultCap) : UniqueSpaceCount`; `UniqueSpaceCount(long Total, bool IsExact, long Cap, IReadOnlyDictionary<string,RecipeSpace> Recipes)`; `RecipeSpace(long Total, long Combos, bool IsExact)`; indexer `count[recipeId]`.
- `Nfty.Core.Stats.RarityCalculator.Compute(LoadedCookBook) : RarityReport`; `RarityReport(IReadOnlyList<RecipeOdds> Recipes, IReadOnlyList<TraitOdds> Traits)`; `RecipeOdds(string RecipeId, string RecipeName, double Percent)`; `TraitOdds(string RecipeId, string RecipeName, string IngredientId, string IngredientName, string VariantId, string VariantName, double WithinRecipePercent, double OverallPercent)`.
- `Nfty.Core.Formats.Archives.KindOf(string path) : ArchiveKind { CookBook, Recipe, Ingredient }` (throws `NotSupportedException` on unknown extension).
- `CookBookArchive.Write(string path, CookBookManifest, IReadOnlyList<LoadedRecipe>)` (tests build fixtures with this).

---

## File Structure

**Create:**
- `src/Nfty.App/Services/ICookBookSession.cs` (+ `CookBookSession`) — owns the open book.
- `src/Nfty.App/ViewModels/ErrorDialogViewModel.cs`, `src/Nfty.App/Views/ErrorDialogView.axaml`(+`.cs`).
- `src/Nfty.Desktop/DesktopFilePicker.cs` — real `IFilePickerService` over `StorageProvider`.
- Test files under `tests/Nfty.App.Tests/` (per task).

**Modify:**
- `src/Nfty.App/Models/ExplorerNode.cs` — carry the domain object.
- `src/Nfty.App/ViewModels/ExplorerViewModel.cs` — take `LoadedCookBook`, build tree, construct real detail VMs.
- `src/Nfty.App/ViewModels/CookBookDetailViewModel.cs`, `RecipeDetailViewModel.cs`, `IngredientDetailViewModel.cs` — real data.
- `src/Nfty.App/ViewModels/LandingViewModel.cs` — Open/Import flow.
- `src/Nfty.App/ServiceRegistration.cs` — register session, the Explorer factory; remove `AddTransient<ExplorerViewModel>`.
- `src/Nfty.Desktop/App.axaml.cs` — register `DesktopFilePicker` (override).
- `src/Nfty.App/Views/ExplorerView.axaml`, `CookBookDetailView.axaml`, `RecipeDetailView.axaml`, `IngredientDetailView.axaml` — bind the real-data properties.
- Existing tests that construct these VMs (`SmokeTests`, `WiringCoverageTests`, `ExplorerViewModelTests`, `ExplorerDetailTests`, `LandingViewModelTests`) — updated to the new constructors.

---

## Task 1: `ICookBookSession` — the open-book lifetime owner

**Files:**
- Create: `src/Nfty.App/Services/ICookBookSession.cs`
- Modify: `src/Nfty.App/ServiceRegistration.cs`
- Test: `tests/Nfty.App.Tests/CookBookSessionTests.cs`

**Interfaces:**
- Produces: `ICookBookSession { LoadedCookBook? Current; event Action? Changed; void Open(LoadedCookBook); void Close(); } : IDisposable` and `CookBookSession`.

- [ ] **Step 1: Write the failing test** (`CookBookSessionTests.cs`)

Builds two tiny in-memory `LoadedCookBook`s and asserts opening the second disposes the first.
```csharp
using Nfty.App.Services;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public class CookBookSessionTests
{
    private static (LoadedCookBook book, Image<Rgba32> variantImage) MiniBook(string id)
    {
        var img = new Image<Rgba32>(4, 4, new Rgba32(120, 120, 120, 255));
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "BG", LayerKind.Custom, null, new[] { new Variant("a", "A", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["a"] = img },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "bg" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest(id, id, new Dimensions(4, 4),
                new Collection(id, "", "X"), new Dictionary<string, double> { ["cat"] = 1 }),
            Recipes = new[] { recipe },
        };
        return (book, img);
    }

    [Fact]
    public void Opening_a_second_book_disposes_the_first()
    {
        var session = new CookBookSession();
        var (a, aImg) = MiniBook("A");
        var (b, _) = MiniBook("B");
        session.Open(a);
        session.Open(b);
        Assert.Same(b, session.Current);
        Assert.Throws<ObjectDisposedException>(() => aImg.ProcessPixelRows(_ => { }));  // A's image freed
        session.Dispose();
    }

    [Fact]
    public void Close_disposes_and_clears_and_raises_changed()
    {
        var session = new CookBookSession();
        var (a, _) = MiniBook("A");
        int changes = 0; session.Changed += () => changes++;
        session.Open(a);
        session.Close();
        Assert.Null(session.Current);
        Assert.Equal(2, changes);   // open + close
        session.Dispose();
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Nfty.App.Tests --filter "FullyQualifiedName~CookBookSessionTests" --nologo`
Expected: FAIL — `ICookBookSession`/`CookBookSession` don't exist.

- [ ] **Step 3: Implement**

`src/Nfty.App/Services/ICookBookSession.cs`:
```csharp
using Nfty.Core.Formats;

namespace Nfty.App.Services;

/// <summary>
/// Owns the currently-open CookBook. A <see cref="LoadedCookBook"/> holds every decoded variant image,
/// so this is the single place that frees them: <see cref="Open"/> disposes the previous book before
/// swapping. Registered as a singleton and disposed at shutdown. No ViewModel disposes the book.
/// </summary>
public interface ICookBookSession : IDisposable
{
    LoadedCookBook? Current { get; }
    event Action? Changed;
    void Open(LoadedCookBook book);
    void Close();
}

public sealed class CookBookSession : ICookBookSession
{
    private LoadedCookBook? _current;
    public LoadedCookBook? Current => _current;
    public event Action? Changed;

    public void Open(LoadedCookBook book)
    {
        if (ReferenceEquals(_current, book)) return;
        _current?.Dispose();
        _current = book;
        Changed?.Invoke();
    }

    public void Close()
    {
        if (_current is null) return;
        _current.Dispose();
        _current = null;
        Changed?.Invoke();
    }

    public void Dispose() => _current?.Dispose();
}
```

- [ ] **Step 4: Register the session** — in `ServiceRegistration.AddNftyApp`, add:
```csharp
        services.AddSingleton<ICookBookSession, CookBookSession>();
```

- [ ] **Step 5: Run the tests** → PASS (2).

- [ ] **Step 6: Commit**
```bash
git add src/Nfty.App/Services/ICookBookSession.cs src/Nfty.App/ServiceRegistration.cs tests/Nfty.App.Tests/CookBookSessionTests.cs
git commit -m "$(printf 'feat(gui): ICookBookSession owns the open cookbook lifetime\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

## Task 2: Explorer builds a real tree from a LoadedCookBook

**Files:**
- Modify: `src/Nfty.App/Models/ExplorerNode.cs`, `src/Nfty.App/ViewModels/ExplorerViewModel.cs`, `src/Nfty.App/ServiceRegistration.cs`
- Modify (tests): `tests/Nfty.App.Tests/ExplorerViewModelTests.cs`, `tests/Nfty.App.Tests/SmokeTests.cs`, `tests/Nfty.App.Tests/WiringCoverageTests.cs`
- Test: extend `ExplorerViewModelTests.cs`

**Interfaces:**
- Consumes: `LoadedCookBook`/`LoadedRecipe`/`LoadedIngredient`.
- Produces: `record ExplorerNode(string Id, string Name, ExplorerNodeKind Kind, IReadOnlyList<ExplorerNode> Children, object? Domain)` (Domain carries the `LoadedCookBook`/`LoadedRecipe`/`LoadedIngredient`); `ExplorerViewModel(LoadedCookBook book, INavigationService, IDialogService, INotYetWired)`; a DI factory `Func<LoadedCookBook, ExplorerViewModel>`.

This task keeps the detail VMs as their current Phase-1 placeholders (Tasks 3–5 evolve them); it only reshapes the tree source and the ExplorerViewModel constructor. So the `CurrentDetail` switch in `OnSelectedNodeChanged` stays constructing the existing placeholder VMs for now (Tasks 3–5 replace each).

- [ ] **Step 1: Write the failing test** — add to `ExplorerViewModelTests.cs`:
```csharp
    private static LoadedCookBook TwoRecipeBook()
    {
        LoadedIngredient Ing(string id) => new()
        {
            Manifest = new IngredientManifest(id, id, LayerKind.Custom, null, new[] { new Variant("a", "A", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["a"] = new Image<Rgba32>(4, 4) },
        };
        LoadedRecipe Rec(string id, params string[] layers) => new()
        {
            Manifest = new RecipeManifest(id, id, layers, Array.Empty<IncompatibilityRule>()),
            Ingredients = layers.Select(Ing).ToArray(),
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "VaporPets", new Dimensions(8, 8),
                new Collection("VaporPets", "", "VP"),
                new Dictionary<string, double> { ["cat"] = 1, ["dog"] = 1 }),
            Recipes = new[] { Rec("cat", "bg", "aura"), Rec("dog", "body") },
        };
    }

    [Fact]
    public void Tree_is_built_from_the_cookbook_recipes_and_ingredients()
    {
        var vm = new ExplorerViewModel(TwoRecipeBook(), new FakeNav(), new FakeDialogs(), new FakeNotYetWired());
        Assert.Equal(ExplorerNodeKind.CookBook, vm.Root.Kind);
        Assert.Equal("VaporPets", vm.Root.Name);
        Assert.Equal(new[] { "cat", "dog" }, vm.Root.Children.Select(c => c.Id));
        Assert.Equal(new[] { "bg", "aura" }, vm.Root.Children[0].Children.Select(c => c.Id));
        Assert.All(vm.Root.Children, r => Assert.Equal(ExplorerNodeKind.Recipe, r.Kind));
    }
```
Remove the old Phase-1 sample-tree test method (`Add_label_tracks_the_selected_node_kind` still works since it constructs nodes directly — keep it but update those `new ExplorerNode(...)` calls to pass a `null` Domain arg; see Step 3). Update the other existing `ExplorerViewModelTests` methods that construct `ExplorerViewModel` via a `Make(out ...)` helper: change `Make` to build with `TwoRecipeBook()`.

- [ ] **Step 2: Run to verify failure**
Run: `dotnet test tests/Nfty.App.Tests --filter "FullyQualifiedName~ExplorerViewModelTests" --nologo`
Expected: FAIL — `ExplorerViewModel` has no `LoadedCookBook` ctor; `ExplorerNode` has no `Domain`.

- [ ] **Step 3: Implement**

`src/Nfty.App/Models/ExplorerNode.cs`:
```csharp
namespace Nfty.App.Models;

public enum ExplorerNodeKind { CookBook, Recipe, Ingredient }

/// <summary>One tree node. <see cref="Domain"/> carries the Core object this node stands for
/// (LoadedCookBook / LoadedRecipe / LoadedIngredient) so the detail views can bind real data.</summary>
public record ExplorerNode(string Id, string Name, ExplorerNodeKind Kind,
    IReadOnlyList<ExplorerNode> Children, object? Domain);
```

`src/Nfty.App/ViewModels/ExplorerViewModel.cs` — replace the sample `Root` and constructor. Keep the toolbar commands (`ToggleLock`/`Search`/`Add`/`DeleteSelected`/`Import`/`SelectNode`/`OpenIngredient`) exactly as they are; only change the ctor + `Root` + keep `CurrentDetail` building the existing placeholder detail VMs (Tasks 3–5 evolve them):
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Models;
using Nfty.App.Services;
using Nfty.Core.Formats;

namespace Nfty.App.ViewModels;

public partial class ExplorerViewModel : ViewModelBase
{
    private readonly INavigationService _nav;
    private readonly IDialogService _dialogs;
    private readonly INotYetWired _notify;
    private readonly LoadedCookBook _book;

    [ObservableProperty] private ExplorerNode? _selectedNode;
    [ObservableProperty] private ViewModelBase? _currentDetail;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    private bool _isEditing;

    public ExplorerNode Root { get; }
    public IReadOnlyList<ExplorerNode> Roots => new[] { Root };

    public string AddLabel => SelectedNode?.Kind switch
    {
        ExplorerNodeKind.CookBook => "Add recipe",
        ExplorerNodeKind.Recipe => "Add ingredient",
        ExplorerNodeKind.Ingredient => "Add variant",
        _ => "Add",
    };

    public ExplorerViewModel(LoadedCookBook book, INavigationService nav, IDialogService dialogs, INotYetWired notify)
    {
        _book = book; _nav = nav; _dialogs = dialogs; _notify = notify;
        Root = BuildTree(book);
    }

    private static ExplorerNode BuildTree(LoadedCookBook book)
    {
        var recipeNodes = book.Recipes.Select(r =>
        {
            var ingById = r.Ingredients.ToDictionary(i => i.Manifest.Id, StringComparer.Ordinal);
            var ingNodes = r.Manifest.LayerOrder
                .Where(ingById.ContainsKey)
                .Select(id => new ExplorerNode(id, ingById[id].Manifest.Name,
                    ExplorerNodeKind.Ingredient, Array.Empty<ExplorerNode>(), ingById[id]))
                .ToList();
            return new ExplorerNode(r.Manifest.Id, r.Manifest.Name, ExplorerNodeKind.Recipe, ingNodes, r);
        }).ToList();
        return new ExplorerNode(book.Manifest.Id, book.Manifest.Name, ExplorerNodeKind.CookBook, recipeNodes, book);
    }

    partial void OnSelectedNodeChanged(ExplorerNode? value)
    {
        OnPropertyChanged(nameof(AddLabel));
        CurrentDetail = value?.Kind switch
        {
            ExplorerNodeKind.CookBook => new CookBookDetailViewModel(_notify),
            ExplorerNodeKind.Recipe => new RecipeDetailViewModel(_notify, id => OpenIngredientCommand.Execute(id)),
            ExplorerNodeKind.Ingredient => new IngredientDetailViewModel(_notify,
                () => _notify.Report("Edit ingredient"), () => IsEditing),
            _ => null,
        };
    }

    [RelayCommand] private void ToggleLock() => IsEditing = !IsEditing;
    [RelayCommand] private void Search() => _notify.Report("Search (⌘K)");
    [RelayCommand] private void Add() => _notify.Report(AddLabel);
    [RelayCommand(CanExecute = nameof(CanEdit))] private void DeleteSelected() => _notify.Report("Delete");
    [RelayCommand] private void Import() => _notify.Report("Import");
    [RelayCommand] private void SelectNode(ExplorerNode node) => SelectedNode = node;
    [RelayCommand] private void OpenIngredient(string id) => _notify.Report($"Open ingredient {id}");
    private bool CanEdit() => IsEditing;
}
```
(Tasks 3–5 change the three `new …DetailViewModel(...)` construction lines to pass real Core objects.)

`ServiceRegistration.cs`: remove `services.AddTransient<ExplorerViewModel>();` and add the factory:
```csharp
        services.AddSingleton<Func<LoadedCookBook, ExplorerViewModel>>(sp =>
            book => new ExplorerViewModel(book,
                sp.GetRequiredService<INavigationService>(),
                sp.GetRequiredService<IDialogService>(),
                sp.GetRequiredService<INotYetWired>()));
```
(add `using Nfty.Core.Formats;` and `using System;` if needed).

Update `tests/Nfty.App.Tests/SmokeTests.cs` and `WiringCoverageTests.cs`: everywhere they do `new ExplorerViewModel(nav, dialogs, notify)`, change to `new ExplorerViewModel(ExplorerViewModelTests.TwoRecipeBook(), nav, dialogs, notify)` — make `TwoRecipeBook()` `internal static` so tests share it. Any `new ExplorerNode(...)` in tests gets a trailing `, null` Domain arg.

- [ ] **Step 4: Run the tests**
Run: `dotnet test tests/Nfty.App.Tests --nologo`
Expected: PASS (all — the new tree test + updated existing tests + smoke/coverage).

- [ ] **Step 5: Commit**
```bash
git add src/Nfty.App/Models/ExplorerNode.cs src/Nfty.App/ViewModels/ExplorerViewModel.cs src/Nfty.App/ServiceRegistration.cs tests/Nfty.App.Tests
git commit -m "$(printf 'feat(gui): Explorer builds its tree from a real LoadedCookBook\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

## Task 3: CookBook detail — real data

**Files:**
- Modify: `src/Nfty.App/ViewModels/CookBookDetailViewModel.cs`, `src/Nfty.App/ViewModels/ExplorerViewModel.cs` (its construction line)
- Test: `tests/Nfty.App.Tests/CookBookDetailViewModelTests.cs`

**Interfaces:**
- Produces: `CookBookDetailViewModel(LoadedCookBook book, INotYetWired notify)` exposing `Name`, `Symbol`, `CanvasText` (`"WxH"`), `RecipeCount`, `LayerCount`, `VariantCount`, `UniqueDnaText`, `Recipes` (`IReadOnlyList<RecipeShareRow>`), and the `Cook` stub command. `record RecipeShareRow(string Name, double SharePercent, string DnaSpaceText)`.

- [ ] **Step 1: Write the failing test**
```csharp
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public class CookBookDetailViewModelTests
{
    [Fact]
    public void Exposes_identity_counts_and_unique_dna()
    {
        var book = ExplorerViewModelTests.TwoRecipeBook();   // cat[bg,aura]+dog[body], custom kind, 1 variant each
        var vm = new CookBookDetailViewModel(book, new FakeNotYetWired());
        Assert.Equal("VaporPets", vm.Name);
        Assert.Equal("8x8", vm.CanvasText);
        Assert.Equal(2, vm.RecipeCount);
        Assert.Equal(3, vm.LayerCount);      // bg, aura, body
        Assert.Equal(3, vm.VariantCount);    // one variant each
        Assert.Equal(2, vm.Recipes.Count);
        Assert.Contains(vm.Recipes, r => r.Name == "cat");
        // custom-only, single variants → unique DNA space is small and exact
        Assert.False(string.IsNullOrEmpty(vm.UniqueDnaText));
    }

    [Fact]
    public void Cook_still_reports_not_yet_wired()
    {
        var n = new FakeNotYetWired();
        new CookBookDetailViewModel(ExplorerViewModelTests.TwoRecipeBook(), n).CookCommand.Execute(null);
        Assert.Equal("Cook", n.Last);
    }
}
```

- [ ] **Step 2: Run to verify failure** → FAIL (ctor mismatch).

- [ ] **Step 3: Implement**

`src/Nfty.App/ViewModels/CookBookDetailViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.Core.Formats;
using Nfty.Core.Generation;

namespace Nfty.App.ViewModels;

public record RecipeShareRow(string Name, double SharePercent, string DnaSpaceText);

public partial class CookBookDetailViewModel : ViewModelBase
{
    private readonly INotYetWired _notify;

    public string Name { get; }
    public string Symbol { get; }
    public string CanvasText { get; }
    public int RecipeCount { get; }
    public int LayerCount { get; }
    public int VariantCount { get; }
    public string UniqueDnaText { get; }
    public IReadOnlyList<RecipeShareRow> Recipes { get; }

    public CookBookDetailViewModel(LoadedCookBook book, INotYetWired notify)
    {
        _notify = notify;
        Name = book.Manifest.Name;
        Symbol = book.Manifest.Collection.Symbol;
        CanvasText = $"{book.Manifest.Canvas.Width}x{book.Manifest.Canvas.Height}";
        RecipeCount = book.Recipes.Count;
        LayerCount = book.Recipes.Sum(r => r.Ingredients.Count);
        VariantCount = book.Recipes.Sum(r => r.Ingredients.Sum(i => i.Manifest.Variants.Count));

        var space = UniqueSpace.Count(book);
        UniqueDnaText = space.IsExact ? space.Total.ToString() : $"more than {space.Total}";

        double totalWeight = book.Manifest.RecipeWeights.Values.Sum();
        Recipes = book.Recipes.Select(r =>
        {
            double w = book.Manifest.RecipeWeights.GetValueOrDefault(r.Manifest.Id);
            double share = totalWeight > 0 ? w / totalWeight * 100 : 0;
            var rs = space[r.Manifest.Id];
            string dna = rs.IsExact ? rs.Total.ToString() : $"more than {rs.Total}";
            return new RecipeShareRow(r.Manifest.Name, Math.Round(share, 1), dna);
        }).ToList();
    }

    [RelayCommand] private void Cook() => _notify.Report("Cook");
}
```

In `ExplorerViewModel.OnSelectedNodeChanged`, change the CookBook branch to:
```csharp
            ExplorerNodeKind.CookBook => new CookBookDetailViewModel(_book, _notify),
```

- [ ] **Step 4: Run the tests** → PASS (2). Then full suite green.

- [ ] **Step 5: Commit**
```bash
git add src/Nfty.App/ViewModels/CookBookDetailViewModel.cs src/Nfty.App/ViewModels/ExplorerViewModel.cs tests/Nfty.App.Tests/CookBookDetailViewModelTests.cs
git commit -m "$(printf 'feat(gui): CookBook detail bound to real data (counts, unique-DNA, mix)\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

## Task 4: Recipe detail — real data

**Files:**
- Modify: `src/Nfty.App/ViewModels/RecipeDetailViewModel.cs`, `ExplorerViewModel.cs` (its construction line)
- Modify (tests): `tests/Nfty.App.Tests/ExplorerDetailTests.cs`
- Test: `tests/Nfty.App.Tests/RecipeDetailViewModelTests.cs`

**Interfaces:**
- Produces: `RecipeDetailViewModel(LoadedRecipe recipe, LoadedCookBook book, INotYetWired notify, Action<string> openIngredient)` exposing `Name`, `Layers` (`IReadOnlyList<LayerRow>`), `Rules` (`IReadOnlyList<RuleRow>`), `RollSeed`, and `Reroll`/`OpenIngredient` commands. `record LayerRow(int Index, string Layer, string Kind, int VariantCount)`; `record RuleRow(string Text)`.

- [ ] **Step 1: Write the failing test**
```csharp
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

public class RecipeDetailViewModelTests
{
    [Fact]
    public void Layer_table_follows_layer_order()
    {
        var book = ExplorerViewModelTests.TwoRecipeBook();
        var cat = book.Recipes.First(r => r.Manifest.Id == "cat");
        var vm = new RecipeDetailViewModel(cat, book, new FakeNotYetWired(), _ => { });
        Assert.Equal(new[] { "bg", "aura" }, vm.Layers.Select(l => l.Layer));
        Assert.All(vm.Layers, l => Assert.Equal(1, l.VariantCount));
        Assert.Empty(vm.Rules);   // TwoRecipeBook has no rules
    }

    [Fact]
    public void Reroll_changes_the_roll_seed_and_open_ingredient_invokes_callback()
    {
        var book = ExplorerViewModelTests.TwoRecipeBook();
        var cat = book.Recipes.First(r => r.Manifest.Id == "cat");
        string? opened = null;
        var vm = new RecipeDetailViewModel(cat, book, new FakeNotYetWired(), id => opened = id);
        var before = vm.RollSeed; vm.RerollCommand.Execute(null); Assert.NotEqual(before, vm.RollSeed);
        vm.OpenIngredientCommand.Execute("aura"); Assert.Equal("aura", opened);
    }
}
```
Update `ExplorerDetailTests.cs`: its `RecipeDetailViewModel` construction (`new RecipeDetailViewModel(new FakeNotYetWired(), _ => { })`) becomes the new 4-arg form using `ExplorerViewModelTests.TwoRecipeBook()`'s `cat` recipe.

- [ ] **Step 2: Run to verify failure** → FAIL.

- [ ] **Step 3: Implement**

`src/Nfty.App/ViewModels/RecipeDetailViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.Core.Formats;
using Nfty.Core.Model;

namespace Nfty.App.ViewModels;

public record LayerRow(int Index, string Layer, string Kind, int VariantCount);
public record RuleRow(string Text);

public partial class RecipeDetailViewModel : ViewModelBase
{
    private readonly INotYetWired _notify;
    private readonly Action<string> _openIngredient;
    [ObservableProperty] private int _rollSeed = 1;

    public string Name { get; }
    public IReadOnlyList<LayerRow> Layers { get; }
    public IReadOnlyList<RuleRow> Rules { get; }

    public RecipeDetailViewModel(LoadedRecipe recipe, LoadedCookBook book, INotYetWired notify, Action<string> openIngredient)
    {
        _notify = notify; _openIngredient = openIngredient;
        Name = recipe.Manifest.Name;

        var ingById = recipe.Ingredients.ToDictionary(i => i.Manifest.Id, StringComparer.Ordinal);
        Layers = recipe.Manifest.LayerOrder
            .Where(ingById.ContainsKey)
            .Select((id, i) => new LayerRow(i + 1, ingById[id].Manifest.Name,
                ingById[id].Manifest.Kind.ToString(), ingById[id].Manifest.Variants.Count))
            .ToList();

        Rules = recipe.Manifest.Rules.Select(RuleText).ToList();
    }

    private static RuleRow RuleText(IncompatibilityRule rule)
    {
        string op = rule.Type == RuleType.Exclude ? "✕ never with" : "→ always with";
        string targets = string.Join(", ", rule.Targets.Select(t => $"{t.IngredientId}:{t.VariantId}"));
        return new RuleRow($"{rule.When.IngredientId}:{rule.When.VariantId}  {op}  {targets}");
    }

    [RelayCommand] private void Reroll() => RollSeed++;
    [RelayCommand] private void OpenIngredient(string id) => _openIngredient(id);
}
```
In `ExplorerViewModel.OnSelectedNodeChanged`, change the Recipe branch to resolve the node's recipe:
```csharp
            ExplorerNodeKind.Recipe => new RecipeDetailViewModel((LoadedRecipe)value!.Domain!, _book, _notify,
                id => OpenIngredientCommand.Execute(id)),
```

- [ ] **Step 4: Run the tests** → PASS. Full suite green.

- [ ] **Step 5: Commit**
```bash
git add src/Nfty.App/ViewModels/RecipeDetailViewModel.cs src/Nfty.App/ViewModels/ExplorerViewModel.cs tests/Nfty.App.Tests/RecipeDetailViewModelTests.cs tests/Nfty.App.Tests/ExplorerDetailTests.cs
git commit -m "$(printf 'feat(gui): Recipe detail bound to real data (layer table, rules)\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

## Task 5: Ingredient detail — real data (rarity)

**Files:**
- Modify: `src/Nfty.App/ViewModels/IngredientDetailViewModel.cs`, `ExplorerViewModel.cs` (its construction line)
- Modify (tests): `tests/Nfty.App.Tests/ExplorerDetailTests.cs`
- Test: `tests/Nfty.App.Tests/IngredientDetailViewModelTests.cs`

**Interfaces:**
- Produces: `IngredientDetailViewModel(LoadedIngredient ing, LoadedRecipe recipe, LoadedCookBook book, INotYetWired notify, Action editIngredient, Func<bool> isEditing)` exposing `Name`, `KindText`, `ColorwaysText`, `Variants` (`IReadOnlyList<VariantRow>`), `SortColumn`, and commands `SortBy`/`SelectVariant`/`DeleteVariant`/`JumpToRules`/`EditIngredient` + `RaiseCanExecuteChanged()`. `record VariantRow(string Name, double Weight, double WithinPercent, double OverallPercent)`.

- [ ] **Step 1: Write the failing test**

Builds a book with known weights so rarity is checkable.
```csharp
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public class IngredientDetailViewModelTests
{
    private static (LoadedCookBook book, LoadedRecipe recipe, LoadedIngredient ing) Fixture()
    {
        LoadedIngredient Ing(string id, params (string vid, string name, double w)[] vs) => new()
        {
            Manifest = new IngredientManifest(id, id, LayerKind.Custom, null,
                vs.Select(v => new Variant(v.vid, v.name, v.w)).ToArray()),
            VariantImages = vs.ToDictionary(v => v.vid, _ => new Image<Rgba32>(4, 4)),
        };
        var aura = Ing("aura", ("glow", "Glow", 3), ("spark", "Spark", 1));   // 75% / 25% within
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "aura" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { aura },
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(4, 4),
                new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 }),
            Recipes = new[] { recipe },
        };
        return (book, recipe, aura);
    }

    [Fact]
    public void Variant_rows_carry_within_recipe_rarity()
    {
        var (book, recipe, ing) = Fixture();
        var vm = new IngredientDetailViewModel(ing, recipe, book, new FakeNotYetWired(), () => { }, () => false);
        Assert.Equal(2, vm.Variants.Count);
        var glow = vm.Variants.Single(v => v.Name == "Glow");
        Assert.Equal(75.0, glow.WithinPercent, 1);   // 3/(3+1)
    }

    [Fact]
    public void Delete_variant_enabled_only_when_editing()
    {
        var (book, recipe, ing) = Fixture();
        bool editing = false;
        var vm = new IngredientDetailViewModel(ing, recipe, book, new FakeNotYetWired(), () => { }, () => editing);
        Assert.False(vm.DeleteVariantCommand.CanExecute(null));
        editing = true; vm.RaiseCanExecuteChanged();
        Assert.True(vm.DeleteVariantCommand.CanExecute(null));
    }
}
```
Update `ExplorerDetailTests.cs`: its `IngredientDetailViewModel` construction uses the new 6-arg form (reuse this fixture shape).

- [ ] **Step 2: Run to verify failure** → FAIL.

- [ ] **Step 3: Implement**

`src/Nfty.App/ViewModels/IngredientDetailViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Nfty.Core.Stats;

namespace Nfty.App.ViewModels;

public record VariantRow(string Name, double Weight, double WithinPercent, double OverallPercent);

public partial class IngredientDetailViewModel : ViewModelBase
{
    private readonly INotYetWired _notify;
    private readonly Action _editIngredient;
    private readonly Func<bool> _isEditing;
    [ObservableProperty] private string _sortColumn = "Variant";

    public string Name { get; }
    public string KindText { get; }
    public string ColorwaysText { get; }
    public IReadOnlyList<VariantRow> Variants { get; }

    public IngredientDetailViewModel(LoadedIngredient ing, LoadedRecipe recipe, LoadedCookBook book,
        INotYetWired notify, Action editIngredient, Func<bool> isEditing)
    {
        _notify = notify; _editIngredient = editIngredient; _isEditing = isEditing;
        Name = ing.Manifest.Name;
        KindText = ing.Manifest.Kind.ToString();
        ColorwaysText = Colorways(ing.Manifest);

        var traits = RarityCalculator.Compute(book).Traits
            .Where(t => t.RecipeId == recipe.Manifest.Id && t.IngredientId == ing.Manifest.Id)
            .ToDictionary(t => t.VariantId, StringComparer.Ordinal);

        Variants = ing.Manifest.Variants.Select(v =>
        {
            traits.TryGetValue(v.Id, out var t);
            return new VariantRow(v.Name, v.Weight,
                Math.Round(t?.WithinRecipePercent ?? 0, 1), Math.Round(t?.OverallPercent ?? 0, 1));
        }).ToList();
    }

    private static string Colorways(IngredientManifest m) => m.Kind switch
    {
        LayerKind.Dynamic => "HSV · rolled  (value ← value-map)",
        LayerKind.Static => "HSV · fixed  (value ← value-map)",
        _ => "no colorize · composited as-is",
    };

    public void RaiseCanExecuteChanged() => DeleteVariantCommand.NotifyCanExecuteChanged();

    [RelayCommand] private void SortBy(string col) => SortColumn = col;
    [RelayCommand] private void SelectVariant(string id) { /* ui-state: active variant */ }
    [RelayCommand(CanExecute = nameof(CanEdit))] private void DeleteVariant() => _notify.Report("Delete variant");
    [RelayCommand] private void JumpToRules() { /* nav within the recipe rail */ }
    [RelayCommand] private void EditIngredient() => _editIngredient();
    private bool CanEdit() => _isEditing();
}
```
In `ExplorerViewModel`, the Ingredient node must know its parent recipe. Update `BuildTree` to make each ingredient node carry a `(LoadedRecipe recipe, LoadedIngredient ing)` tuple as `Domain`, then the Ingredient branch:
```csharp
            ExplorerNodeKind.Ingredient => value!.Domain is (LoadedRecipe r, LoadedIngredient i)
                ? new IngredientDetailViewModel(i, r, _book, _notify, () => _notify.Report("Edit ingredient"), () => IsEditing)
                : null,
```
And in `BuildTree`, the ingredient node: `new ExplorerNode(id, ingById[id].Manifest.Name, ExplorerNodeKind.Ingredient, Array.Empty<ExplorerNode>(), (r, ingById[id]))` — where `r` is the current `LoadedRecipe`. (Recipe nodes keep `Domain = r`; CookBook node keeps `Domain = book`.)

- [ ] **Step 4: Run the tests** → PASS. Full suite green.

- [ ] **Step 5: Commit**
```bash
git add src/Nfty.App/ViewModels/IngredientDetailViewModel.cs src/Nfty.App/ViewModels/ExplorerViewModel.cs tests/Nfty.App.Tests/IngredientDetailViewModelTests.cs tests/Nfty.App.Tests/ExplorerDetailTests.cs
git commit -m "$(printf 'feat(gui): Ingredient detail bound to real data (variant table, rarity)\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

## Task 6: Error dialog

**Files:**
- Create: `src/Nfty.App/ViewModels/ErrorDialogViewModel.cs`, `src/Nfty.App/Views/ErrorDialogView.axaml`(+`.cs`)
- Test: `tests/Nfty.App.Tests/ErrorDialogViewModelTests.cs`

**Interfaces:**
- Produces: `ErrorDialogViewModel(IDialogService dialogs, string title, string message)` with `Title`, `Message`, and a `Close` command.

- [ ] **Step 1: Write the failing test**
```csharp
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

public class ErrorDialogViewModelTests
{
    [Fact]
    public void Close_clears_the_active_dialog()
    {
        var dialogs = new FakeDialogs();
        var vm = new ErrorDialogViewModel(dialogs, "Could not open", "bad archive");
        dialogs.ShowAsync<object>(vm);
        Assert.Equal("bad archive", vm.Message);
        vm.CloseCommand.Execute(null);
        Assert.Null(dialogs.Active);
    }
}
```

- [ ] **Step 2: Run to verify failure** → FAIL.

- [ ] **Step 3: Implement**

`src/Nfty.App/ViewModels/ErrorDialogViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

public partial class ErrorDialogViewModel : ViewModelBase
{
    private readonly IDialogService _dialogs;
    public string Title { get; }
    public string Message { get; }
    public ErrorDialogViewModel(IDialogService dialogs, string title, string message)
    { _dialogs = dialogs; Title = title; Message = message; }
    [RelayCommand] private void Close() => _dialogs.Close(null);
}
```
`src/Nfty.App/Views/ErrorDialogView.axaml`:
```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Nfty.App.ViewModels"
             x:Class="Nfty.App.Views.ErrorDialogView"
             x:DataType="vm:ErrorDialogViewModel">
  <UserControl.KeyBindings>
    <KeyBinding Gesture="Escape" Command="{Binding CloseCommand}" />
  </UserControl.KeyBindings>
  <Border Background="{DynamicResource PanelBrush}" CornerRadius="10" Padding="24" MaxWidth="420"
          BorderBrush="{DynamicResource LineStrongBrush}" BorderThickness="1">
    <StackPanel Spacing="10">
      <TextBlock Text="{Binding Title}" FontWeight="Bold" Foreground="{DynamicResource AccentTextBrush}" />
      <TextBlock Text="{Binding Message}" TextWrapping="Wrap" Classes="muted" />
      <Button Content="OK" Command="{Binding CloseCommand}" Classes="accent" HorizontalAlignment="Right" />
    </StackPanel>
  </Border>
</UserControl>
```
`ErrorDialogView.axaml.cs`: standard `AvaloniaXamlLoader.Load(this)` loader (same shape as `HelpView.axaml.cs`).

- [ ] **Step 4: Run the tests** → PASS.

- [ ] **Step 5: Commit**
```bash
git add src/Nfty.App/ViewModels/ErrorDialogViewModel.cs src/Nfty.App/Views/ErrorDialogView.axaml* tests/Nfty.App.Tests/ErrorDialogViewModelTests.cs
git commit -m "$(printf 'feat(gui): reusable error dialog\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

## Task 7: DesktopFilePicker (real StorageProvider impl)

**Files:**
- Create: `src/Nfty.Desktop/DesktopFilePicker.cs`
- Modify: `src/Nfty.Desktop/App.axaml.cs` (register it, overriding the stub)

**Interfaces:**
- Consumes: `IFilePickerService` (Nfty.App). Produces: `DesktopFilePicker : IFilePickerService` (real open; save stays stub this slice).

This is head-specific and needs a live window, so it is **manually smoke-tested** (Task 10), not unit-tested — the flow tests (Task 8) use a fake picker.

- [ ] **Step 1: Implement `DesktopFilePicker`**

`src/Nfty.Desktop/DesktopFilePicker.cs`:
```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Nfty.App.Services;

namespace Nfty.Desktop;

/// <summary>Real file picker over Avalonia's StorageProvider. Head-specific: it needs the window's
/// TopLevel. Save is not exercised this slice, so it stays a null stub.</summary>
public sealed class DesktopFilePicker : IFilePickerService
{
    public async Task<string?> OpenFileAsync(string title, params string[] extensions)
    {
        var top = TopLevel;
        if (top is null) return null;

        var filter = new FilePickerFileType("nfty")
        {
            Patterns = extensions.Select(e => "*" + (e.StartsWith('.') ? e : "." + e)).ToArray(),
        };
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { filter },
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public Task<string?> SaveFileAsync(string title, string defaultExtension) => Task.FromResult<string?>(null);

    private static TopLevel? TopLevel =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
```

- [ ] **Step 2: Register it in the Desktop head** — in `src/Nfty.Desktop/App.axaml.cs`, where the service provider is built, add the override after `AddNftyApp()`:
```csharp
        var services = new ServiceCollection()
            .AddNftyApp()
            .AddSingleton<Nfty.App.Services.IFilePickerService, DesktopFilePicker>()
            .BuildServiceProvider();
```
(last registration wins over the Phase-1 stub).

- [ ] **Step 3: Build** — Run: `dotnet build src/Nfty.Desktop --nologo`
Expected: 0 warnings, 0 errors. If the `StorageProvider` API differs on Avalonia 11.2.3 (e.g. `OpenFilePickerAsync` return/option names), adjust to the installed version's signatures and note it.

- [ ] **Step 4: Commit**
```bash
git add src/Nfty.Desktop/DesktopFilePicker.cs src/Nfty.Desktop/App.axaml.cs
git commit -m "$(printf 'feat(gui): real desktop file picker over StorageProvider\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

## Task 8: Landing Open/Import flow

**Files:**
- Modify: `src/Nfty.App/ViewModels/LandingViewModel.cs`
- Test: `tests/Nfty.App.Tests/LandingOpenFlowTests.cs`

**Interfaces:**
- Consumes: `ICookBookSession`, `Func<LoadedCookBook, ExplorerViewModel>`, `IFilePickerService`, `IDialogService`, `INavigationService`, `INotYetWired`, `CookBookArchive.Read`, `Archives.KindOf`.

`LandingViewModel`'s constructor gains `ICookBookSession session` and `Func<LoadedCookBook, ExplorerViewModel> explorerFactory`. Existing `LandingViewModelTests` construct `LandingViewModel(...)` — update those constructions to pass a fake session + a factory `book => new ExplorerViewModel(book, nav, dialogs, notify)`.

**Superseded Phase-1 test:** `LandingViewModelTests.Open_cookbook_reports_not_yet_wired` (which asserted `OpenCookBookCommand` reports `"Open CookBook"`) is now **wrong behavior** — Open no longer reports; it picks a file and opens it. **Delete that test method** (its coverage is replaced by the new `LandingOpenFlowTests`). Keep the other Landing tests (they only need the updated constructor). `WiringCoverageTests` still asserts `OpenCookBookCommand` *exists* — that stays true (it's now an `AsyncRelayCommand`).

- [ ] **Step 1: Write the failing test**
```csharp
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Xunit;

namespace Nfty.App.Tests;

public class LandingOpenFlowTests
{
    private sealed class StubPicker : IFilePickerService
    {
        private readonly string? _path;
        public StubPicker(string? path) => _path = path;
        public Task<string?> OpenFileAsync(string title, params string[] extensions) => Task.FromResult(_path);
        public Task<string?> SaveFileAsync(string title, string defaultExtension) => Task.FromResult<string?>(null);
    }

    private static LandingViewModel Make(string? pickerPath, out FakeNav nav, out FakeDialogs dialogs,
        out FakeNotYetWired notify, out CookBookSession session)
    {
        nav = new FakeNav(); dialogs = new FakeDialogs(); notify = new FakeNotYetWired(); session = new CookBookSession();
        var s = session; var n = nav; var d = dialogs; var no = notify;
        return new LandingViewModel(n, d, no, new StubPicker(pickerPath), new RecentsService(), s,
            book => new ExplorerViewModel(book, n, d, no));
    }

    [Fact]
    public void Open_reads_the_cbk_opens_the_session_and_navigates_to_explorer()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string path = Path.Combine(tmp.FullName, "VaporPets.cbk");
            WriteTinyCookBook(path);   // helper below
            var vm = Make(path, out var nav, out _, out _, out var session);
            vm.OpenCookBookCommand.Execute(null);
            Assert.NotNull(session.Current);
            Assert.IsType<ExplorerViewModel>(nav.Current);
        }
        finally { tmp.Delete(true); }
    }

    [Fact]
    public void Cancelled_picker_does_nothing()
    {
        var vm = Make(null, out var nav, out _, out _, out var session);
        vm.OpenCookBookCommand.Execute(null);
        Assert.Null(session.Current);
        Assert.Null(nav.Current);
    }

    [Fact]
    public void A_bad_path_shows_the_error_dialog_and_does_not_navigate()
    {
        var vm = Make("does-not-exist.cbk", out var nav, out var dialogs, out _, out var session);
        vm.OpenCookBookCommand.Execute(null);
        Assert.IsType<ErrorDialogViewModel>(dialogs.Active);
        Assert.Null(nav.Current);
        Assert.Null(session.Current);
    }

    [Fact]
    public void Import_of_a_loose_igt_reports_the_kitchen_message()
    {
        var vm = Make("thing.igt", out _, out _, out var notify, out _);
        vm.ImportCommand.Execute(null);
        Assert.Contains("Kitchen", notify.Last);
    }

    private static void WriteTinyCookBook(string path)
    {
        // reuse the in-memory book builder + CookBookArchive.Write
        var book = ExplorerViewModelTests.TwoRecipeBook();
        CookBookArchive.Write(path, book.Manifest, book.Recipes);
    }
}
```
Note: `Import` reads `Archives.KindOf("thing.igt")` → `Ingredient` without touching the file (extension only), so no file need exist for that test.

- [ ] **Step 2: Run to verify failure** → FAIL (ctor mismatch; flow not implemented).

- [ ] **Step 3: Implement the flow**

Rewrite the relevant parts of `src/Nfty.App/ViewModels/LandingViewModel.cs` — add the two ctor params and implement `OpenCookBook`/`Import`:
```csharp
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Models;
using Nfty.App.Services;
using Nfty.Core.Formats;

namespace Nfty.App.ViewModels;

public partial class LandingViewModel : ViewModelBase
{
    private readonly INavigationService _nav;
    private readonly IDialogService _dialogs;
    private readonly INotYetWired _notify;
    private readonly IFilePickerService _picker;
    private readonly IRecentsService _recents;
    private readonly ICookBookSession _session;
    private readonly Func<LoadedCookBook, ExplorerViewModel> _explorerFactory;

    public IReadOnlyList<RecentItem> Recents => _recents.Items;

    public LandingViewModel(INavigationService nav, IDialogService dialogs, INotYetWired notify,
        IFilePickerService picker, IRecentsService recents, ICookBookSession session,
        Func<LoadedCookBook, ExplorerViewModel> explorerFactory)
    {
        _nav = nav; _dialogs = dialogs; _notify = notify; _picker = picker; _recents = recents;
        _session = session; _explorerFactory = explorerFactory;
    }

    [RelayCommand] private void NewCookBook() => _dialogs.ShowAsync<object>(new NewCookBookViewModel(_dialogs, _notify));
    [RelayCommand(CanExecute = nameof(Never))] private void NewKitchen() => _notify.Report("New Kitchen");
    [RelayCommand] private void NewRecipe() => _dialogs.ShowAsync<object>(new NewRecipeViewModel(_dialogs, _notify));
    [RelayCommand] private void NewIngredient() => _dialogs.ShowAsync<object>(new NewIngredientViewModel(_dialogs, _notify));

    [RelayCommand]
    private async Task OpenCookBook()
    {
        var path = await _picker.OpenFileAsync("Open CookBook", ".cbk");
        if (path is null) return;
        OpenPath(path);
    }

    [RelayCommand]
    private async Task Import()
    {
        var path = await _picker.OpenFileAsync("Import", ".cbk", ".rcp", ".igt");
        if (path is null) return;
        ArchiveKind kind;
        try { kind = Archives.KindOf(path); }
        catch (Exception ex) { ShowError("Could not import", ex.Message); return; }

        if (kind == ArchiveKind.CookBook) OpenPath(path);
        else _notify.Report("Importing a loose recipe/ingredient needs the Kitchen (coming soon)");
    }

    private void OpenPath(string path)
    {
        LoadedCookBook book;
        try { book = CookBookArchive.Read(path); }
        catch (Exception ex) { ShowError("Could not open", ex.Message); return; }
        _session.Open(book);
        _nav.To(_explorerFactory(book));
    }

    private void ShowError(string title, string message) =>
        _dialogs.ShowAsync<object>(new ErrorDialogViewModel(_dialogs, title, message));

    [RelayCommand(CanExecute = nameof(Never))] private void OpenSet() => _notify.Report("Open .set");
    [RelayCommand] private void OpenRecent(RecentItem item) => _notify.Report($"Open recent: {item.Name}");
    [RelayCommand] private void ShowHelp() => _dialogs.ShowAsync<object>(new HelpViewModel(_dialogs));
    private bool Never() => false;
}
```
`OpenCookBook`/`Import` are now `async Task` → the generated commands are `AsyncRelayCommand` (`OpenCookBookCommand`/`ImportCommand` still exist). Update `ServiceRegistration`'s Landing registration is unchanged (still `AddTransient<LandingViewModel>()`), but the DI now resolves the new ctor deps (`ICookBookSession` from Task 1, the `Func<>` factory from Task 2) automatically. Update `LandingViewModelTests` + `WiringCoverageTests` + `SmokeTests` constructions of `LandingViewModel` to pass a `CookBookSession` and a factory `book => new ExplorerViewModel(book, nav, dialogs, notify)`.

- [ ] **Step 4: Run the tests** → PASS (5 new + updated existing). Full suite green.

- [ ] **Step 5: Commit**
```bash
git add src/Nfty.App/ViewModels/LandingViewModel.cs tests/Nfty.App.Tests
git commit -m "$(printf 'feat(gui): Landing Open/Import reads a cookbook and navigates to the Explorer\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

## Task 9: Bind the Views to the real-data properties

**Files:**
- Modify: `src/Nfty.App/Views/ExplorerView.axaml`, `CookBookDetailView.axaml`, `RecipeDetailView.axaml`, `IngredientDetailView.axaml`

**Interfaces:** consumes the properties added in Tasks 3–5.

Functional binding only (mockup-faithful styling is a later pass). Update each detail view to display its VM's real data with token brushes:

- [ ] **Step 1: Update the detail views**

`CookBookDetailView.axaml` — show identity + counts + unique-DNA + the recipe-share list:
```xml
<UserControl xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Nfty.App.ViewModels" x:Class="Nfty.App.Views.CookBookDetailView"
             x:DataType="vm:CookBookDetailViewModel">
  <StackPanel Margin="20" Spacing="8">
    <TextBlock Text="{Binding Name}" FontSize="20" FontWeight="Bold" />
    <TextBlock Classes="muted" Text="{Binding CanvasText, StringFormat='canvas {0}'}" />
    <StackPanel Orientation="Horizontal" Spacing="18">
      <TextBlock Text="{Binding RecipeCount, StringFormat='{}{0} recipes'}" />
      <TextBlock Text="{Binding LayerCount, StringFormat='{}{0} layers'}" />
      <TextBlock Text="{Binding VariantCount, StringFormat='{}{0} variants'}" />
      <TextBlock Foreground="{DynamicResource AccentTextBrush}" Text="{Binding UniqueDnaText, StringFormat='{}{0} unique DNA'}" />
    </StackPanel>
    <TextBlock Classes="muted" Text="Mint distribution" Margin="0,8,0,0" />
    <ItemsControl ItemsSource="{Binding Recipes}">
      <ItemsControl.ItemTemplate>
        <DataTemplate x:DataType="vm:RecipeShareRow">
          <StackPanel Orientation="Horizontal" Spacing="12">
            <TextBlock Width="120" Text="{Binding Name}" />
            <TextBlock Width="70" Text="{Binding SharePercent, StringFormat='{}{0}%'}" />
            <TextBlock Classes="muted" Text="{Binding DnaSpaceText, StringFormat='{}{0} DNA'}" />
          </StackPanel>
        </DataTemplate>
      </ItemsControl.ItemTemplate>
    </ItemsControl>
    <Button Content="Cook set" Command="{Binding CookCommand}" Classes="accent" HorizontalAlignment="Left" Margin="0,8,0,0" />
  </StackPanel>
</UserControl>
```
`RecipeDetailView.axaml` — layer table + rules (bind `Layers`/`Rules`, `RerollCommand`, and layer rows → `OpenIngredientCommand` with `Layer` as parameter), same `ItemsControl` idiom + token brushes. `IngredientDetailView.axaml` — `Name`/`KindText`/`ColorwaysText` + a `Variants` `ItemsControl` (Name · Weight · WithinPercent · OverallPercent), the `SortBy` header buttons, and the `DeleteVariant`/`EditIngredient`/`JumpToRules` buttons. Keep the Phase-1 controls that already bind these commands; add the data displays. `ExplorerView.axaml` — its `TreeView` already binds `Roots`/`SelectNode`; confirm it still resolves (the `ExplorerNode` now has a `Domain` member but the tree template binds `Name`/`Children` which are unchanged).

- [ ] **Step 2: Build (compile the XAML)** — Run: `dotnet build src/Nfty.Desktop --nologo`
Expected: 0 warnings, 0 errors.

- [ ] **Step 3: Run the full App suite** — Run: `dotnet test tests/Nfty.App.Tests --nologo`
Expected: all PASS (no VM changes; the `[AvaloniaFact]` smoke test still resolves each View).

- [ ] **Step 4: Commit**
```bash
git add src/Nfty.App/Views
git commit -m "$(printf 'feat(gui): bind Explorer detail views to real cookbook data\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

## Task 10: Full verification + manual smoke

**Files:** none (verification).

- [ ] **Step 1: Full solution build + test**
Run: `dotnet build nfty.sln --nologo` → Build succeeded, 0 warnings.
Run: `dotnet test nfty.sln --nologo` → all PASS (Core + Cli + App).

- [ ] **Step 2: Manual smoke against the real fixture**
Run: `dotnet run --project src/Nfty.Desktop` — in the window: click **Open CookBook** (or `Ctrl+O`), pick `tests/fixtures/VaporPets.cbk`, and confirm the app navigates into the **Explorer** showing the real tree (the fixture's recipes/ingredients) and that selecting the cookbook / a recipe / an ingredient shows real counts, unique-DNA, layer table, and rarity. Also confirm picking a non-`.cbk` via **Import** shows the "needs the Kitchen" status, and opening a corrupt/renamed file shows the **error dialog**. Close the window.

- [ ] **Step 3: Commit (if any smoke-driven fixups were needed)** — otherwise nothing to commit; the feature is complete.

---

## Self-Review notes (for the implementer)

- **`ExplorerViewModel` ctor change ripples** to: DI (removed `AddTransient<ExplorerViewModel>`, added the `Func<>` factory), `SmokeTests`, `WiringCoverageTests`, `ExplorerViewModelTests`, and the `LandingViewModel` factory param. Each task that changes a signature updates every construction site in the same commit so the build stays green.
- **Disposal:** only `ICookBookSession` disposes the book. The `ExplorerViewModel` and detail VMs hold references but never dispose. Tests build in-memory books and let them be GC'd (or the session disposes them) — do not add `using` over a `LoadedCookBook` handed to `session.Open`.
- **No images this slice:** detail VMs read `Manifest`/weights/computations only; they never touch `VariantImages`. The hero/thumbnail/colorway art stays the Phase-1 placeholder.
- **Async commands:** `OpenCookBook`/`Import` are `async Task` → `AsyncRelayCommand`; the wiring-coverage test's `ICommand` check still passes (`AsyncRelayCommand : ICommand`).
- **StorageProvider API:** if Avalonia 11.2.3's `OpenFilePickerAsync`/`FilePickerOpenOptions`/`TryGetLocalPath` signatures differ, adjust to the installed version and keep the behavior (single-file open, extension filter, return local path or null).
