# nfty Core Engine & CLI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the headless `Nfty.Core` library and `nfty` CLI that authors layered NFT collections as versioned ZIP archives and generates deterministic asset sets with static and dynamic (grayscale-recolored) layers.

**Architecture:** A pure C# class library (`Nfty.Core`) holds the domain model, ZIP archive I/O, imaging/colorization, a seeded generation pipeline (weighted roll → rules → colorize → composite → DNA dedup), and set output. A thin `Nfty.Cli` wires `System.CommandLine` over the library. Everything is TDD with xUnit; imaging uses ImageSharp (fully managed).

**Tech Stack:** .NET 10 (LTS), C#, SixLabors.ImageSharp 4.0.0, System.CommandLine 2.0.9, System.IO.Compression, System.Text.Json, xUnit.

**Design spec:** `docs/superpowers/specs/2026-07-10-nfty-core-engine-design.md` (Model B domain model).

## Global Constraints

- **Target framework:** `net10.0` for every project.
- **Packages (pinned):** `SixLabors.ImageSharp` `4.0.0`; `System.CommandLine` `2.0.9`.
- **Domain model:** Model B — CookBook (`.cbk`) = full template; Recipe (`.rcp`) = one layer; Ingredient (`.igt`) = one image variant; Set (`.set`) = generated bundle.
- **Weights & colorization live on the Recipe; incompatibility rules live on the CookBook; `kind` (static/dynamic) is per-Recipe.**
- **Canvas is the single source of truth for size.** No per-layer/ingredient dimensions; validate images against `canvas` on add/generate.
- **Color input syntax (prefix required):** `hex:rrggbb`, `rgb:r,g,b`, `hsl:h,s,l`, `hsv:h,s,v`. Unknown/missing prefix is an error.
- **Dynamic colorization:** grayscale value = red channel `g = R/255`; `g`→V (hsv) or L (hsl); rolled color supplies H,S; alpha preserved.
- **Determinism:** a string seed drives a SplitMix64 RNG; same cookbook + seed ⇒ identical output. Seed stored in `set.json`.
- **All manifests carry `schemaVersion` (start at `1`).** JSON is camelCase, enums as strings.
- **Metadata:** ERC-721/OpenSea fields + extras (`setNumber`, `dna`, `seed`, `rarity`, `colorRolls`).
- **Git workflow:** no remote yet. Each task lands on its own local branch `feat/<task>` and is merged into `master` with `--no-ff`. When a remote is added later these become PRs. Commit messages end with the `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>` trailer.

---

## File Structure

```
nfty.sln
src/
  Nfty.Core/
    Nfty.Core.csproj
    Model/
      Dimensions.cs            # canvas size record
      Colorization.cs          # ColorModel, ColorRange, ColorEntry, Colorization
      LayerKind.cs             # enum Static|Dynamic
      IngredientManifest.cs
      RecipeManifest.cs
      IncompatibilityRule.cs   # RuleType, RuleTarget, IncompatibilityRule
      Collection.cs
      CookBookManifest.cs
    Imaging/
      RgbColor.cs
      ColorConvert.cs          # HSV/HSL <-> RGB
      ColorSpec.cs             # parse "hex:/rgb:/hsl:/hsv:" -> RgbColor
      Colorizer.cs             # recolor a value-map
      Compositor.cs            # stack layers
    Formats/
      Json.cs                  # shared JsonSerializerOptions
      ArchiveIo.cs             # manifest/image/nested-zip helpers
      Loaded.cs                # LoadedIngredient/Recipe/CookBook
      IngredientArchive.cs
      RecipeArchive.cs
      CookBookArchive.cs
      Validator.cs             # canvas + weight + rule-reference checks
    Generation/
      Rng.cs                   # IRng + SplitMix64Rng + SeedHash
      WeightedRoller.cs
      ColorRoller.cs
      Dna.cs                   # LayerSelection, Dna.Compute
      RulesEngine.cs
      Generator.cs             # orchestrator -> GeneratedSet
      GeneratedSet.cs          # GeneratedAsset, TraitSelection, ColorRoll, GeneratedSet
    Output/
      Metadata.cs              # ERC-721 + extras DTOs
      SetWriter.cs             # write folder or .set, + extend loader
    Stats/
      RarityCalculator.cs
  Nfty.Cli/
    Nfty.Cli.csproj
    Program.cs
    CommandFactory.cs          # builds RootCommand (testable)
tests/
  Nfty.Core.Tests/
    Nfty.Core.Tests.csproj
    <one test file per unit>
  Nfty.Cli.Tests/
    Nfty.Cli.Tests.csproj
    CommandFactoryTests.cs
```

---

## Task 1: Solution & project scaffold

**Files:**
- Create: `nfty.sln`, `src/Nfty.Core/Nfty.Core.csproj`, `src/Nfty.Cli/Nfty.Cli.csproj`, `tests/Nfty.Core.Tests/Nfty.Core.Tests.csproj`, `tests/Nfty.Cli.Tests/Nfty.Cli.Tests.csproj`
- Test: `tests/Nfty.Core.Tests/SmokeTest.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: the solution, project references, and pinned packages every later task builds on.

- [ ] **Step 1: Create the branch**

```bash
cd /home/dev/repo/nfty && git checkout -b feat/scaffold
```

- [ ] **Step 2: Create solution and projects**

```bash
dotnet new sln -n nfty
dotnet new classlib -o src/Nfty.Core -n Nfty.Core -f net10.0
dotnet new console  -o src/Nfty.Cli  -n Nfty.Cli  -f net10.0
dotnet new xunit    -o tests/Nfty.Core.Tests -n Nfty.Core.Tests -f net10.0
dotnet new xunit    -o tests/Nfty.Cli.Tests  -n Nfty.Cli.Tests  -f net10.0
rm src/Nfty.Core/Class1.cs
dotnet sln add src/Nfty.Core src/Nfty.Cli tests/Nfty.Core.Tests tests/Nfty.Cli.Tests
```

- [ ] **Step 3: Wire references and packages**

```bash
dotnet add src/Nfty.Core package SixLabors.ImageSharp --version 4.0.0
dotnet add src/Nfty.Cli  package System.CommandLine   --version 2.0.9
dotnet add src/Nfty.Cli reference src/Nfty.Core
dotnet add tests/Nfty.Core.Tests reference src/Nfty.Core
dotnet add tests/Nfty.Core.Tests package SixLabors.ImageSharp --version 4.0.0
dotnet add tests/Nfty.Cli.Tests  reference src/Nfty.Cli
```

- [ ] **Step 4: Enable nullable + implicit usings in Core**

Confirm `src/Nfty.Core/Nfty.Core.csproj` `<PropertyGroup>` contains:

```xml
<TargetFramework>net10.0</TargetFramework>
<ImplicitUsings>enable</ImplicitUsings>
<Nullable>enable</Nullable>
```

(The templates set these by default; add any that are missing.)

- [ ] **Step 5: Write the smoke test**

`tests/Nfty.Core.Tests/SmokeTest.cs`:

```csharp
namespace Nfty.Core.Tests;

public class SmokeTest
{
    [Fact]
    public void Solution_builds_and_tests_run() => Assert.True(true);
}
```

- [ ] **Step 6: Build and test**

Run: `dotnet build && dotnet test`
Expected: build succeeds; 1 test passes.

- [ ] **Step 7: Commit and merge**

```bash
git add -A
git commit -m "chore: scaffold nfty solution (Core, Cli, tests)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
git checkout master && git merge --no-ff feat/scaffold -m "Merge feat/scaffold"
```

---

## Task 2: Domain model records

**Files:**
- Create: `src/Nfty.Core/Model/Dimensions.cs`, `Colorization.cs`, `LayerKind.cs`, `IngredientManifest.cs`, `RecipeManifest.cs`, `IncompatibilityRule.cs`, `Collection.cs`, `CookBookManifest.cs`
- Test: `tests/Nfty.Core.Tests/ModelTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces (namespace `Nfty.Core.Model`), exact shapes:
  - `record Dimensions(int Width, int Height)`
  - `enum LayerKind { Static, Dynamic }`
  - `enum ColorModel { Hsv, Hsl }`
  - `record ColorRange(double HueMin, double HueMax, double SatMin, double SatMax)`
  - `record ColorEntry(double Weight, ColorRange? Range, string? Fixed)`
  - `record Colorization(ColorModel Model, int HueQuantize, int SatQuantize, IReadOnlyList<ColorEntry> Entries)`
  - `record IngredientManifest(string Id, string Name, string Sha256, int SchemaVersion = 1)`
  - `record RecipeManifest(string Id, string Name, LayerKind Kind, int Order, IReadOnlyDictionary<string,double> Measurements, Colorization? Colorization, int SchemaVersion = 1)`
  - `enum RuleType { Exclude, Require }`
  - `record RuleTarget(string LayerId, string IngredientId)`
  - `record IncompatibilityRule(RuleType Type, RuleTarget When, IReadOnlyList<RuleTarget> Targets)`
  - `record Collection(string Name, string Description, string Symbol)`
  - `record CookBookManifest(string Id, string Name, Dimensions Canvas, IReadOnlyList<string> LayerOrder, IReadOnlyList<IncompatibilityRule> Rules, Collection Collection, int SchemaVersion = 1)`

- [ ] **Step 1: Create the branch**

```bash
git checkout -b feat/model
```

- [ ] **Step 2: Write the failing test**

`tests/Nfty.Core.Tests/ModelTests.cs`:

```csharp
using Nfty.Core.Model;

namespace Nfty.Core.Tests;

public class ModelTests
{
    [Fact]
    public void CookBookManifest_holds_canvas_and_layer_order()
    {
        var cb = new CookBookManifest(
            Id: "cb1", Name: "VaporPets",
            Canvas: new Dimensions(512, 512),
            LayerOrder: new[] { "bg", "body" },
            Rules: Array.Empty<IncompatibilityRule>(),
            Collection: new Collection("VaporPets", "desc", "VPET"));

        Assert.Equal(512, cb.Canvas.Width);
        Assert.Equal(new[] { "bg", "body" }, cb.LayerOrder);
        Assert.Equal(1, cb.SchemaVersion);
    }

    [Fact]
    public void Dynamic_recipe_carries_colorization_entries()
    {
        var recipe = new RecipeManifest(
            Id: "aura", Name: "Aura", Kind: LayerKind.Dynamic, Order: 5,
            Measurements: new Dictionary<string, double> { ["glow"] = 1.0 },
            Colorization: new Colorization(ColorModel.Hsv, 5, 5, new[]
            {
                new ColorEntry(10, null, "hex:d6249f"),
                new ColorEntry(30, new ColorRange(175, 195, 60, 90), null),
            }));

        Assert.Equal(LayerKind.Dynamic, recipe.Kind);
        Assert.Equal(2, recipe.Colorization!.Entries.Count);
        Assert.Equal("hex:d6249f", recipe.Colorization.Entries[0].Fixed);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/Nfty.Core.Tests --filter ModelTests`
Expected: FAIL — types `CookBookManifest`, etc. do not exist.

- [ ] **Step 4: Write the records**

`src/Nfty.Core/Model/Dimensions.cs`:

```csharp
namespace Nfty.Core.Model;

public record Dimensions(int Width, int Height);
```

`src/Nfty.Core/Model/LayerKind.cs`:

```csharp
namespace Nfty.Core.Model;

public enum LayerKind { Static, Dynamic }
```

