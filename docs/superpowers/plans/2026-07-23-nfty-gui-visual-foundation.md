# nfty GUI — Visual Foundation + Shell Chrome Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the shared Avalonia style library (typography, buttons, surfaces, chips, inputs) and restyle the window chrome to mirror the locked mockups, completing the partial token port — the visual vocabulary every screen slice will compose.

**Architecture:** All colour/font/shadow/radius values live as themed resources in `Themes/Tokens.axaml` (light + dark `ThemeDictionaries`); reusable look lives in `Themes/Styles.axaml` as class `Style`s (property/state setters) and, for template-level input restyling, `ControlTheme`s `BasedOn` the Fluent theme. The window chrome in `MainWindow.axaml` is restyled (no VM/behaviour change). A test-only `TestApp` loads the same FluentTheme + Tokens + Styles so styling is exercised headlessly.

**Tech Stack:** .NET 10, Avalonia 11.2.3 (FluentTheme, `Style`, `ControlTheme`, `ThemeDictionaries`, `BoxShadows`), xUnit + `Avalonia.Headless.XUnit` (Skia backend, already configured).

## Global Constraints

- **Mockups are the source of truth.** Every colour/font/radius/shadow value is verbatim from `docs/design/mockups/explorer.html`'s token block or CSS rules. No **invented** colours. Do not edit a hex without first updating the mockup.
- **No `Nfty.Core`, ViewModel, service, command, or keybinding change.** This slice edits `Themes/Tokens.axaml`, `Themes/Styles.axaml`, `MainWindow.axaml`, and test-only files. Presentation only.
- **Both themes always.** Every token added goes in BOTH the `Light` and `Dark` `ThemeDictionaries`. Every style pulls colours via `{DynamicResource <key>}` so light/dark and the theme toggle keep working.
- **Objective API details are doc-pulled, not assumed.** Where the plan flags "confirm against Avalonia 11.2 docs" (BoxShadows resource syntax; ControlTheme `BasedOn` Fluent; Fluent Button hover template parts), pull the docs (Context7 `/avaloniaui/avalonia-docs`) before writing that code. If a documented mechanism contradicts this plan, follow the docs and note it in the report.
- **No golden-image/pixel tests.** Style correctness is guarded by (a) resource-resolution tests, (b) the app/styles loading without throwing, (c) the existing ViewLocator smoke staying green, and (d) manual side-by-side vs the mockup. Applied-value assertions are used only where stable.
- Build 0 warnings; conventional commits; the full existing suite stays green (no behaviour changed).

## File Structure

- `src/Nfty.App/Themes/Tokens.axaml` — all themed resources (colours, fonts, radii, shadow). Task 1.
- `src/Nfty.App/Themes/Styles.axaml` — class `Style`s: typography (T2), buttons (T3), surfaces & chips (T4). Grows across tasks.
- `src/Nfty.App/Themes/Controls.axaml` — NEW: `ControlTheme`s for restyled inputs (T5), merged into the app + test app. Keeps template-level themes out of the flat `Styles.axaml`.
- `src/Nfty.Desktop/App.axaml` — merge the new `Controls.axaml` (T5).
- `src/Nfty.Desktop/MainWindow.axaml` — chrome restyle (T6).
- `tests/Nfty.App.Tests/TestApp.axaml` (+ `.axaml.cs`) — NEW test-only Application loading FluentTheme + Tokens + Styles (+ Controls from T5); `TestAppBuilder` points at it. Task 1.
- `tests/Nfty.App.Tests/ThemeResourceTests.cs` — NEW: resource-resolution + applied-value tests. Grows across tasks.
- `tests/Nfty.App.Tests/StyledHost.cs` — NEW tiny helper: show a control under the themed headless app and return it laid-out, so applied style values can be read. Task 2.

---

### Task 1: Test harness loads app resources + complete the token port

**Files:**
- Create: `tests/Nfty.App.Tests/TestApp.axaml`, `tests/Nfty.App.Tests/TestApp.axaml.cs`
- Modify: `tests/Nfty.App.Tests/TestAppBuilder.cs`
- Modify: `src/Nfty.App/Themes/Tokens.axaml`
- Test: `tests/Nfty.App.Tests/ThemeResourceTests.cs` (create)

**Interfaces:**
- Produces: token resource keys usable by later tasks — `SansFontFamily` (FontFamily), `MonoFontFamily` (updated), `AccentHoverBrush`, `SuccessBrush`, `GuideBrush`, `GuideHiBrush` (SolidColorBrush), `WinShadow` (BoxShadows), `RadiusXs`, `RadiusLg` (x:Double). Test app now applies FluentTheme + Tokens + Styles headlessly.

- [ ] **Step 1: Create the test-only App that mirrors the real app's resources**

