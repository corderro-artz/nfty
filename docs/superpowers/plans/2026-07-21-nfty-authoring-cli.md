# nfty Authoring CLI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add import-based authoring commands (`new` ×3, `add` ×3) to `Nfty.Cli` that build and mutate `.igt`/`.rcp`/`.cbk` archives from manifest JSON plus PNG files, exposing the Core write path through the CLI.

**Architecture:** Bottom-up per-level commands over the existing `IngredientArchive`/`RecipeArchive`/`CookBookArchive` Read/Write APIs. Input is the exact Core manifest JSON (read through `Formats.Json.Options`); children resolve by an `{id}.png → {id}.igt → {id}.rcp` filename convention. A small `Validator` refactor exposes `ValidateIngredient`/`ValidateRecipe` so per-level validation shares one source of truth. No drawing — variant pixels come from PNG files.

**Tech Stack:** .NET 10, C#, `System.CommandLine` 2.0.9, SixLabors.ImageSharp 3.1.11, xUnit.

**Reference spec:** `docs/superpowers/specs/2026-07-21-nfty-authoring-cli-design.md`

## Global Constraints

- Target framework: **.NET 10**; ImageSharp pinned to **3.1.11** (do not upgrade). `System.CommandLine` **2.0.9**.
- All manifest JSON goes through **`Nfty.Core.Formats.Json.Options`** (camelCase, enums as camelCase strings). Never use default `JsonSerializerOptions`.
- Every sort reaching output uses `StringComparer.Ordinal`. String set/lookup comparisons over ids use `StringComparer.Ordinal`.
- **Callers own image disposal.** Every `Image<Rgba32>` and `Loaded*` a command opens is freed with `using`/`finally`. A constructed `Loaded*` that merely *wraps* already-owned images is NOT disposed (that would double-dispose the images their real owner frees).
- Errors surface through `ErrorReport`; commands throw or return non-zero, they do not print traces themselves.
- Tests build fixtures in temp dirs via `Directory.CreateTempSubdirectory()` and are self-contained; no golden-image files. xUnit method names are `Snake_case_sentences`.
- Commits: conventional-commit style, one per task step group as noted. End commit messages with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.

---

## File Structure

**Create:**
- `src/Nfty.Cli/ManifestFile.cs` — `ManifestFile.Read<T>(string path)`: deserialize a manifest JSON file through `Json.Options` with friendly errors.
- `src/Nfty.Cli/CommandFactory.Authoring.cs` — `partial class CommandFactory` holding `NewGroup()` and `AddGroup()` and their subcommand builders.
- `tests/Nfty.Cli.Tests/AuthoringCommandsTests.cs` — round-trip + validation + error tests for all six commands.

**Modify:**
- `src/Nfty.Core/Formats/Validator.cs` — split canvas-dependent image checks out; add `ValidateIngredient` / `ValidateRecipe`.
- `src/Nfty.Cli/CommandFactory.cs` — register `NewGroup()` and `AddGroup()` in `Build()`.
- `tests/Nfty.Core.Tests/ValidatorTests.cs` — add per-level validation tests.

---

## Task 1: Validator refactor — canvas-independent per-level validation

**Files:**
- Modify: `src/Nfty.Core/Formats/Validator.cs`
- Test: `tests/Nfty.Core.Tests/ValidatorTests.cs`

**Interfaces:**
- Produces: `Validator.ValidateIngredient(LoadedIngredient ing) : IReadOnlyList<string>`, `Validator.ValidateRecipe(LoadedRecipe r) : IReadOnlyList<string>`. `Validator.Validate(LoadedCookBook cb) : IReadOnlyList<string>` keeps its signature and behavior.
- Consumes: nothing new.

The whole-book `Validate` path must stay behavior-preserving: existing `ValidatorTests` assert on message *substrings*, so rephrasing is fine but each key substring (`no variants`, `duplicate`, `grayscale`, `dimensions`, `negative`, `rule references unknown`, `layerOrder`, `canvas`/`positive`) must survive.

- [ ] **Step 1: Write failing tests for the new per-level entry points**

Append to `tests/Nfty.Core.Tests/ValidatorTests.cs` (inside the class, before the final `}`):

```csharp
    // --- per-level validation (authoring CLI) ---

    private static LoadedIngredient DynIng(string id, Colorization col, params (string id, Rgba32 fill, int w, int h)[] vs)
    {
        var images = new Dictionary<string, Image<Rgba32>>();
        foreach (var v in vs) images[v.id] = new Image<Rgba32>(v.w, v.h, v.fill);
        return new LoadedIngredient
        {
            Manifest = new IngredientManifest(id, id, LayerKind.Dynamic, col,
                vs.Select(v => new Variant(v.id, v.id, 1)).ToArray()),
            VariantImages = images,
        };
    }

    private static Colorization Range() =>
        new(ColorModel.Hsv, 5, 5, new[] { new ColorEntry(1, new ColorRange(0, 10, 0, 10), null) });

    [Fact]
    public void ValidateIngredient_passes_a_clean_grayscale_dynamic_ingredient() =>
        Assert.Empty(Validator.ValidateIngredient(
            DynIng("d", Range(), ("a", new Rgba32(120, 120, 120, 255), 4, 4))));

    [Fact]
    public void ValidateIngredient_reports_a_non_grayscale_dynamic_variant() =>
        Assert.Contains(
            Validator.ValidateIngredient(DynIng("d", Range(), ("a", new Rgba32(200, 10, 10, 255), 4, 4))),
            p => p.Contains("grayscale", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void ValidateIngredient_reports_variant_images_of_differing_sizes() =>
        Assert.Contains(
            Validator.ValidateIngredient(DynIng("d", Range(),
                ("a", new Rgba32(120, 120, 120, 255), 4, 4),
                ("b", new Rgba32(120, 120, 120, 255), 8, 8))),
            p => p.Contains("differing sizes", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void ValidateIngredient_reports_a_custom_layer_carrying_a_colorization() =>
        Assert.Contains(
            Validator.ValidateIngredient(new LoadedIngredient
            {
                Manifest = new IngredientManifest("c", "C", LayerKind.Custom, Range(),
                    new[] { new Variant("a", "A", 1) }),
                VariantImages = new Dictionary<string, Image<Rgba32>>
                    { ["a"] = new Image<Rgba32>(4, 4, new Rgba32(0, 0, 0, 255)) },
            }),
            p => p.Contains("custom", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void ValidateRecipe_passes_a_clean_single_layer_recipe()
    {
        var ing = DynIng("d", Range(), ("a", new Rgba32(120, 120, 120, 255), 4, 4));
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("r", "R", new[] { "d" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        Assert.Empty(Validator.ValidateRecipe(recipe));
    }

    [Fact]
    public void ValidateRecipe_reports_cross_ingredient_size_mismatch()
    {
        var a = DynIng("a", Range(), ("x", new Rgba32(120, 120, 120, 255), 4, 4));
        var b = DynIng("b", Range(), ("y", new Rgba32(120, 120, 120, 255), 8, 8));
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("r", "R", new[] { "a", "b" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { a, b },
        };
        Assert.Contains(Validator.ValidateRecipe(recipe),
            p => p.Contains("differing sizes", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateRecipe_reports_a_dangling_rule_reference()
    {
        var ing = DynIng("d", Range(), ("a", new Rgba32(120, 120, 120, 255), 4, 4));
        var rule = new IncompatibilityRule(RuleType.Exclude,
            new RuleTarget("nope", "x"), new[] { new RuleTarget("d", "a") });
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("r", "R", new[] { "d" }, new[] { rule }),
            Ingredients = new[] { ing },
        };
        Assert.Contains(Validator.ValidateRecipe(recipe),
            p => p.Contains("rule references unknown", StringComparison.OrdinalIgnoreCase));
    }
```

- [ ] **Step 2: Run tests to verify they fail (method missing)**

Run: `dotnet test tests/Nfty.Core.Tests --filter "FullyQualifiedName~ValidatorTests" --nologo`
Expected: compile error / FAIL — `ValidateIngredient` / `ValidateRecipe` do not exist.

