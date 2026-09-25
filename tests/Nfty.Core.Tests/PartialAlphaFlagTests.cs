using System.IO.Compression;
using System.Text.Json;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>
/// <see cref="IngredientManifest.PartialAlpha"/> / <see cref="RecipeManifest.PartialAlpha"/> /
/// <see cref="CookBookManifest.PartialAlpha"/>: a listing cache stamped from the actual pixels on
/// every write, never trusted on read.
/// </summary>
/// <remarks>
/// The controller shape is a writer/reader pair: a writer's caller-supplied flag must never survive
/// a write (it is recomputed from the images), and a reader must never trust whatever the file says
/// (it is recomputed from the decoded images too) — so a hand-tampered archive cannot lie to either
/// path. <see cref="ArchivePeek"/> is the one deliberate exception: it decodes nothing, so it hands
/// back the stored value, which is exactly what a writer stamped.
/// </remarks>
public class PartialAlphaFlagTests
{
    private static string Dir() => Directory.CreateTempSubdirectory().FullName;

    private static Image<Rgba32> Solid(int w, int h, byte alpha) => new(w, h, new Rgba32(10, 20, 30, alpha));

    private static LoadedIngredient Ing(string id, string name, byte alpha, bool claimPartial = false)
    {
        var images = new Dictionary<string, Image<Rgba32>>(StringComparer.Ordinal)
        {
            [id] = Solid(4, 4, alpha),
        };
        return new LoadedIngredient
        {
            Manifest = new IngredientManifest(id, name, LayerKind.Custom, null,
                new[] { new Variant(id, name, 1) }, PartialAlpha: claimPartial),
            VariantImages = images,
        };
    }

    /// <summary>Reads a manifest.json entry out of an archive as a raw JSON document, without going
    /// through <c>ArchiveIo</c> — this is the reader-independent ground truth an archive's own bytes
    /// carry, which is what the writer-stamping tests need to check.</summary>
    private static JsonDocument ReadRawManifest(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("manifest.json")!;
        using var s = entry.Open();
        return JsonDocument.Parse(s);
    }

