# nfty GUI — Explorer search / filter (D2) design spec

**Date:** 2026-08-01
**Status:** Approved (design), pending implementation planning
**Scope:** Make the Explorer's search real: a query box that filters the tree to matching recipes,
ingredients and variants, with **Ctrl+K focusing the box**. Today the toolbar has a 🔍 button and a
Ctrl+K binding wired to a `_notify` stub. Last functional stub before the visual-polish pass.

## 0. Program bar
Rock-solid, efficient; best practices; pull docs (Context7) rather than assume any library API; escalate
anything off. Pure view-model filtering over the already-built tree — no `Nfty.Core` change, no new
service.

## 1. Goals & non-goals
**Goals**
- A **search box** in the Explorer toolbar (the mockup's *"Find recipe, ingredient, variant… ⌘K"*),
  bound to a `SearchQuery` property.
- Typing filters the tree: a node is kept when **it matches**, when **any descendant matches** (so a
  matching ingredient keeps its recipe visible), or when its **parent matches** (a matched recipe keeps
  its ingredients, so the user sees what they found in context).
- **Variants participate**: an ingredient whose *variant* name or id matches is kept, even if the
  ingredient's own name doesn't (the mockup explicitly lists variants as searchable). Variants are not
  tree nodes, so this only affects whether the ingredient is shown.
- Matching is **case-insensitive** on both **name and id**, substring (`Contains`).
- An **empty/whitespace query restores the full tree**. A query matching nothing yields an empty tree
  (the root is always kept so the user isn't left with a blank pane and no context).
- **Ctrl+K focuses the search box** (it does not run a command — the current binding invokes a stub).

**Non-goals (this slice)**
- Fuzzy/ranked matching, regex, or a result list separate from the tree. Searching Set assets or the
  Landing's recents. Highlighting the matched substring (an E concern). Auto-expanding the tree to
  reveal matches — filtering already removes the non-matching siblings; expansion state is left to the
  `TreeView`. Any `Nfty.Core` change.

## 2. Components

### 2.1 Query + filtering (`ExplorerViewModel`)
- `[ObservableProperty] private string _searchQuery = "";` — `OnSearchQueryChanged` rebuilds the visible
  tree.
- The unfiltered tree is kept as the source of truth (the existing `BuildTree(book)` result); `Root`
  becomes the **filtered projection**:
  - `private ExplorerNode _fullRoot;` — set wherever `Root = BuildTree(book)` is set today (ctor and
    `ApplyBook`), then `Root = Filter(_fullRoot, SearchQuery)`.
  - `ApplyBook` must re-apply the current filter so a save/delete/add doesn't silently clear it.
- `private static ExplorerNode Filter(ExplorerNode root, string query)`:
  - blank query → return `root` unchanged (same instance; no allocation, no behavioural change).
  - otherwise rebuild: keep a **recipe** if it matches or any of its ingredients match; within a kept
    recipe, keep **all** its ingredients when the recipe itself matched, otherwise only the matching
    ingredients. The cookbook **root is always returned** (with its filtered children).
- `private static bool Matches(ExplorerNode n, string q)` — `n.Id`/`n.Name` contains `q`
  (`StringComparison.OrdinalIgnoreCase`); for an **Ingredient** node also test its variants' ids/names
  via `n.Domain is (LoadedRecipe, LoadedIngredient i)` → `i.Manifest.Variants`.
- **Selection:** if the current `SelectedNode` is filtered away, selection falls back to the root (the
  detail pane must never show a node that is no longer listed). If it survives, keep it.
- The `Search` command is **removed** (it was the stub); the toolbar button becomes a focus affordance
  handled in the view (§2.3), matching Ctrl+K.

### 2.2 Result count (small, earns its keep)
- `public string SearchSummary` — empty when the query is blank, else `"{n} match(es)"` counting kept
  recipe+ingredient nodes, so a zero-result query reads as "0 matches" rather than an unexplained empty
  tree. Notified alongside `Root`.

### 2.3 View (`ExplorerView.axaml` + code-behind)
- Replace the 🔍 button with a `TextBox` (`x:Name="SearchBox"`, `Watermark="Find recipe, ingredient,
  variant…"`) bound `SearchQuery`, plus a small muted `TextBlock` bound `SearchSummary`.
- The existing `<KeyBinding Gesture="Ctrl+K" Command="{Binding SearchCommand}" />` is replaced by
  code-behind that focuses `SearchBox` on Ctrl+K (an `Avalonia.Input.KeyBinding` cannot focus a control;
  handle `KeyDown` on the root `UserControl` and call `SearchBox.Focus()`).
- Token styles; no raw hex.

## 3. Data flow
```
type in the box → SearchQuery → Filter(_fullRoot, query) → Root (+ Roots, SearchSummary) → TreeView
                                → selection falls back to the root if it was filtered away
clear the box    → Root = _fullRoot (full tree restored)
Ctrl+K           → view focuses SearchBox
ApplyBook (save/add/delete) → _fullRoot rebuilt → the current filter re-applied
```

## 4. Error handling
None — filtering is pure and total. A query with regex/glob characters is treated literally
(`Contains`), so nothing can throw.

## 5. Testing
- **Filter:** a query matching an **ingredient** keeps its recipe (as a parent) and drops sibling
  recipes; a query matching a **recipe** keeps that recipe with **all** its ingredients; a query matching
  a **variant** keeps its ingredient (and recipe); a query matching nothing yields a root with no
  children and `SearchSummary` == "0 matches"; a blank query restores the full tree (assert the same node
  count as before filtering). Case-insensitivity on both name and id.
- **Selection fallback:** select an ingredient, type a query that excludes it ⇒ `SelectedNode` is the
  root and `CurrentDetail` is the cookbook detail (never a filtered-away node).
- **Filter survives a graph swap:** with a filter active, drive `ApplyBook` (e.g. via the editor's
  `Saved`) and assert the filter is still applied (not silently reset to the full tree).
- **No regression:** Explorer add/delete/loose suites stay green; full suite green; build 0 warnings; no
  raw hex; no `Nfty.Core` change.
- **Visual:** render the Explorer with an active filter (both themes) and confirm the tree shows only
  matches and the summary reads sensibly.
- **Manual smoke:** open a cookbook, press Ctrl+K (the box focuses), type part of an ingredient name ⇒
  only its recipe/ingredient remain; clear ⇒ the full tree returns; add/delete something with a filter
  active ⇒ the filter still applies.

## 6. Risks & escalation
- **Selection vs. filtering** is the sharp edge: leaving a filtered-away node selected would show a
  detail pane for something the tree no longer lists, and (worse) the Explorer's mutating commands act on
  `SelectedNode` — deleting a node the user can't see. The fallback-to-root rule is what prevents that;
  it is the first thing to test.
- **Filter lost on refresh:** `ApplyBook` currently assigns `Root = BuildTree(book)`; if it isn't taught
  about the filter, every save/add/delete silently clears the query while the box still shows text —
  a confusing mismatch. Covered by a test.
- **Node identity:** filtering builds **new** `ExplorerNode` instances for kept recipes (their child
  lists differ), so anything comparing nodes by reference across a filter change must use ids.
  `FindNode`/`ApplyBook` already select by id, so this is safe — do not introduce reference comparisons.
- **Ctrl+K in a TextBox:** once the box has focus, Ctrl+K must not be swallowed or re-trigger oddly;
  handling it at the `UserControl` level and simply focusing is idempotent.
