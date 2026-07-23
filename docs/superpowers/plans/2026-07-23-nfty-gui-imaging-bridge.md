# nfty GUI — Imaging Bridge Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Render real art through the `Nfty.Core` cook path everywhere images appear in the GUI — Explorer detail panes (ingredient hero, variant thumbnails, colorways, composited recipe hero + reroll) and the Ingredient Editor (canvas + live Colorize preview) — via a new `Image<Rgba32>`→Avalonia `Bitmap` bridge.

**Architecture:** A stateless `IImageBridge` copies ImageSharp pixels into a `WriteableBitmap` (no PNG round-trip). A pure `VariantImagery` helper picks the render path per `LayerKind` (custom = raw, dynamic/static = `Colorizer.Apply` with a colour from `ColorRoller`). The recipe hero reuses `Generator.GenerateStreaming` pinned to one recipe. ViewModels own the `Bitmap`s they create and dispose them; `ExplorerViewModel` disposes the outgoing detail on selection swap; `NavigationService.Back()` disposes popped pages.

**Tech Stack:** .NET 10, Avalonia 11.2.3 (`WriteableBitmap`, `Bitmap`), CommunityToolkit.Mvvm 8.4.0, ImageSharp 3.1.11, `Nfty.Core` (`Colorizer`, `ColorRoller`, `Compositor`, `Generator`), xUnit + `Avalonia.Headless.XUnit`.

## Global Constraints

- **No `Nfty.Core` change** — every render uses an existing public seam.
- ImageSharp pinned **3.1.11**; never upgrade. Never commit `sixlabors.lic`.
- Token brushes only in Views (`{DynamicResource …Brush}`); no raw hex literals (mockup token block is locked).
- Tests: `Snake_case_sentences` names. **`[AvaloniaFact]`** for any test that constructs a `Bitmap`/`WriteableBitmap` or Avalonia control (they need the UI thread); `[Fact]` only for pure records/logic that touch no Avalonia types.
- **Callers own images.** No `GeneratedAsset`/`Image<Rgba32>` may outlive a `ToBitmap` call; dispose the source immediately after conversion. `Colorizer.Apply` returns a NEW image (dispose it); `LoadedIngredient.VariantImages[...]` is owned by the book (never dispose it here).
- Every ctor change updates **all** construction sites in the same commit (DI `ServiceRegistration`, `SmokeTests`, `WiringCoverageTests`, the relevant VM tests, and the `Func<>` factories) so the build stays green.
- Colour math stays in Core; App tests assert **wiring** (path chosen, dims, disposal), not pixel colour values.

## File Structure

- Create `src/Nfty.App/Services/IImageBridge.cs` — the interface + `ImageBridge` impl (Task 1).
- Create `src/Nfty.App/Imaging/VariantImagery.cs` — pure per-kind render helper (Task 2).
- Modify `src/Nfty.App/Services/INavigationService.cs` — `Back()` disposes popped page; `IDisposable` (Task 3).
- Modify `src/Nfty.App/ViewModels/IngredientDetailViewModel.cs` + `ExplorerViewModel.cs` + `Views/IngredientDetailView.axaml` — ingredient imagery + Explorer disposal (Task 4).
- Modify `src/Nfty.App/ViewModels/RecipeDetailViewModel.cs` + `Views/RecipeDetailView.axaml` — recipe hero (Task 5).
- Modify `src/Nfty.App/ViewModels/IngredientEditorViewModel.cs` + `ExplorerViewModel.cs` + `IngredientDetailViewModel.cs` + `ServiceRegistration.cs` — editor wired to real data (Task 6).
- Modify `IngredientEditorViewModel.cs` + `Views/IngredientEditorView.axaml` — canvas + live preview (Task 7).
- Verification only (Task 8).

---

### Task 1: `IImageBridge` — the conversion seam

**Files:**
- Create: `src/Nfty.App/Services/IImageBridge.cs`
- Modify: `src/Nfty.App/ServiceRegistration.cs` (register singleton)
- Test: `tests/Nfty.App.Tests/ImageBridgeTests.cs`

**Interfaces:**
- Produces: `interface IImageBridge { Bitmap ToBitmap(Image<Rgba32> image); }` and `sealed class ImageBridge : IImageBridge`. `Bitmap` = `Avalonia.Media.Imaging.Bitmap`; `Image<Rgba32>` = `SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Nfty.App.Tests/ImageBridgeTests.cs
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using Nfty.App.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public class ImageBridgeTests
{
    [AvaloniaFact]
    public void ToBitmap_matches_source_size_and_pixels()
    {
        using var src = new Image<Rgba32>(2, 2);
        src[0, 0] = new Rgba32(10, 20, 30, 255);
        src[1, 0] = new Rgba32(40, 50, 60, 255);

        var bmp = new ImageBridge().ToBitmap(src);

        Assert.Equal(new PixelSize(2, 2), bmp.PixelSize);

        // Read back the top-left pixel through a locked framebuffer.
        var buffer = new byte[2 * 2 * 4];
        unsafe
        {
            fixed (byte* p = buffer)
                bmp.CopyPixels(new PixelRect(0, 0, 2, 2), (nint)p, buffer.Length, 2 * 4);
        }
        // Rgba8888 unpremultiplied: bytes are R,G,B,A in order.
        Assert.Equal(10, buffer[0]); Assert.Equal(20, buffer[1]); Assert.Equal(30, buffer[2]); Assert.Equal(255, buffer[3]);
        bmp.Dispose();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~ImageBridgeTests`
Expected: FAIL — `IImageBridge`/`ImageBridge` do not exist (compile error).

- [ ] **Step 3: Write the implementation**

```csharp
// src/Nfty.App/Services/IImageBridge.cs
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.App.Services;

/// <summary>Converts an ImageSharp image into an Avalonia <see cref="Bitmap"/>. The returned bitmap
/// owns an independent pixel copy, so the caller disposes the source image immediately after.</summary>
public interface IImageBridge
{
    Bitmap ToBitmap(Image<Rgba32> image);
}

public sealed class ImageBridge : IImageBridge
{
    public Bitmap ToBitmap(Image<Rgba32> image)
    {
        var wb = new WriteableBitmap(
            new PixelSize(image.Width, image.Height),
            new Vector(96, 96),
            PixelFormat.Rgba8888,          // ImageSharp Rgba32 is byte order R,G,B,A — a 1:1 match.
            AlphaFormat.Unpremul);

        using (var fb = wb.Lock())
        {
            int bytes = image.Width * image.Height * 4;
            unsafe
            {
                var span = new Span<byte>((void*)fb.Address, bytes);
                image.CopyPixelDataTo(span);
            }
        }
        return wb;
    }
}
```

The project already compiles unsafe blocks in tests; if `ImageBridge.cs` needs it, add `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` to `src/Nfty.App/Nfty.App.csproj` `<PropertyGroup>`. Check first: `grep AllowUnsafeBlocks src/Nfty.App/Nfty.App.csproj`; add it only if absent.

- [ ] **Step 4: Register the singleton**

In `src/Nfty.App/ServiceRegistration.cs`, after the `ICookBookSession` line add:

```csharp
        services.AddSingleton<IImageBridge, ImageBridge>();
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~ImageBridgeTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Nfty.App/Services/IImageBridge.cs src/Nfty.App/ServiceRegistration.cs src/Nfty.App/Nfty.App.csproj tests/Nfty.App.Tests/ImageBridgeTests.cs
git commit -m "feat(gui): Image<Rgba32> -> Avalonia Bitmap bridge"
```

