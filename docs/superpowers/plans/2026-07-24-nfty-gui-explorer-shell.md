# nfty GUI — Explorer Shell + Tree Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Style the Explorer's left column + top to the mockup — breadcrumb bar, context toolbar, and a styled navigation tree (kind marks, guide lines, selection accent bar) — with no behaviour change.

**Architecture:** Presentation-only. `ExplorerNode` gains an optional `LayerKind?` + `IsDynamic/IsStatic/IsCustom` bools (for the kind mark); `ExplorerViewModel` gains a derived `Crumbs` list. The tree kind mark is coloured via **style-class bindings** (`Classes.kdyn="{Binding IsDynamic}"` → `{DynamicResource KindDynamicBrush}`) so it stays theme-aware with no converter. `TreeViewItem` is styled via a `ControlTheme` `BasedOn` Fluent. Verified by rendering the real `ExplorerView` in the capture harness.

**Tech Stack:** .NET 10, Avalonia 11.2.3 (TreeView/TreeViewItem `ControlTheme`, style-class bindings, `ThemeDictionaries`), CommunityToolkit.Mvvm, xUnit + Avalonia.Headless.XUnit (Skia).

## Global Constraints

- **Mockup is the source of truth** (`docs/design/mockups/explorer.html`); every colour/size/radius verbatim from it. No **invented** colours. Colours via `{DynamicResource}` token keys only — no raw hex in Views/Styles.
- **Presentation-only.** No `Nfty.Core` change; no behavioural change to any existing command/keybinding. New VM/model members are derived/display state only.
- **Both themes** always; new colour via `{DynamicResource}`.
- 8-digit hex must be Avalonia `#AARRGGBB` (alpha-first) — but use token refs, not literals.
- Tests building Avalonia controls use `[AvaloniaFact]`. Build 0 warnings. Conventional commits.
- **Visual acceptance from a rendered frame** (capture harness), never from reading XAML. No golden-image tests.

## File Structure
- `src/Nfty.App/Models/ExplorerNode.cs` — add `LayerKind?` + kind bools (T1).
- `src/Nfty.App/ViewModels/ExplorerViewModel.cs` — `BuildTree` sets `LayerKind` (T1); `Crumbs` (T2).
- `src/Nfty.App/Themes/Styles.axaml` — crumbs/`.cseg`, `.kmark` kind colours (T3/T4).
- `src/Nfty.App/Themes/Controls.axaml` — `TreeViewItem` ControlTheme (T4).
- `src/Nfty.App/Views/ExplorerView.axaml` — crumbs bar + toolbar restyle (T3), tree item template (T4).
- `tests/Nfty.App.Tests/ExplorerViewModelTests.cs` — node kind + crumbs tests (T1/T2).
- `tests/Nfty.App.Tests/VisualCapture.cs` — render real ExplorerView (T5).

---

### Task 1: `ExplorerNode.LayerKind` + kind bools

**Files:**
- Modify: `src/Nfty.App/Models/ExplorerNode.cs`, `src/Nfty.App/ViewModels/ExplorerViewModel.cs`
- Test: `tests/Nfty.App.Tests/ExplorerViewModelTests.cs`

**Interfaces:**
- Produces: `ExplorerNode(string Id, string Name, ExplorerNodeKind Kind, IReadOnlyList<ExplorerNode> Children, object? Domain, LayerKind? LayerKind = null)` with computed `bool IsDynamic/IsStatic/IsCustom`.

- [ ] **Step 1: Failing test** — add to `ExplorerViewModelTests`:
```csharp
    [AvaloniaFact]
    public void Ingredient_nodes_carry_their_layer_kind()
    {
        var nav = new FakeNav();
        using var vm = new ExplorerViewModel(TwoRecipeBook(), nav, new FakeDialogs(), new FakeNotYetWired(), new ImageBridge(), EditorFactory(nav));
        var recipe = vm.Root.Children[0];
        var ingredient = recipe.Children[0];
        Assert.Null(vm.Root.LayerKind);            // cookbook node
        Assert.Null(recipe.LayerKind);             // recipe node
        Assert.Equal(Nfty.Core.Model.LayerKind.Custom, ingredient.LayerKind);  // TwoRecipeBook ingredients are Custom
        Assert.True(ingredient.IsCustom);
    }
```

