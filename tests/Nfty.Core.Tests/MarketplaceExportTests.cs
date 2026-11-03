using System.Text.Json;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using Nfty.Core.Publish;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>
/// The export-only marketplace options: base URI, token naming, collection.json, external and
/// animation links, and an optional set.json. The cooked Set is never changed by any of them.
/// </summary>
public class MarketplaceExportTests
{
    private const string Cid = "ipfs://bafyexample/";
    private const string Wallet = "0x1234567890abcdef1234567890ABCDEF12345678";

    private static LoadedCookBook Book()
    {
        var ids = new[] { "a", "b", "c", "d", "e", "f", "g", "h" };
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "Background", LayerKind.Custom, null,
                ids.Select(v => new Variant(v, v.ToUpperInvariant(), 1)).ToList()),
            VariantImages = ids.ToDictionary(v => v,
                v => new Image<Rgba32>(4, 4, new Rgba32((byte)v[0], 2, 3, 255))),
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Chest Demo", new Dimensions(4, 4),
                new Collection("Chest Demo", "Chests of all kinds", "CHST"),
                new Dictionary<string, double> { ["chest"] = 100 }),
            Recipes = new[]
            {
                new LoadedRecipe
                {
                    Manifest = new RecipeManifest("chest", "Chest", new[] { "bg" },
                        Array.Empty<IncompatibilityRule>()),
                    Ingredients = new[] { ing },
                },
            },
        };
    }

    private sealed class GlbModeler : IAssetModeler
    {
        public SetModels Options { get; } = new("glb", "center", 0.5f);
        public IReadOnlyList<string> FileNames(string baseName) => [$"{baseName}.glb"];
        public IReadOnlyList<SetFile>? Model(GeneratedAsset asset, string baseName) =>
            [new SetFile($"{baseName}.glb", [.."glTF-model-payload"u8.ToArray(), (byte)asset.SetNumber])];
    }

    private static string CookedSet(int count = 3, IAssetModeler? models = null)
    {
        var dir = Path.Combine(Directory.CreateTempSubdirectory("nfty-src-").FullName, "ChestDemo");
        using var book = Book();
        using var set = Generator.Generate(book, new GenerateOptions(count, "launch"));
        SetWriter.Write(set, dir, false, models);
        return dir;
    }

    private static string OutDir() => Directory.CreateTempSubdirectory("nfty-out-").FullName;

    private static string Export(string set, ExportOptions options) =>
        SetExporter.Export(set, OutDir(), options with { Shape = ExportShape.Folder }).Path;

    private static List<string> Files(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(dir, f).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(f => f, StringComparer.Ordinal).ToList();

    private static JsonElement Read(string root, string rel)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, rel)));
        return doc.RootElement.Clone();
    }

    [Fact]
    public void The_default_export_is_byte_identical_to_the_cooked_set()
    {
        string set = CookedSet();
        string export = Export(set, new ExportOptions());

        foreach (string rel in Files(set).Where(f => !f.StartsWith("models/", StringComparison.Ordinal)))
            Assert.Equal(File.ReadAllBytes(Path.Combine(set, rel)), File.ReadAllBytes(Path.Combine(export, rel)));
        Assert.Equal(Files(set), Files(export));
    }

    [Fact]
    public void Padded_names_with_start_one_are_what_nfty_always_wrote_even_with_a_base_uri()
    {
        string export = Export(CookedSet(), new ExportOptions { BaseUri = Cid });

        Assert.Contains("metadata/0001.json", Files(export));
        Assert.Contains("images/0001.png", Files(export));
        Assert.Equal(Cid + "images/0001.png", Read(export, "metadata/0001.json").GetProperty("image").GetString());
        Assert.Equal("Chest Demo #1", Read(export, "metadata/0001.json").GetProperty("name").GetString());
    }

    [Fact]
    public void An_unset_base_uri_keeps_the_relative_path_and_the_plan_says_to_set_one()
    {
        string set = CookedSet();
        var plan = SetExporter.Plan(set, new ExportOptions());
        Assert.Contains(plan.Warnings, w => w.Contains("relative", StringComparison.Ordinal) && w.Contains("base URI", StringComparison.Ordinal));

        string export = Export(set, new ExportOptions());
        Assert.Equal("images/0001.png", Read(export, "metadata/0001.json").GetProperty("image").GetString());

        var withBase = SetExporter.Plan(set, new ExportOptions { BaseUri = Cid });
        Assert.DoesNotContain(withBase.Warnings, w => w.Contains("relative", StringComparison.Ordinal));
    }

    [Fact]
    public void Token_id_naming_starting_at_one_names_everything_by_the_unpadded_id()
    {
        string set = CookedSet();
        string export = Export(set, new ExportOptions
        {
            TokenNaming = TokenNaming.TokenId,
            BaseUri = Cid,
            NftyMetadata = true,
        });

        var files = Files(export);
        Assert.Contains("metadata/1", files);
        Assert.Contains("metadata/3", files);
        Assert.Contains("images/1.png", files);
        Assert.DoesNotContain("metadata/0001.json", files);
        // nfty/ keeps the Set's own names and numbering.
        Assert.Contains("nfty/0001.json", files);

        var item = Read(export, "metadata/2");
        Assert.Equal("Chest Demo #2", item.GetProperty("name").GetString());
        Assert.Equal(Cid + "images/2.png", item.GetProperty("image").GetString());
        // The rewritten file still holds the same traits.
        Assert.Equal(
            Read(set, "metadata/0002.json").GetProperty("attributes").GetRawText(),
            item.GetProperty("attributes").GetRawText());
    }

    [Fact]
    public void Token_id_naming_starting_at_zero_shifts_every_name_and_number()
    {
        string export = Export(CookedSet(), new ExportOptions
        {
            TokenNaming = TokenNaming.TokenId,
            TokenStart = 0,
            BaseUri = Cid,
        });

        var files = Files(export);
        Assert.Contains("metadata/0", files);
        Assert.Contains("metadata/2", files);
        Assert.DoesNotContain("metadata/3", files);
        Assert.Contains("images/0.png", files);
        Assert.Equal("Chest Demo #0", Read(export, "metadata/0").GetProperty("name").GetString());
        Assert.Equal(Cid + "images/0.png", Read(export, "metadata/0").GetProperty("image").GetString());
    }

    [Fact]
    public void Padded_names_can_start_at_zero_too()
    {
        string export = Export(CookedSet(), new ExportOptions { TokenStart = 0 });

        var files = Files(export);
        Assert.Contains("metadata/0000.json", files);
        Assert.Contains("images/0000.png", files);
        Assert.Equal("images/0000.png", Read(export, "metadata/0000.json").GetProperty("image").GetString());
        Assert.Equal("Chest Demo #0", Read(export, "metadata/0000.json").GetProperty("name").GetString());
    }

    [Fact]
    public void The_debug_stamp_stays_the_set_number_when_files_are_renamed()
    {
        string set = CookedSet();
        string plain = Export(set, new ExportOptions { NumberWatermark = true });
        string renamed = Export(set, new ExportOptions
        {
            NumberWatermark = true,
            TokenNaming = TokenNaming.TokenId,
            TokenStart = 0,
        });

        Assert.Equal(
            File.ReadAllBytes(Path.Combine(plain, "images/0001.png")),
            File.ReadAllBytes(Path.Combine(renamed, "images/0.png")));
    }

    [Fact]
    public void Collection_json_carries_the_contract_fields()
    {
        string export = Export(CookedSet(), new ExportOptions
        {
            IncludeCollectionMetadata = true,
            BaseUri = Cid,
            ExternalLink = "https://chests.example/",
            RoyaltyPercent = 2.5,
            FeeRecipient = Wallet,
        });

        var c = Read(export, "collection.json");
        Assert.Equal("Chest Demo", c.GetProperty("name").GetString());
        Assert.Equal("Chests of all kinds", c.GetProperty("description").GetString());
        Assert.Equal(Cid + "images/0001.png", c.GetProperty("image").GetString());
        Assert.Equal("https://chests.example/", c.GetProperty("external_link").GetString());
        Assert.Equal(250, c.GetProperty("seller_fee_basis_points").GetInt32());
        Assert.Equal(Wallet, c.GetProperty("fee_recipient").GetString());
        Assert.Equal("CHST", c.GetProperty("symbol").GetString());
    }

    [Fact]
    public void Collection_json_uses_token_ones_image_under_token_id_naming_and_leaves_unset_fields_out()
    {
        string export = Export(CookedSet(), new ExportOptions
        {
            IncludeCollectionMetadata = true,
            BaseUri = Cid,
            TokenNaming = TokenNaming.TokenId,
            TokenStart = 0,
        });

        var c = Read(export, "collection.json");
        Assert.Equal(Cid + "images/0.png", c.GetProperty("image").GetString());
        Assert.False(c.TryGetProperty("external_link", out _));
        Assert.False(c.TryGetProperty("seller_fee_basis_points", out _));
        Assert.False(c.TryGetProperty("fee_recipient", out _));
    }

    [Fact]
    public void An_explicit_collection_image_wins_and_no_image_at_all_warns()
    {
        string set = CookedSet();
        string export = Export(set, new ExportOptions
        {
            IncludeCollectionMetadata = true,
            BaseUri = Cid,
            CollectionImage = "https://x.example/banner.png",
        });
        Assert.Equal("https://x.example/banner.png", Read(export, "collection.json").GetProperty("image").GetString());

        var bare = SetExporter.Plan(set, new ExportOptions { IncludeCollectionMetadata = true });
        Assert.Contains(bare.Warnings, w => w.Contains("collection.json has no image", StringComparison.Ordinal));
        string noImage = Export(set, new ExportOptions { IncludeCollectionMetadata = true });
        Assert.False(Read(noImage, "collection.json").TryGetProperty("image", out _));
    }

    [Fact]
    public void Collection_json_is_listed_by_the_plan_with_its_real_size()
    {
        var plan = SetExporter.Plan(CookedSet(), new ExportOptions { IncludeCollectionMetadata = true });
        var entry = Assert.Single(plan.Entries, e => e.Name == "collection.json");
        Assert.True(entry.Bytes > 0);
        Assert.Contains("collection.json", plan.Parts());

        var result = SetExporter.Export(CookedSet(), OutDir(),
            new ExportOptions { IncludeCollectionMetadata = true, Shape = ExportShape.Folder });
        Assert.Equal(entry.Bytes, new FileInfo(Path.Combine(result.Path, "collection.json")).Length);
    }

    [Fact]
    public void External_url_is_the_base_plus_the_token_id()
    {
        string export = Export(CookedSet(), new ExportOptions
        {
            ExternalUrlBase = "https://chests.example/token/",
            TokenStart = 0,
        });

        Assert.Equal("https://chests.example/token/1",
            Read(export, "metadata/0001.json").GetProperty("external_url").GetString());
        Assert.False(Read(export, "metadata/0000.json").TryGetProperty("animation_url", out _));
    }

    [Fact]
    public void Animation_url_links_a_glb_model_under_the_base_and_follows_the_naming()
    {
        string set = CookedSet(2, new GlbModeler());
        string export = Export(set, new ExportOptions
        {
            BaseUri = Cid,
            Models = true,
            TokenNaming = TokenNaming.TokenId,
        });

        Assert.Contains("models/1.glb", Files(export));
        Assert.Equal(Cid + "models/1.glb", Read(export, "metadata/1").GetProperty("animation_url").GetString());
        Assert.Equal(Cid + "models/2.glb", Read(export, "metadata/2").GetProperty("animation_url").GetString());
    }

    [Fact]
    public void Animation_url_is_skipped_without_a_base_or_without_the_models_in_the_export()
    {
        string set = CookedSet(2, new GlbModeler());

        string noBase = Export(set, new ExportOptions { Models = true, TokenStart = 0 });
        Assert.False(Read(noBase, "metadata/0000.json").TryGetProperty("animation_url", out _));

        string noModels = Export(set, new ExportOptions { BaseUri = Cid, Models = false });
        Assert.False(Read(noModels, "metadata/0001.json").TryGetProperty("animation_url", out _));
        Assert.DoesNotContain(Files(noModels), f => f.StartsWith("models/", StringComparison.Ordinal));
    }

    [Fact]
    public void The_set_manifest_can_be_left_out_and_nothing_else_changes()
    {
        string set = CookedSet();
        string export = Export(set, new ExportOptions { IncludeSetManifest = false });

        Assert.DoesNotContain("set.json", Files(export));
        Assert.Equal(Files(set).Where(f => f != "set.json").ToList(), Files(export));
        Assert.True(File.Exists(Path.Combine(set, "set.json")));
        Assert.Contains("set.json", SetExporter.Plan(set, new ExportOptions()).Entries.Select(e => e.Name));
    }

    [Fact]
    public void A_sealed_export_must_keep_the_set_manifest()
    {
        var ex = Assert.Throws<ArgumentException>(() => SetExporter.Plan(CookedSet(),
            new ExportOptions { Sealed = true, IncludeSetManifest = false }));
        Assert.Contains("set.json", ex.Message);
    }

    [Fact]
    public void The_marketplace_preset_is_collection_json_without_a_set_manifest_and_stays_a_starting_point()
    {
        var market = ExportOptions.For(ExportPreset.Marketplace);
        Assert.True(market.IncludeCollectionMetadata);
        Assert.False(market.IncludeSetManifest);
        Assert.False(new ExportOptions().IncludeCollectionMetadata);
        Assert.True(new ExportOptions().IncludeSetManifest);

        var filled = market with
        {
            BaseUri = Cid,
            TokenNaming = TokenNaming.TokenId,
            TokenStart = 0,
            ExternalUrlBase = "https://x.example/",
            ExternalLink = "https://x.example/",
            RoyaltyPercent = 5,
            FeeRecipient = Wallet,
            CollectionImage = "ipfs://x/y.png",
        };
        Assert.Equal(ExportPreset.Marketplace, filled.MatchingPreset());
        Assert.Null((filled with { IncludeSetManifest = true }).MatchingPreset());
    }

    [Fact]
    public void Metadata_without_images_warns_that_the_links_dangle()
    {
        var plan = SetExporter.Plan(CookedSet(), new ExportOptions { Images = false, BaseUri = Cid });
        Assert.Contains(plan.Warnings, w => w.Contains("dangle", StringComparison.Ordinal));

        var fine = SetExporter.Plan(CookedSet(), new ExportOptions { BaseUri = Cid });
        Assert.Empty(fine.Warnings);
    }

    [Fact]
    public void Rewriting_changes_only_the_export_never_the_cooked_set()
    {
        string set = CookedSet();
        var before = Files(set).ToDictionary(f => f, f => File.ReadAllBytes(Path.Combine(set, f)));

        Export(set, new ExportOptions
        {
            BaseUri = Cid,
            TokenNaming = TokenNaming.TokenId,
            IncludeCollectionMetadata = true,
            ExternalUrlBase = "https://x.example/",
        });

        Assert.Equal(before.Keys.OrderBy(k => k, StringComparer.Ordinal), Files(set));
        foreach (var (f, bytes) in before)
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(set, f)));
    }

    [Theory]
    [InlineData("bafy/", "Base URI")]                          // not absolute
    [InlineData("ftp://host/", "scheme ipfs, https, http or ar")]
    [InlineData("ipfs://CID", "must end with '/'")]
    [InlineData("https://host/dir", "must end with '/'")]
    [InlineData("ipfs://CID /", "Base URI")]
    public void A_bad_base_uri_is_refused_naming_the_rule(string value, string expected)
    {
        var ex = Assert.Throws<ArgumentException>(() => SetExporter.Plan(CookedSet(),
            new ExportOptions { BaseUri = value }));
        Assert.Contains(expected, ex.Message);
        Assert.NotNull(MarketplaceRules.BaseUriProblem(value));
    }

    [Theory]
    [InlineData("ipfs://CID/")]
    [InlineData("https://cdn.example/nfts/")]
    [InlineData("http://localhost:8080/")]
    [InlineData("ar://TxId/")]
    [InlineData("")]
    public void Good_base_uris_pass(string value) => Assert.Null(MarketplaceRules.BaseUriProblem(value));

    [Fact]
    public void Royalty_and_recipient_values_are_validated()
    {
        Assert.NotNull(MarketplaceRules.RoyaltyProblem(100.5));
        Assert.NotNull(MarketplaceRules.RoyaltyProblem(-1));
        Assert.NotNull(MarketplaceRules.RoyaltyProblem(2.555));
        Assert.Null(MarketplaceRules.RoyaltyProblem(2.55));
        Assert.Null(MarketplaceRules.RoyaltyProblem(100));
        Assert.Equal(255, MarketplaceRules.BasisPoints(2.55));
        Assert.Equal(1000, MarketplaceRules.BasisPoints(10));

        Assert.NotNull(MarketplaceRules.FeeRecipientProblem("0x123"));
        Assert.NotNull(MarketplaceRules.FeeRecipientProblem("1234567890abcdef1234567890ABCDEF12345678"));
        Assert.Null(MarketplaceRules.FeeRecipientProblem(Wallet));
        Assert.NotNull(MarketplaceRules.TokenStartProblem(2));

        Assert.Null(MarketplaceRules.TryParseRoyalty("2.5", out var pct));
        Assert.Equal(2.5, pct);
        Assert.NotNull(MarketplaceRules.TryParseRoyalty("abc", out _));
        Assert.Null(MarketplaceRules.TryParseRoyalty("", out var none));
        Assert.Null(none);
    }

    [Fact]
    public void The_exporter_refuses_the_same_bad_values_with_the_same_words()
    {
        string set = CookedSet();
        foreach (var bad in new[]
        {
            new ExportOptions { RoyaltyPercent = 150, FeeRecipient = Wallet },
            new ExportOptions { FeeRecipient = "nope" },
            new ExportOptions { ExternalLink = "chests.example" },
            new ExportOptions { ExternalUrlBase = "ipfs://x/" },
            new ExportOptions { CollectionImage = "image.png" },
            new ExportOptions { TokenStart = 7 },
            new ExportOptions { RoyaltyPercent = 5 },   // a royalty needs a recipient
        })
            Assert.Throws<ArgumentException>(() => SetExporter.Plan(set, bad));
    }

    [Fact]
    public void Token_words_round_trip_and_unknown_words_are_not_guessed()
    {
        foreach (var n in Enum.GetValues<TokenNaming>())
        {
            Assert.True(TokenNamingTokens.TryParse(TokenNamingTokens.Spell(n), out var back));
            Assert.Equal(n, back);
        }
        Assert.False(TokenNamingTokens.TryParse("Padded", out _));
        Assert.False(TokenNamingTokens.TryParse("id", out _));
    }

    [Fact]
    public void A_packed_export_carries_the_same_renamed_files()
    {
        var result = SetExporter.Export(CookedSet(), OutDir(), new ExportOptions
        {
            TokenNaming = TokenNaming.TokenId,
            BaseUri = Cid,
            IncludeCollectionMetadata = true,
        });

        using var zip = System.IO.Compression.ZipFile.OpenRead(result.Path);
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("metadata/1", names);
        Assert.Contains("images/1.png", names);
        Assert.Contains("collection.json", names);
        using var reader = new StreamReader(zip.GetEntry("metadata/1")!.Open());
        Assert.Contains(Cid + "images/1.png", reader.ReadToEnd());
    }
}
