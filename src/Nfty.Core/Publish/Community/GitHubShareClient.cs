using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nfty.Core.Formats;

namespace Nfty.Core.Publish.Community;

/// <summary>
/// The GitHub REST calls sharing needs. The token goes only in the <c>Authorization</c> header of requests to the
/// endpoints' API and uploads hosts: never to the published list, never into a message. Every failure leaves as a
/// <see cref="ShareRules"/> sentence: <see cref="GitHubAuthException"/> for a refused token, <see cref="CommunityException"/>
/// for the rest. A caller's own cancellation stays an <see cref="OperationCanceledException"/>.
/// </summary>
/// <param name="http">The client every request goes through (not disposed here).</param>
/// <param name="token">The person's GitHub access token.</param>
/// <param name="endpoints">Where the gallery and GitHub live.</param>
/// <param name="time">The clock a rate-limit wait is measured against; tests inject one.</param>
/// <param name="bodyTimeout">How long a response body may take once its headers arrived (default 30 seconds).</param>
public sealed class GitHubShareClient(HttpClient http, string token, CommunityEndpoints endpoints, TimeProvider? time = null, TimeSpan? bodyTimeout = null)
{
    private const long ApiBodyCap = 1024 * 1024;
    private const long IndexBodyCap = 16 * 1024 * 1024;
    private const long EntryBodyCap = 64 * 1024;
    private const int EntryChangesCap = 400;
    private const int PullsScanned = 1; // pages of 100: only the newest hundred pull requests are looked at
    private const int EntryFilesPerPull = 3; // the share limit
    private static readonly TimeSpan DefaultBodyTimeout = TimeSpan.FromSeconds(30);
    private const int PerPage = 100;
    private const int MaxPages = 10;
    private const int UploadBuffer = 64 * 1024;
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    // ---- identity and repositories ----------------------------------------------------------------------------

