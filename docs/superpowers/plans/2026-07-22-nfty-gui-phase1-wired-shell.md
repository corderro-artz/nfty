# nfty Avalonia GUI — Phase 1 (Wired Shell) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the fully-wired, themed, VM-tested Avalonia desktop shell for nfty — every button/link/reference on all six screens bound to a command or navigation, with `Nfty.Core`-backed actions wired to a visible stub. No real Core behavior yet.

**Architecture:** A shared `Nfty.App` (Avalonia, head-agnostic) + a `Nfty.Desktop` head + `Nfty.App.Tests`. CommunityToolkit.Mvvm ViewModels over a `ViewModelBase`; a `ShellViewModel` swaps `CurrentPage` (rendered by a `ViewLocator`) and shows an `ActiveDialog` overlay. Navigation, dialogs, file-picking, recents, theming, and a "not-yet-wired" notifier are injected services with test fakes. Wiring is classified `nav` (real) / `ui-state` (real) / `stub` (calls `INotYetWired`).

**Tech Stack:** .NET 10, Avalonia 11 (Fluent theme base), CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection, xUnit + Avalonia.Headless.XUnit. Backed by the existing `Nfty.Core`.

**Reference spec:** `docs/superpowers/specs/2026-07-22-nfty-gui-completion-design.md` (the §6 Wiring Map is the control-by-control source of truth).

## Global Constraints

- Target framework **net10.0** across all new projects; `Nfty.App` references `Nfty.Core` and must stay **head-agnostic** (no `Avalonia.Desktop`/desktop-only APIs).
- **MVVM = CommunityToolkit.Mvvm**: `partial` VM classes derive `ViewModelBase : ObservableObject`; observable state via `[ObservableProperty]` on a private field; commands via `[RelayCommand]` on a private method. Generated members are PascalCase (`_currentPage` → `CurrentPage`; `NewCookBook()` → `NewCookBookCommand`).
- **No dead control.** Every interactive element is bound to a command/navigation. Every `Nfty.Core`-backed action (open/import/add/cook/save/persist) calls `INotYetWired.Report("<action name>")` in Phase 1 — never a crash, never silent.
- **Single source of truth for colour** = `Themes/Tokens.axaml`. A colour literal anywhere else is drift; use `{DynamicResource}`/`{StaticResource}` token brushes. Light + dark via `ThemeVariant`.
- **Pure VM wiring tests use `[Fact]`** (no Avalonia thread). View/ViewLocator smoke tests use `[AvaloniaFact]` and the assembly attribute `[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]`.
- xUnit test methods are `Snake_case_sentences`. Commits are conventional-commit style ending with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- Ordinal string comparison for id/type-name operations.

---

## File Structure

**Create (projects):**
- `src/Nfty.App/Nfty.App.csproj` — Avalonia class library (`net10.0`, `Avalonia`, `CommunityToolkit.Mvvm`, `Microsoft.Extensions.DependencyInjection`), refs `Nfty.Core`.
- `src/Nfty.Desktop/Nfty.Desktop.csproj` — Avalonia desktop app (`net10.0`, `Avalonia.Desktop`), refs `Nfty.App`. `Program.cs`, `App.axaml`(+`.cs`), `MainWindow.axaml`(+`.cs`).
- `tests/Nfty.App.Tests/Nfty.App.Tests.csproj` — xUnit + `Avalonia.Headless.XUnit`, refs `Nfty.App`.

**Create (Nfty.App):**
- `ViewLocator.cs`, `ViewModels/ViewModelBase.cs`, `ServiceRegistration.cs`
- `Services/`: `INavigationService.cs`(+`NavigationService`), `IDialogService.cs`(+`DialogService`), `INotYetWired.cs`(+`NotYetWired`), `IFilePickerService.cs`(+`FilePickerService`), `IRecentsService.cs`(+`RecentsService`), `IThemeService.cs`(+`ThemeService`)
- `ViewModels/ShellViewModel.cs`, `LandingViewModel.cs`, `HelpViewModel.cs`, `NewCookBookViewModel.cs`, `NewRecipeViewModel.cs`, `NewIngredientViewModel.cs`, `ExplorerViewModel.cs`, `CookBookDetailViewModel.cs`, `RecipeDetailViewModel.cs`, `IngredientDetailViewModel.cs`, `IngredientEditorViewModel.cs`, plus small records (`ExplorerNode`, `RecentItem`, wizard result records).
- `Views/`: one `.axaml`(+`.cs`) per screen VM (`LandingView`, `HelpView`, `NewCookBookView`, `NewRecipeView`, `NewIngredientView`, `ExplorerView`, `IngredientEditorView`) and `MainWindow` lives in Desktop.
- `Themes/Tokens.axaml`, `Themes/Styles.axaml`.

**Modify:** `nfty.sln` (add the three projects).

Task order builds infrastructure first, then screens, then the coverage/smoke gate.

---

## Task 1: Solution scaffold + boot smoke test

**Files:**
- Create: `src/Nfty.App/Nfty.App.csproj`, `src/Nfty.App/App.axaml.placeholder` (removed later), `src/Nfty.Desktop/Nfty.Desktop.csproj`, `src/Nfty.Desktop/Program.cs`, `src/Nfty.Desktop/App.axaml`, `src/Nfty.Desktop/App.axaml.cs`, `src/Nfty.Desktop/MainWindow.axaml`, `src/Nfty.Desktop/MainWindow.axaml.cs`
- Create: `tests/Nfty.App.Tests/Nfty.App.Tests.csproj`, `tests/Nfty.App.Tests/TestAppBuilder.cs`, `tests/Nfty.App.Tests/BootTests.cs`
- Modify: `nfty.sln`

**Interfaces:**
- Produces: runnable `Nfty.Desktop` app; `Nfty.App` library; a headless test harness (`TestAppBuilder`) later tasks reuse.

- [ ] **Step 1: Create the three project files**

`src/Nfty.App/Nfty.App.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Avalonia" Version="11.2.3" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="11.2.3" />
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.0" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="9.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Nfty.Core\Nfty.Core.csproj" />
  </ItemGroup>
</Project>
```

`src/Nfty.Desktop/Nfty.Desktop.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <BuiltInComInteropSupport>true</BuiltInComInteropSupport>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Avalonia.Desktop" Version="11.2.3" />
    <PackageReference Include="Avalonia.Diagnostics" Version="11.2.3" Condition="'$(Configuration)'=='Debug'" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Nfty.App\Nfty.App.csproj" />
  </ItemGroup>
</Project>
```

Create `src/Nfty.Desktop/app.manifest` (standard Avalonia manifest):
```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="Nfty.Desktop"/>
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
    </windowsSettings>
  </application>
</assembly>
```

`tests/Nfty.App.Tests/Nfty.App.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="Avalonia.Headless.XUnit" Version="11.2.3" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Nfty.App\Nfty.App.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create the minimal Desktop app entry**

`src/Nfty.Desktop/Program.cs`:
```csharp
using Avalonia;

namespace Nfty.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .LogToTrace();
}
```

`src/Nfty.Desktop/App.axaml`:
```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="Nfty.Desktop.App"
             RequestedThemeVariant="Default">
  <Application.Styles>
    <FluentTheme />
  </Application.Styles>
</Application>
```

`src/Nfty.Desktop/App.axaml.cs`:
```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Nfty.Desktop;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
```

`src/Nfty.Desktop/MainWindow.axaml`:
```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="Nfty.Desktop.MainWindow"
        Width="1180" Height="720" Title="nfty">
  <TextBlock Name="BootText" Text="nfty" HorizontalAlignment="Center" VerticalAlignment="Center" />
</Window>
```

`src/Nfty.Desktop/MainWindow.axaml.cs`:
```csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Nfty.Desktop;

public partial class MainWindow : Window
{
    public MainWindow() => AvaloniaXamlLoader.Load(this);
}
```

Delete the placeholder `src/Nfty.App/App.axaml.placeholder` if created; `Nfty.App` needs no entry point (it's a library). Add a trivial type so the library isn't empty — create `src/Nfty.App/AssemblyMarker.cs`:
```csharp
namespace Nfty.App;

/// <summary>Marks the Nfty.App assembly (used by tests to locate app types).</summary>
public static class AssemblyMarker { }
```

- [ ] **Step 3: Create the headless test harness + a boot test**

`tests/Nfty.App.Tests/TestAppBuilder.cs`:
```csharp
using Avalonia;
using Avalonia.Headless;
using Nfty.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Nfty.App.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Avalonia.Application>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
```

`tests/Nfty.App.Tests/BootTests.cs`:
```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Nfty.App;
using Xunit;

namespace Nfty.App.Tests;

public class BootTests
{
    [Fact]
    public void App_assembly_marker_is_reachable() => Assert.NotNull(typeof(AssemblyMarker));

    [AvaloniaFact]
    public void A_headless_window_can_be_shown()
    {
        var window = new Window { Content = new TextBlock { Text = "nfty" } };
        window.Show();
        Assert.True(window.IsVisible);
    }
}
```

- [ ] **Step 4: Add projects to the solution**

Run:
```bash
dotnet sln nfty.sln add src/Nfty.App/Nfty.App.csproj src/Nfty.Desktop/Nfty.Desktop.csproj tests/Nfty.App.Tests/Nfty.App.Tests.csproj
```

- [ ] **Step 5: Build + run the tests**

Run: `dotnet build nfty.sln --nologo`
Expected: Build succeeded, 0 errors (restore pulls Avalonia 11.2.3). If `net10.0` is rejected by a package, bump Avalonia to the newest 11.x that lists net10 support and record it in this plan's Global Constraints.

Run: `dotnet test tests/Nfty.App.Tests --nologo`
Expected: PASS (2 tests).

- [ ] **Step 6: Verify the desktop app launches (manual smoke, non-blocking)**

Run: `dotnet build src/Nfty.Desktop --nologo`
Expected: Build succeeded. (Launching the window is optional here; the headless test already proves the UI stack boots.)

- [ ] **Step 7: Commit**
```bash
git add src/Nfty.App src/Nfty.Desktop tests/Nfty.App.Tests nfty.sln
git commit -m "$(printf 'feat(gui): scaffold Nfty.App + Nfty.Desktop + test harness\n\nAvalonia 11 shared library, desktop head, and headless xUnit harness; boots\nan empty window. Foundation for the wired shell.\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

## Task 2: ViewModelBase, DI, and the ViewLocator

**Files:**
- Create: `src/Nfty.App/ViewModels/ViewModelBase.cs`, `src/Nfty.App/ViewLocator.cs`, `src/Nfty.App/ServiceRegistration.cs`
- Modify: `src/Nfty.Desktop/App.axaml` (register ViewLocator + build DI), `src/Nfty.Desktop/App.axaml.cs`
- Test: `tests/Nfty.App.Tests/ViewLocatorTests.cs`

**Interfaces:**
- Produces: `ViewModelBase : ObservableObject`; `ViewLocator : IDataTemplate` (maps `Nfty.App.ViewModels.XyzViewModel` → `Nfty.App.Views.XyzView`); `ServiceRegistration.AddNftyApp(IServiceCollection)` (extended in Task 4).

- [ ] **Step 1: Write the failing ViewLocator test**

`tests/Nfty.App.Tests/ViewLocatorTests.cs`:
```csharp
using Avalonia.Controls;
using Nfty.App;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

public class ViewLocatorTests
{
    private sealed class SampleViewModel : ViewModelBase { }

    [Fact]
    public void Build_returns_a_placeholder_for_a_viewmodel_with_no_view()
    {
        var locator = new ViewLocator();
        var control = locator.Build(new SampleViewModel());
        Assert.IsType<TextBlock>(control);
    }

    [Fact]
    public void Match_is_true_for_viewmodels_and_false_otherwise()
    {
        var locator = new ViewLocator();
        Assert.True(locator.Match(new SampleViewModel()));
        Assert.False(locator.Match("not a vm"));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Nfty.App.Tests --filter "FullyQualifiedName~ViewLocatorTests" --nologo`
