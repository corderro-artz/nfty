using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

public class ArchiveRoundTripTests
{
    private static LoadedIngredient Ingredient(string id, params (string vid, Rgba32 fill)[] variants) => new()
    {
        Manifest = new IngredientManifest(id, id, LayerKind.Custom, null,
            variants.Select(v => new Variant(v.vid, v.vid, 1)).ToList()),
        VariantImages = variants.ToDictionary(v => v.vid, v => new Image<Rgba32>(4, 4, v.fill)),
    };

    [Fact]
    public void CookBook_round_trips_through_disk()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "VaporPets.cbk");

        var bg = Ingredient("bg", ("sunset", new Rgba32(255, 128, 0, 255)), ("grid", new Rgba32(0, 128, 255, 255)));
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "bg" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { bg },
        };
        var cb = new CookBookManifest("cb", "VaporPets", new Dimensions(4, 4),
            new Collection("VaporPets", "d", "VP"),
            new Dictionary<string, double> { ["cat"] = 100 });

        CookBookArchive.Write(path, cb, new[] { recipe });
        var loaded = CookBookArchive.Read(path);

        Assert.Equal("VaporPets", loaded.Manifest.Name);
        Assert.Equal(new Dimensions(4, 4), loaded.Manifest.Canvas);
        Assert.Equal(100, loaded.Manifest.RecipeWeights["cat"]);
        Assert.Single(loaded.Recipes);
        var loadedBg = loaded.Recipes[0].Ingredients.Single();
        Assert.Equal(2, loadedBg.Manifest.Variants.Count);
        Assert.Equal(new Rgba32(255, 128, 0, 255), loadedBg.VariantImages["sunset"][0, 0]);
    }
}
