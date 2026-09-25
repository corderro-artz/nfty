using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>Pins the schema gate per archive type, now that <see cref="Schema.Oldest"/> is 2:
/// a v2 manifest reads, a v1 manifest is refused by name (Rise, 2026-09-25 — v1 dropped, see
/// CLAUDE.md), and anything newer than <see cref="Schema.Current"/> is refused as too new.</summary>
public class SchemaVersionTests
{
    private static string TempPath(string name) =>
        Path.Combine(Directory.CreateTempSubdirectory().FullName, name);

    private static LoadedIngredient Ingredient(int schemaVersion = Schema.Current) => new()
    {
        Manifest = new IngredientManifest("bg", "BG", LayerKind.Custom, null,
            new[] { new Variant("a", "A", 1) }, SchemaVersion: schemaVersion),
        VariantImages = new Dictionary<string, Image<Rgba32>>
        {
            ["a"] = new Image<Rgba32>(2, 2, new Rgba32(1, 2, 3, 255)),
        },
    };

    private static LoadedRecipe Recipe(int schemaVersion = Schema.Current) => new()
    {
        Manifest = new RecipeManifest("cat", "Cat", new[] { "bg" },
            Array.Empty<IncompatibilityRule>(), SchemaVersion: schemaVersion),
        Ingredients = new[] { Ingredient() },
    };

    private static CookBookManifest Book(int schemaVersion = Schema.Current) =>
        new("cb", "VaporPets", new Dimensions(2, 2), new Collection("VaporPets", "d", "VP"),
            new Dictionary<string, double> { ["cat"] = 1 }, SchemaVersion: schemaVersion);

    // ---- too new: refused ------------------------------------------------------------------

    [Fact]
    public void Ingredient_with_future_schema_version_is_rejected()
    {
        var path = TempPath("future.igt");
        var ing = Ingredient(schemaVersion: Schema.Current + 1);
        IngredientArchive.Write(path, ing.Manifest, ing.VariantImages, DepthMaps.None);

        var ex = Assert.Throws<UnsupportedSchemaVersionException>(() => IngredientArchive.Read(path));

        Assert.Equal(Schema.Current + 1, ex.Found);
        Assert.Equal(Schema.Current, ex.Supported);
        Assert.Contains((Schema.Current + 1).ToString(), ex.Message);
    }

    [Fact]
    public void Recipe_with_future_schema_version_is_rejected()
    {
        var path = TempPath("future.rcp");
        var recipe = Recipe(schemaVersion: Schema.Current + 1);
        RecipeArchive.Write(path, recipe.Manifest, recipe.Ingredients);

        Assert.Throws<UnsupportedSchemaVersionException>(() => RecipeArchive.Read(path));
    }

    [Fact]
    public void CookBook_with_future_schema_version_is_rejected()
    {
        var path = TempPath("future.cbk");
        CookBookArchive.Write(path, Book(schemaVersion: Schema.Current + 1), new[] { Recipe() });

        Assert.Throws<UnsupportedSchemaVersionException>(() => CookBookArchive.Read(path));
    }

    [Fact]
    public void Nested_ingredient_version_is_checked_through_the_cookbook()
    {
        // A current-schema cookbook whose nested ingredient is one newer must still be rejected,
        // not silently misparsed.
        var path = TempPath("nested.cbk");
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "bg" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { Ingredient(schemaVersion: Schema.Current + 1) },
        };
        CookBookArchive.Write(path, Book(), new[] { recipe });

        Assert.Throws<UnsupportedSchemaVersionException>(() => CookBookArchive.Read(path));
    }

    // ---- too old: refused, v1 dropped -------------------------------------------------------

    /// <summary>Rise, 2026-09-25: <see cref="Schema.Oldest"/> moved to 2, so a v1 manifest is now
    /// refused the same way a too-new one is — by name, not silently misparsed. The message must
    /// name version 1 specifically, since a user hitting this needs to know their archive predates
    /// what this build reads, not merely that something is wrong.</summary>
    [Fact]
    public void Ingredient_with_schema_version_1_is_refused_and_names_it()
    {
        var path = TempPath("old.igt");
        var ing = Ingredient(schemaVersion: 1);
        IngredientArchive.Write(path, ing.Manifest, ing.VariantImages, DepthMaps.None);

        var ex = Assert.Throws<UnsupportedSchemaVersionException>(() => IngredientArchive.Read(path));

        Assert.Equal(1, ex.Found);
        Assert.Equal(Schema.Current, ex.Supported);
        Assert.Contains("schemaVersion 1", ex.Message);
        Assert.Contains("no longer reads", ex.Message);
        Assert.DoesNotContain("supports up to", ex.Message);   // a different failure: too old, not too new
    }

    [Fact]
    public void Recipe_with_schema_version_1_is_refused()
    {
        var path = TempPath("old.rcp");
        var recipe = Recipe(schemaVersion: 1);
        RecipeArchive.Write(path, recipe.Manifest, recipe.Ingredients);

        Assert.Throws<UnsupportedSchemaVersionException>(() => RecipeArchive.Read(path));
    }

    [Fact]
    public void CookBook_with_schema_version_1_is_refused()
    {
        var path = TempPath("old.cbk");
        CookBookArchive.Write(path, Book(schemaVersion: 1), new[] { Recipe() });

        Assert.Throws<UnsupportedSchemaVersionException>(() => CookBookArchive.Read(path));
    }

    // ---- v2 reads ----------------------------------------------------------------------------

    [Fact]
    public void Current_schema_version_round_trips()
    {
        var path = TempPath("ok.cbk");
        CookBookArchive.Write(path, Book(), new[] { Recipe() });

        var loaded = CookBookArchive.Read(path);

        Assert.Equal(Schema.Current, loaded.Manifest.SchemaVersion);
        Assert.Equal(2, loaded.Manifest.SchemaVersion);
        Assert.Equal("VaporPets", loaded.Manifest.Name);
    }
}