Expected: FAIL — `ViewModelBase`/`ViewLocator` don't exist (compile error).

- [ ] **Step 3: Implement ViewModelBase + ViewLocator**

`src/Nfty.App/ViewModels/ViewModelBase.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace Nfty.App.ViewModels;

/// <summary>Base for every ViewModel. Adds nothing yet but centralizes the MVVM base type.</summary>
public abstract class ViewModelBase : ObservableObject { }
```

`src/Nfty.App/ViewLocator.cs`:
```csharp
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Nfty.App.ViewModels;

namespace Nfty.App;

/// <summary>
/// Resolves a View for a ViewModel by convention: replace "ViewModel" with "View" in the full type
/// name (ViewModels namespace → Views namespace). Returns a labelled placeholder when no View exists,
/// so an unmapped VM is visible rather than blank.
/// </summary>
public class ViewLocator : IDataTemplate
{
    public Control Build(object? data)
    {
        if (data is null) return new TextBlock { Text = "No data" };
        var name = data.GetType().FullName!
            .Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);
        return type is not null
            ? (Control)Activator.CreateInstance(type)!
            : new TextBlock { Text = $"View not found: {name}" };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
```

`src/Nfty.App/ServiceRegistration.cs` (stub; Task 4 fills service registrations):
```csharp
using Microsoft.Extensions.DependencyInjection;

namespace Nfty.App;

public static class ServiceRegistration
{
    /// <summary>Registers all Nfty.App services and ViewModels. Extended by later tasks.</summary>
    public static IServiceCollection AddNftyApp(this IServiceCollection services) => services;
}
```

- [ ] **Step 4: Wire the ViewLocator + DI into the Desktop App**

Replace `src/Nfty.Desktop/App.axaml`:
```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:app="using:Nfty.App"
             x:Class="Nfty.Desktop.App"
             RequestedThemeVariant="Default">
  <Application.DataTemplates>
    <app:ViewLocator />
  </Application.DataTemplates>
  <Application.Styles>
    <FluentTheme />
  </Application.Styles>
</Application>
```

Replace `src/Nfty.Desktop/App.axaml.cs`:
```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Nfty.App;

namespace Nfty.Desktop;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection().AddNftyApp().BuildServiceProvider();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();  // DataContext set in Task 5
        base.OnFrameworkInitializationCompleted();
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/Nfty.App.Tests --filter "FullyQualifiedName~ViewLocatorTests" --nologo`
Expected: PASS (2).

- [ ] **Step 6: Commit**
```bash
git add src/Nfty.App src/Nfty.Desktop tests/Nfty.App.Tests
git commit -m "$(printf 'feat(gui): ViewModelBase, convention ViewLocator, DI entry\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

## Task 3: Theme tokens + base styles

**Files:**
- Create: `src/Nfty.App/Themes/Tokens.axaml`, `src/Nfty.App/Themes/Styles.axaml`
- Modify: `src/Nfty.Desktop/App.axaml` (merge the dictionaries)
- Test: `tests/Nfty.App.Tests/ThemeTests.cs`

**Interfaces:**
- Produces: token brush keys used by every View — `AccentBrush`, `AccentTextBrush`, `BgBrush`, `BgAltBrush`, `PanelBrush`, `TileBrush`, `FgBrush`, `FgMutedBrush`, `LineBrush`, `LineStrongBrush`, `KindDynamicBrush`, `KindStaticBrush`, `KindCustomBrush`; a `MonoFontFamily` resource; theme-aware via `ThemeVariant`.

Port the mockups' locked token block (`docs/design/mockups/explorer.html` lines ~2–14) into Avalonia resources. Values are copied verbatim from the mockup — the light set and the dark set.

- [ ] **Step 1: Write the failing theme test**

`tests/Nfty.App.Tests/ThemeTests.cs`:
```csharp
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Xunit;

namespace Nfty.App.Tests;

public class ThemeTests
{
    private static Avalonia.Controls.ResourceDictionary LoadTokens()
    {
        var uri = new Uri("avares://Nfty.App/Themes/Tokens.axaml");
        return (Avalonia.Controls.ResourceDictionary)Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(uri)!;
    }

    [AvaloniaFact]
    public void Tokens_expose_the_accent_brush_in_both_variants()
    {
        var dict = LoadTokens();
        Assert.True(dict.TryGetResource("AccentBrush", ThemeVariant.Light, out var light));
        Assert.True(dict.TryGetResource("AccentBrush", ThemeVariant.Dark, out var dark));
        Assert.IsAssignableFrom<IBrush>(light);
        Assert.IsAssignableFrom<IBrush>(dark);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Nfty.App.Tests --filter "FullyQualifiedName~ThemeTests" --nologo`
Expected: FAIL — `Tokens.axaml` does not exist.

- [ ] **Step 3: Create Tokens.axaml**

`src/Nfty.App/Themes/Tokens.axaml` (values verbatim from the mockup's `.nfty-scope` light block and `prefers-color-scheme: dark` block):
```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <ResourceDictionary.ThemeDictionaries>
    <ResourceDictionary x:Key="Light">
      <SolidColorBrush x:Key="BgBrush" Color="#f4efe8" />
      <SolidColorBrush x:Key="BgAltBrush" Color="#f1ece4" />
      <SolidColorBrush x:Key="BgAlt2Brush" Color="#ede7df" />
      <SolidColorBrush x:Key="PanelBrush" Color="#f8f3ed" />
      <SolidColorBrush x:Key="TileBrush" Color="#ece5db" />
      <SolidColorBrush x:Key="FgBrush" Color="#121318" />
      <SolidColorBrush x:Key="FgMutedBrush" Color="#121418b8" />
      <SolidColorBrush x:Key="LineBrush" Color="#1214181f" />
      <SolidColorBrush x:Key="LineStrongBrush" Color="#12141833" />
      <SolidColorBrush x:Key="AccentBrush" Color="#a11f31" />
      <SolidColorBrush x:Key="AccentTextBrush" Color="#97192a" />
      <SolidColorBrush x:Key="OnAccentBrush" Color="#f7f2ec" />
      <SolidColorBrush x:Key="AccentWashBrush" Color="#a11f3114" />
      <SolidColorBrush x:Key="AccentLineBrush" Color="#a11f3140" />
      <SolidColorBrush x:Key="KindDynamicBrush" Color="#3b5b6f" />
      <SolidColorBrush x:Key="KindStaticBrush" Color="#8a6d1f" />
      <SolidColorBrush x:Key="KindCustomBrush" Color="#6b4a86" />
    </ResourceDictionary>
    <ResourceDictionary x:Key="Dark">
      <SolidColorBrush x:Key="BgBrush" Color="#07080b" />
      <SolidColorBrush x:Key="BgAltBrush" Color="#0a0b10" />
      <SolidColorBrush x:Key="BgAlt2Brush" Color="#0f1118" />
      <SolidColorBrush x:Key="PanelBrush" Color="#0b0c10" />
      <SolidColorBrush x:Key="TileBrush" Color="#12141c" />
      <SolidColorBrush x:Key="FgBrush" Color="#f2ede6" />
      <SolidColorBrush x:Key="FgMutedBrush" Color="#f2ede6c7" />
      <SolidColorBrush x:Key="LineBrush" Color="#f2ede624" />
      <SolidColorBrush x:Key="LineStrongBrush" Color="#f2ede633" />
      <SolidColorBrush x:Key="AccentBrush" Color="#a11f31" />
      <SolidColorBrush x:Key="AccentTextBrush" Color="#e0788a" />
      <SolidColorBrush x:Key="OnAccentBrush" Color="#f7f2ec" />
      <SolidColorBrush x:Key="AccentWashBrush" Color="#a11f3126" />
      <SolidColorBrush x:Key="AccentLineBrush" Color="#a11f3166" />
      <SolidColorBrush x:Key="KindDynamicBrush" Color="#7fb0c4" />
      <SolidColorBrush x:Key="KindStaticBrush" Color="#d8b25a" />
      <SolidColorBrush x:Key="KindCustomBrush" Color="#b79bd6" />
    </ResourceDictionary>
  </ResourceDictionary.ThemeDictionaries>

  <FontFamily x:Key="MonoFontFamily">Consolas, Menlo, monospace</FontFamily>
  <x:Double x:Key="RadiusWin">10</x:Double>
  <x:Double x:Key="RadiusMd">8</x:Double>
  <x:Double x:Key="RadiusSm">5</x:Double>
</ResourceDictionary>
```
Note: the kind hues (`--info`/`--warning`/`--custom`) are read off the mockup; if the mockup's exact hex differs from the light values above, use the mockup's — it is the lock. Adjust and note it here.

- [ ] **Step 4: Create Styles.axaml (base component themes)**

`src/Nfty.App/Themes/Styles.axaml` — the shared idioms as styles. Start minimal (window bg, accent button, toolbar button, muted text); expand as screens need them:
```xml
<Styles xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <Style Selector="Window">
    <Setter Property="Background" Value="{DynamicResource BgBrush}" />
    <Setter Property="FontFamily" Value="{DynamicResource MonoFontFamily}" />
  </Style>
  <Style Selector="Button.accent">
    <Setter Property="Background" Value="{DynamicResource AccentBrush}" />
    <Setter Property="Foreground" Value="{DynamicResource OnAccentBrush}" />
    <Setter Property="Padding" Value="12,9" />
    <Setter Property="CornerRadius" Value="6" />
  </Style>
  <Style Selector="Button.tbtn">
    <Setter Property="Background" Value="{DynamicResource PanelBrush}" />
    <Setter Property="Foreground" Value="{DynamicResource FgBrush}" />
    <Setter Property="BorderBrush" Value="{DynamicResource LineStrongBrush}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="Padding" Value="12,9" />
    <Setter Property="CornerRadius" Value="6" />
    <Setter Property="HorizontalContentAlignment" Value="Left" />
  </Style>
  <Style Selector="TextBlock.muted">
    <Setter Property="Foreground" Value="{DynamicResource FgMutedBrush}" />
  </Style>
</Styles>
```

- [ ] **Step 5: Merge dictionaries into the Desktop App**

Update `src/Nfty.Desktop/App.axaml` — add resource + style includes:
```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:app="using:Nfty.App"
             x:Class="Nfty.Desktop.App"
             RequestedThemeVariant="Default">
  <Application.DataTemplates>
    <app:ViewLocator />
  </Application.DataTemplates>
  <Application.Resources>
    <ResourceInclude Source="avares://Nfty.App/Themes/Tokens.axaml" />
  </Application.Resources>
  <Application.Styles>
    <FluentTheme />
    <StyleInclude Source="avares://Nfty.App/Themes/Styles.axaml" />
  </Application.Styles>
</Application>
```
Ensure both `.axaml` files are `AvaloniaResource` (default for `.axaml` under an Avalonia project; no action needed unless a build error says otherwise, then add `<AvaloniaResource Include="Themes/*.axaml" />`).

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/Nfty.App.Tests --filter "FullyQualifiedName~ThemeTests" --nologo`
Expected: PASS.

- [ ] **Step 7: Commit**
```bash
git add src/Nfty.App/Themes src/Nfty.Desktop/App.axaml tests/Nfty.App.Tests/ThemeTests.cs
git commit -m "$(printf 'feat(gui): port locked token block to Avalonia theme resources\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

## Task 4: Services (navigation, dialogs, not-yet-wired, pickers, recents, theme) + fakes

**Files:**
- Create: `src/Nfty.App/Services/INavigationService.cs`, `IDialogService.cs`, `INotYetWired.cs`, `IFilePickerService.cs`, `IRecentsService.cs`, `IThemeService.cs` (each with its production impl)
- Create: `src/Nfty.App/Models/RecentItem.cs`
- Modify: `src/Nfty.App/ServiceRegistration.cs`
- Test: `tests/Nfty.App.Tests/Fakes.cs`, `tests/Nfty.App.Tests/ServiceTests.cs`

**Interfaces:**
- Produces (consumed by all later VMs):
  - `INavigationService { ViewModelBase? Current; void To(ViewModelBase page); void Back(); event Action? Changed; }`
  - `IDialogService { Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog); void Close(object? result); ViewModelBase? Active { get; } event Action? Changed; }`
  - `INotYetWired { void Report(string action); string? Last { get; } event Action<string>? Reported; }`
  - `IFilePickerService { Task<string?> OpenFileAsync(string title, params string[] extensions); Task<string?> SaveFileAsync(string title, string defaultExtension); }`
  - `IRecentsService { IReadOnlyList<RecentItem> Items { get; } void Add(RecentItem item); }`
  - `IThemeService { bool IsDark { get; } void Toggle(); }`
  - `record RecentItem(string Name, string Meta, string Path, bool Loose)`

- [ ] **Step 1: Write the failing service tests**

`tests/Nfty.App.Tests/ServiceTests.cs`:
```csharp
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

public class ServiceTests
{
    private sealed class PageA : ViewModelBase { }
    private sealed class PageB : ViewModelBase { }

