using System.CommandLine;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Nfty.Core.Stats;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Cli.Tests;

/// <summary>
/// `set chance` — how often a layer is left out of an asset entirely. End to end on a real archive,
/// because the claim that matters is that the number reaches the file and comes back.
/// </summary>
public class ChanceCommandTests
{
    private static readonly InvocationConfiguration NonThrowing = new() { EnableDefaultExceptionHandler = false };

    private static int Run(params string[] args) =>
        CommandFactory.Build().Parse(args).Invoke(NonThrowing);

    private static LoadedIngredient Ing(string id, params string[] variants) => new()
    {
        Manifest = new IngredientManifest(id, id.ToUpperInvariant(), LayerKind.Custom, null,
            variants.Select(v => new Variant(v, v, 1)).ToArray()),
        VariantImages = variants.ToDictionary(
            v => v, _ => new Image<Rgba32>(4, 4, new Rgba32(10, 20, 30, 255))),
    };

    private static string WriteRecipe(string dir)
    {
        var ingredients = new[] { Ing("bg", "day", "night"), Ing("hat", "crown", "cap") };
        var manifest = new RecipeManifest("cat", "Cat", new[] { "bg", "hat" },
            Array.Empty<IncompatibilityRule>());
        string path = Path.Combine(dir, "cat.rcp");
        RecipeArchive.Write(path, manifest, ingredients);
        foreach (var i in ingredients) i.Dispose();
        return path;
    }

    [Fact]
    public void A_chance_reaches_the_archive_and_reads_back()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string path = WriteRecipe(tmp.FullName);
            Assert.Equal(0, Run("set", "chance", path, "--id", "hat", "--absent", "85"));

            using var loaded = RecipeArchive.Read(path);
            Assert.Equal(85, loaded.Manifest.AbsentPercentOf("hat"));
            Assert.Equal(0, loaded.Manifest.AbsentPercentOf("bg"));   // its neighbour is untouched
            Assert.True(loaded.Manifest.HasOptionalLayers);
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Setting_zero_clears_it_rather_than_storing_a_zero()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string path = WriteRecipe(tmp.FullName);
            Run("set", "chance", path, "--id", "hat", "--absent", "85");
            Assert.Equal(0, Run("set", "chance", path, "--id", "hat", "--absent", "0"));

            using var loaded = RecipeArchive.Read(path);
            // "Always appears" is the ABSENCE of an entry, not an entry saying zero — so a recipe
            // that has been given a chance and had it taken away is indistinguishable from one that
            // never had it, and the GUI's derived toggle goes back off with it.
            Assert.Null(loaded.Manifest.AbsentPercent);
            Assert.False(loaded.Manifest.HasOptionalLayers);
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void A_layer_the_recipe_does_not_stack_is_refused_in_the_command_lines_own_words()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string path = WriteRecipe(tmp.FullName);
            var ex = Assert.Throws<InvalidOperationException>(() => CommandFactory.Build()
                .Parse(new[] { "set", "chance", path, "--id", "wings", "--absent", "50" })
                .Invoke(NonThrowing));

            Assert.Contains("--id 'wings'", ex.Message);
            // AbsentChance's own guard throws ArgumentException, whose Message appends
            // "(Parameter 'ingredientId')" — and there is no `ingredientId` on this command line.
            Assert.DoesNotContain("Parameter", ex.Message);

            using var loaded = RecipeArchive.Read(path);
            Assert.Null(loaded.Manifest.AbsentPercent);      // and the refusal wrote nothing
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("101")]
    public void A_percent_outside_the_range_is_refused_by_the_parser(string pct)
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string path = WriteRecipe(tmp.FullName);
            Assert.NotEqual(0, Run("set", "chance", path, "--id", "hat", "--absent", pct));

            using var loaded = RecipeArchive.Read(path);
            Assert.Null(loaded.Manifest.AbsentPercent);
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Inspect_prints_the_chance_beside_the_layer_it_belongs_to()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string path = WriteRecipe(tmp.FullName);
            Run("set", "chance", path, "--id", "hat", "--absent", "85");

            var original = Console.Out;
            var sw = new StringWriter();
            Console.SetOut(sw);
            try { Assert.Equal(0, Run("inspect", path)); }
            finally { Console.SetOut(original); }

            var text = sw.ToString();
            Assert.Contains("Ingredient: HAT [hat] (Custom)  absent 85%", text);
            Assert.Contains("Ingredient: BG [bg] (Custom)", text);
            Assert.DoesNotContain("BG [bg] (Custom)  absent", text);
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Inspect_says_never_appears_rather_than_absent_a_hundred_percent()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string path = WriteRecipe(tmp.FullName);
            Run("set", "chance", path, "--id", "hat", "--absent", "100");

            var original = Console.Out;
            var sw = new StringWriter();
            Console.SetOut(sw);
            try { Run("inspect", path); }
            finally { Console.SetOut(original); }

            Assert.Contains("never appears", sw.ToString());
        }
        finally { tmp.Delete(recursive: true); }
    }

    /// <summary>
    /// The report gains a section only when a book uses the feature. These reports get copied
    /// between machines and compared, so a new empty heading would make two identical collections
    /// look different.
    /// </summary>
    [Fact]
    public void The_report_grows_an_optional_layers_section_only_when_there_are_any()
    {
        LoadedCookBook Book(IReadOnlyDictionary<string, double>? absent) => new()
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(4, 4),
                new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 }),
            Recipes = new[]
            {
                new LoadedRecipe
                {
                    Manifest = new RecipeManifest("cat", "Cat", new[] { "bg", "hat" },
                        Array.Empty<IncompatibilityRule>(), AbsentPercent: absent),
                    Ingredients = new[] { Ing("bg", "day", "night"), Ing("hat", "crown", "cap") },
                },
            },
        };

        using (var plain = Book(null))
            Assert.DoesNotContain("Optional layers:", CollectionReport.Render(plain));

        using (var chase = Book(new Dictionary<string, double> { ["hat"] = 85 }))
        {
            var text = CollectionReport.Render(chase);
            Assert.Contains("Optional layers:", text);
            Assert.Contains("absent  85.00%  present  15.00%", text);
        }

        using (var shelved = Book(new Dictionary<string, double> { ["hat"] = 100 }))
            Assert.Contains("never appears", CollectionReport.Render(shelved));
    }
}
