using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using Nfty.Core.Publish;
using Nfty.Ticket;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Cli.Tests;

public class SampleProgramsTests
{
    [Theory]
    [InlineData(ColorModel.Hsv)]
    [InlineData(ColorModel.Hsl)]
    public void Ticket_sample_color_example_matches_the_actual_colorizer(ColorModel model)
    {
        WithFixture(model, false, (book, menu, _) =>
        {
            var (code, output) = Run("Nfty.Ticket.Sample", "en-US", menu, "player-42");
            Assert.Equal(0, code);
            using var archive = MenuArchive.Open(menu);
            var rng = new SplitMix64Rng(SeedHash.ToUlong("player-42"));
            var ticket = Ticketer.TryRoll(Ticketer.Prepare(archive.Book), rng)!;
            var draw = Plating.Plate(archive, ticket).First(d => d.Kind != LayerKind.Custom);
            using var gray = new Image<Rgba32>(1, 1, new Rgba32(128, 128, 128, 255));
            using var colored = Colorizer.Apply(gray, draw.Hue, draw.Sat, model);
            var pixel = colored[0, 0];
            Assert.Contains("mid-gray becomes " + ColorSpec.Format(new RgbColor(pixel.R, pixel.G, pixel.B)), output);
        });
    }

    [Fact]
    public void Ticket_sample_reports_are_identical_across_locales()
    {
        WithFixture(ColorModel.Hsv, false, (_, menu, _) =>
        {
            var english = Run("Nfty.Ticket.Sample", "en-US", menu, "player-42");
            var french = Run("Nfty.Ticket.Sample", "fr-FR", menu, "player-42");
            Assert.Equal(0, english.Code);
            Assert.Equal(english, french);
        });
    }

    [Fact]
    public void Embedding_sample_reports_and_payloads_are_identical_across_locales()
    {
        WithFixture(ColorModel.Hsv, false, (book, menu, directory) =>
        {
            string englishDir = Path.Combine(directory, "english"), frenchDir = Path.Combine(directory, "french");
            var english = Run("Nfty.Embedding.Sample", "en-US", book, menu, englishDir);
            var french = Run("Nfty.Embedding.Sample", "fr-FR", book, menu, frenchDir);
            Assert.Equal(0, english.Code);
            Assert.Equal(english, french);
            var files = Directory.GetFiles(englishDir).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(files, Directory.GetFiles(frenchDir).Select(Path.GetFileName).Order(StringComparer.Ordinal));
            foreach (var file in files)
                Assert.Equal(SHA256.HashData(File.ReadAllBytes(Path.Combine(englishDir, file!))),
                    SHA256.HashData(File.ReadAllBytes(Path.Combine(frenchDir, file!))));
        });
    }

    [Fact]
    public void Embedding_sample_retries_a_rejected_roll_in_a_valid_book()
    {
        WithFixture(ColorModel.Hsv, true, (book, menu, directory) =>
        {
            using (var archive = MenuArchive.Open(menu))
                Assert.Null(Ticketer.TryRoll(Ticketer.Prepare(archive.Book), new SplitMix64Rng(7)));
            string output = Path.Combine(directory, "out");
            Assert.Equal(0, Run("Nfty.Embedding.Sample", "en-US", book, menu, output).Code);
            Assert.Equal(4, Directory.GetFiles(output, "*.png").Length);
            Assert.Single(Directory.GetFiles(output, "*.glb"));
        });
    }

    private static (int Code, string Output) Run(string project, string culture, params string[] arguments)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "nfty.sln")))
            root = root.Parent ?? throw new InvalidOperationException("Repository root not found.");
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(
            Path.Combine(root.FullName, "samples", project, "bin", configuration, "net10.0", project + ".dll"));
        var previousCulture = CultureInfo.CurrentCulture;
        var previousOutput = Console.Out;
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            Console.SetOut(output);
            return ((int)assembly.EntryPoint!.Invoke(null, [arguments])!, output.ToString());
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
        finally { Console.SetOut(previousOutput); CultureInfo.CurrentCulture = previousCulture; }
    }

    private static void WithFixture(ColorModel model, bool rejectFirst, Action<string, string, string> check)
    {
        var directory = Directory.CreateTempSubdirectory("nfty-samples-");
        using var ingredient = new LoadedIngredient
        {
            Manifest = new IngredientManifest("shade", "Shade", LayerKind.Static,
                new Colorization(model, 12, 4, [new ColorEntry(1, null, "hsv:210.5,61.25,50")]),
                [new("a", "A", 1), new("b", "B", 1), new("c", "C", 1), new("d", "D", 1)]),
            VariantImages = new[] { "a", "b", "c", "d" }.ToDictionary(id => id,
                _ => new Image<Rgba32>(2, 2, new Rgba32(128, 128, 128, 255))),
        };
        using var marker = new LoadedIngredient
        {
            Manifest = new IngredientManifest("mark", "Mark", LayerKind.Custom, null, [new("m", "M", 1)]),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["m"] = new(2, 2) },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("r", "Recipe", ["shade", "mark"], []),
            Ingredients = [ingredient, marker],
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("b", "Book", new Dimensions(2, 2), new Collection("Book", "", "B"),
                new Dictionary<string, double> { ["r"] = 1 }),
            Recipes = [recipe],
        };
        try
        {
            string menuPath = Path.Combine(directory.FullName, "menu.mnu");
            MenuWriter.Write(book, menuPath, new MenuWriteOptions());
            if (rejectFirst)
            {
                string first;
                using (var menu = MenuArchive.Open(menuPath))
                    first = Ticketer.TryRoll(Ticketer.Prepare(menu.Book), new SplitMix64Rng(7))!
                        .Layers.Single(l => l.IngredientId == "shade").VariantId;
                recipe = new LoadedRecipe
                {
                    Manifest = recipe.Manifest with { Rules = [new IncompatibilityRule(RuleType.Exclude,
                        new RuleTarget("shade", first), [new RuleTarget("mark", "m")])] },
                    Ingredients = recipe.Ingredients,
                };
                book = new LoadedCookBook { Manifest = book.Manifest, Recipes = [recipe] };
                MenuWriter.Write(book, menuPath, new MenuWriteOptions());
            }
            string bookPath = Path.Combine(directory.FullName, "book.cbk");
            CookBookArchive.Write(bookPath, book.Manifest, book.Recipes);
            check(bookPath, menuPath, directory.FullName);
        }
        finally { directory.Delete(true); }
    }
}
