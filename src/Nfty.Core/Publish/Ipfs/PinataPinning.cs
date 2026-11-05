using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Nfty.Core.Publish.Ipfs;

/// <summary>
/// Pinata, the hosted pinning service, reached with the author's own API key (a JWT).
/// </summary>
/// <remarks>
/// <para><b>Status</b> is <c>GET /data/testAuthentication</c> with the key as a bearer token: 200
/// means the key works, 401 means Pinata refused it.</para>
/// <para><b>Add</b> is <c>POST /pinning/pinFileToIPFS</c> with every file as a <c>file</c> part named
/// <c>&lt;label&gt;/&lt;path&gt;</c> and <c>pinataOptions</c> of
/// <c>{"cidVersion":1,"wrapWithDirectory":false}</c>. Every part sharing ONE leading folder is how
/// Pinata recognises a directory upload; its <c>IpfsHash</c> is then that folder, whose entries are
/// the files - so <c>ipfs://CID/1.png</c> resolves, exactly as a local node's wrapper does.
/// <c>wrapWithDirectory</c> stays false because the shared folder already IS the wrapper: true would
/// add a second level and move every file to <c>ipfs://CID/&lt;label&gt;/1.png</c>.</para>
/// <para><b>The key is held only here</b>, for the life of this object, and never logged or put in
/// an exception message.</para>
/// </remarks>
public sealed class PinataPinning : IPinningService
{
    /// <summary>Pinata's API root.</summary>
    public const string DefaultApi = "https://api.pinata.cloud";

    private readonly HttpClient _http;
    private readonly string _jwt;
    private readonly Uri _api;

