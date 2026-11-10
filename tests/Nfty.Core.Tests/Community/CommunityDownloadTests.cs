using System.Net;
using System.Security.Cryptography;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using Nfty.Core.Publish;
using Nfty.Core.Publish.Community;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.Core.Tests.Community;

public class CommunityDownloadTests : IDisposable
{
    private const string Url = "https://cdn.example/files/chest.set";
    private const string UrlPath = "/files/chest.set";
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("nfty-download-");

    public void Dispose() => _dir.Delete(recursive: true);

    private string Fresh() => _dir.CreateSubdirectory(Guid.NewGuid().ToString("N")).FullName;

    private static LoadedCookBook Book(int side = 4)
    {
        var ids = Enumerable.Range(0, 8).Select(v => $"v{v}").ToList();
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("l0", "Layer 0", LayerKind.Custom, null,
                ids.Select(v => new Variant(v, v.ToUpperInvariant(), 1)).ToList()),
            VariantImages = ids.Select((v, i) => (v, i)).ToDictionary(t => t.v,
                t => new Image<Rgba32>(side, side, new Rgba32((byte)(t.i * 20), 0, 3, 255))),
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Chest Demo", new Dimensions(side, side),
                new Collection("Chest Demo", "d", "CHST"), new Dictionary<string, double> { ["chest"] = 100 }),
            Recipes =
            [
                new LoadedRecipe
                {
                    Manifest = new RecipeManifest("chest", "Chest", ["l0"], Array.Empty<IncompatibilityRule>()),
                    Ingredients = [ing],
                },
            ],
        };
    }

    private byte[] PackedBytes(ExportOptions? options = null, string? pass = null, int count = 2)
    {
        using var book = Book();
        string dir = Path.Combine(Fresh(), "ChestDemo");
        using (var set = Generator.Generate(book, new GenerateOptions(count, "launch")))
            SetWriter.Write(set, dir, pack: false);
        string path = SetExporter.Export(dir, Fresh(), options ?? new ExportOptions(), null, pass).Path;
        return File.ReadAllBytes(path);
    }

    private static string Hex(byte[] b) => Convert.ToHexStringLower(SHA256.HashData(b));

    private static CommunityEntry Entry(byte[] bytes, int assets = 2, int side = 4, string? sha = null, string url = Url,
        long? size = null) => new(1, "chest", new CommunityOwner(7, "ada"), "nfty-public", "chest-1",
        "Chest", "Chest Demo", "", [], new CommunityCanvas(side, side), assets, false, false, null,
        new CommunityFile(url, size ?? bytes.Length, sha ?? Hex(bytes)), new CommunityFile("https://x/p.png", 1, new string('a', 64)),
        new string('b', 64), "1.0", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private static FakeGitHub Serving(byte[] bytes) =>
        new FakeGitHub().OnBytes(HttpMethod.Get, UrlPath, HttpStatusCode.OK, bytes);

    private static string[] Left(string cache) => Directory.GetFiles(cache).Select(Path.GetFileName).ToArray()!;

    [Fact]
    public async Task A_matching_download_lands_in_the_cache()
    {
        var bytes = PackedBytes();
        var entry = Entry(bytes);
        string cache = Path.Combine(Fresh(), "cache");
        var fake = Serving(bytes);
        var seen = new List<double>();

        string path = await CommunityDownload.FetchAsync(fake.Client(), entry, cache, new Progress<double>(seen.Add), default);
        await Task.Delay(50);

        Assert.Equal(Path.Combine(cache, entry.Set.Sha256 + ".set"), path);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal([entry.Set.Sha256 + ".set"], Left(cache));
        Assert.All(fake.Requests, r => Assert.False(r.Headers.ContainsKey("Authorization")));
        Assert.Equal(1.0, seen.Max());
        fake.AssertAllConsumed();
    }

    [Fact]
    public async Task A_hash_mismatch_is_refused_and_leaves_nothing()
    {
        var bytes = PackedBytes();
        var entry = Entry(bytes, sha: new string('c', 64));
        string cache = Fresh();

        var ex = await Assert.ThrowsAsync<CommunityException>(() =>
            CommunityDownload.FetchAsync(Serving(bytes).Client(), entry, cache, null, default));
        Assert.Equal(ShareRules.HashMismatch, ex.Message);
        Assert.Empty(Left(cache));
    }

    [Fact]
    public async Task A_download_cut_short_leaves_nothing_in_the_cache()
    {
        var bytes = PackedBytes();
        var entry = Entry(bytes);
        string cache = Fresh();
        var fake = new FakeGitHub().On(HttpMethod.Get, UrlPath, _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new CutStream(bytes, bytes.Length / 2)),
        });

        var ex = await Assert.ThrowsAsync<CommunityException>(() =>
            CommunityDownload.FetchAsync(fake.Client(), entry, cache, null, default));
        Assert.Equal(ShareRules.Unreachable, ex.Message);
        Assert.Empty(Left(cache));
    }

    [Fact]
    public async Task A_server_sending_more_than_listed_is_cut_off()
    {
        var bytes = PackedBytes();
        var entry = Entry(bytes, size: bytes.Length - 10);
        string cache = Fresh();
        long read = 0;
        // No Content-Length is declared, so only counting the body can catch it.
        var fake = new FakeGitHub().On(HttpMethod.Get, UrlPath, _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new PushContent(bytes, n => read = n),
        });

        var ex = await Assert.ThrowsAsync<CommunityException>(() =>
            CommunityDownload.FetchAsync(fake.Client(), entry, cache, null, default));
        Assert.Equal(ShareRules.HashMismatch, ex.Message);
        Assert.Empty(Left(cache));
    }

    [Fact]
    public async Task A_sealed_set_in_the_index_is_refused()
    {
        var bytes = PackedBytes(ExportOptions.For(ExportPreset.SealedCritique), "correct-horse-battery-staple");
        string cache = Fresh();

        var ex = await Assert.ThrowsAsync<CommunityException>(() =>
            CommunityDownload.FetchAsync(Serving(bytes).Client(), Entry(bytes), cache, null, default));
        Assert.Equal(ShareRules.Sealed, ex.Message);
        Assert.Empty(Left(cache));
    }

    [Fact]
    public async Task A_listing_that_disagrees_with_the_set_is_refused()
    {
        var bytes = PackedBytes();
        string cache = Fresh();

        var count = await Assert.ThrowsAsync<CommunityException>(() =>
            CommunityDownload.FetchAsync(Serving(bytes).Client(), Entry(bytes, assets: 3), cache, null, default));
        Assert.Equal(ShareRules.CountMismatch, count.Message);

        var canvas = await Assert.ThrowsAsync<CommunityException>(() =>
            CommunityDownload.FetchAsync(Serving(bytes).Client(), Entry(bytes, side: 5), cache, null, default));
        Assert.Equal(ShareRules.CanvasMismatch, canvas.Message);
        Assert.Empty(Left(cache));
    }

    [Fact]
    public async Task A_cached_copy_is_reused_without_a_request()
    {
        var bytes = PackedBytes();
        var entry = Entry(bytes);
        string cache = Fresh();
        File.WriteAllBytes(Path.Combine(cache, entry.Set.Sha256 + ".set"), bytes);
        var fake = new FakeGitHub();

        string path = await CommunityDownload.FetchAsync(fake.Client(), entry, cache, null, default);

        Assert.Equal(Path.Combine(cache, entry.Set.Sha256 + ".set"), path);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task A_corrupt_cached_copy_is_replaced()
    {
        var bytes = PackedBytes();
        var entry = Entry(bytes);
        string cache = Fresh();
        string final = Path.Combine(cache, entry.Set.Sha256 + ".set");
        File.WriteAllBytes(final, [1, 2, 3]);

        string path = await CommunityDownload.FetchAsync(Serving(bytes).Client(), entry, cache, null, default);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task A_link_that_is_not_https_or_a_hash_that_is_not_hex_is_refused_before_any_request()
    {
        var bytes = PackedBytes();
        string cache = Fresh();
        var fake = new FakeGitHub();

        var http = await Assert.ThrowsAsync<CommunityException>(() => CommunityDownload.FetchAsync(
            fake.Client(), Entry(bytes, url: "http://cdn.example/x.set"), cache, null, default));
        Assert.Equal(CommunityDownload.LinkNotOpened, http.Message);

        var bad = await Assert.ThrowsAsync<CommunityException>(() => CommunityDownload.FetchAsync(
            fake.Client(), Entry(bytes, sha: "../../evil"), cache, null, default));
        Assert.Equal(ShareRules.HashMismatch, bad.Message);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task A_stalled_body_times_out_and_a_cancel_stays_a_cancel()
    {
        var bytes = PackedBytes();
        var entry = Entry(bytes);
        string cache = Fresh();
        FakeGitHub Stalling() => new FakeGitHub().On(HttpMethod.Get, UrlPath, _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new StallStream()),
        });

        var ex = await Assert.ThrowsAsync<CommunityException>(() => CommunityDownload.FetchAsync(
            Stalling().Client(), entry, cache, null, TimeSpan.FromMilliseconds(200), default));
        Assert.Equal(ShareRules.TimedOut, ex.Message);

        using var cts = new CancellationTokenSource(100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CommunityDownload.FetchAsync(
            Stalling().Client(), entry, cache, null, TimeSpan.FromSeconds(30), cts.Token));
        Assert.Empty(Left(cache));
    }

    /// <summary>Serves some bytes, then fails like a dropped connection.</summary>
    private sealed class CutStream(byte[] data, int cutAt) : Stream
    {
        private int _pos;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            if (_pos >= cutAt) throw new IOException("connection reset");
            int n = Math.Min(Math.Min(buffer.Length, cutAt - _pos), 1000);
            data.AsSpan(_pos, n).CopyTo(buffer);
            _pos += n;
            return n;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => new(Read(buffer.Span));
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Never produces a byte, until canceled.</summary>
    private sealed class StallStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>A body of unknown length: no Content-Length is ever computed.</summary>
    private sealed class PushContent(byte[] bytes, Action<long> onWritten) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
        {
            stream.Write(bytes);
            onWritten(bytes.Length);
            return Task.CompletedTask;
        }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }
}
