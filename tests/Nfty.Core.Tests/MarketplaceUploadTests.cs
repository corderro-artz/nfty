using System.Text.Json;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using Nfty.Core.Publish;
using Nfty.Core.Publish.Ipfs;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>
/// The whole upload against a fake pinning service: the art goes first, the metadata names it, and
/// the local export carries byte-for-byte what was pinned.
/// </summary>
public class MarketplaceUploadTests
{
    /// <summary>Records every folder it was handed, file by file, and answers a CID per call.</summary>
    private sealed class FakePinning(bool reachable = true) : IPinningService
    {
        public List<(string Label, Dictionary<string, byte[]> Files)> Pins { get; } = new();
        public List<string> Removed { get; } = new();
        /// <summary>When set, the pin with this 1-based number is canceled by this source instead.</summary>
        public (int At, CancellationTokenSource Cts)? CancelAt { get; set; }

        public Task<bool> RemoveAsync(string cid, CancellationToken cancellationToken = default)
        {
            Removed.Add(cid);
            return Task.FromResult(true);
        }
        public string Provider => "Fake";

        public Task<PinningStatus> StatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PinningStatus(reachable, Provider, reachable ? "connected" : "not reachable",
                reachable ? "ok" : "The fake node is down."));

        public Task<string> AddFolderAsync(string folder, string label, IProgress<PinProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CancelAt is { } c && Pins.Count + 1 == c.At)
            {
                c.Cts.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }
            var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .ToDictionary(f => Path.GetRelativePath(folder, f).Replace('\\', '/'), File.ReadAllBytes,
                    StringComparer.Ordinal);
            Pins.Add((label, files));
            return Task.FromResult($"bafycid{Pins.Count}aaaaaaaaaaaaaaaa");
        }
    }

    private static LoadedCookBook Book()
    {
        var ids = new[] { "a", "b", "c", "d" };
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "Background", LayerKind.Custom, null,
                ids.Select(v => new Variant(v, v.ToUpperInvariant(), 1)).ToList()),
            VariantImages = ids.ToDictionary(v => v, v => new Image<Rgba32>(4, 4, new Rgba32((byte)v[0], 2, 3, 255))),
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Chest Demo", new Dimensions(4, 4),
                new Collection("Chest Demo", "Chests", "CHST"), new Dictionary<string, double> { ["chest"] = 100 }),
            Recipes = new[]
            {
                new LoadedRecipe
                {
                    Manifest = new RecipeManifest("chest", "Chest", new[] { "bg" }, Array.Empty<IncompatibilityRule>()),
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
            [new SetFile($"{baseName}.glb", [.."glTF-model"u8.ToArray(), (byte)asset.SetNumber])];
    }

    private static string CookedSet(IAssetModeler? models = null)
    {
        var dir = Path.Combine(Directory.CreateTempSubdirectory("nfty-up-src-").FullName, "ChestDemo");
        using var book = Book();
        using var set = Generator.Generate(book, new GenerateOptions(3, "launch"));
        SetWriter.Write(set, dir, false, models);
        return dir;
    }

    [Fact]
    public async Task Models_are_pinned_between_the_art_and_the_metadata_and_linked()
    {
        var pin = new FakePinning();
        var result = await MarketplaceUpload.RunAsync(CookedSet(new GlbModeler()),
            Directory.CreateTempSubdirectory("nfty-up-out-").FullName, Drop() with { Models = true }, pin);

        Assert.Equal(3, pin.Pins.Count);
        Assert.Equal(new[] { "1.glb", "2.glb", "3.glb" }, pin.Pins[1].Files.Keys.Order(StringComparer.Ordinal));
        Assert.NotNull(result.ModelCid);
        using var doc = JsonDocument.Parse(pin.Pins[2].Files["2"]);
        Assert.Equal($"ipfs://{result.ModelCid}/2.glb", doc.RootElement.GetProperty("animation_url").GetString());
    }

    private static ExportOptions Drop() => ExportOptions.For(ExportPreset.Marketplace) with
    {
        Shape = ExportShape.Folder,
        TokenNaming = TokenNaming.TokenId,
    };

    [Fact]
    public async Task The_art_is_pinned_first_and_the_metadata_names_it()
    {
        var pin = new FakePinning();
        var outDir = Directory.CreateTempSubdirectory("nfty-up-out-").FullName;

        var result = await MarketplaceUpload.RunAsync(CookedSet(), outDir, Drop(), pin);

        Assert.Equal(2, pin.Pins.Count);
        var (imagesLabel, images) = pin.Pins[0];
        Assert.EndsWith("-images", imagesLabel, StringComparison.Ordinal);
        Assert.Equal(new[] { "1.png", "2.png", "3.png" }, images.Keys.Order(StringComparer.Ordinal));

        var (_, meta) = pin.Pins[1];
        Assert.Contains("1", meta.Keys);
        Assert.Contains("collection.json", meta.Keys);
        using var doc = JsonDocument.Parse(meta["1"]);
        Assert.Equal($"ipfs://{result.ImageCid}/1.png", doc.RootElement.GetProperty("image").GetString());

        Assert.Equal($"ipfs://{result.MetadataCid}/", result.BaseUri);
        Assert.Equal($"ipfs://{result.MetadataCid}/collection.json", result.ContractUri);
        Assert.Null(result.ModelCid);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task The_local_export_is_byte_for_byte_what_was_pinned()
    {
        var pin = new FakePinning();
        var outDir = Directory.CreateTempSubdirectory("nfty-up-out-").FullName;

        var result = await MarketplaceUpload.RunAsync(CookedSet(), outDir, Drop(), pin);

        string local = result.Local.Path;
        foreach (var (name, bytes) in pin.Pins[0].Files)
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(local, "images", name)));
        foreach (var (name, bytes) in pin.Pins[1].Files)
        {
            string path = name == "collection.json"
                ? Path.Combine(local, name)
                : Path.Combine(local, "metadata", name);
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
    }

    [Fact]
    public async Task An_unreachable_service_is_refused_before_anything_is_written()
    {
        var pin = new FakePinning(reachable: false);
        var outDir = Directory.CreateTempSubdirectory("nfty-up-out-").FullName;

        var ex = await Assert.ThrowsAsync<PinningException>(() =>
            MarketplaceUpload.RunAsync(CookedSet(), outDir, Drop(), pin));

        Assert.Equal("The fake node is down.", ex.Message);
        Assert.Empty(pin.Pins);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outDir));
    }

    [Fact]
    public async Task Padded_names_are_uploaded_with_a_warning_that_a_contract_will_not_find_them()
    {
        var pin = new FakePinning();
        var result = await MarketplaceUpload.RunAsync(CookedSet(),
            Directory.CreateTempSubdirectory("nfty-up-out-").FullName,
            Drop() with { TokenNaming = TokenNaming.Padded }, pin);
        Assert.Contains(result.Warnings, w => w.Contains("padded", StringComparison.Ordinal));
    }

    [Fact]
    public void What_cannot_be_uploaded_is_named()
    {
        Assert.Null(MarketplaceUpload.Problem(Drop()));
        Assert.NotNull(MarketplaceUpload.Problem(Drop() with { Sealed = true }));
        Assert.NotNull(MarketplaceUpload.Problem(Drop() with { Images = false }));
        Assert.NotNull(MarketplaceUpload.Problem(Drop() with { OpenSeaMetadata = false }));
        Assert.NotNull(MarketplaceUpload.Problem(Drop() with { NumberWatermark = true }));
    }

    [Fact]
    public async Task A_cancel_stops_the_run()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            MarketplaceUpload.RunAsync(CookedSet(), Directory.CreateTempSubdirectory("nfty-up-out-").FullName,
                Drop(), new FakePinning(), cancellationToken: cts.Token));
    }

    [Fact]
    public async Task A_cancel_mid_upload_unpins_what_was_already_pinned()
    {
        using var cts = new CancellationTokenSource();
        var pinning = new FakePinning { CancelAt = (2, cts) };   // the art pins, then the next pin is stopped
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            MarketplaceUpload.RunAsync(CookedSet(), Directory.CreateTempSubdirectory("nfty-up-out-").FullName,
                Drop(), pinning, cancellationToken: cts.Token));

        Assert.Single(pinning.Pins);
        Assert.Equal(["bafycid1aaaaaaaaaaaaaaaa"], pinning.Removed);
    }

    [Fact]
    public async Task A_finished_upload_unpins_nothing()
    {
        var pinning = new FakePinning();
        await MarketplaceUpload.RunAsync(CookedSet(), Directory.CreateTempSubdirectory("nfty-up-out-").FullName,
            Drop(), pinning);
        Assert.Empty(pinning.Removed);
    }
}
