using System.CommandLine;
using Nfty.Cli;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Nfty.Core.Publish;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Cli.Tests;

/// <summary>
/// <c>export</c> reaches every axis the export dialog has: models, spritesheet, number watermark, and
/// the source-book provenance warning. Real cooks, real exports; the dialog's own tests live in the App.
/// </summary>
public class ExportParityTests
{
    private static readonly InvocationConfiguration NonThrowing = new() { EnableDefaultExceptionHandler = false };

    private static int Run(params string[] args) => CommandFactory.Build().Parse(args).Invoke(NonThrowing);

    private static ParseResult Parse(string line) => CommandFactory.Build().Parse(line);

    private static (int Code, string Out, string Err) RunCaptured(params string[] args)
    {
        var origOut = Console.Out;
        var origErr = Console.Error;
        var o = new StringWriter();
        var e = new StringWriter();
        Console.SetOut(o);
        Console.SetError(e);
        try { return (Run(args), o.ToString(), e.ToString()); }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }
    }

    private static readonly Rgba32 Fill = new(40, 90, 200, 255);

    /// <summary>One Custom layer of <paramref name="variants"/> solid <paramref name="size"/> squares.</summary>
    private static string WriteBook(string root, string file, int variants, int size = 16)
    {
        string cbk = Path.Combine(root, file);
        var list = Enumerable.Range(0, variants).Select(i => new Variant("v" + i, "V" + i, 1)).ToArray();
        var images = new Dictionary<string, Image<Rgba32>>();
        for (int i = 0; i < variants; i++) images["v" + i] = new Image<Rgba32>(size, size, Fill);
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "Background", LayerKind.Custom, null, list),
            VariantImages = images,
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "bg" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        CookBookArchive.Write(cbk,
            new CookBookManifest("cb", "Vapor Cats", new Dimensions(size, size),
                new Collection("Vapor Cats", "d", "VC"),
                new Dictionary<string, double> { ["cat"] = 100 }),
            new[] { recipe });
        return cbk;
    }

    private static string Cook(string root, int count, bool models)
    {
        string cbk = WriteBook(root, "Book.cbk", 6);
        string set = Path.Combine(root, "set");
        var args = new List<string> { "generate", cbk, "--count", count.ToString(), "--out", set };
        if (models) args.Add("--models");
        Assert.Equal(0, RunCaptured(args.ToArray()).Code);
        return set;
    }

    /// <summary>Exports as a folder and returns that folder.</summary>
    private static string ExportFolder(string set, string root, params string[] flags)
    {
        string dist = Path.Combine(root, "dist");
        var (code, _, err) = RunCaptured(new[] { "export", set, "--out", dist, "--folder" }.Concat(flags).ToArray());
        Assert.True(code == 0, err);
        return Directory.GetDirectories(dist).Single();
    }

    private static string NewRoot() => Directory.CreateTempSubdirectory().FullName;

    [Fact]
    public void Export_no_models_drops_the_models_folder()
    {
        string root = NewRoot();
        string set = Cook(root, 2, models: true);

        string kept = ExportFolder(set, root);
        Assert.True(Directory.Exists(Path.Combine(kept, "models")));

        Directory.Delete(Path.Combine(root, "dist"), recursive: true);
        string dropped = ExportFolder(set, root, "--no-models");
        Assert.False(Directory.Exists(Path.Combine(dropped, "models")));
    }

    [Fact]
    public void Export_models_on_a_set_without_models_is_refused()
    {
        string root = NewRoot();
        string set = Cook(root, 2, models: false);

        var ex = Assert.Throws<InvalidOperationException>(
            () => Run("export", set, "--out", Path.Combine(root, "dist"), "--models"));

        Assert.Equal("This Set has no 3D models; it was cooked without --models.", ex.Message);
        Assert.False(Directory.Exists(Path.Combine(root, "dist")));
    }

    [Fact]
    public void Export_models_on_a_set_with_models_is_accepted()
    {
        string root = NewRoot();
        string set = Cook(root, 2, models: true);

        string made = ExportFolder(set, root, "--models");

        Assert.True(Directory.Exists(Path.Combine(made, "models")));
    }

    [Fact]
    public void Export_sheet_writes_a_spritesheet_with_its_grid_chunk()
    {
        string root = NewRoot();
        string set = Cook(root, 3, models: false);

        string made = ExportFolder(set, root, "--sheet", "--sheet-cols", "3");

        var grid = SheetGrid.Read(Path.Combine(made, "spritesheet.png"));
        Assert.Equal(new SheetGrid(3, 1, 16, 16, 3), grid);
    }

    [Fact]
    public void Export_sheet_cols_without_sheet_is_refused()
    {
        var errors = Parse("export s --out d --sheet-cols 3").Errors;
        Assert.Contains(errors, e => e.Message.Contains("--sheet-cols") && e.Message.Contains("--sheet"));
        Assert.Contains(Parse("export s --out d --sheet-rows 2").Errors, e => e.Message.Contains("--sheet-rows"));
        Assert.Empty(Parse("export s --out d --sheet --sheet-cols 3 --sheet-rows 1").Errors);
        Assert.NotEmpty(Parse("export s --out d --sheet --sheet-cols 0").Errors);
    }

    [Fact]
    public void Export_watermark_stamps_the_chosen_corner()
    {
        string root = NewRoot();
        string set = Cook(root, 1, models: false);

        string made = ExportFolder(set, root, "--watermark", "--watermark-corner", "bottom-left");

        using var img = Image.Load<Rgba32>(Directory.GetFiles(Path.Combine(made, "images"), "*.png").Single());
        var changed = new List<(int X, int Y)>();
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
                if (img[x, y] != Fill) changed.Add((x, y));

        Assert.NotEmpty(changed);
        Assert.All(changed, p =>
        {
            Assert.True(p.X < img.Width / 2, $"ink at x={p.X}");
            Assert.True(p.Y >= img.Height / 2, $"ink at y={p.Y}");
        });
    }

    [Fact]
    public void Export_watermark_corner_without_watermark_is_refused()
    {
        var errors = Parse("export s --out d --watermark-corner top-left").Errors;
        Assert.Contains(errors, e => e.Message.Contains("--watermark-corner") && e.Message.Contains("--watermark"));
        Assert.NotEmpty(Parse("export s --out d --watermark --watermark-corner middle").Errors);
        Assert.Empty(Parse("export s --out d --watermark --watermark-corner top-right").Errors);
    }

    [Fact]
    public void Export_with_a_different_book_warns_and_still_exports()
    {
        string root = NewRoot();
        string set = Cook(root, 2, models: false);
        string other = WriteBook(root, "Other.cbk", 3);

        string dist = Path.Combine(root, "dist");
        var (code, _, err) = RunCaptured("export", set, "--out", dist, "--folder", "--book", other);

        Assert.Equal(0, code);
        Assert.Contains("this CookBook is not the one this Set was cooked from", err);
        string made = Directory.GetDirectories(dist).Single();
        Assert.True(File.Exists(Path.Combine(made, "Other.cbk")));
    }

    [Fact]
    public void Export_with_the_same_book_says_nothing()
    {
        string root = NewRoot();
        string set = Cook(root, 2, models: false);

        var (code, _, err) = RunCaptured("export", set, "--out", Path.Combine(root, "dist"), "--folder",
            "--book", Path.Combine(root, "Book.cbk"));

        Assert.Equal(0, code);
        Assert.DoesNotContain("not the one", err);
    }
}
