using System.CommandLine;
using System.Text;
using Nfty.Cli;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Nfty.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Cli.Tests;

/// <summary>
/// <c>generate --models</c> and <c>extend</c>: a Set cooked with 3D models, and a Set that keeps the
/// options it was cooked with. Model options without <c>--models</c> are REJECTED, never ignored.
/// </summary>
public class GenerateModelsCommandTests
{
    private static readonly InvocationConfiguration NonThrowing = new() { EnableDefaultExceptionHandler = false };

    private static int Run(params string[] args) => CommandFactory.Build().Parse(args).Invoke(NonThrowing);

    private static ParseResult Parse(string line) => CommandFactory.Build().Parse(line);

    /// <summary>Console.Out/Error are process-wide (this assembly does not parallelize tests).</summary>
    private static (int Code, string Out, string Err) RunCaptured(params string[] args)
    {
        var origOut = Console.Out;
        var origErr = Console.Error;
        var o = new StringWriter();
        var e = new StringWriter();
        Console.SetOut(o);
        Console.SetError(e);
        try
        {
            int code = Run(args);
            return (code, o.ToString(), e.ToString());
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }
    }

    /// <summary>One Custom layer, one variant per entry of <paramref name="pixels"/>; variants are
    /// solid 4x4 fills of the given color (alpha included, so a book can be opaque, clear or partial).</summary>
    private static string WriteBook(string root, params Rgba32[] pixels)
    {
        string cbk = Path.Combine(root, "Book.cbk");
        var variants = pixels.Select((_, i) => new Variant("v" + i, "V" + i, 1)).ToArray();
        var images = new Dictionary<string, Image<Rgba32>>();
        for (int i = 0; i < pixels.Length; i++) images["v" + i] = new Image<Rgba32>(4, 4, pixels[i]);
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "Background", LayerKind.Custom, null, variants),
            VariantImages = images,
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "bg" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        CookBookArchive.Write(cbk,
            new CookBookManifest("cb", "Vapor Cats", new Dimensions(4, 4),
                new Collection("Vapor Cats", "d", "VC"),
                new Dictionary<string, double> { ["cat"] = 100 }),
            new[] { recipe });
        return cbk;
    }

    private static Rgba32[] Opaque(int n) =>
        Enumerable.Range(0, n).Select(i => new Rgba32((byte)(40 + i * 30), 90, 200, 255)).ToArray();

    private static string NewRoot() => Directory.CreateTempSubdirectory().FullName;

    [Fact]
    public void Generate_with_models_writes_a_model_per_asset()
    {
        string root = NewRoot();
        string cbk = WriteBook(root, Opaque(6));
        string outDir = Path.Combine(root, "set");

        var (code, text, _) = RunCaptured("generate", cbk, "--count", "3", "--out", outDir, "--models");

        Assert.Equal(0, code);
        for (int n = 1; n <= 3; n++)
        {
            byte[] glb = File.ReadAllBytes(Path.Combine(outDir, "models", $"{n:D4}.glb"));
            Assert.Equal("glTF", Encoding.ASCII.GetString(glb, 0, 4));
        }
        Assert.Contains("models: glb · bottom-center · voxel 1", text);
        Assert.DoesNotContain("skipped", text);
    }

    [Fact]
    public void Generate_without_models_writes_none()
    {
        string root = NewRoot();
        string cbk = WriteBook(root, Opaque(6));
        string outDir = Path.Combine(root, "set");

        var (code, text, _) = RunCaptured("generate", cbk, "--count", "3", "--out", outDir);

        Assert.Equal(0, code);
        Assert.False(Directory.Exists(Path.Combine(outDir, "models")));
        Assert.DoesNotContain("models:", text);
    }

    [Theory]
    [InlineData("--model-format gltf")]
    [InlineData("--pivot center")]
    [InlineData("--voxel-size 0.5")]
    public void Model_options_without_models_are_a_parse_error(string option)
    {
        var errors = Parse($"generate book.cbk --count 3 --out o {option}").Errors;

        string name = option.Split(' ')[0];
        Assert.Contains(errors, e => e.Message.Contains(name) && e.Message.Contains("--models"));
        // And with --models the same line is fine.
        Assert.Empty(Parse($"generate book.cbk --count 3 --out o --models {option}").Errors);
    }

    [Fact]
    public void A_voxel_size_that_is_not_positive_is_refused_like_model_refuses_it()
    {
        Assert.NotEmpty(Parse("generate book.cbk --count 3 --out o --models --voxel-size 0").Errors);
        Assert.NotEmpty(Parse("generate book.cbk --count 3 --out o --models --voxel-size -1").Errors);
    }

    [Fact]
    public void A_two_d_only_book_is_refused_and_nothing_is_written()
    {
        string root = NewRoot();
        string cbk = WriteBook(root, new Rgba32(10, 20, 30, 128));
        string outDir = Path.Combine(root, "set");

        var ex = Assert.Throws<PartialAlphaBookException>(
            () => CommandFactory.Build().Parse(["generate", cbk, "--count", "2", "--out", outDir, "--models"])
                .Invoke(NonThrowing));

        Assert.Contains("Vapor Cats", ex.Message);
        Assert.True(!Directory.Exists(outDir) || !Directory.EnumerateFileSystemEntries(outDir).Any());
    }

    [Fact]
    public void Generate_models_is_byte_identical_twice()
    {
        string root = NewRoot();
        string cbk = WriteBook(root, Opaque(6));
        string a = Path.Combine(root, "a");
        string b = Path.Combine(root, "b");

        Assert.Equal(0, Run("generate", cbk, "--count", "3", "--out", a, "--models", "--model-format", "gltf-embedded"));
        Assert.Equal(0, Run("generate", cbk, "--count", "3", "--out", b, "--models", "--model-format", "gltf-embedded"));

        for (int n = 1; n <= 3; n++)
            Assert.Equal(File.ReadAllBytes(Path.Combine(a, "models", $"{n:D4}.gltf")),
                File.ReadAllBytes(Path.Combine(b, "models", $"{n:D4}.gltf")));
    }

    [Fact]
    public void Extend_inherits_the_recorded_model_options()
    {
        string root = NewRoot();
        string cbk = WriteBook(root, Opaque(6));
        string outDir = Path.Combine(root, "set");
        Assert.Equal(0, Run("generate", cbk, "--count", "2", "--out", outDir, "--models",
            "--model-format", "gltf", "--pivot", "center", "--voxel-size", "0.5"));

        var (code, text, _) = RunCaptured("extend", cbk, outDir, "--to", "4");

        Assert.Equal(0, code);
        foreach (int n in new[] { 3, 4 })
        {
            Assert.True(File.Exists(Path.Combine(outDir, "models", $"{n:D4}.gltf")));
            Assert.True(File.Exists(Path.Combine(outDir, "models", $"{n:D4}.bin")));
            Assert.False(File.Exists(Path.Combine(outDir, "models", $"{n:D4}.glb")));
        }
        Assert.Contains("models: gltf · center · voxel 0.5", text);
    }

    [Fact]
    public void Extend_takes_no_model_flags()
    {
        Assert.NotEmpty(Parse("extend book.cbk set --to 4 --models").Errors);
    }

    [Fact]
    public void Voxel_size_parses_the_same_under_a_comma_decimal_culture()
    {
        // The built-in float converter reads the CURRENT culture: under de-DE "0.5" is 5. Both
        // commands share one invariant parser, so both must read 0.5 here.
        var original = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        try
        {
            var model = Parse("model sprite.png --out sprite.glb --voxel-size 0.5");
            var generate = Parse("generate book.cbk --count 3 --out o --models --voxel-size 0.5");

            Assert.Empty(model.Errors);
            Assert.Empty(generate.Errors);
            Assert.Equal(0.5, model.GetValue<double>("--voxel-size"));
            Assert.Equal(0.5, generate.GetValue<double>("--voxel-size"));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Extend_lists_only_the_new_assets_skips()
    {
        // Fully clear art: every asset is skipped. The first cook skips #0001; extending to two
        // skips #0002, and only that one belongs to this run's report.
        string root = NewRoot();
        string cbk = WriteBook(root, new Rgba32(0, 0, 0, 0));
        string outDir = Path.Combine(root, "set");
        Assert.Equal(0, Run("generate", cbk, "--count", "1", "--out", outDir, "--models", "--unlimited"));

        var (code, text, _) = RunCaptured("extend", cbk, outDir, "--to", "2", "--unlimited");

        Assert.Equal(0, code);
        Assert.Contains("#0002", text);
        Assert.DoesNotContain("#0001", text);
    }

    [Fact]
    public void Generate_prints_skipped_assets()
    {
        string root = NewRoot();
        string cbk = WriteBook(root, new Rgba32(0, 0, 0, 0));
        string outDir = Path.Combine(root, "set");

        var (code, text, _) = RunCaptured("generate", cbk, "--count", "1", "--out", outDir, "--models", "--unlimited");

        Assert.Equal(0, code);
        Assert.Contains("skipped (nothing fully opaque): #0001", text);
    }
}
