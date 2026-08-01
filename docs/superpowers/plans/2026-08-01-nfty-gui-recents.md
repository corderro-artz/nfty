# nfty GUI — Real, persisted Recents (D1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** The Landing's Recent list records what the user actually opens, persists across runs, and reopens on click.

**Architecture:** `RecentsService` gains an injectable storage directory and JSON load/save (de-dupe by full path, cap 10, silent failures). `LandingViewModel` records an entry after each successful open and dispatches a clicked recent by extension, removing entries whose file has vanished. No `Nfty.Core` change.

**Tech Stack:** .NET 10, Avalonia 11.2.3, CommunityToolkit.Mvvm, `System.Text.Json`, xUnit + Avalonia.Headless.XUnit.

## Global Constraints
- **Tests must never touch the real `%APPDATA%`** — the storage directory is a ctor parameter defaulting to the real location; every test passes a temp dir.
- **Recents never interrupt the user:** a corrupt/unreadable store loads as empty; a failed save is swallowed. No dialogs from the service.
- **Record only after a successful open** (a failed read must not pollute the list). Store `Path.GetFullPath(path)`.
- `RecentItem` is the existing `record RecentItem(string Name, string Meta, string Path, bool Loose)` — do not change its shape.
- Determinism/idiom: `StringComparer.Ordinal` for path de-dupe; no RNG; no view change. Build 0 warnings. Conventional commits. Agents: caveman-ultra terse chat; code/commits/reports normal prose. Context7 for any uncertain API.

## File Structure
- `src/Nfty.App/Services/IRecentsService.cs` — persistence + `Remove` (T1).
- `src/Nfty.App/ViewModels/LandingViewModel.cs` — record on open; real `OpenRecent`; extract `OpenSetPath` (T2).
- Tests: `tests/Nfty.App.Tests/RecentsServiceTests.cs` (create, T1); `tests/Nfty.App.Tests/LandingRecentsTests.cs` (create, T2).

---

### Task 1: Persisted `RecentsService`

**Files:** Modify `src/Nfty.App/Services/IRecentsService.cs`; Test `tests/Nfty.App.Tests/RecentsServiceTests.cs` (create).

**Interfaces:**
- Produces: `RecentsService(string? storageDir = null)`; `void Add(RecentItem)` (de-dupe by full path, front-insert, cap 10, save); `void Remove(string path)`; `IReadOnlyList<RecentItem> Items`.

- [ ] **Step 1: Failing tests** — `RecentsServiceTests.cs`, every test using its own `Directory.CreateTempSubdirectory()`:
  - `Add_dedupes_by_path_and_moves_to_the_front` — add A, B, A ⇒ `Items` is [A, B] with A first.
  - `Add_caps_the_list_at_ten` — add 11 distinct paths ⇒ 10 items, the oldest gone.
  - `Items_round_trip_through_the_file` — add via one service; a **second** `RecentsService` over the same dir sees the same items in order.
  - `Remove_deletes_by_path` — add A+B, `Remove(A)` ⇒ [B], and a fresh service confirms it persisted.
  - `A_corrupt_store_loads_as_empty` — write `"{ not json"` to the store file ⇒ ctor does not throw, `Items` empty.
  - `A_first_run_is_empty` — a fresh temp dir ⇒ `Items` empty (the demo rows are gone).

- [ ] **Step 2: Run — fail** (no ctor parameter / no persistence).

- [ ] **Step 3: Implement.** Rewrite `RecentsService`:
```csharp
public interface IRecentsService
{
    IReadOnlyList<RecentItem> Items { get; }
    void Add(RecentItem item);
    void Remove(string path);
}

/// <summary>Most-recently-opened files, persisted as JSON under the user's app-data folder. Purely
/// convenience state: a corrupt store loads as empty and a failed save is swallowed, so recents can
/// never block or crash the app. The storage directory is injectable so tests never touch %APPDATA%.</summary>
public sealed class RecentsService : IRecentsService
{
    private const int Cap = 10;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    private readonly string _file;
    private readonly List<RecentItem> _items = new();

    public RecentsService(string? storageDir = null)
    {
        var dir = storageDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "nfty");
        _file = Path.Combine(dir, "recents.json");
        try
        {
            if (File.Exists(_file))
                _items = JsonSerializer.Deserialize<List<RecentItem>>(File.ReadAllText(_file), Json) ?? new();
        }
        catch { _items = new(); }   // corrupt/unreadable → start empty, never throw
    }

    public IReadOnlyList<RecentItem> Items => _items;

    public void Add(RecentItem item)
    {
        var full = Path.GetFullPath(item.Path);
        var entry = item with { Path = full };
        _items.RemoveAll(i => string.Equals(i.Path, full, StringComparison.Ordinal));
        _items.Insert(0, entry);
        if (_items.Count > Cap) _items.RemoveRange(Cap, _items.Count - Cap);
        Save();
    }

    public void Remove(string path)
    {
        _items.RemoveAll(i => string.Equals(i.Path, path, StringComparison.Ordinal));
        Save();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, JsonSerializer.Serialize(_items, Json));
        }
        catch { /* convenience state — never surface */ }
    }
}
```
  Add `using System.Text.Json;`/`System.IO`/`Nfty.App.Models`. **Note:** `Remove` compares the stored (already full) path; callers pass `item.Path` straight from the list, so no normalisation is needed there.

- [ ] **Step 4: Run — pass;** whole App suite green (existing Landing tests construct `new RecentsService()` with no args — the default parameter keeps them compiling, but they now hit the REAL appdata; **change those call sites to pass a temp dir** so no test touches `%APPDATA%`); `dotnet build src/Nfty.Desktop --nologo` 0 warnings.