`src/Nfty.Core/Model/Colorization.cs`:

```csharp
namespace Nfty.Core.Model;

public enum ColorModel { Hsv, Hsl }

public record ColorRange(double HueMin, double HueMax, double SatMin, double SatMax);

/// <summary>Exactly one of <see cref="Range"/> or <see cref="Fixed"/> is set.</summary>
public record ColorEntry(double Weight, ColorRange? Range, string? Fixed);

public record Colorization(
    ColorModel Model,
    int HueQuantize,
    int SatQuantize,
    IReadOnlyList<ColorEntry> Entries);
```

`src/Nfty.Core/Model/IngredientManifest.cs`:

```csharp
namespace Nfty.Core.Model;

public record IngredientManifest(string Id, string Name, string Sha256, int SchemaVersion = 1);
```

`src/Nfty.Core/Model/RecipeManifest.cs`:

```csharp
namespace Nfty.Core.Model;

public record RecipeManifest(
    string Id,
    string Name,
    LayerKind Kind,
    int Order,
    IReadOnlyDictionary<string, double> Measurements,
    Colorization? Colorization,
    int SchemaVersion = 1);
```

`src/Nfty.Core/Model/IncompatibilityRule.cs`:

```csharp
namespace Nfty.Core.Model;

public enum RuleType { Exclude, Require }

public record RuleTarget(string LayerId, string IngredientId);

public record IncompatibilityRule(RuleType Type, RuleTarget When, IReadOnlyList<RuleTarget> Targets);
```

`src/Nfty.Core/Model/Collection.cs`:

```csharp
namespace Nfty.Core.Model;

public record Collection(string Name, string Description, string Symbol);
```

`src/Nfty.Core/Model/CookBookManifest.cs`:

```csharp
namespace Nfty.Core.Model;

public record CookBookManifest(
    string Id,
    string Name,
    Dimensions Canvas,
    IReadOnlyList<string> LayerOrder,
    IReadOnlyList<IncompatibilityRule> Rules,
    Collection Collection,
    int SchemaVersion = 1);
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Nfty.Core.Tests --filter ModelTests`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit and merge**

```bash
git add -A
git commit -m "feat: domain model records (Model B)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
git checkout master && git merge --no-ff feat/model -m "Merge feat/model"
```

---

## Task 3: Color conversion & color-spec parsing

**Files:**
- Create: `src/Nfty.Core/Imaging/RgbColor.cs`, `ColorConvert.cs`, `ColorSpec.cs`
- Test: `tests/Nfty.Core.Tests/ColorConvertTests.cs`, `ColorSpecTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces (namespace `Nfty.Core.Imaging`):
  - `readonly record struct RgbColor(byte R, byte G, byte B)`
  - `static class ColorConvert` with:
    - `static RgbColor HsvToRgb(double h, double s, double v)` — `h`∈[0,360), `s,v`∈[0,1]
    - `static RgbColor HslToRgb(double h, double s, double l)`
    - `static (double H, double S, double V) RgbToHsv(RgbColor c)`
    - `static (double H, double S, double L) RgbToHsl(RgbColor c)`
  - `static class ColorSpec` with `static RgbColor Parse(string spec)` (throws `FormatException` on bad/missing prefix)

- [ ] **Step 1: Create the branch**

```bash
git checkout -b feat/color-spec
```

- [ ] **Step 2: Write the failing tests**

`tests/Nfty.Core.Tests/ColorConvertTests.cs`:

```csharp
using Nfty.Core.Imaging;

namespace Nfty.Core.Tests;

public class ColorConvertTests
{
    [Fact]
    public void HsvToRgb_pure_red()
    {
        var c = ColorConvert.HsvToRgb(0, 1, 1);
        Assert.Equal(new RgbColor(255, 0, 0), c);
    }

    [Fact]
    public void HsvToRgb_zero_value_is_black_for_any_hue()
    {
        Assert.Equal(new RgbColor(0, 0, 0), ColorConvert.HsvToRgb(210, 0.8, 0.0));
    }

    [Fact]
    public void HslToRgb_full_lightness_is_white()
    {
        Assert.Equal(new RgbColor(255, 255, 255), ColorConvert.HslToRgb(120, 0.5, 1.0));
    }

    [Fact]
    public void RgbToHsv_roundtrips_hue_saturation()
    {
        var (h, s, v) = ColorConvert.RgbToHsv(new RgbColor(214, 36, 159));
        Assert.InRange(h, 321.0, 323.0);
        Assert.InRange(s, 0.82, 0.84);
        Assert.InRange(v, 0.83, 0.85);
    }
}
```

`tests/Nfty.Core.Tests/ColorSpecTests.cs`:

```csharp
using Nfty.Core.Imaging;

namespace Nfty.Core.Tests;

public class ColorSpecTests
{
    [Fact]
    public void Parses_hex() =>
        Assert.Equal(new RgbColor(214, 36, 159), ColorSpec.Parse("hex:d6249f"));

    [Fact]
    public void Parses_rgb() =>
        Assert.Equal(new RgbColor(214, 36, 159), ColorSpec.Parse("rgb:214,36,159"));

    [Fact]
    public void Parses_hsv_to_expected_rgb() =>
        Assert.Equal(new RgbColor(255, 0, 0), ColorSpec.Parse("hsv:0,100,100"));

    [Fact]
    public void Missing_prefix_throws() =>
        Assert.Throws<FormatException>(() => ColorSpec.Parse("d6249f"));

    [Fact]
    public void Unknown_prefix_throws() =>
        Assert.Throws<FormatException>(() => ColorSpec.Parse("cmyk:1,2,3,4"));
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Nfty.Core.Tests --filter "ColorConvertTests|ColorSpecTests"`
Expected: FAIL — types do not exist.

- [ ] **Step 4: Implement RgbColor and ColorConvert**

`src/Nfty.Core/Imaging/RgbColor.cs`:

```csharp
namespace Nfty.Core.Imaging;

public readonly record struct RgbColor(byte R, byte G, byte B);
```

`src/Nfty.Core/Imaging/ColorConvert.cs`:

```csharp
namespace Nfty.Core.Imaging;

public static class ColorConvert
{
    private static byte B(double x) => (byte)Math.Clamp((int)Math.Round(x * 255.0), 0, 255);

    public static RgbColor HsvToRgb(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60.0 % 2) - 1));
        double m = v - c;
        (double r, double g, double b) = h switch
        {
            < 60  => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _     => (c, 0.0, x),
        };
        return new RgbColor(B(r + m), B(g + m), B(b + m));
    }

    public static RgbColor HslToRgb(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360;
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs((h / 60.0 % 2) - 1));
        double m = l - c / 2;
        (double r, double g, double b) = h switch
        {
            < 60  => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _     => (c, 0.0, x),
        };
        return new RgbColor(B(r + m), B(g + m), B(b + m));
    }

    public static (double H, double S, double V) RgbToHsv(RgbColor c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double d = max - min;
        double h = Hue(r, g, b, max, d);
        double s = max == 0 ? 0 : d / max;
        return (h, s, max);
    }

    public static (double H, double S, double L) RgbToHsl(RgbColor c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double d = max - min;
        double l = (max + min) / 2;
        double s = d == 0 ? 0 : d / (1 - Math.Abs(2 * l - 1));
        return (Hue(r, g, b, max, d), s, l);
    }

    private static double Hue(double r, double g, double b, double max, double d)
    {
        if (d == 0) return 0;
        double h = max == r ? ((g - b) / d % 6)
                 : max == g ? ((b - r) / d + 2)
                 : ((r - g) / d + 4);
        h *= 60;
        return h < 0 ? h + 360 : h;
    }
}
```

- [ ] **Step 5: Implement ColorSpec**

`src/Nfty.Core/Imaging/ColorSpec.cs`:

```csharp
using System.Globalization;

namespace Nfty.Core.Imaging;

public static class ColorSpec
{
    public static RgbColor Parse(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
            throw new FormatException("Empty color spec.");
        int i = spec.IndexOf(':');
        if (i <= 0)
            throw new FormatException($"Color spec '{spec}' is missing a prefix (hex:/rgb:/hsl:/hsv:).");

        string prefix = spec[..i].Trim().ToLowerInvariant();
        string body = spec[(i + 1)..].Trim();

        return prefix switch
        {
            "hex" => Hex(body),
            "rgb" => Triple(body, (a, b, c) => new RgbColor(Byte(a), Byte(b), Byte(c))),
            "hsv" => Triple(body, (h, s, v) => ColorConvert.HsvToRgb(h, s / 100.0, v / 100.0)),
            "hsl" => Triple(body, (h, s, l) => ColorConvert.HslToRgb(h, s / 100.0, l / 100.0)),
            _ => throw new FormatException($"Unknown color prefix '{prefix}'."),
        };
    }

    private static RgbColor Hex(string body)
    {
        if (body.Length != 6 && body.Length != 8)
            throw new FormatException($"hex expects rrggbb or rrggbbaa, got '{body}'.");
        byte P(int start) => byte.Parse(body.Substring(start, 2), NumberStyles.HexNumber);
        return new RgbColor(P(0), P(2), P(4));
    }

    private static RgbColor Triple(string body, Func<double, double, double, RgbColor> make)
    {
        var parts = body.Split(',');
        if (parts.Length != 3)
            throw new FormatException($"Expected 3 comma-separated values, got '{body}'.");
        double D(string s) => double.Parse(s.Trim(), CultureInfo.InvariantCulture);
        return make(D(parts[0]), D(parts[1]), D(parts[2]));
    }

    private static byte Byte(double d) => (byte)Math.Clamp((int)Math.Round(d), 0, 255);
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/Nfty.Core.Tests --filter "ColorConvertTests|ColorSpecTests"`
Expected: PASS (9 tests).

- [ ] **Step 7: Commit and merge**

```bash
git add -A
git commit -m "feat: color conversion and prefixed color-spec parsing

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
git checkout master && git merge --no-ff feat/color-spec -m "Merge feat/color-spec"
```

---

## Task 4: Colorizer (recolor a value-map)

**Files:**
- Create: `src/Nfty.Core/Imaging/Colorizer.cs`
- Test: `tests/Nfty.Core.Tests/ColorizerTests.cs`

**Interfaces:**
- Consumes: `ColorConvert`, `Nfty.Core.Model.ColorModel`.
- Produces: `static class Colorizer` with
  `static Image<Rgba32> Apply(Image<Rgba32> valueMap, double h, double s, ColorModel model)` — returns a new recolored image; `g = R/255` drives V (hsv) or L (hsl); alpha preserved; input not mutated.

- [ ] **Step 1: Create the branch**

```bash
git checkout -b feat/colorizer
```

- [ ] **Step 2: Write the failing test**

`tests/Nfty.Core.Tests/ColorizerTests.cs`:

```csharp
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

public class ColorizerTests
{
    [Fact]
    public void Hsv_maps_grayscale_value_to_v_with_rolled_hue()
    {
        using var map = new Image<Rgba32>(1, 1);
        map[0, 0] = new Rgba32(128, 128, 128, 255); // g ~ 0.502

        using var outImg = Colorizer.Apply(map, h: 0, s: 1.0, ColorModel.Hsv);

        var px = outImg[0, 0];
        // hue 0, s 1, v 0.502 -> pure-ish red at half value
        Assert.Equal(128, px.R);
        Assert.Equal(0, px.G);
        Assert.Equal(0, px.B);
        Assert.Equal(255, px.A);
    }

