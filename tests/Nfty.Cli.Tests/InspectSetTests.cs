using System.CommandLine;
using Nfty.Cli;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Nfty.Core.Output;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Cli.Tests;

/// <summary>
/// <c>inspect</c> on a cooked Set. The manual listed <c>.set</c> among the kinds inspect reads for
/// a long time before it did: <see cref="Archives.KindOf"/> knew four extensions, so the command
/// refused the one archive a person is most likely to have been handed by somebody else.
/// </summary>
public class InspectSetTests
{
    private static readonly InvocationConfiguration NonThrowing = new() { EnableDefaultExceptionHandler = false };

    private static int Run(params string[] args) =>
        CommandFactory.Build().Parse(args).Invoke(NonThrowing);

    /// <summary>Console.Out is PROCESS-wide, which is why this assembly disables test
    /// parallelization; it is restored in a finally so a failure here cannot break every later
    /// test in the run.</summary>
    private static string Capture(Action act)
    {
        var original = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        try { act(); }
        finally { Console.SetOut(original); }
        return writer.ToString();
    }

    /// <summary>Cooks a real Set with the CLI's own commands, so this exercises the path a user
    /// takes rather than a fixture built beside it.</summary>
    private static string CookTo(string dir, bool pack)
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var cbk = Path.Combine(root, "Book.cbk");
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "Background", LayerKind.Custom, null,
                new[] { new Variant("a", "A", 1), new Variant("b", "B", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>>
                { ["a"] = new(4, 4), ["b"] = new(4, 4) },
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

        var outDir = Path.Combine(root, dir);
        var args = new List<string> { "generate", cbk, "--count", "2", "--seed", "s", "--out", outDir };
        if (pack) args.Add("--pack");
        Assert.Equal(0, Run(args.ToArray()));
        return outDir;
    }

    [Fact]
    public void It_reads_a_packed_set()
    {
        var outDir = CookTo("cooked", pack: true);
        var archive = Path.Combine(outDir, "cooked.set");

        int code = 1;
        var output = Capture(() => code = Run("inspect", archive));

        Assert.Equal(0, code);
        Assert.Contains("Set: Vapor Cats", output);
        Assert.Contains("Assets: 2", output);
    }

    [Fact]
    public void It_reads_a_set_FOLDER_which_is_what_generate_writes()
    {
        // --pack is optional, so the folder is the common case: `inspect ./collection` right after
        // `generate --out ./collection` used to fail with "has no extension".
        var outDir = CookTo("cooked", pack: false);

        int code = 1;
        var output = Capture(() => code = Run("inspect", outDir));

        Assert.Equal(0, code);
        Assert.Contains("Set: Vapor Cats", output);
    }

    [Fact]
    public void Voxel_is_refused_on_a_set_and_says_where_to_run_it()
    {
        // Refused rather than quietly ignored, exactly as it is for a Kitchen. The message has to
        // name the CookBook, because that is where a partial-alpha variant can still be changed.
        var outDir = CookTo("cooked", pack: false);

        var ex = Assert.Throws<InvalidOperationException>(
            () => CommandFactory.Build().Parse(["inspect", outDir, "--voxel"]).Invoke(NonThrowing));

        Assert.Contains("CookBook", ex.Message);
    }

    [Fact]
    public void An_ordinary_folder_is_still_refused_rather_than_guessed_at()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;

        var ex = Assert.Throws<NotSupportedException>(
            () => CommandFactory.Build().Parse(["inspect", dir]).Invoke(NonThrowing));

        Assert.Contains("extension", ex.Message);
    }
}
