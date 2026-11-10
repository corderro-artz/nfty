using System.Net;
using System.Security.Cryptography;
using Nfty.App.Services;
using Nfty.Core.Publish.Community;
using Xunit;

namespace Nfty.App.Tests;

public sealed class CommunityCacheTests : IDisposable
{
    private static readonly Uri IndexUrl = new("https://example.test/index.json");
    private readonly string _root = Directory.CreateTempSubdirectory("nfty-cc-").FullName;

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    internal static CommunityEntry Entry(string slug, CommunityFile? preview = null,
        DateTimeOffset? created = null, DateTimeOffset? updated = null)
    {
        var t = created ?? new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        return new CommunityEntry(1, slug, new CommunityOwner(7, "alice"), "nfty-public", "v1", slug, "Coll", "", [],
            new CommunityCanvas(8, 8), 4, false, false, null,
            new CommunityFile("https://example.test/s.set", 10, new string('a', 64)),
            preview ?? new CommunityFile("https://example.test/p.png", 3, new string('b', 64)),
            "fp-" + slug, "1.0", t, updated ?? t);
    }

    internal static byte[] IndexBytes(params CommunityEntry[] entries) =>
        CommunityIndex.Write(new CommunityIndex(1, DateTimeOffset.UnixEpoch, entries));

    private sealed class Fake(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Seen { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Seen.Add(request);
            if (request.RequestUri!.Host == "api.github.com") throw new InvalidOperationException("api.github.com called");
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Ok(byte[] body, string? etag = null)
    {
        var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        if (etag is not null) r.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag);
        return r;
    }

    private CommunityCache Cache(Fake f, long budget = CommunityCache.DefaultPreviewBudget, Func<DateTimeOffset>? clock = null) =>
        new(_root, new HttpClient(f), IndexUrl, budget, clock);

    [Fact]
    public async Task A_304_keeps_the_cached_index_and_sends_the_etag()
    {
        var body = IndexBytes(Entry("one"));
        var f = new Fake(r => r.Headers.IfNoneMatch.Count > 0 ? new HttpResponseMessage(HttpStatusCode.NotModified) : Ok(body, "\"v1\""));
        var cache = Cache(f);

        var first = await cache.RefreshAsync(default);
        var second = await cache.RefreshAsync(default);

        Assert.Null(first.Notice);
        Assert.Equal("\"v1\"", f.Seen.Where(r => r.RequestUri!.AbsolutePath == "/index.json").ElementAt(1)
            .Headers.IfNoneMatch.Single().ToString());
        Assert.Null(second.Notice);
        Assert.Single(second.Index!.Entries);
        Assert.Equal("\"v1\"", File.ReadAllText(Path.Combine(_root, "index.etag")));
    }

    [Fact]
    public async Task Offline_returns_the_cache_with_the_offline_notice()
    {
        var online = true;
        var f = new Fake(_ => online ? Ok(IndexBytes(Entry("one"))) : throw new HttpRequestException("down"));
        var cache = Cache(f);
        var got = await cache.RefreshAsync(default);
        online = false;

        var offline = await new CommunityCache(_root, new HttpClient(f), IndexUrl).RefreshAsync(default);

        Assert.Single(offline.Index!.Entries);
        Assert.Equal(ShareRules.Offline(got.FetchedAt.ToLocalTime().DateTime), offline.Notice);
    }

    [Fact]
    public async Task Offline_without_a_cache_has_no_list_and_says_unreachable()
    {
        var f = new Fake(_ => throw new HttpRequestException("down"));
        var got = await Cache(f).RefreshAsync(default);
        Assert.Null(got.Index);
        Assert.Equal(ShareRules.Unreachable, got.Notice);
    }

    [Fact]
    public async Task A_newer_index_keeps_the_cached_list()
    {
        var newer = System.Text.Encoding.UTF8.GetBytes("{\"schemaVersion\":99,\"generated\":\"2026-01-01T00:00:00Z\",\"entries\":[]}");
        var serveNewer = false;
        var f = new Fake(_ => Ok(serveNewer ? newer : IndexBytes(Entry("one"))));
        var cache = Cache(f);
        await cache.RefreshAsync(default);
        serveNewer = true;

        var got = await cache.RefreshAsync(default);

        Assert.Equal(ShareRules.IndexTooNew, got.Notice);
        Assert.Single(got.Index!.Entries);
        Assert.Single(new CommunityCache(_root, new HttpClient(f), IndexUrl).Cached!.Index!.Entries);
    }

    [Fact]
    public async Task A_damaged_index_never_replaces_a_good_one()
    {
        var bad = false;
        var f = new Fake(_ => Ok(bad ? "{nope"u8.ToArray() : IndexBytes(Entry("one"))));
        var cache = Cache(f);
        await cache.RefreshAsync(default);
        bad = true;
        var got = await cache.RefreshAsync(default);
        Assert.Equal(ShareRules.ListDamaged, got.Notice);
        Assert.Single(got.Index!.Entries);
    }

    [Fact]
    public async Task An_oversized_index_is_refused_and_the_cache_kept()
    {
        var big = false;
        var f = new Fake(_ => Ok(big ? new byte[CommunityCache.MaxIndexBytes + 1] : IndexBytes(Entry("one"))));
        var cache = Cache(f);
        await cache.RefreshAsync(default);
        big = true;
        var got = await cache.RefreshAsync(default);
        Assert.NotNull(got.Notice);
        Assert.Single(got.Index!.Entries);
    }

    private static byte[] TagsBytes(params string[] tags) =>
        System.Text.Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"tags\":[" + string.Join(",", tags.Select(t => "\"" + t + "\"")) + "]}");