- [ ] **Step 3: Refactor `Validator.cs`**

Replace the body of `Validate` down through `CheckVariantImages` with the following. Keep `CheckCookBook`, `CheckLayerOrder`, `CheckKind`, `CheckColorEntries`, `IsGrayscale`, `Duplicates`, and `CheckRange` exactly as they are.

```csharp
    public static IReadOnlyList<string> Validate(LoadedCookBook cb)
    {
        var problems = new List<string>();
        CheckCookBook(problems, cb);
        foreach (var r in cb.Recipes)
            CheckRecipe(problems, r, cb.Manifest.Canvas);
        return problems;
    }

    /// <summary>
    /// Validates a standalone ingredient — every check that does NOT need a canvas. The canvas is a
    /// property of the CookBook, so "does this variant match the canvas?" cannot be answered here;
    /// <see cref="Validate(LoadedCookBook)"/> remains the authoritative whole-book gate. Used by the
    /// CLI's <c>new ingredient</c> / <c>add variant</c>, which build an .igt before a canvas exists.
    /// </summary>
    public static IReadOnlyList<string> ValidateIngredient(LoadedIngredient ing)
    {
        var problems = new List<string>();
        string where = $"Ingredient '{ing.Manifest.Id}'";
        CheckIngredient(problems, where, ing);
        CheckUniformSize(problems, where, ing.VariantImages.Values);
        return problems;
    }

    /// <summary>
    /// Validates a standalone recipe — every canvas-independent check, plus that all variant images
    /// across all its ingredients share one size, so they could match some future canvas. Used by
    /// the CLI's <c>new recipe</c> / <c>add ingredient</c>.
    /// </summary>
    public static IReadOnlyList<string> ValidateRecipe(LoadedRecipe r)
    {
        var problems = new List<string>();
        CheckRecipeStructure(problems, r);
        foreach (var ing in r.Ingredients)
            CheckIngredient(problems, $"Ingredient '{ing.Manifest.Id}' in '{r.Manifest.Id}'", ing);
        CheckUniformSize(problems, $"Recipe '{r.Manifest.Id}'",
            r.Ingredients.SelectMany(i => i.VariantImages.Values));
        return problems;
    }

    private static void CheckRecipe(List<string> problems, LoadedRecipe r, Dimensions canvas)
    {
        CheckRecipeStructure(problems, r);
        foreach (var ing in r.Ingredients)
        {
            string where = $"Ingredient '{ing.Manifest.Id}' in '{r.Manifest.Id}'";
            CheckIngredient(problems, where, ing);
            CheckVariantImagesCanvas(problems, where, ing, canvas);
        }
    }

    /// <summary>Recipe checks that need no canvas: id uniqueness, layerOrder, and rule references.</summary>
    private static void CheckRecipeStructure(List<string> problems, LoadedRecipe r)
    {
        foreach (var dup in Duplicates(r.Ingredients.Select(i => i.Manifest.Id)))
            problems.Add($"Recipe '{r.Manifest.Id}' has duplicate ingredient id '{dup}'.");

        // Built duplicate-tolerantly (last wins) so a duplicate is reported above rather than
        // thrown here; the rest of the checks still run and report what they find.
        var ingById = new Dictionary<string, LoadedIngredient>();
        foreach (var i in r.Ingredients) ingById[i.Manifest.Id] = i;

        CheckLayerOrder(problems, r, ingById);

        foreach (var rule in r.Manifest.Rules)
            foreach (var t in rule.Targets.Append(rule.When))
                if (!ingById.ContainsKey(t.IngredientId))
                    problems.Add($"Recipe '{r.Manifest.Id}' rule references unknown ingredient '{t.IngredientId}'.");
    }

    /// <summary>The canvas-independent per-ingredient checks. <paramref name="where"/> carries the
    /// caller's context ("Ingredient 'x'" standalone, or "…in 'r'" inside a recipe).</summary>
    private static void CheckIngredient(List<string> problems, string where, LoadedIngredient ing)
    {
        if (ing.Manifest.Variants.Count == 0)
            problems.Add($"{where} has no variants.");
        if (ing.Manifest.Variants.Sum(v => v.Weight) <= 0)
            problems.Add($"{where} has zero total variant weight.");
        foreach (var dup in Duplicates(ing.Manifest.Variants.Select(v => v.Id)))
            problems.Add($"{where} has duplicate variant id '{dup}'.");

        // Zero is a legitimate way to shelve a variant; negative is not — see CheckCookBook.
        foreach (var v in ing.Manifest.Variants.Where(v => v.Weight < 0))
            problems.Add($"{where} has variant '{v.Id}' with a negative weight ({v.Weight}); "
                + "weights must be zero or greater, and zero means never rolled.");

        CheckKind(problems, where, ing.Manifest.Kind, ing.Manifest.Colorization);
        CheckColorEntries(problems, where, ing.Manifest.Colorization);
        CheckVariantImagesPresentAndGray(problems, where, ing);
    }

    /// <summary>Presence of every variant's image and grayscale for dynamic/static — no canvas needed.</summary>
    private static void CheckVariantImagesPresentAndGray(List<string> problems, string where, LoadedIngredient ing)
    {
        foreach (var v in ing.Manifest.Variants.DistinctBy(v => v.Id, StringComparer.Ordinal))
        {
            if (!ing.VariantImages.TryGetValue(v.Id, out var img))
            {
                problems.Add($"{where} variant '{v.Id}' has no image.");
                continue;
            }
            if (ing.Manifest.Kind != LayerKind.Custom && !IsGrayscale(img))
                problems.Add($"{where} variant '{v.Id}' is not grayscale; "
                    + "dynamic/static value-maps must have R=G=B.");
        }
    }

    /// <summary>The canvas-dependent half: every present variant image must match the CookBook canvas.</summary>
    private static void CheckVariantImagesCanvas(
        List<string> problems, string where, LoadedIngredient ing, Dimensions canvas)
    {
        foreach (var v in ing.Manifest.Variants.DistinctBy(v => v.Id, StringComparer.Ordinal))
        {
            if (!ing.VariantImages.TryGetValue(v.Id, out var img)) continue; // presence reported elsewhere
            if (img.Width != canvas.Width || img.Height != canvas.Height)
                problems.Add($"{where} variant '{v.Id}' has dimensions {img.Width}x{img.Height}, "
                    + $"expected canvas {canvas.Width}x{canvas.Height}.");
        }
    }

    /// <summary>
    /// Every image in the set must share one size. Without a canvas (ingredient/recipe authoring),
    /// this is the closest reachable check: images that already differ from each other can never all
    /// match a single future canvas.
    /// </summary>
    private static void CheckUniformSize(List<string> problems, string where, IEnumerable<Image<Rgba32>> images)
    {
        var sizes = images.Select(i => (i.Width, i.Height)).Distinct().ToList();
        if (sizes.Count > 1)
        {
            string list = string.Join(", ", sizes
                .OrderBy(s => s.Width).ThenBy(s => s.Height)
                .Select(s => $"{s.Width}x{s.Height}"));
            problems.Add($"{where} has variant images of differing sizes ({list}); every image must "
                + "share one size so they can all match the CookBook canvas.");
        }
    }
```

Delete the old `CheckRecipe(List<string>, LoadedRecipe, Dimensions)` body you replaced, the old `CheckIngredient(List<string>, LoadedRecipe, LoadedIngredient, Dimensions)`, and the old `CheckVariantImages(...)` — they are fully superseded above. `CheckLayerOrder`, `CheckKind`, `CheckColorEntries`, `IsGrayscale`, `Duplicates`, `CheckRange`, `CheckCookBook` are unchanged.

- [ ] **Step 4: Run the full Validator suite (old + new) to verify green**

Run: `dotnet test tests/Nfty.Core.Tests --filter "FullyQualifiedName~ValidatorTests" --nologo`
Expected: PASS — all pre-existing tests plus the seven new ones.

- [ ] **Step 5: Run the whole Core suite to confirm nothing else regressed**

