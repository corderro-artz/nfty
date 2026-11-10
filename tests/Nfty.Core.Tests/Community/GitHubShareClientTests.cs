using System.Net;
using System.Text;
using System.Text.Json;
using Nfty.Core.Publish.Community;

namespace Nfty.Core.Tests.Community;

public class GitHubShareClientTests
{
    private const string Token = "tok-SECRET-123";
    private static readonly CommunityEndpoints Ends = CommunityEndpoints.Default;

    private static GitHubShareClient Client(FakeGitHub fake, TimeProvider? time = null, TimeSpan? bodyTimeout = null) =>
        new(fake.Client(), Token, Ends, time, bodyTimeout);

    private static JsonElement Body(FakeGitHub.Recorded r) => JsonDocument.Parse(r.Body).RootElement;

    private const string RepoJson = "{\"full_name\":\"me/nfty-public\",\"private\":false,\"default_branch\":\"main\",\"size\":42}";

    [Fact]
    public async Task Every_request_carries_the_standard_headers()
    {
        var fake = new FakeGitHub().Ok(HttpMethod.Get, "/user", "{\"id\":7,\"login\":\"me\",\"created_at\":\"2020-01-02T03:04:05Z\"}");
        var user = await Client(fake).GetViewerAsync(default);

        var r = Assert.Single(fake.Requests);
        Assert.Equal("Bearer " + Token, r.Headers["Authorization"]);
        Assert.Contains("application/vnd.github+json", r.Headers["Accept"]);
        Assert.Equal("2022-11-28", r.Headers["X-GitHub-Api-Version"]);
        Assert.Equal("nfty", r.Headers["User-Agent"]);
        Assert.Equal(new GitHubUser(7, "me", DateTimeOffset.Parse("2020-01-02T03:04:05Z")), user);
        fake.AssertAllConsumed();
    }

    [Fact]
    public async Task Get_repo_reads_the_fields_and_answers_null_on_404()
    {
        var fake = new FakeGitHub()
            .Ok(HttpMethod.Get, "/repos/me/nfty-public", RepoJson)
            .On(HttpMethod.Get, "/repos/me/gone", HttpStatusCode.NotFound, "{}");
        var c = Client(fake);
        Assert.Equal(new GitHubRepo("me/nfty-public", false, "main", 42), await c.GetRepoAsync("me", "nfty-public", default));
        Assert.Null(await c.GetRepoAsync("me", "gone", default));
        fake.AssertAllConsumed();
    }

    [Fact]
    public async Task Create_repo_posts_name_description_and_auto_init()
    {
        var fake = new FakeGitHub().On(HttpMethod.Post, "/user/repos", HttpStatusCode.Created, RepoJson);
        var repo = await Client(fake).CreateRepoAsync("nfty-public", "Shared from nfty", default);
        var body = Body(Assert.Single(fake.Requests));
        Assert.Equal("nfty-public", body.GetProperty("name").GetString());
        Assert.Equal("Shared from nfty", body.GetProperty("description").GetString());
        Assert.True(body.GetProperty("auto_init").GetBoolean());
        Assert.Equal("me/nfty-public", repo.FullName);
    }

    [Fact]
    public async Task Get_tree_is_recursive_and_lists_items()
    {
        var fake = new FakeGitHub().Ok(HttpMethod.Get, "/repos/me/r/git/trees/main",
            "{\"tree\":[{\"path\":\"a.txt\",\"type\":\"blob\",\"sha\":\"s1\",\"size\":5},{\"path\":\"d\",\"type\":\"tree\",\"sha\":\"s2\"}]}");
        var tree = await Client(fake).GetTreeAsync("me", "r", "main", default);
        Assert.Equal("recursive=1", Assert.Single(fake.Requests).Url.Query.TrimStart('?'));
        Assert.Equal([new GitHubTreeItem("a.txt", "blob", "s1", 5), new GitHubTreeItem("d", "tree", "s2", null)], tree);
    }

