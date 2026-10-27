using System.Globalization;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Nfty.Core.Stats;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.Core.Tests;

public class VariantReachTests
{
    private static LoadedIngredient Ing(string id, params (string vid, double w)[] variants) => new()
    {
        Manifest = new IngredientManifest(id, id, LayerKind.Custom, null,
            variants.Select(v => new Variant(v.vid, v.vid, v.w)).ToList()),
        VariantImages = variants.ToDictionary(v => v.vid, _ => new Image<Rgba32>(1, 1)),
    };

    private static LoadedCookBook Book(int? supply, bool rules = false) => new()
    {
        Manifest = new CookBookManifest("cb", "B", new Dimensions(1, 1), new Collection("B", "", "B"),
            new Dictionary<string, double> { ["chest"] = 1 }, TargetSupply: supply),
        Recipes = new[]
        {
            new LoadedRecipe
            {
                Manifest = new RecipeManifest("chest", "Chest", new[] { "body", "lock" },
                    rules
                        ? new[] { new IncompatibilityRule(RuleType.Exclude, new RuleTarget("body", "ghost"),
                            new[] { new RuleTarget("lock", "gold") }) }
                        : Array.Empty<IncompatibilityRule>()),
                Ingredients = new[] { Ing("body", ("oak", 3), ("ghost", 1)), Ing("lock", ("gold", 1)) },
            },
        },
    };

    [Fact]
    public void Reach_with_a_target_supply_is_an_expected_count()
    {
        var book = Book(1200);
        Assert.Equal("in ~900 of 1,200 assets", VariantReach.Of(book, "chest", "body", "oak").Text);
        Assert.Equal("in all 1,200 assets", VariantReach.Of(book, "chest", "lock", "gold").Text);
    }

    [Fact]
    public void Reach_without_a_target_supply_is_a_share()
    {
        var book = Book(null);
        Assert.Equal("in 75% of assets", VariantReach.Of(book, "chest", "body", "oak").Text);
        Assert.Equal("in every asset", VariantReach.Of(book, "chest", "lock", "gold").Text);
    }

    [Fact]
    public void Reach_is_book_wide_so_the_recipes_own_share_is_folded_in()
    {
        var one = Book(1000);
        var half = new LoadedCookBook
        {
            Manifest = one.Manifest with { RecipeWeights = new Dictionary<string, double> { ["chest"] = 1, ["other"] = 1 } },
            Recipes = one.Recipes.Append(new LoadedRecipe
            {
                Manifest = new RecipeManifest("other", "Other", new[] { "body" }, Array.Empty<IncompatibilityRule>()),
                Ingredients = new[] { Ing("body", ("oak", 1)) },
            }).ToList(),
        };
        // chest is half the mints, oak is 3/4 of chest: 37.5% of 1,000.
        Assert.Equal("in ~375 of 1,000 assets", VariantReach.Of(half, "chest", "body", "oak").Text);
    }

    [Fact]
    public void An_unknown_id_is_zero_not_a_throw()
    {
        var r = VariantReach.Of(Book(null), "chest", "body", "nope");
        Assert.Equal(0, r.Percent);
        Assert.Equal("in 0% of assets", r.Text);
    }

    [Fact]
    public void Reach_is_invariant_under_de_DE()
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("in ~900 of 1,200 assets", VariantReach.Of(Book(1200), "chest", "body", "oak").Text);
        }
        finally { CultureInfo.CurrentCulture = old; }
    }

    [Fact]
    public void Reach_tooltip_mentions_rules_only_when_the_recipe_has_them()
    {
        Assert.DoesNotContain("Rules", VariantReach.Of(Book(null), "chest", "body", "oak").Tip);
        Assert.Contains("Rules can change it", VariantReach.Of(Book(null, rules: true), "chest", "body", "oak").Tip);
    }
}
