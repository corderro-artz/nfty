# nfty GUI — Explorer Shell + Tree (visual-fidelity, design spec)

**Date:** 2026-07-24
**Status:** Approved (design), pending implementation planning
**Scope:** First of two Explorer visual-fidelity slices. Compose the merged style foundation into the
**left column + top** of `docs/design/mockups/explorer.html`: the breadcrumb bar, the context toolbar,
and a styled navigation tree. The three detail bodies + rails are **Slice B** (separate spec).
**Builds on:** the merged visual-foundation slice (`Themes/Tokens.axaml`, `Styles.axaml`, `Controls.axaml`,
the render-capture harness `tests/Nfty.App.Tests/VisualCapture.cs`). `ExplorerViewModel`/`ExplorerView`
already exist and are constructible in tests (see `SmokeTests`).

## 0. Program bar
Near-identical to the mockup, functioning logically and efficiently; best practices; **pull Avalonia
11.2 docs rather than assume** any API; **confirm visual parity from a rendered screenshot** (the
capture harness), never from reading XAML; escalate anything that doesn't look right. Mockup is the
pixel source of truth; every colour/size/radius is verbatim from `explorer.html`.

## 1. Goals & non-goals
**Goals**
- A breadcrumb bar reflecting the selected node's path (CookBook › Recipe › Ingredient).
- The context toolbar restyled to the mockup's `.exp-toolbar`.
- The `TreeView` styled to the mockup: node rows, hover, selection (accent wash + left accent bar),
  branch guide lines, root mono label, and a kind-coloured mark on ingredient nodes.
- No behaviour change: every existing command/binding is preserved; only presentation is added.

**Non-goals (this slice)**
- The three detail bodies (CookBook / Recipe / Ingredient) and their rails — **Slice B**.
- Cook / edit / add / delete / import real behaviour — remain the existing stubs/nav.
- Any `Nfty.Core` change. Any behaviour change to existing commands.

## 2. Components

### 2.1 Crumbs bar (`ExplorerViewModel.Crumbs`)
A new **presentation-only** property on `ExplorerViewModel`:
```
public record Crumb(string Text, bool Active);
public IReadOnlyList<Crumb> Crumbs { get; }   // recomputed on SelectedNode change
```
Derivation from the selected node (using `ExplorerNode.Kind` + `Domain`, which already carry the
`LoadedCookBook`/`LoadedRecipe`/`(LoadedRecipe,LoadedIngredient)`):
- always: `[CookBook name]`
- if a Recipe node is selected: `+ [recipe name]`
- if an Ingredient node is selected: `+ [recipe name] + [ingredient name]`
- nothing selected (or root): just the cookbook name, active.
The **last** segment is `Active = true`. Recomputed in `OnSelectedNodeChanged` (alongside the existing
`CurrentDetail` switch) via `OnPropertyChanged(nameof(Crumbs))` or an `[ObservableProperty]`-backed
field. This adds no command and no behavioural effect — it is a derived label.

**View:** a mono row above the tree/detail split: each `Crumb` as a `.cseg`-styled element
(mono 12.5px; padding `3,7`; `RadiusXs`; `FgMutedBrush`, `Active` → `FgBrush` + SemiBold), separated by
`›` glyphs at reduced opacity (mockup `.sep` opacity .45). Rendered with an `ItemsControl`
(horizontal `StackPanel` panel) bound to `Crumbs`.

