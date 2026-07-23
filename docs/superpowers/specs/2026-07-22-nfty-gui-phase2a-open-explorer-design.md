# nfty GUI — Phase 2a: Open/Import CookBook → Explorer (design spec)

**Date:** 2026-07-22
**Status:** Approved (design), pending implementation planning
**Scope:** The first Phase-2 behavior slice of the Avalonia GUI: replace the Landing's
`Open CookBook` / `Import` stubs with real `Nfty.Core` reads, own the open book's lifetime, and bind
the Explorer's tree + all three detail views to the real `LoadedCookBook` (text/metrics; variant
images deferred).
**Builds on:** `2026-07-22-nfty-gui-completion-design.md` (§8 services & Core seams, §10 phasing,
§11 deferred). Phase 1 (the wired shell) is merged on `main`.

## 1. Goals & non-goals

**Goals**
- `Open CookBook` and a `.cbk` `Import` actually read a cookbook via `Nfty.Core` and navigate into the
  Explorer bound to it.
- A single owner for the open book's memory (it eagerly decodes every variant PNG), disposed on
  reopen/close/shutdown.
- The Explorer's tree and all three detail views (CookBook / Recipe / Ingredient) show **real data**
  from the opened book: identity, counts, unique-DNA space, mint distribution, layer tables, rules,
  variant tables, and rarity.
- Read failures surface cleanly (error dialog), never crash.

**Non-goals (this slice)**
- **Variant images.** No ImageSharp→Avalonia bitmap bridge exists yet; art renders as the Phase-1
  placeholder. The bridge + real hero/thumbnail/colorway art is a later slice.
- **Loose `.rcp`/`.igt` import** — needs the Kitchen workspace (deferred, §11 of the parent spec).
- **Cook, editing, add/delete, reroll, the Ingredient Editor** — remain Phase-1 stubs.
- Any `Nfty.Core` change (reads use existing APIs).

## 2. Components

### 2.1 `IFilePickerService` — real desktop impl (per-head)
The interface stays in `Nfty.App` (head-agnostic). `Nfty.Desktop` adds a real
`DesktopFilePicker : IFilePickerService` that resolves the active `TopLevel` from the classic desktop
lifetime's `MainWindow` and calls `StorageProvider.OpenFilePickerAsync` with a `FilePickerFileType`
filter built from the requested extensions; returns the picked file's local path via
`IStorageFile.TryGetLocalPath()` (or null on cancel). Registered in the Desktop head **after**
`AddNftyApp()` so it overrides the Phase-1 null `FilePickerService`. The picker impl is per-head
because `StorageProvider` requires a window; mobile heads provide their own later. `SaveFileAsync`
stays a stub this slice (no save path is exercised yet).

### 2.2 `ICookBookSession` — the open-book lifetime owner
New singleton in `Nfty.App.Services`:
```
public interface ICookBookSession : IDisposable
{
    LoadedCookBook? Current { get; }
    event Action? Changed;
    void Open(LoadedCookBook book);   // disposes the previous Current, then swaps + raises Changed
    void Close();                     // disposes + clears + raises Changed
}
```
`Open` disposes the previously-held book (a `LoadedCookBook` owns every decoded variant image, so this
is the one place that frees them) before storing the new one. The session is registered as a singleton
and disposed when the DI container is disposed at shutdown. It is the **single owner**; no ViewModel
disposes the book.

### 2.3 Open / Import flow (`LandingViewModel`)
- `OpenCookBook`: `path = await _picker.OpenFileAsync("Open CookBook", ".cbk")`; if null → return.
  `try { book = CookBookArchive.Read(path); }` on failure → show error dialog (2.5) and return.
  `_session.Open(book); _nav.To(explorerFactory(book));`
- `Import`: `path = await _picker.OpenFileAsync("Import", ".cbk", ".rcp", ".igt")`; if null → return.
  `switch (Archives.KindOf(path))`: `CookBook` → same read+open+navigate as above; `Recipe`/`Ingredient`
  → `_notify.Report("Importing a loose recipe/ingredient needs the Kitchen (coming soon)")`. An unknown
  extension throws from `KindOf` and is caught → error dialog.
- The Explorer is created per-open (a fresh VM bound to the book). `LandingViewModel` injects a
  DI-registered factory delegate `Func<LoadedCookBook, ExplorerViewModel>` (registered as
  `sp => book => new ExplorerViewModel(book, sp.GetRequiredService<INavigationService>(), …)`), so it
  builds the Explorer from the book without hand-constructing the whole Core-typed VM graph.