    [Fact]
    public async Task Branches_and_tags_list_names_and_delete_by_ref()
    {
        var fake = new FakeGitHub()
            .Ok(HttpMethod.Get, "/repos/me/r/branches", "[{\"name\":\"main\"},{\"name\":\"feat\"}]")
            .Ok(HttpMethod.Get, "/repos/me/r/tags", "[{\"name\":\"v1\"}]")
            .On(HttpMethod.Delete, "/repos/me/r/git/refs/heads/feat/x", HttpStatusCode.NoContent, "")
            .On(HttpMethod.Delete, "/repos/me/r/git/refs/tags/v1", HttpStatusCode.NoContent, "");
        var c = Client(fake);
        Assert.Equal(["main", "feat"], await c.ListBranchesAsync("me", "r", default));
        Assert.Equal(["v1"], await c.ListTagsAsync("me", "r", default));
        await c.DeleteBranchAsync("me", "r", "feat/x", default);
        await c.DeleteTagAsync("me", "r", "v1", default);
        fake.AssertAllConsumed();
    }

    [Fact]
    public async Task Delete_asset_removes_one_release_file_by_id()
    {
        var fake = new FakeGitHub().On(HttpMethod.Delete, "/repos/me/r/releases/assets/12", HttpStatusCode.NoContent, "");
        await Client(fake).DeleteAssetAsync("me", "r", 12, default);
        fake.AssertAllConsumed();
    }

