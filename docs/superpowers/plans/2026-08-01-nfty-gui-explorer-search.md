# nfty GUI — Explorer search / filter (D2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** A search box filters the Explorer tree by recipe/ingredient/variant, Ctrl+K focuses it.

**Architecture:** The unfiltered tree is kept in `_fullRoot`; `Root` becomes a filtered projection recomputed whenever `SearchQuery` changes or the graph is swapped (`ApplyBook`). Selection falls back to the root if the selected node is filtered away. The view swaps the 🔍 button for a bound `TextBox` and focuses it on Ctrl+K in code-behind. No `Nfty.Core` change.

**Tech Stack:** .NET 10, Avalonia 11.2.3, CommunityToolkit.Mvvm, xUnit + Avalonia.Headless.XUnit.

## Global Constraints
- **Selection safety:** a filtered-away `SelectedNode` must fall back to the root — the Explorer's mutating commands act on `SelectedNode`, so leaving it pointing at an invisible node risks deleting something the user can't see.
- **Filter survives `ApplyBook`** (save/add/delete rebuild the tree) — otherwise the query silently clears while the box still shows text.
- **Blank query returns `_fullRoot` unchanged** (same instance).
- Node identity across a filter change is by **id**, never by reference (filtering builds new `ExplorerNode`s).
- Determinism/idiom: `StringComparison.OrdinalIgnoreCase` for matching; no RNG; token brushes only, no raw hex. Build 0 warnings. Conventional commits. Agents: caveman-ultra terse chat; code/commits/reports normal prose. Context7 for any uncertain Avalonia API.

## File Structure
- `src/Nfty.App/ViewModels/ExplorerViewModel.cs` — `SearchQuery`, `_fullRoot`, `Filter`/`Matches`, `SearchSummary`, remove the `Search` stub (T1).
- `src/Nfty.App/Views/ExplorerView.axaml` + `.axaml.cs` — search box + Ctrl+K focus (T2).
- Tests: `tests/Nfty.App.Tests/ExplorerSearchTests.cs` (create, T1).

---

### Task 1: Filter the tree

**Files:** Modify `src/Nfty.App/ViewModels/ExplorerViewModel.cs`; Test `tests/Nfty.App.Tests/ExplorerSearchTests.cs` (create).

**Interfaces:** Produces `string SearchQuery`, `string SearchSummary`; removes `SearchCommand`.

- [ ] **Step 1: Failing tests** — `ExplorerSearchTests.cs`. Use `ExplorerViewModelTests.TwoRecipeBook()` (recipes `cat`/`dog` with ingredients) built via the same helper the other Explorer tests use; write each assertion fully:
  - `Matching_an_ingredient_keeps_its_recipe_and_drops_siblings` — query an ingredient id/name unique to `cat` ⇒ `Root.Children` is just `cat`, and `cat.Children` contains only the matching ingredient.
  - `Matching_a_recipe_keeps_all_its_ingredients` — query `"cat"` ⇒ `cat` kept with **all** its ingredients.
  - `Matching_a_variant_keeps_its_ingredient` — query a variant id/name from the fixture ⇒ that ingredient (and its recipe) survive.
  - `A_blank_query_restores_the_full_tree` — filter, then set `SearchQuery = ""` ⇒ the child counts match the unfiltered tree.
  - `A_query_matching_nothing_yields_an_empty_root_and_zero_matches` — `Root.Children` empty and `SearchSummary` says `0`.
  - `Matching_is_case_insensitive` — upper/lower forms of the same query give the same result.
  - `Selection_falls_back_to_the_root_when_filtered_away` — select an ingredient, set a query excluding it ⇒ `SelectedNode.Id == Root.Id` and `CurrentDetail` is a `CookBookDetailViewModel`.
  - `The_filter_survives_a_graph_swap` — set a query, call `OnEditorSaved(book)` (internal, already used by `ExplorerViewModelTests`) with the same book ⇒ the filter is still applied (children still reduced), not reset.

- [ ] **Step 2: Run — fail** (`SearchQuery` missing).

