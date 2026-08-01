# nfty GUI — Open a loose `.rcp` (read-only) (B2) design spec

**Date:** 2026-08-01
**Status:** Approved (design), pending implementation planning
**Scope:** Second "loose" slice (B2). Open a standalone `.rcp` (a recipe not inside a cookbook) as a
read-only one-recipe cookbook in the Explorer. Wires the last remaining Landing **Import** branch. Builds
on B1 (`LooseWorkspace`) and A2 (the Explorer + its read-only-when-no-source gating). No Kitchen screen.

## 0. Program bar
Rock-solid, efficient; best practices; pull docs (Context7) rather than assume any library API; escalate
anything off. Reuse the whole Explorer + recipe/ingredient detail unchanged; read-only falls out of the
synthetic book having no source `.cbk`. No `Nfty.Core` change.

## 1. Goals & non-goals
**Goals**
- Landing **Import** of a `.rcp` reads it, wraps it in a synthetic single-recipe cookbook
  (`LooseWorkspace.WrapRecipe`), opens it into the session **with no source path**, and navigates to the
  Explorer — so the user can browse the loose recipe, its layer order, rules, ingredients, and the recipe
  hero preview, exactly like a recipe inside a cookbook.
- The view is **read-only**: because the wrapped book has no source `.cbk` (`SourcePath` is null), the
  Explorer's add/delete and the Ingredient Editor's Save are already disabled — no new gating is written.

**Non-goals (this slice)**
- Editing/saving a loose `.rcp` (there is no `.rcp` write-back seam this slice; opening it is view-only).
  The loose `.igt` path (B1, shipped). The New-Recipe "Loose Kitchen" create path (B3). A Kitchen screen.
  Any `Nfty.Core` change.

## 2. Components

### 2.1 `LooseWorkspace.WrapRecipe` (extends the B1 helper)
Add alongside `WrapIngredient`:
```
static LoadedCookBook WrapRecipe(LoadedRecipe recipe)
```
- **Canvas:** the first variant image found across the recipe's ingredients
  (`recipe.Ingredients.SelectMany(i => i.VariantImages.Values).FirstOrDefault()`) → its `(Width, Height)`.
  If the recipe has no ingredient images at all (degenerate/empty recipe), fall back to a default
  `new Dimensions(512, 512)` (the recipe still opens; its hero is best-effort and tolerates a
  non-generatable recipe, per the A2c fix).
- Builds `new LoadedCookBook { Manifest = new CookBookManifest("loose", recipe.Manifest.Name, canvas,
  new Collection(recipe.Manifest.Name, "", "L"), new Dictionary<string,double> { [recipe.Manifest.Id] =
  100 }), Recipes = new[] { recipe } }`. The **recipe is kept as-is** (its own id, LayerOrder, Rules,
  Ingredients). `RecipeWeights` keys the recipe's real id (not `"loose"`) so the cookbook is internally
  consistent.
- The returned book **owns** the recipe → its ingredients → their images (disposing the book disposes
  them). Ownership is the session's once opened (§2.2) — the standard opened-book lifecycle.

### 2.2 Landing Import (`LandingViewModel`)
- Replace the `.rcp` arm of `Import` (today: `_notify.Report("… needs the Kitchen (coming soon)")`) with an
  `OpenLooseRecipe(path)`:
  ```csharp
  private void OpenLooseRecipe(string path)
  {
      LoadedRecipe recipe;
      try { recipe = RecipeArchive.Read(path); }
      catch (Exception ex) { ShowError("Could not open", ex.Message); return; }
      var book = LooseWorkspace.WrapRecipe(recipe);
      _session.Open(book, null);            // no source .cbk → the Explorer is read-only
      _nav.To(_explorerFactory(book));
  }
  ```
  and dispatch: `.cbk` → `OpenPath`; `.igt` → `OpenLooseIngredient` (B1); `.rcp` → `OpenLooseRecipe`;
  anything else → the existing stub (now effectively unreachable for the three known kinds — keep it as a
  guard).
- **Session ownership:** unlike the loose *ingredient* editor (which deliberately avoided the session),
  opening a loose *recipe* uses the normal `session.Open` — it replaces the currently-open cookbook, exactly
  as opening a `.cbk` does (Import is an open-a-document action). `Open` disposes the previous book; the
  session then owns the wrapped book. No bespoke ownership handling needed.
- `RecipeArchive` is in `Nfty.Core.Formats` (already imported by Landing).

### 2.3 View
No new views — the Explorer + recipe/ingredient detail render the loose recipe exactly as a cookbook
recipe. The read-only-ness is emergent (disabled add/delete/save), not a new visual state this slice.

## 3. Data flow
```
Import(.rcp) → RecipeArchive.Read(path) → recipe
  → book = LooseWorkspace.WrapRecipe(recipe)     // synthetic 1-recipe cookbook, canvas from variants
  → session.Open(book, null)                     // no source → read-only; session owns the book
  → nav.To(explorerFactory(book))                // browse recipe / layers / rules / ingredients / hero
```

## 4. Error handling
- Unreadable/invalid `.rcp` → error dialog on import, nothing opens (the previous session book is untouched
  because `session.Open` is only called after a successful `Read`).
- A non-generatable recipe (e.g. empty layer order) → the recipe hero is best-effort (A2c fix) and shows a
  blank placeholder rather than crashing.

## 5. Testing
- **`WrapRecipe`:** canvas equals the recipe's first variant image size; a recipe with no images falls back
  to 512×512; the single recipe is the same instance; `RecipeWeights` keys the recipe's id.
- **Import `.rcp`** (`[AvaloniaFact]`, real temp `.rcp`): `Import` opens the wrapped book into the session
  (`session.Current` is the wrapped book, `SourcePath` null) and navigates the nav to an
  `ExplorerViewModel` whose tree shows the recipe; an unreadable `.rcp` shows an error and does not change
  the session.
- **Read-only:** in the loose-recipe Explorer, `DeleteSelectedCommand.CanExecute` is false even after
  toggling edit mode (no source), and `AddCommand` falls to the stub — confirming no accidental mutation of
  a source-less book.
- **No regression:** B1 (loose `.igt`), A2 (add/delete), and the Landing suites stay green; full suite
  green; build 0 warnings; no raw hex.
- **Manual smoke:** File → Import → pick a `.rcp` → it opens in the Explorer with the recipe selected, its
  layers/rules/hero visible; add/delete are disabled; a `.cbk` still opens normally and a `.igt` opens the
  editor.

## 6. Risks & escalation
- **Replacing the open cookbook:** importing a loose `.rcp` calls `session.Open`, which disposes the
  user's currently-open cookbook — this is intended "open a document" behavior (identical to importing a
  `.cbk`), but note it in the manual smoke so it isn't surprising.
- **Canvas from variants:** a recipe's ingredients may legitimately have differing-size variants only if
  the recipe is invalid; for a valid `.rcp` all variants share the cookbook-canvas size, so the first
  image's dimensions are correct. The empty-recipe fallback (512×512) only affects a degenerate recipe that
  can't generate anyway.
- **Read-only is emergent, not enforced by a flag:** it relies on `SourcePath == null` gating add/delete
  (Explorer) and Save (editor). A future change that lets a source-less book be mutated would silently make
  the loose recipe editable with nowhere to save — the read-only test guards the current invariant; if the
  gating model changes, revisit.