Run: `dotnet test tests/Nfty.Core.Tests --nologo`
Expected: PASS (all).

- [ ] **Step 6: Commit**

```bash
git add src/Nfty.Core/Formats/Validator.cs tests/Nfty.Core.Tests/ValidatorTests.cs
git commit -m "$(printf 'refactor(core): expose ValidateIngredient/ValidateRecipe\n\nSplit the canvas-dependent variant-image check out of Validator so the\ncanvas-independent checks can run on a standalone ingredient or recipe.\nThe whole-book Validate path is behavior-preserving. Enables the authoring\nCLI to validate an .igt/.rcp before a canvas exists.\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

## Task 2: `ManifestFile.Read` + `new ingredient`

**Files:**
- Create: `src/Nfty.Cli/ManifestFile.cs`
- Create: `src/Nfty.Cli/CommandFactory.Authoring.cs`
- Modify: `src/Nfty.Cli/CommandFactory.cs` (register `NewGroup()`)
- Create: `tests/Nfty.Cli.Tests/AuthoringCommandsTests.cs`

**Interfaces:**
- Consumes: `Validator.ValidateIngredient` (Task 1); `IngredientArchive.Write(string, IngredientManifest, IReadOnlyDictionary<string, Image<Rgba32>>)`; `IngredientArchive.Read(string)`; `Formats.Json.Options`.
- Produces: `ManifestFile.Read<T>(string path) : T`; `CommandFactory.NewGroup() : Command` (with an `ingredient` subcommand).

- [ ] **Step 1: Write the failing round-trip test**

Create `tests/Nfty.Cli.Tests/AuthoringCommandsTests.cs`:

```csharp
using System.CommandLine;
using System.Text.Json;
using Nfty.Cli;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Cli.Tests;

public class AuthoringCommandsTests
{
    private static readonly InvocationConfiguration NonThrowing = new() { EnableDefaultExceptionHandler = false };

    private static int Run(params string[] args) =>
        CommandFactory.Build().Parse(args).Invoke(NonThrowing);

    private static string WriteJson<T>(string dir, string name, T value)
    {
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, JsonSerializer.Serialize(value, Json.Options));
        return path;
    }

    private static void WritePng(string dir, string name, Rgba32 fill, int w = 4, int h = 4)
    {
        using var img = new Image<Rgba32>(w, h, fill);
        img.Save(Path.Combine(dir, name), new PngEncoder());
    }

    [Fact]
    public void New_ingredient_builds_a_readable_igt_from_a_manifest_and_pngs()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            var manifest = new IngredientManifest("aura", "Aura", LayerKind.Dynamic,
                new Colorization(ColorModel.Hsv, 12, 4,
                    new[] { new ColorEntry(1, new ColorRange(0, 360, 40, 100), null) }),
                new[] { new Variant("glow", "Glow", 1), new Variant("spark", "Spark", 1) });
            string manifestPath = WriteJson(tmp.FullName, "aura.json", manifest);

            string images = Path.Combine(tmp.FullName, "img");
            Directory.CreateDirectory(images);
            WritePng(images, "glow.png", new Rgba32(120, 120, 120, 255));
            WritePng(images, "spark.png", new Rgba32(200, 200, 200, 255));

            string outPath = Path.Combine(tmp.FullName, "aura.igt");
            int code = Run("new", "ingredient", outPath, "--manifest", manifestPath, "--images", images);

            Assert.Equal(0, code);
            using var loaded = IngredientArchive.Read(outPath);
            Assert.Equal("aura", loaded.Manifest.Id);
            Assert.Equal(2, loaded.Manifest.Variants.Count);
            Assert.True(loaded.VariantImages.ContainsKey("glow"));
            Assert.True(loaded.VariantImages.ContainsKey("spark"));
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void New_ingredient_refuses_a_non_grayscale_dynamic_variant()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            var manifest = new IngredientManifest("aura", "Aura", LayerKind.Dynamic,
                new Colorization(ColorModel.Hsv, 12, 4,
                    new[] { new ColorEntry(1, new ColorRange(0, 360, 40, 100), null) }),
                new[] { new Variant("glow", "Glow", 1) });
            string manifestPath = WriteJson(tmp.FullName, "aura.json", manifest);
            string images = Path.Combine(tmp.FullName, "img");
            Directory.CreateDirectory(images);
            WritePng(images, "glow.png", new Rgba32(200, 10, 10, 255)); // coloured, not grayscale

            string outPath = Path.Combine(tmp.FullName, "aura.igt");
            int code = Run("new", "ingredient", outPath, "--manifest", manifestPath, "--images", images);

            Assert.Equal(1, code);
            Assert.False(File.Exists(outPath)); // refused before writing
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void New_ingredient_names_a_missing_variant_png()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            var manifest = new IngredientManifest("aura", "Aura", LayerKind.Custom, null,
                new[] { new Variant("glow", "Glow", 1) });
            string manifestPath = WriteJson(tmp.FullName, "aura.json", manifest);
            string images = Path.Combine(tmp.FullName, "img");
            Directory.CreateDirectory(images); // no glow.png

            string outPath = Path.Combine(tmp.FullName, "aura.igt");
            var ex = Assert.Throws<FileNotFoundException>(() =>
                Run("new", "ingredient", outPath, "--manifest", manifestPath, "--images", images));
            Assert.Contains("glow", ex.Message);
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void New_ingredient_rejects_a_manifest_declaring_an_unsupported_schema_version()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            // Hand-written manifest with an explicit future schemaVersion.
            string manifestPath = Path.Combine(tmp.FullName, "aura.json");
            File.WriteAllText(manifestPath,
                """{"id":"aura","name":"Aura","kind":"custom","colorization":null,"variants":[{"id":"only","name":"Only","weight":1}],"schemaVersion":999}""");
            string images = Path.Combine(tmp.FullName, "img");
            Directory.CreateDirectory(images);
            WritePng(images, "only.png", new Rgba32(0, 0, 0, 255));

            string outPath = Path.Combine(tmp.FullName, "aura.igt");
            Assert.Throws<Nfty.Core.Formats.UnsupportedSchemaVersionException>(() =>
                Run("new", "ingredient", outPath, "--manifest", manifestPath, "--images", images));
            Assert.False(File.Exists(outPath));
        }
        finally { tmp.Delete(recursive: true); }
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Nfty.Cli.Tests --filter "FullyQualifiedName~AuthoringCommandsTests" --nologo`
Expected: FAIL — `new` command does not exist (parse error), and `ManifestFile`/`NewGroup` are undefined (compile error).

- [ ] **Step 3: Create `ManifestFile.cs`**

```csharp
using System.Text.Json;
using Nfty.Core.Formats;
using Nfty.Core.Model;

namespace Nfty.Cli;

/// <summary>Reads an authoring manifest JSON file through the shared <see cref="Json.Options"/>,
/// turning framework parse failures into messages that name the file. Enforces the schema version
/// so an authoring input cannot declare a format this build cannot write.</summary>
public static class ManifestFile
{
    public static T Read<T>(string path) where T : ISchemaVersioned
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"No such manifest file: {path}", path);
        string json = File.ReadAllText(path);
        T manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<T>(json, Json.Options)
                ?? throw new InvalidDataException($"Manifest '{path}' deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Manifest '{path}' is not valid JSON: {ex.Message}", ex);
        }
        // Omitting schemaVersion is fine (it defaults to Schema.Current); declaring an unsupported
        // one is rejected with the same message the archive readers use.
        UnsupportedSchemaVersionException.Require(manifest);
        return manifest;
    }
}
```

Note: the `where T : ISchemaVersioned` constraint holds — `IngredientManifest`, `RecipeManifest`, and `CookBookManifest` all implement `ISchemaVersioned`.

- [ ] **Step 4: Create `CommandFactory.Authoring.cs` with `NewGroup()` and the `ingredient` subcommand**

```csharp
using System.CommandLine;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Cli;