`tests/Nfty.App.Tests/TestApp.axaml`:
```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="Nfty.App.Tests.TestApp"
             RequestedThemeVariant="Default">
  <Application.Resources>
    <ResourceDictionary>
      <ResourceDictionary.MergedDictionaries>
        <ResourceInclude Source="avares://Nfty.App/Themes/Tokens.axaml" />
      </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
  </Application.Resources>
  <Application.Styles>
    <FluentTheme />
    <StyleInclude Source="avares://Nfty.App/Themes/Styles.axaml" />
  </Application.Styles>
</Application>
```
`tests/Nfty.App.Tests/TestApp.axaml.cs`:
```csharp
using Avalonia;
using Avalonia.Markup.Xaml;

namespace Nfty.App.Tests;

public class TestApp : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
}
```

- [ ] **Step 2: Point the test builder at `TestApp`**

`tests/Nfty.App.Tests/TestAppBuilder.cs` — change `Configure<Avalonia.Application>()` to `Configure<TestApp>()`:
```csharp
public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .UseSkia();
```

- [ ] **Step 3: Run the existing suite to confirm the themed test app doesn't break anything**

Run: `dotnet test tests/Nfty.App.Tests --nologo`
Expected: all currently-passing tests still PASS (loading FluentTheme + Tokens + Styles must not change any logic outcome). If a test breaks, STOP and report — a behaviour change here is a signal, not something to paper over.

- [ ] **Step 4: Write the failing token-resolution test**

`tests/Nfty.App.Tests/ThemeResourceTests.cs`:
```csharp
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Xunit;

namespace Nfty.App.Tests;

public class ThemeResourceTests
{
    private static object? Resolve(string key, ThemeVariant variant) =>
        Application.Current!.TryGetResource(key, variant, out var v) ? v : null;

    [AvaloniaTheory]
    [InlineData("AccentHoverBrush")]
    [InlineData("SuccessBrush")]
    [InlineData("GuideBrush")]
    [InlineData("GuideHiBrush")]
    public void New_colour_tokens_resolve_in_both_themes(string key)
    {
        Assert.IsAssignableFrom<IBrush>(Resolve(key, ThemeVariant.Light));
        Assert.IsAssignableFrom<IBrush>(Resolve(key, ThemeVariant.Dark));
    }

    [AvaloniaFact]
    public void Font_and_radius_tokens_resolve()
    {
        Assert.IsType<FontFamily>(Resolve("SansFontFamily", ThemeVariant.Light));
        Assert.IsType<FontFamily>(Resolve("MonoFontFamily", ThemeVariant.Light));
        Assert.Equal(4d, Resolve("RadiusXs", ThemeVariant.Light));
        Assert.Equal(8d, Resolve("RadiusLg", ThemeVariant.Light));
    }
}
```
Note: `SansFontFamily`/`MonoFontFamily`/`RadiusXs`/`RadiusLg` are top-level resources (not theme-scoped); resolving with any variant returns them. Colour tokens are theme-scoped, hence the per-variant checks.

- [ ] **Step 5: Run to verify it fails**

Run: `dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~ThemeResourceTests`
Expected: FAIL — the new keys don't exist yet.

- [ ] **Step 6: Complete the token port in `Tokens.axaml`**

