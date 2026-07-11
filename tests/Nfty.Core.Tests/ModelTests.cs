using Nfty.Core.Model;

namespace Nfty.Core.Tests;

public class ModelTests
{
    [Fact]
    public void CookBook_holds_canvas_and_recipe_weights()
    {
        var cb = new CookBookManifest(
            Id: "cb1", Name: "VaporPets",
            Canvas: new Dimensions(512, 512),
            Collection: new Collection("VaporPets", "desc", "VPET"),
            RecipeWeights: new Dictionary<string, double> { ["cat"] = 70, ["robot"] = 30 });

        Assert.Equal(512, cb.Canvas.Width);
        Assert.Equal(70, cb.RecipeWeights["cat"]);
        Assert.Equal(1, cb.SchemaVersion);
    }

    [Fact]
    public void Recipe_is_an_ordered_layer_stack_with_rules()
    {
        var recipe = new RecipeManifest(
            Id: "cat", Name: "Cat",
            LayerOrder: new[] { "bg", "body", "hat" },
            Rules: new[]
            {
                new IncompatibilityRule(RuleType.Exclude,
                    new RuleTarget("body", "fox"),
                    new[] { new RuleTarget("hat", "visor") }),
            });

        Assert.Equal(new[] { "bg", "body", "hat" }, recipe.LayerOrder);
        Assert.Single(recipe.Rules);
    }

    [Fact]
    public void Dynamic_ingredient_carries_colorization_and_variants()
    {
        var ing = new IngredientManifest(
            Id: "aura", Name: "Aura", Kind: LayerKind.Dynamic,
            Colorization: new Colorization(ColorModel.Hsv, 5, 5, new[]
            {
                new ColorEntry(10, null, "hex:d6249f"),
                new ColorEntry(30, new ColorRange(175, 195, 60, 90), null),
            }),
            Variants: new[]
            {
                new Variant("glow", "Glow", 60),
                new Variant("spark", "Spark", 40),
            });

        Assert.Equal(LayerKind.Dynamic, ing.Kind);
        Assert.Equal(2, ing.Colorization!.Entries.Count);
        Assert.Equal(60, ing.Variants[0].Weight);
    }
}