- [ ] **Step 5: Commit** `feat(gui): persist recents to app-data instead of demo rows`

---

### Task 2: Record on open + reopen on click

**Files:** Modify `src/Nfty.App/ViewModels/LandingViewModel.cs`; Test `tests/Nfty.App.Tests/LandingRecentsTests.cs` (create).

**Interfaces:** Consumes `IRecentsService.Add/Remove`; extracts `private void OpenSetPath(string path)` from `OpenSet`.

- [ ] **Step 1: Failing tests** — `LandingRecentsTests.cs` (build the Landing exactly as the sibling `LandingNewCookBookTests` does — the ctor gained params in recent slices; pass a temp-dir `RecentsService`):
  - `Opening_a_cookbook_records_a_recent` — write a temp `.cbk` (use `CookBookPersistence.WriteNew`), open it via `ImportCommand` with a stub picker, assert `vm.Recents` has one entry whose `Path` is that file and `Loose` is false.
  - `A_failed_open_records_nothing` — picker returns a path to a non-archive/corrupt file ⇒ `Recents` empty.
  - `Clicking_a_missing_recent_removes_it_and_errors` — seed a recent for a deleted path, execute `OpenRecentCommand`, assert an error dialog was shown and `Recents` is empty.
  - `Clicking_a_cookbook_recent_opens_the_explorer` — seed a recent for a real temp `.cbk`, execute, assert `nav.Current` is an `ExplorerViewModel`.

- [ ] **Step 2: Run — fail** (`OpenRecent` is the notify stub; nothing records).

- [ ] **Step 3: Implement** in `LandingViewModel.cs`:
  - Extract the body of `OpenSet` after the picker into `private void OpenSetPath(string path)` (read → error dialog on failure → `_nav.To(_setBrowserFactory(set))` → record a recent), and have `OpenSet` call it.
  - Record after each successful open:
    - in `OpenPath`, after `_nav.To(...)`: `RecordRecent(new RecentItem(book.Manifest.Name, $"{book.Recipes.Count} recipes · {book.Manifest.Canvas.Width}×{book.Manifest.Canvas.Height}", path, false));`
    - in `OpenLooseIngredient`, after `_nav.To(...)`: `RecordRecent(new RecentItem(ing.Manifest.Name, $"loose ingredient · {ing.Manifest.Variants.Count} variants", path, true));`
    - in `OpenLooseRecipe`, after `_nav.To(...)`: `RecordRecent(new RecentItem(recipe.Manifest.Name, $"loose recipe · {recipe.Ingredients.Count} ingredients", path, true));`
    - in `OpenSetPath`, after `_nav.To(...)`: `RecordRecent(new RecentItem(set.Manifest.Name, $"set · {set.Manifest.Count} assets", path, false));`
  - Add:
    ```csharp
    private void RecordRecent(RecentItem item)
    {
        _recents.Add(item);
        OnPropertyChanged(nameof(Recents));
    }

    [RelayCommand]
    private void OpenRecent(RecentItem item)
    {
        if (!File.Exists(item.Path))
        {
            _recents.Remove(item.Path);
            OnPropertyChanged(nameof(Recents));
            ShowError("Missing file", $"“{item.Path}” is no longer there, so it was removed from Recents.");
            return;
        }
        if (string.Equals(Path.GetExtension(item.Path), ".set", StringComparison.OrdinalIgnoreCase))
        { OpenSetPath(item.Path); return; }

        ArchiveKind kind;
        try { kind = Archives.KindOf(item.Path); }
        catch (Exception ex) { ShowError("Can't open", ex.Message); return; }
        switch (kind)
        {
            case ArchiveKind.CookBook: OpenPath(item.Path); return;
            case ArchiveKind.Ingredient: OpenLooseIngredient(item.Path); return;
            case ArchiveKind.Recipe: OpenLooseRecipe(item.Path); return;
        }
    }
    ```
  - Add `using System.IO;` if absent.

- [ ] **Step 4: Run — pass;** whole App suite green; build 0 warnings.

- [ ] **Step 5: Commit** `feat(gui): record and reopen real recents`

---

### Task 3: Verification (orchestrator)

- [ ] `dotnet build nfty.sln --nologo` → 0 warnings; `dotnet test nfty.sln --nologo` → all pass (report totals).
- [ ] `git diff --name-only <base>..HEAD -- src/Nfty.Core/` → empty.
- [ ] `grep -rn "ApplicationData" tests/` → no test uses the real app-data folder.
- [ ] Manual smoke (user): open a cookbook → it tops the Recent list → restart → still there → click → reopens; delete the file → click → error + it disappears.

---

## Self-Review
- **Spec coverage:** §2.1 persistence + `Remove` + silent failure + injectable dir → T1. §2.2 recording on all four open paths → T2. §2.3 `OpenRecent` dispatch + missing-file removal → T2. §2.4 (no view change) → n/a. §4 error handling → T1 (corrupt/save) + T2 (missing/unsupported). §5 tests → T1 (6 service tests) + T2 (4 Landing tests) + manual. §6 risks: test isolation (temp dirs everywhere, verified by a grep in T3), path normalisation (`GetFullPath` on add), deliberate silence, empty first run.
- **Placeholder scan:** T1 gives full code; T2 gives exact edits and the four record sites. The test bullets are descriptive but each names its exact assertion — implement them fully.
- **Type consistency:** `RecentItem(Name, Meta, Path, Loose)` unchanged (uses `with { Path = full }`, so it must stay a record); `IRecentsService` gains `Remove`; the Landing's existing `_recents`/`Recents`/`ShowError`/`OpenPath`/`OpenLooseIngredient`/`OpenLooseRecipe` are reused as-is; `Archives.KindOf` + `ArchiveKind` from `Nfty.Core.Formats`.
