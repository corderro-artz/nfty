using System.CommandLine;
using System.Text.Json;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using Nfty.Core.Publish;
using Nfty.Core.Publish.Community;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Cli.Tests;

/// <summary><c>nfty share check</c>: prints problems or an ok line, exits 1 on any problem, and
/// <c>--json</c> hands the Action the same facts.</summary>
public class ShareCommandTests : IDisposable
{
    private static readonly InvocationConfiguration NonThrowing = new() { EnableDefaultExceptionHandler = false };
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("nfty-sharecli-");

    public void Dispose() => _dir.Delete(recursive: true);

    private string Fresh(string leaf = "") =>
        Path.Combine(_dir.CreateSubdirectory(Guid.NewGuid().ToString("N")).FullName, leaf);

    private static (int Code, string Out) Run(params string[] args)
    {
        var orig = Console.Out;
        var o = new StringWriter();
        Console.SetOut(o);
        try { return (CommandFactory.Build().Parse(args).Invoke(NonThrowing), o.ToString()); }
        finally { Console.SetOut(orig); }
    }

    private static LoadedCookBook Book(int side = 4)
    {
        var ids = Enumerable.Range(0, 8).Select(v => $"v{v}").ToList();
        var ingredient = new LoadedIngredient
        {
            Manifest = new IngredientManifest("l0", "Layer 0", LayerKind.Custom, null,
                ids.Select(v => new Variant(v, v.ToUpperInvariant(), 1)).ToList()),
            VariantImages = ids.Select((v, i) => (v, i)).ToDictionary(t => t.v,
                t => new Image<Rgba32>(side, side, new Rgba32((byte)(t.i * 20), 0, 3, 255))),
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Chest Demo", new Dimensions(side, side),
                new Collection("Chest Demo", "d", "CHST"),
                new Dictionary<string, double> { ["chest"] = 100 }),
            Recipes =
            [
                new LoadedRecipe
                {
                    Manifest = new RecipeManifest("chest", "Chest", ["l0"], Array.Empty<IncompatibilityRule>()),
                    Ingredients = [ingredient],
                },
            ],
        };
    }

    private string Export(ExportOptions options, string? pass = null)
    {
        using var book = Book();
        string dir = Path.Combine(Fresh(), "ChestDemo");
        using var set = Generator.Generate(book, new GenerateOptions(2, "launch"));
        SetWriter.Write(set, dir, pack: false);
        return SetExporter.Export(dir, Fresh(), options, null, pass).Path;
    }

    [Fact]
    public void Share_check_passes_a_clean_set()
    {
        string path = Export(new ExportOptions());
        var (code, text) = Run("share", "check", path);
        Assert.Equal(0, code);
        Assert.Equal("ok: 2 assets · 4 x 4" + Environment.NewLine, text);
    }

    [Fact]
    public void Share_check_fails_a_sealed_set_with_exit_1()
    {
        string tin = Export(ExportOptions.For(ExportPreset.SealedCritique), "correct-horse-battery-staple");
        var (code, text) = Run("share", "check", tin);
        Assert.Equal(1, code);
        Assert.Equal(ShareRules.Sealed + Environment.NewLine, text);
    }

    [Fact]
    public void Share_check_json_has_assets_and_canvas()
    {
        string path = Export(new ExportOptions());
        var (code, text) = Run("share", "check", path, "--json");
        Assert.Equal(0, code);
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        Assert.Equal(2, root.GetProperty("assets").GetInt32());
        Assert.Equal(4, root.GetProperty("width").GetInt32());
        Assert.Equal(4, root.GetProperty("height").GetInt32());
        Assert.Equal(0, root.GetProperty("problems").GetArrayLength());
        Assert.Matches("^[0-9a-f]{64}$", root.GetProperty("fingerprint").GetString());
        Assert.Matches("^[0-9a-f]{64}$", root.GetProperty("fileSha256").GetString());
    }

    [Fact]
    public void Share_check_json_keeps_the_exit_code_rule()
    {
        string tin = Export(ExportOptions.For(ExportPreset.SealedCritique), "correct-horse-battery-staple");
        var (code, text) = Run("share", "check", tin, "--json");
        Assert.Equal(1, code);
        using var doc = JsonDocument.Parse(text);
        Assert.Equal(1, doc.RootElement.GetProperty("problems").GetArrayLength());
    }
}