public static partial class CommandFactory
{
    /// <summary>The `new` command group: create an archive from a manifest JSON plus the artifacts
    /// one level down, resolved by the {id} filename convention.</summary>
    public static Command NewGroup()
    {
        var group = new Command("new", "Create a new .igt / .rcp / .cbk from a manifest and its parts.");
        group.Subcommands.Add(NewIngredient());
        return group;
    }

    private static Command NewIngredient()
    {
        var outPath = new Argument<string>("out") { Description = "Output .igt path to create." };
        var manifest = new Option<string>("--manifest")
        {
            Description = "Path to an IngredientManifest JSON (id, name, kind, colorization, variants).",
            Required = true,
        };
        var images = new Option<string>("--images")
        {
            Description = "Directory of variant PNGs; each variant's image is <dir>/{variantId}.png.",
            Required = true,
        };
        var cmd = new Command("ingredient",
            "Build an .igt from an ingredient manifest and one PNG per variant (named {variantId}.png).")
            { outPath, manifest, images };
        cmd.SetAction(parse =>
        {
            var m = ManifestFile.Read<IngredientManifest>(parse.GetValue(manifest)!);
            string imagesDir = parse.GetValue(images)!;
            var loaded = LoadVariantImages(m, imagesDir);
            try
            {
                var ing = new LoadedIngredient { Manifest = m, VariantImages = loaded };
                var problems = Validator.ValidateIngredient(ing);
                if (problems.Count > 0) { Report(problems); return 1; }

                IngredientArchive.Write(parse.GetValue(outPath)!, m, loaded);
                Console.WriteLine($"Wrote {parse.GetValue(outPath)} ({loaded.Count} variants)");
                return 0;
            }
            finally
            {
                // These images have no other owner: the LoadedIngredient above only wraps them.
                foreach (var img in loaded.Values) img.Dispose();
            }
        });
        return cmd;
    }

    /// <summary>Loads one PNG per distinct variant id, by the {id}.png convention.</summary>
    private static Dictionary<string, Image<Rgba32>> LoadVariantImages(IngredientManifest m, string imagesDir)
    {
        var loaded = new Dictionary<string, Image<Rgba32>>();
        try
        {
            foreach (var v in m.Variants.DistinctBy(v => v.Id, StringComparer.Ordinal))
            {
                string png = Path.Combine(imagesDir, $"{v.Id}.png");
                if (!File.Exists(png))
                    throw new FileNotFoundException(
                        $"No image for variant '{v.Id}': expected {png}", png);
                loaded[v.Id] = Image.Load<Rgba32>(png);
            }
        }
        catch
        {
            foreach (var img in loaded.Values) img.Dispose();
            throw;
        }
        return loaded;
    }

    /// <summary>Prints each validation problem to stderr, as `validate` does.</summary>
    private static void Report(IReadOnlyList<string> problems)
    {
        foreach (var p in problems) Console.Error.WriteLine(p);
    }
}
```

- [ ] **Step 5: Make `CommandFactory` partial and register `NewGroup()`**

In `src/Nfty.Cli/CommandFactory.cs`, change the class declaration to `public static partial class CommandFactory`. In `Build()`, add the registration after `root.Subcommands.Add(Extend());`:

```csharp
        root.Subcommands.Add(NewGroup());
```

- [ ] **Step 6: Run the ingredient tests**

Run: `dotnet test tests/Nfty.Cli.Tests --filter "FullyQualifiedName~AuthoringCommandsTests" --nologo`
Expected: PASS (the three `New_ingredient_*` tests).

- [ ] **Step 7: Commit**

```bash
git add src/Nfty.Cli/ManifestFile.cs src/Nfty.Cli/CommandFactory.Authoring.cs src/Nfty.Cli/CommandFactory.cs tests/Nfty.Cli.Tests/AuthoringCommandsTests.cs
git commit -m "$(printf 'feat(cli): new ingredient command\n\nBuild an .igt from an IngredientManifest JSON plus one PNG per variant\n(named {variantId}.png), validated with Validator.ValidateIngredient and\nrefused if invalid.\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

## Task 3: `new recipe`

**Files:**
- Modify: `src/Nfty.Cli/CommandFactory.Authoring.cs` (add `NewRecipe()`, register in `NewGroup`)
- Modify: `tests/Nfty.Cli.Tests/AuthoringCommandsTests.cs`

**Interfaces:**
- Consumes: `Validator.ValidateRecipe` (Task 1); `IngredientArchive.Read(string)`; `RecipeArchive.Write(string, RecipeManifest, IReadOnlyList<LoadedIngredient>)`.
- Produces: a `recipe` subcommand under `new`.

- [ ] **Step 1: Write the failing test**

Add to `AuthoringCommandsTests.cs`. This first builds two `.igt`s with the `new ingredient` command, then composes them into a `.rcp`:

```csharp
    // Builds an .igt at <dir>/{id}.igt via the new ingredient command. Grayscale so dynamic passes.
    private void BuildIgt(string dir, string id, LayerKind kind, Colorization? col, params string[] variantIds)
    {
        var manifest = new IngredientManifest(id, id, kind, col,
            variantIds.Select(v => new Variant(v, v, 1)).ToArray());
        string manifestPath = WriteJson(dir, $"{id}.manifest.json", manifest);
        string images = Path.Combine(dir, $"{id}.img");
        Directory.CreateDirectory(images);
        foreach (var v in variantIds) WritePng(images, $"{v}.png", new Rgba32(120, 120, 120, 255));
        int code = Run("new", "ingredient", Path.Combine(dir, $"{id}.igt"),
            "--manifest", manifestPath, "--images", images);
        Assert.Equal(0, code);
    }

    private static Colorization Hsv() =>
        new(ColorModel.Hsv, 12, 4, new[] { new ColorEntry(1, new ColorRange(0, 360, 40, 100), null) });

    [Fact]
    public void New_recipe_composes_igts_into_a_readable_rcp()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string ings = Path.Combine(tmp.FullName, "ings");
            Directory.CreateDirectory(ings);
            BuildIgt(ings, "bg", LayerKind.Dynamic, Hsv(), "sky");
            BuildIgt(ings, "aura", LayerKind.Dynamic, Hsv(), "glow");

            var recipe = new RecipeManifest("cat", "Cat",
                new[] { "bg", "aura" }, Array.Empty<IncompatibilityRule>());
            string recipePath = WriteJson(tmp.FullName, "cat.json", recipe);

            string outPath = Path.Combine(tmp.FullName, "cat.rcp");
            int code = Run("new", "recipe", outPath, "--manifest", recipePath, "--ingredients", ings);

            Assert.Equal(0, code);
            using var loaded = RecipeArchive.Read(outPath);
            Assert.Equal(new[] { "bg", "aura" }, loaded.Manifest.LayerOrder);
            Assert.Equal(2, loaded.Ingredients.Count);
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void New_recipe_names_a_missing_ingredient_igt()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string ings = Path.Combine(tmp.FullName, "ings");
            Directory.CreateDirectory(ings);
            BuildIgt(ings, "bg", LayerKind.Dynamic, Hsv(), "sky"); // aura.igt intentionally absent

            var recipe = new RecipeManifest("cat", "Cat",
                new[] { "bg", "aura" }, Array.Empty<IncompatibilityRule>());
            string recipePath = WriteJson(tmp.FullName, "cat.json", recipe);

            var ex = Assert.Throws<FileNotFoundException>(() => Run("new", "recipe",
                Path.Combine(tmp.FullName, "cat.rcp"), "--manifest", recipePath, "--ingredients", ings));
            Assert.Contains("aura", ex.Message);
        }
        finally { tmp.Delete(recursive: true); }
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Nfty.Cli.Tests --filter "FullyQualifiedName~New_recipe" --nologo`
Expected: FAIL — `new recipe` is not a known command.

- [ ] **Step 3: Add `NewRecipe()` and register it**

In `CommandFactory.Authoring.cs`, register in `NewGroup()` (after the ingredient line):

```csharp
        group.Subcommands.Add(NewRecipe());
```

Add the builder:

