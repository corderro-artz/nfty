# nfty GUI — Explorer Detail Bodies + Rails Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Style the three Explorer detail bodies to the mockup — CookBook (id-chips, metric tiles, mint-distribution bar), Recipe (hero + layer table + rules rail), Ingredient (hero + variant table + colorways rail) — presentation-only.

**Architecture:** Small presentation additions to the existing detail VMs (a deterministic per-recipe `SegmentColor`, a structured `RuleRow`, and `ColorwayAxis` rows), then restyle each detail View to the mockup using new shared styles (`.metric`/`.distbar`/`table.data`/`.rules-panel`/`.cw-panel`/`.rhero`/`.vhero`). Fidelity verified by rendering each real detail view in the capture harness.

**Tech Stack:** .NET 10, Avalonia 11.2.3 (styled `Grid`/`ItemsControl` tables — no `DataGrid`), CommunityToolkit.Mvvm, `Nfty.Core.Imaging.ColorConvert`, xUnit + Avalonia.Headless.XUnit (Skia).

## Global Constraints
- **Mockup = source of truth** (`docs/design/mockups/explorer.html`); values verbatim. Colours via `{DynamicResource}` token keys only — NO raw hex in Views/Styles (8-digit token hex is `#AARRGGBB`).
- **Presentation-only:** no `Nfty.Core` change; no behavioural change to Cook/Reroll/OpenIngredient/SortBy/Delete/Edit. New VM members are derived/display state.
- **Both themes** always via `{DynamicResource}`.
- Tests building Avalonia controls use `[AvaloniaFact]`. Build 0 warnings. Conventional commits.
- **Visual acceptance from a rendered frame** (capture harness), never from XAML. No golden-image tests.
- Agents: speak caveman-ultra terse in chat; code/commits/reports normal prose.

## File Structure
- `src/Nfty.App/ViewModels/CookBookDetailViewModel.cs` — `RecipeShareRow.SegmentColor` (T1).
- `src/Nfty.App/ViewModels/RecipeDetailViewModel.cs` — structured `RuleRow` (T2).
- `src/Nfty.App/ViewModels/IngredientDetailViewModel.cs` — `ColorwayAxis` rows (T3).
- `src/Nfty.App/Themes/Styles.axaml` — shared detail styles (T4).
- `src/Nfty.App/Views/{CookBookDetailView,RecipeDetailView,IngredientDetailView}.axaml` — T5/T6/T7.
- `tests/Nfty.App.Tests/{CookBookDetailViewModelTests,RecipeDetailViewModelTests,IngredientDetailViewModelTests,VisualCapture}.cs` — T1/T2/T3/T8.

---

### Task 1: `RecipeShareRow.SegmentColor` (deterministic per-recipe colour)

**Files:** Modify `src/Nfty.App/ViewModels/CookBookDetailViewModel.cs`; Test `tests/Nfty.App.Tests/CookBookDetailViewModelTests.cs`.

**Interfaces:** Produces `RecipeShareRow(string Name, double SharePercent, string DnaSpaceText, Avalonia.Media.Color SegmentColor)`.

- [ ] **Step 1: Failing test** (add to `CookBookDetailViewModelTests`):
```csharp
    [AvaloniaFact]
    public void Recipe_segment_colour_is_deterministic_per_id()
    {
        var book = /* existing fixture builder in this test file (2-recipe book) */ TwoRecipeBook();
        var vm1 = new CookBookDetailViewModel(book, new FakeNotYetWired());
        var vm2 = new CookBookDetailViewModel(book, new FakeNotYetWired());
        // same recipe ids ⇒ identical segment colours across instances
        Assert.Equal(vm1.Recipes.Select(r => r.SegmentColor), vm2.Recipes.Select(r => r.SegmentColor));
        // distinct recipes ⇒ distinct hues (2-recipe fixture)
        Assert.NotEqual(vm1.Recipes[0].SegmentColor, vm1.Recipes[1].SegmentColor);
    }
```
If this test file has no `TwoRecipeBook()` helper, build a minimal 2-recipe `LoadedCookBook` inline (mirror `ExplorerViewModelTests.TwoRecipeBook`).

- [ ] **Step 2: Run — fails** (`SegmentColor` missing): `dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~Recipe_segment_colour`

