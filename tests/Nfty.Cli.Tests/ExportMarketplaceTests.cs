using System.CommandLine;
using System.Text.Json;
using Nfty.Cli;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Cli.Tests;

/// <summary>
/// <c>export</c>'s marketplace options: they parse, they reach the files, and they are rejected
/// (never ignored) where nothing would read them.
/// </summary>
public class ExportMarketplaceTests
{
    private static readonly InvocationConfiguration NonThrowing = new() { EnableDefaultExceptionHandler = false };

    private static ParseResult Parse(string line) => CommandFactory.Build().Parse(line);

    private static int Run(params string[] args) => CommandFactory.Build().Parse(args).Invoke(NonThrowing);

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

    private static string Cook(string root)
    {
        string cbk = Path.Combine(root, "Book.cbk");
        var list = Enumerable.Range(0, 6).Select(i => new Variant("v" + i, "V" + i, 1)).ToArray();
        var images = list.ToDictionary(v => v.Id, _ => new Image<Rgba32>(8, 8, new Rgba32(40, 90, 200, 255)));
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
            new CookBookManifest("cb", "Vapor Cats", new Dimensions(8, 8),
                new Collection("Vapor Cats", "Cats on waves", "VC"),
                new Dictionary<string, double> { ["cat"] = 100 }),
            new[] { recipe });
        string set = Path.Combine(root, "set");
        Assert.Equal(0, RunCaptured("generate", cbk, "--count", "3", "--out", set).Code);
        return set;
    }

    private static string Export(string set, string root, out string err, params string[] flags)
    {
        string dist = Path.Combine(root, "dist");
        var (code, _, e) = RunCaptured(new[] { "export", set, "--out", dist, "--folder" }.Concat(flags).ToArray());
        err = e;
        Assert.True(code == 0, e);
        return Directory.GetDirectories(dist).Single();
    }

    [Fact]
    public void Every_marketplace_option_parses()
    {
        var line = "export s --out d --base-uri ipfs://CID/ --token-names token-id --start-id 0 "
            + "--external-url https://x.example/t/ --collection --external-link https://x.example/ "
            + "--royalty 2.5 --fee-recipient 0x1234567890abcdef1234567890abcdef12345678 "
            + "--collection-image ipfs://CID/images/1.png --no-set-manifest";
        Assert.Empty(Parse(line).Errors);
        Assert.Empty(Parse("export s --out d --no-collection --set-manifest").Errors);
    }

    [Theory]
    [InlineData("--base-uri bafy")]
    [InlineData("--base-uri ipfs://CID")]
    [InlineData("--base-uri ftp://x/")]
    [InlineData("--token-names roman")]
    [InlineData("--start-id 2")]
    [InlineData("--external-url ipfs://x/")]
    [InlineData("--external-link site")]
    [InlineData("--royalty 101")]
    [InlineData("--royalty 2.555")]
    [InlineData("--royalty lots")]
    [InlineData("--fee-recipient 0x12")]
    [InlineData("--collection-image logo.png")]
    [InlineData("--collection --no-collection")]
    [InlineData("--set-manifest --no-set-manifest")]
    public void Bad_values_and_contradictions_are_parse_errors(string flags) =>
        Assert.NotEmpty(Parse("export s --out d " + flags).Errors);

    [Fact]
    public void The_base_uri_error_names_the_rule()
    {
        var errors = Parse("export s --out d --base-uri ipfs://CID").Errors;
        Assert.Contains(errors, e => e.Message.Contains("must end with '/'", StringComparison.Ordinal));
    }

    [Fact]
    public void Options_that_nothing_would_read_are_rejected_not_ignored()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string set = Cook(root);
        string dist = Path.Combine(root, "dist");

        // URI options against an export with no metadata and no collection.json.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Run("export", set, "--out", dist, "--no-opensea", "--base-uri", "ipfs://C/"));
        Assert.Contains("--base-uri", ex.Message);

        // Collection fields without --collection.
        ex = Assert.Throws<InvalidOperationException>(() =>
            Run("export", set, "--out", dist, "--royalty", "5"));
        Assert.Contains("--collection", ex.Message);

        ex = Assert.Throws<InvalidOperationException>(() =>
            Run("export", set, "--out", dist, "--no-opensea", "--external-url", "https://x/"));
        Assert.Contains("--external-url", ex.Message);

        Assert.False(Directory.Exists(dist));
    }

    [Fact]
    public void A_marketplace_export_is_upload_ready()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string set = Cook(root);

        string made = Export(set, root, out string err, "--preset", "marketplace",
            "--base-uri", "ipfs://CID/", "--token-names", "token-id", "--start-id", "0",
            "--external-url", "https://cats.example/", "--royalty", "5",
            "--fee-recipient", "0x1234567890abcdef1234567890abcdef12345678");

        Assert.DoesNotContain("warning", err, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(made, "metadata", "0")));
        Assert.True(File.Exists(Path.Combine(made, "images", "0.png")));
        Assert.False(File.Exists(Path.Combine(made, "set.json")));

        using var item = JsonDocument.Parse(File.ReadAllText(Path.Combine(made, "metadata", "0")));
        Assert.Equal("ipfs://CID/images/0.png", item.RootElement.GetProperty("image").GetString());
        Assert.Equal("Vapor Cats #0", item.RootElement.GetProperty("name").GetString());
        Assert.Equal("https://cats.example/0", item.RootElement.GetProperty("external_url").GetString());

        using var contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(made, "collection.json")));
        Assert.Equal(500, contract.RootElement.GetProperty("seller_fee_basis_points").GetInt32());
        Assert.Equal("VC", contract.RootElement.GetProperty("symbol").GetString());
        Assert.Equal("ipfs://CID/images/0.png", contract.RootElement.GetProperty("image").GetString());
    }

    [Fact]
    public void Relative_image_links_print_a_warning_on_stderr()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string set = Cook(root);

        Export(set, root, out string err);

        Assert.Contains("warning:", err, StringComparison.Ordinal);
        Assert.Contains("set a base URI", err, StringComparison.Ordinal);
    }

    [Fact]
    public void An_export_of_nothing_is_refused()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string set = Cook(root);

        var ex = Assert.Throws<ArgumentException>(() =>
            Run("export", set, "--out", Path.Combine(root, "dist"),
                "--no-images", "--no-opensea", "--no-nfty", "--no-models"));
        Assert.Contains("Nothing to export", ex.Message);
    }
}