    /// <summary>Creates the client.</summary>
    /// <param name="http">The client to speak through; see <see cref="KuboPinning"/> about its timeout.</param>
    /// <param name="jwt">The API key's JWT.</param>
    /// <param name="api">Pinata's API root; tests point it elsewhere.</param>
    /// <exception cref="ArgumentException">The key is empty.</exception>
    public PinataPinning(HttpClient http, string jwt, string api = DefaultApi)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (string.IsNullOrWhiteSpace(jwt)) throw new ArgumentException("A Pinata key is needed.", nameof(jwt));
        _http = http;
        _jwt = jwt.Trim();
        _api = new Uri(api.TrimEnd('/') + "/");
    }

    /// <inheritdoc />
    public string Provider => "Pinata";

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, new Uri(_api, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _jwt);
        return request;
    }

    /// <inheritdoc />
    public async Task<PinningStatus> StatusAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            using var request = Request(HttpMethod.Get, "data/testAuthentication");
            using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
                return new PinningStatus(true, Provider, "connected · pinata", "Pinata accepted the key.");
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return new PinningStatus(false, Provider, "key refused",
                    $"Pinata refused the key ({(int)response.StatusCode}). Check it, or save a new one.");
            return new PinningStatus(false, Provider, "not reachable",
                $"Pinata answered {(int)response.StatusCode} {response.ReasonPhrase}.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new PinningStatus(false, Provider, "not reachable", "Pinata did not answer within 10 seconds.");
        }
        catch (HttpRequestException ex)
        {
            return new PinningStatus(false, Provider, "not reachable", $"Pinata could not be reached: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<string> AddFolderAsync(string folder, string label,
        IProgress<PinProgress>? progress = null, CancellationToken cancellationToken = default) =>
        (await AddFolderOwnedAsync(folder, label, progress, cancellationToken).ConfigureAwait(false)).Cid;

    /// <inheritdoc />
    public async Task<PinAcquisition> AddFolderOwnedAsync(string folder, string label,
        IProgress<PinProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var files = MultipartFolderContent.FilesIn(folder);
        string root = SafeLabel(label);
        using var content = new MultipartFolderContent { Progress = progress };
        foreach (var rel in files) content.AddFile("file", root + "/" + rel, Path.Combine(folder, rel));
        content.AddText("pinataOptions", """{"cidVersion":1,"wrapWithDirectory":false}""");
        content.AddText("pinataMetadata", JsonSerializer.Serialize(new Dictionary<string, string> { ["name"] = root }));

        using var request = Request(HttpMethod.Post, "pinning/pinFileToIPFS");
        request.Content = content;
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new PinningException($"Pinata could not be reached: {ex.Message}", ex);
        }

        using (response)
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new PinningException($"Pinata refused the key ({(int)response.StatusCode}). Check it in Settings, or save a new one.");
            if (!response.IsSuccessStatusCode)
                throw new PinningException($"Pinata refused the upload ({(int)response.StatusCode}): {ErrorText(body)}");
            string? hash = null;
            bool acquired = false;
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("IpfsHash", out var h) && h.ValueKind == JsonValueKind.String)
                {
                    hash = h.GetString();
                    // An omitted or malformed duplication flag is not evidence of new ownership.
                    acquired = doc.RootElement.TryGetProperty("isDuplicate", out var duplicate)
                        && duplicate.ValueKind == JsonValueKind.False;
                }
            }
            catch (JsonException) { }
            return new PinAcquisition(Cid.Require(hash, Provider), acquired);
        }
    }

    /// <summary>
    /// Unpins a folder pin. Pinata's v3 Files API addresses a pin by its id, so the id is looked up by
    /// CID (<c>GET /v3/files/public?cid=</c>) and each match deleted (<c>DELETE /v3/files/public/{id}</c>);
    /// when v3 lists nothing for it (a key without v3 scopes, or a folder pin v3 does not list), the
    /// legacy <c>DELETE /pinning/unpin/{cid}</c> is tried. Uploads stay on <c>pinFileToIPFS</c> because
    /// Pinata's v3 upload endpoint does not take folders, and a drop needs a folder's CID.
    /// </summary>
    /// <param name="cid">The root CID to unpin.</param>
    /// <param name="cancellationToken">Stops the requests.</param>
    /// <returns>True when Pinata confirmed a removal.</returns>
    public async Task<bool> RemoveAsync(string cid, CancellationToken cancellationToken = default)
    {
        Cid.Require(cid, Provider);
        bool removed = false;
        try
        {
            using var list = Request(HttpMethod.Get, "v3/files/public?cid=" + Uri.EscapeDataString(cid));
            using var listed = await _http.SendAsync(list, cancellationToken).ConfigureAwait(false);
            if (listed.IsSuccessStatusCode)
            {
                string body = await listed.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                foreach (var id in FileIds(body))
                {
                    using var delete = Request(HttpMethod.Delete, "v3/files/public/" + Uri.EscapeDataString(id));
                    using var deleted = await _http.SendAsync(delete, cancellationToken).ConfigureAwait(false);
                    removed |= deleted.IsSuccessStatusCode;
                }
            }
            if (removed) return true;
            using var unpin = Request(HttpMethod.Delete, "pinning/unpin/" + Uri.EscapeDataString(cid));
            using var unpinned = await _http.SendAsync(unpin, cancellationToken).ConfigureAwait(false);
            return unpinned.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return removed;
        }
    }

    /// <summary>The ids in a v3 List Files answer (<c>data.files[].id</c>).</summary>
    private static List<string> FileIds(string body)
    {
        var ids = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
                foreach (var f in files.EnumerateArray())
                    if (f.ValueKind == JsonValueKind.Object && f.TryGetProperty("id", out var id)
                        && id.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(id.GetString()))
                        ids.Add(id.GetString()!);
        }
        catch (JsonException) { }
        return ids;
    }

    /// <summary>The folder name every part shares: the label, with nothing that could add a level.</summary>
    private static string SafeLabel(string label)
    {
        var chars = (label ?? "").Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-').ToArray();
        var s = new string(chars).Trim('.', '-');
        return s.Length == 0 ? "nfty" : s;
    }

    private static string ErrorText(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var e))
            {
                if (e.ValueKind == JsonValueKind.String) return e.GetString() ?? "";
                if (e.ValueKind == JsonValueKind.Object)
                {
                    string reason = e.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString()! : "";
                    string details = e.TryGetProperty("details", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString()! : "";
                    return string.Join(": ", new[] { reason, details }.Where(s => s.Length > 0));
                }
            }
        }
        catch (JsonException) { }
        return Cid.Clip(body);
    }
}