```csharp
    private static Command NewRecipe()
    {
        var outPath = new Argument<string>("out") { Description = "Output .rcp path to create." };
        var manifest = new Option<string>("--manifest")
        {
            Description = "Path to a RecipeManifest JSON (id, name, layerOrder, rules).",
            Required = true,
        };
        var ingredients = new Option<string>("--ingredients")
        {
            Description = "Directory of .igt files; each layerOrder id resolves to <dir>/{id}.igt.",
            Required = true,
        };
        var cmd = new Command("recipe",
            "Build a .rcp from a recipe manifest and one .igt per layerOrder id (named {id}.igt).")
            { outPath, manifest, ingredients };
        cmd.SetAction(parse =>
        {
            var m = ManifestFile.Read<RecipeManifest>(parse.GetValue(manifest)!);
            string dir = parse.GetValue(ingredients)!;
            var loaded = new List<LoadedIngredient>();
            try
            {
                foreach (var id in m.LayerOrder.Distinct(StringComparer.Ordinal))
                {
                    string igt = Path.Combine(dir, $"{id}.igt");
                    if (!File.Exists(igt))
                        throw new FileNotFoundException($"No ingredient for layer '{id}': expected {igt}", igt);
                    loaded.Add(IngredientArchive.Read(igt));
                }

                var recipe = new LoadedRecipe { Manifest = m, Ingredients = loaded };
                var problems = Validator.ValidateRecipe(recipe);
                if (problems.Count > 0) { Report(problems); return 1; }

                RecipeArchive.Write(parse.GetValue(outPath)!, m, loaded);
                Console.WriteLine($"Wrote {parse.GetValue(outPath)} ({loaded.Count} ingredients)");
                return 0;
            }
            finally
            {
                foreach (var ing in loaded) ing.Dispose();
            }
        });
        return cmd;
    }
```

- [ ] **Step 4: Run recipe tests**

Run: `dotnet test tests/Nfty.Cli.Tests --filter "FullyQualifiedName~New_recipe" --nologo`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Nfty.Cli/CommandFactory.Authoring.cs tests/Nfty.Cli.Tests/AuthoringCommandsTests.cs
git commit -m "$(printf 'feat(cli): new recipe command\n\nCompose a .rcp from a RecipeManifest JSON and one .igt per layerOrder id\n(named {id}.igt), validated with Validator.ValidateRecipe.\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

## Task 4: `new cookbook` (with `--force`)

**Files:**
- Modify: `src/Nfty.Cli/CommandFactory.Authoring.cs` (add `NewCookbook()`, register)
- Modify: `tests/Nfty.Cli.Tests/AuthoringCommandsTests.cs`

**Interfaces:**
- Consumes: `Validator.Validate(LoadedCookBook)`; `RecipeArchive.Read(string)`; `CookBookArchive.Write(string, CookBookManifest, IReadOnlyList<LoadedRecipe>)`; `Generator.Generate` + `SetWriter.Write` for the end-to-end test.
- Produces: a `cookbook` subcommand under `new`.

- [ ] **Step 1: Write the failing end-to-end pipeline test**

Add to `AuthoringCommandsTests.cs`. Builds `.igt → .rcp → .cbk` entirely through the CLI, then generates from the result:

```csharp
    // Builds a .rcp at <dir>/{id}.rcp from the given ingredient ids (each already an {id}.igt in ingDir).
    private void BuildRcp(string dir, string ingDir, string id, params string[] layerOrder)
    {
        var recipe = new RecipeManifest(id, id, layerOrder, Array.Empty<IncompatibilityRule>());
        string manifestPath = WriteJson(dir, $"{id}.recipe.json", recipe);
        int code = Run("new", "recipe", Path.Combine(dir, $"{id}.rcp"),
            "--manifest", manifestPath, "--ingredients", ingDir);
        Assert.Equal(0, code);
    }

    [Fact]
    public void New_cookbook_pipeline_produces_a_generatable_book()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string ings = Path.Combine(tmp.FullName, "ings");
            Directory.CreateDirectory(ings);
            BuildIgt(ings, "bg", LayerKind.Dynamic, Hsv(), "sky");
            BuildIgt(ings, "aura", LayerKind.Dynamic, Hsv(), "glow");

            string rcps = Path.Combine(tmp.FullName, "rcps");
            Directory.CreateDirectory(rcps);
            BuildRcp(rcps, ings, "cat", "bg", "aura");

            var cbk = new CookBookManifest("cb", "Book", new Dimensions(4, 4),
                new Nfty.Core.Model.Collection("Book", "", "BK"),
                new Dictionary<string, double> { ["cat"] = 100 });
            string cbkPath = WriteJson(tmp.FullName, "book.json", cbk);

            string outPath = Path.Combine(tmp.FullName, "book.cbk");
            int code = Run("new", "cookbook", outPath, "--manifest", cbkPath, "--recipes", rcps);
            Assert.Equal(0, code);

            // The produced .cbk must generate a Set — full parity with a hand-built book.
            using var book = CookBookArchive.Read(outPath);
            using var set = Nfty.Core.Generation.Generator.Generate(book,
                new Nfty.Core.Generation.GenerateOptions(2, "seed"));
            Assert.Equal(2, set.Assets.Count);
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void New_cookbook_refuses_an_invalid_book_but_force_writes_it()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string ings = Path.Combine(tmp.FullName, "ings");
            Directory.CreateDirectory(ings);
            BuildIgt(ings, "bg", LayerKind.Dynamic, Hsv(), "sky"); // variant PNGs are 4x4

            string rcps = Path.Combine(tmp.FullName, "rcps");
            Directory.CreateDirectory(rcps);
            BuildRcp(rcps, ings, "cat", "bg");

            // Canvas 8x8 disagrees with the 4x4 variant images -> invalid book.
            var cbk = new CookBookManifest("cb", "Book", new Dimensions(8, 8),
                new Nfty.Core.Model.Collection("Book", "", "BK"),
                new Dictionary<string, double> { ["cat"] = 100 });
            string cbkPath = WriteJson(tmp.FullName, "book.json", cbk);
            string outPath = Path.Combine(tmp.FullName, "book.cbk");

            Assert.Equal(1, Run("new", "cookbook", outPath, "--manifest", cbkPath, "--recipes", rcps));
            Assert.False(File.Exists(outPath));

            Assert.Equal(0, Run("new", "cookbook", outPath, "--manifest", cbkPath, "--recipes", rcps, "--force"));
            Assert.True(File.Exists(outPath));
        }
        finally { tmp.Delete(recursive: true); }
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Nfty.Cli.Tests --filter "FullyQualifiedName~New_cookbook" --nologo`
Expected: FAIL — `new cookbook` is not a known command.

- [ ] **Step 3: Add `NewCookbook()` and register it**

Register in `NewGroup()`:

```csharp
        group.Subcommands.Add(NewCookbook());
```

Add the builder:

```csharp
    private static Command NewCookbook()
    {
        var outPath = new Argument<string>("out") { Description = "Output .cbk path to create." };
        var manifest = new Option<string>("--manifest")
        {
            Description = "Path to a CookBookManifest JSON (id, name, canvas, collection, recipeWeights).",
            Required = true,
        };
        var recipes = new Option<string>("--recipes")
        {
            Description = "Directory of .rcp files; each recipeWeights key resolves to <dir>/{id}.rcp.",
            Required = true,
        };
        var force = new Option<bool>("--force")
        {
            Description = "Write even if validation reports problems (they are printed as warnings). "
                + "Use only for deliberate work-in-progress; generate will still refuse the book.",
        };
        var cmd = new Command("cookbook",
            "Assemble a .cbk from a cookbook manifest and one .rcp per recipeWeights key (named {id}.rcp).")
            { outPath, manifest, recipes, force };
        cmd.SetAction(parse =>
        {
            var m = ManifestFile.Read<CookBookManifest>(parse.GetValue(manifest)!);
            string dir = parse.GetValue(recipes)!;
            var loaded = new List<LoadedRecipe>();
            try
            {
                foreach (var id in m.RecipeWeights.Keys.Distinct(StringComparer.Ordinal))
                {
                    string rcp = Path.Combine(dir, $"{id}.rcp");
                    if (!File.Exists(rcp))
                        throw new FileNotFoundException($"No recipe '{id}': expected {rcp}", rcp);
                    loaded.Add(RecipeArchive.Read(rcp));
                }

                var book = new LoadedCookBook { Manifest = m, Recipes = loaded, SourceSha256 = null };
                var problems = Validator.Validate(book);
                if (problems.Count > 0)
                {
                    Report(problems);
                    if (!parse.GetValue(force)) return 1;
                    Console.Error.WriteLine("--force: writing despite the problems above.");
                }

                CookBookArchive.Write(parse.GetValue(outPath)!, m, loaded);
                Console.WriteLine($"Wrote {parse.GetValue(outPath)} ({loaded.Count} recipes)");
                return 0;
            }
            finally
            {
                foreach (var r in loaded) r.Dispose();
            }
        });
        return cmd;
    }
```

