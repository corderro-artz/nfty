using System.CommandLine;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Cli.Tests;

/// <summary>
/// The edits the app makes that the command line now makes too. Each test runs the real command on a
/// real archive and re-reads the file, because the claim is that the change reaches the disk.
/// </summary>
public class AuthoringParityTests
{
    private static readonly InvocationConfiguration NonThrowing = new() { EnableDefaultExceptionHandler = false };

    private static int Run(params string[] args) =>
        CommandFactory.Build().Parse(args).Invoke(NonThrowing);

    private static LoadedIngredient Ing(string id, byte alpha, params string[] variants) => new()
    {
        Manifest = new IngredientManifest(id, id.ToUpperInvariant(), LayerKind.Custom, null,
            variants.Select(v => new Variant(v, v, 1)).ToArray()),
        VariantImages = variants.ToDictionary(
            v => v, _ => new Image<Rgba32>(4, 4, new Rgba32(10, 20, 30, alpha))),
    };

    private static LoadedRecipe Recipe(string id, byte hatAlpha = 255)
    {
        var ingredients = new[] { Ing("bg", 255, "day", "night"), Ing("hat", hatAlpha, "crown", "cap") };
        return new LoadedRecipe
        {
            Manifest = new RecipeManifest(id, id.ToUpperInvariant(), new[] { "bg", "hat" },
                Array.Empty<IncompatibilityRule>()),
            Ingredients = ingredients,
        };
    }

    private static string WriteBook(string dir, byte hatAlpha = 255)
    {
        var recipes = new[] { Recipe("cat", hatAlpha), Recipe("dog"), Recipe("owl") };
        var manifest = new CookBookManifest("book", "Book", new Dimensions(4, 4),
            new Collection("Pets", "desc", "PET"),
            new Dictionary<string, double> { ["cat"] = 3, ["dog"] = 2, ["owl"] = 1 });
        string path = Path.Combine(dir, "book.cbk");
        CookBookArchive.Write(path, manifest, recipes);
        foreach (var r in recipes) r.Dispose();
        return path;
    }

    private static void WithTemp(Action<string> body)
    {
        var tmp = Directory.CreateTempSubdirectory();
        try { body(tmp.FullName); }
        finally { tmp.Delete(recursive: true); }
    }

    private static string[] Dnas(string path)
    {
        using var book = CookBookArchive.Read(path);
        using var set = Generator.Generate(book, new GenerateOptions(8, "seed-1"));
        return set.Assets.Select(a => a.Dna).ToArray();
    }

    [Fact]
    public void Remove_ingredient_drops_the_layer_and_its_order_entry()
    {
        WithTemp(dir =>
        {
            string path = WriteBook(dir);
            Assert.Equal(0, Run("remove", "ingredient", path, "--recipe", "cat", "--id", "hat"));
            using var book = CookBookArchive.Read(path);
            var cat = book.Recipes.Single(r => r.Manifest.Id == "cat");
            Assert.Equal(new[] { "bg" }, cat.Manifest.LayerOrder);
            Assert.Single(cat.Ingredients);
            Assert.Equal(2, book.Recipes.Single(r => r.Manifest.Id == "dog").Ingredients.Count);
        });
    }

    [Fact]
    public void Remove_ingredient_on_a_loose_recipe_rejects_the_recipe_option()
    {
        WithTemp(dir =>
        {
            string rcp = Path.Combine(dir, "cat.rcp");
            using var r = Recipe("cat");
            RecipeArchive.Write(rcp, r.Manifest, r.Ingredients);

            Assert.Throws<InvalidOperationException>(() =>
                Run("remove", "ingredient", rcp, "--recipe", "cat", "--id", "hat"));
            Assert.Equal(0, Run("remove", "ingredient", rcp, "--id", "hat"));
            using var back = RecipeArchive.Read(rcp);
            Assert.Equal(new[] { "bg" }, back.Manifest.LayerOrder);
        });
    }

    private static LoadedRecipe RecipeWithHatChanceAndRule(string id)
    {
        var r = Recipe(id);
        return new LoadedRecipe
        {
            Manifest = r.Manifest with
            {
                AbsentPercent = new Dictionary<string, double> { ["hat"] = 40 },
                Rules = new[]
                {
                    new IncompatibilityRule(RuleType.Exclude, new RuleTarget("hat", "crown"),
                        new[] { new RuleTarget("bg", "night") }),
                },
            },
            Ingredients = r.Ingredients,
        };
    }

