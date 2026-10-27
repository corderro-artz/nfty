using System.Globalization;
using Nfty.Core;
using Nfty.Core.Formats;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using Nfty.Core.Output;
using Nfty.Core.Publish;
using Nfty.Ticket;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>The helpers that are the one place for a rule several call sites used to restate.</summary>
public class OnePlaceHelperTests
{
    private sealed record Item(string Id, int Tag);

    [Fact]
    public void ById_keeps_the_last_of_duplicate_ids()
    {
        var map = Lookup.ById(new[] { new Item("a", 1), new Item("b", 2), new Item("a", 3) }, i => i.Id);

        Assert.Equal(2, map.Count);
        Assert.Equal(3, map["a"].Tag);
        Assert.False(map.ContainsKey("A"));   // ordinal
    }

    [Theory]
    [InlineData("ar-SA")]
    [InlineData("de-DE")]
    public void Stem_is_four_digit_and_culture_invariant(string culture)
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal("0007", SetLayout.Stem(7));
            Assert.Equal("0042", SetLayout.Stem(42));
            Assert.Equal("10000", SetLayout.Stem(10000));
        }
        finally { CultureInfo.CurrentCulture = old; }
    }

    // What NewIngredientViewModel and ImportImageViewModel BuildColorization both build today.
    private static Colorization? WizardBuild(LayerKind kind, ColorModel model) => kind switch
    {
        LayerKind.Dynamic => new Colorization(model, 12, 4,
            new[] { new ColorEntry(1, new ColorRange(0, 360, 40, 100), null) }),
        LayerKind.Static => new Colorization(model, 12, 4,
            new[] { new ColorEntry(1, null, "hex:d6249f") }),
        _ => null,
    };

    [Theory]
    [InlineData(LayerKind.Dynamic)]
    [InlineData(LayerKind.Static)]
    [InlineData(LayerKind.Custom)]
    public void Starter_matches_what_both_wizards_build(LayerKind kind)
    {
        var starter = Colorization.Starter(kind, ColorModel.Hsv, ColorDefaults.Range, ColorDefaults.FixedSpec);
        var expected = WizardBuild(kind, ColorModel.Hsv);

        if (expected is null) { Assert.Null(starter); return; }
        Assert.NotNull(starter);
        Assert.Equal(expected.Model, starter.Model);
        Assert.Equal(expected.HueQuantize, starter.HueQuantize);
        Assert.Equal(expected.SatQuantize, starter.SatQuantize);
        Assert.Equal(expected.Entries, starter.Entries);   // ColorEntry/ColorRange are records
    }

    [Fact]
    public void Text_count_pluralizes_regular_nouns()
    {
        Assert.Equal("0 recipes", Text.Count(0, "recipe"));
        Assert.Equal("1 recipe", Text.Count(1, "recipe"));
        Assert.Equal("2 recipes", Text.Count(2, "recipe"));
        Assert.Equal("12 layers", Text.Count(12, "layer"));
    }

    [Fact]
    public void Same_is_false_for_one_differing_pixel()
    {
        using var a = new Image<Rgba32>(3, 3, new Rgba32(10, 20, 30, 255));
        using var b = a.Clone();
        Assert.True(PixelCompare.Same(a, b));

        b[2, 1] = new Rgba32(10, 20, 31, 255);
        Assert.False(PixelCompare.Same(a, b));

        using var wider = new Image<Rgba32>(4, 3, new Rgba32(10, 20, 30, 255));
        Assert.False(PixelCompare.Same(a, wider));
    }

    [Fact]
    public async Task HashOfAsync_equals_HashOf()
    {
        string path = Path.Combine(Path.GetTempPath(), $"nfty-hash-{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4, 5, 250]);
        try
        {
            Assert.Equal(CookBookArchive.HashOf(path), await CookBookArchive.HashOfAsync(path));
            Assert.Matches("^[0-9a-f]{64}$", CookBookArchive.HashOf(path));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(0L, "0 bytes")]
    [InlineData(1023L, "1023 bytes")]
    [InlineData(1024L, "1 KB")]
    [InlineData(651_264L, "636 KB")]
    [InlineData(1_258_291L, "1.2 MB")]
    public void SizeText_words_bytes_kilobytes_and_megabytes_as_the_export_plan_did(long bytes, string expected)
    {
        Assert.Equal(expected, SizeText.Of(bytes));
    }

    [Fact]
    public void The_voxel_size_message_reads_its_bounds_from_Min_and_Max()
    {
        string? message = ModelVoxelSize.ProblemWith(0);
        Assert.Equal("Voxel size must be between 0.001 and 1000, but 0 was given.", message);
    }

    [Fact]
    public void A_recipe_finds_its_ingredient_and_a_book_its_recipe_or_names_the_missing_id()
    {
        using var recipe = LoadedRecipe.Empty("r1", "R1");
        using var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest(
                "b", "B", new Dimensions(1, 1), new Collection("c", "d", "S"),
                new Dictionary<string, double> { ["r1"] = 1 }),
            Recipes = [recipe],
        };

        Assert.Same(recipe, book.Recipe("r1"));
        Assert.False(book.TryRecipe("nope", out _));
        var ex = Assert.Throws<KeyNotFoundException>(() => book.Recipe("nope"));
        Assert.Contains("nope", ex.Message);

        Assert.False(recipe.TryIngredient("x", out _));
        Assert.Contains("x", Assert.Throws<KeyNotFoundException>(() => recipe.Ingredient("x")).Message);
    }
}