    [Fact]
    public async Task Get_file_decodes_content_and_sha_and_answers_null_on_404()
    {
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("hello world, long enough to wrap"));
        var fake = new FakeGitHub()
            .Ok(HttpMethod.Get, "/repos/me/r/contents/dir/a.json", $"{{\"sha\":\"abc\",\"content\":\"{b64[..8]}\\n{b64[8..]}\\n\"}}")
            .On(HttpMethod.Get, "/repos/me/r/contents/nope.json", HttpStatusCode.NotFound, "{}");
        var c = Client(fake);
        var file = await c.GetFileAsync("me", "r", "dir/a.json", "main", default);
        Assert.Equal("ref=main", fake.Requests[0].Url.Query.TrimStart('?'));
        Assert.Equal("hello world, long enough to wrap", Encoding.UTF8.GetString(file!.Value.Content));
        Assert.Equal("abc", file.Value.Sha);
        Assert.Null(await c.GetFileAsync("me", "r", "nope.json", "main", default));
    }

    [Fact]
    public async Task Put_file_sends_base64_message_branch_and_sha()
    {
        var fake = new FakeGitHub()
            .Ok(HttpMethod.Put, "/repos/me/r/contents/a/b.json", "{}")
            .Ok(HttpMethod.Put, "/repos/me/r/contents/new.json", "{}");
        var c = Client(fake);
        await c.PutFileAsync("me", "r", "a/b.json", [1, 2, 3], "msg", "oldsha", "main", default);
        await c.PutFileAsync("me", "r", "new.json", [9], "msg2", null, "main", default);
        var first = Body(fake.Requests[0]);
        Assert.Equal(Convert.ToBase64String([1, 2, 3]), first.GetProperty("content").GetString());
        Assert.Equal("msg", first.GetProperty("message").GetString());
        Assert.Equal("main", first.GetProperty("branch").GetString());
        Assert.Equal("oldsha", first.GetProperty("sha").GetString());
        Assert.False(Body(fake.Requests[1]).TryGetProperty("sha", out _));
    }

    [Fact]
    public async Task Delete_file_sends_sha_message_and_branch()
    {
        var fake = new FakeGitHub().Ok(HttpMethod.Delete, "/repos/me/r/contents/x.json", "{}");
        await Client(fake).DeleteFileAsync("me", "r", "x.json", "sh", "bye", "main", default);
        var body = Body(Assert.Single(fake.Requests));
        Assert.Equal("sh", body.GetProperty("sha").GetString());
        Assert.Equal("bye", body.GetProperty("message").GetString());
        Assert.Equal("main", body.GetProperty("branch").GetString());
    }

    private const string ReleaseJson =
        "{\"id\":11,\"tag_name\":\"cat-v1\",\"assets\":[{\"id\":5,\"name\":\"cat.set\",\"size\":99}]}";

    [Fact]
    public async Task Releases_list_create_and_delete()
    {
        var fake = new FakeGitHub()
            .Ok(HttpMethod.Get, "/repos/me/r/releases", $"[{ReleaseJson}]")
            .On(HttpMethod.Post, "/repos/me/r/releases", HttpStatusCode.Created, ReleaseJson)
            .On(HttpMethod.Delete, "/repos/me/r/releases/11", HttpStatusCode.NoContent, "");
        var c = Client(fake);
        var list = await c.ListReleasesAsync("me", "r", default);
        Assert.Equal(11, list[0].Id);
        Assert.Equal("cat-v1", list[0].Tag);
        Assert.Equal(new GitHubAsset(5, "cat.set", 99), list[0].Assets[0]);
        var made = await c.CreateReleaseAsync("me", "r", "cat-v1", "Cat", default);
        var body = Body(fake.Requests[1]);
        Assert.Equal("cat-v1", body.GetProperty("tag_name").GetString());
        Assert.Equal("Cat", body.GetProperty("name").GetString());
        Assert.Equal(11, made.Id);
        await c.DeleteReleaseAsync("me", "r", 11, default);
        fake.AssertAllConsumed();
    }

    private sealed class Trickle(byte[] data) : Stream
    {
        private int _pos;
        public int MaxRead;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            int n = Math.Min(Math.Min(buffer.Length, 10_000), data.Length - _pos);
            MaxRead = Math.Max(MaxRead, buffer.Length);
            data.AsSpan(_pos, n).CopyTo(buffer);
            _pos += n;
            return n;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Task.FromResult(Read(buffer.AsSpan(offset, count)));
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => new(Read(buffer.Span));
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    private sealed class SyncProgress(Action<long> on) : IProgress<long>
    {
        public void Report(long value) => on(value);
    }

    [Fact]
    public async Task Upload_streams_without_buffering()
    {
        var data = new byte[300_000];
        new Random(5).NextBytes(data);
        var fake = new FakeGitHub().On(HttpMethod.Post, "/repos/me/r/releases/11/assets", HttpStatusCode.Created, "{\"id\":1,\"name\":\"a b.set\",\"size\":300000}");
        var progress = new List<long>();
        var source = new Trickle(data);
        var release = new GitHubRelease(11, "cat-v1", [], "me", "r");

        await Client(fake).UploadAssetAsync(release, "a b.set", source, data.Length, new SyncProgress(progress.Add), default);

        var sent = Assert.Single(fake.Requests);
        Assert.Equal("uploads.github.com", sent.Url.Host);
        Assert.Equal("name=a%20b.set", sent.Url.Query.TrimStart('?'));
        Assert.Equal("300000", sent.Headers["Content-Length"]);
        Assert.False(sent.Headers.ContainsKey("Transfer-Encoding"));
        Assert.Equal("application/octet-stream", sent.Headers["Content-Type"]);
        Assert.Equal("Bearer " + Token, sent.Headers["Authorization"]);
        Assert.Equal(data, sent.BodyBytes);
        Assert.True(source.MaxRead <= 128 * 1024, $"read in a {source.MaxRead}-byte gulp");
        Assert.Equal(data.Length, progress[^1]);
        Assert.Equal(progress.OrderBy(x => x), progress);
    }

    [Fact]
    public async Task Upload_with_a_short_stream_is_refused()
    {
        var fake = new FakeGitHub().On(HttpMethod.Post, "/repos/me/r/releases/11/assets", HttpStatusCode.Created, "{}");
        var release = new GitHubRelease(11, "t", [], "me", "r");
        var ex = await Assert.ThrowsAsync<CommunityException>(() =>
            Client(fake).UploadAssetAsync(release, "a.set", new MemoryStream(new byte[10]), 20, null, default));
        Assert.Equal(ShareRules.UploadSourceFailed, ex.Message);
    }

    [Fact]
    public async Task Fork_posts_to_the_gallery_and_returns_the_repo()
    {
        var fake = new FakeGitHub().On(HttpMethod.Post, "/repos/vaporsoft-dev/nfty-gallery/forks", HttpStatusCode.Accepted,
            "{\"full_name\":\"me/nfty-gallery\",\"private\":false,\"default_branch\":\"main\",\"size\":1}");
        var fork = await Client(fake).ForkAsync(Ends.GalleryRepo, default);
        Assert.Equal("me/nfty-gallery", fork.FullName);
    }

    [Fact]
    public async Task Branch_sha_and_branch_creation()
    {
        var fake = new FakeGitHub()
            .Ok(HttpMethod.Get, "/repos/me/r/git/ref/heads/main", "{\"object\":{\"sha\":\"base123\"}}")
            .On(HttpMethod.Post, "/repos/me/r/git/refs", HttpStatusCode.Created, "{}");
        var c = Client(fake);
        Assert.Equal("base123", await c.GetBranchShaAsync("me", "r", "main", default));
        await c.CreateBranchAsync("me", "r", "share/cat", "base123", default);
        var body = Body(fake.Requests[1]);
        Assert.Equal("refs/heads/share/cat", body.GetProperty("ref").GetString());
        Assert.Equal("base123", body.GetProperty("sha").GetString());
    }

    [Fact]
    public async Task Open_pull_targets_the_gallery_default_branch_and_returns_the_number()
    {
        var fake = new FakeGitHub()
            .Ok(HttpMethod.Get, "/repos/vaporsoft-dev/nfty-gallery", "{\"full_name\":\"vaporsoft-dev/nfty-gallery\",\"private\":false,\"default_branch\":\"trunk\",\"size\":1}")
            .On(HttpMethod.Post, "/repos/vaporsoft-dev/nfty-gallery/pulls", HttpStatusCode.Created, "{\"number\":42}");
        int n = await Client(fake).OpenPullAsync(Ends.GalleryRepo, "me:share/cat", "Add cat", "body text", default);
        var body = Body(fake.Requests[1]);
        Assert.Equal(42, n);
        Assert.Equal("me:share/cat", body.GetProperty("head").GetString());
        Assert.Equal("trunk", body.GetProperty("base").GetString());
        Assert.Equal("Add cat", body.GetProperty("title").GetString());
        Assert.Equal("body text", body.GetProperty("body").GetString());
    }

    [Fact]
    public async Task Pull_state_and_comments()
    {
        var fake = new FakeGitHub()
            .Ok(HttpMethod.Get, "/repos/vaporsoft-dev/nfty-gallery/pulls/42", "{\"state\":\"closed\",\"merged\":true}")
            .Ok(HttpMethod.Get, "/repos/vaporsoft-dev/nfty-gallery/issues/42/comments", "[{\"body\":\"one\",\"user\":{\"login\":\"github-actions[bot]\"}},{\"body\":\"two\",\"user\":{\"login\":\"github-actions[bot]\"}}]");
        var c = Client(fake);
        Assert.Equal(new GitHubPull("closed", true), await c.GetPullAsync(Ends.GalleryRepo, 42, default));
        Assert.Equal(["one", "two"], await c.GetPullCommentsAsync(Ends.GalleryRepo, 42, default));
    }

    [Fact]
    public async Task Only_the_gallery_bots_comments_are_returned()
    {
        var fake = new FakeGitHub().Ok(HttpMethod.Get, "/repos/vaporsoft-dev/nfty-gallery/issues/42/comments",
            "[{\"body\":\"fake reason\",\"user\":{\"login\":\"stranger\"}},{\"body\":\"real\",\"user\":{\"login\":\"github-actions[bot]\"}},{\"body\":\"no user\"},{\"body\":\"null user\",\"user\":null}]");
        Assert.Equal(["real"], await Client(fake).GetPullCommentsAsync(Ends.GalleryRepo, 42, default));
    }

    [Fact]
    public async Task A_401_becomes_the_reconnect_sentence()
    {
        var fake = new FakeGitHub().On(HttpMethod.Get, "/user", HttpStatusCode.Unauthorized, "{\"message\":\"Bad credentials\"}");
        var ex = await Assert.ThrowsAsync<GitHubAuthException>(() => Client(fake).GetViewerAsync(default));
        Assert.Equal(ShareRules.Reconnect, ex.Message);
        Assert.DoesNotContain(Token, ex.Message);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public async Task Rate_limit_names_the_wait()
    {
        var now = new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
        var reset = now.AddMinutes(7).AddSeconds(10).ToUnixTimeSeconds().ToString();
        var fake = new FakeGitHub().On(HttpMethod.Get, "/user", HttpStatusCode.Forbidden, "{}",
            new Dictionary<string, string> { ["x-ratelimit-remaining"] = "0", ["x-ratelimit-reset"] = reset });
        var ex = await Assert.ThrowsAnyAsync<CommunityException>(() => Client(fake, new FixedTime(now)).GetViewerAsync(default));
        Assert.Equal("GitHub is limiting requests. Try again in 8 minutes.", ex.Message);
    }

    [Fact]
    public async Task Any_other_refusal_is_one_plain_sentence()
    {
        var fake = new FakeGitHub().On(HttpMethod.Post, "/user/repos", HttpStatusCode.UnprocessableEntity, "{\"message\":\"secret detail\"}");
        var ex = await Assert.ThrowsAsync<CommunityException>(() => Client(fake).CreateRepoAsync("x", "y", default));
        Assert.Equal("GitHub refused the request.", ex.Message);
    }

    [Fact]
    public async Task A_dropped_network_is_the_unreachable_sentence()
    {
        var fake = new FakeGitHub().On(HttpMethod.Get, "/user", _ => throw new HttpRequestException("connection reset"));
        var ex = await Assert.ThrowsAsync<CommunityException>(() => Client(fake).GetViewerAsync(default));
        Assert.Equal(ShareRules.Unreachable, ex.Message);
    }

    [Fact]
    public async Task A_timeout_is_a_sentence_but_the_callers_cancel_stays_a_cancel()
    {
        var fake = new FakeGitHub().On(HttpMethod.Get, "/user", _ => throw new TaskCanceledException("timed out"));
        var ex = await Assert.ThrowsAsync<CommunityException>(() => Client(fake).GetViewerAsync(default));
        Assert.Equal(ShareRules.TimedOut, ex.Message);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(new FakeGitHub()).GetViewerAsync(cts.Token));
    }

    [Fact]
    public async Task Path_segments_are_escaped()
    {
        var fake = new FakeGitHub()
            .Ok(HttpMethod.Get, "/repos/o%2Fx/r%20y/contents/a%20b/c%23d.json", "{\"sha\":\"s\",\"content\":\"\"}");
        await Client(fake).GetFileAsync("o/x", "r y", "a b/c#d.json", "feat/a b", default);
        var r = Assert.Single(fake.Requests);
        Assert.Contains("ref=feat%2Fa%20b", r.Url.Query);
        fake.AssertAllConsumed();
    }

    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("")]
    public async Task A_dot_or_empty_segment_is_refused_before_anything_is_sent(string bad)
    {
        var fake = new FakeGitHub();
        await Assert.ThrowsAsync<ArgumentException>(() => Client(fake).GetRepoAsync("me", bad, default));
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task Index_fetch_sends_no_token()
    {
        var fake = new FakeGitHub().On(HttpMethod.Get, "/nfty-gallery/index.json", HttpStatusCode.OK,
            "{\"schemaVersion\":1,\"generated\":\"2026-11-09T00:00:00Z\",\"entries\":[]}");
        var index = await Client(fake).GetIndexAsync(default);
        var r = Assert.Single(fake.Requests);
        Assert.Equal("vaporsoft-dev.github.io", r.Url.Host);
        Assert.False(r.Headers.ContainsKey("Authorization"));
        Assert.Equal("nfty", r.Headers["User-Agent"]);
        Assert.Empty(index.Entries);
    }

    private static string EntryJson(string slug) => $$"""
        {"schemaVersion":1,"slug":"{{slug}}","owner":{"id":7,"login":"me"},"repo":"nfty-public","release":"{{slug}}-v1",
        "name":"N","collection":"C","description":"d","tags":["pets"],"canvas":{"width":64,"height":64},"assets":10,
        "models":false,"includesCookBook":false,"cookbookSha256":null,
        "set":{"url":"https://github.com/me/nfty-public/releases/download/{{slug}}-v1/{{slug}}.set","size":5,"sha256":"aa"},
        "preview":{"url":"https://github.com/me/nfty-public/releases/download/{{slug}}-v1/preview.png","size":5,"sha256":"bb"},
        "fingerprint":"ff","nftyVersion":"2.0","created":"2026-11-09T00:00:00Z","updated":"2026-11-09T00:00:00Z"}
        """;

    [Fact]
    public async Task Open_pull_entries_reads_added_entry_files_and_skips_the_rest()
    {
        const string gallery = "/repos/vaporsoft-dev/nfty-gallery";
        var fake = new FakeGitHub()
            .Ok(HttpMethod.Get, gallery + "/pulls", "[{\"number\":3,\"head\":{\"sha\":\"h3\",\"repo\":{\"full_name\":\"me/nfty-gallery\"}}}]")
            .Ok(HttpMethod.Get, gallery + "/pulls/3/files",
                "[{\"filename\":\"entries/7/cat.json\",\"status\":\"added\"},{\"filename\":\"entries/7/old.json\",\"status\":\"removed\"},{\"filename\":\"README.md\",\"status\":\"modified\"},{\"filename\":\"entries/7/bad.json\",\"status\":\"modified\"}]")
            .OnBytes(HttpMethod.Get, "/repos/me/nfty-gallery/contents/entries/7/cat.json", HttpStatusCode.OK, Encoding.UTF8.GetBytes(EntryJson("cat")))
            .OnBytes(HttpMethod.Get, "/repos/me/nfty-gallery/contents/entries/7/bad.json", HttpStatusCode.OK, Encoding.UTF8.GetBytes("not json"));

        var entries = await Client(fake).ListOpenPullEntriesAsync(default);

        Assert.Equal("cat", Assert.Single(entries).Slug);
        Assert.Contains("state=open", fake.Requests[0].Url.Query);
        var raw = fake.Requests[^2];
        Assert.Equal("ref=h3", raw.Url.Query.TrimStart('?'));
        Assert.Contains("application/vnd.github.raw+json", raw.Headers["Accept"]);
        fake.AssertAllConsumed();
    }

    private sealed class Endless : Stream
    {
        public long Total;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => Total; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) { Total += count; Array.Fill(buffer, (byte)'a', offset, count); return count; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            Total += buffer.Length;
            buffer.Span.Fill((byte)'a');
            return new(buffer.Length);
        }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    private sealed class Stalls : Stream
    {
        private bool _first = true;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_first) { _first = false; buffer.Span[0] = (byte)'{'; return 1; }
            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    private sealed class Drops : Stream
    {
        private bool _first = true;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (!_first) throw new IOException("connection reset");
            _first = false;
            buffer.Span[0] = (byte)'{';
            return new(1);
        }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    [Fact]
    public async Task A_connection_dropped_mid_body_is_the_unreachable_sentence()
    {
        var fake = new FakeGitHub().On(HttpMethod.Get, "/user", _ => Streamed(new Drops()));
        var ex = await Assert.ThrowsAsync<CommunityException>(() => Client(fake).GetViewerAsync(default));
        Assert.Equal(ShareRules.Unreachable, ex.Message);
    }

    private sealed class Throws(Func<CancellationToken, Exception> make) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw make(default);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            await Task.Delay(Timeout.Infinite, ct).ContinueWith(_ => { }, CancellationToken.None);
            throw make(ct);
        }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    [Fact]
    public async Task A_cancel_that_surfaces_as_an_IOException_stays_a_cancel()
    {
        using var cts = new CancellationTokenSource(100);
        var fake = new FakeGitHub().On(HttpMethod.Get, "/user", _ => Streamed(new Throws(_ => new IOException("aborted"))));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(fake, bodyTimeout: TimeSpan.FromSeconds(30)).GetViewerAsync(cts.Token));
    }

    [Fact]
    public async Task A_body_deadline_that_surfaces_as_an_IOException_is_the_timeout_sentence()
    {
        var fake = new FakeGitHub().On(HttpMethod.Get, "/user", _ => Streamed(new Throws(_ => new IOException("aborted"))));
        var ex = await Assert.ThrowsAsync<CommunityException>(() => Client(fake, bodyTimeout: TimeSpan.FromMilliseconds(100)).GetViewerAsync(default));
        Assert.Equal(ShareRules.TimedOut, ex.Message);
    }

    [Fact]
    public async Task A_failure_reading_the_callers_file_is_not_blamed_on_GitHub()
    {
        var fake = new FakeGitHub().On(HttpMethod.Post, "/repos/me/r/releases/11/assets", HttpStatusCode.Created, "{}");
        var release = new GitHubRelease(11, "t", [], "me", "r");
        var ex = await Assert.ThrowsAsync<CommunityException>(() =>
            Client(fake).UploadAssetAsync(release, "a.set", new Boom(), 20, null, default));
        Assert.Equal(ShareRules.UploadSourceFailed, ex.Message);
    }

    private sealed class Boom : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("disk gone");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => throw new IOException("disk gone");
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    private static HttpResponseMessage Streamed(Stream body) => new(HttpStatusCode.OK) { Content = new StreamContent(body) };

    [Fact]
    public async Task An_oversized_reply_is_refused_without_reading_it_all()
    {
        var endless = new Endless();
        var fake = new FakeGitHub().On(HttpMethod.Get, "/user", _ => Streamed(endless));
        var ex = await Assert.ThrowsAsync<CommunityException>(() => Client(fake).GetViewerAsync(default));
        Assert.Equal(ShareRules.GitHubTooMuch, ex.Message);
        Assert.True(endless.Total < 2 * 1024 * 1024, $"read {endless.Total} bytes");
    }

    [Fact]
    public async Task A_declared_oversize_is_refused_before_any_read()
    {
        var endless = new Endless();
        var fake = new FakeGitHub().On(HttpMethod.Get, "/user", _ =>
        {
            var r = Streamed(endless);
            r.Content.Headers.ContentLength = 50_000_000;
            return r;
        });
        var ex = await Assert.ThrowsAsync<CommunityException>(() => Client(fake).GetViewerAsync(default));
        Assert.Equal(ShareRules.GitHubTooMuch, ex.Message);
        Assert.Equal(0, endless.Total);
    }

    [Fact]
    public async Task A_body_that_stalls_after_the_headers_times_out_with_the_sentence()
    {
        var fake = new FakeGitHub().On(HttpMethod.Get, "/user", _ => Streamed(new Stalls()));
        var ex = await Assert.ThrowsAsync<CommunityException>(() => Client(fake, bodyTimeout: TimeSpan.FromMilliseconds(150)).GetViewerAsync(default));
        Assert.Equal(ShareRules.TimedOut, ex.Message);
    }

    [Fact]
    public async Task A_caller_cancel_during_a_stalled_body_stays_a_cancel()
    {
        var fake = new FakeGitHub().On(HttpMethod.Get, "/user", _ => Streamed(new Stalls()));
        using var cts = new CancellationTokenSource(150);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(fake, bodyTimeout: TimeSpan.FromSeconds(30)).GetViewerAsync(cts.Token));
    }

    private const string Gallery = "/repos/vaporsoft-dev/nfty-gallery";

    private static string PullsJson(int n) =>
        "[" + string.Join(",", Enumerable.Range(1, n).Select(i => "{\"number\":" + i + ",\"head\":{\"sha\":\"h" + i + "\",\"repo\":{\"full_name\":\"me/nfty-gallery\"}}}")) + "]";

    [Fact]
    public async Task A_pull_with_many_entry_files_has_only_three_read()
    {
        var files = string.Join(",", Enumerable.Range(1, 50).Select(i => "{\"filename\":\"entries/7/f" + i + ".json\",\"status\":\"added\",\"changes\":30}"));
        var fake = new FakeGitHub()
            .Ok(HttpMethod.Get, Gallery + "/pulls", PullsJson(1))
            .Ok(HttpMethod.Get, Gallery + "/pulls/1/files", "[" + files + "]");
        for (int i = 1; i <= 3; i++)
            fake.OnBytes(HttpMethod.Get, $"/repos/me/nfty-gallery/contents/entries/7/f{i}.json", HttpStatusCode.OK, Encoding.UTF8.GetBytes(EntryJson("s" + i)));

        var entries = await Client(fake).ListOpenPullEntriesAsync(default);

        Assert.Equal(3, entries.Count);
        Assert.Equal(5, fake.Requests.Count);
        fake.AssertAllConsumed();
    }

    [Fact]
    public async Task Only_one_page_of_pulls_is_scanned()
    {
        var fake = new FakeGitHub().Ok(HttpMethod.Get, Gallery + "/pulls", PullsJson(100));
        for (int i = 1; i <= 100; i++) fake.Ok(HttpMethod.Get, $"{Gallery}/pulls/{i}/files", "[]");
        await Client(fake).ListOpenPullEntriesAsync(default);
        Assert.Single(fake.To(Gallery + "/pulls"));
        fake.AssertAllConsumed();
    }

    [Fact]
    public async Task A_huge_entry_file_is_skipped_unread()
    {
        var fake = new FakeGitHub()
            .Ok(HttpMethod.Get, Gallery + "/pulls", PullsJson(1))
            .Ok(HttpMethod.Get, Gallery + "/pulls/1/files",
                "[{\"filename\":\"entries/7/big.json\",\"status\":\"added\",\"changes\":90000},{\"filename\":\"entries/7/ok.json\",\"status\":\"added\",\"changes\":30}]")
            .OnBytes(HttpMethod.Get, "/repos/me/nfty-gallery/contents/entries/7/ok.json", HttpStatusCode.OK, Encoding.UTF8.GetBytes(EntryJson("ok")));
        var entries = await Client(fake).ListOpenPullEntriesAsync(default);
        Assert.Equal("ok", Assert.Single(entries).Slug);
        fake.AssertAllConsumed();
    }

    private static FakeGitHub TwoFilePull() => new FakeGitHub()
        .Ok(HttpMethod.Get, Gallery + "/pulls", PullsJson(1))
        .Ok(HttpMethod.Get, Gallery + "/pulls/1/files",
            "[{\"filename\":\"entries/7/a.json\",\"status\":\"added\"},{\"filename\":\"entries/7/b.json\",\"status\":\"added\"}]");

    [Fact]
    public async Task A_500_on_one_entry_still_returns_the_others()
    {
        var fake = TwoFilePull()
            .On(HttpMethod.Get, "/repos/me/nfty-gallery/contents/entries/7/a.json", HttpStatusCode.InternalServerError, "{}")
            .OnBytes(HttpMethod.Get, "/repos/me/nfty-gallery/contents/entries/7/b.json", HttpStatusCode.OK, Encoding.UTF8.GetBytes(EntryJson("b")));
        var entries = await Client(fake).ListOpenPullEntriesAsync(default);
        Assert.Equal("b", Assert.Single(entries).Slug);
    }

    [Fact]
    public async Task A_401_on_an_entry_still_aborts_with_reconnect()
    {
        var fake = TwoFilePull()
            .On(HttpMethod.Get, "/repos/me/nfty-gallery/contents/entries/7/a.json", HttpStatusCode.Unauthorized, "{}");
        var ex = await Assert.ThrowsAsync<GitHubAuthException>(() => Client(fake).ListOpenPullEntriesAsync(default));
        Assert.Equal(ShareRules.Reconnect, ex.Message);
    }

    [Fact]
    public async Task A_rate_limit_on_an_entry_still_aborts()
    {
        var fake = TwoFilePull()
            .On(HttpMethod.Get, "/repos/me/nfty-gallery/contents/entries/7/a.json", HttpStatusCode.Forbidden, "{}",
                new Dictionary<string, string> { ["x-ratelimit-remaining"] = "0", ["x-ratelimit-reset"] = "9999999999" });
        var ex = await Assert.ThrowsAnyAsync<CommunityException>(() => Client(fake).ListOpenPullEntriesAsync(default));
        Assert.StartsWith("GitHub is limiting requests", ex.Message);
    }
}
