using System.IO.Compression;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

public class CoreBoundaryAuditTests
{
    [Theory]
    [InlineData("igt", "variants", "[null]")]
    [InlineData("igt", "variants", "[{\"id\":null,\"name\":\"A\",\"weight\":1}]")]
    [InlineData("igt", "colorization", "{\"model\":\"hsv\",\"hueQuantize\":1,\"satQuantize\":1,\"entries\":[null]}")]
    [InlineData("rcp", "rules", "[null]")]
    [InlineData("rcp", "rules", "[{\"type\":\"exclude\",\"when\":null,\"targets\":[]}]")]
    [InlineData("rcp", "rules", "[{\"type\":\"exclude\",\"when\":{\"ingredientId\":null,\"variantId\":\"v\"},\"targets\":[]}]")]
    [InlineData("rcp", "rules", "[{\"type\":\"exclude\",\"when\":{\"ingredientId\":\"a\",\"variantId\":\"v\"},\"targets\":[null]}]")]
    [InlineData("rcp", "layerOrder", "[null]")]
    [InlineData("cbk", "recipeOrder", "[null]")]
    [InlineData("cbk", "palette", "[null]")]
    public async Task Nested_nulls_fail_at_the_archive_boundary(string kind, string property, string value)
    {
        using var dir = new TempDirectory();
        string json = kind switch
        {
            "igt" => "{\"id\":\"i\",\"name\":\"I\",\"kind\":\"custom\",\"colorization\":null,\"variants\":[]}",
            "rcp" => "{\"id\":\"r\",\"name\":\"R\",\"layerOrder\":[],\"rules\":[]}",
            _ => "{\"id\":\"b\",\"name\":\"B\",\"canvas\":{\"width\":1,\"height\":1},\"collection\":{\"name\":\"B\",\"description\":\"\",\"symbol\":\"B\"},\"recipeWeights\":{}}",
        };
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        node[property] = System.Text.Json.Nodes.JsonNode.Parse(value);
        string path = Path.Combine(dir.Path, "bad." + kind);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open()))
            writer.Write(node.ToJsonString());

        var sync = Assert.Throws<InvalidDataException>(() => Read(path, kind));
        var asyncError = await Assert.ThrowsAsync<InvalidDataException>(() => ReadAsync(path, kind));
        Assert.Contains("manifest.json", sync.Message);
        Assert.Contains(property, sync.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(sync.Message, asyncError.Message);
    }

    [Theory]
    [InlineData("variant")]
    [InlineData("rule")]
    [InlineData("target")]
    [InlineData("layerOrder")]
    [InlineData("entry")]
    [InlineData("ingredient")]
    [InlineData("recipe")]
    [InlineData("image")]
    [InlineData("depth")]
    [InlineData("manifest")]
    [InlineData("canvas")]
    [InlineData("variantList")]
    [InlineData("ruleList")]
    [InlineData("targetList")]
    [InlineData("entryList")]
    [InlineData("name")]
    [InlineData("id")]
    [InlineData("imageDictionary")]
    [InlineData("ingredientList")]
    [InlineData("recipeList")]
    public void Direct_malformed_graphs_return_problems(string malformed)
    {
        using var image = new Image<Rgba32>(1, 1);
        var ingredient = Ingredient(image);
        var recipe = Recipe(ingredient);
        var book = Book(recipe);
        if (malformed == "variant") ((Variant[])ingredient.Manifest.Variants)[0] = null!;
        if (malformed == "variantList") ingredient = WithManifest(ingredient, ingredient.Manifest with { Variants = null! });
        if (malformed == "name") ingredient = WithManifest(ingredient, ingredient.Manifest with { Name = null! });
        if (malformed == "id") ingredient = WithManifest(ingredient, ingredient.Manifest with { Id = null! });
        if (malformed == "rule") ((IncompatibilityRule[])recipe.Manifest.Rules)[0] = null!;
        if (malformed == "ruleList") recipe = new LoadedRecipe { Manifest = recipe.Manifest with { Rules = null! }, Ingredients = recipe.Ingredients };
        if (malformed == "targetList") ((IncompatibilityRule[])recipe.Manifest.Rules)[0] = recipe.Manifest.Rules[0] with { Targets = null! };
        if (malformed == "target") ((RuleTarget[])recipe.Manifest.Rules[0].Targets)[0] = null!;
        if (malformed == "layerOrder") ((string[])recipe.Manifest.LayerOrder)[0] = null!;
        if (malformed == "entry")
            ingredient = WithManifest(ingredient, ingredient.Manifest with
            { Colorization = new(ColorModel.Hsv, 1, 1, new ColorEntry[] { null! }) });
        if (malformed == "entryList") ingredient = WithManifest(ingredient, ingredient.Manifest with
            { Colorization = new(ColorModel.Hsv, 1, 1, null!) });
        if (malformed == "imageDictionary") ingredient = new LoadedIngredient { Manifest = ingredient.Manifest, VariantImages = null! };
        if (malformed == "image")
            ingredient = new LoadedIngredient { Manifest = ingredient.Manifest,
                VariantImages = new Dictionary<string, Image<Rgba32>> { ["a"] = null! } };
        if (malformed == "depth")
            ingredient = new LoadedIngredient { Manifest = ingredient.Manifest,
                VariantImages = ingredient.VariantImages,
                Depths = new Dictionary<string, DepthMap> { ["a"] = null! } };
        if (malformed == "manifest") ingredient = WithManifest(ingredient, null!);
        if (malformed is "entry" or "image" or "depth" or "manifest" or "variantList" or "entryList" or "name" or "id" or "imageDictionary") recipe = Recipe(ingredient);
        if (malformed == "ingredient") recipe = new LoadedRecipe { Manifest = recipe.Manifest, Ingredients = new LoadedIngredient[] { null! } };
        if (malformed == "ingredientList") recipe = new LoadedRecipe { Manifest = recipe.Manifest, Ingredients = null! };
        book = Book(recipe);
        if (malformed == "recipe") book = new LoadedCookBook { Manifest = book.Manifest, Recipes = new LoadedRecipe[] { null! } };
        if (malformed == "recipeList") book = new LoadedCookBook { Manifest = book.Manifest, Recipes = null! };
        if (malformed == "canvas") book = new LoadedCookBook { Manifest = book.Manifest with { Canvas = null! }, Recipes = book.Recipes };

        Assert.NotEmpty(Validator.Validate(book));
        if (malformed is not ("recipe" or "recipeList" or "canvas")) Assert.NotEmpty(Validator.ValidateRecipe(recipe));
        if (malformed is "variant" or "entry" or "image" or "depth" or "manifest" or "variantList" or "entryList" or "name" or "id" or "imageDictionary")
            Assert.NotEmpty(Validator.ValidateIngredient(ingredient));
    }

    [Theory]
    [InlineData("layer")]
    [InlineData("color")]
    [InlineData("rule")]
    public void Undefined_enums_are_validation_problems(string field)
    {
        using var image = new Image<Rgba32>(1, 1);
        var ingredient = Ingredient(image);
        if (field == "layer") ingredient = WithManifest(ingredient, ingredient.Manifest with { Kind = (LayerKind)999 });
        if (field == "color") ingredient = WithManifest(ingredient, ingredient.Manifest with
            { Kind = LayerKind.Static, Colorization = new((ColorModel)999, 1, 1, new[] { new ColorEntry(1, null, "hex:ffffff") }) });
        var recipe = Recipe(ingredient);
        if (field == "rule") ((IncompatibilityRule[])recipe.Manifest.Rules)[0] = recipe.Manifest.Rules[0] with { Type = (RuleType)999 };
        Assert.Contains(Validator.ValidateRecipe(recipe), p => p.Contains("999", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Existing_empty_or_duplicate_rule_targets_keep_their_validation_policy(bool duplicate)
    {
        using var image = new Image<Rgba32>(1, 1);
        var recipe = Recipe(Ingredient(image));
        var targets = duplicate ? new[] { new RuleTarget("j", "b"), new RuleTarget("j", "b") }
            : Array.Empty<RuleTarget>();
        ((IncompatibilityRule[])recipe.Manifest.Rules)[0] = recipe.Manifest.Rules[0] with { Targets = targets };
        Assert.Empty(Validator.Validate(Book(recipe)));
        Assert.Throws<ArgumentException>(() => RuleEdits.Validate(recipe.Manifest.Rules[0]));
    }

    [Fact]
    public void Defined_numeric_enums_remain_readable_and_valid()
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open()))
            writer.Write("{\"id\":\"i\",\"name\":\"I\",\"kind\":0,\"colorization\":{\"model\":1,\"hueQuantize\":1,\"satQuantize\":1,\"entries\":[{\"weight\":1,\"range\":null,\"fixed\":\"hex:ffffff\"}]},\"variants\":[{\"id\":\"a\",\"name\":\"A\",\"weight\":1}]}");
        bytes.Position = 0;
        using var archive = new ZipArchive(bytes, ZipArchiveMode.Read);
        var manifest = ArchiveIo.ReadManifest<IngredientManifest>(archive);
        Assert.Equal(LayerKind.Static, manifest.Kind);
        Assert.Equal(ColorModel.Hsl, manifest.Colorization!.Model);
        using var image = new Image<Rgba32>(1, 1);
        Assert.Empty(Validator.ValidateIngredient(new LoadedIngredient { Manifest = manifest,
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["a"] = image } }));
    }

    [Theory]
    [InlineData("recipe")]
    [InlineData("variant")]
    [InlineData("color")]
    public void Overflowing_weight_totals_are_validation_problems(string table)
    {
        using var image = new Image<Rgba32>(1, 1);
        var ingredient = Ingredient(image);
        if (table == "variant") ingredient = WithManifest(ingredient, ingredient.Manifest with
            { Variants = new[] { new Variant("a", "A", double.MaxValue), new Variant("b", "B", double.MaxValue) } });
        if (table == "color") ingredient = WithManifest(ingredient, ingredient.Manifest with
            { Kind = LayerKind.Dynamic, Colorization = new(ColorModel.Hsv, 1, 1,
                new[] { new ColorEntry(double.MaxValue, null, "hex:ffffff"), new ColorEntry(double.MaxValue, null, "hex:ffffff") }) });
        var book = Book(Recipe(ingredient));
        if (table == "recipe") book = new LoadedCookBook { Manifest = book.Manifest with
            { RecipeWeights = new Dictionary<string, double> { ["r"] = double.MaxValue, ["other"] = double.MaxValue } }, Recipes = book.Recipes };
        Assert.Contains(Validator.Validate(book), p => p.Contains("non-finite total", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("a", "trigger")]
    [InlineData("b", "target")]
    public void Referenced_variants_cannot_be_deleted(string variantId, string reference)
    {
        using var image = new Image<Rgba32>(1, 1);
        var book = Book(Recipe(Ingredient(image)));
        string ingredientId = reference == "trigger" ? "i" : "j";
        var error = Assert.Throws<InvalidOperationException>(() => CookBookEdits.RemoveVariant(book, "r", ingredientId, variantId));
        Assert.Contains("rule 1", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(variantId, error.Message);
        Assert.Contains(reference, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, book.Recipes[0].Ingredients[0].Manifest.Variants.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Kitchen_creation_preserves_existing_bytes(bool canceled)
    {
        using var dir = new TempDirectory();
        string path = Path.Combine(dir.Path, "workspace.ktn");
        var manifest = new KitchenManifest("k", "Kitchen");
        KitchenArchive.Write(path, manifest);
        byte[] before = File.ReadAllBytes(path);
        Assert.Throws<IOException>(() => KitchenArchive.Write(path, manifest));
        if (canceled)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => KitchenArchive.WriteAsync(path, manifest, new CancellationToken(true)));
        else
            await Assert.ThrowsAsync<IOException>(() => KitchenArchive.WriteAsync(path, manifest));
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(manifest, KitchenArchive.Read(path));
    }

    [Fact]
    public async Task Pre_canceled_Kitchen_creation_does_not_create_a_file()
    {
        using var dir = new TempDirectory();
        string path = Path.Combine(dir.Path, "workspace.ktn");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => KitchenArchive.WriteAsync(path,
            new KitchenManifest("k", "Kitchen"), new CancellationToken(true)));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Nested_async_callback_keeps_archive_open_until_it_completes()
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
            ArchiveIo.WriteNested(zip, "nested.ktn", inner => ArchiveIo.WriteManifest(inner, new KitchenManifest("k", "Kitchen")));
        bytes.Position = 0;
        using var outer = new ZipArchive(bytes, ZipArchiveMode.Read);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = ArchiveIo.ReadNestedAsync(outer, "nested.ktn", async (inner, ct) =>
        {
            await resume.Task;
            return await ArchiveIo.ReadManifestAsync<KitchenManifest>(inner, ct);
        }, CancellationToken.None);
        Assert.False(pending.IsCompleted);
        resume.SetResult();
        var result = await pending;
        Assert.Equal("Kitchen", result.Name);
    }

    private static LoadedIngredient Ingredient(Image<Rgba32> image) => new()
    {
        Manifest = new IngredientManifest("i", "I", LayerKind.Custom, null,
            new[] { new Variant("a", "A", 1), new Variant("b", "B", 1) }),
        VariantImages = new Dictionary<string, Image<Rgba32>> { ["a"] = image, ["b"] = image },
    };

    private static LoadedIngredient WithManifest(LoadedIngredient ingredient, IngredientManifest manifest) =>
        new() { Manifest = manifest, VariantImages = ingredient.VariantImages };

    private static LoadedRecipe Recipe(LoadedIngredient ingredient) => new()
    {
        Manifest = new RecipeManifest("r", "R", new[] { "i", "j" }, new[]
            { new IncompatibilityRule(RuleType.Exclude, new("i", "a"), new[] { new RuleTarget("j", "b") }) }),
        Ingredients = new[] { ingredient, WithManifest(ingredient, new IngredientManifest("j", "J", LayerKind.Custom, null,
            new[] { new Variant("a", "A", 1), new Variant("b", "B", 1) })) },
    };

    private static LoadedCookBook Book(LoadedRecipe recipe) => new()
    {
        Manifest = new CookBookManifest("b", "B", new(1, 1), new("B", "", "B"), new Dictionary<string, double> { ["r"] = 1 }),
        Recipes = new[] { recipe },
    };

    private static void Read(string path, string kind)
    {
        using IDisposable loaded = kind switch
        { "igt" => IngredientArchive.Read(path), "rcp" => RecipeArchive.Read(path), _ => CookBookArchive.Read(path) };
    }

    private static async Task ReadAsync(string path, string kind)
    {
        using IDisposable loaded = kind switch
        { "igt" => await IngredientArchive.ReadAsync(path), "rcp" => await RecipeArchive.ReadAsync(path), _ => await CookBookArchive.ReadAsync(path) };
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory().FullName;
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
