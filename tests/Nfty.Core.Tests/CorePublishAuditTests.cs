using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using Nfty.Core.Publish;
using Nfty.Core.Publish.Ipfs;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

public class CorePublishAuditTests
{
    private static string Temp() => Directory.CreateTempSubdirectory("nfty-publish-audit-").FullName;
    private static GeneratedSet Batch(string name = "Collection", int number = 1, bool absent = false) =>
        new(name, "description", "SYM", "seed", [new GeneratedAsset
        {
            SetNumber = number, Dna = "dna" + number, RecipeId = "r", RecipeName = "Recipe",
            Image = new Image<Rgba32>(2, 2), Traits = [], ColorRolls = [],
            AbsentLayers = absent ? [new AbsentLayer("hat", "Hat")] : [],
        }]);
    private static string Source(string name = "Collection")
    {
        string dir = Path.Combine(Temp(), "source");
        using var set = Batch(name);
        SetWriter.Write(set, dir, false);
        return dir;
    }
    private static ExportOptions Folder => new() { Shape = ExportShape.Folder };

    [Fact]
    public void Reexport_refuses_nonempty_destination_without_removing_private_or_user_files()
    {
        string source = Source(), output = Temp();
        var first = SetExporter.Export(source, output, Folder);
        string userFile = Path.Combine(first.Path, "notes.txt");
        File.WriteAllText(userFile, "mine");
        var before = Directory.GetFiles(first.Path, "*", SearchOption.AllDirectories)
            .ToDictionary(f => f, File.ReadAllBytes);
        Assert.Throws<IOException>(() => SetExporter.Export(source, output,
            ExportOptions.For(ExportPreset.Marketplace) with { Shape = ExportShape.Folder }));
        foreach (var (file, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(file));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("CON")]
    [InlineData("trailing. ")]
    public void Export_name_stays_an_immediate_usable_child(string name)
    {
        string output = Temp();
        var result = SetExporter.Export(Source(name), output, Folder);
        Assert.Equal(Path.GetFullPath(output), Path.GetDirectoryName(result.Path));
        Assert.True(File.Exists(Path.Combine(result.Path, "set.json")));
        Assert.NotEqual(".", Path.GetFileName(result.Path));
        Assert.NotEqual("..", Path.GetFileName(result.Path));
        Assert.False(Path.GetFileName(result.Path).EndsWith('.'));
    }

    [Theory]
    [InlineData(ExportShape.Folder)]
    [InlineData(ExportShape.Archive)]
    public void Additional_numbered_files_retain_paths_and_bytes(ExportShape shape)
    {
        string source = Source();
        Directory.CreateDirectory(Path.Combine(source, "images", "hi-res"));
        File.WriteAllText(Path.Combine(source, "images", "hi-res", "0001.webp"), "alternate");
        File.WriteAllText(Path.Combine(source, "images", "0001.webp"), "same-directory-alternate");
        var result = SetExporter.Export(source, Temp(), Folder with { Shape = shape, TokenNaming = TokenNaming.TokenId });
        if (shape == ExportShape.Folder)
        {
            Assert.Equal("alternate", File.ReadAllText(Path.Combine(result.Path, "images", "hi-res", "0001.webp")));
            Assert.Equal("same-directory-alternate", File.ReadAllText(Path.Combine(result.Path, "images", "0001.webp")));
            Assert.Equal(File.ReadAllBytes(Path.Combine(source, "images", "0001.png")), File.ReadAllBytes(Path.Combine(result.Path, "images", "1.png")));
        }
        else
        {
            using var zip = ZipFile.OpenRead(result.Path);
            Assert.NotNull(zip.GetEntry("images/hi-res/0001.webp"));
            Assert.NotNull(zip.GetEntry("images/0001.webp"));
            using var stream = zip.GetEntry("images/1.png")!.Open();
            using var bytes = new MemoryStream(); stream.CopyTo(bytes);
            Assert.Equal(File.ReadAllBytes(Path.Combine(source, "images", "0001.png")), bytes.ToArray());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_pack_restores_manifest_and_invalidates_index(bool async)
    {
        string source = Source();
        byte[] manifest = File.ReadAllBytes(Path.Combine(source, "set.json"));
        Directory.CreateDirectory(Path.Combine(source, "source.set"));
        using var next = Batch(number: 2);
        var error = async ? await Record.ExceptionAsync(() => SetWriter.WriteAsync(next, source, true))
            : Record.Exception(() => SetWriter.Write(next, source, true));
        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.Equal(manifest, File.ReadAllBytes(Path.Combine(source, "set.json")));
        Assert.False(File.Exists(Path.Combine(source, "nfty", "0002.json")));
        Assert.False(File.Exists(Path.Combine(source, "set-index.txt")));
        Assert.Equal(2, SetWriter.ReadExisting(source).NextNumber);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Wrong_identity_number_kind_is_corruption_with_filename(bool async)
    {
        string source = Source();
        string item = Path.Combine(source, "nfty", "0001.json");
        File.WriteAllText(item, "{\"dna\":\"x\",\"setNumber\":\"oops\"}");
        var error = async
            ? await Assert.ThrowsAsync<CorruptSetException>(() => SetWriter.ReadExistingAsync(source))
            : Assert.Throws<CorruptSetException>(() => SetWriter.ReadExisting(source));
        Assert.Contains(item, error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task True_shape_one_absence_survives_extend_without_changing_published_metadata(bool async)
    {
        string source = Source();
        string manifestPath = Path.Combine(source, "set.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        manifest.Remove("metadataShape");
        File.WriteAllText(manifestPath, manifest.ToJsonString(Json.Options));
        string itemPath = Path.Combine(source, "nfty", "0001.json");
        var item = JsonNode.Parse(File.ReadAllText(itemPath))!.AsObject();
        item["absentLayers"] = new JsonArray("Hat");
        item["rarity"]!.AsArray().Add(JsonSerializer.SerializeToNode(new RarityAttribute("Hat", "(none)", 100), Json.Options));
        File.WriteAllText(itemPath, item.ToJsonString(Json.Options));
        string publishedPath = Path.Combine(source, "metadata", "0001.json");
        byte[] published = File.ReadAllBytes(publishedPath);
        using var next = Batch(number: 2, absent: true);
        if (async) await SetWriter.WriteAsync(next, source, false);
        else SetWriter.Write(next, source, false);
        Assert.Equal(published, File.ReadAllBytes(publishedPath));
        var rich = JsonSerializer.Deserialize<NftyMetadata>(File.ReadAllText(itemPath), Json.Options)!;
        Assert.Contains(rich.Rarity, r => r.Trait_type == "Hat" && r.Value == "(none)" && r.RarityPct == 100);
        var combined = JsonSerializer.Deserialize<SetManifest>(File.ReadAllText(manifestPath), Json.Options)!;
        Assert.Contains(combined.Rarity, r => r.Trait_type == "Hat" && r.Value == "(none)" && r.RarityPct == 100);
    }

    [Fact]
    public void Delimiter_header_collision_cannot_relabel_a_new_seal()
    {
        string path = Path.Combine(Temp(), "new.tin");
        using var payload = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        Seal.Write(path, payload, "A|1", 2, "C", SealPolicy.ViewOnly,
            "correct-horse-battery-staple", 1000);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            JsonObject header;
            using (var input = zip.GetEntry(Seal.ManifestEntry)!.Open())
                header = JsonNode.Parse(input)!.AsObject();
            header["collection"] = "A"; header["count"] = 1; header["note"] = "2|C";
            zip.GetEntry(Seal.ManifestEntry)!.Delete();
            using var output = zip.CreateEntry(Seal.ManifestEntry).Open();
            JsonSerializer.Serialize(output, header, Json.Options);
        }
        using var opened = new MemoryStream();
        Assert.Throws<SealedSetException>(() => Seal.ExtractTo(path, "correct-horse-battery-staple", opened));
        Assert.Empty(opened.ToArray());
    }

    [Fact]
    public void Seal_written_by_reviewed_build_with_legacy_header_remains_readable()
    {
        // Golden produced by reviewed commit 95eb1e8, not by the new canonical encoder.
        const string original = "UEsDBBQAAAAAAGNpZV1QSw9/FAAAABQAAAALAAAAcGF5bG9hZC5iaW6AcMlZL00/b0GPu+4qn4n1x0N2JFBLAwQUAAAACABjaWVdPhkyq0sBAABDAgAACQAAAHNlYWwuanNvbnWRQW/bMAyF7wX6HwSdG8CiJVrOscAOAzZgp90pioqNKpZhe1iDov99UNIEyWG68b1H8aP08fyklF6F8veo90qnxkRvewC24Bts2oAmCRA7ZAxt0i/nBi45C29jmWrTDzkQn27Wn2nTe2Uu5VQ2qZmSoxqEoixfubnkkU96rz5qqZSmnMvfb+9zWWp7orzKy4MVx5tR9c/7e34S1yneOWqdawETBAnUd6G34AwGjJawbZ2NIfaYUmh8jATAHbKPLXXIBrsvtreYHsAOZRm34VgnzOEtJtitA4HDS1wpPW6yUH2OVe8VNvVcrZVyxdYmEBjfNwjWtEIeG0HPtrfGQOCe9d1OPM6DLP9DIFl34HB34OMNIC10lNfTJhXANNa7Dh+8s35V5kzjdE3bqzqVieXXIml8PwN7nxxAcw+28iBH+i3Levl6eH76/AdQSwECFAAUAAAAAABjaWVdUEsPfxQAAAAUAAAACwAAAAAAAAAAAAAAAAAAAAAAcGF5bG9hZC5iaW5QSwECFAAUAAAACABjaWVdPhkyq0sBAABDAgAACQAAAAAAAAAAAAAAAAA9AAAAc2VhbC5qc29uUEsFBgAAAAACAAIAcAAAAK8BAAAAAA==";
        string path = Path.Combine(Temp(), "legacy.tin");
        File.WriteAllBytes(path, Convert.FromBase64String(original));
        using var opened = new MemoryStream();
        var header = Seal.ExtractTo(path, "correct-horse-battery-staple", opened);
        Assert.Equal("Legacy", header.Collection);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, opened.ToArray());
        Assert.StartsWith("nfty-seal/1|", header.Canonical());
    }

    private sealed class UnknownOwnershipPinning : IPinningService
    {
        public string Provider => "third party";
        public List<string> Removed { get; } = [];
        private int _calls;
        public Task<PinningStatus> StatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PinningStatus(true, Provider, "ok", "ok"));
        public Task<string> AddFolderAsync(string folder, string label, IProgress<PinProgress>? progress = null,
            CancellationToken cancellationToken = default) => ++_calls == 1
                ? Task.FromResult("bafycid1aaaaaaaaaaaaaaaa") : throw new PinningException("metadata failed");
        public Task<bool> RemoveAsync(string cid, CancellationToken cancellationToken = default)
        { Removed.Add(cid); return Task.FromResult(true); }
    }

    [Fact]
    public async Task Unknown_pin_ownership_is_never_assumed_new_during_rollback()
    {
        var pinning = new UnknownOwnershipPinning();
        await Assert.ThrowsAsync<PinningException>(() => MarketplaceUpload.RunAsync(Source(), Temp(), Folder, pinning));
        Assert.Empty(pinning.Removed);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Paths.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(reply(request));
        }
    }
    private static HttpResponseMessage Response(string json) => new(System.Net.HttpStatusCode.OK)
    { Content = new StringContent(json) };
    private const string Cid = "bafybeigdyrzt5sfp7udm7hu76uh7y26nf3efuylqabf3oclgtqy55fbzdi";

    [Theory]
    [InlineData("recursive", false)]
    [InlineData("direct", false)]
    [InlineData("indirect", false)]
    [InlineData(null, true)]
    public async Task Kubo_establishes_prior_pin_ownership_before_adding(string? kind, bool acquired)
    {
        var handler = new Handler(request => request.RequestUri!.AbsolutePath.EndsWith("/ls", StringComparison.Ordinal)
            ? Response(kind is null ? "{\"Keys\":{}}" : $"{{\"Keys\":{{\"{Cid}\":{{\"Type\":\"{kind}\"}}}}}}")
            : Response($"{{\"Name\":\"\",\"Hash\":\"{Cid}\"}}"));
        string folder = Temp(); File.WriteAllText(Path.Combine(folder, "1.png"), "art");
        var pin = await new KuboPinning(new HttpClient(handler), "http://localhost:1").AddFolderOwnedAsync(folder, "art");
        Assert.Equal(Cid, pin.Cid); Assert.Equal(acquired, pin.Acquired);
        Assert.Equal("/api/v0/pin/ls?type=all&cid-base=base32", handler.Paths[0]);
        Assert.Contains("pin=true", handler.Paths[1]);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"Keys\":null}")]
    [InlineData("{\"Keys\":[]}")]
    [InlineData("invalid")]
    public async Task Kubo_refuses_to_mutate_when_prior_pin_state_is_unreadable(string response)
    {
        var handler = new Handler(_ => Response(response));
        await Assert.ThrowsAsync<PinningException>(() => new KuboPinning(new HttpClient(handler), "http://localhost:1")
            .AddFolderOwnedAsync(Temp(), "art"));
        Assert.Single(handler.Paths);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Keys\":{}}")]
    public async Task Kubo_empty_node_can_acquire_its_first_pin(string response)
    {
        var handler = new Handler(request => request.RequestUri!.AbsolutePath.EndsWith("/ls", StringComparison.Ordinal)
            ? Response(response) : Response($"{{\"Name\":\"\",\"Hash\":\"{Cid}\"}}"));
        string folder = Temp(); File.WriteAllText(Path.Combine(folder, "1.png"), "art");
        var pin = await new KuboPinning(new HttpClient(handler), "http://localhost:1").AddFolderOwnedAsync(folder, "art");
        Assert.True(pin.Acquired); Assert.Equal(Cid, pin.Cid);
        Assert.Equal(2, handler.Paths.Count);
    }

    [Theory]
    [InlineData(",\"isDuplicate\":false", true)]
    [InlineData(",\"isDuplicate\":true", false)]
    [InlineData("", false)]
    [InlineData(",\"isDuplicate\":null", false)]
    public async Task Pinata_only_claims_explicit_new_pin_evidence(string duplication, bool acquired)
    {
        var handler = new Handler(_ => Response($"{{\"IpfsHash\":\"{Cid}\"{duplication}}}"));
        string folder = Temp(); File.WriteAllText(Path.Combine(folder, "1.png"), "art");
        var pin = await new PinataPinning(new HttpClient(handler), "fake", "http://localhost:1")
            .AddFolderOwnedAsync(folder, "art");
        Assert.Equal(Cid, pin.Cid); Assert.Equal(acquired, pin.Acquired);
    }

    private sealed class OwnedPinning(bool artNew, bool metadataNew, bool failMetadata = false,
        CancellationTokenSource? cancelAfterMetadata = null) : IPinningService
    {
        public string Provider => "owned stub";
        public List<string> Removed { get; } = [];
        private int _calls;
        public Task<PinningStatus> StatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PinningStatus(true, Provider, "ok", "ok"));
        public Task<string> AddFolderAsync(string folder, string label, IProgress<PinProgress>? progress = null,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Use acquisition seam");
        public Task<PinAcquisition> AddFolderOwnedAsync(string folder, string label, IProgress<PinProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (++_calls == 2)
            {
                if (failMetadata) throw new PinningException("metadata failed");
                cancelAfterMetadata?.Cancel();
            }
            return Task.FromResult(new PinAcquisition(Cid, _calls == 1 ? artNew : metadataNew));
        }
        public Task<bool> RemoveAsync(string cid, CancellationToken cancellationToken = default)
        { Removed.Add(cid); return Task.FromResult(true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Metadata_failure_rolls_back_only_new_art_pin(bool artNew)
    {
        var pinning = new OwnedPinning(artNew, false, failMetadata: true);
        await Assert.ThrowsAsync<PinningException>(() => MarketplaceUpload.RunAsync(Source(), Temp(), Folder, pinning));
        Assert.Equal(artNew ? new[] { Cid } : [], pinning.Removed);
    }

    [Fact]
    public async Task Repeated_cid_acquired_in_one_run_is_removed_only_once()
    {
        string output = Temp();
        Directory.CreateDirectory(Path.Combine(output, "Collection"));
        File.WriteAllText(Path.Combine(output, "Collection", "user.txt"), "keep");
        var pinning = new OwnedPinning(true, true);
        await Assert.ThrowsAsync<IOException>(() => MarketplaceUpload.RunAsync(Source(), output, Folder, pinning));
        Assert.Equal(new[] { Cid }, pinning.Removed);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(output, "Collection", "user.txt")));
    }

    [Fact]
    public async Task Cancellation_rolls_back_new_pins_but_keeps_preexisting_pins()
    {
        using var cancel = new CancellationTokenSource();
        var pinning = new OwnedPinning(false, true, cancelAfterMetadata: cancel);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MarketplaceUpload.RunAsync(Source(), Temp(), Folder,
            pinning, cancellationToken: cancel.Token));
        Assert.Equal(new[] { Cid }, pinning.Removed);
    }

    private sealed class ProgressSink(Action<ExportProgress> report) : IProgress<ExportProgress>
    { public void Report(ExportProgress progress) => report(progress); }

    [Fact]
    public async Task Canceled_folder_export_leaves_preexisting_empty_destination_empty()
    {
        string source = Source(), output = Temp();
        string destination = Path.Combine(output, "Collection"); Directory.CreateDirectory(destination);
        using var cancel = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SetExporter.ExportAsync(source, output, Folder,
            progress: new ProgressSink(p => { if (p.Done > 0) cancel.Cancel(); }), cancellationToken: cancel.Token));
        Assert.True(Directory.Exists(destination));
        Assert.Empty(Directory.GetFileSystemEntries(destination));
        Assert.Equal(new[] { destination }, Directory.GetFileSystemEntries(output));
    }

