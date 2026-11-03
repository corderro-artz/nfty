using System.Net;
using System.Net.Sockets;
using System.Text;
using Nfty.Core.Publish.Ipfs;

namespace Nfty.Core.Tests;

/// <summary>
/// The two pinning clients against a fake HTTP handler: the shape of every request, how the CID is
/// read back, how failures are worded, and that a cancel stops an upload. No network is touched.
/// </summary>
public class IpfsPinningTests
{
    private sealed record Seen(HttpMethod Method, Uri Uri, string? Auth, string? ContentType, long? Length, byte[] Body);

    private sealed class FakeHandler(Func<Seen, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<Seen> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            byte[] body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            var seen = new Seen(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(),
                request.Content?.Headers.ContentType?.MediaType, request.Content?.Headers.ContentLength, body);
            Requests.Add(seen);
            cancellationToken.ThrowIfCancellationRequested();
            return answer(seen);
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string Folder(params (string Rel, string Text)[] files)
    {
        var dir = Directory.CreateTempSubdirectory("nfty-pin-").FullName;
        foreach (var (rel, text) in files)
        {
            var path = Path.Combine(dir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
        return dir;
    }

    private const string WrapperCid = "bafybeigdyrzt5sfp7udm7hu76uh7y26nf3efuylqabf3oclgtqy55fbzdi";
    private const string FileCid = "bafkreihdwdcefgh4dqkjv67uzcmw7ojee6xedzdetojuzjevtenxquvyku";

    // ------------------------------------------------------------------ Kubo

    [Fact]
    public async Task Kubo_status_posts_version_and_id_and_reports_connected()
    {
        var handler = new FakeHandler(s => s.Uri.AbsolutePath.EndsWith("/version", StringComparison.Ordinal)
            ? Json("""{"Version":"0.29.0","Commit":"x"}""")
            : Json("""{"ID":"12D3KooWpeer","AgentVersion":"kubo/0.29.0/"}"""));
        var kubo = new KuboPinning(new HttpClient(handler), KuboPinning.DefaultApi);

        var status = await kubo.StatusAsync();

        Assert.True(status.Reachable);
        Assert.Equal("connected · kubo 0.29.0", status.Summary);
        Assert.Equal("0.29.0", status.Version);
        Assert.Equal("12D3KooWpeer", status.PeerId);
        Assert.All(handler.Requests, r => Assert.Equal(HttpMethod.Post, r.Method));
        Assert.Equal("http://127.0.0.1:5001/api/v0/version", handler.Requests[0].Uri.ToString());
        Assert.Equal("http://127.0.0.1:5001/api/v0/id", handler.Requests[1].Uri.ToString());
    }

    [Fact]
    public async Task Kubo_status_names_what_to_start_when_nothing_listens()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("refused",
            new SocketException((int)SocketError.ConnectionRefused)));
        var status = await new KuboPinning(new HttpClient(handler), KuboPinning.DefaultApi).StatusAsync();

        Assert.False(status.Reachable);
        Assert.Equal("not reachable", status.Summary);
        Assert.Contains("Start IPFS Desktop or Kubo", status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Kubo_status_on_the_gateway_port_says_so()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.MethodNotAllowed));
        var status = await new KuboPinning(new HttpClient(handler), "http://127.0.0.1:8080").StatusAsync();
        Assert.False(status.Reachable);
        Assert.Contains("405", status.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("127.0.0.1:5001")]
    [InlineData("ftp://127.0.0.1:5001")]
    public void A_bad_api_address_is_refused_by_name(string api)
    {
        Assert.NotNull(KuboPinning.ApiProblem(api));
        Assert.Throws<ArgumentException>(() => new KuboPinning(new HttpClient(), api));
    }

    [Fact]
    public async Task Kubo_add_wraps_the_folder_contents_and_returns_the_wrapper_cid()
    {
        var dir = Folder(("1.png", "one"), ("2.png", "two"));
        var handler = new FakeHandler(_ => Json(
            $"{{\"Name\":\"1.png\",\"Hash\":\"{FileCid}\",\"Size\":\"11\"}}\n"
            + $"{{\"Name\":\"2.png\",\"Hash\":\"{FileCid}\",\"Size\":\"11\"}}\n"
            + $"{{\"Name\":\"\",\"Hash\":\"{WrapperCid}\",\"Size\":\"100\"}}\n"));
        var kubo = new KuboPinning(new HttpClient(handler), KuboPinning.DefaultApi);

        var cid = await kubo.AddFolderAsync(dir, "images");

        Assert.Equal(WrapperCid, cid);
        var req = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("/api/v0/add", req.Uri.AbsolutePath);
        Assert.Contains("wrap-with-directory=true", req.Uri.Query, StringComparison.Ordinal);
        Assert.Contains("cid-version=1", req.Uri.Query, StringComparison.Ordinal);
        Assert.Contains("pin=true", req.Uri.Query, StringComparison.Ordinal);
        Assert.Equal("multipart/form-data", req.ContentType);
        Assert.Equal(req.Body.Length, req.Length);   // the computed length is the real one
        string body = Encoding.UTF8.GetString(req.Body);
        Assert.Contains("name=\"file\"; filename=\"1.png\"", body, StringComparison.Ordinal);
        Assert.Contains("name=\"file\"; filename=\"2.png\"", body, StringComparison.Ordinal);
        Assert.Contains("\r\none\r\n", body, StringComparison.Ordinal);
        Assert.DoesNotContain("x-directory", body, StringComparison.Ordinal);   // flat: no directory parts
    }

    [Fact]
    public async Task Kubo_add_sends_each_nested_folder_once_before_its_files()
    {
        var dir = Folder(("a/b/1.txt", "x"), ("a/2.txt", "y"));
        var handler = new FakeHandler(_ => Json($"{{\"Name\":\"\",\"Hash\":\"{WrapperCid}\"}}"));
        await new KuboPinning(new HttpClient(handler), KuboPinning.DefaultApi).AddFolderAsync(dir, "x");

        string body = Encoding.UTF8.GetString(handler.Requests[0].Body);
        int a = body.IndexOf("filename=\"a\"\r\nContent-Type: application/x-directory", StringComparison.Ordinal);
        int ab = body.IndexOf("filename=\"a/b\"\r\nContent-Type: application/x-directory", StringComparison.Ordinal);
        int file = body.IndexOf("filename=\"a/b/1.txt\"", StringComparison.Ordinal);
        Assert.True(a >= 0 && ab > a && file > ab);
        Assert.Equal(1, CountOf(body, "filename=\"a\"\r\n"));
    }

    private static int CountOf(string s, string what)
    {
        int n = 0, i = 0;
        while ((i = s.IndexOf(what, i, StringComparison.Ordinal)) >= 0) { n++; i += what.Length; }
        return n;
    }

    [Fact]
    public async Task Kubo_add_failure_is_worded_from_the_nodes_message()
    {
        var dir = Folder(("1.png", "one"));
        var handler = new FakeHandler(_ => Json("""{"Message":"repo is full","Code":0,"Type":"error"}""",
            HttpStatusCode.InternalServerError));
        var ex = await Assert.ThrowsAsync<PinningException>(() =>
            new KuboPinning(new HttpClient(handler), KuboPinning.DefaultApi).AddFolderAsync(dir, "x"));
        Assert.Contains("repo is full", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_answer_without_a_usable_cid_is_refused_rather_than_written_into_metadata()
    {
        var dir = Folder(("1.png", "one"));
        var handler = new FakeHandler(_ => Json("""{"Name":"","Hash":"not a cid/../x"}"""));
        var ex = await Assert.ThrowsAsync<PinningException>(() =>
            new KuboPinning(new HttpClient(handler), KuboPinning.DefaultApi).AddFolderAsync(dir, "x"));
        Assert.Contains("no usable CID", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_empty_folder_is_refused_before_any_request()
    {
        var dir = Folder();
        var handler = new FakeHandler(_ => Json("{}"));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new KuboPinning(new HttpClient(handler), KuboPinning.DefaultApi).AddFolderAsync(dir, "x"));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_canceled_upload_stops()
    {
        var dir = Folder(("1.png", "one"));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var handler = new FakeHandler(_ => Json($"{{\"Name\":\"\",\"Hash\":\"{WrapperCid}\"}}"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new KuboPinning(new HttpClient(handler), KuboPinning.DefaultApi).AddFolderAsync(dir, "x", null, cts.Token));
    }

    [Fact]
    public async Task Upload_progress_ends_at_the_whole_body()
    {
        var dir = Folder(("1.png", new string('x', 200_000)));
        var handler = new FakeHandler(_ => Json($"{{\"Name\":\"\",\"Hash\":\"{WrapperCid}\"}}"));
        var reports = new List<PinProgress>();
        await new KuboPinning(new HttpClient(handler), KuboPinning.DefaultApi)
            .AddFolderAsync(dir, "x", new Sync<PinProgress>(reports.Add));
        Assert.NotEmpty(reports);
        Assert.Equal(1.0, reports[^1].Fraction);
        Assert.Equal(handler.Requests[0].Body.Length, reports[^1].BytesTotal);
    }

    private sealed class Sync<T>(Action<T> a) : IProgress<T> { public void Report(T v) => a(v); }

    // ------------------------------------------------------------------ Pinata

    private const string Jwt = "eyJhbGciOiJIUzI1NiJ9.secret-payload.sig";

    [Fact]
    public async Task Pinata_status_sends_the_key_as_a_bearer_token()
    {
        var handler = new FakeHandler(_ => Json("""{"message":"Congratulations!"}"""));
        var status = await new PinataPinning(new HttpClient(handler), Jwt).StatusAsync();

        Assert.True(status.Reachable);
        Assert.Equal("connected · pinata", status.Summary);
        var req = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, req.Method);
        Assert.Equal("https://api.pinata.cloud/data/testAuthentication", req.Uri.ToString());
        Assert.Equal("Bearer " + Jwt, req.Auth);
    }

    [Fact]
    public async Task A_refused_pinata_key_is_reported_without_quoting_it()
    {
        var handler = new FakeHandler(_ => Json("""{"error":"Invalid token"}""", HttpStatusCode.Unauthorized));
        var status = await new PinataPinning(new HttpClient(handler), Jwt).StatusAsync();
        Assert.False(status.Reachable);
        Assert.Equal("key refused", status.Summary);
        Assert.DoesNotContain("secret-payload", status.Detail, StringComparison.Ordinal);

        var dir = Folder(("1.png", "one"));
        var ex = await Assert.ThrowsAsync<PinningException>(() =>
            new PinataPinning(new HttpClient(handler), Jwt).AddFolderAsync(dir, "images"));
        Assert.DoesNotContain("secret-payload", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pinata_add_shares_one_leading_folder_and_does_not_wrap_again()
    {
        var dir = Folder(("1.png", "one"), ("2.png", "two"));
        var handler = new FakeHandler(_ => Json($"{{\"IpfsHash\":\"{WrapperCid}\",\"PinSize\":10}}"));

        var cid = await new PinataPinning(new HttpClient(handler), Jwt).AddFolderAsync(dir, "Chest Demo-images");

        Assert.Equal(WrapperCid, cid);
        var req = Assert.Single(handler.Requests);
        Assert.Equal("https://api.pinata.cloud/pinning/pinFileToIPFS", req.Uri.ToString());
        Assert.Equal("Bearer " + Jwt, req.Auth);
        Assert.Equal(req.Body.Length, req.Length);
        string body = Encoding.UTF8.GetString(req.Body);
        Assert.Contains("name=\"file\"; filename=\"Chest-Demo-images/1.png\"", body, StringComparison.Ordinal);
        Assert.Contains("name=\"file\"; filename=\"Chest-Demo-images/2.png\"", body, StringComparison.Ordinal);
        Assert.Contains("""{"cidVersion":1,"wrapWithDirectory":false}""", body, StringComparison.Ordinal);
        Assert.Contains("""{"name":"Chest-Demo-images"}""", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pinata_error_body_is_worded()
    {
        var dir = Folder(("1.png", "one"));
        var handler = new FakeHandler(_ => Json("""{"error":{"reason":"PAYLOAD_TOO_LARGE","details":"too big"}}""",
            HttpStatusCode.BadRequest));
        var ex = await Assert.ThrowsAsync<PinningException>(() =>
            new PinataPinning(new HttpClient(handler), Jwt).AddFolderAsync(dir, "x"));
        Assert.Contains("PAYLOAD_TOO_LARGE: too big", ex.Message, StringComparison.Ordinal);
    }
}
