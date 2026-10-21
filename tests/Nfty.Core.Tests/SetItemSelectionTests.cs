using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.Core.Tests;

public class SetItemSelectionTests
{
    private static LoadedCookBook Book(string lockName2 = "Iron")
    {
        LoadedIngredient Ing(string id, string name, params (string Id, string Name)[] vs) => new()
        {
            Manifest = new IngredientManifest(id, name, LayerKind.Custom, null,
                vs.Select(v => new Variant(v.Id, v.Name, 1)).ToList()),
            VariantImages = vs.ToDictionary(v => v.Id, v => new Image<Rgba32>(2, 2, new Rgba32((byte)v.Id[0], 9, 9, 255))),
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("chest", "Chest", new[] { "body", "lock" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[]
            {
                Ing("body", "Body", ("oak", "Oak"), ("walnut", "Walnut")),
                Ing("lock", "Lock", ("gold", "Gold"), ("iron", lockName2)),
            },
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Chest Demo", new Dimensions(2, 2),
                new Collection("Chest Demo", "", "CH"), new Dictionary<string, double> { ["chest"] = 1 }),
            Recipes = new[] { recipe },
        };
    }

    [Fact]
    public void Every_cooked_asset_maps_back_onto_the_layers_that_made_it()
    {
        using var book = Book();
        using var set = Generator.Generate(book, new GenerateOptions(4, "s"));
        var dir = Directory.CreateTempSubdirectory("nfty-sel-").FullName;
        SetWriter.Write(set, dir, pack: false);
        using var read = SetReader.Read(dir);

        foreach (var item in read.Items)
        {
            var asset = set.Assets.Single(a => a.SetNumber == item.Number);
            var expected = asset.Traits.Select(t => (t.IngredientId, t.VariantId)).ToList();
            Assert.Equal(expected, SetItemSelection.Resolve(book.Recipes[0], item));
        }
    }

    [Fact]
    public void A_variant_name_two_variants_share_cannot_be_resolved()
    {
        using var book = Book(lockName2: "Gold");             // gold and iron are both named "Gold"
        var item = new SetItem(1, "x.png", "dna", "chest",
            new[] { new RarityAttribute("Type", "Chest", 100), new RarityAttribute("Body", "Oak", 50),
                    new RarityAttribute("Lock", "Gold", 50) },
            Array.Empty<LayerColor>());
        Assert.Null(SetItemSelection.Resolve(book.Recipes[0], item));
    }

    [Fact]
    public void Another_recipe_and_an_absent_layer_are_handled()
    {
        using var book = Book();
        var other = new SetItem(1, "x.png", "dna", "barrel", Array.Empty<RarityAttribute>(), Array.Empty<LayerColor>());
        Assert.Null(SetItemSelection.Resolve(book.Recipes[0], other));

        var lockless = new SetItem(2, "x.png", "dna", "chest",
            new[] { new RarityAttribute("Type", "Chest", 100), new RarityAttribute("Body", "Walnut", 50),
                    new RarityAttribute("Lock", "none", 50) },
            Array.Empty<LayerColor>(), AbsentLayers: new[] { "Lock" });
        Assert.Equal(new[] { ("body", "walnut") }, SetItemSelection.Resolve(book.Recipes[0], lockless));
    }
}