- [ ] **Step 4: Run cookbook tests**

Run: `dotnet test tests/Nfty.Cli.Tests --filter "FullyQualifiedName~New_cookbook" --nologo`
Expected: PASS (both).

- [ ] **Step 5: Commit**

```bash
git add src/Nfty.Cli/CommandFactory.Authoring.cs tests/Nfty.Cli.Tests/AuthoringCommandsTests.cs
git commit -m "$(printf 'feat(cli): new cookbook command\n\nAssemble a .cbk from a CookBookManifest JSON and one .rcp per recipeWeights\nkey (named {id}.rcp). Runs the authoritative Validator and refuses to write\nan invalid book unless --force is given. End-to-end pipeline test proves the\nbuilt book generates.\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

## Task 5: `add variant` + `AddGroup` wiring

**Files:**
- Modify: `src/Nfty.Cli/CommandFactory.Authoring.cs` (add `AddGroup()` + `AddVariant()`)
- Modify: `src/Nfty.Cli/CommandFactory.cs` (register `AddGroup()`)
- Modify: `tests/Nfty.Cli.Tests/AuthoringCommandsTests.cs`

**Interfaces:**
- Consumes: `IngredientArchive.Read(string)` / `IngredientArchive.Write(string, ...)`; `Validator.ValidateIngredient`; `Image.Load<Rgba32>`.
- Produces: `CommandFactory.AddGroup() : Command` (with a `variant` subcommand).

- [ ] **Step 1: Write the failing test**

Add to `AuthoringCommandsTests.cs`:

```csharp
    [Fact]
    public void Add_variant_appends_a_variant_and_image_to_an_igt()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string ings = Path.Combine(tmp.FullName, "ings");
            Directory.CreateDirectory(ings);
            BuildIgt(ings, "aura", LayerKind.Dynamic, Hsv(), "glow");
            string igt = Path.Combine(ings, "aura.igt");

            string sparkPng = Path.Combine(tmp.FullName, "spark.png");
            using (var img = new Image<Rgba32>(4, 4, new Rgba32(150, 150, 150, 255)))
                img.Save(sparkPng, new PngEncoder());

            int code = Run("add", "variant", igt, "--id", "spark", "--name", "Spark",
                "--weight", "2", "--image", sparkPng);
            Assert.Equal(0, code);

            using var loaded = IngredientArchive.Read(igt);
            Assert.Equal(2, loaded.Manifest.Variants.Count);
            var spark = loaded.Manifest.Variants.Single(v => v.Id == "spark");
            Assert.Equal("Spark", spark.Name);
            Assert.Equal(2, spark.Weight);
            Assert.True(loaded.VariantImages.ContainsKey("spark"));
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Add_variant_rejects_a_duplicate_id()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string ings = Path.Combine(tmp.FullName, "ings");
            Directory.CreateDirectory(ings);
            BuildIgt(ings, "aura", LayerKind.Dynamic, Hsv(), "glow");
            string igt = Path.Combine(ings, "aura.igt");
            string png = Path.Combine(tmp.FullName, "glow.png");
            using (var img = new Image<Rgba32>(4, 4, new Rgba32(150, 150, 150, 255)))
                img.Save(png, new PngEncoder());

            var ex = Assert.Throws<InvalidOperationException>(() =>
                Run("add", "variant", igt, "--id", "glow", "--weight", "1", "--image", png));
            Assert.Contains("glow", ex.Message);
        }
        finally { tmp.Delete(recursive: true); }
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Nfty.Cli.Tests --filter "FullyQualifiedName~Add_variant" --nologo`
Expected: FAIL — `add` is not a known command.

- [ ] **Step 3: Add `AddGroup()` and `AddVariant()`**

In `CommandFactory.Authoring.cs`:

```csharp
    /// <summary>The `add` command group: append a single item into an existing archive.</summary>
    public static Command AddGroup()
    {
        var group = new Command("add", "Append a variant / ingredient / recipe to an existing archive.");
        group.Subcommands.Add(AddVariant());
        return group;
    }

    private static Command AddVariant()
    {
        var igtPath = new Argument<string>("igt") { Description = "Path to the .igt to modify in place." };
        var id = new Option<string>("--id") { Description = "New variant id (must be unique in the ingredient).", Required = true };
        var name = new Option<string?>("--name") { Description = "Display name (defaults to the id)." };
        var weight = new Option<double>("--weight") { Description = "Variant weight (zero or greater).", Required = true };
        var image = new Option<string>("--image") { Description = "PNG for this variant.", Required = true };
        var cmd = new Command("variant", "Add one variant (id, weight, image) to an existing .igt.")
            { igtPath, id, name, weight, image };
        cmd.SetAction(parse =>
        {
            string path = parse.GetValue(igtPath)!;
            string vid = parse.GetValue(id)!;
            using var existing = IngredientArchive.Read(path);
            if (existing.Manifest.Variants.Any(v => string.Equals(v.Id, vid, StringComparison.Ordinal)))
                throw new InvalidOperationException(
                    $"Ingredient '{existing.Manifest.Id}' already has a variant '{vid}'.");

            var newImg = Image.Load<Rgba32>(parse.GetValue(image)!);
            try
            {
                var images = new Dictionary<string, Image<Rgba32>>(existing.VariantImages) { [vid] = newImg };
                var variants = existing.Manifest.Variants
                    .Append(new Variant(vid, parse.GetValue(name) ?? vid, parse.GetValue(weight)))
                    .ToList();
                var manifest = existing.Manifest with { Variants = variants };

                var merged = new LoadedIngredient { Manifest = manifest, VariantImages = images };
                var problems = Validator.ValidateIngredient(merged);
                if (problems.Count > 0) { Report(problems); return 1; }

                // Read(path) closed its file handle before returning, so overwriting is safe.
                IngredientArchive.Write(path, manifest, images);
                Console.WriteLine($"Added variant '{vid}' to {path}");
                return 0;
            }
            finally { newImg.Dispose(); }
        });
        return cmd;
    }
```

- [ ] **Step 4: Register `AddGroup()` in `Build()`**

In `CommandFactory.cs` `Build()`, after the `NewGroup()` line:

```csharp
        root.Subcommands.Add(AddGroup());
