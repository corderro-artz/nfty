using System.CommandLine;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Cli.Tests;

/// <summary>
/// What <c>inspect</c> reports beyond the tree: a book's own palette, and voxel readiness.
/// </summary>
/// <remarks>
/// The palette travels inside the archive — a collection handed to someone else brings its colours
/// with it — and nothing on the command line would otherwise have shown it was there. Voxel
/// readiness is opt-in because it costs a full scan of every variant image, and because partial
/// alpha is legal: it is a report, not a validation.
/// </remarks>
public class InspectReportingTests
{
    private static readonly InvocationConfiguration NonThrowing = new() { EnableDefaultExceptionHandler = false };

    private static int Run(params string[] args) =>
        CommandFactory.Build().Parse(args).Invoke(NonThrowing);

    private static (int code, string outText, string errText) Capture(params string[] args)
    {
        var prevOut = Console.Out;
        var prevErr = Console.Error;
        using var outW = new StringWriter();
        using var errW = new StringWriter();
        Console.SetOut(outW);
        Console.SetError(errW);
        int code;
        try { code = Run(args); }
        finally { Console.SetOut(prevOut); Console.SetError(prevErr); }
        return (code, outW.ToString(), errW.ToString());
    }

    private static Image<Rgba32> Solid(byte alpha) => new(4, 4, new Rgba32(9, 9, 9, alpha));

    /// <summary>A one-recipe book on disk, optionally with a palette and a partial-alpha variant.</summary>
    private static string WriteBook(string dir, IReadOnlyList<string>? palette, byte secondAlpha)
    {
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("aura", "Aura", LayerKind.Custom, null,
                new[] { new Variant("glow", "Glow", 1), new Variant("soft", "Soft", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>>
            { ["glow"] = Solid(255), ["soft"] = Solid(secondAlpha) },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "aura" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        var path = Path.Combine(dir, "book.cbk");
        CookBookArchive.Write(path, new CookBookManifest("cb", "Book", new Dimensions(4, 4),
            new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 },
            Palette: palette), new[] { recipe });
        foreach (var i in ing.VariantImages.Values) i.Dispose();
        return path;
    }

    [Fact]
    public void A_books_palette_is_listed_as_the_specs_it_is_stored_as()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var path = WriteBook(dir, new[] { "hex:00ff66", "hex:d6249f" }, 255);

            var (code, text, _) = Capture("inspect", path);

            Assert.Equal(0, code);
            Assert.Contains("Palette: 2 swatches", text, StringComparison.Ordinal);
            Assert.Contains("hex:00ff66", text, StringComparison.Ordinal);
            Assert.Contains("hex:d6249f", text, StringComparison.Ordinal);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void One_swatch_reads_as_one_swatch()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var (_, text, _) = Capture("inspect", WriteBook(dir, new[] { "hex:00ff66" }, 255));

            Assert.Contains("Palette: 1 swatch", text, StringComparison.Ordinal);
            Assert.DoesNotContain("1 swatches", text, StringComparison.Ordinal);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_book_with_no_palette_prints_no_palette_section()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var (_, text, _) = Capture("inspect", WriteBook(dir, null, 255));

            Assert.DoesNotContain("Palette:", text, StringComparison.Ordinal);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Voxel_readiness_is_opt_in()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var path = WriteBook(dir, null, 128);

            Assert.DoesNotContain("Voxel readiness", Capture("inspect", path).outText, StringComparison.Ordinal);
            Assert.Contains("Voxel readiness", Capture("inspect", path, "--voxel").outText, StringComparison.Ordinal);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Voxel_names_the_variant_that_carries_partial_alpha()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var (code, text, _) = Capture("inspect", WriteBook(dir, null, 128), "--voxel");

            Assert.Equal(0, code);
            Assert.Contains("Aura / Soft [soft]", text, StringComparison.Ordinal);
            Assert.Contains("1 of 2 variants carries partial alpha.", text, StringComparison.Ordinal);
            // The tree is still printed; --voxel adds to inspect rather than replacing it.
            Assert.Contains("CookBook: Book", text, StringComparison.Ordinal);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_clean_book_reports_clean()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var (_, text, _) = Capture("inspect", WriteBook(dir, null, 0), "--voxel");

            Assert.Contains("All 2 variants voxelise cleanly.", text, StringComparison.Ordinal);
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>A Kitchen lists paths without opening them — that is the whole point of it. So
    /// <c>--voxel</c> there is REFUSED rather than quietly ignored: an option that silently does
    /// nothing is worse than one that says no.</summary>
    [Fact]
    public void Voxel_on_a_kitchen_is_refused_with_a_reason()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var ktn = Path.Combine(dir, "Studio.ktn");
            KitchenArchive.Write(ktn, new KitchenManifest("studio", "Studio"));

            // Commands in this CLI catch nothing: they throw, and Program prints ex.Message and
            // returns 1 (CommandFactory sets EnableDefaultExceptionHandler = false precisely so its
            // own handler runs). So the contract AT THIS LAYER is the exception and its wording —
            // and the wording is the whole point, since the message is shown to the user verbatim.
            var ex = Assert.Throws<InvalidOperationException>(() => Run("inspect", ktn, "--voxel"));

            Assert.Contains("--voxel", ex.Message, StringComparison.Ordinal);
            Assert.Contains("Kitchen", ex.Message, StringComparison.Ordinal);
            Assert.Contains("CookBooks", ex.Message, StringComparison.Ordinal);   // says what to do instead
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Voxel_works_on_a_loose_ingredient_too()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var igt = Path.Combine(dir, "aura.igt");
            var images = new Dictionary<string, Image<Rgba32>> { ["glow"] = Solid(128) };
            IngredientArchive.Write(igt, new IngredientManifest("aura", "Aura", LayerKind.Custom, null,
                new[] { new Variant("glow", "Glow", 1) }), images);
            foreach (var i in images.Values) i.Dispose();

            var (code, text, _) = Capture("inspect", igt, "--voxel");

            Assert.Equal(0, code);
            Assert.Contains("Voxel readiness: Aura", text, StringComparison.Ordinal);
            Assert.Contains("carries partial alpha", text, StringComparison.Ordinal);
        }
        finally { Directory.Delete(dir, true); }
    }
}