- [ ] **Step 2: Run — fails** (`ExplorerNode` has no `LayerKind`): `dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~Ingredient_nodes_carry_their_layer_kind`

- [ ] **Step 3: Implement** — `ExplorerNode.cs`:
```csharp
using Nfty.Core.Model;

namespace Nfty.App.Models;

public enum ExplorerNodeKind { CookBook, Recipe, Ingredient }

/// <summary>One tree node. <see cref="Domain"/> carries the Core object this node stands for
/// (LoadedCookBook / LoadedRecipe / LoadedIngredient). <see cref="LayerKind"/> is the ingredient's
/// kind on Ingredient nodes (null otherwise), used to colour the tree kind mark.</summary>
public record ExplorerNode(string Id, string Name, ExplorerNodeKind Kind,
    IReadOnlyList<ExplorerNode> Children, object? Domain, LayerKind? LayerKind = null)
{
    public bool IsDynamic => LayerKind == Nfty.Core.Model.LayerKind.Dynamic;
    public bool IsStatic => LayerKind == Nfty.Core.Model.LayerKind.Static;
    public bool IsCustom => LayerKind == Nfty.Core.Model.LayerKind.Custom;
}
```
In `ExplorerViewModel.BuildTree`, the ingredient-node construction passes the kind (last positional arg):
```csharp
                .Select(id => new ExplorerNode(id, ingById[id].Manifest.Name,
                    ExplorerNodeKind.Ingredient, Array.Empty<ExplorerNode>(), (r, ingById[id]),
                    ingById[id].Manifest.Kind))
```