---

### Task 2: `VariantImagery` — per-kind single-layer render

**Files:**
- Create: `src/Nfty.App/Imaging/VariantImagery.cs`
- Test: `tests/Nfty.App.Tests/VariantImageryTests.cs`

**Interfaces:**
- Consumes: `IImageBridge.ToBitmap` (Task 1); `Nfty.Core` — `Colorizer.Apply`, `ColorRoller.Roll(Colorization, IRng)`, `SplitMix64Rng`, `SeedHash.ToUlong`, `LoadedIngredient` (`.Manifest.Kind`, `.Manifest.Colorization`, `.VariantImages`).
- Produces:
  - `static Bitmap Render(IImageBridge bridge, LoadedIngredient ing, string variantId, int salt = 0)`
  - `static IReadOnlyList<Bitmap> Colorways(IImageBridge bridge, LoadedIngredient ing, int samples = 6)`
  - `static Bitmap RenderWith(IImageBridge bridge, Image<Rgba32> valueMap, bool dynamic, double hueMin, double hueMax, double satMin, double satMax, string fixedColor, int salt = 0)`

**Behaviour:** custom (`Colorization is null`) → raw variant image; otherwise `ColorRoller.Roll` on a stable seed `"{ingId}:{variantId}:{salt}"` → `Colorizer.Apply`. Colorways: dynamic → `samples` hues across the range at mid-saturation; static → one fixed swatch; custom → one raw. `RenderWith` colorizes an explicit value-map with the editor's live colour state; a bad `fixedColor` spec falls back to the raw map (never throws).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Nfty.App.Tests/VariantImageryTests.cs
using Avalonia.Headless.XUnit;
using Nfty.App.Imaging;
using Nfty.App.Services;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public class VariantImageryTests
{
    private static LoadedIngredient Ing(LayerKind kind, Colorization? c) => new()
    {
        Manifest = new IngredientManifest("aura", "Aura", kind, c,
            new[] { new Variant("glow", "Glow", 1) }),
        VariantImages = new Dictionary<string, Image<Rgba32>> { ["glow"] = new Image<Rgba32>(4, 4) },
    };

    private static Colorization Dyn() => new(ColorModel.Hsv, 12, 4,
        new[] { new ColorEntry(1, new ColorRange(0, 360, 40, 100), null) });

    private static Colorization Fixed() => new(ColorModel.Hsv, 1, 1,
        new[] { new ColorEntry(1, null, "hex:d6249f") });

    [AvaloniaFact]
    public void Render_custom_returns_a_bitmap_of_the_value_map_size()
    {
        using var ing = Ing(LayerKind.Custom, null);
        var bmp = VariantImagery.Render(new ImageBridge(), ing, "glow");
        Assert.Equal(4, bmp.PixelSize.Width);
        bmp.Dispose();
    }

    [AvaloniaFact]
    public void Render_dynamic_is_stable_for_the_same_salt()
    {
        using var ing = Ing(LayerKind.Dynamic, Dyn());
        var a = VariantImagery.Render(new ImageBridge(), ing, "glow", salt: 0);
        var b = VariantImagery.Render(new ImageBridge(), ing, "glow", salt: 0);
        Assert.Equal(a.PixelSize, b.PixelSize);   // deterministic seed → same dims, no throw
        a.Dispose(); b.Dispose();
    }

    [AvaloniaFact]
    public void Colorways_dynamic_yields_the_requested_sample_count()
    {
        using var ing = Ing(LayerKind.Dynamic, Dyn());
        var swatches = VariantImagery.Colorways(new ImageBridge(), ing, samples: 6);
        Assert.Equal(6, swatches.Count);
        foreach (var s in swatches) s.Dispose();
    }

    [AvaloniaFact]
    public void Colorways_static_yields_one_swatch()
    {
        using var ing = Ing(LayerKind.Static, Fixed());
        var swatches = VariantImagery.Colorways(new ImageBridge(), ing, samples: 6);
        Assert.Single(swatches);
        foreach (var s in swatches) s.Dispose();
    }

    [AvaloniaFact]
    public void RenderWith_bad_fixed_colour_falls_back_instead_of_throwing()
    {
        using var map = new Image<Rgba32>(4, 4);
        var bmp = VariantImagery.RenderWith(new ImageBridge(), map, dynamic: false,
            0, 360, 40, 100, fixedColor: "not-a-colour");
        Assert.Equal(4, bmp.PixelSize.Width);   // fell back to the raw map, no exception
        bmp.Dispose();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~VariantImageryTests`
Expected: FAIL — `VariantImagery` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
// src/Nfty.App/Imaging/VariantImagery.cs
using Avalonia.Media.Imaging;
using Nfty.App.Services;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.App.Imaging;

/// <summary>Turns a variant's value-map into a display <see cref="Bitmap"/> the way the cook path
/// would: custom = raw, dynamic/static = colorized via <see cref="Colorizer"/>. Pure; the caller owns
/// the returned bitmaps.</summary>
public static class VariantImagery
{
    public static Bitmap Render(IImageBridge bridge, LoadedIngredient ing, string variantId, int salt = 0)
    {
        var map = ing.VariantImages[variantId];
        var coloriz = ing.Manifest.Colorization;
        if (coloriz is null) return bridge.ToBitmap(map);        // custom — raw, book owns the source

        var rng = new SplitMix64Rng(SeedHash.ToUlong($"{ing.Manifest.Id}:{variantId}:{salt}"));
        var c = ColorRoller.Roll(coloriz, rng);
        using var colored = Colorizer.Apply(map, c.H, c.S, coloriz.Model);
        return bridge.ToBitmap(colored);
    }

    public static IReadOnlyList<Bitmap> Colorways(IImageBridge bridge, LoadedIngredient ing, int samples = 6)
    {
        var coloriz = ing.Manifest.Colorization;
        string firstId = ing.Manifest.Variants[0].Id;
        if (coloriz is null || !coloriz.Entries.Any(e => e.Range is not null))
            return new[] { Render(bridge, ing, firstId) };       // custom or static — a single swatch

        var range = coloriz.Entries.First(e => e.Range is not null).Range!;
        double sat = (range.SatMin + range.SatMax) / 2.0 / 100.0;
        var map = ing.VariantImages[firstId];
        var result = new List<Bitmap>(samples);
        for (int i = 0; i < samples; i++)
        {
            double t = samples == 1 ? 0 : (double)i / (samples - 1);
            double hue = range.HueMin + t * (range.HueMax - range.HueMin);
            using var colored = Colorizer.Apply(map, hue, sat, coloriz.Model);
            result.Add(bridge.ToBitmap(colored));
        }
        return result;
    }

    public static Bitmap RenderWith(IImageBridge bridge, Image<Rgba32> valueMap, bool dynamic,
        double hueMin, double hueMax, double satMin, double satMax, string fixedColor, int salt = 0)
    {
        try
        {
            RolledColor c;
            if (dynamic)
            {
                var rng = new SplitMix64Rng(SeedHash.ToUlong($"editor:{salt}"));
                double hue = hueMin + rng.NextDouble() * (hueMax - hueMin);
                double s = (satMin + rng.NextDouble() * (satMax - satMin)) / 100.0;
                c = new RolledColor(hue, s);
            }
            else
            {
                c = ColorRoller.FromFixed(fixedColor, ColorModel.Hsv);
            }
            using var colored = Colorizer.Apply(valueMap, c.H, c.S, ColorModel.Hsv);
            return bridge.ToBitmap(colored);
        }
        catch (FormatException) { return bridge.ToBitmap(valueMap); }        // bad colour spec — show raw
        catch (ArgumentException) { return bridge.ToBitmap(valueMap); }
    }
}
```

Note: confirm the exception type `ColorSpec.Parse` throws for a bad spec (`grep -n "throw" src/Nfty.Core/Imaging/ColorSpec.cs`). If it throws a different type, widen the `catch` to that type. Do **not** catch bare `Exception`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~VariantImageryTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Nfty.App/Imaging/VariantImagery.cs tests/Nfty.App.Tests/VariantImageryTests.cs
git commit -m "feat(gui): VariantImagery — per-kind value-map render helper"
```

---

### Task 3: `NavigationService.Back()` disposes popped pages

**Files:**
- Modify: `src/Nfty.App/Services/INavigationService.cs`
- Test: `tests/Nfty.App.Tests/NavigationServiceTests.cs`

**Interfaces:**
- Produces: `NavigationService : INavigationService, IDisposable`. `Back()` disposes the popped page if it is `IDisposable`; `Dispose()` disposes every page still on the stack.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Nfty.App.Tests/NavigationServiceTests.cs
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

public class NavigationServiceTests
{
    private sealed class DisposablePage : ViewModelBase, IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void Back_disposes_the_popped_page()
    {
        var nav = new NavigationService();
        var home = new DisposablePage();
        var page = new DisposablePage();
        nav.To(home);
        nav.To(page);

        nav.Back();

        Assert.True(page.Disposed);     // popped page freed
        Assert.False(home.Disposed);    // page still current is untouched
        Assert.Same(home, nav.Current);
    }

    [Fact]
    public void Dispose_disposes_every_remaining_page()
    {
        var nav = new NavigationService();
        var a = new DisposablePage();
        nav.To(a);
        nav.Dispose();
        Assert.True(a.Disposed);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~NavigationServiceTests`
Expected: FAIL — `NavigationService` is not `IDisposable`; `Back` does not dispose.

- [ ] **Step 3: Write the implementation**

Replace the body of `NavigationService` in `src/Nfty.App/Services/INavigationService.cs`:

```csharp
public sealed class NavigationService : INavigationService, IDisposable
{
    private readonly Stack<ViewModelBase> _stack = new();
    public ViewModelBase? Current => _stack.Count > 0 ? _stack.Peek() : null;
    public event Action? Changed;

    public void To(ViewModelBase page) { _stack.Push(page); Changed?.Invoke(); }

    public void Back()
    {
        if (_stack.Count <= 1) return;
        var popped = _stack.Pop();
        (popped as IDisposable)?.Dispose();
        Changed?.Invoke();
    }

    public void Dispose()
    {
        while (_stack.Count > 0) (_stack.Pop() as IDisposable)?.Dispose();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~NavigationServiceTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Nfty.App/Services/INavigationService.cs tests/Nfty.App.Tests/NavigationServiceTests.cs
git commit -m "feat(gui): NavigationService.Back disposes popped pages"
```

---

### Task 4: Ingredient detail imagery + Explorer selection disposal

**Files:**
- Modify: `src/Nfty.App/ViewModels/IngredientDetailViewModel.cs`
- Modify: `src/Nfty.App/ViewModels/ExplorerViewModel.cs`
- Modify: `src/Nfty.App/ServiceRegistration.cs` (Explorer factory gains the bridge)
- Modify: `src/Nfty.App/Views/IngredientDetailView.axaml`
- Modify: `tests/Nfty.App.Tests/SmokeTests.cs`, `tests/Nfty.App.Tests/ExplorerViewModelTests.cs` (construction sites)
- Test: `tests/Nfty.App.Tests/IngredientDetailViewModelTests.cs`

**Interfaces:**
- Consumes: `IImageBridge` (T1), `VariantImagery` (T2).
- Produces: `IngredientDetailViewModel(LoadedIngredient ing, LoadedRecipe recipe, LoadedCookBook book, IImageBridge bridge, INotYetWired notify, Action editIngredient, Func<bool> isEditing) : IDisposable`; `VariantRow` gains `Bitmap Thumbnail`; new `Bitmap Hero`, `IReadOnlyList<Bitmap> Colorways`, `SelectVariant(string id)` swaps `Hero`. `ExplorerViewModel(LoadedCookBook, INavigationService, IDialogService, INotYetWired, IImageBridge) : IDisposable` disposes the previous `CurrentDetail` on swap.

- [ ] **Step 1: Write the failing tests**

Add to `tests/Nfty.App.Tests/IngredientDetailViewModelTests.cs` (the `Fixture()` helper there already builds a real ingredient; extend calls with the bridge):

```csharp
    [AvaloniaFact]
    public void Hero_thumbnails_and_colorways_are_built()
    {
        var (book, recipe, ing) = Fixture();
        using var vm = new IngredientDetailViewModel(ing, recipe, book, new ImageBridge(),
            new FakeNotYetWired(), () => { }, () => false);
        Assert.NotNull(vm.Hero);
        Assert.All(vm.Variants, v => Assert.NotNull(v.Thumbnail));
        Assert.NotEmpty(vm.Colorways);
    }

    [AvaloniaFact]
    public void Selecting_a_variant_swaps_the_hero()
    {
        var (book, recipe, ing) = Fixture();
        using var vm = new IngredientDetailViewModel(ing, recipe, book, new ImageBridge(),
            new FakeNotYetWired(), () => { }, () => false);
        var first = vm.Hero;
        vm.SelectVariantCommand.Execute(ing.Manifest.Variants[^1].Id);
        Assert.NotNull(vm.Hero);   // rebuilt; old disposed internally
    }
```

Add `using Avalonia.Headless.XUnit; using Nfty.App.Services;` and update the two existing `new IngredientDetailViewModel(...)` calls in that file to pass `new ImageBridge()` in the new 4th position and wrap in `using`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~IngredientDetailViewModelTests`
Expected: FAIL — ctor arity / missing `Hero`/`Thumbnail`/`Colorways` (compile error).

- [ ] **Step 3: Rewrite `IngredientDetailViewModel`**

```csharp
using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Imaging;
using Nfty.App.Services;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Nfty.Core.Stats;

namespace Nfty.App.ViewModels;

public record VariantRow(string Id, string Name, double Weight, double WithinPercent, double OverallPercent, Bitmap Thumbnail);

public partial class IngredientDetailViewModel : ViewModelBase, IDisposable
{
    private readonly INotYetWired _notify;
    private readonly Action _editIngredient;
    private readonly Func<bool> _isEditing;
    private readonly IImageBridge _bridge;
    private readonly LoadedIngredient _ing;
    private readonly IReadOnlyList<VariantRow> _variants;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Variants))]
    private string _sortColumn = "Variant";

    [ObservableProperty] private Bitmap _hero;

    public string Name { get; }
    public string KindText { get; }
    public string ColorwaysText { get; }
    public IReadOnlyList<Bitmap> Colorways { get; }

    public IReadOnlyList<VariantRow> Variants => SortColumn == "Weight"
        ? _variants.OrderByDescending(v => v.Weight).ThenBy(v => v.Name, StringComparer.Ordinal).ToList()
        : _variants.OrderBy(v => v.Name, StringComparer.Ordinal).ToList();

    public IngredientDetailViewModel(LoadedIngredient ing, LoadedRecipe recipe, LoadedCookBook book,
        IImageBridge bridge, INotYetWired notify, Action editIngredient, Func<bool> isEditing)
    {
        _ing = ing; _bridge = bridge;
        _notify = notify; _editIngredient = editIngredient; _isEditing = isEditing;
        Name = ing.Manifest.Name;
        KindText = ing.Manifest.Kind.ToString();
        ColorwaysText = ColorwaysLabel(ing.Manifest);

        var traits = RarityCalculator.Compute(book).Traits
            .Where(t => t.RecipeId == recipe.Manifest.Id && t.IngredientId == ing.Manifest.Id)
            .ToDictionary(t => t.VariantId, StringComparer.Ordinal);

        _variants = ing.Manifest.Variants.Select(v =>
        {
            traits.TryGetValue(v.Id, out var t);
            return new VariantRow(v.Id, v.Name, v.Weight,
                Math.Round(t?.WithinRecipePercent ?? 0, 1), Math.Round(t?.OverallPercent ?? 0, 1),
                VariantImagery.Render(bridge, ing, v.Id));
        }).ToList();

        Colorways = VariantImagery.Colorways(bridge, ing);
        _hero = VariantImagery.Render(bridge, ing, ing.Manifest.Variants[0].Id);
    }

    private static string ColorwaysLabel(IngredientManifest m) => m.Kind switch
    {
        LayerKind.Dynamic => "HSV · rolled  (value ← value-map)",
        LayerKind.Static => "HSV · fixed  (value ← value-map)",
        _ => "no colorize · composited as-is",
    };

    public void RaiseCanExecuteChanged() => DeleteVariantCommand.NotifyCanExecuteChanged();

    [RelayCommand] private void SortBy(string col) => SortColumn = col;

    [RelayCommand]
    private void SelectVariant(string id)
    {
        var old = Hero;
        Hero = VariantImagery.Render(_bridge, _ing, id);
        old.Dispose();
    }

    [RelayCommand(CanExecute = nameof(CanEdit))] private void DeleteVariant() => _notify.Report("Delete variant");
    [RelayCommand] private void JumpToRules() { /* nav within the recipe rail */ }
    [RelayCommand] private void EditIngredient() => _editIngredient();
    private bool CanEdit() => _isEditing();

    public void Dispose()
    {
        Hero.Dispose();
        foreach (var v in _variants) v.Thumbnail.Dispose();
        foreach (var b in Colorways) b.Dispose();
    }
}
```

Note: `SelectVariantCommand` previously took no id; the mockup selects by variant. Passing `id` is fine. The old `_ = id` no-op is replaced by a real swap.

- [ ] **Step 4: Update `ExplorerViewModel` — bridge + disposal**

In `src/Nfty.App/ViewModels/ExplorerViewModel.cs`: add `using Nfty.App.Services;` is already present. Make the class `IDisposable`, add the bridge field, thread it into the detail VMs, and dispose the outgoing detail:

```csharp
public partial class ExplorerViewModel : ViewModelBase, IDisposable
{
    private readonly INavigationService _nav;
    private readonly IDialogService _dialogs;
    private readonly INotYetWired _notify;
    private readonly IImageBridge _bridge;
    private readonly LoadedCookBook _book;
    // ... existing observable properties unchanged ...

