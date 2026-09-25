using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;

namespace Nfty.Core.Tests;

/// <summary>
/// The only tests that read archives written by an *earlier build* rather than built in memory
/// by the test itself. Every other test constructs its own graph, so none of them would notice
/// if a change quietly altered what lands on disk — these would. See tests/fixtures/README.md.
/// </summary>
public class FixtureArchiveTests
{
    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    [Fact]
    public void The_cookbook_fixture_still_reads()
    {
        using var book = CookBookArchive.Read(Fixture("VaporPets.cbk"));

        Assert.Equal("VaporPets", book.Manifest.Name);
        Assert.Equal(new Dimensions(8, 8), book.Manifest.Canvas);
        Assert.Equal("VPET", book.Manifest.Collection.Symbol);
        Assert.Equal(100, book.Manifest.RecipeWeights["cat"]);
        Assert.Equal(Schema.Current, book.Manifest.SchemaVersion);
    }

    [Fact]
    public void The_cookbook_fixture_carries_all_three_layer_kinds()
    {
        // The point of this fixture: one real archive exercising every kind at once.
        using var book = CookBookArchive.Read(Fixture("VaporPets.cbk"));
        var recipe = Assert.Single(book.Recipes);
        var byId = recipe.Ingredients.ToDictionary(i => i.Manifest.Id);

        Assert.Equal(new[] { "bg", "skin", "aura" }, recipe.Manifest.LayerOrder);
        Assert.Equal(LayerKind.Custom, byId["bg"].Manifest.Kind);
        Assert.Equal(LayerKind.Static, byId["skin"].Manifest.Kind);
        Assert.Equal(LayerKind.Dynamic, byId["aura"].Manifest.Kind);

        Assert.Null(byId["bg"].Manifest.Colorization);
        Assert.Single(byId["skin"].Manifest.Colorization!.Entries);
        Assert.Equal(2, byId["aura"].Manifest.Colorization!.Entries.Count);
    }

    [Fact]
    public void Every_fixture_manifest_is_schema_2_with_no_partial_alpha()
    {
        // Rise, 2026-09-25: the fixtures were regenerated at schemaVersion 2 (see
        // tests/fixtures/README.md). PartialAlpha is a recomputed listing cache, not authored
        // data — these placeholder images are all fully opaque or fully transparent per pixel,
        // so it must read false at every level, nested included.
        using var book = CookBookArchive.Read(Fixture("VaporPets.cbk"));
        using var recipe = RecipeArchive.Read(Fixture("cat.rcp"));
        using var ing = IngredientArchive.Read(Fixture("aura.igt"));

        Assert.Equal(2, book.Manifest.SchemaVersion);
        Assert.False(book.Manifest.PartialAlpha);

        var nestedRecipe = Assert.Single(book.Recipes);
        Assert.Equal(2, nestedRecipe.Manifest.SchemaVersion);
        Assert.False(nestedRecipe.Manifest.PartialAlpha);
        Assert.Equal(2, recipe.Manifest.SchemaVersion);
        Assert.False(recipe.Manifest.PartialAlpha);

        foreach (var i in nestedRecipe.Ingredients)
        {
            Assert.Equal(2, i.Manifest.SchemaVersion);
            Assert.False(i.Manifest.PartialAlpha);
        }
        Assert.Equal(2, ing.Manifest.SchemaVersion);
        Assert.False(ing.Manifest.PartialAlpha);
    }

    [Fact]
    public void The_aura_glow_variant_carries_the_depth_map_added_when_regenerated()
    {
        // Rise, 2026-09-25: one depth map added to the v2 fixtures deliberately, so the .igt
        // depth/{variantId}.dep path has a real archive covering it. glow's top half was painted
        // front 2 / back 1 over every solid pixel; the bottom half, and spark entirely, were left
        // untouched (flat, so spark gets no depth/ entry at all — see IngredientArchive.Write).
        using var ing = IngredientArchive.Read(Fixture("aura.igt"));

        Assert.True(ing.Depths.TryGetValue("glow", out var glow));
        var image = ing.VariantImages["glow"];
        Assert.Equal(image.Width, glow!.Width);
        Assert.Equal(image.Height, glow.Height);

        // A solid pixel in the top half: painted.
        Assert.Equal(2, glow.Front(0, 0));
        Assert.Equal(1, glow.Back(0, 0));
        // A solid pixel in the bottom half: never painted.
        Assert.Equal(0, glow.Front(0, image.Height - 1));
        Assert.Equal(0, glow.Back(0, image.Height - 1));

        Assert.False(ing.Depths.ContainsKey("spark"));
    }

    [Fact]
    public void The_cookbook_fixture_carries_its_incompatibility_rule()
    {
        using var book = CookBookArchive.Read(Fixture("VaporPets.cbk"));

        var rule = Assert.Single(book.Recipes.Single().Manifest.Rules);

        Assert.Equal(RuleType.Exclude, rule.Type);
        Assert.Equal(new RuleTarget("bg", "grid"), rule.When);
        Assert.Equal(new RuleTarget("aura", "spark"), Assert.Single(rule.Targets));
    }

    [Fact]
    public void The_cookbook_fixture_is_valid()
    {
        using var book = CookBookArchive.Read(Fixture("VaporPets.cbk"));

        Assert.Empty(Validator.Validate(book));
    }

    [Fact]
    public void The_cookbook_fixture_generates_and_honours_its_rule()
    {
        using var book = CookBookArchive.Read(Fixture("VaporPets.cbk"));
        using var set = Generator.Generate(book, new GenerateOptions(12, "vapor"));

        Assert.Equal(12, set.Assets.Count);
        Assert.Equal(12, set.Assets.Select(a => a.Dna).Distinct().Count());

        // The rule: Grid background excludes the Spark aura.
        foreach (var asset in set.Assets)
        {
            var chosen = asset.Traits.ToDictionary(t => t.IngredientId, t => t.VariantId);
            Assert.False(chosen["bg"] == "grid" && chosen["aura"] == "spark");
        }
    }

    [Fact]
    public void The_recipe_fixture_reads_standalone()
    {
        using var recipe = RecipeArchive.Read(Fixture("cat.rcp"));

        Assert.Equal("Cat", recipe.Manifest.Name);
        Assert.Equal(3, recipe.Ingredients.Count);
    }

    [Fact]
    public void The_ingredient_fixture_reads_standalone()
    {
        using var ing = IngredientArchive.Read(Fixture("aura.igt"));

        Assert.Equal("Aura", ing.Manifest.Name);
        Assert.Equal(LayerKind.Dynamic, ing.Manifest.Kind);
        Assert.Equal(new[] { "glow", "spark" }, ing.Manifest.Variants.Select(v => v.Id));
        Assert.Equal(8, ing.VariantImages["glow"].Width);
    }

    [Fact]
    public void The_fixture_reproduces_the_same_dna_for_the_same_seed()
    {
        // Pins determinism against a file rather than an in-memory graph: if a change altered
        // what a real archive means, the DNA below moves.
        using var book = CookBookArchive.Read(Fixture("VaporPets.cbk"));
        using var first = Generator.Generate(book, new GenerateOptions(3, "vapor"));
        using var second = Generator.Generate(book, new GenerateOptions(3, "vapor"));

        Assert.Equal(first.Assets.Select(a => a.Dna), second.Assets.Select(a => a.Dna));
    }
}