- [ ] **Step 4: Run — passes.** Then `dotnet test tests/Nfty.App.Tests --nologo` (whole suite green; the extra optional param doesn't break existing `new ExplorerNode(...)` sites).

- [ ] **Step 5: Commit**
```bash
git add src/Nfty.App/Models/ExplorerNode.cs src/Nfty.App/ViewModels/ExplorerViewModel.cs tests/Nfty.App.Tests/ExplorerViewModelTests.cs
git commit -m "feat(gui): ExplorerNode carries ingredient LayerKind for the tree kind mark"
```

---

### Task 2: `ExplorerViewModel.Crumbs`

**Files:**
- Modify: `src/Nfty.App/ViewModels/ExplorerViewModel.cs`
- Test: `tests/Nfty.App.Tests/ExplorerViewModelTests.cs`

**Interfaces:**
- Produces: `public record Crumb(string Text, bool Active);` and `IReadOnlyList<Crumb> Crumbs { get; }` on `ExplorerViewModel`, recomputed on `SelectedNode` change.

- [ ] **Step 1: Failing test**:
```csharp
    [AvaloniaFact]
    public void Crumbs_follow_the_selected_node_path()
    {
        var nav = new FakeNav();
        using var vm = new ExplorerViewModel(TwoRecipeBook(), nav, new FakeDialogs(), new FakeNotYetWired(), new ImageBridge(), EditorFactory(nav));
        var book = TwoRecipeBook();

        // nothing selected → just the cookbook, active
        Assert.Equal(new[] { (vm.Root.Name, true) }, vm.Crumbs.Select(c => (c.Text, c.Active)));

        var recipe = vm.Root.Children[0];
        vm.SelectNodeCommand.Execute(recipe);
        Assert.Equal(new[] { (vm.Root.Name, false), (recipe.Name, true) }, vm.Crumbs.Select(c => (c.Text, c.Active)));

        var ingredient = recipe.Children[0];
        vm.SelectNodeCommand.Execute(ingredient);
        Assert.Equal(new[] { (vm.Root.Name, false), (recipe.Name, false), (ingredient.Name, true) },
            vm.Crumbs.Select(c => (c.Text, c.Active)));
    }
```

- [ ] **Step 2: Run — fails** (no `Crumbs`).

- [ ] **Step 3: Implement** — add to `ExplorerViewModel`:
```csharp
public record Crumb(string Text, bool Active);
```
Add a property + recompute. Since crumbs derive from `SelectedNode` (whose `Domain` carries the recipe/ingredient context), build the path from the node kind:
```csharp
    public IReadOnlyList<Crumb> Crumbs { get; private set; } = Array.Empty<Crumb>();

    private void RebuildCrumbs()
    {
        var parts = new List<string> { Root.Name };
        switch (SelectedNode?.Kind)
        {
            case ExplorerNodeKind.Recipe:
                parts.Add(SelectedNode.Name);
                break;
            case ExplorerNodeKind.Ingredient when SelectedNode.Domain is (LoadedRecipe r, LoadedIngredient i):
                parts.Add(r.Manifest.Name);
                parts.Add(i.Manifest.Name);
                break;
        }
        Crumbs = parts.Select((t, idx) => new Crumb(t, idx == parts.Count - 1)).ToList();
        OnPropertyChanged(nameof(Crumbs));
    }
```
Call `RebuildCrumbs()` at the end of the ctor (after `Root` is built) and at the end of `OnSelectedNodeChanged`. Add `using System.Collections.Generic;`/`using System.Linq;` if not present.

- [ ] **Step 4: Run — passes.** Whole App suite green.

- [ ] **Step 5: Commit**
```bash
git add src/Nfty.App/ViewModels/ExplorerViewModel.cs tests/Nfty.App.Tests/ExplorerViewModelTests.cs
git commit -m "feat(gui): ExplorerViewModel derives breadcrumb path from selection"
```

---

### Task 3: Crumbs bar + toolbar restyle (View)

**Files:**
- Modify: `src/Nfty.App/Views/ExplorerView.axaml`, `src/Nfty.App/Themes/Styles.axaml`

**Interfaces:**
- Consumes: `Crumbs` (T2), `AddLabel` + existing commands.
- Produces: `.cseg` crumb style; the crumbs `ItemsControl` + restyled toolbar in `ExplorerView`.

- [ ] **Step 1: Add crumb style to `Styles.axaml`** (mockup `.cseg` line 104–108; `.crumbs` mono 12.5 muted):
```xml
  <Style Selector="TextBlock.cseg">
    <Setter Property="FontFamily" Value="{DynamicResource MonoFontFamily}" />
    <Setter Property="FontSize" Value="12.5" />
    <Setter Property="Foreground" Value="{DynamicResource FgMutedBrush}" />
    <Setter Property="Padding" Value="7,3" />
    <Setter Property="VerticalAlignment" Value="Center" />
  </Style>
  <Style Selector="TextBlock.cseg.active">
    <Setter Property="Foreground" Value="{DynamicResource FgBrush}" />
    <Setter Property="FontWeight" Value="SemiBold" />
  </Style>
```

- [ ] **Step 2: Restyle `ExplorerView.axaml`** — replace the current `RowDefinitions="Auto,*"` grid so a crumbs row sits above the toolbar, and restyle the toolbar. Keep every command binding and the `Ctrl+K` keybinding:
```xml
  <Grid RowDefinitions="Auto,Auto,*">
    <!-- Crumbs bar -->
    <ItemsControl Grid.Row="0" ItemsSource="{Binding Crumbs}" Margin="14,10,14,0">
      <ItemsControl.ItemsPanel>
        <ItemsPanelTemplate><StackPanel Orientation="Horizontal" Spacing="2" /></ItemsPanelTemplate>
      </ItemsControl.ItemsPanel>
      <ItemsControl.ItemTemplate>
        <DataTemplate x:DataType="vm:Crumb">
          <StackPanel Orientation="Horizontal" Spacing="2">
            <TextBlock Text="›" Classes="cseg" Opacity="0.45"
                       IsVisible="{Binding !Active, Converter={x:Static conv:CommonConverters.NotFirst}}" />
            <TextBlock Text="{Binding Text}" Classes="cseg" Classes.active="{Binding Active}" />
          </StackPanel>
        </DataTemplate>
      </ItemsControl.ItemTemplate>
    </ItemsControl>

    <!-- Context toolbar (.exp-toolbar): gap 10, padding 10/14, bottom hairline -->
    <Border Grid.Row="1" BorderBrush="{DynamicResource LineBrush}" BorderThickness="0,0,0,1" Padding="14,10">
      <StackPanel Orientation="Horizontal" Spacing="10">
        <Button Content="🔍" Command="{Binding SearchCommand}" Classes="icon" ToolTip.Tip="Search (Ctrl+K)" />
        <Button Content="{Binding AddLabel}" Command="{Binding AddCommand}" Classes="accent" />
        <Button Content="Delete" Command="{Binding DeleteSelectedCommand}" Classes="tbtn" />
        <Button Content="Import" Command="{Binding ImportCommand}" Classes="tbtn" />
        <Button Content="🔒" Command="{Binding ToggleLockCommand}" Classes="icon" ToolTip.Tip="Toggle lock" />
      </StackPanel>
    </Border>

    <Grid Grid.Row="2" ColumnDefinitions="260,*">
      <!-- Tree (styled in Task 4) -->
      <TreeView Grid.Column="0" Name="Tree" ItemsSource="{Binding Roots}"
                Background="{DynamicResource PanelBrush}" Margin="12,10,12,12">
        <TreeView.ItemTemplate>
          <TreeDataTemplate x:DataType="models:ExplorerNode" ItemsSource="{Binding Children}">
            <TextBlock Text="{Binding Name}" />
          </TreeDataTemplate>
        </TreeView.ItemTemplate>
      </TreeView>
      <ContentControl Grid.Column="1" Content="{Binding CurrentDetail}" Margin="0,0,12,12" />
    </Grid>
  </Grid>
```
The `›` separator should not appear before the first crumb. Simplest correct approach (no converter): drop the `IsVisible`/`conv:` line above and instead render the separator on **active=false OR index>0**. To keep it robust and converter-free, render the separator only when the crumb is **not** the first — implement by binding the separator's `IsVisible` to a new `bool Leading` on `Crumb` (false for the first). Update `Crumb` to `record Crumb(string Text, bool Active, bool Leading)` and set `Leading = idx > 0` in `RebuildCrumbs` (Task 2). Then:
```xml
            <TextBlock Text="›" Classes="cseg" Opacity="0.45" IsVisible="{Binding Leading}" />
```
Remove the `xmlns:conv` reference if not otherwise used. (Add `xmlns:vm="using:Nfty.App.ViewModels"` — already present.)

- [ ] **Step 3: Update `Crumb` + `RebuildCrumbs`** (Task 2 file) to the 3-arg record and set `Leading = idx > 0`; update the Task-2 test tuples to include `Leading` (`(Text, Active, Leading)`), first crumb `Leading=false`, others `true`.

- [ ] **Step 4: Build + suite**: `dotnet build src/Nfty.Desktop --nologo` (0 warnings); `dotnet test tests/Nfty.App.Tests --nologo` (green, Task-2 test updated).

- [ ] **Step 5: Commit**
```bash
git add src/Nfty.App/Views/ExplorerView.axaml src/Nfty.App/Themes/Styles.axaml src/Nfty.App/ViewModels/ExplorerViewModel.cs tests/Nfty.App.Tests/ExplorerViewModelTests.cs
git commit -m "feat(gui): Explorer crumbs bar + restyled context toolbar"
```

---

### Task 4: Styled tree (TreeViewItem theme + node template)

**Files:**
- Modify: `src/Nfty.App/Themes/Controls.axaml`, `src/Nfty.App/Themes/Styles.axaml`, `src/Nfty.App/Views/ExplorerView.axaml`

**Interfaces:**
- Consumes: `ExplorerNode` (`Name`, `IsDynamic/IsStatic/IsCustom`, `Kind`), tokens.
- Produces: a `TreeViewItem` ControlTheme + `.node`/`.kmark` styles + the tree item template with the kind mark and selection accent bar.

**Doc-pull (objective):** pull Avalonia 11.2 docs (Context7 `/avaloniaui/avalonia-docs`, query "TreeViewItem ControlTheme style selected pointerover template parts expander indentation") for the correct `TreeViewItem` template-part names (selection background part, expander toggle, indentation) before writing. The mockup targets: node row `.node` (padding 6/8, gap 8, `RadiusSm`), `:pointerover` → `BgAlt2Brush`, selected → `AccentWashBrush` bg + a 2px left accent bar (`box-shadow: inset 2px 0 0 accent`), branch guide line `GuideBrush` (`:pointerover` → `GuideHiBrush`), root label mono SemiBold. If the selected-state background is painted on a template part (as with Button/inputs), set it there; realise the 2px accent bar as a leading `Border` in the node template if the template part can't express it.

- [ ] **Step 1: Kind-mark + node styles in `Styles.axaml`**:
```xml
  <Style Selector="Border.kmark">
    <Setter Property="Width" Value="8" />
    <Setter Property="Height" Value="8" />
    <Setter Property="CornerRadius" Value="8" />
    <Setter Property="VerticalAlignment" Value="Center" />
    <Setter Property="Background" Value="{DynamicResource FgMutedBrush}" />
  </Style>
  <Style Selector="Border.kmark.kdyn"><Setter Property="Background" Value="{DynamicResource KindDynamicBrush}" /></Style>
  <Style Selector="Border.kmark.kstat"><Setter Property="Background" Value="{DynamicResource KindStaticBrush}" /></Style>
  <Style Selector="Border.kmark.kcust"><Setter Property="Background" Value="{DynamicResource KindCustomBrush}" /></Style>
```

- [ ] **Step 2: `TreeViewItem` ControlTheme in `Controls.axaml`** (per the doc-pull). Base it on Fluent; set the node row background to transparent, `:pointerover` and `:selected` backgrounds on the confirmed template part to `BgAlt2Brush` / `AccentWashBrush`, and the guide-line indentation. Example skeleton (fill the real part names from the doc-pull):
```xml
  <ControlTheme x:Key="{x:Type TreeViewItem}" TargetType="TreeViewItem"
                BasedOn="{StaticResource {x:Type TreeViewItem}}">
    <Setter Property="Padding" Value="8,6" />
    <Setter Property="CornerRadius" Value="{DynamicResource RadiusSm}" />
    <!-- :pointerover / :selected backgrounds on the confirmed template part, per doc-pull -->
  </ControlTheme>
```

- [ ] **Step 3: Tree item template in `ExplorerView.axaml`** — replace the `TreeDataTemplate` content with the node row: a 2px accent left bar (visible when selected), the kind mark (ingredient nodes), and the label (root = mono SemiBold):
```xml
        <TreeDataTemplate x:DataType="models:ExplorerNode" ItemsSource="{Binding Children}">
          <StackPanel Orientation="Horizontal" Spacing="8">
            <Border Classes="kmark"
                    Classes.kdyn="{Binding IsDynamic}"
                    Classes.kstat="{Binding IsStatic}"
                    Classes.kcust="{Binding IsCustom}"
                    IsVisible="{Binding LayerKind, Converter={x:Static ObjectConverters.IsNotNull}}" />
            <TextBlock Text="{Binding Name}" VerticalAlignment="Center" />
          </StackPanel>
        </TreeDataTemplate>
```
(Root/recipe nodes have `LayerKind == null` so the dot is hidden. If the mockup shows a distinct root/recipe glyph, add it here per the mockup; keep it minimal and token-coloured.)

- [ ] **Step 4: Build + suite + a style-load guard**: `dotnet build src/Nfty.Desktop --nologo` (0 warnings — proves the TreeViewItem ControlTheme parses); `dotnet test tests/Nfty.App.Tests --nologo` (green). Add a `[AvaloniaFact]` in `ThemeResourceTests` that constructs a `TreeView { ItemsSource = new[]{ node } }` via `StyledHost.Show` and asserts it renders without throwing (catches a broken tree theme).

- [ ] **Step 5: Commit**
```bash
git add src/Nfty.App/Themes/Controls.axaml src/Nfty.App/Themes/Styles.axaml src/Nfty.App/Views/ExplorerView.axaml tests/Nfty.App.Tests/ThemeResourceTests.cs
git commit -m "feat(gui): styled Explorer tree (kind marks, guide lines, selection)"
```

---

### Task 5: Render the real Explorer + visual verification

**Files:**
- Modify: `tests/Nfty.App.Tests/VisualCapture.cs`

- [ ] **Step 1: Add an Explorer capture** to `VisualCapture.cs` — a second `[AvaloniaFact]` (guarded by the same `NFTY_CAPTURE` env) that builds the real `ExplorerView` bound to an `ExplorerViewModel(TwoRecipeBook)` with a node selected, and saves `explorer-{variant}.png` in both themes:
```csharp
    [AvaloniaFact]
    public void Capture_explorer()
    {
        if (Dir is null) return;
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var nav = new FakeNav();
            var vm = new ExplorerViewModel(ExplorerViewModelTests.TwoRecipeBook(), nav, new FakeDialogs(),
                new FakeNotYetWired(), new ImageBridge(), ExplorerViewModelTests.EditorFactory(nav));
            vm.SelectNodeCommand.Execute(vm.Root.Children[0].Children[0]);   // select an ingredient
            var view = new Views.ExplorerView { DataContext = vm };
            var window = new Window { RequestedThemeVariant = variant, Content = view, Width = 900, Height = 560 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame()!.Save(Path.Combine(Dir!, $"explorer-{variant.Key.ToString()!.ToLowerInvariant()}.png"));
            vm.Dispose();
        }
    }
```
(`FakeNav`/`FakeDialogs`/`FakeNotYetWired` are the existing test doubles; `TwoRecipeBook`/`EditorFactory` are `internal static` in `ExplorerViewModelTests` — same assembly. Add `using Nfty.App.ViewModels;`/`using Nfty.App;` as needed.)

- [ ] **Step 2: Render + LOOK** (the acceptance step):
```
CAP="C:/Users/Corde/AppData/Local/Temp/claude/M--Repositories-nfty/0f27fa31-ff64-4405-96f9-eecf4e89d6fd/scratchpad"
NFTY_CAPTURE=1 NFTY_CAPTURE_DIR="$CAP" dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~Capture_explorer --nologo
```
Read `$CAP/explorer-light.png` and `$CAP/explorer-dark.png` and view them. Compare to `docs/design/mockups/explorer.html`: crumbs path (CookBook › Recipe › Ingredient, last bold), toolbar (Add accent + Delete/Import + search/lock icons), tree node rows, the selected node's accent-wash + left accent bar, branch guide lines, and the ingredient kind mark colour. Iterate (fix styles) until faithful in BOTH themes. Report what you saw.

- [ ] **Step 3: Full verify**: `dotnet build nfty.sln --nologo` (0 warnings); `dotnet test nfty.sln --nologo` (all green). `grep -rniE "#[0-9a-f]{6}" src/Nfty.App/Views/ExplorerView.axaml src/Nfty.App/Themes/Styles.axaml src/Nfty.App/Themes/Controls.axaml` → no new raw hex.

- [ ] **Step 4: Commit** (only if capture-driven style fixups were needed beyond Tasks 3–4):
```bash
git add tests/Nfty.App.Tests/VisualCapture.cs src/Nfty.App
git commit -m "test(gui): render the real Explorer shell for visual verification"
```

---

## Self-Review
- **Spec coverage:** §2.1 crumbs → T2 (+ T3 view). §2.2 toolbar → T3. §2.3 tree (node/hover/selection/guide/kind mark/root) → T4; kind data → T1. §4 tests → T1/T2 (VM), T4 (style-load), T5 (visual). §5 out-of-scope (detail bodies) untouched. No `Nfty.Core`/behaviour change in any task.
- **Placeholder scan:** every step has concrete code. The two doc-pull points (TreeViewItem template parts) are concrete verification steps with a fallback (leading accent `Border`), not TBDs.
- **Type consistency:** `ExplorerNode(... , LayerKind? LayerKind = null)` + `IsDynamic/IsStatic/IsCustom` defined T1, consumed T4/T5. `Crumb(string Text, bool Active, bool Leading)` finalised in T3 (T2 introduces it, T3 adds `Leading`); the T2 test is updated in T3 to match — called out explicitly so the record shape is consistent by end of T3. `Crumbs`/`SelectNodeCommand`/`Root`/`EditorFactory`/`TwoRecipeBook` names match the current code.