    [Fact]
    public void Remove_ingredient_takes_its_absent_chance_and_rules_with_it_so_the_book_stays_valid()
    {
        WithTemp(dir =>
        {
            string cbk = Path.Combine(dir, "book.cbk");
            var cat = RecipeWithHatChanceAndRule("cat");
            CookBookArchive.Write(cbk, new CookBookManifest("book", "Book", new Dimensions(4, 4),
                new Collection("Pets", "desc", "PET"), new Dictionary<string, double> { ["cat"] = 1 }),
                new[] { cat });
            Assert.Equal(0, Run("remove", "ingredient", cbk, "--recipe", "cat", "--id", "hat"));
            using (var book = CookBookArchive.Read(cbk))
            {
                Assert.Empty(Validator.Validate(book));
                Assert.Empty(book.Recipes[0].Manifest.Rules);
                Assert.Equal(0, book.Recipes[0].Manifest.AbsentPercentOf("hat"));
            }

            string rcp = Path.Combine(dir, "cat.rcp");
            RecipeArchive.Write(rcp, cat.Manifest, cat.Ingredients);
            Assert.Equal(0, Run("remove", "ingredient", rcp, "--id", "hat"));
            using var loose = RecipeArchive.Read(rcp);
            Assert.Empty(Validator.ValidateRecipe(loose));
            Assert.Empty(loose.Manifest.Rules);
        });
    }

    [Fact]
    public void Remove_variant_refuses_the_last_one()
    {
        WithTemp(dir =>
        {
            string path = WriteBook(dir);
            Assert.Equal(0, Run("remove", "variant", path, "--recipe", "cat", "--ingredient", "hat", "--id", "cap"));
            using (var book = CookBookArchive.Read(path))
                Assert.Equal(new[] { "crown" },
                    book.Recipes.Single(r => r.Manifest.Id == "cat").Ingredients
                        .Single(i => i.Manifest.Id == "hat").Manifest.Variants.Select(v => v.Id));

            var ex = Assert.Throws<InvalidOperationException>(() =>
                Run("remove", "variant", path, "--recipe", "cat", "--ingredient", "hat", "--id", "crown"));
            Assert.Contains("one variant left", ex.Message);
        });
    }

    [Fact]
    public void Remove_recipe_drops_its_weight()
    {
        WithTemp(dir =>
        {
            string path = WriteBook(dir);
            Assert.Equal(0, Run("remove", "recipe", path, "--id", "owl"));
            using var book = CookBookArchive.Read(path);
            Assert.DoesNotContain("owl", book.Manifest.RecipeWeights.Keys);
            Assert.DoesNotContain(book.Recipes, r => r.Manifest.Id == "owl");
        });
    }

    [Fact]
    public void Move_recipe_changes_presentation_order_only()
    {
        WithTemp(dir =>
        {
            string path = WriteBook(dir);
            var before = Dnas(path);
            Assert.Equal(0, Run("move", "recipe", path, "--id", "owl", "--to", "1"));

            using (var book = CookBookArchive.Read(path))
            {
                Assert.Equal(new[] { "owl", "cat", "dog" }, book.Manifest.RecipeOrder);
                Assert.Equal("owl", book.Recipes[0].Manifest.Id);
            }
            Assert.Equal(before, Dnas(path));
        });
    }

    [Fact]
    public void Set_variant_weight_rewrites_one_weight()
    {
        WithTemp(dir =>
        {
            string path = WriteBook(dir);
            Assert.Equal(0, Run("set", "variant", path, "--recipe", "cat", "--ingredient", "hat",
                "--id", "cap", "--weight", "7.5"));
            using var book = CookBookArchive.Read(path);
            var hat = book.Recipes.Single(r => r.Manifest.Id == "cat").Ingredients.Single(i => i.Manifest.Id == "hat");
            Assert.Equal(7.5, hat.Manifest.Variants.Single(v => v.Id == "cap").Weight);
            Assert.Equal(1, hat.Manifest.Variants.Single(v => v.Id == "crown").Weight);
        });
    }

    [Fact]
    public void Set_variant_weight_works_on_a_loose_ingredient_and_refuses_zero()
    {
        WithTemp(dir =>
        {
            string igt = Path.Combine(dir, "hat.igt");
            using var hat = Ing("hat", 255, "crown", "cap");
            IngredientArchive.Write(igt, hat.Manifest, hat.VariantImages, hat.Depths);

            Assert.NotEqual(0, Run("set", "variant", igt, "--id", "cap", "--weight", "0"));
            Assert.Equal(0, Run("set", "variant", igt, "--id", "cap", "--weight", "4"));
            using var back = IngredientArchive.Read(igt);
            Assert.Equal(4, back.Manifest.Variants.Single(v => v.Id == "cap").Weight);
        });
    }