    [Fact]
    public void Renaming_collision_is_refused_before_destination_write()
    {
        string source = Source(), output = Temp();
        File.WriteAllText(Path.Combine(source, "images", "1.png"), "user alternate");
        Assert.Throws<ArgumentException>(() => SetExporter.Export(source, output, Folder with { TokenNaming = TokenNaming.TokenId }));
        Assert.Empty(Directory.GetFileSystemEntries(output));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_pack_preserves_existing_archive_bytes(bool async)
    {
        if (!OperatingSystem.IsWindows()) return; // Windows sharing denies replacement; Unix allows rename of an open file.
        string source = Source();
        using (var first = Batch()) SetWriter.Write(first, source, true);
        string archive = Path.Combine(source, "source.set");
        byte[] original = File.ReadAllBytes(archive);
        using var held = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var next = Batch(number: 2);
        var error = async ? await Record.ExceptionAsync(() => SetWriter.WriteAsync(next, source, true))
            : Record.Exception(() => SetWriter.Write(next, source, true));
        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.Equal(original, File.ReadAllBytes(archive));
        Assert.DoesNotContain(Directory.GetFiles(source), f => Path.GetFileName(f).StartsWith(".nfty-pack-", StringComparison.Ordinal));
        Assert.Equal(2, SetWriter.ReadExisting(source).NextNumber);
    }

    private sealed class AuditModeler : IAssetModeler
    {
        public SetModels Options => new("glb", "center", 0.5f);
        public IReadOnlyList<string> FileNames(string baseName) => [baseName + ".glb"];
        public IReadOnlyList<SetFile>? Model(GeneratedAsset asset, string baseName) =>
            [new SetFile(baseName + ".glb", [1, 2, 3])];
    }

    [Fact]
    public void Model_inclusion_stays_limited_to_recorded_primary_files()
    {
        string source = Path.Combine(Temp(), "source");
        using (var set = Batch()) SetWriter.Write(set, source, false, new AuditModeler());
        Directory.CreateDirectory(Path.Combine(source, "models", "sub"));
        File.WriteAllText(Path.Combine(source, "models", "sub", "0001.glb"), "user source");
        File.WriteAllText(Path.Combine(source, "models", "0001.blend"), "private source");
        var result = SetExporter.Export(source, Temp(), Folder with { TokenNaming = TokenNaming.TokenId });
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(result.Path, "models", "1.glb")));
        Assert.Single(Directory.GetFiles(Path.Combine(result.Path, "models"), "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void Debug_watermark_preserves_additional_numbered_files()
    {
        string source = Source();
        Directory.CreateDirectory(Path.Combine(source, "images", "sub"));
        string alternate = Path.Combine(source, "images", "sub", "0001.png");
        File.WriteAllText(alternate, "extra not a primary asset");
        var result = SetExporter.Export(source, Temp(), Folder with { NumberWatermark = true });
        Assert.Equal(File.ReadAllBytes(alternate), File.ReadAllBytes(Path.Combine(result.Path, "images", "sub", "0001.png")));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Model_preflight_refusal_preserves_index_bytes_and_file_timestamps(bool modeled, bool async)
    {
        string source = Path.Combine(Temp(), "source");
        using (var set = Batch()) SetWriter.Write(set, source, false, modeled ? new AuditModeler() : null);
        var before = Directory.GetFiles(source, "*", SearchOption.AllDirectories)
            .ToDictionary(f => f, f => (Bytes: File.ReadAllBytes(f), Modified: File.GetLastWriteTimeUtc(f)));
        using var next = Batch(number: 2);
        IAssetModeler? incompatible = modeled ? null : new AuditModeler();
        if (async) await Assert.ThrowsAsync<InvalidOperationException>(() => SetWriter.WriteAsync(next, source, false, models: incompatible));
        else Assert.Throws<InvalidOperationException>(() => SetWriter.Write(next, source, false, incompatible));
        Assert.Equal(before.Count, Directory.GetFiles(source, "*", SearchOption.AllDirectories).Length);
        foreach (var (file, original) in before)
        {
            Assert.Equal(original.Bytes, File.ReadAllBytes(file));
            Assert.Equal(original.Modified, File.GetLastWriteTimeUtc(file));
        }
    }
}