Add to BOTH `ThemeDictionaries`. In the `Light` dictionary add:
```xml
      <SolidColorBrush x:Key="AccentHoverBrush" Color="#b92f44" />
      <SolidColorBrush x:Key="SuccessBrush" Color="#3d6b52" />
      <SolidColorBrush x:Key="GuideBrush" Color="#1214182b" />
      <SolidColorBrush x:Key="GuideHiBrush" Color="#12141859" />
      <BoxShadows x:Key="WinShadow">0 1 2 0 #12141810, 0 18 48 -24 #12141833</BoxShadows>
```
In the `Dark` dictionary add:
```xml
      <SolidColorBrush x:Key="AccentHoverBrush" Color="#ba3447" />
      <SolidColorBrush x:Key="SuccessBrush" Color="#a6c08a" />
      <SolidColorBrush x:Key="GuideBrush" Color="#f2ede61f" />
      <SolidColorBrush x:Key="GuideHiBrush" Color="#f2ede63d" />
      <BoxShadows x:Key="WinShadow">0 1 2 0 #00000060, 0 22 60 -28 #000000</BoxShadows>
```
Add `SansFontFamily`, update `MonoFontFamily`, and add the two radii at the bottom (top-level, outside `ThemeDictionaries`, beside the existing `MonoFontFamily`/`RadiusMd`):
```xml
  <FontFamily x:Key="SansFontFamily">-apple-system, Segoe UI, Helvetica Neue, Arial, sans-serif</FontFamily>
  <FontFamily x:Key="MonoFontFamily">SF Mono, JetBrains Mono, Cascadia Code, Menlo, Consolas, monospace</FontFamily>
  <x:Double x:Key="RadiusXs">4</x:Double>
  <x:Double x:Key="RadiusLg">8</x:Double>
```
(Remove the old `MonoFontFamily` line so it isn't declared twice.)

**Doc-pull (objective):** confirm the `<BoxShadows x:Key="...">…</BoxShadows>` element-resource syntax and the string format (`offsetX offsetY blur spread #color, …`) against Avalonia 11.2 docs (Context7 `/avaloniaui/avalonia-docs`, query "BoxShadows resource and string format"). CSS `0 1px 2px #c` = offsetX 0, offsetY 1, blur 2, spread 0; `0 18px 48px -24px #c` = spread −24. If the element form isn't supported as a themed resource, fall back to defining the shadow inline in the T4 `Border.panel`/`Border.frame` styles (still verbatim, still theme-flipping via two style setters) and drop the `WinShadow` resource + the note here — record the deviation in the report.

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~ThemeResourceTests`
Expected: PASS. Then `dotnet test tests/Nfty.App.Tests --nologo` → whole suite green.

- [ ] **Step 8: Commit**

```bash
git add src/Nfty.App/Themes/Tokens.axaml tests/Nfty.App.Tests/TestApp.axaml tests/Nfty.App.Tests/TestApp.axaml.cs tests/Nfty.App.Tests/TestAppBuilder.cs tests/Nfty.App.Tests/ThemeResourceTests.cs
git commit -m "feat(gui): complete token port + themed test host"
```

---

### Task 2: Typography — sans base + mono/heading classes

**Files:**
- Modify: `src/Nfty.App/Themes/Styles.axaml`
- Create: `tests/Nfty.App.Tests/StyledHost.cs`
- Test: `tests/Nfty.App.Tests/ThemeResourceTests.cs` (extend)

**Interfaces:**
- Consumes: `SansFontFamily`/`MonoFontFamily` (Task 1).
- Produces: base sans font on `TextBlock`; classes `.mono`, `.wordmark`, `.section-h`, `.crumbs`, `.idchip`, `.kind-txt`; helper `StyledHost.Show(Control)`.

Mockup values to match (read `docs/design/mockups/explorer.html` for the exact rules): `.wordmark` = mono, weight 700, 15px, letter-spacing −.01em (line ~90); `.crumbs` = mono, 12.5px, `FgMutedBrush` (line ~?); `.idchip` = mono, 11px (line 345); `.kind-txt` = mono, 12px (line 128); section headers = mono. Base body = sans.

- [ ] **Step 1: Write the `StyledHost` helper**

```csharp
// tests/Nfty.App.Tests/StyledHost.cs
using Avalonia.Controls;

namespace Nfty.App.Tests;

/// <summary>Shows a control under the themed headless app and lays it out, so applied style
/// values (fonts, brushes) can be read back in tests.</summary>
public static class StyledHost
{
    public static T Show<T>(T control) where T : Control
    {
        var window = new Window { Content = control, Width = 200, Height = 100 };
        window.Show();
        window.LayoutManager.ExecuteInitialLayoutPass();
        return control;
    }
}
```

- [ ] **Step 2: Write the failing typography test**

Add to `ThemeResourceTests.cs`:
```csharp
    [AvaloniaFact]
    public void Base_text_is_sans_and_mono_class_is_mono()
    {
        var plain = StyledHost.Show(new TextBlock { Text = "body" });
        var mono = StyledHost.Show(new TextBlock { Text = "id", Classes = { "mono" } });

        var sans = (FontFamily)Application.Current!.FindResource("SansFontFamily")!;
        var monoFam = (FontFamily)Application.Current!.FindResource("MonoFontFamily")!;
        Assert.Equal(sans, plain.FontFamily);   // base default is sans, not mono
        Assert.Equal(monoFam, mono.FontFamily);
    }
```
Add `using Avalonia.Controls;`.

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~Base_text_is_sans`
Expected: FAIL — base is currently mono (set on `Window` in `Styles.axaml`) and `.mono` class doesn't exist.

- [ ] **Step 4: Update `Styles.axaml` typography**

Change the `Window` style to use sans, and add the typography classes. Replace the existing `Window` style block and add classes:
```xml
  <Style Selector="Window">
    <Setter Property="Background" Value="{DynamicResource BgBrush}" />
    <Setter Property="FontFamily" Value="{DynamicResource SansFontFamily}" />
  </Style>
  <Style Selector="TextBlock">
    <Setter Property="FontFamily" Value="{DynamicResource SansFontFamily}" />
    <Setter Property="Foreground" Value="{DynamicResource FgBrush}" />
  </Style>

  <Style Selector="TextBlock.mono">
    <Setter Property="FontFamily" Value="{DynamicResource MonoFontFamily}" />
  </Style>
  <Style Selector="TextBlock.wordmark">
    <Setter Property="FontFamily" Value="{DynamicResource MonoFontFamily}" />
    <Setter Property="FontWeight" Value="Bold" />
    <Setter Property="FontSize" Value="15" />
  </Style>
  <Style Selector="TextBlock.section-h">
    <Setter Property="FontFamily" Value="{DynamicResource MonoFontFamily}" />
    <Setter Property="FontWeight" Value="SemiBold" />
    <Setter Property="FontSize" Value="12" />
    <Setter Property="Foreground" Value="{DynamicResource FgMutedBrush}" />
  </Style>
  <Style Selector="TextBlock.crumbs">
    <Setter Property="FontFamily" Value="{DynamicResource MonoFontFamily}" />
    <Setter Property="FontSize" Value="12.5" />
    <Setter Property="Foreground" Value="{DynamicResource FgMutedBrush}" />
  </Style>
  <Style Selector="TextBlock.kind-txt">
    <Setter Property="FontFamily" Value="{DynamicResource MonoFontFamily}" />
    <Setter Property="FontSize" Value="12" />
  </Style>
```
Keep the existing `TextBlock.muted` style. (The `.idchip` mono lives on the chip `Border` in Task 4; a `.mono` class on inner `TextBlock`s covers idchip text.)

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~ThemeResourceTests`
Expected: PASS. Then whole App suite green (`dotnet test tests/Nfty.App.Tests --nologo`).

- [ ] **Step 6: Commit**

```bash
git add src/Nfty.App/Themes/Styles.axaml tests/Nfty.App.Tests/StyledHost.cs tests/Nfty.App.Tests/ThemeResourceTests.cs
git commit -m "feat(gui): sans body font + mono/heading typography classes"
```

---

### Task 3: Buttons — refine tbtn/accent, add ghost/icon/dice with states

**Files:**
- Modify: `src/Nfty.App/Themes/Styles.axaml`
- Test: `tests/Nfty.App.Tests/ThemeResourceTests.cs` (extend)

**Interfaces:**
- Consumes: `AccentBrush`, `AccentHoverBrush`, `OnAccentBrush`, `PanelBrush`, `LineStrongBrush`, `AccentLineBrush`, `FgBrush`, radii (Tasks 1).
- Produces: button classes `tbtn`, `accent`, `ghost`, `icon`, `dice` with `:pointerover`/`:pressed` states.

Mockup values (read `explorer.html`): `.tbtn` font-size 12.5px, inline-flex, gap 7px, panel bg, line-strong border, radius r-sm (line 155); `.tbtn:hover` → border `accent-line` (158); `.tbtn.accent` → accent bg/border, on-accent text (160); `.tbtn.accent:hover` → `accent-hover` bg (161); `.dice` 27×27, radius, hover → accent-line border + accent-text (289–294).

**Doc-pull (objective):** Avalonia's FluentTheme `Button` applies pointerover/pressed backgrounds via template-part styles (`Button:pointerover /template/ ContentPresenter#PART_ContentPresenter`), which override a plain `Button.Background` setter. Confirm the current 11.2 Button template part name and pattern (Context7 `/avaloniaui/avalonia-docs`, query "style Button pointerover background template ContentPresenter Fluent"). Apply hover/pressed via the template-part selector (or a `ControlTheme` BasedOn the Fluent Button) so the mockup hover actually shows — verify by eye in the manual smoke, and encode at least the base applied colours in the test below.

- [ ] **Step 1: Write the failing test (base applied colours + classes exist)**

Add to `ThemeResourceTests.cs`:
```csharp
    [AvaloniaFact]
    public void Accent_button_uses_accent_background_and_tbtn_uses_panel()
    {
        var accent = StyledHost.Show(new Button { Classes = { "accent" }, Content = "Cook" });
        var tbtn = StyledHost.Show(new Button { Classes = { "tbtn" }, Content = "Open" });

        Assert.Equal(
            ((ISolidColorBrush)Application.Current!.FindResource("AccentBrush")!).Color,
            ((ISolidColorBrush)accent.Background!).Color);
        Assert.Equal(
            ((ISolidColorBrush)Application.Current!.FindResource("PanelBrush")!).Color,
            ((ISolidColorBrush)tbtn.Background!).Color);
    }
```
Add `using Avalonia.Controls;` / `using Avalonia.Media;` if not present.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~Accent_button`
Expected: FAIL (accent class currently sets background but the assertion also pins tbtn=panel; if it already passes for accent, it still fails for the refined expectations — confirm RED before proceeding).

- [ ] **Step 3: Refine/extend the button styles**

Update `Button.tbtn` and `Button.accent`, add `ghost`/`icon`/`dice`, and add state selectors (use the doc-confirmed template-part selector for pointerover background). Base setters:
```xml
  <Style Selector="Button.tbtn">
    <Setter Property="Background" Value="{DynamicResource PanelBrush}" />
    <Setter Property="Foreground" Value="{DynamicResource FgBrush}" />
    <Setter Property="BorderBrush" Value="{DynamicResource LineStrongBrush}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="Padding" Value="12,8" />
    <Setter Property="FontSize" Value="12.5" />
    <Setter Property="CornerRadius" Value="{DynamicResource RadiusSm}" />
    <Setter Property="HorizontalContentAlignment" Value="Left" />
  </Style>
  <Style Selector="Button.tbtn:pointerover">
    <Setter Property="BorderBrush" Value="{DynamicResource AccentLineBrush}" />
  </Style>

  <Style Selector="Button.accent">
    <Setter Property="Background" Value="{DynamicResource AccentBrush}" />
    <Setter Property="Foreground" Value="{DynamicResource OnAccentBrush}" />
    <Setter Property="BorderBrush" Value="{DynamicResource AccentBrush}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="Padding" Value="12,8" />
    <Setter Property="CornerRadius" Value="{DynamicResource RadiusSm}" />
  </Style>

  <Style Selector="Button.ghost">
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="Foreground" Value="{DynamicResource FgBrush}" />
    <Setter Property="BorderThickness" Value="0" />
    <Setter Property="Padding" Value="8,6" />
    <Setter Property="CornerRadius" Value="{DynamicResource RadiusSm}" />
  </Style>

  <Style Selector="Button.icon">
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="Foreground" Value="{DynamicResource FgMutedBrush}" />
    <Setter Property="BorderThickness" Value="0" />
    <Setter Property="Padding" Value="8,6" />
    <Setter Property="CornerRadius" Value="{DynamicResource RadiusSm}" />
    <Setter Property="HorizontalContentAlignment" Value="Center" />
  </Style>

  <Style Selector="Button.dice">
    <Setter Property="Background" Value="{DynamicResource PanelBrush}" />
    <Setter Property="BorderBrush" Value="{DynamicResource LineStrongBrush}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="Width" Value="27" />
    <Setter Property="Height" Value="27" />
    <Setter Property="Padding" Value="0" />
    <Setter Property="CornerRadius" Value="{DynamicResource RadiusSm}" />
    <Setter Property="HorizontalContentAlignment" Value="Center" />
    <Setter Property="VerticalContentAlignment" Value="Center" />
  </Style>
  <Style Selector="Button.dice:pointerover">
    <Setter Property="BorderBrush" Value="{DynamicResource AccentLineBrush}" />
    <Setter Property="Foreground" Value="{DynamicResource AccentTextBrush}" />
  </Style>
```
Then add the doc-confirmed template-part hover/pressed background overrides for `Button.tbtn`, `Button.accent` (→ `AccentHoverBrush`), `Button.ghost`/`Button.icon` (→ a subtle `BgAlt2Brush` wash), so the Fluent default hover doesn't win. Encode exactly what the doc-pull confirms.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~ThemeResourceTests`
Expected: PASS. Whole App suite green.

- [ ] **Step 5: Commit**

```bash
git add src/Nfty.App/Themes/Styles.axaml tests/Nfty.App.Tests/ThemeResourceTests.cs
git commit -m "feat(gui): button styles — tbtn/accent/ghost/icon/dice with states"
```

---

### Task 4: Surfaces & chips — panel/tile/card/frame + idchip + kind chips

**Files:**
- Modify: `src/Nfty.App/Themes/Styles.axaml`
- Test: `tests/Nfty.App.Tests/ThemeResourceTests.cs` (extend)

**Interfaces:**
- Consumes: `PanelBrush`, `TileBrush`, `BgBrush`, `LineBrush`, `LineStrongBrush`, `WinShadow`, radii, `KindDynamicBrush`/`KindStaticBrush`/`KindCustomBrush` (Task 1).
- Produces: `Border` classes `panel`, `tile`, `card`, `frame`, `idchip`; kind chip classes `kind-dynamic`/`kind-static`/`kind-custom`.

Mockup values (read `explorer.html`): `.idchip` mono 11px, bordered pill, gap 6px, radius (line 345); panels use `PanelBrush` + `LineBrush` border + radius r-md + shadow; `.frame`/window uses radius r-win + shadow. Kind marker colours = `Kind*Brush`.

- [ ] **Step 1: Write the failing test**

Add to `ThemeResourceTests.cs`:
```csharp
    [AvaloniaFact]
    public void Panel_uses_panel_brush_and_kind_chip_uses_kind_colour()
    {
        var panel = StyledHost.Show(new Border { Classes = { "panel" } });
        var chip = StyledHost.Show(new Border { Classes = { "kind-dynamic" } });

        Assert.Equal(
            ((ISolidColorBrush)Application.Current!.FindResource("PanelBrush")!).Color,
            ((ISolidColorBrush)panel.Background!).Color);
        Assert.Equal(
            ((ISolidColorBrush)Application.Current!.FindResource("KindDynamicBrush")!).Color,
            ((ISolidColorBrush)chip.BorderBrush!).Color);
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~Panel_uses_panel_brush`
Expected: FAIL — the classes don't exist.

- [ ] **Step 3: Add surface & chip styles**

```xml
  <Style Selector="Border.panel">
    <Setter Property="Background" Value="{DynamicResource PanelBrush}" />
    <Setter Property="BorderBrush" Value="{DynamicResource LineBrush}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="CornerRadius" Value="{DynamicResource RadiusMd}" />
    <Setter Property="BoxShadow" Value="{DynamicResource WinShadow}" />
  </Style>
  <Style Selector="Border.tile">
    <Setter Property="Background" Value="{DynamicResource TileBrush}" />
    <Setter Property="BorderBrush" Value="{DynamicResource LineBrush}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="CornerRadius" Value="{DynamicResource RadiusSm}" />
  </Style>
  <Style Selector="Border.card">
    <Setter Property="Background" Value="{DynamicResource PanelBrush}" />
    <Setter Property="BorderBrush" Value="{DynamicResource LineBrush}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="CornerRadius" Value="{DynamicResource RadiusMd}" />
    <Setter Property="Padding" Value="14,12" />
  </Style>
  <Style Selector="Border.frame">
    <Setter Property="Background" Value="{DynamicResource BgBrush}" />
    <Setter Property="BorderBrush" Value="{DynamicResource LineStrongBrush}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="CornerRadius" Value="{DynamicResource RadiusWin}" />
    <Setter Property="BoxShadow" Value="{DynamicResource WinShadow}" />
  </Style>
  <Style Selector="Border.idchip">
    <Setter Property="Background" Value="{DynamicResource TileBrush}" />
    <Setter Property="BorderBrush" Value="{DynamicResource LineBrush}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="CornerRadius" Value="{DynamicResource RadiusXs}" />
    <Setter Property="Padding" Value="7,3" />
  </Style>
  <Style Selector="Border.kind-dynamic">
    <Setter Property="BorderBrush" Value="{DynamicResource KindDynamicBrush}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="CornerRadius" Value="{DynamicResource RadiusXs}" />
    <Setter Property="Padding" Value="6,2" />
  </Style>
  <Style Selector="Border.kind-static">
    <Setter Property="BorderBrush" Value="{DynamicResource KindStaticBrush}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="CornerRadius" Value="{DynamicResource RadiusXs}" />
    <Setter Property="Padding" Value="6,2" />
  </Style>
  <Style Selector="Border.kind-custom">
    <Setter Property="BorderBrush" Value="{DynamicResource KindCustomBrush}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="CornerRadius" Value="{DynamicResource RadiusXs}" />
    <Setter Property="Padding" Value="6,2" />
  </Style>
```
If Task 1's doc-pull found `BoxShadow="{DynamicResource WinShadow}"` doesn't accept a `BoxShadows` resource, inline the verbatim shadow string per theme instead (per Task 1 Step 6's fallback), keeping the value identical.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~ThemeResourceTests`
Expected: PASS. Whole App suite green.

- [ ] **Step 5: Commit**

```bash
git add src/Nfty.App/Themes/Styles.axaml tests/Nfty.App.Tests/ThemeResourceTests.cs
git commit -m "feat(gui): surface + chip styles (panel/tile/card/frame/idchip/kind)"
```

---

### Task 5: Inputs — ControlThemes for TextBox / Slider / NumericUpDown / RadioButton / CheckBox

**Files:**
- Create: `src/Nfty.App/Themes/Controls.axaml`
- Modify: `src/Nfty.Desktop/App.axaml` (merge `Controls.axaml`), `tests/Nfty.App.Tests/TestApp.axaml` (merge `Controls.axaml`)
- Test: `tests/Nfty.App.Tests/ThemeResourceTests.cs` (extend)

**Interfaces:**
- Consumes: `PanelBrush`, `LineStrongBrush`, `AccentBrush`, `FgBrush`, radii (Task 1).
- Produces: `ControlTheme`s (keyed `{x:Type TextBox}` etc.) restyling the inputs; `Controls.axaml` merged into both the real app and the test app.

**Doc-pull (objective, do this first):** confirm the Avalonia 11.2 pattern for a `ControlTheme` that restyles a built-in control while inheriting the Fluent look — `<ControlTheme x:Key="{x:Type TextBox}" TargetType="TextBox" BasedOn="{StaticResource {x:Type TextBox}}">` with `Setter`s for `Background`/`BorderBrush`/`CornerRadius`, and the `:focus`/`:pointerover` selectors for accent focus (Context7 `/avaloniaui/avalonia-docs`, query "ControlTheme BasedOn built-in TextBox restyle background border focus"). Match the mockup's input look (panel background, `LineStrongBrush` border, accent focus). Do the same for `Slider` (accent thumb/track), `NumericUpDown`, `RadioButton`, `CheckBox` (accent check/selected). Keep it to property-level setters `BasedOn` Fluent — do NOT hand-author full templates unless the doc-pull shows a setter can't reach a needed part; if a full template is unavoidable for one control, note why in the report.

- [ ] **Step 1: Create `Controls.axaml` with a resource dictionary**

`src/Nfty.App/Themes/Controls.axaml`:
```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <!-- Input ControlThemes: BasedOn the Fluent theme, restyled to the mockup.
       Colours pull from Tokens.axaml via DynamicResource so both ThemeVariants stay correct. -->
  <ControlTheme x:Key="{x:Type TextBox}" TargetType="TextBox"
                BasedOn="{StaticResource {x:Type TextBox}}">
    <Setter Property="Background" Value="{DynamicResource PanelBrush}" />
    <Setter Property="BorderBrush" Value="{DynamicResource LineStrongBrush}" />
    <Setter Property="CornerRadius" Value="{DynamicResource RadiusSm}" />
    <Setter Property="Foreground" Value="{DynamicResource FgBrush}" />
    <!-- add :focus accent per the doc-pull -->
  </ControlTheme>
  <!-- Slider, NumericUpDown, RadioButton, CheckBox ControlThemes added here, same pattern -->
</ResourceDictionary>
```
Fill in the remaining input `ControlTheme`s per the doc-pull, matching the mockup.

- [ ] **Step 2: Merge `Controls.axaml` into the real app and the test app**

`src/Nfty.Desktop/App.axaml` — add to `Application.Resources` `MergedDictionaries` (after the Tokens include):
```xml
        <ResourceInclude Source="avares://Nfty.App/Themes/Controls.axaml" />
```
`tests/Nfty.App.Tests/TestApp.axaml` — add the same `ResourceInclude` to its `MergedDictionaries`.

- [ ] **Step 3: Write the failing test**

Add to `ThemeResourceTests.cs`:
```csharp
    [AvaloniaFact]
    public void TextBox_is_restyled_with_panel_background()
    {
        var tb = StyledHost.Show(new TextBox { Text = "x" });
        Assert.Equal(
            ((ISolidColorBrush)Application.Current!.FindResource("PanelBrush")!).Color,
            ((ISolidColorBrush)tb.Background!).Color);
    }
```

- [ ] **Step 4: Run to verify it fails, then implement, then passes**

Run: `dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~TextBox_is_restyled`
Expected: FAIL before the `ControlTheme` is complete; PASS after. Then whole App suite green, and `dotnet build src/Nfty.Desktop --nologo` → 0 warnings (proves the app merges `Controls.axaml`).

- [ ] **Step 5: Commit**

```bash
git add src/Nfty.App/Themes/Controls.axaml src/Nfty.Desktop/App.axaml tests/Nfty.App.Tests/TestApp.axaml tests/Nfty.App.Tests/ThemeResourceTests.cs
git commit -m "feat(gui): input ControlThemes (TextBox/Slider/NumericUpDown/Radio/Check)"
```

---

### Task 6: Shell chrome — restyle `MainWindow.axaml`

**Files:**
- Modify: `src/Nfty.Desktop/MainWindow.axaml`

**Interfaces:**
- Consumes: all classes/tokens from Tasks 1–4 (`Border.frame`, `Button.icon`, `.wordmark`, `WinShadow`, brushes).
- Produces: the restyled window chrome. No VM/command/keybinding change.

Mockup values (read `explorer.html`): `.titlebar` height 46, padding `0 10 0 12`, gap 12 (line 82); `.brandtile` 24×24, radius r-sm, contains a 9×9 accent square rotated 45° (lines 87–89); `.wordmark` mono 700 15px, the "f" (or accent letter) in `AccentTextBrush` (line 90–91). Status bar and outer frame per the mockup.

- [ ] **Step 1: Restyle the titlebar**

In `MainWindow.axaml`, set the titlebar `Grid` height to 46 and rebuild the brand + window controls. Brand block:
```xml
      <StackPanel Grid.Column="0" Orientation="Horizontal" Margin="12,0,0,0" VerticalAlignment="Center" Spacing="9">
        <Border Width="24" Height="24" CornerRadius="{DynamicResource RadiusSm}"
                Background="{DynamicResource AccentWashBrush}">
          <Border Width="9" Height="9" CornerRadius="2" Background="{DynamicResource AccentBrush}">
            <Border.RenderTransform><RotateTransform Angle="45" /></Border.RenderTransform>
          </Border>
        </Border>
        <TextBlock Text="nfty" Classes="wordmark" VerticalAlignment="Center" />
      </StackPanel>
```
Window controls → `icon` buttons:
```xml
      <StackPanel Grid.Column="2" Orientation="Horizontal" VerticalAlignment="Center" Margin="0,0,6,0">
        <Button Content="—" Command="{Binding MinimizeCommand}" Classes="icon" />
        <Button Content="▢" Command="{Binding ToggleMaximizeCommand}" Classes="icon" />
        <Button Content="✕" Command="{Binding CloseCommand}" Classes="icon" />
      </StackPanel>
```

- [ ] **Step 2: Restyle the status bar controls**

Change the status bar's zoom/help `Button`s from `Classes="tbtn"` to `Classes="icon"`; keep bindings/commands identical.

- [ ] **Step 3: Wrap the page host in the outer frame (optional per mockup)**

If the mockup shows a framed inner surface, wrap the `Grid.Row="1"` page host `Panel` content in a `Border` with `Classes="frame"` and a small margin. Keep the `ContentControl` page host and the `DialogScrim` panel structure unchanged; only the visual wrapper is added.

- [ ] **Step 4: Build + smoke**

Run: `dotnet build src/Nfty.Desktop --nologo` → 0 warnings, 0 errors.
Run: `dotnet test tests/Nfty.App.Tests --nologo` → ViewLocator smoke + all App tests still green (chrome change touches no VM).
(`MainWindow` is head-specific and not unit-tested; correctness is the build + the manual smoke in Task 7.)

- [ ] **Step 5: Commit**

```bash
git add src/Nfty.Desktop/MainWindow.axaml
git commit -m "feat(gui): restyle window chrome (titlebar/brand/controls/status/frame)"
```

---

### Task 7: Full verification + manual smoke (both themes, zoom)

**Files:** none (verification).

- [ ] **Step 1: Full solution build + test**

Run: `dotnet build nfty.sln --nologo` → 0 warnings / 0 errors.
Run: `dotnet test nfty.sln --nologo` → all PASS (Core + Cli + App). No test outcome changed by this presentation-only slice except the added `ThemeResourceTests`.

- [ ] **Step 2: Guard greps**

Run: `grep -rniE "#[0-9a-f]{6}" src/Nfty.App/Themes/Styles.axaml src/Nfty.App/Themes/Controls.axaml src/Nfty.Desktop/MainWindow.axaml` — confirm no raw hex in the STYLES/chrome (all colour comes from token `DynamicResource`s; hex belongs only in `Tokens.axaml`).
Run: `git diff --stat main...HEAD -- src/Nfty.Core` — confirm empty (no Core change).

- [ ] **Step 3: Manual smoke (user-driven)**

Run: `dotnet run --project src/Nfty.Desktop`. Compare against `docs/design/mockups/explorer.html` side-by-side:
- Titlebar: brand tile (rotated accent square) + mono wordmark; window controls as borderless icon buttons with hover.
- Body text is sans; IDs/headings/crumbs/kind labels are mono.
- Buttons: `tbtn` panel + accent-line hover; `accent` accent + accent-hover; icon/ghost/dice.
- Panels/cards show the soft shadow; chips render; inputs (open a wizard) show the restyled look.
- Toggle **dark** (theme toggle) — every surface flips correctly, no stray light/dark leak.
- A couple of **zoom** levels via the status bar.
Log any CSS effect Avalonia can only approximate; escalate any that reads clearly wrong.

- [ ] **Step 4: Commit (only if smoke-driven fixups were needed)** — otherwise nothing to commit.

---

## Self-Review

**Spec coverage:**
- §2.1 token port completion → Task 1 (all missing tokens verbatim, both themes, fonts, radii, shadow). §2.2 typography (sans base + mono classes) → Task 2. §2.3 buttons (tbtn/accent/ghost/icon/dice + states) → Task 3. §2.4 surfaces & chips (panel/tile/card/frame/idchip/kind) → Task 4. §2.5 inputs (ControlThemes BasedOn Fluent) → Task 5. §2.6 shell chrome → Task 6. §4 testing (themed test host + resource/applied tests + smoke + manual, no golden images) → Tasks 1–7. §7 risks (BoxShadows syntax, ControlTheme vs Style, Fluent Button hover) → doc-pull notes in Tasks 1/3/5.
- Gap check: no `Nfty.Core`/VM/behaviour change in any task (only Themes + MainWindow + test files) — matches §6.

**Placeholder scan:** every code step shows the actual XAML/C#. The three "doc-pull" notes are concrete, sourced (Context7 `/avaloniaui/avalonia-docs`) verification steps with the exact query and a stated fallback, not "TBD". The Task 5 input `ControlTheme`s beyond `TextBox` and the Task 3 hover template-part selectors are the one place the implementer completes per the doc-pull — flagged explicitly with the pattern, the source, and the acceptance (manual smoke) rather than left vague.

**Type/name consistency:** token keys (`AccentHoverBrush`, `SuccessBrush`, `GuideBrush`, `GuideHiBrush`, `WinShadow`, `SansFontFamily`, `RadiusXs`, `RadiusLg`) are introduced in Task 1 and consumed by the exact same names in Tasks 2–6. Style class names (`mono`, `wordmark`, `crumbs`, `kind-txt`, `tbtn`, `accent`, `ghost`, `icon`, `dice`, `panel`, `tile`, `card`, `frame`, `idchip`, `kind-dynamic/static/custom`) are defined once and reused consistently. `StyledHost.Show` (Task 2) is reused by Tasks 3–5. `Controls.axaml` (Task 5) is merged into both `App.axaml` and `TestApp.axaml`.