### 2.4 Explorer bound to real data
`ExplorerViewModel` is constructed with the non-null `LoadedCookBook` (the Phase-1 sample tree is
removed — the Explorer is only shown with a book open). Responsibilities:
- **Tree:** build `ExplorerNode`s from `book.Recipes`; each recipe is a node, each ingredient (in the
  recipe's `layerOrder`) a child. `ExplorerNode` gains what the detail VMs need to resolve their domain
  object — carry the `LoadedRecipe`/`LoadedIngredient` reference (or ids resolvable against the book).
  A single root node for the cookbook (name + canvas), matching the Explorer spec's CookBook▸Recipe▸
  Ingredient tree.
- **CurrentDetail** (set on selection) uses the real detail VMs (2.4.1). The context-aware toolbar
  (Add/Delete/Import/lock/search) keeps its Phase-1 behavior (stubs/ui-state); Delete/Add wiring to real
  mutation is a later slice.

#### 2.4.1 Detail ViewModels — real data
- **`CookBookDetailViewModel(LoadedCookBook book)`** — name, symbol, canvas `W×H`, counts (recipes /
  layers / variants), **unique-DNA total** from `UniqueSpace.Count(book)` (rendered `Total` with the
  "more than N" form when `!IsExact`), mint distribution (per-recipe `RecipeWeights` share %), and
  per-recipe DNA-space rows from `UniqueSpace.Count(book).Recipes`. `Cook` stays a stub.
- **`RecipeDetailViewModel(LoadedRecipe recipe, LoadedCookBook book)`** — layer table
  (`layerOrder` → ingredient name · kind · variant count), rules (`recipe.Manifest.Rules` as
  operator + trait rows, empty-state when none), the recipe's factor total + weight/mint-share. `Reroll`
  stays ui-state (placeholder art), `OpenIngredient` stays nav.
- **`IngredientDetailViewModel(LoadedIngredient ing, LoadedRecipe recipe, LoadedCookBook book)`** —
  variant table (name · weight · kind), in-recipe + overall **rarity** from
  `RarityCalculator.Compute(book)` (`TraitOdds` filtered to this ingredient's variants), colorways
  (kind + colorization H/S ranges as text; "value ← value-map"). `SortBy`/`SelectVariant` stay ui-state;
  `DeleteVariant`/`EditIngredient`/`JumpToRules` stay stubs/nav.

These VMs are constructed by `ExplorerViewModel` from the selected node + the book. They read Core
records/computations only; they do not decode images (deferred).

### 2.5 Error dialog
A small `ErrorDialogViewModel(IDialogService, string title, string message)` + `ErrorDialogView`
shown via the existing `IDialogService` overlay on a read/`KindOf` failure, with an OK/Close command
(and `Esc`). Message is `ex.Message` (engine exceptions already carry human-readable messages). Reuses
the Phase-1 dialog infrastructure — no new overlay mechanism.

## 3. Data flow

```
Landing.OpenCookBook
  → IFilePickerService.OpenFileAsync(".cbk")           [DesktopFilePicker → StorageProvider]
  → CookBookArchive.Read(path)  → LoadedCookBook        [throws → ErrorDialog]
  → ICookBookSession.Open(book) [disposes previous]
  → INavigationService.To( ExplorerViewModel(book) )
        → builds ExplorerNode tree from book.Recipes
        → on node select → CookBook/Recipe/Ingredient DetailViewModel(book, …)
             → UniqueSpace.Count / RarityCalculator.Compute for metrics
```

## 4. Testing

In-memory `LoadedCookBook`s built the way the Core tests do (tiny solid-fill `Image<Rgba32>`s); fakes
for picker/dialog/nav/session where useful.
- **`CookBookSession`** — `Open(b2)` after `Open(b1)` disposes `b1` (assert a variant image of `b1`
  throws `ObjectDisposedException` on access); `Close()` disposes `Current`; `Current`/`Changed` behave.
- **`ExplorerViewModel`** — built from a 2-recipe book, the tree has the right node names/kinds/shape;
  selecting a recipe/ingredient node yields the matching detail VM with the book's data.
- **Detail VMs** — `CookBookDetailViewModel` exposes correct counts + `UniqueSpace` total;
  `RecipeDetailViewModel` layer table matches `layerOrder`; `IngredientDetailViewModel` rarity numbers
  match `RarityCalculator.Compute` for a known-weight fixture.
- **`LandingViewModel.OpenCookBook`** — fake picker returning a real temp `.cbk` (written via
  `CookBookArchive.Write`) → `session.Open` called + nav to an `ExplorerViewModel`; picker returns null
  → no nav, no session change; `CookBookArchive.Read` throws (fake picker returns a bad path) → error
  dialog shown, no nav. `Import` of a `.rcp`/`.igt` path → reports the Kitchen message, no nav.
- **`DesktopFilePicker`** — needs a real window/`TopLevel`; **manually smoke-tested** (open a `.cbk`
  from disk → Explorer renders), not unit-tested. Note this in the plan.

Pure VM/service tests are `[Fact]`; anything constructing Avalonia controls uses `[AvaloniaFact]`.

## 5. Open items / deferred (reserved)
- **Imaging bridge** (ImageSharp `Image<Rgba32>` → Avalonia `Bitmap`) + real variant art in hero/
  thumbnails/colorways — the next slice; groundwork the editor/preview also need.
- **Cook / editing / add / delete / reroll** real behavior — later slices.
- **Loose import** + **Kitchen workspace**, **Set browser**, **command palette**, **mobile heads** —
  as in the parent spec §11.

## 6. Out of scope
- Any `Nfty.Core` change.
- Visual-fidelity polish of the Explorer to the mockup (functional binding only; polish is its own pass).
- Saving/writing archives (this slice only reads).
