using System.IO.Compression;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>
/// Reading an archive's manifest without touching what is inside it.
/// </summary>
/// <remarks>
/// The point is what it does NOT do. A workspace listing has to name every archive in a folder, and
/// <c>CookBookArchive.Read</c> decodes every variant PNG in the tree — which is why
/// <c>KitchenContents</c> holds paths in the first place. These prove the peek stays out of the
/// images, not merely that it returns the right record.
/// </remarks>
public class ArchivePeekTests
{
    private static string Dir() => Directory.CreateTempSubdirectory().FullName;

    private static LoadedIngredient Ing(string id, string name, LayerKind kind, int variants)
    {
        var images = new Dictionary<string, Image<Rgba32>>(StringComparer.Ordinal);
        var vs = new List<Variant>();
        for (int i = 1; i <= variants; i++)
        {
            images[$"v{i}"] = new Image<Rgba32>(4, 4, new Rgba32(1, 2, 3, 255));
            vs.Add(new Variant($"v{i}", $"V{i}", 1));
        }
        return new LoadedIngredient
        {
            Manifest = new IngredientManifest(id, name, kind,
                kind == LayerKind.Custom ? null
                    : new Colorization(ColorModel.Hsv, 12, 4,
                        new[] { new ColorEntry(1, new ColorRange(0, 360, 40, 100), null) }),
                vs),
            VariantImages = images,
        };
    }