    [Fact]
    public void Set_recipe_weight_zero_shelves_it()
    {
        WithTemp(dir =>
        {
            string path = WriteBook(dir);
            Assert.Equal(0, Run("set", "recipe", path, "--id", "dog", "--weight", "0"));
            using var book = CookBookArchive.Read(path);
            Assert.Equal(0, book.Manifest.RecipeWeights["dog"]);
            Assert.Equal(3, book.Recipes.Count);   // shelved, not deleted
            Assert.Equal(3, book.Manifest.RecipeWeights["cat"]);
        });
    }

    [Fact]
    public void Set_cookbook_changes_the_collection_name_and_target_supply()
    {
        WithTemp(dir =>
        {
            string path = WriteBook(dir);
            Assert.Equal(0, Run("set", "cookbook", path, "--collection-name", "Better Pets", "--target-supply", "1200"));
            using (var book = CookBookArchive.Read(path))
            {
                Assert.Equal("Better Pets", book.Manifest.Collection.Name);
                Assert.Equal("PET", book.Manifest.Collection.Symbol);
                Assert.Equal("Book", book.Manifest.Name);
                Assert.Equal(1200, book.Manifest.TargetSupply);
            }
            Assert.Equal(0, Run("set", "cookbook", path, "--target-supply", "none"));
            using var cleared = CookBookArchive.Read(path);
            Assert.Null(cleared.Manifest.TargetSupply);
            Assert.Equal("Better Pets", cleared.Manifest.Collection.Name);
        });
    }

    [Fact]
    public void Set_cookbook_without_options_is_refused()
    {
        WithTemp(dir =>
        {
            string path = WriteBook(dir);
            var ex = Assert.Throws<InvalidOperationException>(() => Run("set", "cookbook", path));
            Assert.Contains("Nothing to set", ex.Message);
        });
    }

    [Fact]
    public void Set_voxel_ready_dry_run_writes_nothing()
    {
        WithTemp(dir =>
        {
            string path = WriteBook(dir, hatAlpha: 100);
            byte[] before = File.ReadAllBytes(path);
            Assert.Equal(0, Run("set", "voxel-ready", path, "--dry-run"));
            Assert.Equal(before, File.ReadAllBytes(path));
        });
    }

    [Fact]
    public void Set_voxel_ready_snaps_partial_alpha()
    {
        WithTemp(dir =>
        {
            string path = WriteBook(dir, hatAlpha: 100);
            Assert.Equal(0, Run("set", "voxel-ready", path));
            using var book = CookBookArchive.Read(path);
            var hat = book.Recipes.Single(r => r.Manifest.Id == "cat").Ingredients.Single(i => i.Manifest.Id == "hat");
            Assert.All(hat.VariantImages.Values, img => Assert.Equal(0, img[0, 0].A));   // 100 < 128 -> clear
            Assert.Empty(VoxelReady.Plan(book));
        });
    }

    [Fact]
    public void Validate_accepts_a_loose_recipe_and_ingredient()
    {
        WithTemp(dir =>
        {
            string rcp = Path.Combine(dir, "cat.rcp");
            using var r = Recipe("cat");
            RecipeArchive.Write(rcp, r.Manifest, r.Ingredients);
            string igt = Path.Combine(dir, "hat.igt");
            using var hat = Ing("hat", 255, "crown");
            IngredientArchive.Write(igt, hat.Manifest, hat.VariantImages, hat.Depths);

            Assert.Equal(0, Run("validate", rcp));
            Assert.Equal(0, Run("validate", igt));
        });
    }

    [Fact]
    public void Inspect_prints_the_collection_and_target_supply()
    {
        WithTemp(dir =>
        {
            string path = WriteBook(dir);
            Run("set", "cookbook", path, "--target-supply", "1200");
            Run("move", "recipe", path, "--id", "owl", "--to", "1");

            var original = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            try { Assert.Equal(0, Run("inspect", path)); }
            finally { Console.SetOut(original); }

            string text = sw.ToString();
            Assert.Contains("Collection: Pets (PET)", text);
            Assert.Contains("Target supply: 1,200", text);
            Assert.Contains("Recipe order: owl, cat, dog", text);
        });
    }
}