    [Fact]
    public void Navigation_sets_current_and_back_restores_previous()
    {
        var nav = new NavigationService();
        var a = new PageA();
        var b = new PageB();
        nav.To(a);
        nav.To(b);
        Assert.Same(b, nav.Current);
        nav.Back();
        Assert.Same(a, nav.Current);
    }

    [Fact]
    public void NotYetWired_records_and_raises_the_last_action()
    {
        var n = new NotYetWired();
        string? seen = null;
        n.Reported += a => seen = a;
        n.Report("Open CookBook");
        Assert.Equal("Open CookBook", n.Last);
        Assert.Equal("Open CookBook", seen);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Nfty.App.Tests --filter "FullyQualifiedName~ServiceTests" --nologo`
Expected: FAIL — services don't exist.

- [ ] **Step 3: Implement the services**

`src/Nfty.App/Models/RecentItem.cs`:
```csharp
namespace Nfty.App.Models;

public record RecentItem(string Name, string Meta, string Path, bool Loose);
```

`src/Nfty.App/Services/INavigationService.cs`:
```csharp
using Nfty.App.ViewModels;

namespace Nfty.App.Services;

public interface INavigationService
{
    ViewModelBase? Current { get; }
    event Action? Changed;
    void To(ViewModelBase page);
    void Back();
}

public sealed class NavigationService : INavigationService
{
    private readonly Stack<ViewModelBase> _stack = new();
    public ViewModelBase? Current => _stack.Count > 0 ? _stack.Peek() : null;
    public event Action? Changed;

    public void To(ViewModelBase page) { _stack.Push(page); Changed?.Invoke(); }
    public void Back() { if (_stack.Count > 1) { _stack.Pop(); Changed?.Invoke(); } }
}
```

`src/Nfty.App/Services/IDialogService.cs`:
```csharp
using Nfty.App.ViewModels;

namespace Nfty.App.Services;

public interface IDialogService
{
    ViewModelBase? Active { get; }
    event Action? Changed;
    Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog);
    void Close(object? result);
}

public sealed class DialogService : IDialogService
{
    private ViewModelBase? _active;
    private TaskCompletionSource<object?>? _tcs;

    public ViewModelBase? Active => _active;
    public event Action? Changed;

    public Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog)
    {
        _active = dialog;
        _tcs = new TaskCompletionSource<object?>();
        Changed?.Invoke();
        return _tcs.Task.ContinueWith(t => (TResult?)(t.Result is TResult r ? r : default));
    }

    public void Close(object? result)
    {
        _active = null;
        Changed?.Invoke();
        _tcs?.TrySetResult(result);
        _tcs = null;
    }
}
```

`src/Nfty.App/Services/INotYetWired.cs`:
```csharp
namespace Nfty.App.Services;

/// <summary>
/// The Phase-1 stub notifier. Every Core-backed action calls Report; the shell surfaces the message
/// on the status line. Phase 2 replaces each caller's body with the real Core call.
/// </summary>
public interface INotYetWired
{
    string? Last { get; }
    event Action<string>? Reported;
    void Report(string action);
}

public sealed class NotYetWired : INotYetWired
{
    public string? Last { get; private set; }
    public event Action<string>? Reported;
    public void Report(string action) { Last = action; Reported?.Invoke(action); }
}
```

`src/Nfty.App/Services/IFilePickerService.cs`:
```csharp
namespace Nfty.App.Services;

/// <summary>File open/save dialogs. Phase-1 desktop impl returns null (no picker wired yet); the
/// commands that use it are stubs until Phase 2, so a null result is never dereferenced.</summary>
public interface IFilePickerService
{
    Task<string?> OpenFileAsync(string title, params string[] extensions);
    Task<string?> SaveFileAsync(string title, string defaultExtension);
}

public sealed class FilePickerService : IFilePickerService
{
    public Task<string?> OpenFileAsync(string title, params string[] extensions) => Task.FromResult<string?>(null);
    public Task<string?> SaveFileAsync(string title, string defaultExtension) => Task.FromResult<string?>(null);
}
```

`src/Nfty.App/Services/IRecentsService.cs`:
```csharp
using Nfty.App.Models;

namespace Nfty.App.Services;

public interface IRecentsService
{
    IReadOnlyList<RecentItem> Items { get; }
    void Add(RecentItem item);
}

/// <summary>Phase-1 recents: seeded with the mockup's sample rows so the Landing renders its list;
/// persistence lands in Phase 2.</summary>
public sealed class RecentsService : IRecentsService
{
    private readonly List<RecentItem> _items =
    [
        new("VaporPets", "3 recipes · 1000×1000", "~/art/vaporpets.cbk", false),
        new("NeonKoi", "1 recipe · 512×512", "~/art/neonkoi.cbk", false),
        new("aura.igt", "loose ingredient · 4 variants", "Kitchen", true),
    ];
    public IReadOnlyList<RecentItem> Items => _items;
    public void Add(RecentItem item) => _items.Insert(0, item);
}
```

`src/Nfty.App/Services/IThemeService.cs`:
```csharp
using Avalonia;
using Avalonia.Styling;

namespace Nfty.App.Services;

public interface IThemeService
{
    bool IsDark { get; }
    void Toggle();
}

public sealed class ThemeService : IThemeService
{
    public bool IsDark => Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
    public void Toggle()
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = IsDark ? ThemeVariant.Light : ThemeVariant.Dark;
    }
}
```

- [ ] **Step 4: Register services + all VMs in ServiceRegistration**

Replace `src/Nfty.App/ServiceRegistration.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using Nfty.App.Services;
using Nfty.App.ViewModels;

namespace Nfty.App;

public static class ServiceRegistration
{
    public static IServiceCollection AddNftyApp(this IServiceCollection services)
    {
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<INotYetWired, NotYetWired>();
        services.AddSingleton<IFilePickerService, FilePickerService>();
        services.AddSingleton<IRecentsService, RecentsService>();
        services.AddSingleton<IThemeService, ThemeService>();

        services.AddSingleton<ShellViewModel>();
        services.AddTransient<LandingViewModel>();
        services.AddTransient<ExplorerViewModel>();
        services.AddTransient<IngredientEditorViewModel>();
        services.AddTransient<HelpViewModel>();
        services.AddTransient<NewCookBookViewModel>();
        services.AddTransient<NewRecipeViewModel>();
        services.AddTransient<NewIngredientViewModel>();
        return services;
    }
}
```
This references VMs created in later tasks. To keep the solution compiling task-by-task, add the VM registrations **incrementally** as each VM lands (comment out not-yet-created lines, or add each `AddTransient` in the task that creates the VM). Re-list them here so the final state is unambiguous.

- [ ] **Step 5: Add test fakes**

`tests/Nfty.App.Tests/Fakes.cs`:
```csharp
using Nfty.App.Services;
using Nfty.App.ViewModels;

namespace Nfty.App.Tests;

public sealed class FakeNav : INavigationService
{
    public ViewModelBase? Current { get; private set; }
    public event Action? Changed;
    public void To(ViewModelBase page) { Current = page; Changed?.Invoke(); }
    public void Back() { }
}

public sealed class FakeDialogs : IDialogService
{
    public ViewModelBase? Active { get; private set; }
    public event Action? Changed;
    public Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog) { Active = dialog; return Task.FromResult<TResult?>(default); }
    public void Close(object? result) { Active = null; }
}