    private static bool IsTags(HttpRequestMessage r) => r.RequestUri!.AbsolutePath == "/tags.json";

    [Fact]
    public async Task Cache_fetches_tags_beside_the_index_without_a_token()
    {
        var f = new Fake(r => IsTags(r) ? Ok(TagsBytes("props", "items"), "\"t1\"") : Ok(IndexBytes(Entry("one"))));
        var cache = Cache(f);
        Assert.Empty(cache.Tags);

        var listing = await cache.RefreshAsync(default);

        Assert.Null(listing.Notice);
        Assert.Equal(["props", "items"], cache.Tags);
        var req = f.Seen.Single(IsTags);
        Assert.Equal("https://example.test/tags.json", req.RequestUri!.ToString());
        Assert.Null(req.Headers.Authorization);
        // kept on disk: a new cache over the same folder has them before any fetch
        Assert.Equal(["props", "items"], new CommunityCache(_root, new HttpClient(f), IndexUrl).Tags);
        // second refresh asks conditionally and keeps them on a 304
        var f2 = new Fake(r => IsTags(r) ? new HttpResponseMessage(HttpStatusCode.NotModified) : Ok(IndexBytes(Entry("one"))));
        var again = new CommunityCache(_root, new HttpClient(f2), IndexUrl);
        await again.RefreshAsync(default);
        Assert.Equal("\"t1\"", f2.Seen.Single(IsTags).Headers.IfNoneMatch.Single().ToString());
        Assert.Equal(["props", "items"], again.Tags);
    }

    [Fact]
    public async Task A_failed_tags_fetch_keeps_the_cached_list()
    {
        var mode = 0;
        var f = new Fake(r =>
        {
            if (!IsTags(r)) return Ok(IndexBytes(Entry("one")));
            return mode switch
            {
                0 => Ok(TagsBytes("props")),
                1 => throw new HttpRequestException("down"),
                2 => Ok("{nope"u8.ToArray()),
                3 => Ok(System.Text.Encoding.UTF8.GetBytes("{\"schemaVersion\":99,\"tags\":[\"x\"]}")),
                4 => Ok(new byte[CommunityCache.MaxTagsBytes + 1]),
                _ => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            };
        });
        var cache = Cache(f);
        await cache.RefreshAsync(default);

        for (mode = 1; mode <= 5; mode++)
        {
            var listing = await cache.RefreshAsync(default);
            Assert.Null(listing.Notice);                       // the index refresh is untouched
            Assert.Single(listing.Index!.Entries);
            Assert.Equal(["props"], cache.Tags);
        }
        Assert.Equal(["props"], new CommunityCache(_root, new HttpClient(f), IndexUrl).Tags);
    }

