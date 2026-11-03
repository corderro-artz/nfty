using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Nfty.Core.Publish.Ipfs;

/// <summary>
/// A local IPFS node — IPFS Desktop or the Kubo daemon — reached over its HTTP RPC.
/// </summary>
/// <remarks>
/// <para><b>Status</b> is <c>POST /api/v0/version</c> (and <c>/api/v0/id</c> for the peer id). The
/// RPC refuses GET, so every call is a POST; a node that answers anything at all is running.</para>
/// <para><b>Add</b> is <c>POST /api/v0/add?wrap-with-directory=true&amp;cid-version=1&amp;pin=true</c>
/// with one multipart part per file, each named by its path inside the folder. The node adds the
/// files, wraps them in one directory and pins it; the LAST line of its newline-delimited answer —
/// the one with an empty <c>Name</c> — is that wrapper, so its <c>Hash</c> is the folder's contents
/// and <c>ipfs://CID/1.png</c> resolves. Nested folders are sent as explicit directory parts, which
/// is what Kubo's multipart reader expects.</para>
/// <para>Nothing here is a secret: the RPC on <c>127.0.0.1</c> takes no key. Anyone who can reach
/// that port can already do anything to the node.</para>
/// </remarks>
public sealed class KuboPinning : IPinningService
{
    /// <summary>Where IPFS Desktop and <c>ipfs daemon</c> listen unless told otherwise.</summary>
    public const string DefaultApi = "http://127.0.0.1:5001";

    /// <summary>How long a status check waits before calling the node unreachable.</summary>
    public static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(5);

    private readonly HttpClient _http;
    private readonly Uri _api;