public sealed class FakeNotYetWired : INotYetWired
{
    public string? Last { get; private set; }
    public event Action<string>? Reported;
    public void Report(string action) { Last = action; Reported?.Invoke(action); }
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/Nfty.App.Tests --filter "FullyQualifiedName~ServiceTests" --nologo`
Expected: PASS. (Comment the not-yet-created VM registrations in `ServiceRegistration` so the project compiles; each later task uncomments its line.)

- [ ] **Step 7: Commit**
```bash
git add src/Nfty.App/Services src/Nfty.App/Models src/Nfty.App/ServiceRegistration.cs tests/Nfty.App.Tests/Fakes.cs tests/Nfty.App.Tests/ServiceTests.cs
git commit -m "$(printf 'feat(gui): navigation, dialog, not-yet-wired, picker, recents, theme services\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

## Task 5: ShellViewModel + MainWindow chrome + dialog overlay

**Files:**
- Create: `src/Nfty.App/ViewModels/ShellViewModel.cs`
- Move/Modify: `src/Nfty.Desktop/MainWindow.axaml`(+`.cs`) into the shell layout; bind to `ShellViewModel`
- Modify: `src/Nfty.Desktop/App.axaml.cs` (set `MainWindow.DataContext = shell`)
- Test: `tests/Nfty.App.Tests/ShellViewModelTests.cs`

**Interfaces:**
- Consumes: `INavigationService`, `IDialogService`, `INotYetWired`, `IThemeService`, `LandingViewModel` (Task 6 — for the initial page).
- Produces: `ShellViewModel` with `CurrentPage`, `ActiveDialog`, `Zoom`, `StatusMessage`, and commands `ShowHelpCommand`, `ZoomInCommand`/`ZoomOutCommand`/`ZoomResetCommand`, `MinimizeCommand`/`ToggleMaximizeCommand`/`CloseCommand`, `OpenKitchenCommand`, `ToggleThemeCommand`. Window commands raise events the `MainWindow` handles.

- [ ] **Step 1: Write the failing ShellViewModel tests**

`tests/Nfty.App.Tests/ShellViewModelTests.cs`:
```csharp
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

public class ShellViewModelTests
{
    private static ShellViewModel Make(out FakeNotYetWired notify)
    {
        notify = new FakeNotYetWired();
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var shell = new ShellViewModel(nav, dialogs, notify, new StubTheme());
        return shell;
    }

    private sealed class StubTheme : Nfty.App.Services.IThemeService
    { public bool IsDark { get; private set; } public void Toggle() => IsDark = !IsDark; }

    [Fact]
    public void Zoom_in_and_out_stays_within_50_to_300()
    {
        var shell = Make(out _);
        for (int i = 0; i < 50; i++) shell.ZoomInCommand.Execute(null);
        Assert.True(shell.Zoom <= 300);
        for (int i = 0; i < 50; i++) shell.ZoomOutCommand.Execute(null);
        Assert.True(shell.Zoom >= 50);
        shell.ZoomResetCommand.Execute(null);
        Assert.Equal(100, shell.Zoom);
    }

    [Fact]
    public void Open_kitchen_reports_not_yet_wired()
    {
        var shell = Make(out var notify);
        shell.OpenKitchenCommand.Execute(null);
        Assert.Equal("New Kitchen", notify.Last);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Nfty.App.Tests --filter "FullyQualifiedName~ShellViewModelTests" --nologo`
Expected: FAIL — `ShellViewModel` does not exist.

- [ ] **Step 3: Implement ShellViewModel**

`src/Nfty.App/ViewModels/ShellViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

public partial class ShellViewModel : ViewModelBase
{
    private readonly INavigationService _nav;
    private readonly IDialogService _dialogs;
    private readonly INotYetWired _notify;
    private readonly IThemeService _theme;

    [ObservableProperty] private ViewModelBase? _currentPage;
    [ObservableProperty] private ViewModelBase? _activeDialog;
    [ObservableProperty] private int _zoom = 100;
    [ObservableProperty] private string _statusMessage = "";

    public event Action? MinimizeRequested;
    public event Action? ToggleMaximizeRequested;
    public event Action? CloseRequested;

    public ShellViewModel(INavigationService nav, IDialogService dialogs, INotYetWired notify, IThemeService theme)
    {
        _nav = nav; _dialogs = dialogs; _notify = notify; _theme = theme;
        _nav.Changed += () => CurrentPage = _nav.Current;
        _dialogs.Changed += () => ActiveDialog = _dialogs.Active;
        _notify.Reported += a => StatusMessage = $"Not wired yet: {a}";
    }

    [RelayCommand] private void ShowHelp() => _dialogs.ShowAsync<object>(new HelpViewModel(_dialogs));
    [RelayCommand] private void ZoomIn() => Zoom = Math.Min(300, Zoom + 10);
    [RelayCommand] private void ZoomOut() => Zoom = Math.Max(50, Zoom - 10);
    [RelayCommand] private void ZoomReset() => Zoom = 100;
    [RelayCommand] private void Minimize() => MinimizeRequested?.Invoke();
    [RelayCommand] private void ToggleMaximize() => ToggleMaximizeRequested?.Invoke();
    [RelayCommand] private void Close() => CloseRequested?.Invoke();
    [RelayCommand] private void OpenKitchen() => _notify.Report("New Kitchen");
    [RelayCommand] private void ToggleTheme() => _theme.Toggle();
}
```
(`HelpViewModel` is created in Task 7; add its `using`/registration then. To keep Task 5 compiling before Task 7, temporarily make `ShowHelp` call `_notify.Report("Help")` and switch it to the dialog in Task 7 — note this in the Task 7 steps.)

- [ ] **Step 4: Build the MainWindow shell chrome**

Replace `src/Nfty.Desktop/MainWindow.axaml` with the frameless shell: titlebar (brand + `.kroot` slot + breadcrumb slot + window buttons), a `ContentControl` bound to `CurrentPage`, an overlay `ContentControl` bound to `ActiveDialog` with a scrim, and a status bar (StatusMessage left; zoom + `?` right). Use token brushes only.
```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:Nfty.App.ViewModels"
        x:Class="Nfty.Desktop.MainWindow"
        x:DataType="vm:ShellViewModel"
        Width="1180" Height="720" Title="nfty"
        ExtendClientAreaToDecorationsHint="True" SystemDecorations="None">
  <Grid RowDefinitions="Auto,*,Auto">
    <!-- Titlebar -->
    <Grid Grid.Row="0" ColumnDefinitions="Auto,*,Auto" Background="{DynamicResource PanelBrush}" Height="38">
      <StackPanel Grid.Column="0" Orientation="Horizontal" Margin="10,0" VerticalAlignment="Center" Spacing="8">
        <Border Width="20" Height="20" CornerRadius="5" Background="{DynamicResource AccentWashBrush}" />
        <TextBlock Text="nfty" FontWeight="Bold" />
        <ContentControl Content="{Binding CurrentPage}" /> <!-- page contributes breadcrumb via its own view; simplified here -->
      </StackPanel>
      <StackPanel Grid.Column="2" Orientation="Horizontal" VerticalAlignment="Center">
        <Button Content="—" Command="{Binding MinimizeCommand}" Classes="tbtn" />
        <Button Content="▢" Command="{Binding ToggleMaximizeCommand}" Classes="tbtn" />
        <Button Content="✕" Command="{Binding CloseCommand}" Classes="tbtn" />
      </StackPanel>
    </Grid>

    <!-- Page + dialog overlay -->
    <Panel Grid.Row="1">
      <ContentControl Content="{Binding CurrentPage}" />
      <Panel IsVisible="{Binding ActiveDialog, Converter={x:Static ObjectConverters.IsNotNull}}"
             Background="#88000000">
        <ContentControl Content="{Binding ActiveDialog}"
                        HorizontalAlignment="Center" VerticalAlignment="Center" />
      </Panel>
    </Panel>

    <!-- Status bar -->
    <Grid Grid.Row="2" ColumnDefinitions="*,Auto" Background="{DynamicResource BgAltBrush}" Height="34">
      <TextBlock Grid.Column="0" Margin="16,0" VerticalAlignment="Center" Classes="muted"
                 Text="{Binding StatusMessage}" />
      <StackPanel Grid.Column="1" Orientation="Horizontal" Margin="8,0" VerticalAlignment="Center" Spacing="2">
        <Button Content="−" Command="{Binding ZoomOutCommand}" Classes="tbtn" />
        <TextBlock VerticalAlignment="Center" Width="46" TextAlignment="Center"
                   Text="{Binding Zoom, StringFormat='{}{0}%'}" />
        <Button Content="+" Command="{Binding ZoomInCommand}" Classes="tbtn" />
        <Button Content="?" Command="{Binding ShowHelpCommand}" Classes="tbtn" />
      </StackPanel>
    </Grid>
  </Grid>
</Window>
```

`src/Nfty.Desktop/MainWindow.axaml.cs` — wire window commands to the actual window:
```csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Nfty.App.ViewModels;

namespace Nfty.Desktop;

public partial class MainWindow : Window
{
    public MainWindow() => AvaloniaXamlLoader.Load(this);

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ShellViewModel shell)
        {
            shell.MinimizeRequested += () => WindowState = WindowState.Minimized;
            shell.ToggleMaximizeRequested += () =>
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            shell.CloseRequested += Close;
        }
    }
}
```

- [ ] **Step 5: Set the shell as MainWindow DataContext**

In `src/Nfty.Desktop/App.axaml.cs`, resolve the shell, set the initial page to a `LandingViewModel` (via `INavigationService.To`), and assign the DataContext:
```csharp
using Microsoft.Extensions.DependencyInjection;
using Nfty.App.Services;
using Nfty.App.ViewModels;
// ...
public override void OnFrameworkInitializationCompleted()
{
    var services = new ServiceCollection().AddNftyApp().BuildServiceProvider();
    var shell = services.GetRequiredService<ShellViewModel>();
    services.GetRequiredService<INavigationService>().To(services.GetRequiredService<LandingViewModel>());
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        desktop.MainWindow = new MainWindow { DataContext = shell };
    base.OnFrameworkInitializationCompleted();
}
```
(`LandingViewModel` lands in Task 6; until then set the initial page to a temporary `new HelpViewModel(...)` or leave `CurrentPage` null and add the Landing nav in Task 6. Note this dependency in Task 6.)

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/Nfty.App.Tests --filter "FullyQualifiedName~ShellViewModelTests" --nologo`
Expected: PASS (2). (Use the `_notify.Report("Help")` temporary in `ShowHelp` until Task 7.)

- [ ] **Step 7: Commit**
```bash
git add src/Nfty.App/ViewModels/ShellViewModel.cs src/Nfty.Desktop tests/Nfty.App.Tests/ShellViewModelTests.cs
git commit -m "$(printf 'feat(gui): ShellViewModel + frameless MainWindow chrome + dialog overlay\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

## Task 6: Landing screen (VM + View), fully wired (§6.1)

**Files:**
- Create: `src/Nfty.App/ViewModels/LandingViewModel.cs`, `src/Nfty.App/Views/LandingView.axaml`(+`.cs`)
- Modify: `src/Nfty.App/ServiceRegistration.cs` (uncomment `LandingViewModel`); `src/Nfty.Desktop/App.axaml.cs` (initial page → Landing)
- Test: `tests/Nfty.App.Tests/LandingViewModelTests.cs`

**Interfaces:**
- Consumes: `INavigationService`, `IDialogService`, `INotYetWired`, `IFilePickerService`, `IRecentsService`, and the wizard VMs (Tasks 8–10) + `ShellViewModel.ShowHelp`.
- Produces: `LandingViewModel` with commands `NewCookBook`, `NewKitchen`, `NewRecipe`, `NewIngredient`, `OpenCookBook`, `Import`, `OpenSet`, `OpenRecent(RecentItem)`, `ShowHelp`, and `Recents` (bound list). Enablement: `NewKitchen`/`OpenSet` disabled (reserved).

- [ ] **Step 1: Write the failing LandingViewModel tests**

`tests/Nfty.App.Tests/LandingViewModelTests.cs`:
```csharp
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

public class LandingViewModelTests
{
    private static LandingViewModel Make(out FakeNotYetWired notify, out FakeDialogs dialogs)
    {
        notify = new FakeNotYetWired();
        dialogs = new FakeDialogs();
        return new LandingViewModel(new FakeNav(), dialogs, notify,
            new FilePickerService(), new RecentsService());
    }

    [Fact]
    public void New_cookbook_opens_the_wizard_dialog()
    {
        var vm = Make(out _, out var dialogs);
        vm.NewCookBookCommand.Execute(null);
        Assert.IsType<NewCookBookViewModel>(dialogs.Active);
    }

    [Fact]
    public void Open_cookbook_reports_not_yet_wired()
    {
        var vm = Make(out var notify, out _);
        vm.OpenCookBookCommand.Execute(null);
        Assert.Equal("Open CookBook", notify.Last);
    }

    [Fact]
    public void Recents_are_exposed_for_the_list()
    {
        var vm = Make(out _, out _);
        Assert.NotEmpty(vm.Recents);
    }

    [Fact]
    public void New_kitchen_is_disabled_reserved()
    {
        var vm = Make(out _, out _);
        Assert.False(vm.NewKitchenCommand.CanExecute(null));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Nfty.App.Tests --filter "FullyQualifiedName~LandingViewModelTests" --nologo`
Expected: FAIL — `LandingViewModel` does not exist.

- [ ] **Step 3: Implement LandingViewModel**

`src/Nfty.App/ViewModels/LandingViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Models;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

public partial class LandingViewModel : ViewModelBase
{
    private readonly INavigationService _nav;
    private readonly IDialogService _dialogs;
    private readonly INotYetWired _notify;
    private readonly IFilePickerService _picker;
    private readonly IRecentsService _recents;

    public IReadOnlyList<RecentItem> Recents => _recents.Items;

    public LandingViewModel(INavigationService nav, IDialogService dialogs, INotYetWired notify,
        IFilePickerService picker, IRecentsService recents)
    { _nav = nav; _dialogs = dialogs; _notify = notify; _picker = picker; _recents = recents; }

    [RelayCommand] private void NewCookBook() => _dialogs.ShowAsync<object>(new NewCookBookViewModel(_dialogs, _notify));
    [RelayCommand(CanExecute = nameof(Never))] private void NewKitchen() => _notify.Report("New Kitchen");
    [RelayCommand] private void NewRecipe() => _dialogs.ShowAsync<object>(new NewRecipeViewModel(_dialogs, _notify));
    [RelayCommand] private void NewIngredient() => _dialogs.ShowAsync<object>(new NewIngredientViewModel(_dialogs, _notify));
    [RelayCommand] private void OpenCookBook() => _notify.Report("Open CookBook");
    [RelayCommand] private void Import() => _notify.Report("Import");
    [RelayCommand(CanExecute = nameof(Never))] private void OpenSet() => _notify.Report("Open .set");
    [RelayCommand] private void OpenRecent(RecentItem item) => _notify.Report($"Open recent: {item.Name}");
    [RelayCommand] private void ShowHelp() => _notify.Report("Help");   // routed to shell dialog in Task 7

    private bool Never() => false;
}
```

- [ ] **Step 4: Implement LandingView (wire every control)**

`src/Nfty.App/Views/LandingView.axaml` — port the Landing mockup layout (Create/Open groups + Recent), binding every button to its command. Functional wiring; refine visuals against `docs/design/mockups/landing.html`:
```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Nfty.App.ViewModels"
             xmlns:m="using:Nfty.App.Models"
             x:Class="Nfty.App.Views.LandingView"
             x:DataType="vm:LandingViewModel">
  <Grid ColumnDefinitions="*,*" Margin="38,34">
    <StackPanel Grid.Column="0" Spacing="7" MaxWidth="272">
      <TextBlock Text="nfty" FontSize="30" FontWeight="Bold" />
      <TextBlock Text="Asset Generator" Classes="muted" />
      <TextBlock Text="CREATE" Classes="muted" Margin="0,12,0,4" />
      <Button Classes="accent" Content="＋ New CookBook  ⌘N" Command="{Binding NewCookBookCommand}" />
      <Button Classes="tbtn" Content="＋ New Kitchen…" Command="{Binding NewKitchenCommand}" />
      <StackPanel Orientation="Horizontal" Spacing="7">
        <Button Classes="tbtn" Content="Recipe" Command="{Binding NewRecipeCommand}" />
        <Button Classes="tbtn" Content="Ingredient" Command="{Binding NewIngredientCommand}" />
      </StackPanel>
      <TextBlock Text="OPEN" Classes="muted" Margin="0,12,0,4" />
      <Button Classes="tbtn" Content="↗ Open CookBook…  ⌘O" Command="{Binding OpenCookBookCommand}" />
      <Button Classes="tbtn" Content="↧ Import…  ⌘I" Command="{Binding ImportCommand}" />
      <Button Classes="tbtn" Content="↗ Open a cooked .set…" Command="{Binding OpenSetCommand}" />
      <Button Classes="tbtn" Content="New to nfty? The cooking metaphor →" Command="{Binding ShowHelpCommand}" />
    </StackPanel>
    <StackPanel Grid.Column="1" Spacing="2">
      <TextBlock Text="RECENT" Classes="muted" />
      <ItemsControl ItemsSource="{Binding Recents}">
        <ItemsControl.ItemTemplate>
          <DataTemplate x:DataType="m:RecentItem">
            <Button Classes="tbtn" HorizontalAlignment="Stretch"
                    Command="{Binding $parent[ItemsControl].((vm:LandingViewModel)DataContext).OpenRecentCommand}"
                    CommandParameter="{Binding}">
              <StackPanel>
                <TextBlock Text="{Binding Name}" />
                <TextBlock Text="{Binding Meta}" Classes="muted" />
              </StackPanel>
            </Button>
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>
    </StackPanel>
  </Grid>
</UserControl>
```
`src/Nfty.App/Views/LandingView.axaml.cs`:
```csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Nfty.App.Views;

public partial class LandingView : UserControl
{
    public LandingView() => AvaloniaXamlLoader.Load(this);
}
```

- [ ] **Step 5: Uncomment the Landing registration + set it as the initial page**

In `ServiceRegistration`, ensure `services.AddTransient<LandingViewModel>();` is active. In `App.axaml.cs`, the initial `To(LandingViewModel)` (added in Task 5's step 5) is now valid.

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/Nfty.App.Tests --filter "FullyQualifiedName~LandingViewModelTests" --nologo`
Expected: PASS (4). (Requires the wizard VMs from Tasks 8–10 to compile — if executing strictly in order, temporarily have `NewCookBook`/`NewRecipe`/`NewIngredient` call `_notify.Report(...)` and switch them to the dialogs when Tasks 8–10 land. Note this in those tasks.)

- [ ] **Step 7: Commit**
```bash
git add src/Nfty.App/ViewModels/LandingViewModel.cs src/Nfty.App/Views/LandingView.axaml* src/Nfty.App/ServiceRegistration.cs tests/Nfty.App.Tests/LandingViewModelTests.cs
git commit -m "$(printf 'feat(gui): Landing screen fully wired (create/open/import/recent/help)\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

## Task 7: Help dialog (VM + View)

**Files:**
- Create: `src/Nfty.App/ViewModels/HelpViewModel.cs`, `src/Nfty.App/Views/HelpView.axaml`(+`.cs`)
- Modify: `ShellViewModel.ShowHelp` (open the dialog); `LandingViewModel.ShowHelp` (open the dialog)
- Test: `tests/Nfty.App.Tests/HelpViewModelTests.cs`

**Interfaces:**
- Consumes: `IDialogService` (to close).
- Produces: `HelpViewModel(IDialogService)` with a `CloseCommand`. The legend content is static display.

- [ ] **Step 1: Write the failing test**

`tests/Nfty.App.Tests/HelpViewModelTests.cs`:
```csharp
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

public class HelpViewModelTests
{
    [Fact]
    public void Close_clears_the_active_dialog()
    {
        var dialogs = new FakeDialogs();
        var help = new HelpViewModel(dialogs);
        dialogs.ShowAsync<object>(help);
        help.CloseCommand.Execute(null);
        Assert.Null(dialogs.Active);
    }
}
```

- [ ] **Step 2: Run to verify failure**
Run: `dotnet test tests/Nfty.App.Tests --filter "FullyQualifiedName~HelpViewModelTests" --nologo`
Expected: FAIL — `HelpViewModel` does not exist.

- [ ] **Step 3: Implement HelpViewModel + View**

`src/Nfty.App/ViewModels/HelpViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

public partial class HelpViewModel : ViewModelBase
{
    private readonly IDialogService _dialogs;
    public HelpViewModel(IDialogService dialogs) => _dialogs = dialogs;
    [RelayCommand] private void Close() => _dialogs.Close(null);
}
```

`src/Nfty.App/Views/HelpView.axaml` — the legend sheet; `Esc` bound to Close:
```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Nfty.App.ViewModels"
             x:Class="Nfty.App.Views.HelpView"
             x:DataType="vm:HelpViewModel">
  <UserControl.KeyBindings>
    <KeyBinding Gesture="Escape" Command="{Binding CloseCommand}" />
  </UserControl.KeyBindings>
  <Border Background="{DynamicResource PanelBrush}" CornerRadius="10" Padding="28" MaxWidth="820"
          BorderBrush="{DynamicResource LineStrongBrush}" BorderThickness="1">
    <StackPanel Spacing="8">
      <StackPanel Orientation="Horizontal" Spacing="10">
        <TextBlock Text="nfty" FontWeight="Bold" />
        <TextBlock Text="Quick reference" Classes="muted" />
        <Button Content="Esc" Command="{Binding CloseCommand}" Classes="tbtn" HorizontalAlignment="Right" />
      </StackPanel>
      <TextBlock TextWrapping="Wrap" Classes="muted"
        Text="CookBook .cbk · Recipe .rcp · Ingredient .igt · Variant · Set .set.  Kinds: Dynamic / Static / Custom.  Rules: ✕ never-together, → always-together, ⚑ layer-in-a-rule, ● valid.  Keys: ⌘N new, ⌘O open, ⌘I import, ⌘/ this sheet.  Colour: hex: rgb: hsl: hsv:.  Unique DNA = 4 × 3 × 5 × 6 = 360." />
    </StackPanel>
  </Border>
</UserControl>
```
`src/Nfty.App/Views/HelpView.axaml.cs`: standard `AvaloniaXamlLoader.Load(this)` code-behind (same shape as `LandingView.axaml.cs`).

- [ ] **Step 4: Route ShowHelp to the dialog**

In `ShellViewModel.ShowHelp`, replace the temporary `_notify.Report("Help")` with:
```csharp
[RelayCommand] private void ShowHelp() => _dialogs.ShowAsync<object>(new HelpViewModel(_dialogs));
```
In `LandingViewModel.ShowHelp`, replace with:
```csharp
[RelayCommand] private void ShowHelp() => _dialogs.ShowAsync<object>(new HelpViewModel(_dialogs));
```
Add `services.AddTransient<HelpViewModel>();` (already listed in Task 4's final registration).

- [ ] **Step 5: Run the tests**
Run: `dotnet test tests/Nfty.App.Tests --filter "FullyQualifiedName~HelpViewModelTests" --nologo`
Expected: PASS.

- [ ] **Step 6: Commit**
```bash
git add src/Nfty.App/ViewModels/HelpViewModel.cs src/Nfty.App/Views/HelpView.axaml* src/Nfty.App/ViewModels/ShellViewModel.cs src/Nfty.App/ViewModels/LandingViewModel.cs tests/Nfty.App.Tests/HelpViewModelTests.cs
git commit -m "$(printf 'feat(gui): Help legend dialog + Esc-to-close, routed from shell and landing\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

## Task 8: Wizard base + New CookBook wizard

**Files:**
- Create: `src/Nfty.App/ViewModels/WizardViewModelBase.cs`, `src/Nfty.App/ViewModels/NewCookBookViewModel.cs`, `src/Nfty.App/Views/NewCookBookView.axaml`(+`.cs`)
- Test: `tests/Nfty.App.Tests/NewCookBookViewModelTests.cs`

**Interfaces:**
- Produces: `WizardViewModelBase(IDialogService, INotYetWired)` with a `CancelCommand` (closes) and an abstract `Create()` that the concrete wizard overrides via a `[RelayCommand] CreateCommand`. `NewCookBookViewModel` with `Name`, `DerivedId` (computed), `Symbol`, `Width`, `Height`, `AspectLocked`, `Description`, and `CreateCommand` (stub → reports "Create CookBook").

- [ ] **Step 1: Write the failing tests**

`tests/Nfty.App.Tests/NewCookBookViewModelTests.cs`:
```csharp
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

public class NewCookBookViewModelTests
{
    private static NewCookBookViewModel Make(out FakeDialogs dialogs, out FakeNotYetWired notify)
    { dialogs = new FakeDialogs(); notify = new FakeNotYetWired(); return new NewCookBookViewModel(dialogs, notify); }

    [Fact]
    public void Derived_id_lowercases_and_hyphenates_the_name()
    {
        var vm = Make(out _, out _);
        vm.Name = "Vapor Pets";
        Assert.Equal("vapor-pets", vm.DerivedId);
    }

    [Fact]
    public void Aspect_lock_scales_height_when_width_changes()
    {
        var vm = Make(out _, out _);
        vm.Width = 1000; vm.Height = 1000; vm.AspectLocked = true;
        vm.Width = 500;
        Assert.Equal(500, vm.Height);
    }

    [Fact]
    public void Create_reports_not_yet_wired_and_closes()
    {
        var vm = Make(out var dialogs, out var notify);
        dialogs.ShowAsync<object>(vm);
        vm.CreateCommand.Execute(null);
        Assert.Equal("Create CookBook", notify.Last);
        Assert.Null(dialogs.Active);
    }

    [Fact]
    public void Cancel_closes_without_reporting()
    {
        var vm = Make(out var dialogs, out var notify);
        dialogs.ShowAsync<object>(vm);
        vm.CancelCommand.Execute(null);
        Assert.Null(dialogs.Active);
        Assert.Null(notify.Last);
    }
}
```

- [ ] **Step 2: Run to verify failure**
Run: `dotnet test tests/Nfty.App.Tests --filter "FullyQualifiedName~NewCookBookViewModelTests" --nologo`
Expected: FAIL — types don't exist.

- [ ] **Step 3: Implement the wizard base + New CookBook VM**

`src/Nfty.App/ViewModels/WizardViewModelBase.cs`:
```csharp
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

public abstract partial class WizardViewModelBase : ViewModelBase
{
    protected readonly IDialogService Dialogs;
    protected readonly INotYetWired Notify;
    protected WizardViewModelBase(IDialogService dialogs, INotYetWired notify) { Dialogs = dialogs; Notify = notify; }
    [RelayCommand] protected void Cancel() => Dialogs.Close(null);
}
```

`src/Nfty.App/ViewModels/NewCookBookViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

public partial class NewCookBookViewModel : WizardViewModelBase
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _symbol = "";
    [ObservableProperty] private int _width = 1000;
    [ObservableProperty] private int _height = 1000;
    [ObservableProperty] private bool _aspectLocked = true;
    [ObservableProperty] private string _description = "";

    private double _ratio = 1.0;

    public string DerivedId => string.Join('-',
        Name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    public NewCookBookViewModel(IDialogService dialogs, INotYetWired notify) : base(dialogs, notify) { }

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(DerivedId));

    partial void OnWidthChanged(int value)
    {
        if (AspectLocked && _height > 0 && !_syncing) { _syncing = true; Height = (int)Math.Round(value / _ratio); _syncing = false; }
        else if (!AspectLocked && _height > 0) _ratio = (double)value / _height;
    }
    partial void OnHeightChanged(int value) { if (value > 0 && !_syncing) _ratio = (double)_width / value; }
    private bool _syncing;

    [RelayCommand] private void Create() { Notify.Report("Create CookBook"); Dialogs.Close(null); }
}
```
Note: the aspect-lock ratio is captured when both dimensions are set; the test sets 1000×1000 (ratio 1), locks, then Width=500 → Height 500. If a subtle ordering bug appears, capture `_ratio` on `AspectLocked` turning true instead — adjust and keep the test green.

- [ ] **Step 4: Implement NewCookBookView (wire every field + Cancel/Create)**

`src/Nfty.App/Views/NewCookBookView.axaml` (centered pane; bind every field):
```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Nfty.App.ViewModels"
             x:Class="Nfty.App.Views.NewCookBookView"
             x:DataType="vm:NewCookBookViewModel">
  <Border Background="{DynamicResource PanelBrush}" CornerRadius="10" Padding="28" Width="440"
          BorderBrush="{DynamicResource LineStrongBrush}" BorderThickness="1">
    <StackPanel Spacing="10">
      <TextBlock Text="New CookBook" FontWeight="Bold" />
      <TextBox Watermark="Name" Text="{Binding Name}" />
      <TextBlock Text="{Binding DerivedId}" Classes="muted" />
      <TextBox Watermark="Symbol (optional)" Text="{Binding Symbol}" MaxLength="255" />
      <StackPanel Orientation="Horizontal" Spacing="8">
        <NumericUpDown Value="{Binding Width}" Minimum="1" Width="120" />
        <ToggleButton IsChecked="{Binding AspectLocked}" Content="🔗" />
        <NumericUpDown Value="{Binding Height}" Minimum="1" Width="120" />
      </StackPanel>
      <TextBox Watermark="Description" Text="{Binding Description}" AcceptsReturn="True" Height="70" />
      <StackPanel Orientation="Horizontal" Spacing="8" HorizontalAlignment="Right">
        <Button Content="Cancel" Command="{Binding CancelCommand}" Classes="tbtn" />
        <Button Content="Create" Command="{Binding CreateCommand}" Classes="accent" />
      </StackPanel>
    </StackPanel>
  </Border>
</UserControl>
```
Code-behind: standard loader (same shape as `LandingView.axaml.cs`).

- [ ] **Step 5: Run the tests**
Run: `dotnet test tests/Nfty.App.Tests --filter "FullyQualifiedName~NewCookBookViewModelTests" --nologo`
Expected: PASS (4).

- [ ] **Step 6: Commit**
```bash
git add src/Nfty.App/ViewModels/WizardViewModelBase.cs src/Nfty.App/ViewModels/NewCookBookViewModel.cs src/Nfty.App/Views/NewCookBookView.axaml* tests/Nfty.App.Tests/NewCookBookViewModelTests.cs
git commit -m "$(printf 'feat(gui): New CookBook wizard (fields, aspect-lock, derived id, stub Create)\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

## Task 9: New Recipe wizard

**Files:** Create `src/Nfty.App/ViewModels/NewRecipeViewModel.cs`, `src/Nfty.App/Views/NewRecipeView.axaml`(+`.cs`); Test `tests/Nfty.App.Tests/NewRecipeViewModelTests.cs`.

**Interfaces:** `NewRecipeViewModel(IDialogService, INotYetWired)` with `Name`, `Weight`, `Destination` (`enum RecipeDestination { IntoCookBook, LooseKitchen }`), `WeightEnabled` (false when `LooseKitchen`), `CreateCommand` (stub → "Create Recipe"), inherited `CancelCommand`.

- [ ] **Step 1: Write the failing tests**
```csharp
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

public class NewRecipeViewModelTests
{
    private static NewRecipeViewModel Make(out FakeDialogs d, out FakeNotYetWired n)
    { d = new FakeDialogs(); n = new FakeNotYetWired(); return new NewRecipeViewModel(d, n); }

    [Fact]
    public void Choosing_loose_kitchen_disables_the_weight_field()
    {
        var vm = Make(out _, out _);
        vm.Destination = RecipeDestination.LooseKitchen;
        Assert.False(vm.WeightEnabled);
        vm.Destination = RecipeDestination.IntoCookBook;
        Assert.True(vm.WeightEnabled);
    }

    [Fact]
    public void Create_reports_not_yet_wired()
    {
        var vm = Make(out var d, out var n);
        d.ShowAsync<object>(vm);
        vm.CreateCommand.Execute(null);
        Assert.Equal("Create Recipe", n.Last);
        Assert.Null(d.Active);
    }
}
```
Save as `tests/Nfty.App.Tests/NewRecipeViewModelTests.cs`.

- [ ] **Step 2: Run to verify failure** — `dotnet test tests/Nfty.App.Tests --filter "FullyQualifiedName~NewRecipeViewModelTests" --nologo` → FAIL.

- [ ] **Step 3: Implement**

`src/Nfty.App/ViewModels/NewRecipeViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

public enum RecipeDestination { IntoCookBook, LooseKitchen }

public partial class NewRecipeViewModel : WizardViewModelBase
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private double _weight = 100;
    [ObservableProperty] private RecipeDestination _destination = RecipeDestination.IntoCookBook;

    public bool WeightEnabled => Destination == RecipeDestination.IntoCookBook;

    public NewRecipeViewModel(IDialogService dialogs, INotYetWired notify) : base(dialogs, notify) { }

    partial void OnDestinationChanged(RecipeDestination value) => OnPropertyChanged(nameof(WeightEnabled));

    [RelayCommand] private void Create() { Notify.Report("Create Recipe"); Dialogs.Close(null); }
}
```

`src/Nfty.App/Views/NewRecipeView.axaml` — bind Name, Weight (a live "Resulting mix" bar can be a simple `ProgressBar` bound to `Weight`), a `Destination` radio group (two `RadioButton`s), and Cancel/Create. Weight field `IsEnabled="{Binding WeightEnabled}"`. Code-behind: standard loader.

- [ ] **Step 4: Run the tests** — expected PASS (2).

- [ ] **Step 5: Commit**
```bash
git add src/Nfty.App/ViewModels/NewRecipeViewModel.cs src/Nfty.App/Views/NewRecipeView.axaml* tests/Nfty.App.Tests/NewRecipeViewModelTests.cs
git commit -m "$(printf 'feat(gui): New Recipe wizard (weight, save-to toggles weight, stub Create)\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

## Task 10: New Ingredient wizard

**Files:** Create `src/Nfty.App/ViewModels/NewIngredientViewModel.cs`, `src/Nfty.App/Views/NewIngredientView.axaml`(+`.cs`); Test `tests/Nfty.App.Tests/NewIngredientViewModelTests.cs`.

**Interfaces:** `NewIngredientViewModel(IDialogService, INotYetWired)` with `Name`, `Kind` (`Nfty.Core.Model.LayerKind`), `ShowColourRange`/`ShowFixedColour`/`ShowCanvas` computed from `Kind`+`Destination`, `Destination` (reuse `RecipeDestination`), colour-range fields (`HueMin/Max`, `SatMin/Max`), `FixedColor`, `CreateCommand` (stub → "Create Ingredient").

- [ ] **Step 1: Write the failing tests**
```csharp
using Nfty.App.ViewModels;
using Nfty.Core.Model;
using Xunit;

namespace Nfty.App.Tests;

public class NewIngredientViewModelTests
{
    private static NewIngredientViewModel Make(out FakeDialogs d, out FakeNotYetWired n)
    { d = new FakeDialogs(); n = new FakeNotYetWired(); return new NewIngredientViewModel(d, n); }

    [Fact]
    public void Kind_selects_the_matching_colour_zone()
    {
        var vm = Make(out _, out _);
        vm.Kind = LayerKind.Dynamic; Assert.True(vm.ShowColourRange); Assert.False(vm.ShowFixedColour);
        vm.Kind = LayerKind.Static;  Assert.True(vm.ShowFixedColour); Assert.False(vm.ShowColourRange);
        vm.Kind = LayerKind.Custom;  Assert.False(vm.ShowColourRange); Assert.False(vm.ShowFixedColour);
    }

    [Fact]
    public void Canvas_field_shows_only_when_loose()
    {
        var vm = Make(out _, out _);
        vm.Destination = RecipeDestination.LooseKitchen; Assert.True(vm.ShowCanvas);
        vm.Destination = RecipeDestination.IntoCookBook; Assert.False(vm.ShowCanvas);
    }

    [Fact]
    public void Create_reports_not_yet_wired()
    {
        var vm = Make(out var d, out var n);
        d.ShowAsync<object>(vm); vm.CreateCommand.Execute(null);
        Assert.Equal("Create Ingredient", n.Last);
    }
}
```
Save as `tests/Nfty.App.Tests/NewIngredientViewModelTests.cs`.

- [ ] **Step 2: Run to verify failure** → FAIL.

- [ ] **Step 3: Implement**

`src/Nfty.App/ViewModels/NewIngredientViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.Core.Model;

namespace Nfty.App.ViewModels;

public partial class NewIngredientViewModel : WizardViewModelBase
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private LayerKind _kind = LayerKind.Dynamic;
    [ObservableProperty] private RecipeDestination _destination = RecipeDestination.IntoCookBook;
    [ObservableProperty] private double _hueMin, _hueMax = 360, _satMin = 40, _satMax = 100;
    [ObservableProperty] private string _fixedColor = "hex:d6249f";

    public bool ShowColourRange => Kind == LayerKind.Dynamic;
    public bool ShowFixedColour => Kind == LayerKind.Static;
    public bool ShowCanvas => Destination == RecipeDestination.LooseKitchen;

    public NewIngredientViewModel(IDialogService dialogs, INotYetWired notify) : base(dialogs, notify) { }

    partial void OnKindChanged(LayerKind value)
    { OnPropertyChanged(nameof(ShowColourRange)); OnPropertyChanged(nameof(ShowFixedColour)); }
    partial void OnDestinationChanged(RecipeDestination value) => OnPropertyChanged(nameof(ShowCanvas));

    [RelayCommand] private void Create() { Notify.Report("Create Ingredient"); Dialogs.Close(null); }
}
```

`src/Nfty.App/Views/NewIngredientView.axaml` — Name, a 3-`RadioButton` Kind group, the kind-dependent zone (Dynamic: two range sliders bound to Hue/Sat min/max; Static: a `TextBox` for `FixedColor`; Custom: nothing), a `Destination` radio group, a Canvas field visible via `ShowCanvas`, and Cancel/Create. Zones toggle via `IsVisible="{Binding ShowColourRange}"` etc. Code-behind: standard loader.

- [ ] **Step 4: Run the tests** → PASS (3).

- [ ] **Step 5: Commit**
```bash
git add src/Nfty.App/ViewModels/NewIngredientViewModel.cs src/Nfty.App/Views/NewIngredientView.axaml* tests/Nfty.App.Tests/NewIngredientViewModelTests.cs
git commit -m "$(printf 'feat(gui): New Ingredient wizard (kind zones, save-to canvas, stub Create)\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

## Task 11: Explorer shell VM (chrome, tree, lock, toolbar) + sample model

**Files:** Create `src/Nfty.App/Models/ExplorerNode.cs`, `src/Nfty.App/ViewModels/ExplorerViewModel.cs`; Test `tests/Nfty.App.Tests/ExplorerViewModelTests.cs`.

**Interfaces:**
- Produces: `record ExplorerNode(string Id, string Name, ExplorerNodeKind Kind, IReadOnlyList<ExplorerNode> Children)`; `enum ExplorerNodeKind { CookBook, Recipe, Ingredient }`. `ExplorerViewModel` with `Root`, `SelectedNode`, `IsEditing`, `AddLabel` (context-aware), and commands `ToggleLock`, `Search`(stub), `Add`(nav→stub), `DeleteSelected`(stub, enabled iff editing), `Import`(stub), `SelectNode(ExplorerNode)`, `OpenIngredient(string)`. Detail sub-VM selection exposed as `CurrentDetail` (set from `SelectedNode`; detail VMs land in Task 12).

- [ ] **Step 1: Write the failing tests**
```csharp
using Nfty.App.Models;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

public class ExplorerViewModelTests
{
    private static ExplorerViewModel Make(out FakeNotYetWired n)
    { n = new FakeNotYetWired(); return new ExplorerViewModel(new FakeNav(), new FakeDialogs(), n); }

    [Fact]
    public void Opens_read_only_and_lock_toggles_editing()
    {
        var vm = Make(out _);
        Assert.False(vm.IsEditing);
        vm.ToggleLockCommand.Execute(null);
        Assert.True(vm.IsEditing);
    }

    [Fact]
    public void Delete_is_disabled_until_editing()
    {
        var vm = Make(out _);
        Assert.False(vm.DeleteSelectedCommand.CanExecute(null));
        vm.ToggleLockCommand.Execute(null);
        Assert.True(vm.DeleteSelectedCommand.CanExecute(null));
    }

    [Fact]
    public void Add_label_tracks_the_selected_node_kind()
    {
        var vm = Make(out _);
        vm.SelectNodeCommand.Execute(new ExplorerNode("r", "Cat", ExplorerNodeKind.Recipe, []));
        Assert.Equal("Add ingredient", vm.AddLabel);
        vm.SelectNodeCommand.Execute(new ExplorerNode("i", "Aura", ExplorerNodeKind.Ingredient, []));
        Assert.Equal("Add variant", vm.AddLabel);
    }

    [Fact]
    public void Search_and_import_report_not_yet_wired()
    {
        var vm = Make(out var n);
        vm.SearchCommand.Execute(null); Assert.Equal("Search (⌘K)", n.Last);
        vm.ImportCommand.Execute(null); Assert.Equal("Import", n.Last);
    }
}
```
Save as `tests/Nfty.App.Tests/ExplorerViewModelTests.cs`.

- [ ] **Step 2: Run to verify failure** → FAIL.

- [ ] **Step 3: Implement the model + Explorer VM**

`src/Nfty.App/Models/ExplorerNode.cs`:
```csharp
namespace Nfty.App.Models;

public enum ExplorerNodeKind { CookBook, Recipe, Ingredient }

public record ExplorerNode(string Id, string Name, ExplorerNodeKind Kind, IReadOnlyList<ExplorerNode> Children);
```

`src/Nfty.App/ViewModels/ExplorerViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Models;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

public partial class ExplorerViewModel : ViewModelBase
{
    private readonly INavigationService _nav;
    private readonly IDialogService _dialogs;
    private readonly INotYetWired _notify;

    [ObservableProperty] private ExplorerNode? _selectedNode;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    private bool _isEditing;

    public ExplorerNode Root { get; } = Sample();

    public string AddLabel => SelectedNode?.Kind switch
    {
        ExplorerNodeKind.CookBook => "Add recipe",
        ExplorerNodeKind.Recipe => "Add ingredient",
        ExplorerNodeKind.Ingredient => "Add variant",
        _ => "Add",
    };

    public ExplorerViewModel(INavigationService nav, IDialogService dialogs, INotYetWired notify)
    { _nav = nav; _dialogs = dialogs; _notify = notify; }

    partial void OnSelectedNodeChanged(ExplorerNode? value) => OnPropertyChanged(nameof(AddLabel));

    [RelayCommand] private void ToggleLock() => IsEditing = !IsEditing;
    [RelayCommand] private void Search() => _notify.Report("Search (⌘K)");
    [RelayCommand] private void Add() => _notify.Report($"{AddLabel}");
    [RelayCommand(CanExecute = nameof(CanEdit))] private void DeleteSelected() => _notify.Report("Delete");
    [RelayCommand] private void Import() => _notify.Report("Import");
    [RelayCommand] private void SelectNode(ExplorerNode node) => SelectedNode = node;
    [RelayCommand] private void OpenIngredient(string id) => _notify.Report($"Open ingredient {id}");

    private bool CanEdit() => IsEditing;

    private static ExplorerNode Sample() =>
        new("cb", "VaporPets", ExplorerNodeKind.CookBook,
        [
            new("cat", "Cat", ExplorerNodeKind.Recipe,
            [
                new("bg", "Background", ExplorerNodeKind.Ingredient, []),
                new("aura", "Aura", ExplorerNodeKind.Ingredient, []),
            ]),
        ]);
}
```
(The `Add` command here reports; when the wizards/editor become the real destination in Phase 2 it navigates. For Phase 1 the nav destination for Add is deferred to keep it a single visible stub; this matches the spec's `nav→stub` note.)

- [ ] **Step 4: Run the tests** → PASS (4).

- [ ] **Step 5: Commit**
```bash
git add src/Nfty.App/Models/ExplorerNode.cs src/Nfty.App/ViewModels/ExplorerViewModel.cs tests/Nfty.App.Tests/ExplorerViewModelTests.cs
git commit -m "$(printf 'feat(gui): Explorer VM — tree, lock/edit, context-aware toolbar (stubs)\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

## Task 12: Explorer detail VMs (cookbook / recipe / ingredient)

**Files:** Create `src/Nfty.App/ViewModels/CookBookDetailViewModel.cs`, `RecipeDetailViewModel.cs`, `IngredientDetailViewModel.cs`, and small display records; Modify `ExplorerViewModel` to expose `CurrentDetail` off `SelectedNode`; Test `tests/Nfty.App.Tests/ExplorerDetailTests.cs`.

**Interfaces:**
- `CookBookDetailViewModel` — display props + `CookCommand` (stub → "Cook").
- `RecipeDetailViewModel` — `RerollCommand` (ui-state; bumps a `RollSeed`), `OpenIngredientCommand(string)` (nav via a callback to the Explorer).
- `IngredientDetailViewModel` — `SortColumn`, `SortByCommand(string)` (ui-state), `SelectVariantCommand`, `DeleteVariantCommand` (stub, enabled iff editing), `JumpToRulesCommand` (nav), `EditIngredientCommand` (nav → editor).
- `ExplorerViewModel.CurrentDetail : ViewModelBase?` set from `SelectedNode.Kind`.

- [ ] **Step 1: Write the failing tests**
```csharp
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

public class ExplorerDetailTests
{
    [Fact]
    public void Cook_reports_not_yet_wired()
    {
        var n = new FakeNotYetWired();
        var vm = new CookBookDetailViewModel(n);
        vm.CookCommand.Execute(null);
        Assert.Equal("Cook", n.Last);
    }

    [Fact]
    public void Reroll_changes_the_roll_seed()
    {
        var vm = new RecipeDetailViewModel(new FakeNotYetWired(), _ => { });
        var before = vm.RollSeed;
        vm.RerollCommand.Execute(null);
        Assert.NotEqual(before, vm.RollSeed);
    }

    [Fact]
    public void Sort_sets_the_active_column()
    {
        var vm = new IngredientDetailViewModel(new FakeNotYetWired(), () => { }, () => false);
        vm.SortByCommand.Execute("Weight");
        Assert.Equal("Weight", vm.SortColumn);
    }

    [Fact]
    public void Delete_variant_enabled_only_when_editing()
    {
        bool editing = false;
        var vm = new IngredientDetailViewModel(new FakeNotYetWired(), () => { }, () => editing);
        Assert.False(vm.DeleteVariantCommand.CanExecute(null));
        editing = true;
        vm.RaiseCanExecuteChanged();
        Assert.True(vm.DeleteVariantCommand.CanExecute(null));
    }
}
```
Save as `tests/Nfty.App.Tests/ExplorerDetailTests.cs`.

- [ ] **Step 2: Run to verify failure** → FAIL.

- [ ] **Step 3: Implement the three detail VMs**

`src/Nfty.App/ViewModels/CookBookDetailViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

public partial class CookBookDetailViewModel : ViewModelBase
{
    private readonly INotYetWired _notify;
    public CookBookDetailViewModel(INotYetWired notify) => _notify = notify;
    [RelayCommand] private void Cook() => _notify.Report("Cook");
}
```

`src/Nfty.App/ViewModels/RecipeDetailViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

public partial class RecipeDetailViewModel : ViewModelBase
{
    private readonly INotYetWired _notify;
    private readonly Action<string> _openIngredient;
    [ObservableProperty] private int _rollSeed = 1;

    public RecipeDetailViewModel(INotYetWired notify, Action<string> openIngredient)
    { _notify = notify; _openIngredient = openIngredient; }

    [RelayCommand] private void Reroll() => RollSeed++;   // ui-state; P2 samples a real colour
    [RelayCommand] private void OpenIngredient(string id) => _openIngredient(id);
}
```

`src/Nfty.App/ViewModels/IngredientDetailViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

public partial class IngredientDetailViewModel : ViewModelBase
{
    private readonly INotYetWired _notify;
    private readonly Action _editIngredient;
    private readonly Func<bool> _isEditing;
    [ObservableProperty] private string _sortColumn = "Variant";

    public IngredientDetailViewModel(INotYetWired notify, Action editIngredient, Func<bool> isEditing)
    { _notify = notify; _editIngredient = editIngredient; _isEditing = isEditing; }

    public void RaiseCanExecuteChanged() => DeleteVariantCommand.NotifyCanExecuteChanged();

    [RelayCommand] private void SortBy(string col) => SortColumn = col;
    [RelayCommand] private void SelectVariant(string id) { /* ui-state: active variant */ }
    [RelayCommand(CanExecute = nameof(CanEdit))] private void DeleteVariant() => _notify.Report("Delete variant");
    [RelayCommand] private void JumpToRules() { /* nav within the recipe rail */ }
    [RelayCommand] private void EditIngredient() => _editIngredient();

    private bool CanEdit() => _isEditing();
}
```

Modify `ExplorerViewModel` — add `CurrentDetail` and set it in `OnSelectedNodeChanged`:
```csharp
[ObservableProperty] private ViewModelBase? _currentDetail;
// inside OnSelectedNodeChanged(value):
CurrentDetail = value?.Kind switch
{
    ExplorerNodeKind.CookBook => new CookBookDetailViewModel(_notify),
    ExplorerNodeKind.Recipe => new RecipeDetailViewModel(_notify, id => OpenIngredientCommand.Execute(id)),
    ExplorerNodeKind.Ingredient => new IngredientDetailViewModel(_notify,
        () => _nav.To(/* IngredientEditorViewModel — Task 13 */ new IngredientEditorViewModel(_nav, _notify)),
        () => IsEditing),
    _ => null,
};
```
(The `IngredientEditorViewModel` reference requires Task 13; if executing in order, temporarily pass a `HelpViewModel` or report a stub, then switch to the editor in Task 13. Note this in Task 13.)

- [ ] **Step 4: Run the tests** → PASS (4).

- [ ] **Step 5: Commit**
```bash
git add src/Nfty.App/ViewModels/CookBookDetailViewModel.cs src/Nfty.App/ViewModels/RecipeDetailViewModel.cs src/Nfty.App/ViewModels/IngredientDetailViewModel.cs src/Nfty.App/ViewModels/ExplorerViewModel.cs tests/Nfty.App.Tests/ExplorerDetailTests.cs
git commit -m "$(printf 'feat(gui): Explorer detail VMs — cookbook/recipe/ingredient wiring\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

## Task 13: Explorer View + Ingredient Editor (VM + View)

**Files:** Create `src/Nfty.App/Views/ExplorerView.axaml`(+`.cs`); `src/Nfty.App/ViewModels/IngredientEditorViewModel.cs`, `src/Nfty.App/Views/IngredientEditorView.axaml`(+`.cs`); Test `tests/Nfty.App.Tests/IngredientEditorViewModelTests.cs`.

**Interfaces:**
- `IngredientEditorViewModel(INavigationService, INotYetWired)` with `ActiveTool` (`enum EditorTool { Brush, Eraser, Rectangle, Circle, Triangle, Select, Fill }`), `BrushValue` (0–255), `Mode` (`LayerKind`), colour-range props, commands `SelectTool`, `Undo`/`Redo`, `AddVariant`/`DuplicateVariant`/`DeleteVariant`, `ApplyStroke` (stub → "Paint"), `RerollPreview` (stub → "Preview roll"), `EnlargePreview`/`FillPanePreview` (ui-state), `Save` (stub → "Save ingredient"), `Back` (nav).

- [ ] **Step 1: Write the failing IngredientEditor tests**
```csharp
using Nfty.App.ViewModels;
using Nfty.Core.Model;
using Xunit;

namespace Nfty.App.Tests;

public class IngredientEditorViewModelTests
{
    private static IngredientEditorViewModel Make(out FakeNotYetWired n, out FakeNav nav)
    { n = new FakeNotYetWired(); nav = new FakeNav(); return new IngredientEditorViewModel(nav, n); }

    [Fact]
    public void Select_tool_sets_the_active_tool()
    {
        var vm = Make(out _, out _);
        vm.SelectToolCommand.Execute(EditorTool.Fill);
        Assert.Equal(EditorTool.Fill, vm.ActiveTool);
    }

    [Fact]
    public void Mode_toggle_changes_the_layer_kind()
    {
        var vm = Make(out _, out _);
        vm.Mode = LayerKind.Static;
        Assert.Equal(LayerKind.Static, vm.Mode);
    }

    [Fact]
    public void Paint_and_save_report_not_yet_wired()
    {
        var vm = Make(out var n, out _);
        vm.ApplyStrokeCommand.Execute(null); Assert.Equal("Paint", n.Last);
        vm.SaveCommand.Execute(null); Assert.Equal("Save ingredient", n.Last);
    }
}
```
Save as `tests/Nfty.App.Tests/IngredientEditorViewModelTests.cs`.

- [ ] **Step 2: Run to verify failure** → FAIL.

- [ ] **Step 3: Implement IngredientEditorViewModel**

`src/Nfty.App/ViewModels/IngredientEditorViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.Core.Model;

namespace Nfty.App.ViewModels;

public enum EditorTool { Brush, Eraser, Rectangle, Circle, Triangle, Select, Fill }

public partial class IngredientEditorViewModel : ViewModelBase
{
    private readonly INavigationService _nav;
    private readonly INotYetWired _notify;

    [ObservableProperty] private EditorTool _activeTool = EditorTool.Brush;
    [ObservableProperty] private int _brushValue = 128;
    [ObservableProperty] private LayerKind _mode = LayerKind.Dynamic;
    [ObservableProperty] private double _hueMin, _hueMax = 360, _satMin = 40, _satMax = 100;
    [ObservableProperty] private int _hueQuantize = 12, _satQuantize = 4;

    public IngredientEditorViewModel(INavigationService nav, INotYetWired notify) { _nav = nav; _notify = notify; }

    [RelayCommand] private void SelectTool(EditorTool tool) => ActiveTool = tool;
    [RelayCommand] private void Undo() { /* EditHistory in P2 */ }
    [RelayCommand] private void Redo() { /* EditHistory in P2 */ }
    [RelayCommand] private void AddVariant() { /* in-memory drafts in P2 */ }
    [RelayCommand] private void DuplicateVariant() { /* P2 */ }
    [RelayCommand] private void DeleteVariant() { /* P2 */ }
    [RelayCommand] private void ApplyStroke() => _notify.Report("Paint");
    [RelayCommand] private void RerollPreview() => _notify.Report("Preview roll");
    [RelayCommand] private void EnlargePreview() { /* ui-state P2 */ }
    [RelayCommand] private void FillPanePreview() { /* ui-state P2 */ }
    [RelayCommand] private void Save() => _notify.Report("Save ingredient");
    [RelayCommand] private void Back() => _nav.Back();
}
```

- [ ] **Step 4: Implement the Explorer + Editor Views**

`ExplorerView.axaml` — a `Grid` with the left `TreeView` (bound to `Root`, selection → `SelectNodeCommand`), a toolbar row (Search/Add/Delete/Import/lock buttons bound to the commands; `Add` content `{Binding AddLabel}`; Delete `IsEnabled` via the command's CanExecute), and a `ContentControl Content="{Binding CurrentDetail}"` for the detail (the ViewLocator resolves each detail VM to its view — add `CookBookDetailView`/`RecipeDetailView`/`IngredientDetailView` as simple views binding their commands, or a single inline `DataTemplate` set per kind). Bind lock button to `ToggleLockCommand`.
`IngredientEditorView.axaml` — the filmstrip (Add/Duplicate/Delete variant), the tool strip (each tool button → `SelectToolCommand` with its `EditorTool`; undo/redo; value ramp bound to `BrushValue`), the canvas (`Border` placeholder with a pointer handler calling `ApplyStrokeCommand`), the Colorize rail (Static|Dynamic toggle bound to `Mode`; range sliders; quantize `NumericUpDown`s; fixed-colour box), the preview blip (reroll/enlarge/fill-pane buttons), and Save/Back. Code-behind for both: standard loader.

Fix the Task-12 temporary: in `ExplorerViewModel.OnSelectedNodeChanged`, the Ingredient detail's edit callback now navigates to a real `IngredientEditorViewModel`:
```csharp
() => _nav.To(new IngredientEditorViewModel(_nav, _notify)),
```

- [ ] **Step 5: Run the tests** → PASS (3). Then the whole suite: `dotnet test tests/Nfty.App.Tests --nologo` → all PASS.

- [ ] **Step 6: Commit**
```bash
git add src/Nfty.App/Views/ExplorerView.axaml* src/Nfty.App/ViewModels/IngredientEditorViewModel.cs src/Nfty.App/Views/IngredientEditorView.axaml* src/Nfty.App/ViewModels/ExplorerViewModel.cs tests/Nfty.App.Tests/IngredientEditorViewModelTests.cs
git commit -m "$(printf 'feat(gui): Explorer view + Ingredient Editor (tools, colorize, preview) wired\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

## Task 14: Wiring-coverage gate + smoke path + final verification

**Files:** Create `tests/Nfty.App.Tests/WiringCoverageTests.cs`, `tests/Nfty.App.Tests/SmokeTests.cs`.

**Interfaces:** consumes every VM + the `ViewLocator`.

- [ ] **Step 1: Write the wiring-coverage test**

Asserts each screen VM exposes the commands the §6 Wiring Map names — a guard against a dropped control.
```csharp
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

public class WiringCoverageTests
{
    private static bool HasCommand(object vm, string name) =>
        vm.GetType().GetProperty(name)?.GetValue(vm) is System.Windows.Input.ICommand;

    [Fact]
    public void Landing_exposes_every_mapped_command()
    {
        var vm = new LandingViewModel(new FakeNav(), new FakeDialogs(), new FakeNotYetWired(),
            new FilePickerService(), new RecentsService());
        foreach (var c in new[] { "NewCookBookCommand","NewKitchenCommand","NewRecipeCommand","NewIngredientCommand",
                                  "OpenCookBookCommand","ImportCommand","OpenSetCommand","OpenRecentCommand","ShowHelpCommand" })
            Assert.True(HasCommand(vm, c), $"Landing missing {c}");
    }

    [Fact]
    public void Explorer_exposes_every_mapped_command()
    {
        var vm = new ExplorerViewModel(new FakeNav(), new FakeDialogs(), new FakeNotYetWired());
        foreach (var c in new[] { "ToggleLockCommand","SearchCommand","AddCommand","DeleteSelectedCommand",
                                  "ImportCommand","SelectNodeCommand","OpenIngredientCommand" })
            Assert.True(HasCommand(vm, c), $"Explorer missing {c}");
    }

    [Fact]
    public void Editor_exposes_every_mapped_command()
    {
        var vm = new IngredientEditorViewModel(new FakeNav(), new FakeNotYetWired());
        foreach (var c in new[] { "SelectToolCommand","UndoCommand","RedoCommand","AddVariantCommand",
                                  "DuplicateVariantCommand","DeleteVariantCommand","ApplyStrokeCommand",
                                  "RerollPreviewCommand","EnlargePreviewCommand","FillPanePreviewCommand",
                                  "SaveCommand","BackCommand" })
            Assert.True(HasCommand(vm, c), $"Editor missing {c}");
    }
}
```

- [ ] **Step 2: Write the ViewLocator + smoke path test**
```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Nfty.App;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

public class SmokeTests
{
    [AvaloniaFact]
    public void ViewLocator_resolves_a_view_for_every_page_and_dialog_vm()
    {
        var locator = new ViewLocator();
        var dialogs = new FakeDialogs();
        var notify = new FakeNotYetWired();
        var nav = new FakeNav();
        ViewModelBase[] vms =
        [
            new LandingViewModel(nav, dialogs, notify, new FilePickerService(), new RecentsService()),
            new ExplorerViewModel(nav, dialogs, notify),
            new IngredientEditorViewModel(nav, notify),
            new HelpViewModel(dialogs),
            new NewCookBookViewModel(dialogs, notify),
            new NewRecipeViewModel(dialogs, notify),
            new NewIngredientViewModel(dialogs, notify),
        ];
        foreach (var vm in vms)
        {
            var control = locator.Build(vm);
            Assert.False(control is TextBlock tb && tb.Text!.StartsWith("View not found"),
                $"No view for {vm.GetType().Name}");
        }
    }

    [AvaloniaFact]
    public void Landing_new_cookbook_opens_then_cancel_closes()
    {
        var dialogs = new DialogService();
        var vm = new LandingViewModel(new FakeNav(), dialogs, new FakeNotYetWired(),
            new FilePickerService(), new RecentsService());
        vm.NewCookBookCommand.Execute(null);
        Assert.IsType<NewCookBookViewModel>(dialogs.Active);
        ((NewCookBookViewModel)dialogs.Active!).CancelCommand.Execute(null);
        Assert.Null(dialogs.Active);
    }
}
```

- [ ] **Step 3: Run to verify failure, then implement any gaps**

Run: `dotnet test tests/Nfty.App.Tests --filter "FullyQualifiedName~WiringCoverageTests|FullyQualifiedName~SmokeTests" --nologo`
Expected initially: may FAIL if a command is missing or a View is unmapped. Add the missing command/view (that IS the wiring fix), re-run until PASS.

- [ ] **Step 4: Full solution build + test**

Run: `dotnet build nfty.sln --nologo` → Build succeeded, 0 warnings.
Run: `dotnet test nfty.sln --nologo` → all PASS (Core + Cli + App).

- [ ] **Step 5: Manual smoke (non-blocking)**

Run: `dotnet run --project src/Nfty.Desktop` → the window opens on the Landing; clicking New CookBook opens the wizard; Open CookBook shows *"Not wired yet: Open CookBook"* in the status bar; `?` opens Help; `Esc` closes it. Close the window.

- [ ] **Step 6: Commit**
```bash
git add tests/Nfty.App.Tests/WiringCoverageTests.cs tests/Nfty.App.Tests/SmokeTests.cs
git commit -m "$(printf 'test(gui): wiring-coverage gate + ViewLocator/smoke path\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

## Self-Review notes (for the implementer)

- **Task-order compile dependencies.** A few VMs reference types created in later tasks (Shell→Help; Landing→wizards; Explorer detail→Editor). Each such spot is flagged in-task with a temporary (`_notify.Report(...)`) to swap for the real reference when the later task lands. Keep `ServiceRegistration`'s VM registrations in sync — uncomment each `AddTransient` in the task that creates the VM.
- **Stub discipline.** Every command whose Wiring-Map tier is `stub` must call `INotYetWired.Report("<exact action name from §6>")`. The wiring-coverage and per-VM tests assert those names — keep them identical to the map.
- **No colour literals outside `Tokens.axaml`.** Views use `{DynamicResource ...Brush}`. A hex in a View is drift.
- **Avalonia version.** If `net10.0` restore rejects Avalonia 11.2.3, bump to the newest 11.x advertising net10 support and update Global Constraints + every `.csproj` `Version=`.
- **Visual fidelity.** The Views here are functionally complete (every control bound) but not pixel-matched; refining each against its locked mockup (`docs/design/mockups/*.html`) is in-scope polish within its task, using only token brushes.