```

- [ ] **Step 5: Run the variant tests**

Run: `dotnet test tests/Nfty.Cli.Tests --filter "FullyQualifiedName~Add_variant" --nologo`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Nfty.Cli/CommandFactory.Authoring.cs src/Nfty.Cli/CommandFactory.cs tests/Nfty.Cli.Tests/AuthoringCommandsTests.cs
git commit -m "$(printf 'feat(cli): add variant command\n\nAppend one variant (id, name, weight, image) to an existing .igt via\nread-modify-write, re-validated with Validator.ValidateIngredient and\nrejecting a duplicate id.\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

## Task 6: `add ingredient` (with `--index`)

**Files:**
- Modify: `src/Nfty.Cli/CommandFactory.Authoring.cs` (add `AddIngredient()`, register in `AddGroup`)
- Modify: `tests/Nfty.Cli.Tests/AuthoringCommandsTests.cs`

**Interfaces:**
- Consumes: `RecipeArchive.Read`/`Write`; `IngredientArchive.Read`; `Validator.ValidateRecipe`.
- Produces: an `ingredient` subcommand under `add`.

- [ ] **Step 1: Write the failing test (index placement + duplicate rejection)**

Add to `AuthoringCommandsTests.cs`:

```csharp
    [Fact]
    public void Add_ingredient_inserts_a_layer_at_the_given_index()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string ings = Path.Combine(tmp.FullName, "ings");
            Directory.CreateDirectory(ings);
            BuildIgt(ings, "bg", LayerKind.Dynamic, Hsv(), "sky");
            BuildIgt(ings, "aura", LayerKind.Dynamic, Hsv(), "glow");
            BuildIgt(ings, "mid", LayerKind.Dynamic, Hsv(), "m");

            string rcps = Path.Combine(tmp.FullName, "rcps");
            Directory.CreateDirectory(rcps);
            BuildRcp(rcps, ings, "cat", "bg", "aura");
            string rcp = Path.Combine(rcps, "cat.rcp");

            int code = Run("add", "ingredient", rcp, "--igt", Path.Combine(ings, "mid.igt"), "--index", "1");
            Assert.Equal(0, code);

            using var loaded = RecipeArchive.Read(rcp);
            Assert.Equal(new[] { "bg", "mid", "aura" }, loaded.Manifest.LayerOrder);
            Assert.Equal(3, loaded.Ingredients.Count);
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Add_ingredient_appends_when_no_index_given()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string ings = Path.Combine(tmp.FullName, "ings");
            Directory.CreateDirectory(ings);
            BuildIgt(ings, "bg", LayerKind.Dynamic, Hsv(), "sky");
            BuildIgt(ings, "aura", LayerKind.Dynamic, Hsv(), "glow");
            string rcps = Path.Combine(tmp.FullName, "rcps");
            Directory.CreateDirectory(rcps);
            BuildRcp(rcps, ings, "cat", "bg");
            string rcp = Path.Combine(rcps, "cat.rcp");

            Assert.Equal(0, Run("add", "ingredient", rcp, "--igt", Path.Combine(ings, "aura.igt")));
            using var loaded = RecipeArchive.Read(rcp);
            Assert.Equal(new[] { "bg", "aura" }, loaded.Manifest.LayerOrder);
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Add_ingredient_rejects_a_duplicate_layer()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string ings = Path.Combine(tmp.FullName, "ings");
            Directory.CreateDirectory(ings);
            BuildIgt(ings, "bg", LayerKind.Dynamic, Hsv(), "sky");
            string rcps = Path.Combine(tmp.FullName, "rcps");
            Directory.CreateDirectory(rcps);
            BuildRcp(rcps, ings, "cat", "bg");
            string rcp = Path.Combine(rcps, "cat.rcp");

            var ex = Assert.Throws<InvalidOperationException>(() =>
                Run("add", "ingredient", rcp, "--igt", Path.Combine(ings, "bg.igt")));
            Assert.Contains("bg", ex.Message);
        }
        finally { tmp.Delete(recursive: true); }
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Nfty.Cli.Tests --filter "FullyQualifiedName~Add_ingredient" --nologo`
Expected: FAIL — `add ingredient` is not a known command.

- [ ] **Step 3: Add `AddIngredient()` and register it**

Register in `AddGroup()`:

```csharp
        group.Subcommands.Add(AddIngredient());
```

Add the builder:

```csharp
    private static Command AddIngredient()
    {
        var rcpPath = new Argument<string>("rcp") { Description = "Path to the .rcp to modify in place." };
        var igt = new Option<string>("--igt") { Description = "Path to the .igt to add as a layer.", Required = true };
        var index = new Option<int?>("--index")
        {
            Description = "0-based position in layerOrder to insert at (default: end).",
        };
        index.Validators.Add(r =>
        {
            var v = r.GetValueOrDefault<int?>();
            if (v is < 0) r.AddError("--index must be zero or greater.");
        });
        var cmd = new Command("ingredient", "Add an .igt as a layer of an existing .rcp.")
            { rcpPath, igt, index };
        cmd.SetAction(parse =>
        {
            string path = parse.GetValue(rcpPath)!;
            using var recipe = RecipeArchive.Read(path);
            using var newIng = IngredientArchive.Read(parse.GetValue(igt)!);
            string id = newIng.Manifest.Id;

            if (recipe.Manifest.LayerOrder.Contains(id, StringComparer.Ordinal)
                || recipe.Ingredients.Any(i => string.Equals(i.Manifest.Id, id, StringComparison.Ordinal)))
                throw new InvalidOperationException(
                    $"Recipe '{recipe.Manifest.Id}' already has an ingredient '{id}'.");

            var order = recipe.Manifest.LayerOrder.ToList();
            int at = parse.GetValue(index) ?? order.Count;
            if (at > order.Count)
                throw new InvalidOperationException(
                    $"--index {at} is past the end; layerOrder has {order.Count} layer(s).");
            order.Insert(at, id);

            var ingredients = recipe.Ingredients.Append(newIng).ToList();
            var manifest = recipe.Manifest with { LayerOrder = order };
            var merged = new LoadedRecipe { Manifest = manifest, Ingredients = ingredients };
            var problems = Validator.ValidateRecipe(merged);
            if (problems.Count > 0) { Report(problems); return 1; }

            RecipeArchive.Write(path, manifest, ingredients);
            Console.WriteLine($"Added ingredient '{id}' to {path} at index {at}");
            return 0;
        });
        return cmd;
    }
```

- [ ] **Step 4: Run the ingredient-add tests**

Run: `dotnet test tests/Nfty.Cli.Tests --filter "FullyQualifiedName~Add_ingredient" --nologo`
Expected: PASS (all three).

- [ ] **Step 5: Commit**

```bash
git add src/Nfty.Cli/CommandFactory.Authoring.cs tests/Nfty.Cli.Tests/AuthoringCommandsTests.cs
git commit -m "$(printf 'feat(cli): add ingredient command\n\nSplice an .igt into an existing .rcp, inserting its id into layerOrder at\n--index (default end), re-validated with Validator.ValidateRecipe and\nrejecting a duplicate layer.\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

## Task 7: `add recipe` (with `--force`)

**Files:**
- Modify: `src/Nfty.Cli/CommandFactory.Authoring.cs` (add `AddRecipe()`, register in `AddGroup`)
- Modify: `tests/Nfty.Cli.Tests/AuthoringCommandsTests.cs`

**Interfaces:**
- Consumes: `CookBookArchive.Read`/`Write`; `RecipeArchive.Read`; `Validator.Validate(LoadedCookBook)`.
- Produces: a `recipe` subcommand under `add`.

- [ ] **Step 1: Write the failing test**

Add to `AuthoringCommandsTests.cs`. Reuses the `new cookbook` pipeline to make a base book, then adds a second recipe:

```csharp
    [Fact]
    public void Add_recipe_adds_a_recipe_and_weight_to_a_cookbook()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string ings = Path.Combine(tmp.FullName, "ings");
            Directory.CreateDirectory(ings);
            BuildIgt(ings, "bg", LayerKind.Dynamic, Hsv(), "sky");
            BuildIgt(ings, "aura", LayerKind.Dynamic, Hsv(), "glow");

            string rcps = Path.Combine(tmp.FullName, "rcps");
            Directory.CreateDirectory(rcps);
            BuildRcp(rcps, ings, "cat", "bg");
            BuildRcp(rcps, ings, "dog", "aura");

            var cbk = new CookBookManifest("cb", "Book", new Dimensions(4, 4),
                new Nfty.Core.Model.Collection("Book", "", "BK"),
                new Dictionary<string, double> { ["cat"] = 100 });
            string cbkPath = WriteJson(tmp.FullName, "book.json", cbk);
            string outPath = Path.Combine(tmp.FullName, "book.cbk");
            Assert.Equal(0, Run("new", "cookbook", outPath, "--manifest", cbkPath, "--recipes", rcps));

            int code = Run("add", "recipe", outPath, "--rcp", Path.Combine(rcps, "dog.rcp"), "--weight", "50");
            Assert.Equal(0, code);

            using var book = CookBookArchive.Read(outPath);
            Assert.Equal(2, book.Recipes.Count);
            Assert.Equal(50, book.Manifest.RecipeWeights["dog"]);
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Add_recipe_rejects_a_duplicate_recipe_id()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string ings = Path.Combine(tmp.FullName, "ings");
            Directory.CreateDirectory(ings);
            BuildIgt(ings, "bg", LayerKind.Dynamic, Hsv(), "sky");
            string rcps = Path.Combine(tmp.FullName, "rcps");
            Directory.CreateDirectory(rcps);
            BuildRcp(rcps, ings, "cat", "bg");

            var cbk = new CookBookManifest("cb", "Book", new Dimensions(4, 4),
                new Nfty.Core.Model.Collection("Book", "", "BK"),
                new Dictionary<string, double> { ["cat"] = 100 });
            string cbkPath = WriteJson(tmp.FullName, "book.json", cbk);
            string outPath = Path.Combine(tmp.FullName, "book.cbk");
            Assert.Equal(0, Run("new", "cookbook", outPath, "--manifest", cbkPath, "--recipes", rcps));

            var ex = Assert.Throws<InvalidOperationException>(() =>
                Run("add", "recipe", outPath, "--rcp", Path.Combine(rcps, "cat.rcp"), "--weight", "10"));
            Assert.Contains("cat", ex.Message);
        }
        finally { tmp.Delete(recursive: true); }
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Nfty.Cli.Tests --filter "FullyQualifiedName~Add_recipe" --nologo`
Expected: FAIL — `add recipe` is not a known command.

- [ ] **Step 3: Add `AddRecipe()` and register it**

Register in `AddGroup()`:

```csharp
        group.Subcommands.Add(AddRecipe());
```

Add the builder:

```csharp
    private static Command AddRecipe()
    {
        var cbkPath = new Argument<string>("cbk") { Description = "Path to the .cbk to modify in place." };
        var rcp = new Option<string>("--rcp") { Description = "Path to the .rcp to add.", Required = true };
        var weight = new Option<double>("--weight") { Description = "Recipe roll weight (zero or greater).", Required = true };
        var force = new Option<bool>("--force")
        {
            Description = "Write even if validation reports problems (printed as warnings).",
        };
        var cmd = new Command("recipe", "Add a .rcp to an existing .cbk with a roll weight.")
            { cbkPath, rcp, weight, force };
        cmd.SetAction(parse =>
        {
            string path = parse.GetValue(cbkPath)!;
            using var book = CookBookArchive.Read(path);
            using var newRcp = RecipeArchive.Read(parse.GetValue(rcp)!);
            string id = newRcp.Manifest.Id;

            if (book.Recipes.Any(r => string.Equals(r.Manifest.Id, id, StringComparison.Ordinal))
                || book.Manifest.RecipeWeights.ContainsKey(id))
                throw new InvalidOperationException(
                    $"CookBook '{book.Manifest.Id}' already has a recipe '{id}'.");

            var weights = new Dictionary<string, double>(book.Manifest.RecipeWeights) { [id] = parse.GetValue(weight) };
            var recipes = book.Recipes.Append(newRcp).ToList();
            var manifest = book.Manifest with { RecipeWeights = weights };
            var merged = new LoadedCookBook { Manifest = manifest, Recipes = recipes, SourceSha256 = null };

            var problems = Validator.Validate(merged);
            if (problems.Count > 0)
            {
                Report(problems);
                if (!parse.GetValue(force)) return 1;
                Console.Error.WriteLine("--force: writing despite the problems above.");
            }

            CookBookArchive.Write(path, manifest, recipes);
            Console.WriteLine($"Added recipe '{id}' (weight {parse.GetValue(weight)}) to {path}");
            return 0;
        });
        return cmd;
    }
```

- [ ] **Step 4: Run the recipe-add tests**

Run: `dotnet test tests/Nfty.Cli.Tests --filter "FullyQualifiedName~Add_recipe" --nologo`
Expected: PASS (both).

- [ ] **Step 5: Run the whole solution suite**

Run: `dotnet build nfty.sln --nologo && dotnet test nfty.sln --nologo`
Expected: Build succeeded, 0 warnings; all tests PASS (Core + CLI).

- [ ] **Step 6: Smoke-test the pipeline against the real binary**

Run (bash):
```bash
dotnet run --project src/Nfty.Cli -- new --help
dotnet run --project src/Nfty.Cli -- add --help
```
Expected: `new` lists subcommands `ingredient`/`recipe`/`cookbook`; `add` lists `variant`/`ingredient`/`recipe`.

- [ ] **Step 7: Commit**

```bash
git add src/Nfty.Cli/CommandFactory.Authoring.cs tests/Nfty.Cli.Tests/AuthoringCommandsTests.cs
git commit -m "$(printf 'feat(cli): add recipe command\n\nAdd a .rcp to an existing .cbk with a roll weight, running the authoritative\nValidator and refusing an invalid result unless --force is given. Completes\nthe new/add authoring surface.\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

## Task 8: Docs — mark authoring shipped

**Files:**
- Modify: `docs/superpowers/specs/2026-07-10-nfty-core-engine-design.md:256`
- Modify: `CLAUDE.md` (the `Nfty.Cli` command list)

**Interfaces:** none.

- [ ] **Step 1: Update the core-engine spec §7 note**

Replace the "Authoring commands (`new`, `add`) … deferred thin follow-up" sentence
(`docs/superpowers/specs/2026-07-10-nfty-core-engine-design.md:256`) with:

```markdown
Authoring commands `new ingredient|recipe|cookbook` and `add variant|ingredient|recipe` build and
mutate archives from manifest JSON plus PNGs (import-based; the GUI owns draw-based authoring). See
`2026-07-21-nfty-authoring-cli-design.md`.
```

- [ ] **Step 2: Update the CLAUDE.md command list**

In `CLAUDE.md`, find the sentence listing CLI commands (`Commands: inspect … generate, extend. Authoring commands (new, add) are a deferred follow-up …`) and replace the deferral clause so it reads:

```markdown
Commands: `inspect`, `validate`, `stats`, `preview`, `generate`, `extend`, plus authoring — `new ingredient|recipe|cookbook` and `add variant|ingredient|recipe` (manifest JSON + PNGs, resolved by the `{id}.png`→`{id}.igt`→`{id}.rcp` convention; `Validator.ValidateIngredient`/`ValidateRecipe` gate the per-level builds).
```

- [ ] **Step 3: Commit**

```bash
git add docs/superpowers/specs/2026-07-10-nfty-core-engine-design.md CLAUDE.md
git commit -m "$(printf 'docs: authoring CLI commands are shipped\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

## Self-Review notes (for the implementer)

- **Overwrite-in-place safety:** every `add`/mutation reads the target archive with `IngredientArchive.Read(path)` / `RecipeArchive.Read(path)` / `CookBookArchive.Read(path)`, all of which close their file handle before returning (they eagerly decode into memory). Writing back to the same path is therefore safe. Do not hold an open `ZipArchive` over the target while writing.
- **Disposal discipline:** the constructed `merged`/`ing`/`book` `Loaded*` objects only *wrap* images owned elsewhere — never wrap them in `using`. Free the real owners: the loaded children (`foreach … Dispose()` / `using`) and any freshly `Image.Load`ed PNG (`finally`).
- **`System.CommandLine` 2.0.9 patterns** (match the existing `CommandFactory.cs`): `Option<T>` with `Required = true` or `DefaultValueFactory`; `Argument<T>`; `Validators.Add(r => r.AddError(...))`; `parse.GetValue(option)`; `cmd.SetAction(parse => { … return int; })`; nested groups via `command.Subcommands.Add(...)`.
- **Ordinal everywhere** ids are compared (`Contains`, `Any`, `Distinct`, dictionary construction).