    /// <summary>Creates the client.</summary>
    /// <param name="http">The client to speak through. Its timeout should be infinite: an upload of
    /// a large collection takes as long as it takes, and the token stops it.</param>
    /// <param name="api">The RPC address, e.g. <see cref="DefaultApi"/>.</param>
    /// <exception cref="ArgumentException">The address is not an absolute http(s) URL.</exception>
    public KuboPinning(HttpClient http, string api)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (ApiProblem(api) is { } problem) throw new ArgumentException(problem, nameof(api));
        _http = http;
        _api = new Uri(api.TrimEnd('/') + "/");
    }

    /// <summary>Why a typed RPC address cannot be used, or null when it can.</summary>
    /// <param name="api">The address as typed.</param>
    /// <returns>The sentence, or null.</returns>
    public static string? ApiProblem(string? api)
    {
        if (string.IsNullOrWhiteSpace(api)) return "The IPFS API address is empty.";
        if (!Uri.TryCreate(api.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return "The IPFS API address must be an http address, like " + DefaultApi + ".";
        return null;
    }

    /// <inheritdoc />
    public string Provider => "Local IPFS";

    /// <summary>The RPC address this client talks to.</summary>
    public string Api => _api.ToString().TrimEnd('/');

    /// <inheritdoc />
    public async Task<PinningStatus> StatusAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(StatusTimeout);
        try
        {
            string? version;
            using (var response = await _http.PostAsync(new Uri(_api, "api/v0/version"), null, timeout.Token)
                       .ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                    return Unreachable($"The node at {Api} answered {(int)response.StatusCode} "
                        + $"{response.ReasonPhrase}. Is that the RPC port (usually 5001), not the gateway?");
                using var doc = JsonDocument.Parse(
                    await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
                version = Str(doc.RootElement, "Version");
            }

            string? peer = null;
            try
            {
                using var response = await _http.PostAsync(new Uri(_api, "api/v0/id"), null, timeout.Token)
                    .ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(
                        await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
                    peer = Str(doc.RootElement, "ID");
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException)
            {
                // The version answered, so the node is up; the peer id is a nicety.
            }

            string name = version is { Length: > 0 } v ? $"kubo {v}" : "kubo";
            return new PinningStatus(true, Provider, $"connected · {name}",
                $"Connected to {name} at {Api}.", version, peer);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Unreachable($"Local IPFS did not answer at {Api} within "
                + $"{StatusTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds. "
                + "Start IPFS Desktop or Kubo.");
        }
        catch (HttpRequestException ex)
        {
            return Unreachable(ex.InnerException is SocketException
                ? $"Local IPFS is not reachable at {Api}. Start IPFS Desktop or Kubo (ipfs daemon)."
                : $"Local IPFS is not reachable at {Api}: {ex.Message}");
        }
        catch (JsonException)
        {
            return Unreachable($"Something answered at {Api}, but not as an IPFS node does.");
        }
    }

    private PinningStatus Unreachable(string detail) =>
        new(false, Provider, "not reachable", detail);

    /// <inheritdoc />
    public async Task<string> AddFolderAsync(string folder, string label,
        IProgress<PinProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var files = MultipartFolderContent.FilesIn(folder);
        using var content = new MultipartFolderContent { Progress = progress };

        // Parents before children, each directory once: Kubo's reader walks the parts as a tree.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rel in files)
        {
            var parts = rel.Split('/');
            for (int i = 1; i < parts.Length; i++)
            {
                string dir = string.Join('/', parts[..i]);
                if (seen.Add(dir)) content.AddDirectory("file", Escape(dir));
            }
            content.AddFile("file", Escape(rel), Path.Combine(folder, rel));
        }

        var uri = new Uri(_api, "api/v0/add?wrap-with-directory=true&cid-version=1&pin=true&progress=false");
        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync(uri, content, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new PinningException($"Local IPFS could not be reached at {Api}: {ex.Message}", ex);
        }

        using (response)
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new PinningException($"Local IPFS refused the upload ({(int)response.StatusCode}): "
                    + ErrorText(body));
            if (response.TrailingHeaders.TryGetValues("X-Stream-Error", out var errors)
                && errors.FirstOrDefault() is { Length: > 0 } streamError)
                throw new PinningException($"Local IPFS failed partway through the upload: {streamError}");

            string? wrapper = null, last = null;
            foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                JsonElement root;
                try { using var doc = JsonDocument.Parse(line); root = doc.RootElement.Clone(); }
                catch (JsonException) { continue; }
                if (Str(root, "Message") is { Length: > 0 } message && Str(root, "Hash") is null)
                    throw new PinningException($"Local IPFS failed the upload: {message}");
                if (Str(root, "Hash") is not { Length: > 0 } hash) continue;
                last = hash;
                if (Str(root, "Name") is "" or null) wrapper = hash;
            }
            return Cid.Require(wrapper ?? last, Provider);
        }
    }

    /// <summary>Kubo reads each part's file name URL-escaped; '/' stays a separator.</summary>
    private static string Escape(string rel) =>
        string.Join('/', rel.Split('/').Select(Uri.EscapeDataString));

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static string ErrorText(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (Str(doc.RootElement, "Message") is { Length: > 0 } m) return m;
        }
        catch (JsonException) { }
        return Cid.Clip(body);
    }
}

/// <summary>What a returned CID must look like before it is written into anyone's metadata.</summary>
internal static class Cid
{
    /// <summary>Returns <paramref name="cid"/> when it is a plausible CID; throws otherwise.</summary>
    /// <param name="cid">What the service answered.</param>
    /// <param name="provider">Who answered, for the message.</param>
    /// <returns>The CID.</returns>
    /// <exception cref="PinningException">Nothing usable came back.</exception>
    public static string Require(string? cid, string provider)
    {
        // base32 CIDv1 (bafy..., bafk...) or base58 CIDv0 (Qm...): letters and digits only. Anything
        // else would be pasted into every token's metadata, so it is refused here, by name.
        if (cid is { Length: >= 16 and <= 128 } && cid.All(char.IsAsciiLetterOrDigit)) return cid;
        throw new PinningException($"{provider} finished the upload but returned no usable CID"
            + (string.IsNullOrEmpty(cid) ? "." : $" ('{Clip(cid)}')."));
    }

    /// <summary>A response body cut short enough to quote in a sentence.</summary>
    /// <param name="text">The text.</param>
    /// <returns>At most 200 characters of it.</returns>
    public static string Clip(string text)
    {
        text = text.Trim();
        return text.Length <= 200 ? text : text[..200] + "…";
    }
}