    public ExplorerViewModel(LoadedCookBook book, INavigationService nav, IDialogService dialogs,
        INotYetWired notify, IImageBridge bridge)
    {
        _book = book; _nav = nav; _dialogs = dialogs; _notify = notify; _bridge = bridge;
        Root = BuildTree(book);
    }

    partial void OnSelectedNodeChanged(ExplorerNode? value)
    {
        OnPropertyChanged(nameof(AddLabel));
        (CurrentDetail as IDisposable)?.Dispose();
        CurrentDetail = value?.Kind switch
        {
            ExplorerNodeKind.CookBook => new CookBookDetailViewModel(_book, _notify),
            ExplorerNodeKind.Recipe => new RecipeDetailViewModel((LoadedRecipe)value!.Domain!, _book, _bridge, _notify,
                id => OpenIngredientCommand.Execute(id)),
            ExplorerNodeKind.Ingredient => value!.Domain is (LoadedRecipe r, LoadedIngredient i)
                ? new IngredientDetailViewModel(i, r, _book, _bridge, _notify, () => _notify.Report("Edit ingredient"), () => IsEditing)
                : null,
            _ => null,
        };
    }

    // ... existing commands unchanged ...

    public void Dispose() => (CurrentDetail as IDisposable)?.Dispose();
}
```

Note: `RecipeDetailViewModel`'s new `bridge` parameter is added in Task 5; for THIS task's build to pass, add the `_bridge` argument to the `RecipeDetailViewModel` call **only after Task 5**. To keep Task 4 self-contained and green, leave the `RecipeDetailViewModel` call as its current 4-arg form here and add `_bridge` in Task 5. (i.e. in Task 4, only the `IngredientDetailViewModel` branch gains `_bridge`.)

- [ ] **Step 5: Update the Explorer DI factory + test construction sites**

`src/Nfty.App/ServiceRegistration.cs` — the `Func<LoadedCookBook, ExplorerViewModel>` registration:

```csharp
        services.AddSingleton<Func<LoadedCookBook, ExplorerViewModel>>(sp =>
            book => new ExplorerViewModel(book,
                sp.GetRequiredService<INavigationService>(),
                sp.GetRequiredService<IDialogService>(),
                sp.GetRequiredService<INotYetWired>(),
                sp.GetRequiredService<IImageBridge>()));