    [Fact]
    public void The_writer_stamps_the_ingredient_flag_from_pixels_whatever_the_caller_said()
    {
        var dir = Dir();
        try
        {
            var path = Path.Combine(dir, "aura.igt");
            using var ing = Ing("aura", "Aura", alpha: 128, claimPartial: false);

            IngredientArchive.Write(path, ing.Manifest, ing.VariantImages, DepthMaps.None);

            using var doc = ReadRawManifest(path);
            Assert.True(doc.RootElement.GetProperty("partialAlpha").GetBoolean());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void The_writer_stamps_the_recipe_flag_from_its_ingredients_pixels()
    {
        var dir = Dir();
        try
        {
            var dirtyPath = Path.Combine(dir, "dirty.rcp");
            using (var ing = Ing("aura", "Aura", alpha: 128))
                RecipeArchive.Write(dirtyPath, new RecipeManifest("r", "R", new[] { "aura" },
                    Array.Empty<IncompatibilityRule>(), PartialAlpha: false), new[] { ing });

            using var dirtyDoc = ReadRawManifest(dirtyPath);
            Assert.True(dirtyDoc.RootElement.GetProperty("partialAlpha").GetBoolean());

            var cleanPath = Path.Combine(dir, "clean.rcp");
            using (var ing = Ing("aura", "Aura", alpha: 255))
                RecipeArchive.Write(cleanPath, new RecipeManifest("r", "R", new[] { "aura" },
                    Array.Empty<IncompatibilityRule>(), PartialAlpha: true), new[] { ing });

            using var cleanDoc = ReadRawManifest(cleanPath);
            Assert.False(cleanDoc.RootElement.GetProperty("partialAlpha").GetBoolean());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void The_writer_stamps_the_cookbook_flag_from_its_recipes_pixels()
    {
        var dir = Dir();
        try
        {
            LoadedRecipe Recipe(byte alpha) => new()
            {
                Manifest = new RecipeManifest("r", "R", new[] { "aura" },
                    Array.Empty<IncompatibilityRule>()),
                Ingredients = new[] { Ing("aura", "Aura", alpha) },
            };

            var dirtyPath = Path.Combine(dir, "dirty.cbk");
            using (var recipe = Recipe(128))
                CookBookArchive.Write(dirtyPath,
                    new CookBookManifest("book", "Book", new Dimensions(4, 4),
                        new Collection("Book", "desc", "BOOK"),
                        new Dictionary<string, double> { ["r"] = 1 }, PartialAlpha: false),
                    new[] { recipe });

            using var dirtyDoc = ReadRawManifest(dirtyPath);
            Assert.True(dirtyDoc.RootElement.GetProperty("partialAlpha").GetBoolean());

            var cleanPath = Path.Combine(dir, "clean.cbk");
            using (var recipe = Recipe(255))
                CookBookArchive.Write(cleanPath,
                    new CookBookManifest("book", "Book", new Dimensions(4, 4),
                        new Collection("Book", "desc", "BOOK"),
                        new Dictionary<string, double> { ["r"] = 1 }, PartialAlpha: true),
                    new[] { recipe });

            using var cleanDoc = ReadRawManifest(cleanPath);
            Assert.False(cleanDoc.RootElement.GetProperty("partialAlpha").GetBoolean());
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>Overwrites an archive's manifest.json with hand-edited bytes — the way a hand-edited
    /// (or otherwise tampered) archive would carry a stored flag the pixels disagree with.</summary>
    private static void ReplaceManifestJson(string path, string json)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
        zip.GetEntry("manifest.json")!.Delete();
        using var s = new StreamWriter(zip.CreateEntry("manifest.json").Open());
        s.Write(json);
    }

    [Fact]
    public void A_reader_recomputes_rather_than_trusting_the_stored_flag()
    {
        var dir = Dir();
        try
        {
            var path = Path.Combine(dir, "aura.igt");
            using (var ing = Ing("aura", "Aura", alpha: 255))
                IngredientArchive.Write(path, ing.Manifest, ing.VariantImages, DepthMaps.None);

            // The write above stamped false (the pixel is fully opaque). Hand-edit the stored
            // manifest text to lie about it — a plain string swap, so the test does not depend on
            // any of the reader machinery it is trying to distrust.
            string rawJson;
            using (var zip = ZipFile.OpenRead(path))
            using (var s = new StreamReader(zip.GetEntry("manifest.json")!.Open()))
                rawJson = s.ReadToEnd();
            Assert.Contains("\"partialAlpha\": false", rawJson);
            var tamperedJson = rawJson.Replace("\"partialAlpha\": false", "\"partialAlpha\": true");
            ReplaceManifestJson(path, tamperedJson);

            using var doc2 = ReadRawManifest(path);
            Assert.True(doc2.RootElement.GetProperty("partialAlpha").GetBoolean());

            using var loaded = IngredientArchive.Read(path);
            Assert.False(loaded.Manifest.PartialAlpha);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Peek_reads_the_stored_flag_without_decoding()
    {
        var dir = Dir();
        try
        {
            var path = Path.Combine(dir, "book.cbk");
            var recipe = new LoadedRecipe
            {
                Manifest = new RecipeManifest("r", "R", new[] { "aura" },
                    Array.Empty<IncompatibilityRule>()),
                Ingredients = new[] { Ing("aura", "Aura", alpha: 128) },
            };
            using (recipe)
                CookBookArchive.Write(path,
                    new CookBookManifest("book", "Book", new Dimensions(4, 4),
                        new Collection("Book", "desc", "BOOK"),
                        new Dictionary<string, double> { ["r"] = 1 }),
                    new[] { recipe });

            var tree = ArchivePeek.CookBookTree(path);

            Assert.True(tree.Manifest.PartialAlpha);
            Assert.True(tree.Recipes[0].Manifest.PartialAlpha);
            Assert.True(tree.Recipes[0].Ingredients[0].PartialAlpha);
        }
        finally { Directory.Delete(dir, true); }
    }
}
