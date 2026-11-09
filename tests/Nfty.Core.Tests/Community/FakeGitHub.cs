using System.Net;
using System.Text;

namespace Nfty.Core.Tests.Community;

/// <summary>
/// A scripted <see cref="HttpMessageHandler"/> standing in for GitHub. Replies are queued per (method, path) and
/// served in order; every request is recorded (method, full URL, headers, body); an unscripted request fails loudly.
/// </summary>
internal sealed class FakeGitHub : HttpMessageHandler
{
    /// <summary>One request as it arrived.</summary>
    public sealed record Recorded(HttpMethod Method, Uri Url, IReadOnlyDictionary<string, string> Headers, string Body, byte[] BodyBytes)
    {
        /// <summary>The URL's path, without host or query.</summary>
        public string Path => Url.AbsolutePath;
    }

    private readonly Dictionary<(string Method, string Path), Queue<Func<HttpRequestMessage, HttpResponseMessage>>> _routes = new();
    private readonly object _gate = new();
    private readonly List<Recorded> _requests = [];

    /// <summary>Every request seen so far, in arrival order.</summary>
    public IReadOnlyList<Recorded> Requests { get { lock (_gate) return _requests.ToArray(); } }

    /// <summary>A client whose traffic goes to this fake.</summary>
    public HttpClient Client() => new(this, disposeHandler: false);

    /// <summary>Queues one reply for <paramref name="method"/> + <paramref name="path"/> (path only, no query).</summary>
    public FakeGitHub On(HttpMethod method, string path, HttpStatusCode status, string json,
        IReadOnlyDictionary<string, string>? headers = null)
        => On(method, path, _ => Make(status, json, headers));

    /// <summary>Queues one reply with a raw byte body.</summary>
    public FakeGitHub OnBytes(HttpMethod method, string path, HttpStatusCode status, byte[] body,
        IReadOnlyDictionary<string, string>? headers = null)
        => On(method, path, _ =>
        {
            var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(body) };
            AddHeaders(response, headers);
            return response;
        });

    /// <summary>Fails the test when a queued reply was never asked for.</summary>
    public void AssertAllConsumed()
    {
        string[] left;
        lock (_gate)
            left = _routes.Where(r => r.Value.Count > 0).Select(r => $"{r.Key.Method} {r.Key.Path} x{r.Value.Count}").ToArray();
        if (left.Length > 0) Xunit.Assert.Fail("FakeGitHub: scripted replies never used: " + string.Join(", ", left));
    }

    /// <summary>Queues one computed reply for <paramref name="method"/> + <paramref name="path"/>.</summary>
    public FakeGitHub On(HttpMethod method, string path, Func<HttpRequestMessage, HttpResponseMessage> reply)
    {
        lock (_gate)
        {
            var key = (method.Method, path);
            if (!_routes.TryGetValue(key, out var queue)) _routes[key] = queue = new();
            queue.Enqueue(reply);
        }
        return this;
    }

    /// <summary>Queues an HTTP 200 reply.</summary>
    public FakeGitHub Ok(HttpMethod method, string path, string json) => On(method, path, HttpStatusCode.OK, json);

    /// <summary>Requests to one path, in order.</summary>
    public IReadOnlyList<Recorded> To(string path) => Requests.Where(r => r.Path == path).ToArray();

    private static HttpResponseMessage Make(HttpStatusCode status, string json, IReadOnlyDictionary<string, string>? headers)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        AddHeaders(response, headers);
        return response;
    }

    private static void AddHeaders(HttpResponseMessage response, IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null) return;
        foreach (var (name, value) in headers)
            if (!response.Headers.TryAddWithoutValidation(name, value))
                response.Content!.Headers.TryAddWithoutValidation(name, value);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        byte[] bytes = [];
        if (request.Content is not null)
        {
            // Read the way a socket would: in 64 KB chunks, never asking the content to buffer itself.
            using var source = await request.Content.ReadAsStreamAsync(token);
            using var all = new MemoryStream();
            var chunk = new byte[64 * 1024];
            int n;
            while ((n = await source.ReadAsync(chunk, token)) > 0) all.Write(chunk, 0, n);
            bytes = all.ToArray();
        }
        string body = Encoding.UTF8.GetString(bytes);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in request.Headers) headers[h.Key] = string.Join(", ", h.Value);
        if (request.Content is not null)
            foreach (var h in request.Content.Headers) headers[h.Key] = string.Join(", ", h.Value);

        Func<HttpRequestMessage, HttpResponseMessage>? reply = null;
        lock (_gate)
        {
            _requests.Add(new Recorded(request.Method, request.RequestUri!, headers, body, bytes));
            if (_routes.TryGetValue((request.Method.Method, request.RequestUri!.AbsolutePath), out var queue) && queue.Count > 0)
                reply = queue.Dequeue();
        }
        if (reply is null)
            throw new InvalidOperationException($"FakeGitHub: unscripted request {request.Method} {request.RequestUri}");
        return reply(request);
    }
}
