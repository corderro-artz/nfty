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
}