    [Fact]
    public void Preserves_alpha_and_does_not_mutate_input()
    {
        using var map = new Image<Rgba32>(1, 1);
        map[0, 0] = new Rgba32(200, 200, 200, 64);

        using var outImg = Colorizer.Apply(map, h: 180, s: 0.5, ColorModel.Hsl);

        Assert.Equal(64, outImg[0, 0].A);
        Assert.Equal(new Rgba32(200, 200, 200, 64), map[0, 0]); // unchanged
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/Nfty.Core.Tests --filter ColorizerTests`
Expected: FAIL — `Colorizer` does not exist.

- [ ] **Step 4: Implement Colorizer**

`src/Nfty.Core/Imaging/Colorizer.cs`:

```csharp
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Imaging;

public static class Colorizer
{
    public static Image<Rgba32> Apply(Image<Rgba32> valueMap, double h, double s, ColorModel model)
    {
        var result = valueMap.Clone();
        result.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    double g = row[x].R / 255.0;
                    RgbColor c = model == ColorModel.Hsv
                        ? ColorConvert.HsvToRgb(h, s, g)
                        : ColorConvert.HslToRgb(h, s, g);
                    row[x] = new Rgba32(c.R, c.G, c.B, row[x].A);
                }
            }
        });
        return result;
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Nfty.Core.Tests --filter ColorizerTests`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit and merge**

```bash
git add -A
git commit -m "feat: value-map colorizer (HSV/HSL, alpha-preserving)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
git checkout master && git merge --no-ff feat/colorizer -m "Merge feat/colorizer"
```

---

## Task 5: Compositor (stack layers)

**Files:**
- Create: `src/Nfty.Core/Imaging/Compositor.cs`
- Test: `tests/Nfty.Core.Tests/CompositorTests.cs`

**Interfaces:**
- Consumes: `Nfty.Core.Model.Dimensions`.
- Produces: `static class Compositor` with
  `static Image<Rgba32> Composite(Dimensions canvas, IReadOnlyList<Image<Rgba32>> layersBottomToTop)` — new transparent canvas with each layer drawn source-over, bottom first.

- [ ] **Step 1: Create the branch**

```bash
git checkout -b feat/compositor
```

- [ ] **Step 2: Write the failing test**

`tests/Nfty.Core.Tests/CompositorTests.cs`:

```csharp
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

public class CompositorTests
{
    [Fact]
    public void Top_opaque_layer_covers_bottom()
    {
        using var bottom = new Image<Rgba32>(2, 2, new Rgba32(255, 0, 0, 255));
        using var top = new Image<Rgba32>(2, 2, new Rgba32(0, 0, 255, 255));

        using var result = Compositor.Composite(new Dimensions(2, 2), new[] { bottom, top });

        Assert.Equal(new Rgba32(0, 0, 255, 255), result[0, 0]);
    }

    [Fact]
    public void Transparent_top_reveals_bottom()
    {
        using var bottom = new Image<Rgba32>(1, 1, new Rgba32(255, 0, 0, 255));
        using var top = new Image<Rgba32>(1, 1, new Rgba32(0, 0, 255, 0));

        using var result = Compositor.Composite(new Dimensions(1, 1), new[] { bottom, top });

        Assert.Equal(new Rgba32(255, 0, 0, 255), result[0, 0]);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/Nfty.Core.Tests --filter CompositorTests`
Expected: FAIL — `Compositor` does not exist.

- [ ] **Step 4: Implement Compositor**

`src/Nfty.Core/Imaging/Compositor.cs`:

```csharp
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Nfty.Core.Imaging;

public static class Compositor
{
    public static Image<Rgba32> Composite(Dimensions canvas, IReadOnlyList<Image<Rgba32>> layersBottomToTop)
    {
        var result = new Image<Rgba32>(canvas.Width, canvas.Height, new Rgba32(0, 0, 0, 0));
        foreach (var layer in layersBottomToTop)
            result.Mutate(ctx => ctx.DrawImage(layer, 1f));
        return result;
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Nfty.Core.Tests --filter CompositorTests`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit and merge**

```bash
git add -A
git commit -m "feat: layer compositor

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
git checkout master && git merge --no-ff feat/compositor -m "Merge feat/compositor"
```

---

## Task 6: Archive formats (.igt / .rcp / .cbk) + validator

**Files:**
- Create: `src/Nfty.Core/Formats/Json.cs`, `ArchiveIo.cs`, `Loaded.cs`, `IngredientArchive.cs`, `RecipeArchive.cs`, `CookBookArchive.cs`, `Validator.cs`
- Test: `tests/Nfty.Core.Tests/ArchiveRoundTripTests.cs`, `ValidatorTests.cs`

**Interfaces:**
- Consumes: `Nfty.Core.Model.*`, ImageSharp.
- Produces (namespace `Nfty.Core.Formats`):
  - `static class Json { static JsonSerializerOptions Options }` (camelCase, `JsonStringEnumConverter`, indented).
  - `class LoadedIngredient { IngredientManifest Manifest; Image<Rgba32> Image }`
  - `class LoadedRecipe { RecipeManifest Manifest; IReadOnlyList<LoadedIngredient> Ingredients }`
  - `class LoadedCookBook { CookBookManifest Manifest; IReadOnlyList<LoadedRecipe> Recipes }`
  - `static class IngredientArchive { void Write(string path, IngredientManifest m, Image<Rgba32> img); LoadedIngredient Read(string path) }`
  - `static class RecipeArchive { void Write(string path, RecipeManifest m, IReadOnlyList<LoadedIngredient> ingredients); LoadedRecipe Read(string path) }`
  - `static class CookBookArchive { void Write(string path, CookBookManifest m, IReadOnlyList<LoadedRecipe> recipes); LoadedCookBook Read(string path) }`
  - `static class Validator { IReadOnlyList<string> Validate(LoadedCookBook cb) }` — returns human-readable problems (empty = valid).

- [ ] **Step 1: Create the branch**

```bash
git checkout -b feat/formats
```

- [ ] **Step 2: Write the failing tests**

`tests/Nfty.Core.Tests/ArchiveRoundTripTests.cs`:

```csharp
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

public class ArchiveRoundTripTests
{
    private static LoadedIngredient MakeIngredient(string id, Rgba32 fill) => new()
    {
        Manifest = new IngredientManifest(id, id, Sha256: ""),
        Image = new Image<Rgba32>(4, 4, fill),
    };

    [Fact]
    public void CookBook_round_trips_through_disk()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "VaporPets.cbk");

        var bg = new LoadedRecipe
        {
            Manifest = new RecipeManifest("bg", "Background", LayerKind.Static, 0,
                new Dictionary<string, double> { ["sunset"] = 55, ["grid"] = 45 }, null),
            Ingredients = new[]
            {
                MakeIngredient("sunset", new Rgba32(255, 128, 0, 255)),
                MakeIngredient("grid", new Rgba32(0, 128, 255, 255)),
            },
        };
        var cb = new CookBookManifest("cb", "VaporPets", new Dimensions(4, 4),
            new[] { "bg" }, Array.Empty<IncompatibilityRule>(),
            new Collection("VaporPets", "d", "VP"));

        CookBookArchive.Write(path, cb, new[] { bg });
        var loaded = CookBookArchive.Read(path);

        Assert.Equal("VaporPets", loaded.Manifest.Name);
        Assert.Equal(new Dimensions(4, 4), loaded.Manifest.Canvas);
        Assert.Single(loaded.Recipes);
        Assert.Equal(2, loaded.Recipes[0].Ingredients.Count);
        Assert.Equal(55, loaded.Recipes[0].Manifest.Measurements["sunset"]);
        Assert.Equal(new Rgba32(255, 128, 0, 255), loaded.Recipes[0].Ingredients[0].Image[0, 0]);
    }
}
```

`tests/Nfty.Core.Tests/ValidatorTests.cs`:

```csharp
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

public class ValidatorTests
{
    private static LoadedCookBook Book(int imgW, int imgH, double weight)
    {
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("a", "A", ""),
            Image = new Image<Rgba32>(imgW, imgH, new Rgba32(0, 0, 0, 255)),
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("bg", "Background", LayerKind.Static, 0,
                new Dictionary<string, double> { ["a"] = weight }, null),
            Ingredients = new[] { ing },
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(4, 4),
                new[] { "bg" }, Array.Empty<IncompatibilityRule>(), new Collection("B", "", "B")),
            Recipes = new[] { recipe },
        };
    }

    [Fact]
    public void Valid_book_has_no_problems() => Assert.Empty(Validator.Validate(Book(4, 4, 10)));

