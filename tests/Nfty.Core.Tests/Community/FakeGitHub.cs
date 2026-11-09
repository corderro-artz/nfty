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
    public sealed record Recorded(HttpMethod Method, Uri Url, IReadOnlyDictionary<string, string> Headers, string Body)
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
        if (headers is not null)
            foreach (var (name, value) in headers)
                if (!response.Headers.TryAddWithoutValidation(name, value))
                    response.Content.Headers.TryAddWithoutValidation(name, value);
        return response;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(token);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in request.Headers) headers[h.Key] = string.Join(", ", h.Value);
        if (request.Content is not null)
            foreach (var h in request.Content.Headers) headers[h.Key] = string.Join(", ", h.Value);

        Func<HttpRequestMessage, HttpResponseMessage>? reply = null;
        lock (_gate)
        {
            _requests.Add(new Recorded(request.Method, request.RequestUri!, headers, body));
            if (_routes.TryGetValue((request.Method.Method, request.RequestUri!.AbsolutePath), out var queue) && queue.Count > 0)
                reply = queue.Dequeue();
        }
        if (reply is null)
            throw new InvalidOperationException($"FakeGitHub: unscripted request {request.Method} {request.RequestUri}");
        return reply(request);
    }
}