- [ ] **Step 3: Implement.** Add `SegmentColor` to the record and compute it. In `CookBookDetailViewModel.cs`:
```csharp
using Avalonia.Media;
using Nfty.Core.Generation;   // SeedHash
using Nfty.Core.Imaging;      // ColorConvert
// ...
public record RecipeShareRow(string Name, double SharePercent, string DnaSpaceText, Color SegmentColor);

// helper (mirrors the mockup's hsv(r.hue, .5, .72)):
private static Color SegmentColorFor(string recipeId)
{
    double hue = SeedHash.ToUlong(recipeId) % 360UL;   // stable hue in [0,360)
    var rgb = ColorConvert.HsvToRgb(hue, 0.5, 0.72);   // RgbColor { R,G,B } bytes
    return Color.FromRgb(rgb.R, rgb.G, rgb.B);
}
```
Where the ctor builds each `RecipeShareRow`, pass `SegmentColorFor(<recipe id>)` as the 4th arg. (The ctor already iterates recipes to compute `SharePercent`/`DnaSpaceText`; add the id-derived colour there. Confirm `RgbColor`'s byte member names via `grep -n "record RgbColor\|struct RgbColor" src/Nfty.Core/Imaging/*.cs`; if they are `byte R,G,B` use as above, else adapt.)

- [ ] **Step 4: Run — passes.** Then `dotnet test tests/Nfty.App.Tests --nologo` (whole suite green; the extra positional-with-value arg is set at the one construction site — update it).

- [ ] **Step 5: Commit** `feat(gui): deterministic per-recipe mint-distribution segment colour`

---

### Task 2: Structured `RuleRow`

**Files:** Modify `src/Nfty.App/ViewModels/RecipeDetailViewModel.cs`; Test `tests/Nfty.App.Tests/RecipeDetailViewModelTests.cs`.

**Interfaces:** Produces `RuleRow` carrying the operator + traits:
```csharp
public record RuleTargetRow(string Ingredient, string Variant);
public record RuleRow(bool IsExclude, RuleTargetRow When, IReadOnlyList<RuleTargetRow> Targets);
```
(Replaces `record RuleRow(string Text)`.)

- [ ] **Step 1: Failing test.** Replace the existing rule-formatting test in `RecipeDetailViewModelTests` (currently asserting `RuleRow.Text` contains "✕"/"→") with one asserting structure:
```csharp
    [AvaloniaFact]
    public void Rules_expose_operator_and_traits()
    {
        // build a recipe with one Exclude and one Require rule (reuse the file's existing rule fixture)
        var vm = /* RecipeDetailViewModel over a recipe with the two rules */;
        var exclude = vm.Rules.Single(r => r.IsExclude);
        var require = vm.Rules.Single(r => !r.IsExclude);
        Assert.Equal("bg", exclude.When.Ingredient);
        Assert.Equal("day", exclude.When.Variant);
        Assert.Contains(exclude.Targets, t => t.Ingredient == "aura" && t.Variant == "none");
        Assert.Equal("bg", require.When.Ingredient);
    }
```
(Match the ingredient/variant ids to whatever the file's existing rules fixture uses; that fixture already builds Exclude+Require rules — reuse it.)

- [ ] **Step 2: Run — fails** (RuleRow shape changed / members missing).

- [ ] **Step 3: Implement.** In `RecipeDetailViewModel.cs`, replace the record + the `RuleText` mapper:
```csharp
public record RuleTargetRow(string Ingredient, string Variant);
public record RuleRow(bool IsExclude, RuleTargetRow When, IReadOnlyList<RuleTargetRow> Targets);
// ...
Rules = recipe.Manifest.Rules.Select(MapRule).ToList();
// ...
private static RuleRow MapRule(IncompatibilityRule rule) => new(
    rule.Type == RuleType.Exclude,
    new RuleTargetRow(rule.When.IngredientId, rule.When.VariantId),
    rule.Targets.Select(t => new RuleTargetRow(t.IngredientId, t.VariantId)).ToList());
```
Remove the old `RuleText` method. (The View in Task 6 renders the operator badge + chips from these fields; the old `RuleRow.Text` binding in the current `RecipeDetailView.axaml` will be replaced in Task 6 — this task leaves the view compiling by NOT referencing `.Text` anywhere; if the current view binds `RuleRow.Text`, update that binding minimally in this task to `IsExclude`/`When` so the build stays green, or fold the view rules-list change here. Simplest: in this task, also update the `RecipeDetailView.axaml` rules `ItemsControl` DataTemplate to a minimal `TextBlock` showing `When.Ingredient` so it compiles; Task 6 restyles it fully.)

- [ ] **Step 4: Run — passes.** Whole suite green.

- [ ] **Step 5: Commit** `feat(gui): structured RuleRow (operator + when/targets)`

---

### Task 3: `ColorwayAxis` rows on `IngredientDetailViewModel`

**Files:** Modify `src/Nfty.App/ViewModels/IngredientDetailViewModel.cs`; Test `tests/Nfty.App.Tests/IngredientDetailViewModelTests.cs`.

**Interfaces:** Produces `public record ColorwayAxis(string Label, string Value, bool Derived);` and `IReadOnlyList<ColorwayAxis> ColorwayAxes { get; }`.

- [ ] **Step 1: Failing test:**
```csharp
    [AvaloniaFact]
    public void Colorway_axes_reflect_the_kind()
    {
        var (book, recipe, ing) = Fixture();   // existing helper: Custom ingredient
        using var vm = new IngredientDetailViewModel(ing, recipe, book, new ImageBridge(),
            new FakeNotYetWired(), () => { }, () => false);
        // custom → a single "composited as-is" axis (no H/S)
        Assert.Contains(vm.ColorwayAxes, a => a.Value.Contains("composited as-is"));
    }
```
(If time permits add a dynamic-ingredient case asserting Hue/Sat axes; the Fixture is Custom.)

- [ ] **Step 2: Run — fails.**

- [ ] **Step 3: Implement.** Derive axes from the ingredient's `Colorization` (null = custom). In `IngredientDetailViewModel.cs`:
```csharp
public record ColorwayAxis(string Label, string Value, bool Derived);
// ...
public IReadOnlyList<ColorwayAxis> ColorwayAxes { get; }
// in ctor, after KindText/ColorwaysText:
ColorwayAxes = BuildAxes(ing.Manifest);
// ...
private static IReadOnlyList<ColorwayAxis> BuildAxes(IngredientManifest m)
{
    if (m.Colorization is null)
        return new[] { new ColorwayAxis("Colour", "no colorize · composited as-is", true) };
    var c = m.Colorization;
    var range = c.Entries.FirstOrDefault(e => e.Range is not null)?.Range;
    var list = new List<ColorwayAxis>();
    if (range is not null)
    {
        list.Add(new ColorwayAxis("Hue", $"{range.HueMin:0}–{range.HueMax:0}°", false));
        list.Add(new ColorwayAxis("Sat", $"{range.SatMin:0}–{range.SatMax:0}%", false));
    }
    else
    {
        var fixedSpec = c.Entries.FirstOrDefault(e => e.Fixed is not null)?.Fixed;
        if (fixedSpec is not null) list.Add(new ColorwayAxis("Colour", fixedSpec, false));
    }
    list.Add(new ColorwayAxis("Value", "← value-map", true));
    return list;
}
```
(Confirm `ColorRange` member names `HueMin/HueMax/SatMin/SatMax` and `ColorEntry.Range`/`.Fixed` — they are per `Model/Colorization.cs`.)

- [ ] **Step 4: Run — passes.** Whole suite green.

- [ ] **Step 5: Commit** `feat(gui): ingredient colorway axis rows`

---

### Task 4: Shared detail styles

**Files:** Modify `src/Nfty.App/Themes/Styles.axaml`; Test `tests/Nfty.App.Tests/ThemeResourceTests.cs` (style-load).

Add styles mirroring the mockup (line refs in `explorer.html`): `.metric`+`.mv`+`.ml`+`.metric.accent` (210-216); `.distbar`+`.distlegend`+`.li`+`.sw`+`.pc` (219-225); `table.data` header/row (259-267) — realise as reusable classes for a `Grid`/`ItemsControl` "table" (header row mono uppercase 10.5px `FgMuted`, body row padding 12/10, bottom `LineBrush`, hover `BgAlt2`, selected `AccentWash`); `.rules-panel` (365) + `.rop`(.exclude accent / .require info) (372-376) + `.rchip`+`.rcl`+`.rcv` (378-381); `.cw-panel` (385) + `.cwaxis`+`.ax`+`.av`(+`.av.der` italic muted) (395-400); `.rhero`/`.vhero` tile (283/313, gradient-ish via `PanelBrush` bg + `LineBrush`). Colours via tokens only.

- [ ] **Step 1:** Write a `[AvaloniaFact]` style-load guard in `ThemeResourceTests` constructing a `Border{Classes={"metric","accent"}}` and a `Border{Classes={"rules-panel"}}` via `StyledHost.Show` and asserting they render + resolve a representative brush (e.g. metric.accent Background = `AccentWashBrush`). Run — fails.
- [ ] **Step 2:** Add the styles to `Styles.axaml` (values per the mockup lines above; tokens only).
- [ ] **Step 3:** Run — passes; `dotnet build src/Nfty.Desktop --nologo` 0 warnings; suite green.
- [ ] **Step 4: Commit** `feat(gui): shared detail styles (metric/distbar/data/rules/colorways)`

---

### Task 5: CookBook detail body

**Files:** Modify `src/Nfty.App/Views/CookBookDetailView.axaml`.

Restyle to: an `idchip` row (Name, `Symbol`, `CanvasText`); a metric-tile row (recipes/layers/variants + accent unique-DNA using `.metric`/`.metric.accent`); a mint-distribution `.distbar` (an `ItemsControl` with a horizontal panel; each segment a `Border` with `Width` proportional to `SharePercent` and `Background` = a `SolidColorBrush` from `SegmentColor`) + a `.distlegend` (swatch = `SegmentColor`, name, `SharePercent`%); the Cook `accent` button. Bind `SegmentColor` via a `SolidColorBrush` (a `Color`→brush is direct in Avalonia: `Background="{Binding SegmentColor}"` works if the target is a `Color`? No — use `<Border Background="{Binding SegmentColor, Converter=...}"/>`; simplest: expose the brush. If binding a `Color` to `Background` needs a converter, add a tiny `FuncValueConverter<Color, IBrush>` in `Converters` OR change `SegmentColor` to `IBrush`/`SolidColorBrush` in Task 1). Decide during Task 5; prefer exposing `SegmentColor` as `ISolidColorBrush` from the VM to avoid a converter (adjust Task 1's type if so, keeping the test comparing `.Color`).

- [ ] Steps: build → render via harness (Task 8 covers capture; here just build 0-warning + suite green) → commit `feat(gui): CookBook detail body (id-chips, metric tiles, mint distribution)`.

Distribution segment width: use a `Grid`/relative width. Simplest robust approach in Avalonia: a `Grid` with `ColumnDefinitions` built from shares, or each segment `Width` set from a star-proportional container. If proportional star widths from a binding are awkward, render the bar with each segment's `Width` = `SharePercent` inside a fixed-width container scaled to 100 — confirm the cleanest approach and note it; the visual capture (T8) verifies proportions.

---

### Task 6: Recipe detail body (2-column: hero + layer table + rules rail)

**Files:** Modify `src/Nfty.App/Views/RecipeDetailView.axaml`.

Two-column `Grid` (`*,300`). **Main:** `.rhero` tile (the `Hero` `Image` ~92 + name mono + `Seed {RollSeed}` + reroll `Button.dice` bound to `RerollCommand`); then the layer table using `table.data` styles (header index/layer/kind/variants; rows = `Layers`, each a row invoking `OpenIngredientCommand` with `LayerRow.Id`, kind shown as a kind-coloured chip/text). **Rules rail:** a `.rules-panel` `ItemsControl` over `Rules` — each row a `.rop` badge (`.exclude` when `IsExclude` else `.require`) + `.rchip`s for `When` and each `Targets` item; empty-state muted "No incompatibility rules" (reuse the `CollectionConverters.IsEmpty`). Use `x:DataType="vm:RuleRow"`/`vm:RuleTargetRow`.

- [ ] Steps: build 0-warning + suite green → commit `feat(gui): Recipe detail body (hero, layer table, rules rail)`.

---

### Task 7: Ingredient detail body (2-column: hero + variant table + colorways rail)

**Files:** Modify `src/Nfty.App/Views/IngredientDetailView.axaml`.

Two-column `Grid` (`*,280`). **Main:** `.vhero` tile (`Hero` ~84 + name mono 17 + kind sub from `KindText`) with the ✏ `EditIngredientCommand` button top-right; then the variant table using `table.data`/`.vtable` (header thumbnail/name/weight/in-recipe/overall with the two sortable headers bound to `SortByCommand`; rows = `Variants`). **Colorways rail:** a `.cw-panel` with the `Colorways` swatch `ItemsControl` (existing bitmaps) then a `.cwaxis` `ItemsControl` over `ColorwayAxes` (label `.ax`, value `.av` / `.av.der` when `Derived`). Keep Delete/Jump-to-rules actions. `x:DataType` on each template.

- [ ] Steps: build 0-warning + suite green → commit `feat(gui): Ingredient detail body (hero, variant table, colorways rail)`.

---

### Task 8: Render all three detail bodies + visual verification

**Files:** Modify `tests/Nfty.App.Tests/VisualCapture.cs`.

- [ ] **Step 1:** Add a `[AvaloniaFact]` (guarded by `NFTY_CAPTURE`) that renders each real detail view bound to fixture VMs and saves `cookbook-detail-{v}.png`, `recipe-detail-{v}.png`, `ingredient-detail-{v}.png` in both themes. Build the fixture VMs from `ExplorerViewModelTests.TwoRecipeBook()` (the `cat` recipe carries rules? if not, build a small recipe-with-rules fixture inline for the recipe capture so the rules rail is exercised). Wrap each view in a `Window { RequestedThemeVariant=v, Content=view, Width=900, Height=560 }`, `Show()`, `Dispatcher.UIThread.RunJobs()`, `CaptureRenderedFrame()!.Save(...)`.
- [ ] **Step 2:** Run with `NFTY_CAPTURE=1 NFTY_CAPTURE_DIR=$CAP` (CAP = `C:/Users/Corde/AppData/Local/Temp/claude/M--Repositories-nfty/0f27fa31-ff64-4405-96f9-eecf4e89d6fd/scratchpad`). **Read every PNG and view it.** Compare to `explorer.html`: CookBook metric tiles + distribution bar colours/proportions + legend; Recipe hero + layer table + rules rail (exclude accent badge, require info badge, trait chips); Ingredient hero + variant table + colorways rail (swatches + Hue/Sat/Value axes). Iterate on styles/views until faithful in BOTH themes; report what you saw.
- [ ] **Step 3:** `dotnet build nfty.sln --nologo` 0 warnings; `dotnet test nfty.sln --nologo` green; `grep -rniE "#[0-9a-f]{6}" src/Nfty.App/Views src/Nfty.App/Themes/Styles.axaml` → no raw hex outside Tokens.
- [ ] **Step 4: Commit** `test(gui): render Explorer detail bodies for visual verification`.

---

## Self-Review
- **Spec coverage:** §2.1 CookBook (id-chips/metrics/distbar) → T1 (colour) + T4 (styles) + T5 (view). §2.2 Recipe (hero/layer-table/rules-rail) → T2 (RuleRow) + T4 + T6. §2.3 Ingredient (hero/variant-table/colorways-rail) → T3 (axes) + T4 + T7. §2.4 shared styles → T4. §4 tests → T1/T2/T3 (VM), T4 (style-load), T8 (visual). No `Nfty.Core`/behaviour change in any task.
- **Placeholder scan:** VM tasks (T1-T3) carry full code. View tasks (T5-T7) give structure + the mockup line refs + style hooks, with the visual-capture loop (T8) as the fidelity gate — the deliberate approach for "match the mockup" XAML, not TBDs. One decision flagged inline (SegmentColor as `Color` vs `ISolidColorBrush`) with a concrete resolution (prefer brush to avoid a converter; adjust T1 type, keep the test on `.Color`).
- **Type consistency:** `RecipeShareRow(...+SegmentColor)`, `RuleRow(IsExclude,When,Targets)`+`RuleTargetRow`, `ColorwayAxis(Label,Value,Derived)` defined in T1/T2/T3 and consumed in T5/T6/T7. `SeedHash.ToUlong`, `ColorConvert.HsvToRgb`, `IncompatibilityRule`/`RuleType`/`RuleTarget`, `Colorization`/`ColorEntry`/`ColorRange` names match Core.