    [Fact]
    public void Wrong_dimensions_reported()
    {
        var problems = Validator.Validate(Book(8, 8, 10));
        Assert.Contains(problems, p => p.Contains("dimension", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Zero_total_weight_reported()
    {
        var problems = Validator.Validate(Book(4, 4, 0));
        Assert.Contains(problems, p => p.Contains("weight", StringComparison.OrdinalIgnoreCase));
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Nfty.Core.Tests --filter "ArchiveRoundTripTests|ValidatorTests"`
Expected: FAIL — `CookBookArchive`, `Validator`, etc. do not exist.

- [ ] **Step 4: Implement JSON options and loaded types**

`src/Nfty.Core/Formats/Json.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nfty.Core.Formats;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
```

`src/Nfty.Core/Formats/Loaded.cs`:

```csharp
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Formats;

public class LoadedIngredient
{
    public required IngredientManifest Manifest { get; init; }
    public required Image<Rgba32> Image { get; init; }
}

public class LoadedRecipe
{
    public required RecipeManifest Manifest { get; init; }
    public required IReadOnlyList<LoadedIngredient> Ingredients { get; init; }
}

public class LoadedCookBook
{
    public required CookBookManifest Manifest { get; init; }
    public required IReadOnlyList<LoadedRecipe> Recipes { get; init; }
}
```

- [ ] **Step 5: Implement ArchiveIo helpers**

`src/Nfty.Core/Formats/ArchiveIo.cs`:

```csharp
using System.IO.Compression;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Formats;

internal static class ArchiveIo
{
    public static void WriteManifest<T>(ZipArchive zip, T manifest)
    {
        var entry = zip.CreateEntry("manifest.json");
        using var s = entry.Open();
        JsonSerializer.Serialize(s, manifest, Json.Options);
    }

    public static T ReadManifest<T>(ZipArchive zip)
    {
        var entry = zip.GetEntry("manifest.json")
            ?? throw new InvalidDataException("Archive is missing manifest.json.");
        using var s = entry.Open();
        return JsonSerializer.Deserialize<T>(s, Json.Options)
            ?? throw new InvalidDataException("manifest.json deserialized to null.");
    }

    public static void WriteImage(ZipArchive zip, string name, Image<Rgba32> img)
    {
        var entry = zip.CreateEntry(name);
        using var s = entry.Open();
        img.Save(s, new PngEncoder());
    }

    public static Image<Rgba32> ReadImage(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name)
            ?? throw new InvalidDataException($"Archive is missing {name}.");
        using var s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        ms.Position = 0;
        return Image.Load<Rgba32>(ms);
    }

    public static void WriteNested(ZipArchive zip, string entryName, Action<ZipArchive> build)
    {
        using var ms = new MemoryStream();
        using (var inner = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            build(inner);
        ms.Position = 0;
        var entry = zip.CreateEntry(entryName);
        using var s = entry.Open();
        ms.CopyTo(s);
    }

    public static T ReadNested<T>(ZipArchive zip, string entryName, Func<ZipArchive, T> read)
    {
        var entry = zip.GetEntry(entryName)
            ?? throw new InvalidDataException($"Archive is missing {entryName}.");
        using var s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        ms.Position = 0;
        using var inner = new ZipArchive(ms, ZipArchiveMode.Read);
        return read(inner);
    }

    public static IEnumerable<string> EntryNamesUnder(ZipArchive zip, string prefix) =>
        zip.Entries.Where(e => e.FullName.StartsWith(prefix, StringComparison.Ordinal)
                            && e.FullName.Length > prefix.Length)
                   .Select(e => e.FullName);
}
```

- [ ] **Step 6: Implement the three archive types**

`src/Nfty.Core/Formats/IngredientArchive.cs`:

```csharp
using System.IO.Compression;
using Nfty.Core.Model;

namespace Nfty.Core.Formats;

public static class IngredientArchive
{
    public static void Write(ZipArchive zip, IngredientManifest manifest, SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32> image)
    {
        ArchiveIo.WriteManifest(zip, manifest);
        ArchiveIo.WriteImage(zip, "image.png", image);
    }

    public static LoadedIngredient Read(ZipArchive zip) => new()
    {
        Manifest = ArchiveIo.ReadManifest<IngredientManifest>(zip),
        Image = ArchiveIo.ReadImage(zip, "image.png"),
    };

    public static void Write(string path, IngredientManifest manifest, SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32> image)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Write(zip, manifest, image);
    }

    public static LoadedIngredient Read(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        return Read(zip);
    }
}
```

`src/Nfty.Core/Formats/RecipeArchive.cs`:

```csharp
using System.IO.Compression;
using Nfty.Core.Model;

namespace Nfty.Core.Formats;

public static class RecipeArchive
{
    public static void Write(ZipArchive zip, RecipeManifest manifest, IReadOnlyList<LoadedIngredient> ingredients)
    {
        ArchiveIo.WriteManifest(zip, manifest);
        foreach (var ing in ingredients)
            ArchiveIo.WriteNested(zip, $"ingredients/{ing.Manifest.Id}.igt",
                inner => IngredientArchive.Write(inner, ing.Manifest, ing.Image));
    }

    public static LoadedRecipe Read(ZipArchive zip)
    {
        var manifest = ArchiveIo.ReadManifest<RecipeManifest>(zip);
        var ingredients = ArchiveIo.EntryNamesUnder(zip, "ingredients/")
            .OrderBy(n => n, StringComparer.Ordinal)
            .Select(n => ArchiveIo.ReadNested(zip, n, IngredientArchive.Read))
            .ToList();
        return new LoadedRecipe { Manifest = manifest, Ingredients = ingredients };
    }

    public static void Write(string path, RecipeManifest manifest, IReadOnlyList<LoadedIngredient> ingredients)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Write(zip, manifest, ingredients);
    }

    public static LoadedRecipe Read(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        return Read(zip);
    }
}
```

`src/Nfty.Core/Formats/CookBookArchive.cs`:

```csharp
using System.IO.Compression;
using Nfty.Core.Model;

namespace Nfty.Core.Formats;

public static class CookBookArchive
{
    public static void Write(string path, CookBookManifest manifest, IReadOnlyList<LoadedRecipe> recipes)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        ArchiveIo.WriteManifest(zip, manifest);
        foreach (var r in recipes)
            ArchiveIo.WriteNested(zip, $"recipes/{r.Manifest.Id}.rcp",
                inner => RecipeArchive.Write(inner, r.Manifest, r.Ingredients));
    }

    public static LoadedCookBook Read(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var manifest = ArchiveIo.ReadManifest<CookBookManifest>(zip);
        var recipes = ArchiveIo.EntryNamesUnder(zip, "recipes/")
            .OrderBy(n => n, StringComparer.Ordinal)
            .Select(n => ArchiveIo.ReadNested(zip, n, RecipeArchive.Read))
            .ToList();
        return new LoadedCookBook { Manifest = manifest, Recipes = recipes };
    }
}
```

- [ ] **Step 7: Implement Validator**

`src/Nfty.Core/Formats/Validator.cs`:

```csharp
using Nfty.Core.Model;

namespace Nfty.Core.Formats;

public static class Validator
{
    public static IReadOnlyList<string> Validate(LoadedCookBook cb)
    {
        var problems = new List<string>();
        var canvas = cb.Manifest.Canvas;
        var recipeIds = cb.Recipes.Select(r => r.Manifest.Id).ToHashSet();

        foreach (var id in cb.Manifest.LayerOrder)
            if (!recipeIds.Contains(id))
                problems.Add($"layerOrder references unknown recipe '{id}'.");

        foreach (var r in cb.Recipes)
        {
            if (r.Manifest.Measurements.Values.Sum() <= 0)
                problems.Add($"Recipe '{r.Manifest.Id}' has zero total weight.");

            foreach (var ing in r.Ingredients)
            {
                if (ing.Image.Width != canvas.Width || ing.Image.Height != canvas.Height)
                    problems.Add(
                        $"Ingredient '{ing.Manifest.Id}' in '{r.Manifest.Id}' has dimensions "
                        + $"{ing.Image.Width}x{ing.Image.Height}, expected canvas {canvas.Width}x{canvas.Height}.");
                if (!r.Manifest.Measurements.ContainsKey(ing.Manifest.Id))
                    problems.Add($"Ingredient '{ing.Manifest.Id}' in '{r.Manifest.Id}' has no measurement.");
            }
        }

        foreach (var rule in cb.Manifest.Rules)
            foreach (var t in rule.Targets.Append(rule.When))
                if (!recipeIds.Contains(t.LayerId))
                    problems.Add($"Rule references unknown layer '{t.LayerId}'.");

        return problems;
    }
}
```

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test tests/Nfty.Core.Tests --filter "ArchiveRoundTripTests|ValidatorTests"`
Expected: PASS (4 tests).

- [ ] **Step 9: Commit and merge**

```bash
git add -A
git commit -m "feat: zip archive formats (.igt/.rcp/.cbk) and validator

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
git checkout master && git merge --no-ff feat/formats -m "Merge feat/formats"
```

---

## Task 7: Deterministic RNG, weighted roller, color roller

**Files:**
- Create: `src/Nfty.Core/Generation/Rng.cs`, `WeightedRoller.cs`, `ColorRoller.cs`
- Test: `tests/Nfty.Core.Tests/RngTests.cs`, `WeightedRollerTests.cs`, `ColorRollerTests.cs`

**Interfaces:**
- Consumes: `Nfty.Core.Model.Colorization/ColorModel/ColorEntry`, `Nfty.Core.Imaging.ColorSpec/ColorConvert`.
- Produces (namespace `Nfty.Core.Generation`):
  - `interface IRng { double NextDouble(); }`
  - `sealed class SplitMix64Rng(ulong seed) : IRng`
  - `static class SeedHash { ulong ToUlong(string seed) }`
  - `static class WeightedRoller { string Roll(IReadOnlyDictionary<string,double> weights, IRng rng) }` — iterates weights ordered by key.
  - `readonly record struct RolledColor(double H, double S)`
  - `static class ColorRoller { RolledColor Roll(Colorization c, IRng rng) }`

- [ ] **Step 1: Create the branch**

```bash
git checkout -b feat/rng-roll
```

- [ ] **Step 2: Write the failing tests**

`tests/Nfty.Core.Tests/RngTests.cs`:

```csharp
using Nfty.Core.Generation;

namespace Nfty.Core.Tests;

public class RngTests
{
    [Fact]
    public void Same_seed_same_sequence()
    {
        var a = new SplitMix64Rng(SeedHash.ToUlong("vapor"));
        var b = new SplitMix64Rng(SeedHash.ToUlong("vapor"));
        for (int i = 0; i < 10; i++) Assert.Equal(a.NextDouble(), b.NextDouble());
    }

    [Fact]
    public void Different_seed_different_sequence()
    {
        var a = new SplitMix64Rng(SeedHash.ToUlong("vapor"));
        var b = new SplitMix64Rng(SeedHash.ToUlong("soft"));
        Assert.NotEqual(a.NextDouble(), b.NextDouble());
    }

    [Fact]
    public void Output_in_unit_interval()
    {
        var r = new SplitMix64Rng(1);
        for (int i = 0; i < 1000; i++)
        {
            double d = r.NextDouble();
            Assert.InRange(d, 0.0, 1.0);
        }
    }
}
```

`tests/Nfty.Core.Tests/WeightedRollerTests.cs`:

```csharp
using Nfty.Core.Generation;

namespace Nfty.Core.Tests;

public class WeightedRollerTests
{
    [Fact]
    public void Distribution_matches_weights_within_tolerance()
    {
        var weights = new Dictionary<string, double> { ["a"] = 90, ["b"] = 10 };
        var rng = new SplitMix64Rng(42);
        int a = 0;
        for (int i = 0; i < 10000; i++)
            if (WeightedRoller.Roll(weights, rng) == "a") a++;

        Assert.InRange(a / 10000.0, 0.87, 0.93);
    }

    [Fact]
    public void Single_option_always_selected()
    {
        var weights = new Dictionary<string, double> { ["only"] = 5 };
        Assert.Equal("only", WeightedRoller.Roll(weights, new SplitMix64Rng(1)));
    }
}
```

`tests/Nfty.Core.Tests/ColorRollerTests.cs`:

```csharp
using Nfty.Core.Generation;
using Nfty.Core.Model;

namespace Nfty.Core.Tests;

public class ColorRollerTests
{
    [Fact]
    public void Fixed_entry_yields_its_hue_saturation()
    {
        var c = new Colorization(ColorModel.Hsv, 5, 5, new[]
        {
            new ColorEntry(1, null, "hsv:200,50,80"),
        });
        var rolled = ColorRoller.Roll(c, new SplitMix64Rng(7));
        Assert.InRange(rolled.H, 199.0, 201.0);
        Assert.InRange(rolled.S, 0.49, 0.51);
    }

    [Fact]
    public void Range_entry_samples_within_bounds()
    {
        var c = new Colorization(ColorModel.Hsv, 5, 5, new[]
        {
            new ColorEntry(1, new ColorRange(175, 195, 60, 90), null),
        });
        for (int i = 0; i < 200; i++)
        {
            var rolled = ColorRoller.Roll(c, new SplitMix64Rng((ulong)i));
            Assert.InRange(rolled.H, 175.0, 195.0);
            Assert.InRange(rolled.S, 0.60, 0.90);
        }
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Nfty.Core.Tests --filter "RngTests|WeightedRollerTests|ColorRollerTests"`
Expected: FAIL — types do not exist.

- [ ] **Step 4: Implement Rng**

`src/Nfty.Core/Generation/Rng.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace Nfty.Core.Generation;

public interface IRng
{
    double NextDouble();
}

public sealed class SplitMix64Rng : IRng
{
    private ulong _state;
    public SplitMix64Rng(ulong seed) => _state = seed;

    public double NextDouble()
    {
        _state += 0x9E3779B97F4A7C15UL;
        ulong z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return (z >> 11) * (1.0 / (1UL << 53)); // [0,1)
    }
}

public static class SeedHash
{
    public static ulong ToUlong(string seed)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return BitConverter.ToUInt64(hash, 0);
    }
}
```

- [ ] **Step 5: Implement WeightedRoller**

`src/Nfty.Core/Generation/WeightedRoller.cs`:

```csharp
namespace Nfty.Core.Generation;

public static class WeightedRoller
{
    public static string Roll(IReadOnlyDictionary<string, double> weights, IRng rng)
    {
        var ordered = weights.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();
        double total = ordered.Sum(kv => kv.Value);
        if (total <= 0) throw new InvalidOperationException("Total weight must be positive.");

        double r = rng.NextDouble() * total;
        double acc = 0;
        foreach (var kv in ordered)
        {
            acc += kv.Value;
            if (r < acc) return kv.Key;
        }
        return ordered[^1].Key;
    }
}
```

- [ ] **Step 6: Implement ColorRoller**

`src/Nfty.Core/Generation/ColorRoller.cs`:

```csharp
using Nfty.Core.Imaging;
using Nfty.Core.Model;

namespace Nfty.Core.Generation;

public readonly record struct RolledColor(double H, double S);

public static class ColorRoller
{
    public static RolledColor Roll(Colorization c, IRng rng)
    {
        var entry = PickEntry(c.Entries, rng);
        if (entry.Fixed is not null)
        {
            var rgb = ColorSpec.Parse(entry.Fixed);
            var (h, s) = c.Model == ColorModel.Hsv
                ? (ColorConvert.RgbToHsv(rgb).H, ColorConvert.RgbToHsv(rgb).S)
                : (ColorConvert.RgbToHsl(rgb).H, ColorConvert.RgbToHsl(rgb).S);
            return new RolledColor(h, s);
        }

        var range = entry.Range!;
        double hue = range.HueMin + rng.NextDouble() * (range.HueMax - range.HueMin);
        double sat = (range.SatMin + rng.NextDouble() * (range.SatMax - range.SatMin)) / 100.0;
        return new RolledColor(hue, sat);
    }

    private static ColorEntry PickEntry(IReadOnlyList<ColorEntry> entries, IRng rng)
    {
        double total = entries.Sum(e => e.Weight);
        if (total <= 0) throw new InvalidOperationException("Color entries have zero total weight.");
        double r = rng.NextDouble() * total, acc = 0;
        foreach (var e in entries)
        {
            acc += e.Weight;
            if (r < acc) return e;
        }
        return entries[^1];
    }
}
```

Note: `satRange` values are percentages (0–100) per the spec; a `fixed` color's saturation already comes back as 0–1 from `ColorConvert`.

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/Nfty.Core.Tests --filter "RngTests|WeightedRollerTests|ColorRollerTests"`
Expected: PASS (7 tests).

- [ ] **Step 8: Commit and merge**

```bash
git add -A
git commit -m "feat: deterministic RNG, weighted roller, color roller

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
git checkout master && git merge --no-ff feat/rng-roll -m "Merge feat/rng-roll"
```

---

## Task 8: DNA & dedup identity

**Files:**
- Create: `src/Nfty.Core/Generation/Dna.cs`
- Test: `tests/Nfty.Core.Tests/DnaTests.cs`

**Interfaces:**
- Consumes: nothing (self-contained value types).
- Produces (namespace `Nfty.Core.Generation`):
  - `readonly record struct LayerSelection(string LayerId, string IngredientId, double? Hue, double? Sat, int HueQuantize, int SatQuantize)` — `Hue/Sat` null for static layers.
  - `static class Dna { string Compute(IReadOnlyList<LayerSelection> selections) }` — order-independent (sorts by LayerId), color quantized before hashing, SHA-256 hex.

- [ ] **Step 1: Create the branch**

```bash
git checkout -b feat/dna
```

- [ ] **Step 2: Write the failing test**

`tests/Nfty.Core.Tests/DnaTests.cs`:

```csharp
using Nfty.Core.Generation;

namespace Nfty.Core.Tests;

public class DnaTests
{
    [Fact]
    public void Same_selection_same_dna()
    {
        var a = new[] { new LayerSelection("bg", "sunset", null, null, 5, 5) };
        var b = new[] { new LayerSelection("bg", "sunset", null, null, 5, 5) };
        Assert.Equal(Dna.Compute(a), Dna.Compute(b));
    }

    [Fact]
    public void Layer_order_does_not_change_dna()
    {
        var a = new[]
        {
            new LayerSelection("bg", "sunset", null, null, 5, 5),
            new LayerSelection("body", "cat", null, null, 5, 5),
        };
        var b = new[]
        {
            new LayerSelection("body", "cat", null, null, 5, 5),
            new LayerSelection("bg", "sunset", null, null, 5, 5),
        };
        Assert.Equal(Dna.Compute(a), Dna.Compute(b));
    }

    [Fact]
    public void Colors_in_same_quant_bucket_share_dna()
    {
        var a = new[] { new LayerSelection("aura", "glow", 181.0, 0.71, 5, 5) };
        var b = new[] { new LayerSelection("aura", "glow", 184.0, 0.73, 5, 5) };
        Assert.Equal(Dna.Compute(a), Dna.Compute(b)); // both bucket hue=36, sat=14
    }

    [Fact]
    public void Colors_in_different_buckets_differ()
    {
        var a = new[] { new LayerSelection("aura", "glow", 181.0, 0.71, 5, 5) };
        var b = new[] { new LayerSelection("aura", "glow", 200.0, 0.71, 5, 5) };
        Assert.NotEqual(Dna.Compute(a), Dna.Compute(b));
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/Nfty.Core.Tests --filter DnaTests`
Expected: FAIL — `Dna`/`LayerSelection` do not exist.

- [ ] **Step 4: Implement Dna**

`src/Nfty.Core/Generation/Dna.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace Nfty.Core.Generation;

public readonly record struct LayerSelection(
    string LayerId, string IngredientId, double? Hue, double? Sat, int HueQuantize, int SatQuantize);

public static class Dna
{
    public static string Compute(IReadOnlyList<LayerSelection> selections)
    {
        var sb = new StringBuilder();
        foreach (var s in selections.OrderBy(x => x.LayerId, StringComparer.Ordinal))
        {
            sb.Append(s.LayerId).Append('=').Append(s.IngredientId);
            if (s.Hue is double h && s.Sat is double sat)
            {
                long hb = (long)Math.Floor(h / Math.Max(1, s.HueQuantize));
                long sb2 = (long)Math.Floor(sat * 100.0 / Math.Max(1, s.SatQuantize));
                sb.Append('@').Append(hb).Append(',').Append(sb2);
            }
            sb.Append('|');
        }
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Nfty.Core.Tests --filter DnaTests`
Expected: PASS (4 tests).

- [ ] **Step 6: Commit and merge**

```bash
git add -A
git commit -m "feat: DNA computation with quantized color identity

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
git checkout master && git merge --no-ff feat/dna -m "Merge feat/dna"
```

---

## Task 9: Rules engine

**Files:**
- Create: `src/Nfty.Core/Generation/RulesEngine.cs`
- Test: `tests/Nfty.Core.Tests/RulesEngineTests.cs`

**Interfaces:**
- Consumes: `Nfty.Core.Model.IncompatibilityRule/RuleType/RuleTarget`.
- Produces (namespace `Nfty.Core.Generation`):
  - `static class RulesEngine { bool IsLegal(IReadOnlyDictionary<string,string> selection, IReadOnlyList<IncompatibilityRule> rules) }` — `selection` maps layerId→ingredientId.

- [ ] **Step 1: Create the branch**

```bash
git checkout -b feat/rules
```

- [ ] **Step 2: Write the failing test**

`tests/Nfty.Core.Tests/RulesEngineTests.cs`:

```csharp
using Nfty.Core.Generation;
using Nfty.Core.Model;

namespace Nfty.Core.Tests;

public class RulesEngineTests
{
    private static Dictionary<string, string> Sel(params (string, string)[] xs) =>
        xs.ToDictionary(x => x.Item1, x => x.Item2);

    [Fact]
    public void Exclude_rule_blocks_forbidden_pair()
    {
        var rules = new[]
        {
            new IncompatibilityRule(RuleType.Exclude,
                new RuleTarget("body", "fox"),
                new[] { new RuleTarget("hat", "visor") }),
        };
        Assert.False(RulesEngine.IsLegal(Sel(("body", "fox"), ("hat", "visor")), rules));
        Assert.True(RulesEngine.IsLegal(Sel(("body", "fox"), ("hat", "none")), rules));
        Assert.True(RulesEngine.IsLegal(Sel(("body", "cat"), ("hat", "visor")), rules));
    }

    [Fact]
    public void Require_rule_forces_pair()
    {
        var rules = new[]
        {
            new IncompatibilityRule(RuleType.Require,
                new RuleTarget("body", "robot"),
                new[] { new RuleTarget("eyes", "chrome") }),
        };
        Assert.True(RulesEngine.IsLegal(Sel(("body", "robot"), ("eyes", "chrome")), rules));
        Assert.False(RulesEngine.IsLegal(Sel(("body", "robot"), ("eyes", "sleepy")), rules));
        Assert.True(RulesEngine.IsLegal(Sel(("body", "cat"), ("eyes", "sleepy")), rules));
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/Nfty.Core.Tests --filter RulesEngineTests`
Expected: FAIL — `RulesEngine` does not exist.

- [ ] **Step 4: Implement RulesEngine**

`src/Nfty.Core/Generation/RulesEngine.cs`:

```csharp
using Nfty.Core.Model;

namespace Nfty.Core.Generation;

public static class RulesEngine
{
    public static bool IsLegal(
        IReadOnlyDictionary<string, string> selection,
        IReadOnlyList<IncompatibilityRule> rules)
    {
        foreach (var rule in rules)
        {
            bool whenMatches = selection.TryGetValue(rule.When.LayerId, out var chosen)
                               && chosen == rule.When.IngredientId;
            if (!whenMatches) continue;

            foreach (var target in rule.Targets)
            {
                bool targetPresent = selection.TryGetValue(target.LayerId, out var got)
                                     && got == target.IngredientId;
                if (rule.Type == RuleType.Exclude && targetPresent) return false;
                if (rule.Type == RuleType.Require && !targetPresent) return false;
            }
        }
        return true;
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Nfty.Core.Tests --filter RulesEngineTests`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit and merge**

```bash
git add -A
git commit -m "feat: incompatibility rules engine (exclude/require)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
git checkout master && git merge --no-ff feat/rules -m "Merge feat/rules"
```

---

## Task 10: Generator orchestrator

**Files:**
- Create: `src/Nfty.Core/Generation/GeneratedSet.cs`, `Generator.cs`
- Test: `tests/Nfty.Core.Tests/GeneratorTests.cs`

**Interfaces:**
- Consumes: `LoadedCookBook`, `Compositor`, `Colorizer`, `WeightedRoller`, `ColorRoller`, `Dna`, `RulesEngine`, `SplitMix64Rng`, `SeedHash`.
- Produces (namespace `Nfty.Core.Generation`):
  - `record TraitSelection(string LayerId, string LayerName, string IngredientId, string IngredientName)`
  - `record ColorRoll(string LayerId, ColorModel Model, double H, double S)`
  - `class GeneratedAsset { int SetNumber; string Dna; Image<Rgba32> Image; IReadOnlyList<TraitSelection> Traits; IReadOnlyList<ColorRoll> ColorRolls }`
  - `record GeneratedSet(string CollectionName, string Description, string Symbol, string Seed, IReadOnlyList<GeneratedAsset> Assets)`
  - `record GenerateOptions(int Count, string Seed, int MaxRerollsPerAsset = 10000)`
  - `static class Generator { GeneratedSet Generate(LoadedCookBook book, GenerateOptions opts, IReadOnlyList<string>? existingDnas = null, int startNumber = 1) }`
  - Throws `InvalidOperationException` when the unique space is exhausted before `Count`.

- [ ] **Step 1: Create the branch**

```bash
git checkout -b feat/generator
```

- [ ] **Step 2: Write the failing test**

`tests/Nfty.Core.Tests/GeneratorTests.cs`:

```csharp
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

public class GeneratorTests
{
    private static LoadedIngredient Ing(string id, Rgba32 fill) => new()
    {
        Manifest = new IngredientManifest(id, id, ""),
        Image = new Image<Rgba32>(2, 2, fill),
    };

    private static LoadedCookBook TwoLayerBook()
    {
        var bg = new LoadedRecipe
        {
            Manifest = new RecipeManifest("bg", "Background", LayerKind.Static, 0,
                new Dictionary<string, double> { ["a"] = 1, ["b"] = 1 }, null),
            Ingredients = new[] { Ing("a", new Rgba32(255, 0, 0, 255)), Ing("b", new Rgba32(0, 255, 0, 255)) },
        };
        var body = new LoadedRecipe
        {
            Manifest = new RecipeManifest("body", "Body", LayerKind.Static, 1,
                new Dictionary<string, double> { ["x"] = 1, ["y"] = 1 }, null),
            Ingredients = new[] { Ing("x", new Rgba32(0, 0, 255, 128)), Ing("y", new Rgba32(0, 0, 0, 0)) },
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "VaporPets", new Dimensions(2, 2),
                new[] { "bg", "body" }, Array.Empty<IncompatibilityRule>(), new Collection("VaporPets", "d", "VP")),
            Recipes = new[] { bg, body },
        };
    }

    [Fact]
    public void Same_seed_reproduces_identical_dna_sequence()
    {
        var opts = new GenerateOptions(3, "seed-1");
        var a = Generator.Generate(TwoLayerBook(), opts).Assets.Select(x => x.Dna);
        var b = Generator.Generate(TwoLayerBook(), opts).Assets.Select(x => x.Dna);
        Assert.Equal(a, b);
    }

    [Fact]
    public void All_dna_unique()
    {
        var set = Generator.Generate(TwoLayerBook(), new GenerateOptions(4, "seed-1"));
        Assert.Equal(4, set.Assets.Select(x => x.Dna).Distinct().Count());
    }

    [Fact]
    public void Exhausted_space_throws()
    {
        // 2 x 2 = 4 unique combos; asking for 5 must fail.
        Assert.Throws<InvalidOperationException>(
            () => Generator.Generate(TwoLayerBook(), new GenerateOptions(5, "seed-1")));
    }

    [Fact]
    public void Numbering_starts_at_one_and_is_sequential()
    {
        var set = Generator.Generate(TwoLayerBook(), new GenerateOptions(3, "seed-1"));
        Assert.Equal(new[] { 1, 2, 3 }, set.Assets.Select(a => a.SetNumber));
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/Nfty.Core.Tests --filter GeneratorTests`
Expected: FAIL — `Generator` does not exist.

- [ ] **Step 4: Implement GeneratedSet types**

`src/Nfty.Core/Generation/GeneratedSet.cs`:

```csharp
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Generation;

public record TraitSelection(string LayerId, string LayerName, string IngredientId, string IngredientName);

public record ColorRoll(string LayerId, ColorModel Model, double H, double S);

public class GeneratedAsset
{
    public required int SetNumber { get; init; }
    public required string Dna { get; init; }
    public required Image<Rgba32> Image { get; init; }
    public required IReadOnlyList<TraitSelection> Traits { get; init; }
    public required IReadOnlyList<ColorRoll> ColorRolls { get; init; }
}

public record GeneratedSet(
    string CollectionName, string Description, string Symbol, string Seed,
    IReadOnlyList<GeneratedAsset> Assets);

public record GenerateOptions(int Count, string Seed, int MaxRerollsPerAsset = 10000);
```

- [ ] **Step 5: Implement Generator**

`src/Nfty.Core/Generation/Generator.cs`:

```csharp
using Nfty.Core.Formats;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Generation;

public static class Generator
{
    public static GeneratedSet Generate(
        LoadedCookBook book,
        GenerateOptions opts,
        IReadOnlyList<string>? existingDnas = null,
        int startNumber = 1)
    {
        var problems = Validator.Validate(book);
        if (problems.Count > 0)
            throw new InvalidOperationException("Invalid cookbook:\n" + string.Join("\n", problems));

        var layers = book.Recipes
            .OrderBy(r => book.Manifest.LayerOrder.ToList().IndexOf(r.Manifest.Id))
            .ToList();

        var rng = new SplitMix64Rng(SeedHash.ToUlong(opts.Seed));
        var seen = new HashSet<string>(existingDnas ?? Array.Empty<string>());
        var assets = new List<GeneratedAsset>();
        int number = startNumber;

        for (int i = 0; i < opts.Count; i++)
        {
            GeneratedAsset? asset = null;
            for (int attempt = 0; attempt < opts.MaxRerollsPerAsset; attempt++)
            {
                var candidate = RollOne(book, layers, rng, number);
                if (candidate is null) continue;          // rule violation → reroll
                if (seen.Add(candidate.Dna))
                {
                    asset = candidate;
                    break;
                }
                candidate.Image.Dispose();                // duplicate → discard
            }

            if (asset is null)
                throw new InvalidOperationException(
                    $"Could not produce a unique asset after {opts.MaxRerollsPerAsset} attempts; "
                    + $"generated {assets.Count} of {opts.Count}. The unique/legal space is likely exhausted.");

            assets.Add(asset);
            number++;
        }

        return new GeneratedSet(
            book.Manifest.Collection.Name,
            book.Manifest.Collection.Description,
            book.Manifest.Collection.Symbol,
            opts.Seed,
            assets);
    }

    private static GeneratedAsset? RollOne(
        LoadedCookBook book, IReadOnlyList<LoadedRecipe> layers, IRng rng, int number)
    {
        var selection = new Dictionary<string, string>();
        var traits = new List<TraitSelection>();
        var colorRolls = new List<ColorRoll>();
        var dnaParts = new List<LayerSelection>();
        var images = new List<Image<Rgba32>>();

        foreach (var layer in layers)
        {
            string ingId = WeightedRoller.Roll(layer.Manifest.Measurements, rng);
            selection[layer.Manifest.Id] = ingId;
            var ing = layer.Ingredients.First(x => x.Manifest.Id == ingId);
            traits.Add(new TraitSelection(layer.Manifest.Id, layer.Manifest.Name, ingId, ing.Manifest.Name));

            if (layer.Manifest.Kind == LayerKind.Dynamic && layer.Manifest.Colorization is { } col)
            {
                var rolled = ColorRoller.Roll(col, rng);
                colorRolls.Add(new ColorRoll(layer.Manifest.Id, col.Model, rolled.H, rolled.S));
                images.Add(Colorizer.Apply(ing.Image, rolled.H, rolled.S, col.Model));
                dnaParts.Add(new LayerSelection(layer.Manifest.Id, ingId, rolled.H, rolled.S,
                    col.HueQuantize, col.SatQuantize));
            }
            else
            {
                images.Add(ing.Image.Clone());
                dnaParts.Add(new LayerSelection(layer.Manifest.Id, ingId, null, null, 1, 1));
            }
        }

        if (!RulesEngine.IsLegal(selection, book.Manifest.Rules))
        {
            foreach (var img in images) img.Dispose();
            return null;
        }

        var composed = Compositor.Composite(book.Manifest.Canvas, images);
        foreach (var img in images) img.Dispose();

        return new GeneratedAsset
        {
            SetNumber = number,
            Dna = Dna.Compute(dnaParts),
            Image = composed,
            Traits = traits,
            ColorRolls = colorRolls,
        };
    }
}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/Nfty.Core.Tests --filter GeneratorTests`
Expected: PASS (4 tests).

- [ ] **Step 7: Commit and merge**

```bash
git add -A
git commit -m "feat: generation orchestrator (roll/rules/colorize/composite/dedup)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
git checkout master && git merge --no-ff feat/generator -m "Merge feat/generator"
```

---

## Task 11: Set output, ERC-721 metadata, and extend

**Files:**
- Create: `src/Nfty.Core/Output/Metadata.cs`, `SetWriter.cs`
- Test: `tests/Nfty.Core.Tests/SetWriterTests.cs`

**Interfaces:**
- Consumes: `GeneratedSet`, `GeneratedAsset`, ImageSharp, `Json`.
- Produces (namespace `Nfty.Core.Output`):
  - `record MetadataAttribute(string Trait_type, string Value)` (serialized as `trait_type`/`value`).
  - `record RarityAttribute(string Trait_type, string Value, double RarityPct)`
  - `record ColorRollDto(string Layer, string Model, double H, double S)`
  - `record ItemMetadata(string Name, string Description, string Image, IReadOnlyList<MetadataAttribute> Attributes, int SetNumber, string Dna, string Seed, IReadOnlyList<RarityAttribute> Rarity, IReadOnlyList<ColorRollDto> ColorRolls)`
  - `record SetManifest(string Name, int Count, string Seed, string GeneratorVersion, IReadOnlyList<RarityAttribute> Rarity)`
  - `static class SetWriter`:
    - `void Write(GeneratedSet set, string outDir, bool pack)`
    - `record ExistingSet(IReadOnlyList<string> Dnas, int NextNumber)`
    - `ExistingSet ReadExisting(string outDir)` — reads `metadata/*.json`, returns DNAs + max(setNumber)+1.

- [ ] **Step 1: Create the branch**

```bash
git checkout -b feat/set-output
```

- [ ] **Step 2: Write the failing test**

`tests/Nfty.Core.Tests/SetWriterTests.cs`:

```csharp
using System.Text.Json;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

public class SetWriterTests
{
    private static GeneratedSet MakeSet() => new(
        "VaporPets", "desc", "VP", "seed-1",
        new[]
        {
            new GeneratedAsset
            {
                SetNumber = 1, Dna = "abc",
                Image = new Image<Rgba32>(2, 2, new Rgba32(1, 2, 3, 255)),
                Traits = new[] { new TraitSelection("bg", "Background", "sunset", "Sunset") },
                ColorRolls = Array.Empty<ColorRoll>(),
            },
        });

    [Fact]
    public void Writes_images_metadata_and_set_manifest()
    {
        var dir = Path.Combine(Directory.CreateTempSubdirectory().FullName, "out");
        SetWriter.Write(MakeSet(), dir, pack: false);

        Assert.True(File.Exists(Path.Combine(dir, "images", "0001.png")));
        Assert.True(File.Exists(Path.Combine(dir, "set.json")));

        var json = File.ReadAllText(Path.Combine(dir, "metadata", "0001.json"));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("VaporPets #1", doc.RootElement.GetProperty("name").GetString());
        Assert.Equal("Background", doc.RootElement.GetProperty("attributes")[0].GetProperty("trait_type").GetString());
        Assert.Equal("abc", doc.RootElement.GetProperty("dna").GetString());
    }

    [Fact]
    public void ReadExisting_recovers_dnas_and_next_number()
    {
        var dir = Path.Combine(Directory.CreateTempSubdirectory().FullName, "out");
        SetWriter.Write(MakeSet(), dir, pack: false);

        var existing = SetWriter.ReadExisting(dir);
        Assert.Contains("abc", existing.Dnas);
        Assert.Equal(2, existing.NextNumber);
    }

    [Fact]
    public void Pack_produces_a_set_archive()
    {
        var dir = Path.Combine(Directory.CreateTempSubdirectory().FullName, "out");
        SetWriter.Write(MakeSet(), dir, pack: true);
        Assert.True(File.Exists(dir + ".set"));
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/Nfty.Core.Tests --filter SetWriterTests`
Expected: FAIL — `SetWriter`/`Metadata` do not exist.

- [ ] **Step 4: Implement Metadata DTOs**

`src/Nfty.Core/Output/Metadata.cs`:

```csharp
namespace Nfty.Core.Output;

public record MetadataAttribute(string Trait_type, string Value);

public record RarityAttribute(string Trait_type, string Value, double RarityPct);

public record ColorRollDto(string Layer, string Model, double H, double S);

public record ItemMetadata(
    string Name,
    string Description,
    string Image,
    IReadOnlyList<MetadataAttribute> Attributes,
    int SetNumber,
    string Dna,
    string Seed,
    IReadOnlyList<RarityAttribute> Rarity,
    IReadOnlyList<ColorRollDto> ColorRolls);

public record SetManifest(
    string Name,
    int Count,
    string Seed,
    string GeneratorVersion,
    IReadOnlyList<RarityAttribute> Rarity);
```

Note: property names `Trait_type` serialize to `trait_type` under camelCase policy (the policy lowercases the first segment only, preserving the underscore), matching ERC-721.

- [ ] **Step 5: Implement SetWriter**

`src/Nfty.Core/Output/SetWriter.cs`:

```csharp
using System.IO.Compression;
using System.Text.Json;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Output;

public static class SetWriter
{
    public const string GeneratorVersion = "nfty/1.0";

    public record ExistingSet(IReadOnlyList<string> Dnas, int NextNumber);

    public static void Write(GeneratedSet set, string outDir, bool pack)
    {
        Directory.CreateDirectory(Path.Combine(outDir, "images"));
        Directory.CreateDirectory(Path.Combine(outDir, "metadata"));

        // Aggregate rarity across the produced set (actual observed frequencies).
        var counts = new Dictionary<(string, string), int>();
        foreach (var a in set.Assets)
            foreach (var t in a.Traits)
                counts[(t.LayerName, t.IngredientName)] =
                    counts.GetValueOrDefault((t.LayerName, t.IngredientName)) + 1;

        double n = Math.Max(1, set.Assets.Count);
        RarityAttribute Rar(string layer, string value) =>
            new(layer, value, Math.Round(counts.GetValueOrDefault((layer, value)) / n * 100, 2));

        foreach (var a in set.Assets)
        {
            string stem = a.SetNumber.ToString("D4");
            a.Image.Save(Path.Combine(outDir, "images", $"{stem}.png"), new PngEncoder());

            var meta = new ItemMetadata(
                Name: $"{set.CollectionName} #{a.SetNumber}",
                Description: set.Description,
                Image: $"images/{stem}.png",
                Attributes: a.Traits.Select(t => new MetadataAttribute(t.LayerName, t.IngredientName)).ToList(),
                SetNumber: a.SetNumber,
                Dna: a.Dna,
                Seed: set.Seed,
                Rarity: a.Traits.Select(t => Rar(t.LayerName, t.IngredientName)).ToList(),
                ColorRolls: a.ColorRolls
                    .Select(c => new ColorRollDto(c.LayerId, c.Model.ToString().ToLowerInvariant(),
                        Math.Round(c.H, 1), Math.Round(c.S, 3)))
                    .ToList());

            File.WriteAllText(Path.Combine(outDir, "metadata", $"{stem}.json"),
                JsonSerializer.Serialize(meta, Json.Options));
        }

        var rarityTable = counts.Keys
            .Select(k => Rar(k.Item1, k.Item2))
            .OrderBy(r => r.Trait_type).ThenBy(r => r.Value).ToList();
        var setManifest = new SetManifest(set.CollectionName, set.Assets.Count, set.Seed,
            GeneratorVersion, rarityTable);
        File.WriteAllText(Path.Combine(outDir, "set.json"),
            JsonSerializer.Serialize(setManifest, Json.Options));

        if (pack)
        {
            string archivePath = outDir + ".set";
            if (File.Exists(archivePath)) File.Delete(archivePath);
            ZipFile.CreateFromDirectory(outDir, archivePath);
        }
    }

    public static ExistingSet ReadExisting(string outDir)
    {
        var metaDir = Path.Combine(outDir, "metadata");
        if (!Directory.Exists(metaDir))
            return new ExistingSet(Array.Empty<string>(), 1);

        var dnas = new List<string>();
        int maxNumber = 0;
        foreach (var file in Directory.EnumerateFiles(metaDir, "*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            dnas.Add(doc.RootElement.GetProperty("dna").GetString()!);
            maxNumber = Math.Max(maxNumber, doc.RootElement.GetProperty("setNumber").GetInt32());
        }
        return new ExistingSet(dnas, maxNumber + 1);
    }
}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/Nfty.Core.Tests --filter SetWriterTests`
Expected: PASS (3 tests).

- [ ] **Step 7: Add and run the extend round-trip test**

Append to `tests/Nfty.Core.Tests/SetWriterTests.cs` (inside the class):

```csharp
    [Fact]
    public void Extend_preserves_existing_and_appends_new()
    {
        // Build a 4-combo book, generate 2, then extend to 4.
        LoadedIngredient Ing(string id, Rgba32 f) => new()
        { Manifest = new IngredientManifest(id, id, ""), Image = new Image<Rgba32>(2, 2, f) };

        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "VP", new Dimensions(2, 2),
                new[] { "bg", "body" }, Array.Empty<IncompatibilityRule>(), new Collection("VP", "", "VP")),
            Recipes = new[]
            {
                new LoadedRecipe { Manifest = new RecipeManifest("bg", "BG", LayerKind.Static, 0,
                    new Dictionary<string, double>{["a"]=1,["b"]=1}, null),
                    Ingredients = new[]{ Ing("a", new Rgba32(255,0,0,255)), Ing("b", new Rgba32(0,255,0,255)) } },
                new LoadedRecipe { Manifest = new RecipeManifest("body", "Body", LayerKind.Static, 1,
                    new Dictionary<string, double>{["x"]=1,["y"]=1}, null),
                    Ingredients = new[]{ Ing("x", new Rgba32(0,0,255,255)), Ing("y", new Rgba32(0,0,0,0)) } },
            },
        };

        var dir = Path.Combine(Directory.CreateTempSubdirectory().FullName, "out");
        SetWriter.Write(Generator.Generate(book, new GenerateOptions(2, "s")), dir, pack: false);

        var existing = SetWriter.ReadExisting(dir);
        var more = Generator.Generate(book, new GenerateOptions(2, "s2"),
            existingDnas: existing.Dnas, startNumber: existing.NextNumber);
        SetWriter.Write(more, dir, pack: false);

        var all = Directory.GetFiles(Path.Combine(dir, "images"), "*.png").Select(Path.GetFileName).OrderBy(x => x);
        Assert.Equal(new[] { "0001.png", "0002.png", "0003.png", "0004.png" }, all);
    }
```

Run: `dotnet test tests/Nfty.Core.Tests --filter SetWriterTests`
Expected: PASS (4 tests).

- [ ] **Step 8: Commit and merge**

```bash
git add -A
git commit -m "feat: set output, ERC-721 metadata, and extend support

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
git checkout master && git merge --no-ff feat/set-output -m "Merge feat/set-output"
```

---

## Task 12: Rarity stats (theoretical odds)

**Files:**
- Create: `src/Nfty.Core/Stats/RarityCalculator.cs`
- Test: `tests/Nfty.Core.Tests/RarityCalculatorTests.cs`

**Interfaces:**
- Consumes: `LoadedCookBook`.
- Produces (namespace `Nfty.Core.Stats`):
  - `record TraitOdds(string LayerId, string LayerName, string IngredientId, string IngredientName, double Percent)`
  - `static class RarityCalculator { IReadOnlyList<TraitOdds> Compute(LoadedCookBook book) }` — per-trait theoretical probability = weight / layer total × 100.

- [ ] **Step 1: Create the branch**

```bash
git checkout -b feat/stats
```

- [ ] **Step 2: Write the failing test**

`tests/Nfty.Core.Tests/RarityCalculatorTests.cs`:

```csharp
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Nfty.Core.Stats;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

public class RarityCalculatorTests
{
    [Fact]
    public void Percent_is_weight_over_layer_total()
    {
        LoadedIngredient Ing(string id) => new()
        { Manifest = new IngredientManifest(id, id, ""), Image = new Image<Rgba32>(1, 1) };

        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "B", new Dimensions(1, 1),
                new[] { "bg" }, Array.Empty<IncompatibilityRule>(), new Collection("B", "", "B")),
            Recipes = new[]
            {
                new LoadedRecipe { Manifest = new RecipeManifest("bg", "BG", LayerKind.Static, 0,
                    new Dictionary<string, double> { ["a"] = 75, ["b"] = 25 }, null),
                    Ingredients = new[] { Ing("a"), Ing("b") } },
            },
        };

        var odds = RarityCalculator.Compute(book);
        Assert.Equal(75.0, odds.Single(o => o.IngredientId == "a").Percent);
        Assert.Equal(25.0, odds.Single(o => o.IngredientId == "b").Percent);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/Nfty.Core.Tests --filter RarityCalculatorTests`
Expected: FAIL — `RarityCalculator` does not exist.

- [ ] **Step 4: Implement RarityCalculator**

`src/Nfty.Core/Stats/RarityCalculator.cs`:

```csharp
using Nfty.Core.Formats;

namespace Nfty.Core.Stats;

public record TraitOdds(
    string LayerId, string LayerName, string IngredientId, string IngredientName, double Percent);

public static class RarityCalculator
{
    public static IReadOnlyList<TraitOdds> Compute(LoadedCookBook book)
    {
        var result = new List<TraitOdds>();
        foreach (var r in book.Recipes)
        {
            double total = r.Manifest.Measurements.Values.Sum();
            foreach (var ing in r.Ingredients)
            {
                double w = r.Manifest.Measurements.GetValueOrDefault(ing.Manifest.Id);
                double pct = total > 0 ? Math.Round(w / total * 100, 2) : 0;
                result.Add(new TraitOdds(r.Manifest.Id, r.Manifest.Name,
                    ing.Manifest.Id, ing.Manifest.Name, pct));
            }
        }
        return result;
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Nfty.Core.Tests --filter RarityCalculatorTests`
Expected: PASS (1 test).

- [ ] **Step 6: Commit and merge**

```bash
git add -A
git commit -m "feat: theoretical rarity calculator

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
git checkout master && git merge --no-ff feat/stats -m "Merge feat/stats"
```

---

## Task 13: CLI wiring

**Files:**
- Create: `src/Nfty.Cli/CommandFactory.cs`; Modify: `src/Nfty.Cli/Program.cs`
- Test: `tests/Nfty.Cli.Tests/CommandFactoryTests.cs`

**Interfaces:**
- Consumes: all of `Nfty.Core` (`CookBookArchive`, `Generator`, `SetWriter`, `RarityCalculator`, `Validator`, `Colorizer`, `ColorSpec`).
- Produces:
  - `static class CommandFactory { RootCommand Build() }` — commands: `inspect`, `validate`, `stats`, `preview`, `generate`, `extend`. (`new`/`add` authoring commands are covered by a follow-up; `generate`/`extend`/`stats`/`validate`/`inspect`/`preview` are the read/produce surface exercised here.)
  - `Program.Main` returns `CommandFactory.Build().Parse(args).Invoke()`.

Note: This task wires the CLI over already-tested Core logic. The test invokes the parser in-process and asserts on exit codes / stdout, so it does not depend on Core internals beyond the public API. If the installed `System.CommandLine` 2.0.9 surface differs from the calls below (e.g. `SetAction` overloads), consult `dotnet` IntelliSense for the exact method names — the command tree and handler bodies stay the same.

- [ ] **Step 1: Create the branch**

```bash
git checkout -b feat/cli
```

- [ ] **Step 2: Write the failing test**

`tests/Nfty.Cli.Tests/CommandFactoryTests.cs`:

```csharp
using Nfty.Cli;

namespace Nfty.Cli.Tests;

public class CommandFactoryTests
{
    [Fact]
    public void Root_has_expected_subcommands()
    {
        var root = CommandFactory.Build();
        var names = root.Subcommands.Select(c => c.Name).ToHashSet();
        foreach (var expected in new[] { "inspect", "validate", "stats", "preview", "generate", "extend" })
            Assert.Contains(expected, names);
    }

    [Fact]
    public void Unknown_command_is_a_parse_error()
    {
        var result = CommandFactory.Build().Parse("bogus-command");
        Assert.NotEmpty(result.Errors);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/Nfty.Cli.Tests --filter CommandFactoryTests`
Expected: FAIL — `CommandFactory` does not exist.

- [ ] **Step 4: Implement CommandFactory**

`src/Nfty.Cli/CommandFactory.cs`:

```csharp
using System.CommandLine;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Imaging;
using Nfty.Core.Output;
using Nfty.Core.Stats;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Cli;

public static class CommandFactory
{
    public static RootCommand Build()
    {
        var root = new RootCommand("nfty — layered NFT asset generator");

        root.Subcommands.Add(Inspect());
        root.Subcommands.Add(Validate());
        root.Subcommands.Add(Stats());
        root.Subcommands.Add(Preview());
        root.Subcommands.Add(Generate());
        root.Subcommands.Add(Extend());
        return root;
    }

    private static Command Inspect()
    {
        var path = new Argument<string>("cookbook") { Description = "Path to a .cbk file" };
        var cmd = new Command("inspect", "Print the tree of a cookbook") { path };
        cmd.SetAction(parse =>
        {
            var cb = CookBookArchive.Read(parse.GetValue(path)!);
            Console.WriteLine($"CookBook: {cb.Manifest.Name} ({cb.Manifest.Canvas.Width}x{cb.Manifest.Canvas.Height})");
            foreach (var id in cb.Manifest.LayerOrder)
            {
                var r = cb.Recipes.First(x => x.Manifest.Id == id);
                Console.WriteLine($"  Recipe: {r.Manifest.Name} [{r.Manifest.Kind}]");
                foreach (var ing in r.Ingredients)
                    Console.WriteLine($"    Ingredient: {ing.Manifest.Name} (w={r.Manifest.Measurements.GetValueOrDefault(ing.Manifest.Id)})");
            }
            return 0;
        });
        return cmd;
    }

    private static Command Validate()
    {
        var path = new Argument<string>("cookbook") { Description = "Path to a .cbk file" };
        var cmd = new Command("validate", "Validate a cookbook") { path };
        cmd.SetAction(parse =>
        {
            var problems = Validator.Validate(CookBookArchive.Read(parse.GetValue(path)!));
            if (problems.Count == 0) { Console.WriteLine("OK — no problems."); return 0; }
            foreach (var p in problems) Console.Error.WriteLine(p);
            return 1;
        });
        return cmd;
    }

    private static Command Stats()
    {
        var path = new Argument<string>("cookbook") { Description = "Path to a .cbk file" };
        var cmd = new Command("stats", "Show rarity breakdown") { path };
        cmd.SetAction(parse =>
        {
            foreach (var o in RarityCalculator.Compute(CookBookArchive.Read(parse.GetValue(path)!)))
                Console.WriteLine($"{o.LayerName,-16} {o.IngredientName,-16} {o.Percent,6:0.00}%");
            return 0;
        });
        return cmd;
    }

    private static Command Preview()
    {
        var path = new Argument<string>("ingredient") { Description = "Path to a .igt value-map" };
        var color = new Option<string>("--color") { Description = "Color spec, e.g. hsv:200,70,80", Required = true };
        var model = new Option<string>("--model") { Description = "hsv or hsl", DefaultValueFactory = _ => "hsv" };
        var outp = new Option<string>("--out") { Description = "Output PNG path", DefaultValueFactory = _ => "preview.png" };
        var cmd = new Command("preview", "Render a value-map with a chosen color") { path, color, model, outp };
        cmd.SetAction(parse =>
        {
            var ing = IngredientArchive.Read(parse.GetValue(path)!);
            var rgb = ColorSpec.Parse(parse.GetValue(color)!);
            var m = parse.GetValue(model)!.Equals("hsl", StringComparison.OrdinalIgnoreCase)
                ? Nfty.Core.Model.ColorModel.Hsl : Nfty.Core.Model.ColorModel.Hsv;
            var (h, s) = m == Nfty.Core.Model.ColorModel.Hsv
                ? (ColorConvert.RgbToHsv(rgb).H, ColorConvert.RgbToHsv(rgb).S)
                : (ColorConvert.RgbToHsl(rgb).H, ColorConvert.RgbToHsl(rgb).S);
            using var img = Colorizer.Apply(ing.Image, h, s, m);
            img.Save(parse.GetValue(outp)!, new PngEncoder());
            Console.WriteLine($"Wrote {parse.GetValue(outp)}");
            return 0;
        });
        return cmd;
    }

    private static Command Generate()
    {
        var path = new Argument<string>("cookbook") { Description = "Path to a .cbk file" };
        var count = new Option<int>("--count") { Description = "How many to generate", Required = true };
        var seed = new Option<string>("--seed") { Description = "RNG seed", DefaultValueFactory = _ => "nfty" };
        var outDir = new Option<string>("--out") { Description = "Output directory", Required = true };
        var pack = new Option<bool>("--pack") { Description = "Also produce a .set archive" };
        var cmd = new Command("generate", "Generate a set") { path, count, seed, outDir, pack };
        cmd.SetAction(parse =>
        {
            var book = CookBookArchive.Read(parse.GetValue(path)!);
            var set = Generator.Generate(book, new GenerateOptions(parse.GetValue(count), parse.GetValue(seed)!));
            SetWriter.Write(set, parse.GetValue(outDir)!, parse.GetValue(pack));
            Console.WriteLine($"Generated {set.Assets.Count} → {parse.GetValue(outDir)}");
            return 0;
        });
        return cmd;
    }

    private static Command Extend()
    {
        var path = new Argument<string>("cookbook") { Description = "Path to a .cbk file" };
        var dir = new Argument<string>("set-dir") { Description = "Existing set directory" };
        var to = new Option<int>("--to") { Description = "Target total count", Required = true };
        var seed = new Option<string>("--seed") { Description = "RNG seed", DefaultValueFactory = _ => "nfty-extend" };
        var cmd = new Command("extend", "Grow an existing set to a new count") { path, dir, to, seed };
        cmd.SetAction(parse =>
        {
            var book = CookBookArchive.Read(parse.GetValue(path)!);
            var existing = SetWriter.ReadExisting(parse.GetValue(dir)!);
            int have = existing.NextNumber - 1;
            int need = parse.GetValue(to) - have;
            if (need <= 0) { Console.WriteLine($"Already at {have}."); return 0; }
            var more = Generator.Generate(book, new GenerateOptions(need, parse.GetValue(seed)!),
                existing.Dnas, existing.NextNumber);
            SetWriter.Write(more, parse.GetValue(dir)!, pack: false);
            Console.WriteLine($"Extended by {need} → {parse.GetValue(to)} total.");
            return 0;
        });
        return cmd;
    }
}
```

- [ ] **Step 5: Wire Program.cs**

`src/Nfty.Cli/Program.cs`:

```csharp
using Nfty.Cli;

return CommandFactory.Build().Parse(args).Invoke();
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/Nfty.Cli.Tests --filter CommandFactoryTests`
Expected: PASS (2 tests).

- [ ] **Step 7: Full solution build + test**

Run: `dotnet build && dotnet test`
Expected: all projects build; all tests pass.

- [ ] **Step 8: Manual smoke check (optional but recommended)**

Run: `dotnet run --project src/Nfty.Cli -- --help`
Expected: usage lists `inspect`, `validate`, `stats`, `preview`, `generate`, `extend`.

- [ ] **Step 9: Commit and merge**

```bash
git add -A
git commit -m "feat: nfty CLI (inspect/validate/stats/preview/generate/extend)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
git checkout master && git merge --no-ff feat/cli -m "Merge feat/cli"
```

---

## Deferred to a follow-up plan
- Authoring commands `new` (scaffold empty `.cbk`/`.rcp`/`.igt`) and `add ingredient` (append an image variant with dimension validation). The formats and validator already support these; they are a thin CLI layer added once the read/generate surface is proven end-to-end.
- The Avalonia GUI (separate sub-project per the spec).

---

## Self-Review

**Spec coverage:**
- §2 domain model → Task 2. §3/§4 formats (ZIP + manifest, versioned, `.igt`/`.rcp`/`.cbk`) → Task 6. §4.5 color-spec syntax → Task 3. Dynamic colorization math (HSV/HSL, g→V/L, alpha) → Task 4. Compositing → Task 5. Weighted roll → Task 7. Color roll (weighted fixed/range) → Task 7. DNA + dedup (quantized color) → Task 8. Incompatibility rules → Task 9. Deterministic seed + generation pipeline + exhausted-space error → Task 10. Set output + ERC-721 metadata + extend → Task 11. Rarity stats → Task 12. Canvas-only dimension validation → Task 6 (`Validator`). CLI surface → Task 13. `new`/`add` authoring commands → explicitly deferred (noted above), formats/validation already present.
- Every spec section maps to a task; the only spec-listed CLI verbs not built here (`new`, `add`) are called out as a deferred thin follow-up, not a gap in the engine.

**Placeholder scan:** No "TBD"/"handle edge cases"/"similar to Task N" — every step carries complete code and exact commands. The one caveat (System.CommandLine API surface) is explicit and actionable, not a placeholder.

**Type consistency:** `LoadedIngredient/Recipe/CookBook` (Task 6) are consumed unchanged by Tasks 10–13. `IRng`/`SplitMix64Rng`/`SeedHash` (Task 7) used by Tasks 8/10. `LayerSelection`/`Dna.Compute` (Task 8) match Generator usage (Task 10). `GeneratedSet`/`GeneratedAsset`/`TraitSelection`/`ColorRoll` (Task 10) match `SetWriter` (Task 11). `RulesEngine.IsLegal(IReadOnlyDictionary<string,string>, …)` (Task 9) matches Generator's `selection` dictionary (Task 10). Color-roll saturation convention (0–1) is consistent between `ColorRoller` (Task 7) and `Colorizer` (Task 4).