### 2.2 Context toolbar
Restyle the existing toolbar row (`ExplorerView.axaml`: Search / Add / Delete / Import / lock) to the
mockup's `.exp-toolbar` (flex, gap 10, padding `10,14`, bottom hairline `LineBrush`): **Add** = `accent`
button showing the context-aware `AddLabel` (already bound); **Delete**/**Import** = `tbtn`; **search**
and **lock** = `icon` buttons. Keep every `Command` binding exactly as-is (`SearchCommand`, `AddCommand`,
`DeleteSelectedCommand`, `ImportCommand`, `ToggleLockCommand`) and the `Ctrl+K` keybinding. No VM change.

### 2.3 Styled tree
Style the `TreeView`/`TreeViewItem` to the mockup (a `ControlTheme` for `TreeViewItem` `BasedOn` the
Fluent theme, in `Themes/Controls.axaml`, matching the input-restyle pattern; pull the Avalonia 11.2
docs for the correct template parts / expander before writing):
- **Node row** (`.node`, line 191): horizontal, gap 8, padding `6,8`, `RadiusSm`; `:pointerover` →
  `BgAlt2Brush`; **selected** → `AccentWashBrush` background + a 2px left **accent bar**
  (mockup `box-shadow: inset 2px 0 0 accent`; realise with a left `BorderThickness`/accent `Border` or a
  2px accent rectangle in the item template).
- **Guide lines** (`.branch`, line 189): child levels indented with a left guide line
  `BorderBrush=GuideBrush` (`:pointerover` → `GuideHiBrush`).
- **Root label** (`.node.root`, line 200): mono, SemiBold.
- **Kind mark** on **ingredient** nodes: a small leading marker coloured by the ingredient's
  `LayerKind` — `KindDynamicBrush` / `KindStaticBrush` / `KindCustomBrush` (mockup `.kmark` dyn/stat/cust,
  lines 123–127). CookBook/Recipe nodes get a neutral leading glyph (or none) — match the mockup's tree
  markers.
- Expander/twist chevron styled subtly (Fluent default acceptable if it reads clean; refine only if it
  clashes).

**Model addition (presentation-only):** `ExplorerNode` gains `LayerKind? LayerKind` (null for CookBook
and Recipe nodes; the ingredient's `Manifest.Kind` for Ingredient nodes), set in
`ExplorerViewModel.BuildTree`. The tree item template maps it to the kind brush (a small converter or a
kind→brush `IValueConverter`, mirroring the existing converter pattern). This does not change any
existing behaviour — it only supplies the dot colour.

## 3. Data flow / behaviour
Presentation only. `SelectedNode` changing already drives `CurrentDetail`; this slice additionally
derives `Crumbs`. `SelectNode`/`ToggleLock`/`Add`/`Delete`/`Import`/`Search` keep their current
(stub/ui-state) behaviour. Both `ThemeVariant`s must stay correct — all new colour via
`{DynamicResource}` token keys.

## 4. Testing & acceptance
- **VM test** (`[Fact]`/`[AvaloniaFact]` as needed): building an `ExplorerViewModel` from the
  `TwoRecipeBook` fixture and selecting a cookbook / recipe / ingredient node yields the correct
  `Crumbs` sequence (names + which segment is active). `ExplorerNode.LayerKind` is null for
  cookbook/recipe nodes and equals the ingredient's kind for ingredient nodes.
- **Style resolution** test: the `TreeViewItem` theme loads and a representative node resolves its
  selected/hover brushes (guard against a broken `ControlTheme`), consistent with the existing
  `ThemeResourceTests` approach.
- **Visual acceptance (required):** extend `VisualCapture` to render the **real** `ExplorerView` bound to
  an `ExplorerViewModel(TwoRecipeBook)` (fakes for nav/dialog/notify/bridge/editor-factory as in
  `SmokeTests`), with a node selected, and save a PNG in both themes. **View the render and compare to
  `explorer.html`**: crumbs path, toolbar, tree node rows, selection accent bar + wash, guide lines, the
  ingredient kind marks. Iterate until faithful. No golden-image tests.
- Build 0 warnings; the full existing suite stays green (no behaviour changed). No raw hex outside
  `Tokens.axaml`.

## 5. Out of scope
- Detail bodies + rails (Slice B); Cook/edit/add/delete/import behaviour; `Nfty.Core`; mobile heads.
- Search results / command palette; drag-reorder of the tree.

## 6. Risks & escalation
- `TreeViewItem` template parts (expander toggle, indentation, selection visual) are an objective
  Avalonia API — pull the 11.2 docs; do not assume. If the inset-left accent bar can't be done cleanly
  via the item template, realise it with a leading 2px accent `Border` in the node row and note it.
- If rendering the full `ExplorerView` headlessly needs a service the fakes don't cover, use the
  `SmokeTests` construction as the template; escalate if a real service is unexpectedly required.