    private static (CommunityFile file, byte[] body) PreviewOf(byte fill, int size = 16)
    {
        var body = Enumerable.Repeat(fill, size).ToArray();
        return (new CommunityFile("https://example.test/p" + fill + ".png", size,
            Convert.ToHexStringLower(SHA256.HashData(body))), body);
    }

    [Fact]
    public async Task Previews_are_verified_by_hash()
    {
        var (file, body) = PreviewOf(1);
        var cache = Cache(new Fake(_ => Ok(body)));
        var path = await cache.PreviewAsync(file, default);
        Assert.Equal(body, File.ReadAllBytes(path!));
        Assert.Equal(file.Sha256 + ".png", Path.GetFileName(path));

        var (wrong, _) = PreviewOf(2);
        Assert.Null(await Cache(new Fake(_ => Ok(body))).PreviewAsync(wrong, default));
        Assert.False(File.Exists(Path.Combine(_root, "previews", wrong.Sha256 + ".png")));
    }

    [Fact]
    public async Task Previews_refuse_non_https_oversized_and_unsafe_names()
    {
        var (file, body) = PreviewOf(3);
        var cache = Cache(new Fake(_ => Ok(body)));
        Assert.Null(await cache.PreviewAsync(file with { Url = "http://example.test/p.png" }, default));
        Assert.Null(await cache.PreviewAsync(file with { Size = ShareRules.MaxPreviewBytes + 1 }, default));
        Assert.Null(await cache.PreviewAsync(file with { Sha256 = "../../evil" }, default));
        Assert.Null(await cache.PreviewAsync(file with { Sha256 = file.Sha256.ToUpperInvariant() }, default));
    }

    [Fact]
    public async Task Previews_trim_oldest_past_200_mb()
    {
        // The 200 MB budget is a constructor value; the test squeezes it to 40 bytes so two 16-byte
        // previews fit and a third pushes the least recently used out.
        Assert.Equal(209_715_200, CommunityCache.DefaultPreviewBudget);
        var t = new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
        var now = t;
        var items = new[] { PreviewOf(1), PreviewOf(2), PreviewOf(3) };
        var f = new Fake(r => Ok(items.First(i => i.file.Url == r.RequestUri!.ToString()).body));
        var cache = Cache(f, budget: 40, clock: () => now);

        var p1 = await cache.PreviewAsync(items[0].file, default);
        now = t.AddMinutes(1);
        var p2 = await cache.PreviewAsync(items[1].file, default);
        now = t.AddMinutes(2);
        await cache.PreviewAsync(items[0].file, default);   // touch 1: 2 is now the oldest
        now = t.AddMinutes(3);
        var p3 = await cache.PreviewAsync(items[2].file, default);

        Assert.True(File.Exists(p1));
        Assert.False(File.Exists(p2));
        Assert.True(File.Exists(p3));
    }

    [Fact]
    public async Task Never_calls_api_github_com()
    {
        var (file, body) = PreviewOf(4);
        var f = new Fake(r => r.RequestUri!.AbsolutePath.EndsWith("index.json") ? Ok(IndexBytes(Entry("one"))) : Ok(body));
        var cache = Cache(f);
        await cache.RefreshAsync(default);
        await cache.PreviewAsync(file, default);
        Assert.NotEmpty(f.Seen);
        Assert.All(f.Seen, r =>
        {
            Assert.NotEqual("api.github.com", r.RequestUri!.Host);
            Assert.Null(r.Headers.Authorization);
        });
    }

    [Fact]
    public void Sets_live_under_the_community_folder()
    {
        Assert.Equal(Path.Combine(_root, "sets"), Cache(new Fake(_ => Ok([]))).SetsDir);
    }
}