```

`tests/Nfty.App.Tests/SmokeTests.cs` — every `new ExplorerViewModel(...)` and the Landing factory lambda gains `new ImageBridge()`; add `using Nfty.App.Services;`. Example:

```csharp
            new ExplorerViewModel(ExplorerViewModelTests.TwoRecipeBook(), nav, dialogs, notify, new ImageBridge()),
```
and in both `LandingViewModel` constructions: `book => new ExplorerViewModel(book, nav, dialogs, notify, new ImageBridge())`.

`tests/Nfty.App.Tests/ExplorerViewModelTests.cs` and `LandingOpenFlowTests.cs` / `LandingViewModelTests.cs` / `WiringCoverageTests.cs` — update each `new ExplorerViewModel(...)` construction and each explorer-factory lambda the same way (add `new ImageBridge()` / `using Nfty.App.Services;`). Grep to find them all:
`grep -rn "new ExplorerViewModel(" tests/Nfty.App.Tests`.

- [ ] **Step 6: Bind the images in the View**

`src/Nfty.App/Views/IngredientDetailView.axaml` — add an `Image` for the hero next to the name block, thumbnails in the variant rows, and a colorways strip. Add to the top identity `Grid` a hero `Image` (before the name `StackPanel`), and give each variant row a thumbnail. Minimal edits (token brushes only, no hex):

Hero — replace the identity `Grid`'s first child area to include:
```xml
      <Image Grid.Column="0" Source="{Binding Hero}" Width="84" Height="84" Margin="0,0,12,0"
             VerticalAlignment="Top" />
