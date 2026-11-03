using System.CommandLine;
using Nfty.Cli;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Nfty.Core.Publish;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Cli.Tests;

/// <summary><c>inspect</c> on a marketplace upload bundle (no set.json): a summary to check it before uploading.</summary>
public class InspectBundleTests
{
    private static readonly InvocationConfiguration NonThrowing = new() { EnableDefaultExceptionHandler = false };

    private static int Run(params string[] args) => CommandFactory.Build().Parse(args).Invoke(NonThrowing);

    private static string Capture(Action act)
    {
        var original = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        try { act(); }
        finally { Console.SetOut(original); }
        return writer.ToString();
    }

    private static string CookAndBundle()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var cbk = Path.Combine(root, "Book.cbk");
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "Background", LayerKind.Custom, null,
                [new Variant("a", "A", 1), new Variant("b", "B", 1)]),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["a"] = new(4, 4), ["b"] = new(4, 4) },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", ["bg"], []),
            Ingredients = [ing],
        };
        CookBookArchive.Write(cbk,
            new CookBookManifest("cb", "Vapor Cats", new Dimensions(4, 4), new Collection("Vapor Cats", "d", "VC"),
                new Dictionary<string, double> { ["cat"] = 100 }),
            [recipe]);
        string set = Path.Combine(root, "cooked");
        Assert.Equal(0, Run("generate", cbk, "--count", "2", "--seed", "s", "--out", set));
        return SetExporter.Export(set, Path.Combine(root, "out"),
            ExportOptions.For(ExportPreset.Marketplace) with
            {
                Shape = ExportShape.Folder, TokenNaming = TokenNaming.TokenId, BaseUri = "ipfs://CID/",
                RoyaltyPercent = 2.5, FeeRecipient = "0x1234567890abcdef1234567890ABCDEF12345678",
            }).Path;
    }

    [Fact]
    public void Inspect_summarizes_an_upload_bundle_with_its_traits_and_collection_file()
    {
        string bundle = CookAndBundle();

        int code = 1;
        string output = Capture(() => code = Run("inspect", bundle));

        Assert.Equal(0, code);
        Assert.Contains("Upload bundle: Vapor Cats", output);
        Assert.Contains("not a Set", output);
        Assert.Contains("Items: 2", output);
        Assert.Contains("Pictures found in the bundle: 2 of 2", output);
        Assert.Contains("collection.json:", output);
        Assert.Contains("Symbol: VC", output);
        Assert.Contains("Royalty: 2.5% (250 basis points)", output);
        Assert.Contains("Background (", output);
    }

    [Fact]
    public void Voxel_is_refused_on_a_bundle()
    {
        string bundle = CookAndBundle();
        var ex = Assert.Throws<InvalidOperationException>(
            () => CommandFactory.Build().Parse(["inspect", bundle, "--voxel"]).Invoke(NonThrowing));
        Assert.Contains("CookBook", ex.Message);
    }
}