    /// <summary>The signed-in account.</summary>
    public async Task<GitHubUser> GetViewerAsync(CancellationToken ct)
    {
        var reply = await SendAsync(HttpMethod.Get, Api("user"), null, ct).ConfigureAwait(false);
        return Parse(reply, e => new GitHubUser(e.GetProperty("id").GetInt64(), Str(e, "login"),
            DateTimeOffset.Parse(Str(e, "created_at"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)));
    }

    /// <summary>A repository, or <see langword="null"/> when it does not exist.</summary>
    public async Task<GitHubRepo?> GetRepoAsync(string owner, string name, CancellationToken ct)
    {
        var reply = await SendAsync(HttpMethod.Get, Api("repos", owner, name), null, ct, HttpStatusCode.NotFound).ConfigureAwait(false);
        return reply.Status == HttpStatusCode.NotFound ? null : Parse(reply, Repo);
    }

    /// <summary>Makes a public repository for the signed-in account, with a first commit so it has a branch.</summary>
    public async Task<GitHubRepo> CreateRepoAsync(string name, string description, CancellationToken ct)
    {
        var body = new JsonObject { ["name"] = name, ["description"] = description, ["private"] = false, ["auto_init"] = true };
        var reply = await SendAsync(HttpMethod.Post, Api("user", "repos"), Json(body), ct).ConfigureAwait(false);
        return Parse(reply, Repo);
    }

    /// <summary>Every item of a branch's tree, recursively.</summary>
    public async Task<IReadOnlyList<GitHubTreeItem>> GetTreeAsync(string owner, string repo, string branch, CancellationToken ct)
    {
        var reply = await SendAsync(HttpMethod.Get, Api(Join("repos", owner, repo, "git", "trees") + "/" + Seg(branch) + "?recursive=1"), null, ct).ConfigureAwait(false);
        return Parse<IReadOnlyList<GitHubTreeItem>>(reply, e => e.GetProperty("tree").EnumerateArray()
            .Select(t => new GitHubTreeItem(Str(t, "path"), Str(t, "type"), Str(t, "sha"),
                t.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt64() : null))
            .ToList());
    }

    /// <summary>Branch names.</summary>
    public Task<IReadOnlyList<string>> ListBranchesAsync(string owner, string repo, CancellationToken ct) =>
        PagedAsync(Join("repos", owner, repo, "branches"), e => Str(e, "name"), ct);

    /// <summary>Deletes a branch.</summary>
    public async Task DeleteBranchAsync(string owner, string repo, string branch, CancellationToken ct) =>
        await SendAsync(HttpMethod.Delete, Api(Join("repos", owner, repo, "git", "refs", "heads") + "/" + Ref(branch)), null, ct).ConfigureAwait(false);

    /// <summary>The tip commit of a branch.</summary>
    public async Task<string> GetBranchShaAsync(string owner, string repo, string branch, CancellationToken ct)
    {
        var reply = await SendAsync(HttpMethod.Get, Api(Join("repos", owner, repo, "git", "ref", "heads") + "/" + Ref(branch)), null, ct).ConfigureAwait(false);
        return Parse(reply, e => Str(e.GetProperty("object"), "sha"));
    }

    /// <summary>Makes a branch at a commit.</summary>
    public async Task CreateBranchAsync(string owner, string repo, string branch, string fromSha, CancellationToken ct)
    {
        var body = new JsonObject { ["ref"] = "refs/heads/" + branch, ["sha"] = fromSha };
        await SendAsync(HttpMethod.Post, Api("repos", owner, repo, "git", "refs"), Json(body), ct).ConfigureAwait(false);
    }

    // ---- files -------------------------------------------------------------------------------------------------

    /// <summary>A file's bytes and blob SHA on a branch, or <see langword="null"/> when it is not there.</summary>
    public async Task<(byte[] Content, string Sha)?> GetFileAsync(string owner, string repo, string path, string branch, CancellationToken ct)
    {
        var url = Api(Join("repos", owner, repo, "contents") + "/" + FilePath(path) + "?ref=" + Uri.EscapeDataString(branch));
        var reply = await SendAsync(HttpMethod.Get, url, null, ct, HttpStatusCode.NotFound).ConfigureAwait(false);
        if (reply.Status == HttpStatusCode.NotFound) return null;
        return Parse<(byte[], string)?>(reply, e => (Convert.FromBase64String(Str(e, "content")), Str(e, "sha")));
    }

    /// <summary>Creates a file, or replaces it when <paramref name="sha"/> names the version being replaced.</summary>
    public async Task PutFileAsync(string owner, string repo, string path, byte[] content, string message, string? sha, string branch, CancellationToken ct)
    {
        var body = new JsonObject { ["message"] = message, ["content"] = Convert.ToBase64String(content), ["branch"] = branch };
        if (sha is not null) body["sha"] = sha;
        await SendAsync(HttpMethod.Put, Api(Join("repos", owner, repo, "contents") + "/" + FilePath(path)), Json(body), ct).ConfigureAwait(false);
    }

    /// <summary>Deletes a file at the blob SHA given.</summary>
    public async Task DeleteFileAsync(string owner, string repo, string path, string sha, string message, string branch, CancellationToken ct)
    {
        var body = new JsonObject { ["message"] = message, ["sha"] = sha, ["branch"] = branch };
        await SendAsync(HttpMethod.Delete, Api(Join("repos", owner, repo, "contents") + "/" + FilePath(path)), Json(body), ct).ConfigureAwait(false);
    }

    // ---- releases and tags -------------------------------------------------------------------------------------

    /// <summary>The repository's releases with their files.</summary>
    public Task<IReadOnlyList<GitHubRelease>> ListReleasesAsync(string owner, string repo, CancellationToken ct) =>
        PagedAsync(Join("repos", owner, repo, "releases"), e => Release(e, owner, repo), ct);

    /// <summary>Makes a published release on a new tag.</summary>
    public async Task<GitHubRelease> CreateReleaseAsync(string owner, string repo, string tag, string name, CancellationToken ct)
    {
        var body = new JsonObject { ["tag_name"] = tag, ["name"] = name, ["draft"] = false, ["prerelease"] = false };
        var reply = await SendAsync(HttpMethod.Post, Api("repos", owner, repo, "releases"), Json(body), ct).ConfigureAwait(false);
        return Parse(reply, e => Release(e, owner, repo));
    }

    /// <summary>
    /// Attaches a file to a release, streamed: the content is read from <paramref name="content"/> as it is sent, never
    /// buffered, with <c>Content-Length</c> stated up front. Progress is the number of bytes sent so far.
    /// </summary>
    public async Task UploadAssetAsync(GitHubRelease release, string name, Stream content, long length, IProgress<long>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        var url = new Uri(endpoints.UploadsBase, Join("repos", release.Owner, release.Repo, "releases", release.Id.ToString(CultureInfo.InvariantCulture), "assets")
            + "?name=" + Uri.EscapeDataString(name));
        using var body = new StreamContent(new CountingStream(content, length, progress), UploadBuffer);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        body.Headers.ContentLength = length;
        await SendAsync(HttpMethod.Post, url, body, ct).ConfigureAwait(false);
    }

    /// <summary>Deletes a release (its tag stays; see <see cref="DeleteTagAsync"/>).</summary>
    public async Task DeleteReleaseAsync(string owner, string repo, long id, CancellationToken ct) =>
        await SendAsync(HttpMethod.Delete, Api("repos", owner, repo, "releases", id.ToString(CultureInfo.InvariantCulture)), null, ct).ConfigureAwait(false);

    /// <summary>Tag names.</summary>
    public Task<IReadOnlyList<string>> ListTagsAsync(string owner, string repo, CancellationToken ct) =>
        PagedAsync(Join("repos", owner, repo, "tags"), e => Str(e, "name"), ct);

    /// <summary>Deletes a tag.</summary>
    public async Task DeleteTagAsync(string owner, string repo, string tag, CancellationToken ct) =>
        await SendAsync(HttpMethod.Delete, Api(Join("repos", owner, repo, "git", "refs", "tags") + "/" + Ref(tag)), null, ct).ConfigureAwait(false);

    // ---- the gallery -------------------------------------------------------------------------------------------

    /// <summary>
    /// Forks the gallery into the signed-in account. GitHub answers with the existing fork when there already is one;
    /// a new fork may take a moment to become readable.
    /// </summary>
    public async Task<GitHubRepo> ForkAsync(string galleryRepo, CancellationToken ct)
    {
        var (owner, name) = SplitFullName(galleryRepo);
        var reply = await SendAsync(HttpMethod.Post, Api("repos", owner, name, "forks"), Json(new JsonObject()), ct).ConfigureAwait(false);
        return Parse(reply, Repo);
    }

    /// <summary>Opens a pull request into the gallery's default branch; <paramref name="head"/> is <c>owner:branch</c>.</summary>
    public async Task<int> OpenPullAsync(string galleryRepo, string head, string title, string body, CancellationToken ct)
    {
        var (owner, name) = SplitFullName(galleryRepo);
        var gallery = await GetRepoAsync(owner, name, ct).ConfigureAwait(false) ?? throw new CommunityException(ShareRules.GitHubRefused);
        var json = new JsonObject { ["title"] = title, ["head"] = head, ["base"] = gallery.DefaultBranch, ["body"] = body };
        var reply = await SendAsync(HttpMethod.Post, Api("repos", owner, name, "pulls"), Json(json), ct).ConfigureAwait(false);
        return Parse(reply, e => e.GetProperty("number").GetInt32());
    }

    /// <summary>A pull request's state.</summary>
    public async Task<GitHubPull> GetPullAsync(string galleryRepo, int number, CancellationToken ct)
    {
        var (owner, name) = SplitFullName(galleryRepo);
        var reply = await SendAsync(HttpMethod.Get, Api("repos", owner, name, "pulls", Num(number)), null, ct).ConfigureAwait(false);
        return Parse(reply, e => new GitHubPull(Str(e, "state"),
            e.TryGetProperty("merged", out var m) && m.ValueKind == JsonValueKind.True));
    }

    /// <summary>The text of a pull request's conversation comments (where the gallery's checks answer).</summary>
    public Task<IReadOnlyList<string>> GetPullCommentsAsync(string galleryRepo, int number, CancellationToken ct)
    {
        var (owner, name) = SplitFullName(galleryRepo);
        return PagedAsync(Join("repos", owner, name, "issues", Num(number), "comments"), e => Str(e, "body"), ct);
    }

    /// <summary>The published list, fetched from the gallery's site WITHOUT the token.</summary>
    /// <exception cref="InvalidDataException">The list is damaged.</exception>
    /// <exception cref="UnsupportedSchemaVersionException">The list is newer or older than this build reads.</exception>
    public async Task<CommunityIndex> GetIndexAsync(CancellationToken ct)
    {
        var reply = await SendAsync(HttpMethod.Get, endpoints.IndexUrl, null, ct, maxBody: IndexBodyCap).ConfigureAwait(false);
        using var stream = new MemoryStream(reply.Body);
        return CommunityIndex.Read(stream);
    }

    /// <summary>
    /// The entries carried by the gallery's open pull requests (added or changed <c>entries/**/*.json</c> files, read at
    /// each request's head commit through the API). Files that don't parse as an entry are skipped.
    /// </summary>
    public async Task<IReadOnlyList<CommunityEntry>> ListOpenPullEntriesAsync(CancellationToken ct)
    {
        var (galleryOwner, galleryName) = SplitFullName(endpoints.GalleryRepo);
        var pulls = await PagedAsync(Join("repos", galleryOwner, galleryName, "pulls"), e => new
        {
            Number = e.GetProperty("number").GetInt32(),
            Sha = Str(e.GetProperty("head"), "sha"),
            HeadRepo = e.GetProperty("head").TryGetProperty("repo", out var r) && r.ValueKind == JsonValueKind.Object ? Str(r, "full_name") : null,
        }, ct, "state=open", PullsScanned).ConfigureAwait(false);

        var entries = new List<CommunityEntry>();
        foreach (var pull in pulls)
        {
            if (pull.HeadRepo is null) continue;
            var (headOwner, headName) = SplitFullName(pull.HeadRepo);
            var files = await PagedAsync(Join("repos", galleryOwner, galleryName, "pulls", Num(pull.Number), "files"),
                e => (Name: Str(e, "filename"), Status: Str(e, "status"),
                    Changes: e.TryGetProperty("changes", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt64() : 0),
                ct, maxPages: 1).ConfigureAwait(false);
            // Anyone can open a pull request, so what is read from one is bounded: a few files, none of them huge.
            var wanted = files
                .Where(f => f.Status is "added" or "modified" && f.Changes <= EntryChangesCap
                    && f.Name.StartsWith("entries/", StringComparison.Ordinal) && f.Name.EndsWith(".json", StringComparison.Ordinal))
                .Take(EntryFilesPerPull);
            foreach (var file in wanted)
            {
                try
                {
                    var url = Api(Join("repos", headOwner, headName, "contents") + "/" + FilePath(file.Name) + "?ref=" + Uri.EscapeDataString(pull.Sha));
                    var reply = await SendAsync(HttpMethod.Get, url, null, ct, HttpStatusCode.NotFound,
                        accept: "application/vnd.github.raw+json", maxBody: EntryBodyCap).ConfigureAwait(false);
                    if (reply.Status != HttpStatusCode.NotFound && TryReadEntry(reply.Body) is { } entry) entries.Add(entry);
                }
                // A file that can't be fetched is skipped like one that doesn't parse. A refused token
                // (GitHubAuthException) and a rate limit end the scan: nothing after them would answer either.
                catch (CommunityException ex) when (ex is not RateLimitedException) { }
                catch (ArgumentException) { }
            }
        }
        return entries;
    }

    private static CommunityEntry? TryReadEntry(byte[] bytes)
    {
        try
        {
            var entry = JsonSerializer.Deserialize<CommunityEntry>(bytes, Nfty.Core.Formats.Json.Options);
            if (entry is null) return null;
            CommunitySchema.Require(entry.SchemaVersion);
            CommunitySchema.Verify([entry]);
            return entry;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or UnsupportedSchemaVersionException) { return null; }
    }

    // ---- transport ---------------------------------------------------------------------------------------------

    private sealed record Reply(HttpStatusCode Status, byte[] Body);

    private async Task<Reply> SendAsync(HttpMethod method, Uri url, HttpContent? content, CancellationToken ct,
        HttpStatusCode? tolerate = null, string accept = "application/vnd.github+json", long maxBody = ApiBodyCap)
    {
        using var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.UserAgent.ParseAdd("nfty");
        request.Headers.Accept.ParseAdd(accept);
        if (SendsToken(url))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        }
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode && response.StatusCode != tolerate) throw Refusal(response);
            return new Reply(response.StatusCode, await ReadCappedAsync(response.Content, maxBody, ct).ConfigureAwait(false));
        }
        catch (HttpRequestException) { throw new CommunityException(ShareRules.Unreachable); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new CommunityException(ShareRules.TimedOut); }
    }

    // HttpClient.Timeout stops at the headers (ResponseHeadersRead), so the body gets its own deadline, and a size cap
    // that is checked as bytes arrive: an endless or enormous answer is refused after `max` bytes, not buffered.
    private async Task<byte[]> ReadCappedAsync(HttpContent content, long max, CancellationToken ct)
    {
        if (content.Headers.ContentLength is { } declared && declared > max) throw new CommunityException(ShareRules.GitHubTooMuch);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(bodyTimeout ?? DefaultBodyTimeout);
        using var source = await content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
        using var all = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int n;
        while ((n = await source.ReadAsync(chunk, deadline.Token).ConfigureAwait(false)) > 0)
        {
            if (all.Length + n > max) throw new CommunityException(ShareRules.GitHubTooMuch);
            all.Write(chunk, 0, n);
        }
        return all.ToArray();
    }

    // The token is for GitHub's own API and uploads hosts, nothing else (not the Pages list, not a raw or download host).
    private bool SendsToken(Uri url) =>
        SameOrigin(url, endpoints.ApiBase) || SameOrigin(url, endpoints.UploadsBase);

    private static bool SameOrigin(Uri a, Uri b) =>
        a.Scheme == b.Scheme && a.Port == b.Port && string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase);

    private Exception Refusal(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized) return new GitHubAuthException(ShareRules.Reconnect);
        bool limited = response.StatusCode == (HttpStatusCode)429
            || (response.StatusCode == HttpStatusCode.Forbidden && Header(response, "x-ratelimit-remaining") == "0");
        return limited
            ? new RateLimitedException(ShareRules.RateLimited(MinutesToWait(response)))
            : new CommunityException(ShareRules.GitHubRefused);
    }

    private int MinutesToWait(HttpResponseMessage response)
    {
        double seconds = 60;
        if (long.TryParse(Header(response, "x-ratelimit-reset"), NumberStyles.None, CultureInfo.InvariantCulture, out long reset))
            seconds = reset - _time.GetUtcNow().ToUnixTimeSeconds();
        else if (long.TryParse(Header(response, "retry-after"), NumberStyles.None, CultureInfo.InvariantCulture, out long retry))
            seconds = retry;
        return (int)Math.Clamp(Math.Ceiling(seconds / 60), 1, 24 * 60);
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    // ---- paths -------------------------------------------------------------------------------------------------

    private Uri Api(params string[] segments) => Api(Join(segments));

    private Uri Api(string pathAndQuery) => new(endpoints.ApiBase.ToString().TrimEnd('/') + "/" + pathAndQuery);

    private static string Join(params string[] segments) => string.Join('/', segments.Select(Seg));

    // One path segment, escaped. A dot segment would climb out of the intended path, so it is refused.
    private static string Seg(string s)
    {
        if (string.IsNullOrEmpty(s) || s is "." or "..") throw new ArgumentException("A path segment can't be empty or a dot segment.", nameof(s));
        return Uri.EscapeDataString(s);
    }

    private static string FilePath(string path) => string.Join('/', path.Split('/').Select(Seg));

    private static string Ref(string name) => FilePath(name);

    private static string Num(long n) => n.ToString(CultureInfo.InvariantCulture);

    private static (string Owner, string Name) SplitFullName(string full)
    {
        var parts = full.Split('/');
        if (parts.Length != 2) throw new ArgumentException("Expected owner/name.", nameof(full));
        return (parts[0], parts[1]);
    }

    // ---- JSON --------------------------------------------------------------------------------------------------

    private static StringContent Json(JsonNode body) => new(body.ToJsonString(), Encoding.UTF8, "application/json");

    private static T Parse<T>(Reply reply, Func<JsonElement, T> read)
    {
        try
        {
            using var doc = JsonDocument.Parse(reply.Body);
            return read(doc.RootElement);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            throw new CommunityException(ShareRules.GitHubUnexpected);
        }
    }

    private async Task<IReadOnlyList<T>> PagedAsync<T>(string path, Func<JsonElement, T> map, CancellationToken ct, string? query = null, int maxPages = MaxPages)
    {
        var all = new List<T>();
        for (int page = 1; page <= maxPages; page++)
        {
            var url = Api(path + "?" + (query is null ? "" : query + "&") + $"per_page={PerPage}&page={page}");
            var reply = await SendAsync(HttpMethod.Get, url, null, ct).ConfigureAwait(false);
            int count = Parse(reply, e =>
            {
                int n = 0;
                foreach (var item in e.EnumerateArray()) { all.Add(map(item)); n++; }
                return n;
            });
            if (count < PerPage) break;
        }
        return all;
    }

    private static string Str(JsonElement e, string name) =>
        e.GetProperty(name).GetString() ?? throw new InvalidOperationException(name);

    private static GitHubRepo Repo(JsonElement e) => new(Str(e, "full_name"), e.GetProperty("private").GetBoolean(),
        Str(e, "default_branch"), e.GetProperty("size").GetInt64());

    private static GitHubRelease Release(JsonElement e, string owner, string repo) => new(
        e.GetProperty("id").GetInt64(), Str(e, "tag_name"),
        e.GetProperty("assets").EnumerateArray()
            .Select(a => new GitHubAsset(a.GetProperty("id").GetInt64(), Str(a, "name"), a.GetProperty("size").GetInt64())).ToList(),
        owner, repo);

    // Hands at most `length` bytes on, counts them for progress, and complains when the source runs dry early so a
    // short file is never sent as if whole.
    private sealed class CountingStream(Stream inner, long length, IProgress<long>? progress) : Stream
    {
        private long _sent;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => _sent; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            int want = (int)Math.Min(buffer.Length, length - _sent);
            if (want == 0) return 0;
            return Account(inner.Read(buffer[..want]));
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int want = (int)Math.Min(buffer.Length, length - _sent);
            if (want == 0) return 0;
            return Account(await inner.ReadAsync(buffer[..want], cancellationToken).ConfigureAwait(false));
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        private int Account(int read)
        {
            if (read == 0) throw new EndOfStreamException("The file ended before its stated length.");
            _sent += read;
            progress?.Report(_sent);
            return read;
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
