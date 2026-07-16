using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

public class SchemaVersionTests
{
    private static string TempPath(string name) =>
        Path.Combine(Directory.CreateTempSubdirectory().FullName, name);

    private static LoadedIngredient Ingredient(int schemaVersion = 1) => new()
    {
        Manifest = new IngredientManifest("bg", "BG", LayerKind.Custom, null,
            new[] { new Variant("a", "A", 1) }, SchemaVersion: schemaVersion),
        VariantImages = new Dictionary<string, Image<Rgba32>>
        {
            ["a"] = new Image<Rgba32>(2, 2, new Rgba32(1, 2, 3, 255)),
        },
    };

    private static LoadedRecipe Recipe(int schemaVersion = 1) => new()
    {
        Manifest = new RecipeManifest("cat", "Cat", new[] { "bg" },
            Array.Empty<IncompatibilityRule>(), SchemaVersion: schemaVersion),
        Ingredients = new[] { Ingredient() },
    };

    private static CookBookManifest Book(int schemaVersion = 1) =>
        new("cb", "VaporPets", new Dimensions(2, 2), new Collection("VaporPets", "d", "VP"),
            new Dictionary<string, double> { ["cat"] = 1 }, SchemaVersion: schemaVersion);

    [Fact]
    public void Ingredient_with_future_schema_version_is_rejected()
    {
        var path = TempPath("future.igt");
        var ing = Ingredient(schemaVersion: 2);
        IngredientArchive.Write(path, ing.Manifest, ing.VariantImages);

        var ex = Assert.Throws<UnsupportedSchemaVersionException>(() => IngredientArchive.Read(path));

        Assert.Equal(2, ex.Found);
        Assert.Equal(Schema.Current, ex.Supported);
        Assert.Contains("2", ex.Message);
    }

    [Fact]
    public void Recipe_with_future_schema_version_is_rejected()
    {
        var path = TempPath("future.rcp");
        var recipe = Recipe(schemaVersion: 2);
        RecipeArchive.Write(path, recipe.Manifest, recipe.Ingredients);

        Assert.Throws<UnsupportedSchemaVersionException>(() => RecipeArchive.Read(path));
    }

    [Fact]
    public void CookBook_with_future_schema_version_is_rejected()
    {
        var path = TempPath("future.cbk");
        CookBookArchive.Write(path, Book(schemaVersion: 2), new[] { Recipe() });

        Assert.Throws<UnsupportedSchemaVersionException>(() => CookBookArchive.Read(path));
    }

    [Fact]
    public void Nested_ingredient_version_is_checked_through_the_cookbook()
    {
        // A v1 cookbook whose nested ingredient is v2 must still be rejected, not silently misparsed.
        var path = TempPath("nested.cbk");
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "bg" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { Ingredient(schemaVersion: 2) },
        };
        CookBookArchive.Write(path, Book(), new[] { recipe });

        Assert.Throws<UnsupportedSchemaVersionException>(() => CookBookArchive.Read(path));
    }

    [Fact]
    public void Current_schema_version_round_trips()
    {
        var path = TempPath("ok.cbk");
        CookBookArchive.Write(path, Book(), new[] { Recipe() });

        var loaded = CookBookArchive.Read(path);

        Assert.Equal(Schema.Current, loaded.Manifest.SchemaVersion);
        Assert.Equal("VaporPets", loaded.Manifest.Name);
    }
}
