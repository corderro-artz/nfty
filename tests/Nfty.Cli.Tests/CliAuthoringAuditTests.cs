using System.CommandLine;
using System.Globalization;
using Nfty.Cli;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Cli.Tests;

public class CliAuthoringAuditTests
{
    private static readonly InvocationConfiguration NonThrowing = new() { EnableDefaultExceptionHandler = false };

    [Theory]
    [InlineData("set chance cat.rcp --id hat --absent abc", "--absent")]
    [InlineData("remove rule cat.rcp --at abc", "--at")]
    [InlineData("remove rule cat.rcp --at 2147483648", "--at")]
    [InlineData("add ingredient cat.rcp --igt hat.igt --index abc", "--index")]
    [InlineData("move ingredient cat.rcp --id hat --to abc", "--to")]
    [InlineData("move ingredient cat.rcp --id hat --to 2147483648", "--to")]
    [InlineData("move ingredient cat.rcp --id hat --to", "--to")]
    [InlineData("move recipe book.cbk --id cat --to abc", "--to")]
    [InlineData("set variant art.igt --id one --weight abc", "--weight")]
    [InlineData("set recipe book.cbk --id cat --weight abc", "--weight")]
    public void Malformed_authoring_numbers_remain_parse_errors(string command, string option)
    {
        var parsed = CommandFactory.Build().Parse(command);

        Assert.Contains(parsed.Errors, e => e.Message.Contains(option, StringComparison.Ordinal));
        var (code, _, error) = ProgramTests.Run(command.Split(' '));
        Assert.Equal(1, code);
        Assert.Contains(option, error, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled exception", error, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ingredient", "{\"id\":\"art\",\"name\":\"Art\",\"kind\":\"custom\",\"colorization\":null,\"variants\":[null]}", "variants[0]")]
    [InlineData("ingredient", "{\"id\":\"art\",\"name\":\"Art\",\"kind\":\"dynamic\",\"colorization\":{\"model\":\"hsv\",\"hueQuantize\":10,\"satQuantize\":10,\"entries\":[null]},\"variants\":[]}", "entries[0]")]
    [InlineData("recipe", "{\"id\":\"cat\",\"name\":\"Cat\",\"layerOrder\":[null],\"rules\":[]}", "layerOrder[0]")]
    [InlineData("recipe", "{\"id\":\"cat\",\"name\":\"Cat\",\"layerOrder\":[],\"rules\":[null]}", "rules[0]")]
    public void Authoring_manifests_reject_null_elements_before_loading_parts(string kind, string json, string element)
    {
        var temp = Directory.CreateTempSubdirectory();
        try
        {
            string manifest = Path.Combine(temp.FullName, "input.json");
            string output = Path.Combine(temp.FullName, kind == "ingredient" ? "art.igt" : "cat.rcp");
            File.WriteAllText(manifest, json);
            string partsOption = kind == "ingredient" ? "--images" : "--ingredients";

            var error = Assert.Throws<InvalidDataException>(() => CommandFactory.Build()
                .Parse(["new", kind, output, "--manifest", manifest, partsOption, temp.FullName])
                .Invoke(NonThrowing));

            Assert.Contains(manifest, error.Message, StringComparison.Ordinal);
            Assert.Contains(element, error.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(output));
        }
        finally { temp.Delete(recursive: true); }
    }

    [Fact]
    public void Adding_a_recipe_confirms_the_same_fractional_weight_in_every_culture()
    {
        var temp = Directory.CreateTempSubdirectory();
        var originalCulture = CultureInfo.CurrentCulture;
        var originalOut = Console.Out;
        try
        {
            using var cat = Recipe("cat");
            using var dog = Recipe("dog");
            string rcp = Path.Combine(temp.FullName, "dog.rcp");
            RecipeArchive.Write(rcp, dog.Manifest, dog.Ingredients);
            string? expected = null;
            byte[]? expectedArchive = null;
            string book = Path.Combine(temp.FullName, "book.cbk");
            foreach (string culture in new[] { "en-US", "de-DE", "fr-FR", "tr-TR" })
            {
                if (File.Exists(book)) File.Delete(book);
                CookBookArchive.Write(book, new CookBookManifest("book", "Book", new Dimensions(1, 1),
                    new Collection("Pets", "", "PET"), new Dictionary<string, double> { ["cat"] = 1 }), [cat]);
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                using var text = new StringWriter();
                Console.SetOut(text);

                Assert.Equal(0, CommandFactory.Build().Parse(
                    ["add", "recipe", book, "--rcp", rcp, "--weight", "0.5"]).Invoke(NonThrowing));

                Assert.Contains("(weight 0.5)", text.ToString(), StringComparison.Ordinal);
                expected ??= text.ToString();
                expectedArchive ??= File.ReadAllBytes(book);
                Assert.Equal(expected, text.ToString());
                Assert.Equal(expectedArchive, File.ReadAllBytes(book));
            }
        }
        finally
        {
            Console.SetOut(originalOut);
            CultureInfo.CurrentCulture = originalCulture;
            temp.Delete(recursive: true);
        }
    }