- [ ] **Step 3: Implement** in `ExplorerViewModel.cs`:
  - Add `private ExplorerNode _fullRoot = default!;`
  - Add:
    ```csharp
    [ObservableProperty] private string _searchQuery = "";

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    /// <summary>Recompute the visible tree from the unfiltered one, keeping the selection only if it
    /// survived (a filtered-away selection would leave the detail pane — and the mutating commands —
    /// pointing at a node the user can no longer see).</summary>
    private void ApplyFilter()
    {
        var selectedId = SelectedNode?.Id;
        Root = Filter(_fullRoot, SearchQuery);
        OnPropertyChanged(nameof(SearchSummary));
        SelectedNode = FindNode(Root, selectedId) ?? Root;
    }

    /// <summary>Match count for the current query ("" when not filtering), so a zero-result query
    /// reads as such instead of an unexplained empty tree.</summary>
    public string SearchSummary
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SearchQuery)) return "";
            int n = Root.Children.Count + Root.Children.Sum(r => r.Children.Count);
            return n == 1 ? "1 match" : $"{n} matches";
        }
    }

    private static ExplorerNode Filter(ExplorerNode root, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return root;
        var q = query.Trim();
        var recipes = new List<ExplorerNode>();
        foreach (var r in root.Children)
        {
            bool recipeMatches = Matches(r, q);
            var kept = recipeMatches ? r.Children.ToList() : r.Children.Where(i => Matches(i, q)).ToList();
            if (recipeMatches || kept.Count > 0)
                recipes.Add(r with { Children = kept });
        }
        return root with { Children = recipes };
    }

    /// <summary>Name or id, case-insensitive; an ingredient also matches on its variants' ids/names
    /// (variants aren't tree nodes, so this only decides whether the ingredient is shown).</summary>
    private static bool Matches(ExplorerNode n, string q)
    {
        if (n.Id.Contains(q, StringComparison.OrdinalIgnoreCase)
            || n.Name.Contains(q, StringComparison.OrdinalIgnoreCase)) return true;
        if (n.Domain is (LoadedRecipe, LoadedIngredient ing))
            return ing.Manifest.Variants.Any(v =>
                v.Id.Contains(q, StringComparison.OrdinalIgnoreCase)
                || v.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
        return false;
    }
    ```
    (`ExplorerNode` is a record, so `r with { Children = kept }` works — verify against `src/Nfty.App/Models/ExplorerNode.cs`.)
  - **Wire `_fullRoot`:** in the ctor replace `Root = BuildTree(book);` with `_fullRoot = BuildTree(book); Root = _fullRoot;`. In `ApplyBook` replace `Root = BuildTree(book);` with `_fullRoot = BuildTree(book); Root = Filter(_fullRoot, SearchQuery); OnPropertyChanged(nameof(SearchSummary));` (keep its existing selection logic, which already falls back to `Root`).
  - **Remove** `[RelayCommand] private void Search() => _notify.Report("Search (⌘K)");`.

- [ ] **Step 4: Run — pass;** whole App suite green; `dotnet build src/Nfty.Desktop --nologo` 0 warnings. Note `WiringCoverageTests` lists `"SearchCommand"` — remove it there (the command is gone; that test exists to catch a view binding with no VM command, and T2 removes the binding too).

- [ ] **Step 5: Commit** `feat(gui): filter the Explorer tree by recipe, ingredient or variant`

---

### Task 2: Search box + Ctrl+K focus

**Files:** Modify `src/Nfty.App/Views/ExplorerView.axaml`, `src/Nfty.App/Views/ExplorerView.axaml.cs`.

- [ ] **Step 1:** In `ExplorerView.axaml`:
  - Replace `<Button Content="🔍" Command="{Binding SearchCommand}" .../>` with:
    ```xml
    <TextBox x:Name="SearchBox" Width="220" Text="{Binding SearchQuery}"
             Watermark="Find recipe, ingredient, variant…" ToolTip.Tip="Search (Ctrl+K)" />
    <TextBlock Text="{Binding SearchSummary}" Classes="muted" VerticalAlignment="Center" />
    ```
  - Remove `<KeyBinding Gesture="Ctrl+K" Command="{Binding SearchCommand}" />` from `UserControl.KeyBindings` (a KeyBinding can't focus a control).
- [ ] **Step 2:** In `ExplorerView.axaml.cs`, focus the box on Ctrl+K:
  ```csharp
  protected override void OnKeyDown(KeyEventArgs e)
  {
      if (e.Key == Key.K && e.KeyModifiers.HasFlag(KeyModifiers.Control))
      {
          this.FindControl<TextBox>("SearchBox")?.Focus();
          e.Handled = true;
      }
      base.OnKeyDown(e);
  }
  ```
  Add `using Avalonia.Input;`.
- [ ] **Step 3:** `dotnet build src/Nfty.Desktop --nologo` 0 warnings; `dotnet test tests/Nfty.App.Tests --nologo` green (SmokeTests must still resolve the view); `grep -rniE "#[0-9a-fA-F]{6}" src/Nfty.App/Views/ExplorerView.axaml` → nothing.
- [ ] **Step 4: Commit** `feat(gui): Explorer search box focused by Ctrl+K`

---

### Task 3: Verification (orchestrator)

- [ ] `dotnet build nfty.sln --nologo` → 0 warnings; `dotnet test nfty.sln --nologo` → all pass (report totals).
- [ ] `git diff --name-only <base>..HEAD -- src/Nfty.Core/` → empty.
- [ ] Visual: render the Explorer with an active filter (both themes).
- [ ] Manual smoke (user): Ctrl+K focuses the box; typing filters; clearing restores; add/delete with a filter active keeps the filter.

---

## Self-Review
- **Spec coverage:** §2.1 query + filter + selection fallback + `ApplyBook` re-apply → T1. §2.2 summary → T1. §2.3 view + Ctrl+K → T2. §4 (nothing can throw) → n/a. §5 tests → T1's eight + visual/manual (T3). §6 risks: selection safety and filter-survives-swap are each a named test; id-not-reference identity is preserved by reusing `FindNode`.
- **Placeholder scan:** T1 gives full code; the test bullets each name their exact assertion — implement them fully.
- **Type consistency:** `ExplorerNode` is a positional record (`with` used for `Children`); `FindNode(root, id)` already exists and is reused; `LoadedRecipe`/`LoadedIngredient` tuple `Domain` matches `BuildTree`; removing `SearchCommand` requires the matching removal in `WiringCoverageTests` and the view's KeyBinding.