    private static string WriteBook(string dir, params string[] recipeIds)
    {
        var recipes = recipeIds.Select(rid => new LoadedRecipe
        {
            Manifest = new RecipeManifest(rid, rid.ToUpperInvariant(), new[] { "aura" },
                Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { Ing("aura", "Aura", LayerKind.Custom, 2) },
        }).ToList();

        var path = Path.Combine(dir, "book.cbk");
        CookBookArchive.Write(path, new CookBookManifest("cb", "VaporPets", new Dimensions(1000, 1000),
            new Collection("VaporPets", "", "VP"),
            recipeIds.ToDictionary(r => r, _ => 100.0)), recipes);
        foreach (var r in recipes) r.Dispose();
        return path;
    }

    [Fact]
    public void A_cookbook_peek_gives_everything_a_listing_row_needs()
    {
        var dir = Dir();
        try
        {
            var manifest = ArchivePeek.CookBook(WriteBook(dir, "cat", "dog", "bird"));

            Assert.Equal("VaporPets", manifest.Name);
            // The two facts a Recent/Kitchen row prints — both in the OUTER manifest, so the row is
            // rich without a single nested read.
            Assert.Equal(3, manifest.RecipeWeights.Count);
            Assert.Equal(1000, manifest.Canvas.Width);
            Assert.Equal(1000, manifest.Canvas.Height);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_recipe_peek_gives_its_layer_count()
    {
        var dir = Dir();
        try
        {
            var path = Path.Combine(dir, "cat.rcp");
            using var ing = Ing("aura", "Aura", LayerKind.Custom, 1);
            RecipeArchive.Write(path, new RecipeManifest("cat", "Cat", new[] { "aura" },
                Array.Empty<IncompatibilityRule>()), new[] { ing });

            var manifest = ArchivePeek.Recipe(path);

            Assert.Equal("Cat", manifest.Name);
            Assert.Single(manifest.LayerOrder);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void An_ingredient_peek_gives_its_kind_and_variant_count()
    {
        var dir = Dir();
        try
        {
            var path = Path.Combine(dir, "aura.igt");
            using var ing = Ing("aura", "Aura", LayerKind.Dynamic, 4);
            IngredientArchive.Write(path, ing.Manifest, ing.VariantImages);

            var manifest = ArchivePeek.Ingredient(path);

            Assert.Equal("Aura", manifest.Name);
            Assert.Equal(LayerKind.Dynamic, manifest.Kind);
            Assert.Equal(4, manifest.Variants.Count);
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>
    /// The load-bearing one: a peek must not decode, or even open, a single image.
    /// </summary>
    /// <remarks>
    /// Proved by corrupting the PNG bytes inside a written archive and peeking it anyway. A full
    /// <c>Read</c> of the same file throws; the peek does not, which it could only manage by never
    /// having touched the image. That is a stronger claim than timing it, and it does not flake.
    /// </remarks>
    [Fact]
    public void A_peek_reads_no_image_at_all()
    {
        var dir = Dir();
        try
        {
            var path = Path.Combine(dir, "aura.igt");
            using (var ing = Ing("aura", "Aura", LayerKind.Dynamic, 2))
                IngredientArchive.Write(path, ing.Manifest, ing.VariantImages);

            // Replace one variant's PNG bytes with something that is not a PNG.
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
            {
                var entry = zip.Entries.First(e => e.FullName.EndsWith(".png", StringComparison.Ordinal));
                var name = entry.FullName;
                entry.Delete();
                using var s = new StreamWriter(zip.CreateEntry(name).Open());
                s.Write("this is not a png");
            }

            // The peek is untroubled...
            var manifest = ArchivePeek.Ingredient(path);
            Assert.Equal("Aura", manifest.Name);
            Assert.Equal(2, manifest.Variants.Count);

            // ...and a real read is not, which is what makes the line above mean something.
            Assert.ThrowsAny<Exception>(() => IngredientArchive.Read(path).Dispose());
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>A peek goes through the same <c>ArchiveIo.ReadManifest</c> gate as every other reader,
    /// so a future schema is refused here too rather than half-understood.</summary>
    [Fact]
    public void A_newer_schema_is_refused_by_the_peek_as_well()
    {
        var dir = Dir();
        try
        {
            var path = Path.Combine(dir, "aura.igt");
            using (var ing = Ing("aura", "Aura", LayerKind.Dynamic, 1))
                IngredientArchive.Write(path, ing.Manifest, ing.VariantImages);

            using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
            {
                var entry = zip.Entries.First(e => e.FullName.EndsWith("manifest.json", StringComparison.Ordinal));
                string json;
                using (var r = new StreamReader(entry.Open())) json = r.ReadToEnd();
                json = json.Replace($"\"schemaVersion\": {Schema.Current}",
                                    $"\"schemaVersion\": {Schema.Current + 1}", StringComparison.Ordinal);
                var name = entry.FullName;
                entry.Delete();
                using var w = new StreamWriter(zip.CreateEntry(name).Open());
                w.Write(json);
            }

            Assert.Throws<UnsupportedSchemaVersionException>(() => ArchivePeek.Ingredient(path));
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>
    /// The deep peek reaches every nested manifest, and still opens no image.
    /// </summary>
    /// <remarks>
    /// Same proof as <see cref="A_peek_reads_no_image_at_all"/>, one level in: the PNG bytes inside
    /// the nested ingredient are replaced with rubbish, and the whole tree still reads. That is what
    /// makes it safe for a probability figure to be computed from a book a user is merely LOOKING at
    /// — the alternative pulls every variant in the collection into memory to read a few dozen
    /// weights.
    /// </remarks>
    [Fact]
    public void A_deep_peek_reads_every_nested_manifest_and_no_image()
    {
        var dir = Dir();
        try
        {
            var path = WriteBook(dir, "cat", "dog");

            // Rubbish in place of every variant PNG, at the bottom of a .cbk > .rcp > .igt nest.
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
            {
                var entry = zip.Entries.First(e => e.FullName.StartsWith("recipes/", StringComparison.Ordinal));
                var name = entry.FullName;
                entry.Delete();
                using var s = new StreamWriter(zip.CreateEntry(name).Open());
                s.Write("this is not an archive");
            }

            // One recipe is now unreadable, so a full Read fails...
            Assert.ThrowsAny<Exception>(() => CookBookArchive.Read(path).Dispose());

            // ...which is what makes this next line worth something. Rewrite it intact and peek.
            Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            path = WriteBook(dir, "cat", "dog");

            var book = ArchivePeek.CookBookTree(path);

            Assert.Equal("VaporPets", book.Manifest.Name);
            Assert.Equal(2, book.Recipes.Count);
            Assert.Equal(new[] { "cat", "dog" }, book.Recipes.Select(r => r.Manifest.Id));
            Assert.All(book.Recipes, r =>
            {
                Assert.Equal(new[] { "aura" }, r.Manifest.LayerOrder);
                var ing = Assert.Single(r.Ingredients);
                Assert.Equal("Aura", ing.Name);
                Assert.Equal(2, ing.Variants.Count);
            });
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>
    /// The projection off an already-open book says the same thing the archive does, or the two ways
    /// of getting a <see cref="PeekedCookBook"/> would disagree about the same file.
    /// </summary>
    /// <remarks>
    /// Compared member by member rather than with one <c>Assert.Equal</c> on the records: a record
    /// holding an <c>IReadOnlyList</c> does NOT compare structurally, so the whole-object compare
    /// passes only when both sides share list instances — which these never do, and which would make
    /// the assertion a test of object identity dressed as a test of content.
    /// </remarks>
    [Fact]
    public void Peeking_a_file_and_projecting_an_open_book_agree()
    {
        var dir = Dir();
        try
        {
            var path = WriteBook(dir, "cat", "dog");
            var peeked = ArchivePeek.CookBookTree(path);
            using var loaded = CookBookArchive.Read(path);
            var projected = PeekedCookBook.Of(loaded);

            Assert.Equal(peeked.Manifest.Name, projected.Manifest.Name);
            Assert.Equal(peeked.Manifest.RecipeWeights, projected.Manifest.RecipeWeights);
            Assert.Equal(peeked.Recipes.Select(r => r.Manifest.Id),
                         projected.Recipes.Select(r => r.Manifest.Id));
            Assert.Equal(
                peeked.Recipes.SelectMany(r => r.Ingredients).Select(i => (i.Id, i.Kind, i.Variants.Count)),
                projected.Recipes.SelectMany(r => r.Ingredients).Select(i => (i.Id, i.Kind, i.Variants.Count)));
        }
        finally { Directory.Delete(dir, true); }
    }
}