    [Fact]
    public void Weight_edits_preserve_recipe_listing_and_unrelated_manifest_fields()
    {
        var temp = Directory.CreateTempSubdirectory();
        try
        {
            string book = WriteOrderedBook(temp.FullName);

            Assert.Equal(0, CommandFactory.Build().Parse(
                ["set", "recipe", book, "--id", "dog", "--weight", "0"]).Invoke(NonThrowing));

            using var loaded = CookBookArchive.Read(book);
            Assert.Equal(new[] { "dog", "cat" }, loaded.Recipes.Select(r => r.Manifest.Id));
            Assert.Equal(new[] { "dog", "cat" }, loaded.Manifest.RecipeOrder);
            Assert.Equal(0, loaded.Manifest.RecipeWeights["dog"]);
            Assert.Equal(1, loaded.Manifest.RecipeWeights["cat"]);
            Assert.Equal(1200, loaded.Manifest.TargetSupply);
            Assert.Equal(new[] { "hex:123456" }, loaded.Manifest.Palette);
            Assert.All(loaded.Recipes, r => Assert.Equal(new Rgba32(20, 30, 40, 255),
                r.Ingredients[0].VariantImages["one"][0, 0]));
        }
        finally { temp.Delete(recursive: true); }
    }

    [Fact]
    public void Identity_edits_distinguish_empty_text_and_supply_clearing_from_absent_options()
    {
        var temp = Directory.CreateTempSubdirectory();
        try
        {
            string book = WriteOrderedBook(temp.FullName);
            Assert.Equal(0, CommandFactory.Build().Parse(
                ["set", "cookbook", book, "--symbol", ""]).Invoke(NonThrowing));
            using (var loaded = CookBookArchive.Read(book))
            {
                Assert.Equal("", loaded.Manifest.Collection.Symbol);
                Assert.Equal(1200, loaded.Manifest.TargetSupply);
            }

            Assert.Equal(0, CommandFactory.Build().Parse(
                ["set", "cookbook", book, "--name", "Changed", "--target-supply", "none"]).Invoke(NonThrowing));
            using var changed = CookBookArchive.Read(book);
            Assert.Equal("Changed", changed.Manifest.Name);
            Assert.Equal("Pets", changed.Manifest.Collection.Name);
            Assert.Equal("Description", changed.Manifest.Collection.Description);
            Assert.Equal("", changed.Manifest.Collection.Symbol);
            Assert.Null(changed.Manifest.TargetSupply);
            Assert.Equal(new[] { "dog", "cat" }, changed.Manifest.RecipeOrder);
            Assert.Equal(new[] { "hex:123456" }, changed.Manifest.Palette);
        }
        finally { temp.Delete(recursive: true); }
    }

    private static string WriteOrderedBook(string directory)
    {
        using var dog = Recipe("dog");
        using var cat = Recipe("cat");
        string book = Path.Combine(directory, "book.cbk");
        CookBookArchive.Write(book, new CookBookManifest("book", "Book", new Dimensions(1, 1),
            new Collection("Pets", "Description", "PET"), new Dictionary<string, double> { ["dog"] = 1, ["cat"] = 1 },
            TargetSupply: 1200, RecipeOrder: ["dog", "cat"], Palette: ["hex:123456"]), [dog, cat]);
        return book;
    }

    private static LoadedRecipe Recipe(string id) => new()
    {
        Manifest = new RecipeManifest(id, id, ["art"], []),
        Ingredients = [new LoadedIngredient
        {
            Manifest = new IngredientManifest("art", "Art", LayerKind.Custom, null, [new Variant("one", "One", 1)]),
            VariantImages = new Dictionary<string, Image<Rgba32>>
            {
                ["one"] = new Image<Rgba32>(1, 1, new Rgba32(20, 30, 40, 255)),
            },
        }],
    };
}