```
(Shift the existing name `StackPanel` to a new column; use `ColumnDefinitions="Auto,*,Auto"`.)

Variant row `Grid` (`x:DataType="vm:VariantRow"`) — prepend a thumbnail cell:
```xml
          <Grid ColumnDefinitions="38,*,80,100,100">
            <Image Grid.Column="0" Source="{Binding Thumbnail}" Width="32" Height="32" />
            <TextBlock Grid.Column="1" Text="{Binding Name}" />
            <TextBlock Grid.Column="2" Classes="muted" Text="{Binding Weight}" />
            <TextBlock Grid.Column="3" Classes="muted" Text="{Binding WithinPercent, StringFormat='{}{0}% in recipe'}" />
            <TextBlock Grid.Column="4" Classes="muted" Text="{Binding OverallPercent, StringFormat='{}{0}% overall'}" />
          </Grid>
```

Colorways strip — after the variants `ItemsControl`, add:
```xml
    <TextBlock Text="Colorways" Classes="muted" Margin="0,8,0,0" />
    <ItemsControl ItemsSource="{Binding Colorways}">
      <ItemsControl.ItemsPanel>
        <ItemsPanelTemplate><StackPanel Orientation="Horizontal" Spacing="6" /></ItemsPanelTemplate>
      </ItemsControl.ItemsPanel>
      <ItemsControl.ItemTemplate>
        <DataTemplate><Image Source="{Binding}" Width="40" Height="40" /></DataTemplate>
      </ItemsControl.ItemTemplate>
    </ItemsControl>
```

- [ ] **Step 7: Build + run the full App test project**

Run: `dotnet build src/Nfty.Desktop --nologo` then `dotnet test tests/Nfty.App.Tests --nologo`
Expected: build 0 warnings; all App tests PASS.

- [ ] **Step 8: Commit**

```bash
git add src/Nfty.App tests/Nfty.App.Tests
git commit -m "feat(gui): Ingredient detail renders real hero, thumbnails, colorways"
```

---

### Task 5: Recipe hero via the real pipeline + reroll

**Files:**
- Modify: `src/Nfty.App/ViewModels/RecipeDetailViewModel.cs`
- Modify: `src/Nfty.App/ViewModels/ExplorerViewModel.cs` (pass `_bridge` to the recipe VM)
- Modify: `src/Nfty.App/Views/RecipeDetailView.axaml`
- Modify: `tests/Nfty.App.Tests/RecipeDetailViewModelTests.cs`
- Test: same file.

**Interfaces:**
- Consumes: `IImageBridge` (T1), `Generator.GenerateStreaming`, `GenerateOptions(Count, Seed, RecipeId, MaxRerollsPerAsset, EnforceUniqueDna)`, `GeneratedAsset.Image`.
- Produces: `RecipeDetailViewModel(LoadedRecipe recipe, LoadedCookBook book, IImageBridge bridge, INotYetWired notify, Action<string> openIngredient) : IDisposable`; new `Bitmap Hero`; `Reroll` rebuilds `Hero`.

- [ ] **Step 1: Write the failing tests**

Update the existing tests in `RecipeDetailViewModelTests.cs` to pass the bridge (add `new ImageBridge()` as the 3rd arg, wrap in `using`, add `using Avalonia.Headless.XUnit; using Nfty.App.Services;`), convert them to `[AvaloniaFact]` (they now build a `Bitmap`), and add:

```csharp
    [AvaloniaFact]
    public void Hero_is_built_and_reroll_rebuilds_it()
    {
        var book = ExplorerViewModelTests.TwoRecipeBook();
        var cat = book.Recipes.First(r => r.Manifest.Id == "cat");
        using var vm = new RecipeDetailViewModel(cat, book, new ImageBridge(), new FakeNotYetWired(), _ => { });
        Assert.NotNull(vm.Hero);
        var before = vm.RollSeed;
        vm.RerollCommand.Execute(null);
        Assert.NotEqual(before, vm.RollSeed);
        Assert.NotNull(vm.Hero);   // rebuilt; old disposed internally
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~RecipeDetailViewModelTests`
Expected: FAIL — ctor arity / missing `Hero`.

- [ ] **Step 3: Rewrite `RecipeDetailViewModel`**

```csharp
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;

namespace Nfty.App.ViewModels;

public record LayerRow(int Index, string Id, string Layer, string Kind, int VariantCount);
public record RuleRow(string Text);

public partial class RecipeDetailViewModel : ViewModelBase, IDisposable
{
    private readonly INotYetWired _notify;
    private readonly Action<string> _openIngredient;
    private readonly IImageBridge _bridge;
    private readonly LoadedRecipe _recipe;
    private readonly LoadedCookBook _book;

    [ObservableProperty] private int _rollSeed = 1;
    [ObservableProperty] private Bitmap _hero;

    public string Name { get; }
    public IReadOnlyList<LayerRow> Layers { get; }
    public IReadOnlyList<RuleRow> Rules { get; }

    public RecipeDetailViewModel(LoadedRecipe recipe, LoadedCookBook book, IImageBridge bridge,
        INotYetWired notify, Action<string> openIngredient)
    {
        _recipe = recipe; _book = book; _bridge = bridge; _notify = notify; _openIngredient = openIngredient;
        Name = recipe.Manifest.Name;

        var ingById = recipe.Ingredients.ToDictionary(i => i.Manifest.Id, StringComparer.Ordinal);
        Layers = recipe.Manifest.LayerOrder
            .Where(ingById.ContainsKey)
            .Select((id, i) => new LayerRow(i + 1, id, ingById[id].Manifest.Name,
                ingById[id].Manifest.Kind.ToString(), ingById[id].Manifest.Variants.Count))
            .ToList();

        Rules = recipe.Manifest.Rules.Select(RuleText).ToList();
        _hero = BuildHero();
    }

    private Bitmap BuildHero()
    {
        var opts = new GenerateOptions(Count: 1, Seed: RollSeed.ToString(),
            RecipeId: _recipe.Manifest.Id, EnforceUniqueDna: false);
        using var asset = Generator.GenerateStreaming(_book, opts).First();
        return _bridge.ToBitmap(asset.Image);
    }

    private static RuleRow RuleText(IncompatibilityRule rule)
    {
        string op = rule.Type == RuleType.Exclude ? "✕ never with" : "→ always with";
        string targets = string.Join(", ", rule.Targets.Select(t => $"{t.IngredientId}:{t.VariantId}"));
        return new RuleRow($"{rule.When.IngredientId}:{rule.When.VariantId}  {op}  {targets}");
    }

    [RelayCommand]
    private void Reroll()
    {
        RollSeed++;
        var old = Hero;
        Hero = BuildHero();
        old.Dispose();
    }

    [RelayCommand] private void OpenIngredient(string id) => _openIngredient(id);

    public void Dispose() => Hero.Dispose();
}
```

- [ ] **Step 4: Pass the bridge from `ExplorerViewModel`**

In `ExplorerViewModel.OnSelectedNodeChanged`, the Recipe branch becomes:
```csharp
            ExplorerNodeKind.Recipe => new RecipeDetailViewModel((LoadedRecipe)value!.Domain!, _book, _bridge, _notify,
                id => OpenIngredientCommand.Execute(id)),
```

- [ ] **Step 5: Bind the hero in the View**

`src/Nfty.App/Views/RecipeDetailView.axaml` — add a hero `Image` under the name / above the Layers list:
```xml
    <Image Source="{Binding Hero}" Width="92" Height="92" HorizontalAlignment="Left" />
```
Place it after the `Reroll` row so Reroll visibly updates it.

- [ ] **Step 6: Run tests + build**

Run: `dotnet build src/Nfty.Desktop --nologo` then `dotnet test tests/Nfty.App.Tests --nologo`
Expected: build 0 warnings; all PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Nfty.App tests/Nfty.App.Tests
git commit -m "feat(gui): Recipe hero rendered through the real generator, with reroll"
```

---

### Task 6: Ingredient Editor wired to the opened ingredient

**Files:**
- Modify: `src/Nfty.App/ViewModels/IngredientEditorViewModel.cs`
- Modify: `src/Nfty.App/ViewModels/ExplorerViewModel.cs` (supply the editor factory; ingredient edit navigates)
- Modify: `src/Nfty.App/ViewModels/IngredientDetailViewModel.cs` (no change if the edit callback already navigates — see note)
- Modify: `src/Nfty.App/ServiceRegistration.cs` (editor `Func<>` factory; drop `AddTransient<IngredientEditorViewModel>`)
- Modify: `tests/Nfty.App.Tests/SmokeTests.cs`, `WiringCoverageTests.cs`
- Test: `tests/Nfty.App.Tests/IngredientEditorViewModelTests.cs` (new)

**Interfaces:**
- Consumes: `IImageBridge` (T1), `VariantImagery` (T2), `LoadedIngredient`/`LoadedRecipe`/`LoadedCookBook`.
- Produces: `IngredientEditorViewModel(LoadedIngredient ing, LoadedRecipe recipe, LoadedCookBook book, IImageBridge bridge, INavigationService nav, INotYetWired notify) : IDisposable`; DI `Func<LoadedIngredient, LoadedRecipe, LoadedCookBook, IngredientEditorViewModel>`. `ExplorerViewModel` ctor gains that factory and wires the Ingredient detail's edit callback to `nav.To(factory(i, r, book))`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Nfty.App.Tests/IngredientEditorViewModelTests.cs
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public class IngredientEditorViewModelTests
{
    private static (LoadedIngredient, LoadedRecipe, LoadedCookBook) Real()
    {
        var coloriz = new Colorization(ColorModel.Hsv, 12, 4,
            new[] { new ColorEntry(1, new ColorRange(0, 360, 40, 100), null) });
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("aura", "Aura", LayerKind.Dynamic, coloriz,
                new[] { new Variant("glow", "Glow", 1), new Variant("spark", "Spark", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>>
                { ["glow"] = new(8, 8), ["spark"] = new(8, 8) },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "aura" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(8, 8),
                new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 }),
            Recipes = new[] { recipe },
        };
        return (ing, recipe, book);
    }

    [AvaloniaFact]
    public void Editor_filmstrip_reflects_the_real_variants_with_thumbnails()
    {
        var (ing, recipe, book) = Real();
        using var vm = new IngredientEditorViewModel(ing, recipe, book, new ImageBridge(),
            new FakeNav(), new FakeNotYetWired());
        Assert.Equal(new[] { "Glow", "Spark" }, vm.Variants.Select(v => v.Name));
        Assert.All(vm.Variants, v => Assert.NotNull(v.Thumbnail));
        Assert.NotNull(vm.SelectedVariant);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~IngredientEditorViewModelTests`
Expected: FAIL — ctor arity; `EditorVariant` has no `Thumbnail`.

- [ ] **Step 3: Rewrite `IngredientEditorViewModel` (filmstrip + ctor)**

Replace the stub `EditorVariant` and the Phase-1 fields/ctor. Keep the tool/colorize observable properties. New shape (canvas + preview bitmaps land in Task 7 — this task adds real variants + thumbnails + the ctor/DI/nav wiring):

```csharp
using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Imaging;
using Nfty.App.Services;
using Nfty.Core.Formats;
using Nfty.Core.Model;

namespace Nfty.App.ViewModels;

public enum EditorTool { Brush, Eraser, Rectangle, Circle, Triangle, Select, Fill }

/// <summary>A variant in the editor filmstrip, backed by a real loaded variant.</summary>
public record EditorVariant(string Id, string Name, double Weight, Bitmap Thumbnail);

public partial class IngredientEditorViewModel : ViewModelBase, IDisposable
{
    private readonly INavigationService _nav;
    private readonly INotYetWired _notify;
    private readonly IImageBridge _bridge;
    private readonly LoadedIngredient _ing;

    [ObservableProperty] private EditorTool _activeTool = EditorTool.Brush;
    [ObservableProperty] private int _brushValue = 128;
    [ObservableProperty] private LayerKind _mode;
    [ObservableProperty] private double _hueMin, _hueMax = 360, _satMin = 40, _satMax = 100;
    [ObservableProperty] private int _hueQuantize = 12, _satQuantize = 4;
    [ObservableProperty] private string _fixedColor = "hex:d6249f";
    [ObservableProperty] private EditorVariant? _selectedVariant;

    public ObservableCollection<EditorVariant> Variants { get; } = new();

    public bool ShowColourRange => Mode == LayerKind.Dynamic;
    public bool ShowFixedColour => Mode == LayerKind.Static;
    public bool IsModeStatic { get => Mode == LayerKind.Static; set { if (value) Mode = LayerKind.Static; } }
    public bool IsModeDynamic { get => Mode == LayerKind.Dynamic; set { if (value) Mode = LayerKind.Dynamic; } }

    public IngredientEditorViewModel(LoadedIngredient ing, LoadedRecipe recipe, LoadedCookBook book,
        IImageBridge bridge, INavigationService nav, INotYetWired notify)
    {
        _ing = ing; _bridge = bridge; _nav = nav; _notify = notify;
        Mode = ing.Manifest.Kind == LayerKind.Custom ? LayerKind.Dynamic : ing.Manifest.Kind;

        foreach (var v in ing.Manifest.Variants)
            Variants.Add(new EditorVariant(v.Id, v.Name, v.Weight, VariantImagery.Render(bridge, ing, v.Id)));
        SelectedVariant = Variants.Count > 0 ? Variants[0] : null;
    }

    partial void OnModeChanged(LayerKind value)
    {
        OnPropertyChanged(nameof(ShowColourRange));
        OnPropertyChanged(nameof(ShowFixedColour));
        OnPropertyChanged(nameof(IsModeStatic));
        OnPropertyChanged(nameof(IsModeDynamic));
    }

    [RelayCommand] private void SelectTool(EditorTool tool) => ActiveTool = tool;
    [RelayCommand] private void Undo() => _notify.Report("Undo");
    [RelayCommand] private void Redo() => _notify.Report("Redo");
    [RelayCommand] private void SelectVariant(EditorVariant v) => SelectedVariant = v;
    [RelayCommand] private void AddVariant() => _notify.Report("Add variant");
    [RelayCommand] private void DuplicateVariant() => _notify.Report("Duplicate variant");
    [RelayCommand] private void DeleteVariant() => _notify.Report("Delete variant");
    [RelayCommand] private void ApplyStroke() => _notify.Report("Paint");
    [RelayCommand] private void Save() => _notify.Report("Save ingredient");
    [RelayCommand] private void Back() => _nav.Back();

    // Canvas + preview commands are completed in Task 7.
    [RelayCommand] private void RerollPreview() => _notify.Report("Preview roll");
    [RelayCommand] private void EnlargePreview() => _notify.Report("Enlarge preview");
    [RelayCommand] private void FillPanePreview() => _notify.Report("Fill pane");

    public void Dispose()
    {
        foreach (var v in Variants) v.Thumbnail.Dispose();
    }
}
```

Note: `AddVariant`/`DuplicateVariant`/`DeleteVariant` were Phase-1 list-mutation stubs producing fake variants; with real thumbnails they'd need real draft variants (editor slice), so they revert to `Report` stubs here — mutation is out of scope. The `IngredientEditorView.axaml` filmstrip `DataTemplate` (`x:DataType="vm:EditorVariant"`) already binds `Name`/`Weight`; add a `<Image Source="{Binding Thumbnail}" Width="32" Height="32" />` to it in Task 7 (View pass).

- [ ] **Step 4: Wire the factory + navigation**

`src/Nfty.App/ServiceRegistration.cs` — remove `services.AddTransient<IngredientEditorViewModel>();` and add:
```csharp
        services.AddSingleton<Func<LoadedIngredient, LoadedRecipe, LoadedCookBook, IngredientEditorViewModel>>(sp =>
            (ing, recipe, book) => new IngredientEditorViewModel(ing, recipe, book,
                sp.GetRequiredService<IImageBridge>(),
                sp.GetRequiredService<INavigationService>(),
                sp.GetRequiredService<INotYetWired>()));
```
(Add `using Nfty.Core.Formats;` if not present — it is, for `LoadedCookBook`.)

`ExplorerViewModel` — add the factory to the ctor and wire the ingredient edit callback to it:
```csharp
    private readonly Func<LoadedIngredient, LoadedRecipe, LoadedCookBook, IngredientEditorViewModel> _editorFactory;

    public ExplorerViewModel(LoadedCookBook book, INavigationService nav, IDialogService dialogs,
        INotYetWired notify, IImageBridge bridge,
        Func<LoadedIngredient, LoadedRecipe, LoadedCookBook, IngredientEditorViewModel> editorFactory)
    {
        _book = book; _nav = nav; _dialogs = dialogs; _notify = notify; _bridge = bridge; _editorFactory = editorFactory;
        Root = BuildTree(book);
    }
```
And the Ingredient detail branch's edit callback:
```csharp
            ExplorerNodeKind.Ingredient => value!.Domain is (LoadedRecipe r, LoadedIngredient i)
                ? new IngredientDetailViewModel(i, r, _book, _bridge, _notify,
                    () => _nav.To(_editorFactory(i, r, _book)), () => IsEditing)
                : null,
```

`ServiceRegistration` — the `Func<LoadedCookBook, ExplorerViewModel>` registration gains the editor factory arg:
```csharp
        services.AddSingleton<Func<LoadedCookBook, ExplorerViewModel>>(sp =>
            book => new ExplorerViewModel(book,
                sp.GetRequiredService<INavigationService>(),
                sp.GetRequiredService<IDialogService>(),
                sp.GetRequiredService<INotYetWired>(),
                sp.GetRequiredService<IImageBridge>(),
                sp.GetRequiredService<Func<LoadedIngredient, LoadedRecipe, LoadedCookBook, IngredientEditorViewModel>>()));
```

- [ ] **Step 5: Update test construction sites**

Every `new ExplorerViewModel(...)` in tests gains a stub editor factory arg. Add a shared helper in `ExplorerViewModelTests` (or inline):
```csharp
    internal static Func<LoadedIngredient, LoadedRecipe, LoadedCookBook, IngredientEditorViewModel> EditorFactory(
        INavigationService nav) => (i, r, b) => new IngredientEditorViewModel(i, r, b, new ImageBridge(), nav, new FakeNotYetWired());
```
and pass `ExplorerViewModelTests.EditorFactory(nav)` as the last arg to each `new ExplorerViewModel(...)`. `SmokeTests` also constructs `IngredientEditorViewModel` directly in its VM list — replace that entry with one built from a real ingredient:
```csharp
            // build a throwaway real ingredient/recipe/book for the editor smoke row
```
Use `ExplorerViewModelTests.TwoRecipeBook()` to pull a real `(ing, recipe, book)` for the row, or drop the direct editor entry and rely on `IngredientEditorViewModelTests` + the ViewLocator resolving it through the factory. Simplest: keep a ViewLocator row by constructing from `TwoRecipeBook()`’s `cat` recipe's first ingredient.

- [ ] **Step 6: Build + test**

Run: `dotnet build src/Nfty.Desktop --nologo` then `dotnet test tests/Nfty.App.Tests --nologo`
Expected: build 0 warnings; all PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Nfty.App tests/Nfty.App.Tests
git commit -m "feat(gui): Ingredient Editor wired to the opened ingredient (real filmstrip)"
```

---

### Task 7: Editor canvas + live Colorize preview

**Files:**
- Modify: `src/Nfty.App/ViewModels/IngredientEditorViewModel.cs`
- Modify: `src/Nfty.App/Views/IngredientEditorView.axaml`
- Test: `tests/Nfty.App.Tests/IngredientEditorViewModelTests.cs`

**Interfaces:**
- Consumes: `VariantImagery.RenderWith` (T2).
- Produces: `Bitmap Canvas` and `Bitmap Preview` on `IngredientEditorViewModel`, rebuilt when `SelectedVariant` / `Mode` / `HueMin` / `HueMax` / `SatMin` / `SatMax` / `FixedColor` change; `RerollPreview` re-samples; both disposed in `Dispose()`.

- [ ] **Step 1: Write the failing tests**

Add to `IngredientEditorViewModelTests.cs`:

```csharp
    [AvaloniaFact]
    public void Canvas_and_preview_render_and_update_on_colour_change()
    {
        var (ing, recipe, book) = Real();
        using var vm = new IngredientEditorViewModel(ing, recipe, book, new ImageBridge(),
            new FakeNav(), new FakeNotYetWired());
        Assert.NotNull(vm.Canvas);
        Assert.NotNull(vm.Preview);
        var before = vm.Preview;
        vm.HueMin = 120;                 // change colour state
        Assert.NotSame(before, vm.Preview);   // preview rebuilt (old disposed internally)
    }

    [AvaloniaFact]
    public void Reroll_preview_rebuilds_the_preview()
    {
        var (ing, recipe, book) = Real();
        using var vm = new IngredientEditorViewModel(ing, recipe, book, new ImageBridge(),
            new FakeNav(), new FakeNotYetWired());
        var before = vm.Preview;
        vm.RerollPreviewCommand.Execute(null);
        Assert.NotSame(before, vm.Preview);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~IngredientEditorViewModelTests`
Expected: FAIL — no `Canvas`/`Preview`.

- [ ] **Step 3: Add canvas + preview to the VM**

Add fields/props and rebuild hooks to `IngredientEditorViewModel`:

```csharp
    [ObservableProperty] private Bitmap _canvas = default!;
    [ObservableProperty] private Bitmap _preview = default!;
    private int _previewSalt;
```

At the end of the ctor (after `SelectedVariant` is set):
```csharp
        _canvas = RenderCanvas();
        _preview = RenderPreview();
```

Rebuild helpers + change hooks:
```csharp
    private SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32> SelectedMap =>
        _ing.VariantImages[(SelectedVariant ?? Variants[0]).Id];

    private Bitmap RenderCanvas() =>
        VariantImagery.RenderWith(_bridge, SelectedMap, Mode == LayerKind.Dynamic,
            HueMin, HueMax, SatMin, SatMax, FixedColor, _previewSalt);

    private Bitmap RenderPreview() => RenderCanvas();   // same source; preview is the small companion

    private void RebuildSurfaces()
    {
        var oldCanvas = Canvas; var oldPreview = Preview;
        Canvas = RenderCanvas();
        Preview = RenderPreview();
        oldCanvas?.Dispose(); oldPreview?.Dispose();
    }

    partial void OnSelectedVariantChanged(EditorVariant? value) => RebuildSurfaces();
    partial void OnHueMinChanged(double value) => RebuildSurfaces();
    partial void OnHueMaxChanged(double value) => RebuildSurfaces();
    partial void OnSatMinChanged(double value) => RebuildSurfaces();
    partial void OnSatMaxChanged(double value) => RebuildSurfaces();
    partial void OnFixedColorChanged(string value) => RebuildSurfaces();
```

Extend `OnModeChanged` to also `RebuildSurfaces();` (append at its end).

`RerollPreview` becomes real:
```csharp
    [RelayCommand] private void RerollPreview() { _previewSalt++; RebuildSurfaces(); }
```
`EnlargePreview`/`FillPanePreview` stay `Report` stubs (ui-state only).

Update `Dispose()`:
```csharp
    public void Dispose()
    {
        foreach (var v in Variants) v.Thumbnail.Dispose();
        Canvas?.Dispose(); Preview?.Dispose();
    }
```

Guard: `RebuildSurfaces` runs from property setters during ctor field init — ensure `_bridge`/`_ing` are assigned before any observable property that has a hook is set. In the ctor, assign `_ing`/`_bridge`/`_nav`/`_notify` **first** (already the case), and note that `[ObservableProperty]` initial values assigned via field initializers do NOT fire hooks — only later `Mode = …` in the ctor does. Since `Mode = …` runs before `_canvas`/`_preview` are set, `OnModeChanged`→`RebuildSurfaces` would touch null `Canvas`. Fix: in `RebuildSurfaces`, the `old?.Dispose()` already null-guards; and set `Canvas`/`Preview` there. But `SelectedMap` needs `Variants` populated. Order the ctor so `Variants` is filled and `SelectedVariant` set BEFORE `Mode = …`; then `Mode = …`'s hook builds surfaces safely, and drop the explicit `_canvas = RenderCanvas()` lines (the `Mode` set already built them). Verify by test. If `Mode`'s incoming value equals the field default (no change ⇒ no hook), keep the explicit build lines as a fallback.

- [ ] **Step 4: Bind canvas + preview + filmstrip thumbnails in the View**

`src/Nfty.App/Views/IngredientEditorView.axaml`:
- Filmstrip `DataTemplate` (`x:DataType="vm:EditorVariant"`): add `<Image Source="{Binding Thumbnail}" Width="32" Height="32" />` above the name.
- Canvas (`Grid.Column="2"` `Border` named `Canvas`): put an `Image` inside it — `<Image Source="{Binding Canvas}" Stretch="Uniform" Margin="12" />`. (Rename the `Border` `Name="Canvas"` to avoid colliding with the `Canvas` binding — e.g. `Name="CanvasHost"`.)
- Preview `Border` (Height 120): put `<Image Source="{Binding Preview}" Stretch="Uniform" Margin="6" />` inside.

Token brushes only; no hex.

- [ ] **Step 5: Build + test**

Run: `dotnet build src/Nfty.Desktop --nologo` then `dotnet test tests/Nfty.App.Tests --nologo`
Expected: build 0 warnings; all PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Nfty.App tests/Nfty.App.Tests
git commit -m "feat(gui): editor canvas + live Colorize preview render real art"
```

---

### Task 8: Full verification + manual smoke

**Files:** none (verification).

- [ ] **Step 1: Full solution build + test**

Run: `dotnet build nfty.sln --nologo` → 0 warnings / 0 errors.
Run: `dotnet test nfty.sln --nologo` → all PASS (Core + Cli + App).

- [ ] **Step 2: Grep guards**

Run: `grep -rn "new IngredientEditorViewModel(\|new ExplorerViewModel(\|new RecipeDetailViewModel(\|new IngredientDetailViewModel(" src tests` — confirm every call matches the final ctor arity (no stale 4-arg/5-arg calls).
Run: `grep -rniE "#[0-9a-f]{6}" src/Nfty.App/Views` — confirm no raw hex crept into the edited Views (token brushes only).

- [ ] **Step 3: Manual smoke (user-driven)**

Run: `dotnet run --project src/Nfty.Desktop`. Open `tests/fixtures/VaporPets.cbk` (Ctrl+O). Confirm: selecting the **cookbook** shows metrics; a **recipe** shows a composited hero pet and **Reroll** changes it; an **ingredient** shows a colorized hero, per-variant thumbnails, and a colorways strip; the ✏ button opens the **Ingredient Editor** with a real filmstrip, canvas, and a Colorize preview that updates as the Hue/Sat sliders or Fixed-colour box change and on **Reroll**. **Back** returns to the Explorer. (The look is intentionally plain — visual fidelity is a later pass.)

- [ ] **Step 4: Commit (only if smoke-driven fixups were needed)** — otherwise nothing to commit.

---

## Self-Review

**Spec coverage:**
- §2.1 bridge → Task 1. §2.2 colour derivation / single-layer → Task 2 (+ consumed in 4/6/7). §2.3 recipe hero via `Generator` pinned → Task 5. §2.4 detail VMs imagery (CookBook = no bitmap per spec default; Recipe hero; Ingredient hero/thumbnails/colorways) → Tasks 4–5. §2.5 editor wired to real data (ctor/nav/filmstrip/canvas/preview) → Tasks 6–7. §2.6 lifetime & disposal (VM `IDisposable`, Explorer swap-dispose, `NavigationService.Back` dispose) → Tasks 3–7. §4 testing → each task's tests + Task 8. §5 deferred / §6 out-of-scope → not implemented, correct.
- Gap check: the cookbook-level montage is explicitly out of scope (spec §2.4 default: no cookbook bitmap) — no task, correct. `Enlarge`/`Fill pane` remain ui-state stubs (spec §2.5) — Task 7 keeps them as `Report`, correct.

**Placeholder scan:** every code step shows full code; no "TBD"/"handle edge cases"/"similar to Task N". Two explicit verification notes (confirm `ColorSpec.Parse` exception type in Task 2; confirm `AllowUnsafeBlocks` in Task 1) are concrete checks with the exact grep, not placeholders.

**Type consistency:** `IImageBridge.ToBitmap` signature identical across Tasks 1/2/4/5/6/7. `VariantImagery.Render/Colorways/RenderWith` signatures fixed in Task 2 and consumed unchanged. `VariantRow` gains `Id` (already added in the prior merged slice) + `Bitmap Thumbnail` (Task 4). `EditorVariant` gains `Bitmap Thumbnail` (Task 6). `ExplorerViewModel` ctor final arity = `(book, nav, dialogs, notify, bridge, editorFactory)` — Task 4 adds `bridge`, Task 6 adds `editorFactory`; both tasks update all construction sites, and Task 8 greps for stragglers. `GenerateOptions(Count, Seed, RecipeId, …, EnforceUniqueDna)` used with named args in Task 5, matching the Core record.
